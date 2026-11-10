using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Momoka.Danmaku;
using Momoka.Infrastructure;

namespace Momoka.Shell.Views;

/// <summary>官方网页登录只使用本应用的隐私 profile，候选 Cookie 经 Core 验证后交视图模型保存。</summary>
internal static class BilibiliLoginDialog
{
    /// <summary>核对飞行期间网页登录可变；只接受当前窗口仍拥有的同一候选。</summary>
    private static async Task<BilibiliAccount?> ValidateCandidateAsync(
        string candidate,
        Func<string, CancellationToken, Task<BilibiliAccount>> validate,
        Func<Task<string>> readCookie,
        Func<bool> live,
        CancellationToken token)
    {
        var account = await validate(candidate, token);
        if (!live() || token.IsCancellationRequested) return null;
        var current = await readCookie();
        return live() && !token.IsCancellationRequested && current == candidate ? account : null;
    }

    internal static async Task<(string CookieHeader, long UserId, string Name)?> ShowAsync(
        FrameworkElement owner, AppPaths paths, BilibiliClient client, CancellationToken token)
    {
        if (owner.XamlRoot is not { } root || token.IsCancellationRequested) return null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var session = new LoginSession(owner, root, paths, client, lifetime);
        return await session.ShowAsync();
    }

    private sealed class LoginSession
    {
        private readonly FrameworkElement _owner;
        private readonly XamlRoot _root;
        private readonly AppPaths _paths;
        private readonly BilibiliClient _client;
        private readonly CancellationTokenSource _lifetime;
        private readonly CancellationToken _token;
        private readonly WebView2 _web;
        private readonly ContentDialog _dialog;
        private readonly TextBlock _status;
        private CoreWebView2? _core;
        private bool _closed;
        private bool _accepted;
        private bool _checking;
        private Task? _cleanup;
        private string _lastCandidate = "";
        private long _lastCheck;
        private (string CookieHeader, long UserId, string Name)? _receipt;

        internal LoginSession(FrameworkElement owner, XamlRoot root, AppPaths paths, BilibiliClient client,
            CancellationTokenSource lifetime)
        {
            _owner = owner;
            _root = root;
            _paths = paths;
            _client = client;
            _lifetime = lifetime;
            _token = lifetime.Token;
            _web = new WebView2
            {
                Width = Math.Min(900, Math.Max(280, root.Size.Width - 96)),
                Height = Math.Min(620, Math.Max(260, root.Size.Height - 240))
            };
            AutomationProperties.SetAutomationId(_web, "BilibiliLoginWeb");
            AutomationProperties.SetName(_web, "B 站官方登录页");
            _status = new TextBlock
            {
                Text = "正在打开 B 站官方登录页…",
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["EgCaptionStyle"]
            };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(_status);
            content.Children.Add(_web);
            _dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = "登录 B 站",
                Content = content,
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light,
                Style = (Style)Application.Current.Resources["EgDialogSurfaceStyle"]
            };
            _dialog.Resources["ContentDialogMaxWidth"] = _web.Width + 64;
            _dialog.Resources["ContentDialogMaxHeight"] = root.Size.Height - 24;
            AutomationProperties.SetAutomationId(_dialog, "BilibiliLoginDialog");
            _dialog.Opened += OnOpened;
            _dialog.Closing += OnClosing;
            _root.Changed += OnRootChanged;
            Resize();
        }

        private bool Live => !_closed && !_token.IsCancellationRequested && ReferenceEquals(_owner.XamlRoot, _dialog.XamlRoot);

