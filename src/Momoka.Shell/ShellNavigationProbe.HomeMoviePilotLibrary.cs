using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeMoviePilotLibraryAsync()
    {
        var animations = HomeMotion.PreferenceEnabled;
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        foreach (var id in server.Items.Keys)
            server.ImageReplies["https://poster.invalid/" + id + ".png"] = await LibraryCoverFixtureAsync(id);
        using var fixture = CreateFixture(server);
        var artwork = await FixtureImageAsync(1280, 720);
        var movie = new EmbyItem { Id = "home-movie", Name = "离线影片", Type = EmbyItemType.Movie, BackdropImageTags = ["backdrop"] };
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Views", StringComparison.Ordinal)) return Task.FromResult(Json(new
            {
                Items = new[] { new EmbyItem { Id = "library", Name = "电影", Type = EmbyItemType.CollectionFolder, CollectionType = "movies", ImageTags = new() { ["Primary"] = "poster" } } }
            }));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal) || path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>() }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(new[] { movie }));
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(artwork) });
            return Task.FromResult(FakeTransport.Standard(request));
        };
        // Login caches the directory; install the reply before either account is restored.
        await fixture.SelectAsync();
        await fixture.SelectAsync("b.invalid");
        fixture.Settings.Ui.AnimationsEnabled = false;
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        try
        {
            HomeMotion.ApplyPreference(false);
            ResizeProbeClient(1280, 900);
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            var home = (HomePage)shell.Pages.Content;
            Require(home.ViewModel.LibraryShelf?.Cards.All(card => !card.IsNavigation) != false, "未连接时显示了 MP 库卡");
            await ConnectMoviePilotAsync(fixture);
            // 2026-11-11 起主页的 MP 订阅库是两张分类导航卡（订阅·电影／订阅·电视剧），
            // 不再是旧的那一张「MP 订阅库」汇总卡；点开落在对应分类页，那一页立订阅墙。
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.Count(card => card.IsNavigation) == 2);
            var ordinary = home.ViewModel.LibraryShelf!.Cards.Where(card => !card.IsNavigation).ToArray();
            Require(ordinary.Length == 2, "两来源媒体库夹具没有生效");
            var navigation = home.ViewModel.LibraryShelf.Cards.Where(card => card.IsNavigation).ToArray();
            Require(navigation.Length == 2
                && navigation.Any(card => card.Item.Id == HomeLayout.MoviePilotMovies
                    && card.Title == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies))
                && navigation.Any(card => card.Item.Id == HomeLayout.MoviePilotSeries
                    && card.Title == HomeLayout.FixedTitle(HomeLayout.MoviePilotSeries))
                && navigation.All(card => card.Scope is null && card.Item.Source is null
                    && card.PlayVisibility == Visibility.Collapsed && card.MoreVisibility == Visibility.Collapsed),
                "主页订阅分类卡不是两张纯导航卡");
            var card = navigation.Single(item => item.Item.Id == HomeLayout.MoviePilotMovies);
            await card.EnsurePosterAsync(Task.CompletedTask);
            Require(card.Poster is not null, "订阅入口没有合成封面");
            await CheckLibraryCoverPixelsAsync(card.Poster!, 3);
            await CheckLibraryCoverLoaderAsync(server);
            var cardRemovals = 0;
            home.ViewModel.LibraryShelf.Cards.CollectionChanged += (_, args) =>
            {
                if (args.OldItems is null) return;
                foreach (var old in args.OldItems)
                    if (navigation.Any(item => ReferenceEquals(item, old))) cardRemovals++;
            };
            await home.ViewModel.BackgroundRefreshAsync();
            Require(cardRemovals == 0, "刷新拆掉了 MP 库卡");
            foreach (var placement in new[] { HomeLibraryPlacement.BelowCarousel, HomeLibraryPlacement.OnCarousel })
                foreach (var width in new[] { 1280, 760 })
                {
                    fixture.Settings.Ui.HomeLibraryPlacement = placement;
                    ResizeProbeClient(width, 900);
                    Call(home, "UpdateLibraryOverlay");
                    await LayoutAsync(home);
                    var holder = placement == HomeLibraryPlacement.OnCarousel ? (FrameworkElement)Get<Border>(home, "LibraryOverlay") : home;
                    var strip = Descendants(holder).OfType<ShelfStrip>().First(strip => ReferenceEquals(strip.ItemsSource, home.ViewModel.LibraryShelf.Cards));
                    strip.FocusOn(home.ViewModel.LibraryShelf.Cards.Count - 1);
                    await LayoutAsync(strip);
                    var poster = Descendants(strip).OfType<PosterCard>().Single(poster => ReferenceEquals(poster.Card, card));
                    Require(poster.ActualWidth > 0 && Bounds(poster, strip).Left >= -1
                        && Bounds(poster, strip).Right <= strip.ActualWidth + 1, "MP 库卡被截断");
                    var caption = Get<Border>(poster, "CaptionSurface");
                    Require(poster.OnScrim == (placement == HomeLibraryPlacement.OnCarousel)
                        && caption.Background is SolidColorBrush background
                        && background.Color.A == 0 && caption.Padding == new Thickness(0), "标题仍有实色底板或额外内边距");
                    if (poster.OnScrim)
                    {
                        var layer = Get<Grid>(home, "LibraryReadingLayer");
                        Require(layer.ActualHeight > caption.ActualHeight && layer.ActualHeight < 85
                            && Get<Border>(home, "LibraryReadingBlur").Background is AcrylicBrush,
                            "整排高斯阅读层没有限在标题下段");
                    }
                    await SaveProbeScreenAsync($"mp-library-{placement}-{width}-screen.png");
                }
            await CheckLibraryCaptionAsync();
            var overlay = Get<Border>(home, "LibraryOverlay");
            var button = Descendants(overlay).OfType<Button>().Single(button => button.Content is PosterCard poster && ReferenceEquals(poster.Card, card));
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => shell.Pages.Content is MoviePilotSubscriptionsPage);
            var page = (MoviePilotSubscriptionsPage)shell.Pages.Content;
            await LayoutAsync(page);
            Require(shell.CurrentTag == HomeLayout.MoviePilotMovies
                && Get<TextBlock>(page, "PageTitle").Text == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies),
                "首页电影订阅卡没有落到电影分类页");
            // 分类页立墙、收横排；汇总库保留横排是另一条入口的形态，不再由主页卡进入。
            Require(Get<MoviePilotSubscriptionWallView>(page, "Wall").Visibility == Visibility.Visible
                && Get<MoviePilotSubscriptionsView>(page, "Subscriptions").Visibility == Visibility.Collapsed,
                "电影分类页没有立墙、收横排");
            await SaveProbeScreenAsync("mp-library-all-screen.png");
            Call(shell, "GoBack");
            await UntilAsync(() => shell.Pages.Content is HomePage returned && returned.IsReady);
            home = (HomePage)shell.Pages.Content;
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.Count(card => card.IsNavigation) == 2);
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.All(card => !card.IsNavigation) != false);
            await LayoutAsync(home);
            await SaveProbeScreenAsync("mp-library-hidden-screen.png");
            Require((home.ViewModel.LibraryShelf?.Cards.Count ?? 0) == ordinary.Length, "关闭 MP 改变普通媒体库");
            home.ViewModel.Open(card);
            Require(ReferenceEquals(shell.Pages.Content, home), "失效卡仍打开 MP 页");
            server.Items.Clear();
            await ConnectMoviePilotAsync(fixture);
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.Count(card => card.IsNavigation) == 2);
            await LayoutAsync(home);
            await SaveProbeScreenAsync("mp-library-restored-screen.png");
            home.ViewModel.Open(home.ViewModel.LibraryShelf!.Cards.First(card => card.IsNavigation));
            await UntilAsync(() => shell.Pages.Content is MoviePilotSubscriptionsPage);
            page = (MoviePilotSubscriptionsPage)shell.Pages.Content;
            await LayoutAsync(page);
            // 分类页把页面级空态收起，零订阅的标题与说明由订阅墙自己那块画。
            Require(Get<StackPanel>(page, "EmptyState").Visibility == Visibility.Collapsed
                && Get<MoviePilotSubscriptionWallView>(page, "Wall").Visibility == Visibility.Visible,
                "零订阅时分类页没有把空态交给订阅墙");
            await SaveProbeScreenAsync("mp-library-empty-screen.png");
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => shell.Pages.Content is HomePage ready && ready.IsReady);
            await LayoutAsync((HomePage)shell.Pages.Content);
            Require(shell.Pages.BackStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionsPage))
                && shell.Pages.ForwardStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionsPage)), "断线仍保留 MP 历史");
            Require(server.Writes.Count == 0, "导航产生了 MP 写入");
        }
        finally
        {
            Call(shell, "StopMoviePilotMonitoring");
            // 同侧栏探针：用完即弃的 shell 只需放开页面，清 Frame 返回栈会在导航未落地时
            // 被 WinUI 拒绝（0x800710DD），不该让清理异常盖掉本项的检查结果。
            if (shell.Pages.Content is IShellContent current) current.Release();
            shell.SignInRoot.Detach();
            Field(shell, "_window", null);
            _root.Children.Remove(shell);
            _window!.PlaybackTitleBar = false;
            _window.SetTitleBarHoles();
            HomeMotion.ApplyPreference(animations);
            ResizeInspect(1420, 980);
        }
    }

    private async Task CheckLibraryCoverLoaderAsync(SubscriptionProbeServer server)
    {
        var urls = Enumerable.Range(1, 3).Select(id => "https://poster.invalid/" + id + ".png").ToArray();
        var bytes = await MoviePilotLibraryCoverLoader.LoadAsync(urls,
            (url, _) => Task.FromResult(url == urls[1] ? Array.Empty<byte>() : server.ImageReplies[url]), CancellationToken.None);
        var picture = await PosterLoader.DecodeAsync(bytes, 208);
        Require(picture is { PixelWidth: 576, PixelHeight: 324 }, "部分缺图没有按固定封面尺寸合成");
        await CheckLibraryCoverPixelsAsync(picture!, 2);
        var recovered = await MoviePilotLibraryCoverLoader.LoadAsync(urls,
            (url, _) => url == urls[1] ? Task.FromException<byte[]>(new OperationCanceledException("单图超时"))
                : Task.FromResult(server.ImageReplies[url]), CancellationToken.None);
        await CheckLibraryCoverPixelsAsync((await PosterLoader.DecodeAsync(recovered, 208))!, 2);
        var failed = false;
        try { await MoviePilotLibraryCoverLoader.LoadAsync(urls, (_, _) => Task.FromResult(Array.Empty<byte>()), CancellationToken.None); }
        catch (IOException) { failed = true; }
        Require(failed, "全部缺图没有交还导航卡失败冷却");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var reads = 0;
        var rejected = false;
        try { await MoviePilotLibraryCoverLoader.LoadAsync(urls, (_, _) => { reads++; return Task.FromResult(server.Poster); }, cancelled.Token); }
        catch (OperationCanceledException) { rejected = true; }
        Require(rejected && reads == 0, "封面取消后仍发起读取");
    }

    private async Task CheckLibraryCoverPixelsAsync(ImageSource picture, int count)
    {
        var pixels = await ReadBackdropPixelsAsync(picture);
        var colors = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var x = (int)((index + 0.5) * pixels.Width / count);
            var y = pixels.Height / 2;
            var at = (y * pixels.Width + x) * 4;
            colors.Add(pixels.Pixels[at] | pixels.Pixels[at + 1] << 8 | pixels.Pixels[at + 2] << 16);
        }
        Require(colors.Count == count, "订阅封面没有呈现不同海报");
        var last = ((pixels.Height - 2) * pixels.Width + pixels.Width / 2) * 4;
        Require(pixels.Pixels[last] < 25 && pixels.Pixels[last + 1] < 25 && pixels.Pixels[last + 2] < 25,
            "订阅封面倒影没有渐淡到深底");
    }

    private async Task CheckLibraryCaptionAsync()
    {
        var originalTheme = ThemeHost.Current;
        var card = new CardItem(new EmbyItem { Id = "caption-fixture", Name = "订阅·电影", Type = EmbyItemType.CollectionFolder },
            null!, 208, wide: true, subtitle: "", navigate: () => { });
        var panel = new Grid { Width = 560, Height = 210, Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
        var shade = new Border { Height = 64, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.9 };
        var blur = new Border { Height = 64, VerticalAlignment = VerticalAlignment.Bottom };
        var poster = new PosterCard
        {
            Card = card,
            CardWidth = 208,
            PosterHeight = 117,
            OnScrim = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        panel.Children.Add(blur);
        panel.Children.Add(shade);
        panel.Children.Add(poster);
        _root.Children.Add(panel);
        var topMost = _window!.TopMost;
        try
        {
            _window.TopMost = true;
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                blur.Background = (Brush)Application.Current.Resources["EgHomeLibraryBlurBrush"];
                shade.Background = (Brush)Application.Current.Resources["EgScrimBrush"];
                await LayoutAsync(panel);
                await Task.Delay(300);
                var title = Get<TextBlock>(poster, "TitleLine");
                Require(title.ActualHeight > 0 && Get<Border>(poster, "CaptionSurface").Background is SolidColorBrush { Color.A: 0 },
                    "透明标题层没有恢复");
                Require(CaptionScreenContrast(title) >= 4.5, "纯白背景上的标题对比度不足");
                await SaveProbeScreenAsync($"mp-library-caption-{theme.Id}-screen.png");
            }
            await CheckLibraryBlurAsync(panel, blur, shade);
            blur.Visibility = shade.Visibility = Visibility.Collapsed;
            await LayoutAsync(panel);
            await Task.Delay(200);
            Require(CaptionScreenContrast(Get<TextBlock>(poster, "TitleLine")) < 4.5, "文字检查未拒绝无模糊遮罩反例");
            blur.Visibility = shade.Visibility = Visibility.Visible;
            await LayoutAsync(panel);
        }
        finally
        {
            _window.TopMost = topMost;
            _root.Children.Remove(panel);
            ThemeHost.Apply(originalTheme);
        }
    }

    private async Task CheckLibraryBlurAsync(Grid panel, Border blur, Border shade)
    {
        var stripes = new Grid { Width = 352, HorizontalAlignment = HorizontalAlignment.Right };
        for (var index = 0; index < 88; index++)
            stripes.Children.Add(new Border
            {
                Width = 4,
                Margin = new Thickness(index * 4, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(index % 2 == 0 ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black)
            });
        panel.Children.Insert(0, stripes);
        var acrylic = blur.Background as AcrylicBrush ?? throw new InvalidOperationException("阅读层不是原生亚克力");
        var forceFallback = acrylic.AlwaysUseFallback;
        try
        {
            await LayoutAsync(panel);
            await Task.Delay(300);
            var shown = Capture();
            await SaveProbeScreenAsync("mp-library-gaussian-screen.png");
            blur.Visibility = Visibility.Collapsed;
            await LayoutAsync(panel);
            await Task.Delay(200);
            var clear = Capture();
            Require(clear.Edge > 40 && shown.Edge < clear.Edge * 0.4, "阅读层没有实际柔化背景条纹");
            Require(shown.Mean > 2 && shown.Mean < 245, "阅读层没有保留背景透出");
            blur.Visibility = Visibility.Visible;
            acrylic.AlwaysUseFallback = true;
            await LayoutAsync(panel);
            await Task.Delay(300);
            var fallback = Capture();
            Require(Math.Abs(shown.Mean - fallback.Mean) > 2, "常态材质与不透明回退没有像素差异");
            await SaveProbeScreenAsync("mp-library-gaussian-fallback-screen.png");
        }
        finally
        {
            acrylic.AlwaysUseFallback = forceFallback;
            blur.Visibility = shade.Visibility = Visibility.Visible;
            panel.Children.Remove(stripes);
        }

        (double Edge, double Mean) Capture()
        {
            var frame = CompositionPlaybackProbe.CaptureResizeFrame(_window!.Handle, 0);
            Require(frame.Stable, "模糊像素检查没有稳定画面");
            var position = panel.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point(250, panel.ActualHeight - 24));
            var scale = panel.XamlRoot.RasterizationScale;
            var left = (int)Math.Round(position.X * scale);
            var row = (int)Math.Round(position.Y * scale);
            var count = (int)Math.Round(220 * scale);
            double edge = 0, total = 0, previous = 0;
            for (var x = 0; x < count; x++)
            {
                var at = (row * frame.Width + left + x) * 4;
                var value = (frame.Bytes[at] + frame.Bytes[at + 1] + frame.Bytes[at + 2]) / 3d;
                total += value;
                if (x > 0) edge = Math.Max(edge, Math.Abs(value - previous));
                previous = value;
            }
            return (edge, total / count);
        }
    }

    private double CaptionScreenContrast(TextBlock title)
    {
        var frame = CompositionPlaybackProbe.CaptureResizeFrame(_window!.Handle, 0);
        Require(frame.Stable && frame.Bytes.Length > 0, "文字检查未取得稳定真实屏幕帧");
        var bounds = title.TransformToVisual(_root).TransformBounds(new Windows.Foundation.Rect(0, 0, title.ActualWidth, title.ActualHeight));
        var scale = title.XamlRoot.RasterizationScale;
        var left = Math.Clamp((int)Math.Floor(bounds.Left * scale), 0, frame.Width - 1);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right * scale), left + 1, frame.Width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top * scale), 0, frame.Height - 1);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * scale), top + 1, frame.Height);
        double minimum = 1, maximum = 0;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var index = (y * frame.Width + x) * 4;
                var luminance = 0.2126 * Channel(frame.Bytes[index + 2])
                    + 0.7152 * Channel(frame.Bytes[index + 1]) + 0.0722 * Channel(frame.Bytes[index]);
                minimum = Math.Min(minimum, luminance);
                maximum = Math.Max(maximum, luminance);
            }
        return (maximum + 0.05) / (minimum + 0.05);

        static double Channel(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
    private static async Task<byte[]> LibraryCoverFixtureAsync(int index)
    {
        const int width = 200, height = 300;
        var colors = new[] { (B: 65, G: 90, R: 210), (B: 200, G: 145, R: 40), (B: 95, G: 185, R: 90), (B: 175, G: 75, R: 155), (B: 60, G: 160, R: 220) };
        var color = colors[(index - 1) % colors.Length];
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                var shade = 0.65 + 0.35 * (height - y) / height;
                pixels[at] = (byte)(color.B * shade);
                pixels[at + 1] = (byte)(color.G * shade);
                pixels[at + 2] = (byte)(color.R * shade);
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
