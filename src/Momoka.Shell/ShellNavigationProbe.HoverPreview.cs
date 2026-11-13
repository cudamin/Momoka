using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // 匿名数据、真实控件与 Popup.Closed；不移动指针，不启动媒体播放。
    private async Task HoverPreviewAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync("hover-preview.invalid");
        var portrait = await FixtureImageAsync(400, 600);
        var landscape = await FixtureImageAsync(800, 400);
        var logoBytes = await FixtureImageAsync(420, 88);
        var originalTheme = ThemeHost.Current;
        var originalMotion = HomeMotion.PreferenceEnabled;
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Get<FrameworkElement>(shell, "SignIn").Visibility = Visibility.Collapsed;
        var shellRoot = Get<Grid>(shell, "Root");
        var poster = new PosterCard { CardWidth = 170, PosterHeight = 255 };
        var stage = new Grid { Padding = new Thickness(24), MinHeight = 650 };
        poster.HorizontalAlignment = HorizontalAlignment.Center;
        poster.VerticalAlignment = VerticalAlignment.Top;
        poster.Margin = new Thickness(0, 260, 0, 0);
        stage.Children.Add(poster);
        var scroll = new ScrollViewer { Content = stage, IsHitTestVisible = false };
        shellRoot.Children.Add(scroll);
        _root.Children.Add(shell);
        var readings = new List<string>();
        var actions = 0;
        var runId = Guid.NewGuid().ToString("N");
        poster.ActionRequested += (_, args) =>
        {
            if (args.Action == CardAction.Details)
            {
                ItemCommands.Handle(fixture.Session, fixture.Actions, poster, args);
                return;
            }
            Require(args.Action == CardAction.Play, "预览按钮产生了非播放动作");
            actions++;
        };
        try
        {
            foreach (var width in new[] { 1280, 760, 360 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(scroll);
                Require(Math.Abs(_root.ActualWidth - width) < 2, "窗口实际宽度未到达请求值");
                Require(shell.KeyboardAcceleratorPlacementMode == Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden
                    && shellRoot.KeyboardAcceleratorPlacementMode == Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Auto,
                    "壳级快捷键提示未隐藏或业务控件提示被一并抑制");
                Require(shell.KeyboardAccelerators.Count == 2 && shell.KeyboardAccelerators.All(key => key.IsEnabled),
                    "关闭提示时移除了快捷键");
                Require(ToolTipService.GetToolTip(Get<Button>(shell, "BackButton"))?.ToString()?.Contains("返回", StringComparison.Ordinal) == true,
                    "返回按钮的显式提示丢失");
                foreach (var scenario in new[] { "resume", "empty", "virtual" })
                {
                    var pending = new ConcurrentQueue<TaskCompletionSource<HttpResponseMessage>>();
                    fixture.Transport.Reply = (request, token) =>
                    {
                        Require(request.Method == HttpMethod.Get, "悬停不能写入服务器");
                        if (request.RequestUri!.AbsolutePath.EndsWith("/Images/Logo", StringComparison.Ordinal))
                            return Task.FromResult(Picture(logoBytes));
                        if (!request.RequestUri!.AbsolutePath.EndsWith("/Images/Thumb", StringComparison.Ordinal))
                            return Task.FromResult(FakeTransport.Standard(request));
                        var response = Pending<HttpResponseMessage>();
                        pending.Enqueue(response);
                        return response.Task.WaitAsync(token);
                    };
                    var item = new EmbyItem
                    {
                        Id = $"hover-{runId}-{width}-{scenario}",
                        Name = scenario == "resume" ? "青之箱：冬季赛场上的约定与新的开始"
                            : scenario == "empty" ? "字段为空的影片" : "尚未播出的虚拟单集",
                        Type = scenario == "empty" ? EmbyItemType.Movie : EmbyItemType.Episode,
                        ImageTags = new() { [EmbyImageStore.Thumb] = "fixture-thumb" }
                    };
                    if (scenario == "resume")
                    {
                        item.SeriesName = item.Name;
                        item.ParentIndexNumber = 2;
                        item.IndexNumber = 5;
                        item.RunTimeTicks = TimeSpan.FromMinutes(24).Ticks;
                        item.CommunityRating = 8.6f;
                        item.Genres = ["Animation", "Comedy", "Romance"];
                        item.Overview = "终于迎来了冬季杯的首场比赛。在队友的鼓励下，带着共同的约定踏上赛场，迎接属于自己的挑战。";
                        item.UserData = new() { PlaybackPositionTicks = TimeSpan.FromMinutes(6).Ticks };
                        item.ParentLogoItemId = "series-logo";
                        item.ParentLogoImageTag = "logo";
                        item.PremiereDate = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
                    }
                    if (scenario == "virtual") item.LocationType = "Virtual";
                    var card = new CardItem(fixture.Session.Capture().Bind(item), fixture.Images, 170,
                        upcoming: scenario == "virtual", fetchPoster: _ => Task.FromResult(portrait));
                    poster.Card = card;
                    await card.EnsurePosterAsync();
                    await LayoutAsync(poster);
                    Require(ShowPreview() == card.Title, "正式卡片未打开悬停预览");
                    var popup = Get<Popup>(poster, "_previewHost");
                    var preview = Get<CardPreview>(poster, "_preview");
                    await LayoutAsync(preview);
                    await UntilAsync(() => pending.Count >= 1);
                    var old = pending.ToArray()[0];
                    popup.IsOpen = false;
                    await UntilAsync(() => !Get<bool>(poster, "_previewOpen")
                        && Get<CancellationTokenSource?>(poster, "_previewArtwork") is null);
                    Require(Get<List<ScrollViewer>>(poster, "_previewScrolls").Count == 0, "关闭后仍保留滚动监听");
                    Require(ShowPreview() == card.Title, "关闭后无法重新打开");
                    await LayoutAsync(preview);
                    await UntilAsync(() => pending.Count >= 2);
                    var image = Get<Image>(preview, "PreviewImage");
                    var media = Get<Border>(preview, "Media");
                    var fallback = image.Source;
                    Require(fallback is not null, "宽图未到时没有海报");
                    var before = new Size(preview.ActualWidth, preview.ActualHeight);
                    var mediaBefore = new Size(media.ActualWidth, media.ActualHeight);
                    old.TrySetResult(Picture(landscape));
                    await Task.Delay(100);
                    Require(ReferenceEquals(image.Source, fallback), "旧请求覆盖重新打开的预览");
                    pending.ToArray()[1].TrySetResult(Picture(landscape));
                    try { await UntilAsync(() => image.Source is not null && !ReferenceEquals(image.Source, fallback)); }
                    catch
                    {
                        readings.Add($"图片到达等待失败：open={popup.IsOpen}/{Get<bool>(poster, "_previewOpen")}, shown={Get<bool>(preview, "_shown")}, running={Get<bool>(preview, "_running")}, pending={Get<BitmapImage?>(preview, "_pendingArtwork") is not null}, fade={Get<Storyboard?>(preview, "_artworkFade") is not null}, panel={Get<Grid>(preview, "Panel").Visibility}, root={preview.XamlRoot is not null}, source={image.Source is not null}");
                        throw;
                    }
                    await LayoutAsync(preview);
                    Require(Math.Abs(preview.ActualHeight - before.Height) < 1
                        && Math.Abs(media.ActualHeight - mediaBefore.Height) < 1, "宽图到达后浮层跳高");
                    Require(Math.Abs(image.Width - media.ActualWidth) < 1
                        && Math.Abs(image.Height - media.ActualHeight) < 1, "图片未匹配媒体视口");
                    var play = Get<Button>(preview, "PlayButton");
                    Require(play.Visibility == card.PlayVisibility, "播放入口未沿用卡片能力");
                    var bounds = Bounds(preview, _root);
                    var artBounds = Bounds(Get<FrameworkElement>(poster, "Art"), _root);
                    Require(Math.Abs(bounds.Left + bounds.Width / 2 - artBounds.Left - artBounds.Width / 2) < 1
                        && Math.Abs(bounds.Top + bounds.Height / 2 - artBounds.Top - artBounds.Height / 2) < 1,
                        $"预览没有以封面中心对齐：panel={bounds}; art={artBounds}");
                    Require(!VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot).Any(open => open.Child is ToolTip tip
                        && (tip.Content?.ToString()?.Contains("Alt", StringComparison.OrdinalIgnoreCase) ?? false)), "预览误弹出Alt快捷键提示");
                    Require(bounds.Left >= 10 && bounds.Top >= 10 && bounds.Right <= _root.ActualWidth - 10
                        && bounds.Bottom <= _root.ActualHeight - 10, $"预览越界：{bounds} / {_root.ActualWidth}x{_root.ActualHeight}");
                    foreach (var name in new[] { "TitleText", "MetaText", "GenresText", "OverviewText", "ResumeText" })
                    {
                        var text = Get<TextBlock>(preview, name);
                        if (text.Visibility != Visibility.Visible || text.ActualWidth <= 0) continue;
                        var box = Bounds(text, preview);
                        Require(box.Left >= 0 && box.Right <= preview.ActualWidth + 1
                            && box.Bottom <= preview.ActualHeight + 1, "文本越界：" + name);
                    }
                    if (scenario == "empty")
                        foreach (var name in new[] { "FactsBlock", "OverviewText", "ResumeBlock" })
                            Require(Get<FrameworkElement>(preview, name).Visibility == Visibility.Collapsed, "空字段未收起：" + name);
                    if (scenario == "resume")
                    {
                        await UntilAsync(() => Get<Image>(preview, "LogoImage").Source is not null);
                        Require(Bounds(Get<Image>(preview, "LogoImage"), preview).Bottom
                            <= Bounds(Get<TextBlock>(preview, "TitleText"), preview).Top, "徽标未在片名上方");
                        var ratingBounds = Bounds(Get<TextBlock>(preview, "RatingText"), preview);
                        var dateBounds = Bounds(Get<TextBlock>(preview, "MetaText"), preview);
                        var genresBounds = Bounds(Get<TextBlock>(preview, "GenresText"), preview);
                        Require(Math.Abs(ratingBounds.Top - dateBounds.Top) < 2 && Math.Abs(dateBounds.Top - genresBounds.Top) < 2,
                            "评分日期类型未排在一行");
                        Require(Get<TextBlock>(preview, "PlayText").Text == "继续播放", "续播文案错误");
                        Require(!Get<TextBlock>(preview, "MetaText").Text.Contains(item.SeriesName!, StringComparison.Ordinal), "重复剧名");
                        var progress = Get<ProgressBar>(preview, "ResumeProgress");
                        var peer = new ProgressBarAutomationPeer(progress);
                        Require(peer.GetAutomationId() == "CardPreviewProgress", "进度条无法定位");
                        var value = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
                        Require(value.Minimum == 0 && value.Maximum == 100 && Math.Abs(value.Value - 25) < 0.1, "进度条UIA读数错误");
                        var buttonPeer = new ButtonAutomationPeer(play);
                        Require(buttonPeer.GetAutomationId() == "CardPreviewPlay" && buttonPeer.GetName() == card.PlayActionTip,
                            "播放UIA名称或定位错误");
                        Require(play.ActualHeight >= 40, "播放按钮命中区域不足");
                        if (width == 1280)
                        {
                            foreach (var theme in UiThemes.All)
                            {
                                ThemeHost.Apply(theme);
                                await LayoutAsync(preview);
                                await SaveProbeScreenAsync($"hover-preview-{theme.Id}-screen.png");
                            }
                            ThemeHost.Apply(originalTheme);
                            HomeMotion.ApplyPreference(true);
                            if (HomeMotion.AnimationsEnabled)
                            {
                                await HoverArtworkFadeAsync(preview, readings);
                                var completed = 0;
                                preview.PlayEnter();
                                preview.PlayExit(() => completed++);
                                preview.PlayEnter();
                                await Task.Delay(300);
                                Require(completed == 0 && Get<Grid>(preview, "Panel").Visibility == Visibility.Visible, "反转执行旧关闭回调");
                                preview.PlayExit(() => completed++);
                                HomeMotion.ApplyPreference(false);
                                Require(completed == 1 && Get<Grid>(preview, "Panel").Visibility == Visibility.Collapsed, "关动画未立即落定");
                                await HoverMorphFramesAsync(preview, readings);
                            }
                            else readings.Add("系统动画关闭，未覆盖有帧反转");
                            HomeMotion.ApplyPreference(originalMotion);
                            preview.PlayEnter();
                            await UntilAsync(() => !Get<bool>(preview, "_running"));
                            // 底边案例让整张原卡仍在视口内，避免焦点自动滚动按产品规则关闭预览。
                            foreach (var top in new[] { 0d, 550d })
                            {
                                Call(poster, "ClosePreview", true);
                                poster.Margin = new Thickness(0, top, 0, 0);
                                await LayoutAsync(poster);
                                ShowPreview();
                                await LayoutAsync(preview);
                                var edge = Bounds(preview, _root);
                                Require(top == 0 ? Math.Abs(edge.Top - 12) < 1 : Math.Abs(edge.Bottom - _root.ActualHeight + 12) < 1,
                                    $"顶底边界未正确夹取：{top}/{edge}");
                            }
                            Call(poster, "ClosePreview", true);
                            poster.Margin = new Thickness(0, 260, 0, 0);
                            await LayoutAsync(poster);
                            ShowPreview();
                            await LayoutAsync(preview);
                        }
                        await SaveProbeScreenAsync($"hover-preview-resume-{width}-screen.png");
                        CheckPicture(preview, width, scenario);
                        var expected = actions + 1;
                        ((IInvokeProvider)buttonPeer.GetPattern(PatternInterface.Invoke)).Invoke();
                        await UntilAsync(() => actions == expected && !popup.IsOpen);
                        Require(ShowPreview() == card.Title, "播放后无法重开预览验证详情");
                        await LayoutAsync(preview);
                        var details = Get<Button>(preview, "DetailsButton");
                        var opened = fixture.Actions.Opened;
                        ((IInvokeProvider)new ButtonAutomationPeer(details).GetPattern(PatternInterface.Invoke)).Invoke();
                        await UntilAsync(() => fixture.Actions.Opened == opened + 1 && !popup.IsOpen);
                        Require(actions == expected, "打开详情错误触发了播放");
                    }
                    else
                    {
                        await SaveProbeScreenAsync($"hover-preview-{scenario}-{width}-screen.png");
                        CheckPicture(preview, width, scenario);
                    }
                    readings.Add($"{width}/{scenario}: panel={before}; media={mediaBefore}; Closed后重开通过");
                    Call(poster, "ClosePreview", true);
                    await UntilAsync(() => Get<CancellationTokenSource?>(poster, "_previewArtwork") is null);
                    card.AbandonPoster();
                }
            }
            Require(actions == 3 && fixture.Actions.Opened == 3 && fixture.Actions.Plays == 0, "匿名动作次数错误或进入播放服务");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "hover-preview.txt"), readings);
            Call(poster, "ClosePreview", true);
            poster.Card?.AbandonPoster();
            shellRoot.Children.Remove(scroll);
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
            ThemeHost.Apply(originalTheme);
            HomeMotion.ApplyPreference(originalMotion);
            ResizeInspect(1420, 980);
        }

        string? ShowPreview()
        {
            var title = poster.ShowPreviewForShot();
            // 该用例只验显式状态与UIA动作，真实鼠标不能在截图期间触发离开计时。
            Get<CardPreview>(poster, "_preview").IsHitTestVisible = false;
            return title;
        }
        void CheckPicture(CardPreview preview, int width, string scenario)
        {
            var image = Get<Image>(preview, "PreviewImage");
            var cover = Get<Border>(preview, "TransitionCover");
            var pictureBounds = Bounds(cover, _root);
            var imageVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(image);
            var surfaceVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(cover);
            readings.Add($"{width}/{scenario}: image={image.Source is not null}, size={image.ActualWidth}x{image.ActualHeight}, visible={image.Visibility}/{cover.Visibility}, opacity={image.Opacity}/{imageVisual.Opacity}, surface={pictureBounds}, visual={surfaceVisual.Scale}/{surfaceVisual.Offset}");
            var pixels = CompositionPlaybackProbe.CaptureResizeFrame(_window!.Handle, 0);
            var scale = _root.XamlRoot.RasterizationScale;
            var x = (int)((pictureBounds.Left + pictureBounds.Width / 2) * scale);
            var y = (int)((pictureBounds.Top + pictureBounds.Height / 4) * scale);
            var pixel = (y * pixels.Width + x) * 4;
            Require(pixels.Stable && pixel >= 0 && pixel + 3 < pixels.Bytes.Length, "图片屏幕取样越界");
            readings.Add($"{width}/{scenario}: picture-screen-bgra={string.Join(',', pixels.Bytes[pixel..(pixel + 4)])}");
            Require(pixels.Bytes[pixel] > 55 || pixels.Bytes[pixel + 1] > 55 || pixels.Bytes[pixel + 2] > 55,
                "图片Source已赋值但实际屏幕仍是深色空图");
        }
        static HttpResponseMessage Picture(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        static Rect Bounds(FrameworkElement element, UIElement target) => element.TransformToVisual(target)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    private async Task HoverMorphFramesAsync(CardPreview preview, List<string> readings)
    {
        var state = Get<CompositionPropertySet>(preview, "_state");
        HomeMotion.ApplyPreference(true);
        state.StopAnimation("Progress");
        state.InsertScalar("Progress", 0);
        var title = Get<TextBlock>(preview, "TitleText");
        var titleWidth = title.ActualWidth;
        var frames = 0;
        var samples = new List<float>();
        void Frame(object? sender, object args)
        {
            var controller = state.TryGetAnimationController("Progress");
            frames++;
            samples.Add(controller?.Progress ?? -1);
            Require(Get<Grid>(preview, "Panel").Opacity == 1 && Get<Grid>(preview, "ContentRoot").Opacity == 1,
                "撑开动画不能淡入淡出");
            Require(Math.Abs(title.ActualWidth - titleWidth) < 1, "撑开时文字被重排");
        }
        CompositionTarget.Rendering += Frame;
        try { preview.PlayEnter(); await Task.Delay(350); }
        finally { CompositionTarget.Rendering -= Frame; }
        readings.Add($"撑开动画：frames={frames}，controller={string.Join(',', samples)}，整卡不透明且标题宽度不变");
        Require(frames >= 2, "没有收到撑开期间的呈现事件");
        // 独立冻结中点留像素证据；明确不是动画帧速率测量。
        state.InsertScalar("Progress", 0.5f);
        await SaveProbeScreenAsync("hover-preview-morph-midpoint-screen.png");
        var surface = Get<Border>(preview, "Surface");
        var middle = surface.TransformToVisual(preview).TransformBounds(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
        Require(middle.Width > 170 && middle.Width < preview.ActualWidth - 1,
            $"半程边框没有实际撑开：{middle.Width:0.0}/{preview.ActualWidth:0.0}");
        Require(Get<Grid>(preview, "Panel").Opacity == 1, "半程卡片不应半透明");
        readings.Add($"冻结半程真实边框={middle}，最终={preview.ActualWidth:0.0}×{preview.ActualHeight:0.0}");
        state.InsertScalar("Progress", 1);
    }

    private async Task HoverArtworkFadeAsync(CardPreview preview, List<string> readings)
    {
        var first = (await PosterLoader.DecodeAsync(await LibraryCoverFixtureAsync(1), 400))!;
        var second = (await PosterLoader.DecodeAsync(await LibraryCoverFixtureAsync(2), 400))!;
        var third = (await PosterLoader.DecodeAsync(await LibraryCoverFixtureAsync(3), 400))!;
        var image = Get<Image>(preview, "PreviewImage");
        var incoming = Get<Image>(preview, "TransitionImage");
        var cover = Get<Border>(preview, "TransitionCover");
        HomeMotion.ApplyPreference(false);
        preview.PlayEnter();
        await UntilAsync(() => !Get<bool>(preview, "_running"));
        preview.SetArtwork(first);
        Require(ReferenceEquals(image.Source, first), "关闭动画时未准备第一张图片");
        HomeMotion.ApplyPreference(true);
        preview.SetArtwork(second);
        var fade = Get<Storyboard?>(preview, "_artworkFade");
        Require(fade is not null, $"图片淡化未启动：shown={Get<bool>(preview, "_shown")}, running={Get<bool>(preview, "_running")}, enabled={HomeMotion.AnimationsEnabled}, source={image.Source is not null}, pending={Get<BitmapImage?>(preview, "_pendingArtwork") is not null}");
        if (fade is null) return;
        fade.Pause();
        fade.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(100));
        await LayoutAsync(preview);
        Require(ReferenceEquals(image.Source, first) && ReferenceEquals(incoming.Source, second)
            && Math.Abs(incoming.Opacity - 0.5) < 0.05 && image.Opacity == 1, "图片半程没有交叉混合或旧图提前消失");
        Require(Get<Grid>(preview, "Panel").Opacity == 1 && Get<Grid>(preview, "ContentRoot").Opacity == 1,
            "图片切换连带淡化了整卡和文字");
        var middleOpacity = incoming.Opacity;
        var middle = await PixelAsync(cover);
        await SaveProbeScreenAsync("hover-preview-image-midpoint-screen.png");
        fade.SeekAlignedToLastTick(TimeSpan.Zero);
        var start = await PixelAsync(cover);
        fade.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(199));
        var end = await PixelAsync(cover);
        Require(Enumerable.Range(0, 3).Any(channel => Math.Abs(start[channel] - end[channel]) > 30), "图片夹具没有足够色差");
        Require(Enumerable.Range(0, 3).All(channel => Math.Abs(middle[channel] - (start[channel] + end[channel]) / 2d) < 8)
            && middle[3] == 255, "半程像素没有混合两图或露出了背景");
        readings.Add($"图片交叉淡化冻结半程：opacity={middleOpacity:0.000}; start={string.Join(',', start)}; middle={string.Join(',', middle)}; end={string.Join(',', end)}");
        preview.SetArtwork(third);
        Require(ReferenceEquals(Get<BitmapImage>(preview, "_pendingArtwork"), third), "淡化期间未保留最新图片");
        fade.Resume();
        await UntilAsync(() => ReferenceEquals(image.Source, third) && Get<Storyboard?>(preview, "_artworkFade") is null);
        preview.SetArtwork(third);
        preview.SetArtwork(null);
        Require(Get<Storyboard?>(preview, "_artworkFade") is null && incoming.Source is null && incoming.Opacity == 0,
            "同图或空图重启动画，或完成后残留旧图");
        preview.SetArtwork(first);
        preview.SetArtwork(second);
        HomeMotion.ApplyPreference(false);
        Require(ReferenceEquals(image.Source, second) && Get<Storyboard?>(preview, "_artworkFade") is null,
            "关闭动画未立即显示最新图片");
        HomeMotion.ApplyPreference(true);
        preview.SetArtwork(first);
        var beforeExit = incoming.Source;
        preview.PlayExit(() => { });
        Require(ReferenceEquals(incoming.Source, beforeExit) && ReferenceEquals(image.Source, second), "收回第一帧突然换图");
        preview.PlayEnter();
        await UntilAsync(() => !Get<bool>(preview, "_running") && Get<Storyboard?>(preview, "_artworkFade") is null);
        preview.SetArtwork(third);
        var card = Get<CardItem>(preview, "_card");
        preview.Reset();
        await Task.Delay(250);
        Require(image.Source is null && incoming.Source is null && Get<Storyboard?>(preview, "_artworkFade") is null,
            "Reset后旧淡化回调恢复了图片");
        preview.Show(card);
        preview.PlayEnter();
        await UntilAsync(() => !Get<bool>(preview, "_running"));
        Call(preview, "OnUnloaded", preview, new RoutedEventArgs());
        Require(Get<bool>(preview, "_shown") && image.Source is not null,
            "仍在活动树的迟到Unloaded清空了重开预览");
        readings.Add("图片同源/空图、连续到达、关闭动画、收回反转、Reset迟到完成检查通过");

        static async Task<byte[]> PixelAsync(FrameworkElement target)
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(target);
            var bytes = (await bitmap.GetPixelsAsync()).ToArray();
            var offset = ((bitmap.PixelHeight / 2) * bitmap.PixelWidth + bitmap.PixelWidth / 2) * 4;
            return bytes[offset..(offset + 4)];
        }
    }
}
