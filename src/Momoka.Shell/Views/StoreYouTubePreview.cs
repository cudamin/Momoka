using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>YouTube官方预告的一支字幕轨；语言码交给IFrame API选轨，名称只用于菜单。</summary>
internal sealed record TrailerCaptionTrack(string Language, string Name);

/// <summary>Gallery Samples/WebView2/WebView2Page.xaml/.xaml.cs，
/// v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7 原生宿主；背景不接受输入。
/// </summary>
internal sealed class StoreYouTubePreview(Func<CancellationToken, Task<CoreWebView2Environment>> environment) : IDisposable
{
    private const string Origin = "https://momoka";
    private const string CaptionPrefix = "captions:";
    private const string ErrorPrefix = "error:";
    private readonly WebView2 _web = new() { IsHitTestVisible = false, IsTabStop = false };
    private readonly List<TrailerCaptionTrack> _captions = [];
    private bool _disposed;
    private bool _active;
    private bool _muted = true;
    private bool _playing;
    private bool _signedIn;
    private string? _caption;
    private TaskCompletionSource? _playingCompleted;
    internal WebView2 Element => _web;
    internal IReadOnlyList<TrailerCaptionTrack> Captions => _captions;
    internal string? SelectedCaption => _caption;
    internal bool IsPlaying => _playing;
    /// <summary>这一次失败是不是 YouTube 要求登录；页面据此决定提示去设置登录还是安静地回退静态图。</summary>
    internal bool NeedsLogin { get; private set; }
    internal event Action? PlaybackChanged;
    internal event Action? CaptionsChanged;

    internal async Task StartAsync(string id, string folder, CancellationToken token, Action<bool> state)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(folder);
        _playingCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // ID由Core白名单解析；所有其它内容固定，不能转发服务端URL、账号或脚本。
        var html = StoreTrailerDocument.Build(id);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), html, token);
        token.ThrowIfCancellationRequested();
        // 背景预告的播放/暂停由原生按钮驱动，没有网页内的用户手势。默认策略会把「取消静音」当成
        // 无手势的带声音播放并把视频暂停，表现为点一下静音键画面就停住；这条只作用于这支隔离的
        // 预告网页，且页面自身仍以 autoplay=0、controls=0 固定加载。
        // 配置与设置页的 YouTube 登录共用，登录后的 Cookie 才会带进这支预告。
        var web = await environment(token);
        token.ThrowIfCancellationRequested();
        if (_disposed) return;
        await _web.EnsureCoreWebView2Async(web);
        token.ThrowIfCancellationRequested();
        if (_disposed) return;
        var core = _web.CoreWebView2;
        core.IsMuted = _muted;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        // 先读登录状态再起播：YouTube 的登录卡片用 150 报错，页面要靠这一位区分「去登录」和「视频不让嵌」。
        _signedIn = await HasSessionAsync(core);
        core.SetVirtualHostNameToFolderMapping("momoka", folder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.NavigationStarting += (_, args) => { if (args.Uri != Origin + "/index.html") args.Cancel = true; };
        core.NavigationCompleted += (_, args) => { if (!_disposed && !args.IsSuccess) _playingCompleted?.TrySetException(new IOException("预告片网页加载失败")); };
        core.WebMessageReceived += (_, args) =>
        {
            if (_disposed || token.IsCancellationRequested || args.Source != Origin + "/index.html") return;
            var message = args.TryGetWebMessageAsString();
            if (message == "ready") { if (!_active) _playingCompleted?.TrySetResult(); Send(_muted ? "mute" : "unmute"); Send(_active ? "play" : "pause"); }
            else if (message == "playing") { _playingCompleted?.TrySetResult(); SetPlayback(true); state(true); }
            else if (message == "paused" || message == "blocked") SetPlayback(false);
            else if (message.StartsWith(CaptionPrefix, StringComparison.Ordinal)) ReadCaptions(message[CaptionPrefix.Length..]);
            else if (message.StartsWith("error", StringComparison.Ordinal))
            {
                NeedsLogin = ErrorNeedsSignIn(message) && !_signedIn;
                _playingCompleted?.TrySetException(new IOException("预告片播放失败"));
                state(false);
            }
        };
        core.Navigate(Origin + "/index.html");
        // 登录卡片有时不触发 onError，一直停在未起播；超时前读一次播放器自己的 errorCode，别把
        // 「要登录」拖成一次无声的 20 秒等待。
        var finished = await Task.WhenAny(_playingCompleted.Task, Task.Delay(TimeSpan.FromSeconds(15), token));
        if (finished != _playingCompleted.Task)
        {
            NeedsLogin = !_signedIn && await HasAuthErrorAsync(core);
            throw new IOException("预告片播放超时");
        }
        await _playingCompleted.Task;
        token.ThrowIfCancellationRequested();
    }

    private static async Task<bool> HasAuthErrorAsync(CoreWebView2 core)
    {
        try
        {
            var data = await core.ExecuteScriptAsync("typeof p!=='undefined'?JSON.stringify(p.getVideoData()):''");
            return data.Contains("\"errorCode\":\"auth\"", StringComparison.Ordinal);
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { return false; }
    }

    private static async Task<bool> HasSessionAsync(CoreWebView2 core)
    {
        var cookies = await core.CookieManager.GetCookiesAsync("https://www.youtube.com");
        return YouTubeTrailer.HasSession(cookies.Select(cookie => cookie.Name));
    }

    /// <summary>只认带错误码的 101／150；<c>error</c> 这种加载失败不能当成「去登录」。</summary>
    private static bool ErrorNeedsSignIn(string message) =>
        message.StartsWith(ErrorPrefix, StringComparison.Ordinal)
        && int.TryParse(message[ErrorPrefix.Length..], out var code)
        && YouTubeTrailer.NeedsSignIn(code);

    private void SetPlayback(bool playing)
    {
        if (_playing == playing) return;
        _playing = playing;
        PlaybackChanged?.Invoke();
    }

    private void ReadCaptions(string json)
    {
        if (json.Length > 16384) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var tracks = new List<TrailerCaptionTrack>();
            if (root.TryGetProperty("tracks", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var language = Text(item, "language");
                    if (language.Length == 0 || tracks.Any(track => track.Language == language)) continue;
                    var name = Text(item, "name");
                    tracks.Add(new TrailerCaptionTrack(language, name.Length == 0 ? language : name));
                    if (tracks.Count >= 128) break;
                }
            }
            _captions.Clear();
            _captions.AddRange(tracks);
            _caption = root.TryGetProperty("language", out var selected) && selected.ValueKind == JsonValueKind.String
                && selected.GetString() is { Length: > 0 } selectedLanguage ? selectedLanguage : null;
            CaptionsChanged?.Invoke();
        }
        catch (JsonException) { }
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    internal void SelectCaption(string? language) { _caption = language; Send("caption:" + (language ?? "")); }
    internal void Send(string command) { if (!_disposed && _web.CoreWebView2 is { } core) core.PostWebMessageAsString(command); }
    internal void SetActive(bool active) { _active = active; Send(active ? "play" : "pause"); }
    internal void Mute(bool muted) { _muted = muted; if (!_disposed && _web.CoreWebView2 is { } core) core.IsMuted = muted; Send(muted ? "mute" : "unmute"); }
    public void Dispose() { if (_disposed) return; _disposed = true; _playingCompleted?.TrySetCanceled(); _web.Close(); }
}
