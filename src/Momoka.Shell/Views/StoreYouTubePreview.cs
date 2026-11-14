using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Momoka.Shell.Views;

/// <summary>Gallery Samples/WebView2/WebView2Page.xaml/.xaml.cs，
/// v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7 原生宿主；背景不接受输入。
/// </summary>
internal sealed class StoreYouTubePreview : IDisposable
{
    private const string Origin = "https://momoka";
    private readonly WebView2 _web = new() { IsHitTestVisible = false, IsTabStop = false };
    private bool _disposed;
    private bool _active;
    private bool _muted = true;
    private TaskCompletionSource? _playing;
    internal WebView2 Element => _web;

    internal async Task StartAsync(string id, string folder, CancellationToken token, Action<bool> state)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(folder);
        _playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // ID由Core白名单解析；所有其它内容固定，不能转发服务端URL、账号或脚本。
        var html = StoreTrailerDocument.Build(id);
        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), html, token);
        token.ThrowIfCancellationRequested();
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync("", Path.Combine(folder, "profile"), new());
        token.ThrowIfCancellationRequested();
        if (_disposed) return;
        await _web.EnsureCoreWebView2Async(environment);
        token.ThrowIfCancellationRequested();
        if (_disposed) return;
        var core = _web.CoreWebView2;
        core.IsMuted = _muted;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping("momoka", folder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.NavigationStarting += (_, args) => { if (args.Uri != Origin + "/index.html") args.Cancel = true; };
        core.NavigationCompleted += (_, args) => { if (!_disposed && !args.IsSuccess) _playing?.TrySetException(new IOException("预告片网页加载失败")); };
        core.WebMessageReceived += (_, args) =>
        {
            if (!_disposed && !token.IsCancellationRequested && args.Source == Origin + "/index.html")
            {
                var message = args.TryGetWebMessageAsString();
                if (message == "ready") { if (!_active) _playing?.TrySetResult(); Send(_muted ? "mute" : "unmute"); Send(_active ? "play" : "pause"); }
                else if (message == "playing") { _playing?.TrySetResult(); state(true); }
                else if (message == "error") { _playing?.TrySetException(new IOException("预告片播放失败")); state(false); }
            }
        };
        core.Navigate(Origin + "/index.html");
        await _playing.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
        token.ThrowIfCancellationRequested();
    }

    internal void Send(string command) { if (!_disposed && _web.CoreWebView2 is { } core) core.PostWebMessageAsString(command); }
    internal void SetActive(bool active) { _active = active; Send(active ? "play" : "pause"); }
    internal void Mute(bool muted) { _muted = muted; if (!_disposed && _web.CoreWebView2 is { } core) core.IsMuted = muted; Send(muted ? "mute" : "unmute"); }
    public void Dispose() { if (_disposed) return; _disposed = true; _playing?.TrySetCanceled(); _web.Close(); }
}
