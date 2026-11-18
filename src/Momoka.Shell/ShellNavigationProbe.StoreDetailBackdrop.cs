using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实详情页、匿名图片与首卡；仅布局预告控件占位，不创建任何播放或网页后端。</summary>
    private async Task StoreDetailBackdropAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var savedTheme = ThemeHost.Current.Id;
        var motion = HomeMotion.PreferenceEnabled;
        var frame = new Frame();
        var reads = new List<object>();
        try
        {
            HomeMotion.ApplyPreference(false);
            var poster = await FixtureImageAsync(400, 600);
            var backdrop = await StoreBackdropFixtureAsync(ambient: true);
            var series = Series();
            series.Name = "商店详情";
            series.Overview = "首字完整。" + string.Concat(Enumerable.Repeat(ResponsiveOverview, 8));
            series.People = [new() { Id = "store-director", Name = "离线导演", Type = "Director" }];
            series.ImageTags["Primary"] = "store-poster";
            series.BackdropImageTags = ["store-backdrop"];
            var episode = Episode("a");
            episode.MediaSources =
            [
                new() { Id = "store-source-a", Container = "mkv", MediaStreams =
                    [new() { Index = 0, Type = "Video", Codec = "h264", Width = 1920, Height = 1080 }] },
                new() { Id = "store-source-b", Container = "mkv", MediaStreams =
                    [new() { Index = 0, Type = "Video", Codec = "h264", Width = 1280, Height = 720 }] }
            ];
            series.MediaSources = episode.MediaSources;
            fixture.Transport.Reply = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/Images/", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new ByteArrayContent(path.Contains("/Images/Primary", StringComparison.Ordinal) ? poster : backdrop) });
                return Task.FromResult(path.EndsWith("/Items/show", StringComparison.Ordinal) ? Json(series)
                    : path.EndsWith("/Episodes", StringComparison.Ordinal) ? Json(new { Items = new[] { episode } })
                    : path.EndsWith("/Items/" + episode.Id, StringComparison.Ordinal) ? Json(episode)
                    : FakeTransport.Standard(request));
            };
            fixture.Settings.Ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            _root.Children.Add(frame);
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, series)), "商店顶部检查导航失败");
            var page = (DetailPage)frame.Content;
            page.SetStorePreviewActive(false);
            page.AttachWindow(_window!);
            await UntilAsync(() => page.IsReady && page.IsLoaded && page.ViewModel.HeroImage is not null
                && page.ViewModel.StillImage is not null && Get<bool>(page, "_appearanceObserved"));
            var canvas = Get<Grid>(page, "StoreScrollContent");
            var scroll = Get<ScrollView>(page, "Body");
            var overview = Get<TextBlock>(page, "Overview");
            foreach (var width in new[] { 1240, 760, 360, 1240 })
            {
                await ResizeResponsiveAsync(page, width, 900);
                await ScrollProbeAsync(scroll, 0);
                await LayoutAsync(page);
                Call(page, "LayoutStoreTrailer");
                // 连同隐藏的预告视口量实际边距；只有匿名空容器，绝不启动素材。
                var trailer = Get<Grid>(page, "StoreTrailerHost");
                trailer.Visibility = Visibility.Visible;
                await LayoutAsync(page);
                var picture = Get<Grid>(page, "StoreBackdrop");
                var acrylic = Get<Grid>(page, "StoreBackdropAcrylic");
                var pictureBox = Bounds(picture, canvas);
                var pictureGap = canvas.ActualWidth - pictureBox.Right;
                var trailerGap = canvas.ActualWidth - Bounds(trailer, canvas).Right;
                var acrylicGap = acrylic.Visibility == Visibility.Visible
                    ? canvas.ActualWidth - Bounds(acrylic, canvas).Right : pictureGap;
                StoreDetailProbeEvidence.RequireViewport(page.ActualWidth, page.ViewModel.PageWidth,
                    DetailHero.StoreBackdropWidth(page.ActualWidth), picture.ActualWidth,
                    pictureGap, acrylicGap, trailerGap, DetailHero.StorePreviewEdgeGap(page.ActualWidth));
                Require(Math.Abs(page.ActualWidth - width) < 1.5
                    && Math.Abs(_window!.ClientSize.Width / _root.XamlRoot.RasterizationScale - width) < 1.5,
                    "商店顶部检查没有取得目标实际页宽与客户区宽");
                trailer.Visibility = Visibility.Collapsed;
                var cover = Get<Border>(page, "PosterStill");
                // 2026-11-15 用户令「增加详情页/订阅详情页上方的空位」：封面从内容区上沿往下起排。
                // 2026-11-17 用户令「窄窗口出现亚克力背景时候组件整体向上移一点，左边跟上面的间距要一致」：
                // 宽版式仍是 StoreHeroTop，窄档（亚克力那一档）与左右留白同值。
                var coverTop = Bounds(cover, canvas).Top;
                var coverTopExpect = DetailHero.StoreHeroInsetTop(page.ViewModel.PageWidth);
                Require(Math.Abs(coverTop - coverTopExpect) < 1.5,
                    $"商店详情封面没有按当前档次的上留白起排：{coverTop:0.#} 对 {coverTopExpect:0.#}");
                var details = Get<StackPanel>(page, "StoreHeroDetails");
                var firstCard = Get<StackPanel>(page, "EpisodePanel");
                Require(firstCard.Visibility == Visibility.Visible && firstCard.ActualHeight > 0, "没有可量的正文首卡");
                StoreDetailProbeEvidence.RequireClearance(Bounds(cover, canvas).Bottom, Bounds(details, canvas).Top,
                    Bounds(details, canvas).Bottom, pictureBox.Bottom, 0, Bounds(firstCard, canvas).Top, DetailHero.StorePreviewGap);
                RequireStoreHeroAlignment(page);
                StoreDetailProbeEvidence.RequireOverview(overview.MaxLines, 2, overview.IsTextTrimmed,
                    new[] { "OverviewPanel", "PickerPanel", "CompactBlock" }
                        .All(name => Get<StackPanel>(page, name).CornerRadius == default));
                var more = Get<HyperlinkButton>(page, "DetailReadMoreButton");
                Require(more.Visibility == Visibility.Collapsed, "简洁模式仍有阅读更多按钮");
                var tip = ToolTipService.GetToolTip(overview) as ToolTip;
                Require(tip is { Visibility: Visibility.Visible, Content: TextBlock text }
                    && text.Text == overview.Text && text.TextWrapping == TextWrapping.Wrap,
                    "折叠剧情没有保留原生全文提示");
                Require(overview.Text.StartsWith("首字完整。", StringComparison.Ordinal)
                    && overview.TextTrimming == TextTrimming.CharacterEllipsis, "匿名长简介没有保留首字或省略语义");
                var pictureMask = ReadStoreMask(page, "StoreBackdropMask");
                var ambientMask = ReadStoreMask(page, "StoreAmbientMask");
                StoreDetailProbeEvidence.RequireMask(pictureMask);
                StoreDetailProbeEvidence.RequireAmbientMask(ambientMask);
                var ambientBrush = Get<Border>(page, "StoreBackdropSides").Background as ImageBrush;
                Require(ambientBrush?.ImageSource is WriteableBitmap, "多色环境底未挂到实际页面");
                var ambientBitmap = (WriteableBitmap)ambientBrush!.ImageSource;
                var ambientPixels = ambientBitmap.PixelBuffer.ToArray();
                StoreDetailProbeEvidence.RequireAmbient(ambientPixels, ambientBitmap.PixelWidth, ambientBitmap.PixelHeight);
                var reference = new RenderTargetBitmap();
                await reference.RenderAsync(overview);
                var referencePixels = (await reference.GetPixelsAsync()).ToArray();
                var glyphBox = Bounds(overview, _root);
                var scale = _root.XamlRoot.RasterizationScale;
                var screen = await CaptureStoreDetailFrameAsync(width);
                var glyph = StoreDetailProbeEvidence.ReadGlyph(referencePixels, reference.PixelWidth, reference.PixelHeight,
                    screen.Bytes, screen.Width, screen.Height, (int)Math.Round(glyphBox.Left * scale), (int)Math.Round(glyphBox.Top * scale),
                    Math.Min(reference.PixelWidth, (int)Math.Ceiling(overview.FontSize * scale)),
                    Math.Min(reference.PixelHeight, (int)Math.Ceiling(overview.LineHeight * scale)));
                reads.Add(new
                {
                    width,
                    pageWidth = page.ActualWidth,
                    canvasWidth = canvas.ActualWidth,
                    pictureBox,
                    pictureGap,
                    acrylicGap,
                    trailerGap,
                    firstCard = Bounds(firstCard, canvas),
                    cover = Bounds(cover, canvas),
                    details = Bounds(details, canvas),
                    lines = overview.MaxLines,
                    overview.IsTextTrimmed,
                    pictureMask,
                    ambientMask,
                    glyph,
                    screen.Stable,
                    theme = savedTheme,
                    localPlayback = false
                });
                StoreDetailProbeEvidence.RequireGlyph(glyph);
                if (width == 1240) await StoreTrailerClearanceAsync(page, canvas, firstCard, reads);
            }
            fixture.Settings.Ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(page);
            Require(Get<Grid>(page, "StoreBackdrop").Visibility == Visibility.Collapsed
                && !Get<StoreBackdropFade>(page, "_storeBackdropFade").IsActive
                && !Get<StoreBackdropFade>(page, "_storeAmbientFade").IsActive,
                "退出商店模式没有释放两层渐隐");
            fixture.Settings.Ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(page);
            RequireStoreHeroAlignment(page);
            Require(Get<Grid>(page, "StoreBackdrop").Visibility == Visibility.Visible
                && Get<StoreBackdropFade>(page, "_storeBackdropFade").IsActive, "回到商店模式没有恢复背景");
            foreach (var light in new[] { true, false })
            {
                // 只注入有效配色，不改Windows应用模式；真实系统通知验证留在系统主题批次。
                typeof(ThemeHost).GetMethod("ApplyResolved", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static)!.Invoke(null, [UiThemes.System(!light)]);
                await LayoutAsync(page);
                await CaptureStoreDetailFrameAsync(light ? 1241 : 1242);
                if (light)
                    Require(overview.Foreground is SolidColorBrush ink && ink.Color == ThemeHost.ToColor(ThemeHost.Current.Colors.Text)
                        && Get<Button>(page, "ColumnMoreButton").Foreground is SolidColorBrush buttonInk
                        && buttonInk.Color == ThemeHost.ToColor(ThemeHost.Current.Colors.Text), "浅色商店正文或按钮仍使用白墨");
            }
            Require(fixture.Actions.Plays == 0 && !fixture.Transport.Requests.Any(request => request.Contains("/Playing", StringComparison.Ordinal)),
                "商店顶部布局检查意外播放或上报");
            Require(Get<Windows.Media.Playback.MediaPlayer?>(page, "_storeTrailerPlayer") is null
                && Get<StoreYouTubePreview?>(page, "_storeYouTube") is null, "布局占位意外创建预告后端");
        }
        finally
        {
            try { await WritePerformanceEvidenceAsync("store-detail-backdrop.json", new { reads, localPlayback = false, realServer = false }); }
            finally
            {
                StoreDetailProbeEvidence.Cleanup(
                    () => (frame.Content as IShellContent)?.Release(),
                    () => _root.Children.Remove(frame),
                    () => ShellPrefs.ApplyAppearance(new()),
                    () => HomeMotion.ApplyPreference(motion),
                    () => ThemeHost.Apply(savedTheme));
            }
        }
    }

    private static StoreFadeRead ReadStoreMask(DetailPage page, string name)
    {
        var brush = Get<Border>(page, name).Background as ImageBrush;
        Require(brush?.ImageSource is WriteableBitmap, "商店实际遮罩没有位图");
        var bitmap = (WriteableBitmap)brush!.ImageSource;
        return StoreDetailProbeEvidence.ReadMask(bitmap.PixelBuffer.ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private async Task<CompositionPlaybackProbe.ResizeScreenFrame> CaptureStoreDetailFrameAsync(int width)
    {
        var topMost = _window!.TopMost;
        try
        {
            _window.TopMost = true;
            await Task.Delay(300);
            var screen = CompositionPlaybackProbe.CaptureResizeFrame(_window.Handle, System.Diagnostics.Stopwatch.GetTimestamp());
            await screen.SaveAsync(Path.Combine(_options.Paths.LogDirectory, $"store-detail-backdrop-{width}-screen.png"));
            Require(screen.Stable, "商店首字取样过程中客户区发生变化");
            return screen;
        }
        finally { _window.TopMost = topMost; }
    }

    private static async Task StoreTrailerClearanceAsync(DetailPage page, Grid canvas, StackPanel firstCard, List<object> reads)
    {
        // 只注入预告存在元数据并走正式布局函数；SetStorePreviewActive(false)始终禁止创建后端。
        var subject = page.ViewModel.CurrentItem!;
        var previous = subject.LocalTrailerCount;
        var controls = Get<CommandBar>(page, "StoreTrailerControls");
        try
        {
            subject.LocalTrailerCount = 1;
            Call(page.ViewModel, "NotifyStoreRoom");
            Call(page, "LayoutStoreTrailer");
            controls.Visibility = Visibility.Visible;
            // 控制排自己的高要露头之后才量得到，量到会自己再摆一次（SizeChanged）；这里等两拍再读。
            await LayoutAsync(page);
            await LayoutAsync(page);
            // 2026-11-18 用户令「放预告片的时候下面会出现大片空位」：那排键在画框里面，尾部不再替它让位，
            // 卡片的位置与这支预告开没开播无关。
            var flatTail = Math.Max(0, Math.Round(page.ViewModel.StoreBackdropHeight
                + DetailHero.StorePreviewGap - page.ViewModel.HeroHeight));
            Require(Math.Abs(page.ViewModel.TailMinHeight - flatTail) < 0.5,
                $"预告控制键上屏后尾部多留了一段：{page.ViewModel.TailMinHeight:0.##} 对 {flatTail:0.##}");
            var cover = Get<Border>(page, "PosterStill");
            var details = Get<StackPanel>(page, "StoreHeroDetails");
            var picture = Get<Grid>(page, "StoreBackdrop");
            var controlsBox = Bounds(controls, canvas);
            var pictureBox = Bounds(picture, canvas);
            // 右下角（2026-11-18 用户令「移到右下」）：右沿与下沿各比画面再进一个内缩，整排都在画框里。
            Require(controlsBox.Right <= pictureBox.Right - DetailHero.StoreTrailerControlsInset + 1
                && controlsBox.Left >= pictureBox.Left
                && controlsBox.Bottom <= pictureBox.Bottom - DetailHero.StoreTrailerControlsInset + 1
                && controlsBox.Top >= pictureBox.Top,
                $"预告控制排没有落在画框右下角：键 {controlsBox}、画面 {pictureBox}");
            StoreDetailProbeEvidence.RequireClearance(Bounds(cover, canvas).Bottom, Bounds(details, canvas).Top,
                Bounds(details, canvas).Bottom, pictureBox.Bottom, controlsBox.Bottom,
                Bounds(firstCard, canvas).Top, DetailHero.StorePreviewGap);
            reads.Add(new { trailerControlsOnly = true, controlsBox, pictureBox, firstCard = Bounds(firstCard, canvas), page.ViewModel.TailMinHeight });
        }
        finally
        {
            subject.LocalTrailerCount = previous;
            controls.Visibility = Visibility.Collapsed;
            Call(page.ViewModel, "NotifyStoreRoom");
            await LayoutAsync(page);
        }
    }
}
