using System.Text;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Momoka.Shell.Interop;
using Momoka.Shell.Views;
using Momoka.Emby;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task StoreTrailerWebAsync(DetailPage page)
    {
        Call(page, "StopStoreTrailer");
        Field(page, "_storeTrailerSubject", page.ViewModel.CurrentItem);
        Call(page, "LayoutStoreTrailer");
        var folder = Path.Combine(_options.Paths.LogDirectory, "trailer-web-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        // 匿名探针用自己的一次性配置，不读写用户的 YouTube 数据。
        using var preview = new StoreYouTubePreview();
        Field(page, "_storeYouTube", preview);
        var host = Get<Grid>(page, "StoreTrailerHost");
        host.Visibility = Visibility.Visible;
        host.Opacity = 0;
        Get<Grid>(page, "StoreTrailerContent").Children.Add(preview.Element);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ProbeAssets", "store-trailer.mp4"), Path.Combine(folder, "store-trailer.mp4"));
        var blocked = 0;
        var apiRequests = 0;
        var embedRequests = 0;
        var webMoves = 0;
        var playbackChanges = 0;
        var captionChanges = 0;
        string? webState = null;
        var instrumentationReady = false;
        // 手工安装的匿名预告沿用正式 StartStoreTrailerAsync 的通知接线；不靠探针直接写图标或字幕列表。
        preview.PlaybackChanged += () =>
        {
            playbackChanges++;
            Require(page.DispatcherQueue.TryEnqueue(() => Call(page, "UpdateStoreTrailerControls")), "网页预告播放通知未进入页面队列");
        };
        preview.CaptionsChanged += () =>
        {
            captionChanges++;
            Require(page.DispatcherQueue.TryEnqueue(() => Call(page, "UpdateStoreTrailerControls")), "网页字幕通知未进入页面队列");
        };
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
                        ? "Content-Type: application/javascript; charset=utf-8" : "Content-Type: text/html; charset=utf-8");
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
            Get<Border>(page, "StoreTrailerFoundation").Visibility = Visibility.Visible;
            host.Opacity = 1;
            Get<CommandBar>(page, "StoreTrailerControls").Visibility = Visibility.Visible;
            Call(page, "UpdateStoreTrailerControls");
        });
        await UntilAsync(() => Get<bool>(page, "_storeTrailerOpened"));
        await UntilAsync(() => instrumentationReady);
        Require(apiRequests == 1 && embedRequests == 1, "网页夹具没有截获实际API和iframe请求");
        await UntilAsync(() => captionChanges > 0 && preview.Captions.Count == 2 && preview.IsPlaying);
        var captionReading = System.Text.Json.JsonSerializer.Serialize(new
        {
            Tracks = preview.Captions.Select(track => new { track.Language, track.Name }).ToArray(),
            preview.SelectedCaption,
            CaptionChanges = captionChanges,
            ApiRequests = apiRequests,
            EmbedRequests = embedRequests
        });
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "store-trailer-web-caption-reading.json"), captionReading);
        Require(preview.Captions.Select(track => (track.Language, track.Name))
            .SequenceEqual(new[] { ("zh", "匿名中文"), ("en", "Anonymous English") }), "真实网页字幕消息未解析为固定两轨：" + captionReading);
        Require(preview.SelectedCaption is null, "网页预告字幕没有默认关闭");
        await RequireWebStateAsync(paused: false, muted: true);
        await RequireWebCaptionAsync(null);
        foreach (var caption in new string?[] { "zh", "en", null }) await SelectWebCaptionAsync(caption);
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
            // 桌面被别的窗口占着时，这里等到 6 秒仍不进来就如实报红；不绕过前台守卫去注入指针。
            await UntilAsync(() => Native.GetForegroundWindow() == _window.Handle, attempts: 240);
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
        await StoreTrailerWebScrollAsync(page, preview, ReadWebStateAsync, RequirePlaybackNotificationAsync);
        Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
        Require(Get<bool>(page, "_storeTrailerUserPaused"), "网页预告暂停按钮没有切换");
        await RequireWebStateAsync(paused: true, muted: true);
        await RequirePlaybackNotificationAsync(paused: true);
        Call(page, "OnStoreTrailerMute", Get<AppBarButton>(page, "StoreTrailerMute"), new RoutedEventArgs());
        await RequireWebStateAsync(paused: true, muted: false);
        Call(page, "OnStoreTrailerMute", Get<AppBarButton>(page, "StoreTrailerMute"), new RoutedEventArgs());
        Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
        await RequirePlaybackNotificationAsync(paused: false);
        await RequireWebStateAsync(paused: false, muted: true);
        // 让网页自己暂停，验证生产状态通知决定下一次原生点击是继续，而不是再发一次暂停。
        preview.Element.CoreWebView2.PostWebMessageAsString("probe-pause");
        await RequireWebStateAsync(paused: true, muted: true);
        await RequirePlaybackNotificationAsync(paused: true);
        Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
        await RequirePlaybackNotificationAsync(paused: false);
        await RequireWebStateAsync(paused: false, muted: true);
        Call(page, "OnStoreTrailerPause", Get<AppBarButton>(page, "StoreTrailerPause"), new RoutedEventArgs());
        await RequireWebStateAsync(paused: true, muted: true);
        await RequirePlaybackNotificationAsync(paused: true);
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
        foreach (var width in new[] { 1023, 1024, 1025, 760, 360, 1280 })
        {
            await ResizeResponsiveAsync(page, width, 900);
            Require(page.ViewModel.StillVisibility == Visibility.Visible && Get<Border>(page, "PosterStill").ActualWidth > 0,
                "宽窄往返隐藏了商店封面");
            Require((acrylic.Visibility == Visibility.Visible) == (width < DetailHero.StoreWideFloor), "亚克力未按商店宽窄断点切换");
        }
        if (Native.VirtualScreen().Width / _root.XamlRoot.RasterizationScale > 2060)
        {
            ResizeProbeClient(2000, 900);
            await UntilAsync(() =>
            {
                page.UpdateLayout();
                return StoreDetailProbeEvidence.PageSizeMatches(2000,
                    _window!.ClientSize.Width / _root.XamlRoot.RasterizationScale,
                    page.ActualWidth, Get<ScrollView>(page, "Body").ActualWidth, page.ActualWidth)
                    && Math.Abs(page.ViewModel.PageWidth - 1840) < 1;
            });
            await LayoutAsync(page);
            var content = Get<Grid>(page, "StoreScrollContent");
            var left = content.TransformToVisual(_root).TransformPoint(default).X;
            Require(Math.Abs(content.ActualWidth - 1840) < 1 && Math.Abs(left - 80) < 2,
                $"超宽窗口的右侧空隙不是内容区居中产生：页{page.ActualWidth:0.##}、内容{content.ActualWidth:0.##}、左{left:0.##}");
            await SaveProbeScreenAsync("store-backdrop-centered-2000-screen.png");
            await ResizeResponsiveAsync(page, 1280, 900);
        }
        Require(blocked == 0, "匿名网页字幕探针请求了夹具之外的外站资源");

        async Task<StoreTrailerWebRead> ReadWebStateAsync()
        {
            webState = null;
            preview.Element.CoreWebView2.PostWebMessageAsString("probe-state");
            await UntilAsync(() => webState is not null);
            await File.AppendAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "store-trailer-web-state-readings.jsonl"), webState + "\n");
            return StoreTrailerScrollProbeEvidence.ReadWeb(webState!);
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

        async Task RequirePlaybackNotificationAsync(bool paused)
        {
            await UntilAsync(() => preview.IsPlaying == !paused && Get<AppBarButton>(page, "StoreTrailerPause").Label
                == (paused ? "播放预告片" : "暂停预告片"));
            Require(playbackChanges > 0, "网页播放/暂停没有经过生产 PlaybackChanged 通知");
        }

        async Task RequireWebCaptionAsync(string? language)
        {
            webState = null;
            preview.Element.CoreWebView2.PostWebMessageAsString("probe-state");
            await UntilAsync(() => webState is not null);
            using var state = System.Text.Json.JsonDocument.Parse(webState!);
            Require(state.RootElement.GetProperty("caption").GetString() == (language ?? "")
                && state.RootElement.GetProperty("apiCaption").GetString() == (language ?? ""), "网页字幕API与实际文本轨回读不一致");
        }

        async Task SelectWebCaptionAsync(string? language)
        {
            var subtitle = Get<AppBarButton>(page, "StoreTrailerSubtitles");
            await UntilAsync(() => subtitle.Visibility == Visibility.Visible && subtitle.ActualWidth > 0);
            Call(page, "OnStoreTrailerSubtitles", subtitle, new RoutedEventArgs());
            var menu = Get<MenuFlyout>(page, "_storeTrailerTracksMenu");
            var rows = menu.Items.OfType<RadioMenuFlyoutItem>().ToArray();
            Require(rows.Length == 3 && rows.Select(row => row.Text).SequenceEqual(new[] { "关闭字幕", "匿名中文", "Anonymous English" }),
                "原生网页字幕菜单没有关闭与实际两轨");
            var checkedText = preview.SelectedCaption switch { "zh" => "匿名中文", "en" => "Anonymous English", _ => "关闭字幕" };
            Require(rows.Count(row => row.IsChecked) == 1 && rows.Single(row => row.IsChecked).Text == checkedText,
                "重新打开的字幕菜单没有反映实际选轨");
            var selectedText = language switch { "zh" => "匿名中文", "en" => "Anonymous English", _ => "关闭字幕" };
            var selected = rows.Single(row => row.Text == selectedText);
            await UntilAsync(() => menu.IsOpen && selected.IsLoaded && selected.ActualWidth > 0
                && FrameworkElementAutomationPeer.CreatePeerForElement(selected) is { } visiblePeer && !visiblePeer.IsOffscreen());
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(selected)!;
            Require(peer.GetPattern(PatternInterface.Toggle) is IToggleProvider, "原生网页字幕菜单没有单选自动化模式");
            await SaveProbeScreenAsync($"store-trailer-web-captions-{language ?? "off"}-screen.png");
            var previousChanges = captionChanges;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnClosed(object? sender, object args) => closed.TrySetResult();
            menu.Closed += OnClosed;
            try
            {
                ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally { menu.Closed -= OnClosed; }
            await UntilAsync(() => captionChanges > previousChanges && preview.SelectedCaption == language);
            Require(!menu.IsOpen, "选择网页字幕后原生菜单没有关闭");
            await RequireWebCaptionAsync(language);
            await File.AppendAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "store-trailer-web-captions.txt"),
                $"language={language ?? "off"}; captionChanges={captionChanges}; menuClosed={!menu.IsOpen}; apiAndTextTrackMatched=true\n");
        }
    }

    // 所有外站响应由本地夹具截获，视频只从隔离虚拟来源读取；真实YouTube行为另记边界。
    private const string Api = """
        document.addEventListener('pointermove',()=>chrome.webview.postMessage('probe-pointer'));chrome.webview.postMessage('probe-ready');
        chrome.webview.addEventListener('message',e=>{if(e.data==='probe-state'||e.data==='probe-pause')document.getElementById('video').contentWindow.postMessage(e.data,'*')});
        window.YT={Player:function(id,o){const frame=document.getElementById(id);let ready=false,caption='',playCalls=0,pauseCalls=0;const tracks=[{languageCode:'zh',displayName:'匿名中文'},{languageCode:'en',displayName:'Anonymous English'}];
        const poll=setInterval(()=>frame.contentWindow.postMessage('probe-ready','*'),100);
        addEventListener('message',e=>{if(e.source!==frame.contentWindow)return;if(e.data==='decoded')o.events.onStateChange({data:1});
        if(e.data==='paused')o.events.onStateChange({data:2});if(e.data&&e.data.captionApplied!==undefined){caption=e.data.captionApplied;o.events.onApiChange()}
        if(e.data==='embed-ready'&&!ready){ready=true;clearInterval(poll);o.events.onReady()}if(e.data==='probe-pointer')chrome.webview.postMessage(e.data);
        if(String(e.data).startsWith('state:')){const state=JSON.parse(e.data.substring(6));state.apiCaption=this.getOption('captions','track').languageCode||'';state.playCalls=playCalls;state.pauseCalls=pauseCalls;chrome.webview.postMessage('state:'+JSON.stringify(state))}});this.playVideo=()=>{playCalls++;frame.contentWindow.postMessage('play','*')};
        this.pauseVideo=()=>{pauseCalls++;frame.contentWindow.postMessage('pause','*')};this.mute=()=>frame.contentWindow.postMessage('mute','*');this.unMute=()=>frame.contentWindow.postMessage('unmute','*');
        this.loadModule=name=>{if(name==='captions')queueMicrotask(()=>o.events.onApiChange())};
        this.getOption=(module,name)=>module==='captions'?(name==='tracklist'?tracks:{languageCode:caption}):null;
        this.setOption=(module,name,value)=>{if(module==='captions'&&name==='track')frame.contentWindow.postMessage({caption:value.languageCode||''},'*')};
        }};onYouTubeIframeAPIReady();
        """;
    private const string Embed = """
        <!doctype html><meta charset="utf-8"><style>html,body,video{margin:0;width:100%;height:100%;overflow:hidden}video{object-fit:cover}.hover{display:none;position:absolute;top:0;background:red;color:white}body:hover .hover{display:block}</style>
        <video muted loop playsinline src="https://momoka/store-trailer.mp4"></video><div class="hover">悬停控件不应出现</div>
        <script>let v=document.querySelector('video');const captions=[v.addTextTrack('subtitles','匿名中文','zh'),v.addTextTrack('subtitles','Anonymous English','en')];
        captions.forEach((t,i)=>{t.addCue(new VTTCue(0,2,i?'Anonymous English':'匿名中文字幕'));t.mode='disabled'});
        document.addEventListener('pointermove',()=>parent.postMessage('probe-pointer','*'));
        v.addEventListener('playing',()=>v.requestVideoFrameCallback(()=>parent.postMessage('decoded','*')));addEventListener('message',e=>{if(e.data==='pause')v.pause();if(e.data==='play')v.play();if(e.data==='mute')v.muted=true;if(e.data==='unmute')v.muted=false;
        if(e.data==='probe-pause')v.pause();if(e.data&&e.data.caption!==undefined){captions.forEach(t=>t.mode=t.language===e.data.caption?'showing':'disabled');parent.postMessage({captionApplied:captions.find(t=>t.mode==='showing')?.language||''},'*')}
        if(e.data==='probe-ready')parent.postMessage('embed-ready','*');if(e.data==='probe-state')parent.postMessage('state:'+JSON.stringify({paused:v.paused,muted:v.muted,caption:captions.find(t=>t.mode==='showing')?.language||''}),'*')});
        v.addEventListener('pause',()=>parent.postMessage('paused','*'));</script>
        """;
}
