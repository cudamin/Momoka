using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private static void RequireDetailBackdropFade(DetailPage page)
    {
        var expected = page.ViewModel.BackdropShown && !page.ViewModel.Showcase
            && page.ViewModel.ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season;
        var fade = Get<Border>(page, "BackdropFade");
        var shade = Get<Border>(page, "BackdropShade");
        Require((fade.Visibility == Visibility.Visible) == expected, "底部渐变显隐不符合页面及沉浸模式");
        Require((shade.Visibility == Visibility.Visible) == (expected && !page.ViewModel.GaussianBackdropApplied),
            "固定阅读遮罩显隐错误");
        if (!expected) return;
        var picture = Get<Border>(page, "BackdropPicture");
        var color = ((SolidColorBrush)Get<Border>(page, "BackdropThemeSource").Background).Color;
        Require(fade.Parent == picture.Parent && shade.Parent == picture.Parent
            && fade.Opacity == 1 && Math.Abs(fade.ActualHeight - picture.ActualHeight) < 0.01
            && Math.Abs(fade.ActualWidth - picture.ActualWidth) < 0.01, "渐变没有独立覆盖实际图片盒子");
        Require(fade.Background is LinearGradientBrush brush
            && brush.StartPoint == new Windows.Foundation.Point(0.5, 0)
            && brush.EndPoint == new Windows.Foundation.Point(0.5, 1)
            && brush.GradientStops.Count == 3
            && brush.GradientStops[0].Offset == 0 && brush.GradientStops[0].Color.A == 0
            && Math.Abs(brush.GradientStops[1].Offset - 0.7) < 0.000001 && brush.GradientStops[1].Color.A == 0
            && brush.GradientStops[2].Offset == 1 && brush.GradientStops[2].Color.A == 255
            && brush.GradientStops.All(stop => stop.Color.R == color.R && stop.Color.G == color.G && stop.Color.B == color.B),
            $"渐变范围或主题末色不正确：主题{color}；"
            + (fade.Background is LinearGradientBrush actual
                ? $"方向{actual.StartPoint}→{actual.EndPoint}；{string.Join(';', actual.GradientStops.Select(stop => $"{stop.Offset:R}:{stop.Color}"))}"
                : "不是渐变画刷"));
        Require(Get<Grid>(page, "HeroBand").Background is SolidColorBrush { Color.A: 0 }
            && Get<StackPanel>(page, "HeroTail").Background is SolidColorBrush { Color.A: 0 },
            "滚动内容再次压暗主题收边");
    }

    private static void RequireHomeWithoutBottomFade(HomeBanner banner) =>
        Require(Get<Grid>(banner, "Band").Children.OfType<Border>()
            .All(border => border.VerticalAlignment != VerticalAlignment.Bottom), "主页仍有底部渐变");

    private async Task BackdropFadeAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        if (_window!.IsMaximized) _window.ToggleMaximize();
        var image = await FixtureImageAsync(1600, 900);
        var item = Series();
        item.Name = "主题渐变 · 离线背景验证";
        item.BackdropImageTags = ["fixture"];
        var reads = 0;
        fixture.Transport.Reply = (request, _) =>
        {
            reads++;
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) }
                : path.EndsWith("/Items/" + item.Id, StringComparison.Ordinal)
                    ? Json(item) : FakeTransport.Standard(request));
        };
        var frame = new Frame { Margin = new Thickness(0, 40, 0, 0) };
        var savedTheme = ThemeHost.Current;
        var motion = HomeMotion.PreferenceEnabled;
        _root.Children.Add(frame);
        HomeMotion.ApplyPreference(false);
        try
        {
            foreach (var type in new[] { EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Movie })
            {
                item.Type = type;
                ResizeProbeClient(760, 820);
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item)), "渐变详情导航失败");
                var page = (DetailPage)frame.Content;
                page.AttachWindow(_window);
                await UntilAsync(() => page.IsReady && page.ViewModel.HeroImage is not null && page.ViewModel.GaussianBackdropReady);
                foreach (var gaussian in new[] { false, true })
                {
                    fixture.Settings.Ui.UseGaussianBackdrop = gaussian;
                    fixture.Settings.Ui.AlwaysImmersive = false;
                    ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                    await UntilAsync(() => page.ViewModel.GaussianBackdropReady && page.ViewModel.GaussianBackdropApplied == gaussian);
                    var baseline = reads;
                    foreach (var theme in UiThemes.All)
                    {
                        ThemeHost.Apply(theme);
                        await LayoutAsync(page);
                        RequireDetailBackdropFade(page);
                        await CheckBackdropFadePixelsAsync(page);
                        await SaveProbeScreenAsync($"backdrop-fade-{type}-{gaussian}-{theme.Id}-screen.png");
                    }
                    Require(reads == baseline, "切主题重新请求了媒体或图片");
                }
                var fade = Get<Border>(page, "BackdropFade");
                var brush = (LinearGradientBrush)fade.Background;
                var last = brush.GradientStops[^1].Color;
                try
                {
                    brush.GradientStops[^1].Color = Windows.UI.Color.FromArgb(10, last.R, last.G, last.B);
                    var rejected = false;
                    try { RequireDetailBackdropFade(page); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "渐变检查没有拒绝透明末端反例");
                }
                finally { brush.GradientStops[^1].Color = last; }

                foreach (var gaussian in new[] { false, true })
                {
                    ResizeProbeClient(1100, 1120);
                    await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
                    fixture.Settings.Ui.AlwaysImmersive = true;
                    fixture.Settings.Ui.UseGaussianBackdrop = gaussian;
                    ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                    await UntilAsync(() => page.ViewModel.GaussianBackdropReady && page.ViewModel.GaussianBackdropApplied == gaussian);
                    await LayoutAsync(page);
                    Require(page.ViewModel.Showcase, "未进入固定沉浸");
                    RequireDetailBackdropFade(page);
                    await SaveProbeScreenAsync($"backdrop-fade-{type}-{gaussian}-immersive-screen.png");
                    ResizeProbeClient(760, 820);
                    await UntilAsync(() => { page.UpdateLayout(); return page.ViewModel.IsCompact; });
                    await LayoutAsync(page);
                    RequireDetailBackdropFade(page);
                }
                fixture.Settings.Ui.UseGaussianBackdrop = false;
                fixture.Settings.Ui.AlwaysImmersive = false;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !page.ViewModel.GaussianBackdropApplied);
                await ScrollProbeAsync(Get<ScrollView>(page, "Body"), Get<ScrollView>(page, "Body").ScrollableHeight);
                var seal = page.BodySeal();
                Require(seal.Ok, seal.Detail);
                var wash = page.WashRead(null);
                Require(wash.Ok, wash.Detail);
                await SaveProbeScreenAsync($"backdrop-fade-{type}-scrolled-screen.png");
                page.Release();
            }
            item.Type = EmbyItemType.Movie;
            _root.Children.Remove(frame);
            await CheckHomeBackgroundOpacityAsync(fixture, item, () => reads);
            Require(fixture.Actions.Plays == 0, "渐变检查意外请求播放");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ThemeHost.Apply(savedTheme);
            HomeMotion.ApplyPreference(motion);
            ResizeInspect(1420, 980);
        }
    }

    private async Task CheckBackdropFadePixelsAsync(DetailPage page)
    {
        var backdrop = Get<Grid>(page, "Backdrop");
        var picture = Get<Border>(page, "BackdropPicture");
        var image = picture.Background;
        var opacity = backdrop.Opacity;
        try
        {
            // 像素预期按「图满不透明」算，所以连背景整层那一档一起放到 1；收尾两处都写回去。
            backdrop.Opacity = 1;
            picture.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
            await LayoutAsync(page);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(backdrop);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var color = ((SolidColorBrush)Get<Border>(page, "BackdropThemeSource").Background).Color;
            // RTB 使用绘制边界：Backdrop 布局高851时，图和渐变仅428高，返帧就是428高。
            // 不能由返帧高/容器布局高推缩放，否则85%误采到图的43%。
            var scale = page.XamlRoot.RasterizationScale;
            Require(Math.Abs(bitmap.PixelWidth - picture.ActualWidth * scale) <= 1
                && Math.Abs(bitmap.PixelHeight - picture.ActualHeight * scale) <= 1,
                "背景返帧绘制边界与图片不一致，不能按图片坐标采样");
            var x = bitmap.PixelWidth * 3 / 4;
            var geometry = $"bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}; backdrop={backdrop.ActualWidth}x{backdrop.ActualHeight}; "
                + $"picture={picture.ActualWidth}x{picture.ActualHeight}; raster={page.XamlRoot.RasterizationScale}; "
                + $"pictureAt={picture.TransformToVisual(backdrop).TransformPoint(default)}; fadeAt={Get<Border>(page, "BackdropFade").TransformToVisual(backdrop).TransformPoint(default)}";
            File.AppendAllLines(Path.Combine(_options.Paths.LogDirectory, "backdrop-fade-pixels.txt"),
                new[] { geometry }.Concat(Enumerable.Range(0, 21).Select(step =>
                {
                    var row = Math.Min(bitmap.PixelHeight - 1, bitmap.PixelHeight * step / 20);
                    var at = (row * bitmap.PixelWidth + x) * 4;
                    return $"y={row}: {pixels[at + 2]},{pixels[at + 1]},{pixels[at]},{pixels[at + 3]}";
                })));
            foreach (var along in new[] { 0.6, 0.85, 0.995 })
            {
                var y = Math.Clamp((int)(picture.ActualHeight * scale * along), 0, bitmap.PixelHeight - 1);
                var at = (y * bitmap.PixelWidth + x) * 4;
                var amount = Math.Clamp((along - 0.7) / 0.3, 0, 1);
                var ink = DetailHero.ScrimInk;
                var alpha = page.ViewModel.GaussianBackdropApplied ? 0 : 184 / 255d;
                var expected = new[] { (ink.B, color.B), (ink.G, color.G), (ink.R, color.R) };
                for (var channel = 0; channel < 3; channel++)
                {
                    var under = 255 * (1 - alpha) + expected[channel].Item1 * alpha;
                    var value = under * (1 - amount) + expected[channel].Item2 * amount;
                    Require(Math.Abs(pixels[at + channel] - value) < 5,
                        $"渐变真实像素 {along:0.###}/{channel}={pixels[at + channel]}，预期{value:0.##}");
                }
            }
        }
        finally { picture.Background = image; backdrop.Opacity = opacity; }
        await LayoutAsync(page);
    }
}
