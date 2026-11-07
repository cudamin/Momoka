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
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private static void RequireDetailBackdropFade(DetailPage page)
    {
        var expected = page.ViewModel.BackdropShown;
        var fade = Get<Border>(page, "BackdropFade");
        Require((fade.Visibility == Visibility.Visible) == expected, "亚克力显隐不符合背景图片状态");
        Require(!Descendants(page).OfType<Border>().Any(border => border.Name == "BackdropShade"), "重复阅读遮罩仍然存在");
        if (!expected) return;
        var picture = Get<Border>(page, "BackdropPicture");
        Require(fade.Parent == picture.Parent
            && fade.Opacity == 1 && Math.Abs(fade.ActualHeight - picture.ActualHeight) < 0.01
            && Math.Abs(fade.ActualWidth - picture.ActualWidth) < 0.01, "亚克力没有独立覆盖实际图片盒子");
        var ink = ExpectedBackdropBaseColor(page);
        Require(fade.Background is AcrylicBrush acrylic
            && acrylic.TintColor == ink && acrylic.FallbackColor == ink
            && acrylic.TintOpacity == 0.55 && acrylic.TintLuminosityOpacity == 0.55
            && acrylic.Opacity == page.ViewModel.BackdropStrength
            && acrylic.AlwaysUseFallback == (page.ViewModel.BackdropStrength == 1),
            "背景亚克力没有使用主图均色、当前不透明度或完全不透明回退");
        var edge = Get<Border>(page, "BackdropEdgeFade");
        Require((edge.Visibility == Visibility.Visible) == !page.ViewModel.Showcase, "有背景时收边只应在窄窗紧凑布局生效");
        if (!page.ViewModel.Showcase)
        {
            Require(edge.Parent == picture.Parent && Math.Abs(edge.ActualHeight - picture.ActualHeight) < 0.01,
                "收边未随实际图片高度缩放");
            var stops = ((LinearGradientBrush)edge.Background).GradientStops;
            var color = ThemeHost.ToColor(ThemeHost.Current.Colors.Window);
            Require(stops.Count > 16 && stops[0].Color.A == 0 && Math.Abs(stops[1].Offset - 0.8) < 0.000001
                && stops[1].Color.A == 0 && stops[^1].Offset == 1 && stops[^1].Color == color,
                $"图片最后20%未从透明平滑收向主题底色：count={stops.Count}、起点={stops[1].Offset}/{stops[1].Color}、末点={stops[^1].Offset}/{stops[^1].Color}、主题={color}");
            Require(stops.All(stop => stop.Color.R == color.R && stop.Color.G == color.G && stop.Color.B == color.B),
                "渐变中间混入了非主题颜色");
        }
        Require(Get<Grid>(page, "HeroBand").Background is null or SolidColorBrush { Color.A: 0 }
            && Get<StackPanel>(page, "HeroTail").Background is null or SolidColorBrush { Color.A: 0 },
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
        // 非16:9图覆盖首次解码比例从兜底切到真实值的时序。
        var image = await BackdropEdgeImageAsync();
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
            foreach (var type in new[] { EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Movie, EmbyItemType.Episode })
            {
                item.Type = type;
                ResizeProbeClient(760, 820);
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item)), "渐变详情导航失败");
                var page = (DetailPage)frame.Content;
                page.AttachWindow(_window);
                await UntilAsync(() => page.IsReady && page.ViewModel.HeroImage is not null && page.ViewModel.GaussianBackdropReady);
                var originalImage = new BitmapImage();
                using (var stream = new MemoryStream(image).AsRandomAccessStream()) await originalImage.SetSourceAsync(stream);
                var original = await ReadBackdropPixelsAsync(originalImage);
                var rendered = await ReadBackdropPixelsAsync(page.ViewModel.HeroImage!);
                Require(original.Width == rendered.Width && original.Height == rendered.Height,
                    "底边模糊改变了解码尺寸");
                Require(original.Pixels.AsSpan().SequenceEqual(rendered.Pixels),
                    "窄窗原图仍有局部接缝模糊或全图高斯");
                var baseline = reads;
                var originalPicture = page.ViewModel.HeroImage;
                foreach (var theme in UiThemes.All)
                    foreach (var width in new[] { 760, 1280 })
                        foreach (var strength in new[] { 0d, 20, 100 })
                        {
                            ResizeProbeClient(width, 1120);
                            ThemeHost.Apply(theme);
                            fixture.Settings.Ui.BackdropOpacity = strength;
                            fixture.Settings.Ui.EnableImmersive = true;
                            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                            await LayoutAsync(page);
                            await UntilAsync(() => Math.Abs(page.ViewModel.PageWidth - width) < 1);
                            Require(page.ViewModel.Showcase == (width >= DetailHero.CompactFloor)
                                && page.ViewModel.BackdropShown, "开启时宽窄背景布局不符合当前尺寸");
                            Require(ReferenceEquals(originalPicture, page.ViewModel.HeroImage)
                                && !page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady,
                                "固定亚克力重新处理了原图");
                            RequireDetailBackdropFade(page);
                            RequireFixedDetailForeground(page);
                            await CheckBackdropFadePixelsAsync(page);
                            await SaveProbeScreenAsync($"backdrop-acrylic-{type}-{theme.Id}-{width}-{strength:0}-screen.png");
                        }
                Require(reads == baseline, "切主题或背景不透明度重新请求了媒体或图片");
                fixture.Settings.Ui.BackdropOpacity = 20;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                var fade = Get<Border>(page, "BackdropFade");
                var brush = (AcrylicBrush)fade.Background;
                var last = brush.Opacity;
                var lastColor = brush.TintColor;
                try
                {
                    brush.Opacity = 0.91;
                    var rejected = false;
                    try { RequireDetailBackdropFade(page); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "背景检查没有拒绝错误不透明度反例");
                    brush.Opacity = last;
                    brush.TintColor = Microsoft.UI.Colors.Lime;
                    rejected = false;
                    try { RequireDetailBackdropFade(page); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "背景检查没有拒绝错误主图底色反例");
                }
                finally { brush.Opacity = last; brush.TintColor = lastColor; }

                foreach (var width in new[] { 760, 1280 })
                {
                    ResizeProbeClient(width, 900);
                    await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - width) < 1; });
                    fixture.Settings.Ui.EnableImmersive = false;
                    ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                    await LayoutAsync(page);
                    Require(!page.ViewModel.BackdropShown && !page.ViewModel.Showcase
                        && Get<Grid>(page, "Backdrop").Visibility == Visibility.Collapsed,
                        "关闭沉浸后宽窄背景没有整层收起");
                    RequireDetailBackdropFade(page);
                    fixture.Settings.Ui.EnableImmersive = true;
                    ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                    await LayoutAsync(page);
                    Require(page.ViewModel.BackdropShown && page.ViewModel.Showcase == (width >= DetailHero.CompactFloor),
                        "恢复沉浸后宽窄背景没有重新出现");
                    RequireDetailBackdropFade(page);
                }
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

    private static Windows.UI.Color ExpectedBackdropBaseColor(DetailPage page)
    {
        var picture = page.ViewModel.BackdropPixels;
        Require(picture is not null, "主图均色检查缺少已解码的原图像素");
        var samples = picture!;
        var sums = new int[3];
        for (var row = 0; row < 5; row++)
            for (var column = 0; column < 8; column++)
            {
                var x = (int)Math.Round(Math.Clamp(samples.Width * ((column + 0.5) / 8), 0, samples.Width - 1),
                    MidpointRounding.AwayFromZero);
                var y = (int)Math.Round(Math.Clamp(samples.Height * ((row + 0.5) / 5), 0, samples.Height - 1),
                    MidpointRounding.AwayFromZero);
                var at = (y * samples.Width + x) * 4;
                for (var channel = 0; channel < 3; channel++) sums[channel] += samples.Pixels[at + channel];
            }
        return Windows.UI.Color.FromArgb(255, (byte)Math.Round(sums[2] / 40d),
            (byte)Math.Round(sums[1] / 40d), (byte)Math.Round(sums[0] / 40d));
    }

    private async Task CheckBackdropFadePixelsAsync(DetailPage page)
    {
        var backdrop = Get<Grid>(page, "Backdrop");
        var picture = Get<Border>(page, "BackdropPicture");
        var fade = Get<Border>(page, "BackdropFade");
        var image = picture.Background;
        Require(fade.Background is AcrylicBrush, "像素检查缺少实际亚克力");
        var glass = (AcrylicBrush)fade.Background;
        var previousFallback = glass.AlwaysUseFallback;
        var previousOpacity = glass.Opacity;
        try
        {
            // RTB不合成实时亚克力；只强制实际刷子的实色回退验证Tint来源与渐变几何，实时材质另看屏幕截图。
            picture.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
            glass.AlwaysUseFallback = true;
            glass.Opacity = previousOpacity == 0 ? 0 : 1;
            await LayoutAsync(page);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(backdrop);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var scale = page.XamlRoot.RasterizationScale;
            Require(Math.Abs(bitmap.PixelWidth - picture.ActualWidth * scale) <= 1
                && Math.Abs(bitmap.PixelHeight - picture.ActualHeight * scale) <= 1,
                "背景返帧绘制边界与图片不一致，不能按图片坐标采样");
            var x = bitmap.PixelWidth * 3 / 4;
            var baseColor = ExpectedBackdropBaseColor(page);
            var baseChannels = new[] { baseColor.B, baseColor.G, baseColor.R };
            var theme = ThemeHost.ToColor(ThemeHost.Current.Colors.Window);
            var themeChannels = new[] { theme.B, theme.G, theme.R };
            var clear = glass.Opacity == 0;
            foreach (var along in new[] { 0.6, 0.8, 0.85, 0.9, 0.94, 0.96, 0.995 })
            {
                var y = Math.Clamp((int)(picture.ActualHeight * scale * along), 0, bitmap.PixelHeight - 1);
                var at = (y * bitmap.PixelWidth + x) * 4;
                var t = page.ViewModel.Showcase ? 0 : Math.Clamp((along - 0.8) / 0.2, 0, 1);
                var edgeAlpha = t * t * (3 - 2 * t);
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = (clear ? 255 : baseChannels[channel]) * (1 - edgeAlpha) + themeChannels[channel] * edgeAlpha;
                    Require(Math.Abs(pixels[at + channel] - value) < 5,
                        $"亚克力实色回退/收边像素 {along:0.###}/{channel}={pixels[at + channel]}，预期{value:0.##}");
                }
            }
            File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "backdrop-fade-pixels.txt"),
                $"fallback-only; type={page.ViewModel.ItemType}; tint={baseColor}; clear={clear}; size={bitmap.PixelWidth}x{bitmap.PixelHeight}\n");
        }
        finally { picture.Background = image; glass.AlwaysUseFallback = previousFallback; glass.Opacity = previousOpacity; }
        await LayoutAsync(page);
    }

    private static async Task<byte[]> BackdropEdgeImageAsync()
    {
        const int width = 1200, height = 900;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                // 全图高频纹理，尤其最下沿；原来的夹具下半是纯色，无法见证局部高斯。
                var bright = (x / 8 + y / 8) % 2 == 0;
                pixels[at] = (byte)(bright ? 220 : 50);
                pixels[at + 1] = (byte)(bright ? 185 : 85);
                pixels[at + 2] = (byte)(bright ? 245 : 100);
                pixels[at + 3] = 255;
            }
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
