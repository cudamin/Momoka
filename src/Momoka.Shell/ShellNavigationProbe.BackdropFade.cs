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
        var expected = page.ViewModel.BackdropShown
            && (page.ViewModel.Showcase
                || page.ViewModel.ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season);
        var fade = Get<Border>(page, "BackdropFade");
        Require((fade.Visibility == Visibility.Visible) == expected, "底部渐变显隐不符合页面及沉浸模式");
        Require(!Descendants(page).OfType<Border>().Any(border => border.Name == "BackdropShade"), "重复阅读遮罩仍然存在");
        if (!expected) return;
        var picture = Get<Border>(page, "BackdropPicture");
        Require(fade.Parent == picture.Parent
            && fade.Opacity == 1 && Math.Abs(fade.ActualHeight - picture.ActualHeight) < 0.01
            && Math.Abs(fade.ActualWidth - picture.ActualWidth) < 0.01, "渐变没有独立覆盖实际图片盒子");
        // 背景固定亚克力：三项参数直接读设置，没有第四格浓度，也没有颜色遮罩和高斯两条老路。
        var ink = ShellPrefs.AppearanceColor(true, page.ViewModel.AppearanceSettings);
        var tint = ThemeHost.ToColor(ink);
        tint.A = byte.MaxValue;
        Require(fade.Background is AcrylicBrush acrylic
            && acrylic.TintColor == tint
            && acrylic.FallbackColor == tint
            && Math.Abs(acrylic.TintOpacity - page.ViewModel.BackdropTintOpacity) < 0.000001
            && acrylic.TintLuminosityOpacity is { } luminosity
            && Math.Abs(luminosity - page.ViewModel.BackdropTintLuminosityOpacity) < 0.000001
            && acrylic.Opacity == 1
            && !acrylic.AlwaysUseFallback,
            "背景亚克力没有按三项参数落在图上");
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
            foreach (var type in new[] { EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Movie })
            {
                item.Type = type;
                ResizeProbeClient(760, 820);
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item)), "渐变详情导航失败");
                var page = (DetailPage)frame.Content;
                page.AttachWindow(_window);
                await UntilAsync(() => page.IsReady && page.ViewModel.HeroImage is not null);
                var originalImage = new BitmapImage();
                using (var stream = new MemoryStream(image).AsRandomAccessStream()) await originalImage.SetSourceAsync(stream);
                var original = await ReadBackdropPixelsAsync(originalImage);
                var rendered = await ReadBackdropPixelsAsync(page.ViewModel.HeroImage!);
                Require(original.Width == rendered.Width && original.Height == rendered.Height,
                    "底边模糊改变了解码尺寸");
                Require(original.Pixels.AsSpan().SequenceEqual(rendered.Pixels),
                    "窄窗原图仍有局部接缝模糊或全图高斯");

                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                foreach (var theme in UiThemes.All)
                    foreach (var width in new[] { 760, 1280 })
                    {
                        ResizeProbeClient(width, 1120);
                        ThemeHost.Apply(theme);
                        await LayoutAsync(page);
                        await UntilAsync(() => Math.Abs(page.ViewModel.PageWidth - width) < 1);
                        Require(page.ViewModel.Showcase == (width >= DetailHero.CompactFloor)
                            && page.ViewModel.BackdropShown, "开启时宽窄背景布局不符合当前尺寸");
                        RequireDetailBackdropFade(page);
                        await SaveProbeScreenAsync($"backdrop-fade-{type}-{theme.Id}-{width}-screen.png");
                    }

                // 三项参数的三个端点：0/0 什么都不盖、45 是中间档、100/100 盖上实色。
                var samples = new List<(byte R, byte G, byte B)>();
                foreach (var (tintOpacity, luminosityOpacity, name) in new[]
                {
                    (0d, 0d, "clear"), (0.45, 0.45, "half"), (1d, 1d, "solid")
                })
                {
                    ResizeProbeClient(1280, 1120);
                    fixture.Settings.Ui.BackdropTintOpacity = tintOpacity * 100;
                    fixture.Settings.Ui.BackdropTintLuminosityOpacity = luminosityOpacity * 100;
                    ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                    await LayoutAsync(page);
                    RequireDetailBackdropFade(page);
                    samples.Add(await SampleBackdropAsync(page));
                    await SaveProbeScreenAsync($"backdrop-tint-{type}-{name}-screen.png");
                }
                var ink = ShellPrefs.AppearanceColor(true, page.ViewModel.AppearanceSettings);
                Require(samples[0].R > 248 && samples[0].G > 248 && samples[0].B > 248,
                    $"0/0 仍然盖住了白底：{samples[0]}");
                Require(Math.Abs(samples[^1].R - ink.R) <= 4 && Math.Abs(samples[^1].G - ink.G) <= 4
                    && Math.Abs(samples[^1].B - ink.B) <= 4, $"100/100 没有盖上染色本身：{samples[^1]}，染色 {ink}");
                static int Sum((byte R, byte G, byte B) value) => value.R + value.G + value.B;
                Require(Sum(samples[0]) > Sum(samples[1]) && Sum(samples[1]) > Sum(samples[^1]),
                    "染色浓度提高没有让画面更实：" + string.Join(" → ", samples.Select(Sum)));

                // 反例：把画笔的染色浓度改成设置里没有的数，检查必须拒绝。
                fixture.Settings.Ui.BackdropTintOpacity = 55;
                fixture.Settings.Ui.BackdropTintLuminosityOpacity = 55;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                var fade = Get<Border>(page, "BackdropFade");
                var brush = (AcrylicBrush)fade.Background;
                var savedTint = brush.TintOpacity;
                try
                {
                    brush.TintOpacity = 0.9;
                    var rejected = false;
                    try { RequireDetailBackdropFade(page); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "背景检查没有拒绝错误染色浓度反例");
                }
                finally { brush.TintOpacity = savedTint; }

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

    /// <summary>
    /// 白底上取背景中段那一个点的颜色 —— 收边还没开始的位置（60% 高）。
    /// <para>
    /// 亚克力的合成由合成器完成，像素级复刻它的混合公式没有意义；这里只钉两端与顺序：0/0 时白底照旧是白的
    /// （材料什么都不盖），1/1 时同一个点变成那块染色本身（材料盖实了），中间档落在两者之间。
    /// </para>
    /// </summary>
    private async Task<(byte R, byte G, byte B)> SampleBackdropAsync(DetailPage page)
    {
        var backdrop = Get<Grid>(page, "Backdrop");
        var picture = Get<Border>(page, "BackdropPicture");
        var image = picture.Background;
        try
        {
            picture.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
            await LayoutAsync(page);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(backdrop);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var scale = page.XamlRoot.RasterizationScale;
            Require(Math.Abs(bitmap.PixelWidth - picture.ActualWidth * scale) <= 1
                && Math.Abs(bitmap.PixelHeight - picture.ActualHeight * scale) <= 1,
                "背景返帧绘制边界与图片不一致，不能按图片坐标采样");
            var x = bitmap.PixelWidth * 3 / 4;
            var y = Math.Clamp((int)(picture.ActualHeight * scale * 0.6), 0, bitmap.PixelHeight - 1);
            var at = (y * bitmap.PixelWidth + x) * 4;
            Require(pixels[at + 3] == 255, "背景返帧不是不透明的一帧");
            var sample = (R: pixels[at + 2], G: pixels[at + 1], B: pixels[at]);
            File.AppendAllLines(Path.Combine(_options.Paths.LogDirectory, "backdrop-fade-pixels.txt"),
            [
                $"bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight}; picture={picture.ActualWidth}x{picture.ActualHeight}; "
                + $"raster={scale}; tint={page.ViewModel.BackdropTintOpacity:0.###}/{page.ViewModel.BackdropTintLuminosityOpacity:0.###}; "
                + $"coverage={AppearanceOpacity.Coverage(page.ViewModel.BackdropTintOpacity, page.ViewModel.BackdropTintLuminosityOpacity):0.###}; "
                + $"rgb={sample.R},{sample.G},{sample.B}"
            ]);
            return sample;
        }
        finally { picture.Background = image; }
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
