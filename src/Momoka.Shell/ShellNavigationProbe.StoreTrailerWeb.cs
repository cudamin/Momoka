using System.Text;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Momoka.Shell.Interop;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task StoreTrailerWebAsync(DetailPage page)
    {
        Call(page, "StopStoreTrailer");
        Field(page, "_storeTrailerSubject", page.ViewModel.CurrentItem);
        Call(page, "LayoutStoreTrailer");
        using var preview = new StoreYouTubePreview();
        Field(page, "_storeYouTube", preview);
        var host = Get<Grid>(page, "StoreTrailerHost");
        host.Visibility = Visibility.Visible;
        host.Opacity = 0;
        Get<Grid>(page, "StoreTrailerContent").Children.Add(preview.Element);
        var folder = Path.Combine(_options.Paths.LogDirectory, "trailer-web-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ProbeAssets", "store-trailer.mp4"), Path.Combine(folder, "store-trailer.mp4"));
        var blocked = 0;
        var apiRequests = 0;
        var embedRequests = 0;
        var webMoves = 0;
        string? webState = null;
        var instrumentationReady = false;
        preview.Element.CoreWebView2Initialized += (_, _) =>
        {
            var core = preview.Element.CoreWebView2;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) =>
            {
                var uri = new Uri(args.Request.Uri);
                if (uri.Host == "momoka") return;
                var body = uri.Host == "www.youtube.com" && uri.AbsolutePath == "/iframe_api" ? Api
                    : uri.Host == "www.youtube-nocookie.com" && uri.AbsolutePath == "/embed/Abc_123-xyz" ? Embed : "";
                if (body == Api) apiRequests++;
                if (body == Embed) embedRequests++;
                if (body.Length == 0) blocked++;
                args.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)).AsRandomAccessStream(),
                    body.Length > 0 ? 200 : 403, "Fixture", uri.AbsolutePath == "/iframe_api"
                        ? "Content-Type: application/javascript" : "Content-Type: text/html");
            };
            core.WebMessageReceived += (_, args) =>
            {
                var message = args.TryGetWebMessageAsString();
                if (message == "probe-pointer") webMoves++;
                if (message == "probe-ready") instrumentationReady = true;
                if (message.StartsWith("state:", StringComparison.Ordinal)) webState = message[6..];
            };
        };
        preview.SetActive(true);
        await preview.StartAsync("Abc_123-xyz", folder, CancellationToken.None, playing =>
        {
            Require(playing, "匿名网页预告播放失败");
            Field(page, "_storeTrailerOpened", true);
            host.Opacity = 1;
            Get<CommandBar>(page, "StoreTrailerControls").Visibility = Visibility.Visible;
            Call(page, "UpdateStoreTrailerControls");
        });
        await UntilAsync(() => Get<bool>(page, "_storeTrailerOpened"));
        await UntilAsync(() => instrumentationReady);
        Require(apiRequests == 1 && embedRequests == 1, "网页夹具没有截获实际API和iframe请求");
        await preview.Element.CoreWebView2.ExecuteScriptAsync("document.dispatchEvent(new PointerEvent('pointermove'))");
        await UntilAsync(() => webMoves > 0);
        webMoves = 0;
        await LayoutAsync(page);
        await Task.Delay(800);
        Require(!preview.Element.IsHitTestVisible && !preview.Element.IsTabStop, "网页背景仍接收输入");
        await SaveProbeScreenAsync("store-trailer-web-screen.png");
        var pointerEvents = 0;
        var point = host.TransformToVisual(_root).TransformPoint(new Point(host.ActualWidth * 0.78, host.ActualHeight * 0.4));
        PointerEventHandler moved = (_, args) =>
        {
            var position = args.GetCurrentPoint(_root).Position;
            if (Math.Abs(position.X - point.X) <= 30 && Math.Abs(position.Y - point.Y) <= 3) pointerEvents++;
        };
        _root.AddHandler(UIElement.PointerMovedEvent, moved, true);
        Require(Native.GetCursorPos(out var original), "预告悬停无法保存鼠标位置");
        try
        {
            _window!.Activate();
            // 激活是异步的：先等前台真的落到探针窗口，别在第一下 Guard 上把「还没切过来」当成外国窗口。
            await UntilAsync(() => Native.GetForegroundWindow() == _window.Handle);
            var origin = new NativePoint();
            Require(Native.ClientToScreen(_window.Handle, ref origin), "预告悬停无法读取屏幕原点");
            var scale = _root.XamlRoot.RasterizationScale;
            var target = new NativePoint { X = origin.X + (int)(point.X * scale), Y = origin.Y + (int)(point.Y * scale) };
            Guard(new NativePoint { X = target.X - 24, Y = target.Y });
            await MoveAsync(new NativePoint { X = target.X - 24, Y = target.Y });
            Guard(target);
            await MoveAsync(target);
            Require(Native.GetCursorPos(out var actual) && Math.Abs(actual.X - target.X) <= 1 && Math.Abs(actual.Y - target.Y) <= 1,
                "预告悬停鼠标未到位");
            await Task.Delay(800);
            Guard(target);
            Require(pointerEvents > 0 && webMoves == 0, "悬停未到达XAML或泄露给网页播放控件");
            await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "store-trailer-hover.txt"),
                $"target={target.X},{target.Y}; actual={actual.X},{actual.Y}; xamlMoves={pointerEvents}; webMoves={webMoves}; blockedUnknownRequests={blocked}\n");
            await SaveProbeScreenAsync("store-trailer-hover-screen.png");

            void Guard(NativePoint at)
            {
                var under = Native.WindowFromPoint(at);
                Native.GetWindowThreadProcessId(under, out var pid);
                Require(Native.GetForegroundWindow() == _window.Handle && Native.GetAncestor(under, Native.GaRoot) == _window.Handle
                    && pid == (uint)Environment.ProcessId, "悬停目标不是本次前台探针，未继续注入输入");
            }

            // 沿用CursorVisibilityProbe：SendInput成功不代表到位，每次实际读回，后备搬运仍守卫归属。
            async Task MoveAsync(NativePoint at)
            {
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    Guard(at);
                    Native.MovePointerTo(at.X, at.Y);
                    await Task.Delay(160);
                    Guard(at);
                    if (Native.GetCursorPos(out var position) && Math.Abs(position.X - at.X) <= 1 && Math.Abs(position.Y - at.Y) <= 1) return;
                    Guard(at);
                    Require(Native.SetCursorPos(at.X, at.Y), "预告悬停后备指针移动失败");
                    Native.NudgeCursorState();
                    await Task.Delay(160);
                    Guard(at);
                    if (Native.GetCursorPos(out position) && Math.Abs(position.X - at.X) <= 1 && Math.Abs(position.Y - at.Y) <= 1) return;
                }
                throw new InvalidOperationException("预告悬停指针未到位，未开始断言");
            }
        }
        finally { _root.RemoveHandler(UIElement.PointerMovedEvent, moved); Native.SetCursorPos(original.X, original.Y); }
        Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
        Require(Get<bool>(page, "_storeTrailerUserPaused"), "网页预告暂停按钮没有切换");
        await RequireWebStateAsync(paused: true, muted: true);
        Call(page, "OnStoreTrailerMute", Get<AppBarButton>(page, "StoreTrailerMute"), new RoutedEventArgs());
        await RequireWebStateAsync(paused: true, muted: false);
        Call(page, "OnStoreTrailerMute", Get<AppBarButton>(page, "StoreTrailerMute"), new RoutedEventArgs());
        await SaveProbeScreenAsync("store-trailer-paused-screen.png");
        page.SetStorePreviewActive(false);
        await ResizeResponsiveAsync(page, 760, 900);
        Require(host.Visibility == Visibility.Collapsed && Get<CommandBar>(page, "StoreTrailerControls").Visibility == Visibility.Collapsed,
            "窄窗没有收起背景预告及控件");
        var acrylic = Get<Grid>(page, "StoreBackdropAcrylic");
        Require(acrylic.Visibility == Visibility.Visible
            && Get<Border>(page, "StoreBackdropAcrylicFill").Background is Microsoft.UI.Xaml.Media.AcrylicBrush
            && page.ViewModel.StillVisibility == Visibility.Visible && Get<Border>(page, "PosterStill").ActualWidth > 0,
            "窄商店没有亚克力背景或隐藏了封面");
        await Task.Delay(600);
        await SaveProbeScreenAsync("store-trailer-narrow-screen.png");
        acrylic.Visibility = Visibility.Collapsed;
        await Task.Delay(300);
        await SaveProbeScreenAsync("store-backdrop-narrow-clear-screen.png");
        Call(page, "PaintStoreBackdrop");
        var acrylicFill = (Microsoft.UI.Xaml.Media.AcrylicBrush)Get<Border>(page, "StoreBackdropAcrylicFill").Background;
        acrylicFill.AlwaysUseFallback = true;
        await Task.Delay(300);
        await SaveProbeScreenAsync("store-backdrop-narrow-fallback-screen.png");
        acrylicFill.AlwaysUseFallback = false;
        foreach (var width in new[] { 1099, 1100, 1101, 760, 360, 1280 })
        {
            await ResizeResponsiveAsync(page, width, 900);
            Require(page.ViewModel.StillVisibility == Visibility.Visible && Get<Border>(page, "PosterStill").ActualWidth > 0,
                "宽窄往返隐藏了商店封面");
            Require((acrylic.Visibility == Visibility.Visible) == (width < 1100), "亚克力未按1100断点切换");
        }
        if (Native.VirtualScreen().Width / _root.XamlRoot.RasterizationScale > 2060)
        {
            ResizeProbeClient(2000, 900);
            await UntilAsync(() => Math.Abs(page.ViewModel.PageWidth - 1840) < 1);
            var content = Get<Grid>(page, "StoreScrollContent");
            var left = content.TransformToVisual(_root).TransformPoint(default).X;
            Require(Math.Abs(content.ActualWidth - 1840) < 1 && Math.Abs(left - 80) < 2,
                "超宽窗口的右侧空隙不是内容区居中产生");
            await SaveProbeScreenAsync("store-backdrop-centered-2000-screen.png");
            await ResizeResponsiveAsync(page, 1280, 900);
        }

        async Task RequireWebStateAsync(bool paused, bool muted)
        {
            webState = null;
            // 与生产控制共用WebMessage队列，查询在pause/mute之后执行；不让另一条脚本队列抢先。
            preview.Element.CoreWebView2.PostWebMessageAsString("probe-state");
            await UntilAsync(() => webState is not null);
            using var state = System.Text.Json.JsonDocument.Parse(webState!);
            Require(state.RootElement.GetProperty("paused").GetBoolean() == paused
                && state.RootElement.GetProperty("muted").GetBoolean() == muted, "网页预告实际暂停/静音状态与按钮不一致");
        }
    }

    // 所有外站响应由本地夹具截获，视频只从隔离虚拟来源读取；真实YouTube行为另记边界。
    private const string Api = """
        document.addEventListener('pointermove',()=>chrome.webview.postMessage('probe-pointer'));chrome.webview.postMessage('probe-ready');
        chrome.webview.addEventListener('message',e=>{if(e.data==='probe-state')document.getElementById('video').contentWindow.postMessage('probe-state','*')});
        window.YT={Player:function(id,o){const frame=document.getElementById(id);let ready=false;const poll=setInterval(()=>frame.contentWindow.postMessage('probe-ready','*'),100);
        addEventListener('message',e=>{if(e.source!==frame.contentWindow)return;if(e.data==='decoded')o.events.onStateChange({data:1});
        if(e.data==='embed-ready'&&!ready){ready=true;clearInterval(poll);o.events.onReady()}if(e.data==='probe-pointer'||String(e.data).startsWith('state:'))chrome.webview.postMessage(e.data)});this.playVideo=()=>frame.contentWindow.postMessage('play','*');
        this.pauseVideo=()=>frame.contentWindow.postMessage('pause','*');this.mute=()=>frame.contentWindow.postMessage('mute','*');this.unMute=()=>frame.contentWindow.postMessage('unmute','*');
        }};onYouTubeIframeAPIReady();
        """;
    private const string Embed = """
        <!doctype html><style>html,body,video{margin:0;width:100%;height:100%;overflow:hidden}video{object-fit:cover}.hover{display:none;position:absolute;top:0;background:red;color:white}body:hover .hover{display:block}</style>
        <video muted loop playsinline src="https://momoka/store-trailer.mp4"></video><div class="hover">悬停控件不应出现</div>
        <script>let v=document.querySelector('video');document.addEventListener('pointermove',()=>parent.postMessage('probe-pointer','*'));
        v.addEventListener('playing',()=>v.requestVideoFrameCallback(()=>parent.postMessage('decoded','*')));addEventListener('message',e=>{if(e.data==='pause')v.pause();if(e.data==='play')v.play();if(e.data==='mute')v.muted=true;if(e.data==='unmute')v.muted=false;
        if(e.data==='probe-ready')parent.postMessage('embed-ready','*');if(e.data==='probe-state')parent.postMessage('state:'+JSON.stringify({paused:v.paused,muted:v.muted}),'*')});</script>
        """;
}