        internal async Task<(string CookieHeader, long UserId, string Name)?> ShowAsync()
        {
            using var cancellation = _token.Register(() => _owner.DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed) return;
                _closed = true;
                try { _dialog.Hide(); }
                catch (Exception) { /* 所属窗口已经释放。 */ }
            }));
            try { if (Live) await _dialog.ShowAsync(); }
            catch (Exception)
            {
                if (!_token.IsCancellationRequested)
                    throw new InvalidOperationException("B 站登录窗口未能打开，请关闭其他弹窗后重试。");
            }
            finally
            {
                _closed = true;
                CancelLifetime();
                await CleanupAsync();
                _dialog.Opened -= OnOpened;
                _dialog.Closing -= OnClosing;
                _root.Changed -= OnRootChanged;
            }
            return _accepted ? _receipt : null;
        }

        private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) => _ = StartAsync();

        private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();

        private void Resize()
        {
            _web.Width = Math.Min(900, Math.Max(120, _root.Size.Width - 96));
            _web.Height = Math.Min(620, Math.Max(100, _root.Size.Height - 240));
            _dialog.Resources["ContentDialogMaxWidth"] = Math.Max(120, _root.Size.Width - 24);
            _dialog.Resources["ContentDialogMaxHeight"] = Math.Max(120, _root.Size.Height - 24);
        }

        private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            _closed = true;
            CancelLifetime();
            if (!_accepted) _receipt = null;
        }

        private void CancelLifetime()
        {
            try { _lifetime.Cancel(); }
            catch (ObjectDisposedException) { /* 晚于 Show 收尾的框架关闭事件。 */ }
        }

        private async Task StartAsync()
        {
            try
            {
                // 独立于服务器控制台 profile；InPrivate 不把候选登录状态持久写入此目录。
                var profile = Path.Combine(_paths.Root, "webview", "bilibili-login");
                Directory.CreateDirectory(profile);
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync("", profile,
                    new CoreWebView2EnvironmentOptions { AllowSingleSignOnUsingOSPrimaryAccount = false });
                if (!Live) return;
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.IsInPrivateModeEnabled = true;
                // 关闭后立即重开时，旧初始化/清理不能清掉新窗口的 Cookie。
                options.ProfileName = "BilibiliLogin" + Guid.NewGuid().ToString("N");
                await _web.EnsureCoreWebView2Async(environment, options);
                if (_web.CoreWebView2 is not { } core) return;
                _core = core;
                _cleanup = null;
                if (!Live) return;
                core.CookieManager.DeleteAllCookies();
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
                if (!Live) return;
                core.Profile.IsPasswordAutosaveEnabled = false;
                core.Profile.IsGeneralAutofillEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsWebMessageEnabled = false;
                core.NavigationStarting += OnNavigationStarting;
                core.NavigationCompleted += OnNavigationCompleted;
                core.NewWindowRequested += OnNewWindowRequested;
                core.DownloadStarting += OnDownloadStarting;
                core.Navigate(BilibiliSession.LoginUrl);
                _status.Text = "请在官方网页完成登录，Momoka 会自动核对账号。关闭窗口即取消。";
                _ = PollAsync();
            }
            catch (Exception)
            {
                if (Live) _status.Text = "无法打开 B 站登录页。请确认已安装 WebView2 Runtime 后重试。";
            }
            finally
            {
                // 初始化 API 本身不接受取消；晚于关闭创建出来的 WebView 也必须再收尾。
                if (_closed || _token.IsCancellationRequested) await CleanupAsync();
            }
        }

        private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            // 只检查顶层跳转；官网验证码的 iframe 和资源仍由浏览器加载。
            if (BilibiliSession.IsOfficialNavigation(args.Uri)) return;
            args.Cancel = true;
            if (Live) _status.Text = "已阻止离开 B 站官方登录页面。请在当前页面继续登录。";
        }

        private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (Live && !args.IsSuccess) _status.Text = "B 站登录页未能载入，请检查网络后重新登录。";
        }

        private static void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args) => args.Handled = true;

        private static void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
        {
            args.Cancel = true;
            args.Handled = true;
        }

        private async Task PollAsync()
        {
            try
            {
                while (Live)
                {
                    await CheckSessionAsync();
                    await Task.Delay(1200, _token);
                }
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
            catch (Exception) { /* 控件随窗口释放时结束轮询，不回显浏览器异常。 */ }
        }

        private async Task CheckSessionAsync()
        {
            if (!Live || _checking || _core is not { } core) return;
            _checking = true;
            try
            {
                var cookies = await core.CookieManager.GetCookiesAsync("https://api.bilibili.com");
                if (!Live) return;
                var candidate = BilibiliSession.CookieHeader(cookies.Select(cookie => new KeyValuePair<string, string>(cookie.Name, cookie.Value)));
                if (candidate.Length == 0 || candidate == _lastCandidate && Environment.TickCount64 - _lastCheck < 10000) return;
                _lastCandidate = candidate;
                _lastCheck = Environment.TickCount64;
                _status.Text = "正在核对 B 站账号…";
                var account = await ValidateCandidateAsync(candidate, _client.ValidateSessionAsync, async () =>
                {
                    var currentCookies = await core.CookieManager.GetCookiesAsync("https://api.bilibili.com");
                    return BilibiliSession.CookieHeader(currentCookies.Select(cookie => new KeyValuePair<string, string>(cookie.Name, cookie.Value)));
                }, () => Live, _token);
                if (!Live || account is null) return;
                _receipt = (candidate, account.UserId, account.Name);
                _accepted = true;
                _dialog.Hide();
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
            catch (Exception)
            {
                if (Live) _status.Text = "尚未取得有效的 B 站登录状态，请在官方网页继续登录。";
            }
            finally { _checking = false; }
        }

        private Task CleanupAsync() => _cleanup ??= ClearAndCloseAsync();

        private async Task ClearAndCloseAsync()
        {
            if (_core is { } core)
            {
                try
                {
                    core.NavigationStarting -= OnNavigationStarting;
                    core.NavigationCompleted -= OnNavigationCompleted;
                    core.NewWindowRequested -= OnNewWindowRequested;
                    core.DownloadStarting -= OnDownloadStarting;
                    core.CookieManager.DeleteAllCookies();
                    await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile).AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (Exception) { /* 隐私 profile 不持久化凭据；清理失败也必须继续关闭控件。 */ }
            }
            try { _web.Close(); }
            catch (Exception) { /* 控件已随窗口释放。 */ }
        }
    }
}
