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
        var expected = page.ViewModel.BackdropShown && !page.ViewModel.Showcase
            && page.ViewModel.ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season;
        var fade = Get<Border>(page, "BackdropFade");
        Require((fade.Visibility == Visibility.Visible) == expected, "底部渐变显隐不符合页面及沉浸模式");
        Require(!Descendants(page).OfType<Border>().Any(border => border.Name == "BackdropShade"), "重复阅读遮罩仍然存在");
        if (!expected) return;
        var picture = Get<Border>(page, "BackdropPicture");
        Require(fade.Parent == picture.Parent
            && fade.Opacity == 1 && Math.Abs(fade.ActualHeight - picture.ActualHeight) < 0.01
            && Math.Abs(fade.ActualWidth - picture.ActualWidth) < 0.01, "渐变没有独立覆盖实际图片盒子");
        var ink = DetailHero.ScrimInk;
        Require(fade.Background is SolidColorBrush brush
            && brush.Color.A == (page.ViewModel.GaussianBackdropApplied ? 0 : ScrimExpected(page))
            && (brush.Color.A == 0 || brush.Color.R == ink.R && brush.Color.G == ink.G && brush.Color.B == ink.B),
            "背景应只保留原均匀阅读遮罩，不得再画下沿渐变");
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
                await UntilAsync(() => page.IsReady && page.ViewModel.HeroImage is not null && page.ViewModel.GaussianBackdropReady);
                var originalImage = new BitmapImage();
                using (var stream = new MemoryStream(image).AsRandomAccessStream()) await originalImage.SetSourceAsync(stream);
                var original = await ReadBackdropPixelsAsync(originalImage);
                var rendered = await ReadBackdropPixelsAsync(page.ViewModel.HeroImage!);
                Require(original.Width == rendered.Width && original.Height == rendered.Height,
                    "底边模糊改变了解码尺寸");
                var clearLength = (int)Math.Floor(rendered.Height * 0.95) * rendered.Width * 4;
                Require(original.Pixels.AsSpan(0, clearLength).SequenceEqual(rendered.Pixels.AsSpan(0, clearLength)),
                    "底边模糊污染了上方95%画面");
                Require(!original.Pixels.AsSpan(clearLength).SequenceEqual(rendered.Pixels.AsSpan(clearLength)),
                    "图片下沿没有实际高斯模糊");
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
                foreach (var strength in new[] { 0d, 40, 100 })
                {
                    fixture.Settings.Ui.UseGaussianBackdrop = false;
                    fixture.Settings.Ui.BackdropOpacity = strength;
                    ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                    await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !page.ViewModel.GaussianBackdropApplied);
                    await LayoutAsync(page);
                    RequireDetailBackdropFade(page);
                    await CheckBackdropFadePixelsAsync(page);
                    await SaveProbeScreenAsync($"backdrop-fade-{type}-shade-{strength:0}-screen.png");
                }
                fixture.Settings.Ui.BackdropOpacity = 40;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                var fade = Get<Border>(page, "BackdropFade");
                var brush = (SolidColorBrush)fade.Background;
                var last = brush.Color;
                try
                {
                    brush.Color = Windows.UI.Color.FromArgb(10, last.R, last.G, last.B);
                    var rejected = false;
                    try { RequireDetailBackdropFade(page); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "背景检查没有拒绝错误遮罩浓度反例");
                }
                finally { brush.Color = last; }

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
        try
        {
            // 图本身是满不透明的一层（滑块只管图上那道遮罩），换成白色当底，像素预期才只剩遮罩与收边两道。
            picture.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
            await LayoutAsync(page);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(backdrop);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
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
            foreach (var along in new[] { 0.6, 0.8, 0.94, 0.96, 0.995 })
            {
                var y = Math.Clamp((int)(picture.ActualHeight * scale * along), 0, bitmap.PixelHeight - 1);
                var at = (y * bitmap.PixelWidth + x) * 4;
                var ink = DetailHero.ScrimInk;
                var alpha = page.ViewModel.GaussianBackdropApplied ? 0
                    : AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, page.ViewModel.BackdropStrength) / 255d;
                var expected = new[] { ink.B, ink.G, ink.R };
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = 255 * (1 - alpha) + expected[channel] * alpha;
                    Require(Math.Abs(pixels[at + channel] - value) < 5,
                        $"渐变真实像素 {along:0.###}/{channel}={pixels[at + channel]}，预期{value:0.##}");
                }
            }
        }
        finally { picture.Background = image; }
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
