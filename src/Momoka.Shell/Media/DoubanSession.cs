using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Momoka.Scrobbling;
using Momoka.Shell.Localization;
using Momoka.Shell.Platform;

namespace Momoka.Shell.Media;

/// <summary>UI-owned, task-scoped WebViews share a dedicated Douban profile. Cookies never enter managed code.</summary>
internal sealed class DoubanSession(string profileFolder, IUiDispatcher dispatcher) : IDoubanScrobblingClient, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Panel? _host;
    private CoreWebView2Environment? _environment;
    private bool _disposed;

    internal void Attach(Panel host) => _host = host;

    public async Task<bool> CheckLoginAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        return await OnUiAsync(async token =>
        {
            await _gate.WaitAsync(token);
            try
            {
                if (_host is not { } host) throw Failure();
                var web = new WebView2 { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false, IsTabStop = false };
                host.Children.Add(web);
                try
                {
                    await InitializeAsync(web, token);
                    var core = web.CoreWebView2;
                    Guard(core, false);
                    return await ReadLoginStateAsync(core, token);
                }
                finally { web.Close(); host.Children.Remove(web); }
            }
            finally { _gate.Release(); }
        }, deadline.Token);
    }

    private static async Task<bool> ReadLoginStateAsync(CoreWebView2 core, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Navigated(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess) loaded.TrySetResult();
            else loaded.TrySetException(Failure());
        }
        core.NavigationCompleted += Navigated;
        try
        {
            core.Navigate("https://movie.douban.com/");
            await loaded.Task.WaitAsync(token);
        }
        finally { core.NavigationCompleted -= Navigated; }
        token.ThrowIfCancellationRequested();
        var state = await core.ExecuteScriptAsync(DoubanProtocol.LoginStateScript).AsTask().WaitAsync(token);
        token.ThrowIfCancellationRequested();
        return state switch { "\"connected\"" => true, "\"signed-out\"" => false, _ => throw Failure() };
    }

    public async Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string query, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        return await OnUiAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 60) throw Failure();
            await _gate.WaitAsync(token);
            try
            {
                if (_host is not { } host) throw Failure();
                var web = new WebView2 { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false, IsTabStop = false };
                host.Children.Add(web);
                try
                {
                    await InitializeAsync(web, token);
                    var core = web.CoreWebView2;
                    Guard(core, false);
                    var address = "https://search.douban.com/movie/subject_search?search_text="
                        + Uri.EscapeDataString(query.Trim()) + "&cat=1002";
                    var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Navigated(object? sender, CoreWebView2NavigationCompletedEventArgs args)
                    {
                        if (args.IsSuccess) loaded.TrySetResult();
                        else loaded.TrySetException(Failure());
                    }
                    core.NavigationCompleted += Navigated;
                    try
                    {
                        core.Navigate(address);
                        await loaded.Task.WaitAsync(token);
                    }
                    finally { core.NavigationCompleted -= Navigated; }
                    var nonce = Guid.NewGuid().ToString("N");
                    return await RunSearchAsync(core, query.Trim(), nonce, token);
                }
                finally { web.Close(); host.Children.Remove(web); }
            }
            finally { _gate.Release(); }
        }, deadline.Token);
    }

    public Task<ScrobblingSubject> SubjectAsync(long id, CancellationToken cancellationToken) =>
        OnUiAsync(async token =>
        {
            await _gate.WaitAsync(token);
            try
            {
                return await WithPageAsync(id, async core =>
                {
                    var result = await RunAsync(core, id, nonce => DoubanProtocol.PrepareScript(id, nonce), token);
                    if (result.State != "metadata" || string.IsNullOrWhiteSpace(result.Title)) throw Failure();
                    return new ScrobblingSubject(id, result.Title, result.Count is > 0 ? result.Count : null);
                }, token);
            }
            finally { _gate.Release(); }
        }, cancellationToken);

    public Task MarkAsync(long id, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken) =>
        OnUiAsync(async token =>
        {
            await _gate.WaitAsync(token);
            try
            {
                return await WithPageAsync(id, core => MarkOnPageAsync(core, id, completed, stillAllowed, token), token);
            }
            finally { _gate.Release(); }
        }, cancellationToken);

    public Task<bool> MarkEpisodeAsync(long id, int episodeNumber, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken) =>
        OnUiAsync(async token =>
        {
            if (episodeNumber < 1) throw Failure();
            await _gate.WaitAsync(token);
            try
            {
                return await WithPageAsync(id, async core =>
                {
                    EnsureAllowed(stillAllowed, token);
                    if (completed)
                    {
                        var metadata = await RunAsync(core, id, nonce => DoubanProtocol.PrepareScript(id, nonce), token);
                        if (metadata.State != "metadata" || string.IsNullOrWhiteSpace(metadata.Title)) throw Failure();
                        if (metadata.Count != episodeNumber) return false;
                    }
                    return await MarkOnPageAsync(core, id, completed, stillAllowed, token);
                }, token);
            }
            finally { _gate.Release(); }
        }, cancellationToken);

    private static async Task<bool> MarkOnPageAsync(CoreWebView2 core, long id, bool completed, Func<bool> stillAllowed,
        CancellationToken token)
    {
        EnsureAllowed(stillAllowed, token);
        var nonce = Guid.NewGuid().ToString("N");
        var prepared = await RunAsync(core, id, _ => DoubanProtocol.PrepareMarkScript(id, completed, nonce), token, nonce);
        if (prepared.State == "watched") return true;
        if (prepared.State == "watching") return false;
        await EnsurePreparedAsync(core, prepared.State, token);
        EnsureAllowed(stillAllowed, token);
        var result = await RunAsync(core, id, _ => DoubanProtocol.CommitScript(nonce), token, nonce);
        if (result.State != "done") throw Failure();
        return completed;
    }

    private static async Task EnsurePreparedAsync(CoreWebView2 core, string state, CancellationToken token)
    {
        if (state == "ready") return;
        // The preparation script also uses "login" for an HTTP failure or missing form data.
        // Read the trusted homepage before treating the failure as confirmed sign-out.
        if (state == "login" && !await ReadLoginStateAsync(core, token)) throw new SignedOutException();
        throw Failure();
    }

    private async Task<T> WithPageAsync<T>(long id, Func<CoreWebView2, Task<T>> action, CancellationToken token)
    {
        if (id <= 0 || _host is null) throw Failure();
        var host = _host;
        var web = new WebView2 { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false, IsTabStop = false };
        host.Children.Add(web);
        try
        {
            await InitializeAsync(web, token);
            var core = web.CoreWebView2;
            Guard(core, false);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Navigated(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (args.IsSuccess) loaded.TrySetResult();
                else loaded.TrySetException(Failure());
            }
            core.NavigationCompleted += Navigated;
            try
            {
                core.Navigate($"https://movie.douban.com/subject/{id}/");
                await loaded.Task.WaitAsync(token);
            }
            finally { core.NavigationCompleted -= Navigated; }
            token.ThrowIfCancellationRequested();
            return await action(core);
        }
        finally { web.Close(); host.Children.Remove(web); }
    }

    internal Task LoginAsync(FrameworkElement owner, CancellationToken cancellationToken) => OnUiAsync(async token =>
    {
        await _gate.WaitAsync(token);
        var web = new WebView2 { Width = 700, Height = 440 };
        ContentDialog? dialog = null;
        try
        {
            if (owner.XamlRoot is not { } root) throw Failure();
            web.Width = Math.Max(280, Math.Min(700, root.Size.Width - 100));
            web.Height = Math.Max(220, Math.Min(440, root.Size.Height - 200));
            dialog = new ContentDialog
            {
                XamlRoot = root,
                RequestedTheme = owner.ActualTheme,
                Title = UiText.Translate("登录豆瓣"),
                Content = web,
                CloseButtonText = UiText.Translate("完成"),
                DefaultButton = ContentDialogButton.Close
            };
            dialog.Resources["ContentDialogMaxWidth"] = 820d;
            await InitializeAsync(web, token);
            Guard(web.CoreWebView2, true);
            web.CoreWebView2.Navigate("https://accounts.douban.com/passport/login");
            await CancelableDialog.ShowAsync(async () => await dialog.ShowAsync(), () => dialog.Hide(), dispatcher.Run, token);
            return true;
        }
        finally { web.Close(); _gate.Release(); }
    }, cancellationToken);

    internal Task LogoutAsync(CancellationToken cancellationToken) => OnUiAsync(async token =>
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_host is not { } host) throw Failure();
            var web = new WebView2 { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false, IsTabStop = false };
            host.Children.Add(web);
            try
            {
                await InitializeAsync(web, token);
                await web.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
                return true;
            }
            finally { web.Close(); host.Children.Remove(web); }
        }
        finally { _gate.Release(); }
    }, cancellationToken);

    private async Task InitializeAsync(WebView2 web, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_environment is null)
        {
            Directory.CreateDirectory(profileFolder);
            _environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profileFolder, null).AsTask().WaitAsync(token);
        }
        await web.EnsureCoreWebView2Async(_environment).AsTask().WaitAsync(token);
        token.ThrowIfCancellationRequested();
        web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        web.CoreWebView2.Settings.IsStatusBarEnabled = false;
    }

    private static void Guard(CoreWebView2 core, bool login)
    {
        core.NavigationStarting += (_, args) => args.Cancel = !DoubanProtocol.IsTrustedNavigation(args.Uri, login);
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (login && DoubanProtocol.IsTrustedNavigation(args.Uri, true)) core.Navigate(args.Uri);
        };
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
    }

    private sealed record Reply(string State, string? Title, int? Count);

    private static async Task<IReadOnlyList<ScrobblingCandidate>> RunSearchAsync(CoreWebView2 core, string query,
        string nonce, CancellationToken token)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ScrobblingCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = "https://search.douban.com/movie/subject_search?search_text=" + Uri.EscapeDataString(query) + "&cat=1002";
        void Received(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!string.Equals(args.Source, expected, StringComparison.Ordinal)) return;
            try
            {
                var message = args.TryGetWebMessageAsString();
                if (message.Length > 32768) throw Failure();
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                if (root.GetProperty("nonce").GetString() != nonce || root.GetProperty("state").GetString() != "results")
                    throw Failure();
                if (!root.TryGetProperty("candidates", out var values) || values.ValueKind != JsonValueKind.Array
                    || values.GetArrayLength() > 20) throw Failure();
                var candidates = new List<ScrobblingCandidate>();
                foreach (var value in values.EnumerateArray())
                {
                    if (!value.TryGetProperty("id", out var idValue) || !idValue.TryGetInt64(out var id) || id <= 0
                        || !value.TryGetProperty("title", out var titleValue) || titleValue.ValueKind != JsonValueKind.String)
                        continue;
                    var title = titleValue.GetString()?.Trim() ?? "";
                    if (title.Length is 0 or > 240) continue;
                    int? year = value.TryGetProperty("year", out var yearValue) && yearValue.ValueKind == JsonValueKind.Number
                        && yearValue.TryGetInt32(out var yearNumber) && yearNumber is >= 1900 and <= 2200 ? yearNumber : null;
                    var detail = value.TryGetProperty("detail", out var detailValue) && detailValue.ValueKind == JsonValueKind.String
                        ? detailValue.GetString()?.Trim() : null;
                    if (detail?.Length > 240) detail = detail[..240];
                    if (!candidates.Any(candidate => candidate.Id == id)) candidates.Add(new(id, title, year, null, detail));
                }
                completion.TrySetResult(candidates);
            }
            catch (Exception) { completion.TrySetException(Failure()); }
        }
        core.WebMessageReceived += Received;
        try
        {
            token.ThrowIfCancellationRequested();
            await core.ExecuteScriptAsync(DoubanProtocol.SearchScript(query, nonce));
            return await completion.Task.WaitAsync(token);
        }
        finally { core.WebMessageReceived -= Received; }
    }

    private static async Task<Reply> RunAsync(CoreWebView2 core, long id, Func<string, string> script,
        CancellationToken token, string? operationNonce = null)
    {
        var nonce = operationNonce ?? Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = $"https://movie.douban.com/subject/{id}/";
        void Received(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!string.Equals(args.Source, expected, StringComparison.Ordinal)) return;
            try
            {
                var message = args.TryGetWebMessageAsString();
                if (message.Length > 4096) return;
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                if (root.GetProperty("nonce").GetString() != nonce) return;
                var state = root.GetProperty("state").GetString() ?? "";
                var title = root.TryGetProperty("title", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
                int? count = root.TryGetProperty("count", out var total) && total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var value) ? value : null;
                completion.TrySetResult(new(state, title, count));
            }
            catch (Exception) { completion.TrySetException(Failure()); }
        }
        core.WebMessageReceived += Received;
        try
        {
            token.ThrowIfCancellationRequested();
            await core.ExecuteScriptAsync(script(nonce));
            return await completion.Task.WaitAsync(token);
        }
        finally { core.WebMessageReceived -= Received; }
    }

    private Task<T> OnUiAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken token)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.Post(() => _ = ExecuteAsync())) result.TrySetException(Failure());
        return result.Task;
        async Task ExecuteAsync()
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                result.TrySetResult(await work(linked.Token));
            }
            catch (OperationCanceledException) { result.TrySetCanceled(); }
            catch (SignedOutException error) { result.TrySetException(error); }
            catch (Exception) { result.TrySetException(Failure()); }
        }
    }

    private sealed class SignedOutException() : HttpRequestException("豆瓣登录已失效，请重新登录", null, HttpStatusCode.Unauthorized);

    private static InvalidOperationException Failure() => new("豆瓣会话操作未完成，请重新登录或检查网络");
    private static void EnsureAllowed(Func<bool> allowed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!allowed()) throw new OperationCanceledException(token);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); }
}
