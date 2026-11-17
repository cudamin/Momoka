using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>YouTube官方预告的一支字幕轨；语言码交给IFrame API选轨，名称只用于菜单。</summary>
internal sealed record TrailerCaptionTrack(string Language, string Name);

/// <summary>Gallery Samples/WebView2/WebView2Page.xaml/.xaml.cs，
/// v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7 原生宿主；背景不接受输入。
/// </summary>
internal sealed class StoreYouTubePreview(CoreWebView2Environment environment) : IDisposable
{
    private const string Origin = "https://momoka";
    private const string CaptionPrefix = "captions:";
    private readonly CoreWebView2Environment _environment = environment;
    private readonly WebView2 _web = new() { IsHitTestVisible = false, IsTabStop = false };
    private readonly List<TrailerCaptionTrack> _captions = [];
    private bool _disposed;
    private bool _active;
    private bool _muted = true;
    private bool _playing;
    private string? _caption;
    private TaskCompletionSource? _playingCompleted;
    internal WebView2 Element => _web;
    internal IReadOnlyList<TrailerCaptionTrack> Captions => _captions;
    internal string? SelectedCaption => _caption;
    internal bool IsPlaying => _playing;

    /// <summary>这次起播时共享配置里有没有 YouTube 登录态；失败时页面据此决定提示登录还是一般失败。</summary>
    internal bool HasSession { get; private set; }
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
        // 共享配置：登录窗写下的 YouTube Cookie 会随 www.youtube.com/embed 一起带上，需要登录的
        // 网络不再因为匿名嵌入报 150。
        await _web.EnsureCoreWebView2Async(_environment);
        token.ThrowIfCancellationRequested();
        if (_disposed) return;
        var core = _web.CoreWebView2;
        try
        {
            var cookies = await core.CookieManager.GetCookiesAsync("https://www.youtube.com");
            HasSession = YouTubeTrailer.HasSession(cookies.Select(cookie => cookie.Name));
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            HasSession = false;
        }
        Log.Info("预告", $"YouTube 预告 id={id} 登录态={HasSession}");
        core.IsMuted = _muted;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
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
                Log.Info("预告", $"YouTube 预告 id={id} 报错 {message}");
                _playingCompleted?.TrySetException(new IOException("预告片播放失败"));
                state(false);
            }
        };
        core.Navigate(Origin + "/index.html");
        // 需要登录的网络上，播放器会停在登录卡片而不触发 onError；20 秒内没有起播就按失败收尾，
        // 让详情页明确提示。代理慢时首帧可能接近十秒，留足余量，别把真的在加载误判成失败。
        var finished = await Task.WhenAny(_playingCompleted.Task, Task.Delay(TimeSpan.FromSeconds(20), token));
        if (finished != _playingCompleted.Task)
        {
            Log.Info("预告", $"YouTube 预告 id={id} 20 秒内未起播（登录态={HasSession}）");
            throw new IOException("预告片播放超时");
        }
        await _playingCompleted.Task;
        token.ThrowIfCancellationRequested();
    }

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
