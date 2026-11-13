using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Momoka.Shell.Views;

/// <summary>Gallery WebView2Page (v2.9.3) 原生宿主，使用YouTube官方IFrame API，仅接受已验证的视频ID。</summary>
internal sealed class StoreYouTubePreview : IDisposable
{
    private const string Origin = "https://momoka";
    private readonly WebView2 _web = new();
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
        var html = $$$$"""
            <!doctype html><html><head><meta name="referrer" content="strict-origin-when-cross-origin">
            <style>html,body,iframe{margin:0;width:100%;height:100%;border:0;overflow:hidden;background:transparent}</style></head>
            <body><iframe id="video" src="https://www.youtube-nocookie.com/embed/{{{{id}}}}?enablejsapi=1&amp;origin=https%3A%2F%2Fmomoka&amp;playsinline=1&amp;autoplay=0"
            allow="autoplay; encrypted-media" referrerpolicy="strict-origin-when-cross-origin"></iframe>
            <script>let p,active=false,muted=true;function onYouTubeIframeAPIReady(){p=new YT.Player('video',{events:{onReady:()=>{if(muted)p.mute();else p.unMute();chrome.webview.postMessage('ready');if(active)p.playVideo()},
            onStateChange:e=>{if(e.data===1)chrome.webview.postMessage('playing')},onError:()=>chrome.webview.postMessage('error'),
            onAutoplayBlocked:()=>chrome.webview.postMessage('error')}})};
            chrome.webview.addEventListener('message',e=>{if(e.data==='play')active=true;if(e.data==='pause')active=false;if(e.data==='mute')muted=true;if(e.data==='unmute')muted=false;if(!p)return;switch(e.data){case'play':p.playVideo();break;case'pause':p.pauseVideo();break;
            case'mute':p.mute();break;case'unmute':p.unMute();break}});</script><script src="https://www.youtube.com/iframe_api" onerror="chrome.webview.postMessage('error')"></script></body></html>
            """;
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
                if (message == "ready") { _playing?.TrySetResult(); Send(_muted ? "mute" : "unmute"); Send(_active ? "play" : "pause"); }
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
