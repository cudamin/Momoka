using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Interop;
using Momoka.Shell.Services;
using Momoka.Shell.Views;
using Momoka.Theming;
using Momoka.Updates;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // Official layering: one BaseAlt for the browsing island; Base for the separate settings window.
    // Real pages and navigation remain intact. Only HTTP, playback and external launch boundaries are fake.
    private async Task MicaSurfacesAsync()
    {
        var window = _window ?? throw new InvalidOperationException("云母检查缺少正式窗口");
        var savedTheme = ThemeHost.Current.Id;
        var savedMotion = HomeMotion.PreferenceEnabled;
        var savedRoot = _root.Background;
        var savedBackdrop = window.UseBackdrop;
        var savedTopMost = window.TopMost;
        var reads = new List<object>();
        var screens = new List<object>();
        try
        {
            if (window.IsMaximized) window.ToggleMaximize();
            HomeMotion.ApplyPreference(false);
            _root.Background = null;
            window.UseBackdrop = true;
            window.TopMost = true;
            window.Activate();
            CheckMicaHighContrast();
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
                await CheckProviderAsync(kind);
        }
        finally
        {
            _root.Background = savedRoot;
            window.UseBackdrop = savedBackdrop;
            window.TopMost = savedTopMost;
            HomeMotion.ApplyPreference(savedMotion);
            ThemeHost.Apply(savedTheme);
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "mica-surfaces-readings.json"),
                JsonSerializer.Serialize(new
                {
                    Reads = reads,
                    Screens = screens,
                    HighContrastChanged = false,
                    Coverage = "Emby 主页/库/音乐、本地空库和设置各五主题两尺寸；Jellyfin/飞牛影视/Plex库各一个默认主题宽窗；其它服务主页与音乐、飞牛音乐系统 WebSocket、系统透明开关/节电/低端设备回退未覆盖"
                }, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task CheckProviderAsync(MediaServerKind kind)
        {
            using var fixture = CreateFixture();
            var poster = await FixtureImageAsync(400, 600);
            var backdrop = await FixtureImageAsync(1280, 720);
            var library = new EmbyItem
            {
                Id = "mica-library", Name = "云母电影库", Type = EmbyItemType.CollectionFolder, CollectionType = "movies"
            };
            var movie = new EmbyItem
            {
                Id = "mica-movie", Name = "匿名云母电影", Type = EmbyItemType.Movie, ProductionYear = 2026,
                Overview = "只核对官方云母分层，不会播放或操作真实媒体。",
                ImageTags = new() { ["Primary"] = "mica-poster" }, BackdropImageTags = ["mica-wide"]
            };
            var plex = new PlexStateProbeServer(false);
            fixture.Transport.Reply = (request, token) => MicaBrowseReply(request, token, kind, library, movie, poster, backdrop) is { } reply
                ? Task.FromResult(reply) : kind switch
                {
                    MediaServerKind.FnNas => FnNasSettingsReply(request, token),
                    MediaServerKind.Jellyfin => Task.FromResult(JellyfinResponse(request, poster)),
                    MediaServerKind.Plex => plex.ReplyAsync(request, token),
                    _ => Task.FromResult(FakeTransport.Standard(request))
                };
            fixture.Settings.Ui.ShowHomeBanner = kind == MediaServerKind.Emby;
            fixture.Settings.Ui.CarouselCount = 1;
            fixture.Settings.Ui.CarouselSeconds = 60;
            fixture.Settings.Ui.EnableImmersive = false;
            await fixture.SelectAsync(kind == MediaServerKind.FnNas ? "fn.invalid" : kind.ToString().ToLowerInvariant() + ".invalid",
                user: kind == MediaServerKind.FnNas ? "fn-user" : "user", kind: kind);
            fixture.Capabilities.ServerKind = kind;
            fixture.Capabilities.InterfaceFlavor = kind == MediaServerKind.Emby ? ServerInterfaceFlavor.Emby : ServerInterfaceFlavor.Generic;
            var paths = fixture.Services.GetRequiredService<AppPaths>();
            fixture.Settings.LocalMedia.Libraries.Add(new()
            { Id = "mica-local", Name = "匿名本地空库", SourceDirectory = paths.Root, TargetDirectory = Path.Combine(paths.Root, "target") });
            using var local = new LocalLibraryCoordinator(fixture.SettingsService, PassthroughSecretProtector.Instance,
                fixture.Services.GetRequiredService<MoviePilotService>(), paths, fixture.Dispatcher);
            var services = new MicaProbeServices(fixture.Services, local);
            var shell = new ShellPage();
            services.Actions = shell;
            shell.Attach(services);
            Field(shell, "_window", window);
            _root.Children.Add(shell);
            try
            {
                await LayoutAsync(shell);
                await (Task)Call(shell, "EnterShellAsync")!;
                await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage);
                var home = (HomePage)shell.Pages.Content;
                Require(shell.LibraryViews.Count == 1, kind + " 夹具没有读到本服务的电影库");
                if (kind == MediaServerKind.Emby)
                    await CaptureBrowseAsync(shell, home, "home");

                var realLibrary = shell.LibraryViews.Single();
                shell.GoTo(HomeLayout.LibraryKey(realLibrary));
                await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is LibraryPage readyLibrary && !readyLibrary.ViewModel.Busy);
                var page = (LibraryPage)shell.Pages.Content;
                Require(page.ViewModel.Cards.Count > 0, kind + " 电影库夹具没有实现真实结果区");
                Require(page.ViewModel.IsFnNas == (kind == MediaServerKind.FnNas)
                    && page.ViewModel.IsJellyfin == (kind == MediaServerKind.Jellyfin), kind + " 未进入对应服务的媒体库分支");
                await CaptureBrowseAsync(shell, page, kind + "-library", allThemes: kind == MediaServerKind.Emby);
                ExpectMicaRejection(() => RequireMicaFill(new AcrylicBrush(), "LayerOnMicaBaseAltFillColorDefault", "页面"),
                    kind + " 错误页面材质");
                if (!new AccessibilitySettings().HighContrast)
                    ExpectMicaRejection(() => RequireMicaFill(MicaResource("EgMicaCommandBrush"), "LayerFillColorDefault", "库正文"),
                        "错误命令层充当正文");

                if (kind == MediaServerKind.Emby)
                {
                    var music = fixture.Session.Capture().Bind(new EmbyItem
                    {
                        Id = "mica-music", Name = "匿名音乐库", Type = EmbyItemType.CollectionFolder, CollectionType = "music"
                    });
                    shell.OpenMusicContainer(music, music.Id);
                    await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is MusicLibraryPage readyMusic
                        && readyMusic.ViewModel.Rows.Count > 0);
                    var musicPage = (MusicLibraryPage)shell.Pages.Content;
                    await CaptureBrowseAsync(shell, musicPage, "music");
                    shell.GoTo("local-library:mica-local");
                    await UntilAsync(() => shell.Pages.Content is LocalLibraryPage readyLocal && readyLocal.ViewModel.Available);
                    var localPage = (LocalLibraryPage)shell.Pages.Content;
                    await CaptureBrowseAsync(shell, localPage, "local-library");
                    Require(!localPage.ViewModel.CanPlay, "本地库夹具不应提供播放回调");
                    await CheckSettingsAsync(fixture, services);
                }

                // Navigate the real frame to a non-target page; the sign-in overlay retains old frame content.
                Require(shell.Pages.Navigate(typeof(ServersPage), new ServerRequest(services)), "非云母服务器页未导航");
                await UntilAsync(() => shell.Pages.Content is ServersPage serverPage && serverPage.IsReady);
                await LayoutAsync(shell);
                Require(Get<Grid>(shell, "ContentHost").Background is SolidColorBrush contentFill
                    && contentFill.Color == ThemeHost.ToColor(ThemeHost.Current.Colors.Window)
                    && Get<Grid>(shell, "SidebarHost").Background is SolidColorBrush sidebarFill
                    && sidebarFill.Color == ThemeHost.ToColor(ThemeHost.Current.Colors.Surface), "切离目标页面没有恢复原外壳底色");
                shell.GoTo("home", force: true);
                await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage);
                home = (HomePage)shell.Pages.Content;
                await LayoutAsync(home);
                CheckBrowse(shell, home);
                Require(!shell.PlayerRoot.Attached && window.UseBackdrop, "云母检查意外进入播放页面");
                reads.Add(new { Provider = kind.ToString(), State = "离页与返回恢复通过", Requests = fixture.Transport.Requests.Count });
            }
            finally
            {
                Call(shell, "StopMoviePilotMonitoring");
                Call(shell, "ReleaseContent");
                shell.SignInRoot.Detach();
                shell.SignInRoot.ViewModel.Dispose();
                _root.Children.Remove(shell);
            }
        }

        async Task CaptureBrowseAsync(ShellPage shell, Page page, string stage, bool allThemes = true)
        {
            IEnumerable<UiTheme> themes = allThemes ? UiThemes.All : [UiThemes.Default];
            int[] widths = allThemes ? [1280, 760] : [1280];
            foreach (var width in widths)
                foreach (var theme in themes)
                {
                    ThemeHost.Apply(theme.Id);
                    ResizeProbeClient(width, 760);
                    await UntilAsync(() => { _root.UpdateLayout(); return Math.Abs(_root.ActualWidth - width) < 1; });
                    await LayoutAsync(page);
                    CheckBrowse(shell, page);
                    RejectMicaBackground(Get<Grid>(shell, "ContentHost"), new SolidColorBrush(Microsoft.UI.Colors.Black),
                        () => CheckBrowse(shell, page), "旧实色外壳容器");
                    var name = $"mica-{stage}-{theme.Id}-{width}-screen.png";
                    await SaveProbeScreenAsync(name);
                    RecordScreen(window.Handle, page, name, stage, theme.Id, width);
                    reads.Add(new { Stage = stage, Theme = theme.Id, Width = width, PageFill = MicaBrushText(page.Background), shell.TitleStripNow });
                }
        }

        async Task CheckSettingsAsync(Fixture fixture, MicaProbeServices browseServices)
        {
            var fonts = new FontLibrary();
            Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
            using var updates = new UpdateClient(new RejectNetwork());
            var playback = new PlaybackService(fixture.Session, fixture.Settings,
                () => throw new InvalidOperationException("云母设置检查禁止创建播放后端"),
                new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders)), allowPlayback: false);
            using var additions = new ServiceCollection().AddSingleton(fonts).AddSingleton(updates).AddSingleton(playback)
                .AddSingleton(new ShaderStaging(fixture.Settings)).AddSingleton(new AudioDeviceCatalogue(() => null))
                .AddSingleton(new MoviePilotCredentials(PassthroughSecretProtector.Instance)).BuildServiceProvider();
            var services = new MicaProbeServices(browseServices, null, additions);
            var settings = SettingsWindow.TryCreate(window) ?? throw new InvalidOperationException("云母设置窗口创建失败");
            // Closing the only real WinUI Window exits this app. FinishAsync closes these after all cases.
            _openingProbeWindows.Add(settings);
            var nativeWindow = Get<Window>(settings, "_window");
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)nativeWindow.AppWindow.Presenter;
            var savedAlwaysOnTop = presenter.IsAlwaysOnTop;
            var hostTopMost = window.TopMost;
            window.TopMost = false;
            try
            {
                presenter.IsAlwaysOnTop = true;
                settings.Show(new SettingsRequest(services, "主页"));
                var page = settings.Page ?? throw new InvalidOperationException("设置未导航到真实页面");
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                var backdrop = nativeWindow.SystemBackdrop;
                foreach (var width in new[] { 1280, 760 })
                    foreach (var theme in UiThemes.All)
                    {
                        ThemeHost.Apply(theme.Id);
                        var scale = page.XamlRoot.RasterizationScale;
                        await ResizeSettingsProbeWindowAsync(nativeWindow, (int)Math.Round(width * scale), (int)Math.Round(760 * scale));
                        await LayoutAsync(page);
                        await WaitForSettingsSettledAsync(page);
                        CheckSettings(page);
                        var card = Descendants(Get<ItemsControl>(page, "SettingsSections")).OfType<Border>()
                            .FirstOrDefault(border => border.Child is ItemsControl { Name: "SettingsRowsList" }
                                && border.ActualWidth > 0 && border.ActualHeight > 0)
                            ?? throw new InvalidOperationException("设置夹具没有实现可见内容卡片");
                        RequireMicaFill(card.Background, "LayerFillColorDefault", "设置内容卡片");
                        ExpectMicaRejection(() => RequireMicaFill(new AcrylicBrush(), "LayerFillColorDefault", "设置内容卡片"),
                            "旧亚克力设置卡片");
                        RejectMicaBackground((Grid)nativeWindow.Content, new SolidColorBrush(Microsoft.UI.Colors.Black),
                            () => CheckSettings(page), "旧实色设置根容器");
                        await WaitForDiagnosticsFrameAsync();
                        var name = $"mica-settings-{theme.Id}-{width}-screen.png";
                        await CompositionPlaybackProbe.SaveScreenAsync(settings.Handle, Path.Combine(_options.Paths.LogDirectory, name));
                        RecordScreen(settings.Handle, page, name, "settings", theme.Id, width);
                        settings.Hide();
                        Require(!settings.IsOpen && !Native.IsWindowVisible(settings.Handle), "设置窗口隐藏后仍可见");
                        Require(ReferenceEquals(backdrop, nativeWindow.SystemBackdrop), "隐藏设置丢失系统云母对象");
                        settings.Show(new SettingsRequest(services, "主页"));
                        Require(ReferenceEquals(page, settings.Page), "重开设置无故更换缓存页面");
                        await LayoutAsync(page);
                        await WaitForSettingsSettledAsync(page);
                        CheckSettings(page);
                    }

                page.SelectedCategory = "服务器";
                await UntilAsync(() => settings.CurrentPage is ServersPage hosted && hosted.IsReady);
                Require(settings.CurrentPage is Page hostedPage && IsMicaTransparent(hostedPage.Background), "托管服务器页实色挡住设置云母");
                page.SelectedCategory = "主页";
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                CheckSettings(page);
                var mica = nativeWindow.SystemBackdrop as MicaBackdrop ?? throw new InvalidOperationException("设置窗口丢失云母");
                try
                {
                    mica.Kind = MicaKind.BaseAlt;
                    ExpectMicaRejection(() => CheckSettings(page), "设置窗口错误使用 Alt");
                }
                finally { mica.Kind = MicaKind.Base; }
                reads.Add(new { Stage = "settings", HiddenRestored = true, HostedPage = "ServersPage", BackdropKind = mica.Kind.ToString() });
            }
            finally
            {
                settings.Hide();
                presenter.IsAlwaysOnTop = savedAlwaysOnTop;
                nativeWindow.Content = null;
                window.TopMost = hostTopMost;
                window.Activate();
                await playback.ShutdownAsync();
            }

            void CheckSettings(SettingsPage page)
            {
                Require(nativeWindow.SystemBackdrop is MicaBackdrop { Kind: MicaKind.Base }, "设置独立窗口缺少 Base 云母");
                Require(nativeWindow.Content is Grid root && IsMicaTransparent(root.Background), "设置根容器实色挡住云母");
                Require(IsMicaTransparent(Get<Frame>(settings, "_frame").Background) && IsMicaTransparent(page.Background),
                    "设置 Frame 或 Page 实色挡住云母");
                Require(IsMicaTransparent(Get<Frame>(page, "HostedFrame").Background), "托管 Frame 实色挡住设置云母");
                RequireMicaFill((Brush)page.Resources["CardBackgroundFillColorDefaultBrush"], "LayerFillColorDefault", "设置局部卡片资源");
                Require(Get<DesktopWindowXamlSource>(window, "_source").SystemBackdrop is MicaBackdrop { Kind: MicaKind.BaseAlt },
                    "设置窗口材质覆盖了主窗口 Alt");
            }
        }

        void CheckBrowse(ShellPage shell, Page page)
        {
            Require(Get<DesktopWindowXamlSource>(window, "_source").SystemBackdrop is MicaBackdrop { Kind: MicaKind.BaseAlt },
                "浏览主窗口缺少单个 Alt 云母");
            Require(Descendants(page).OfType<SystemBackdropElement>().All(element => element.SystemBackdrop is null),
                "目标浏览页另加背景材质，破坏单基层结构");
            Require(IsMicaTransparent(_root.Background) && IsMicaTransparent(Get<Grid>(shell, "ContentHost").Background),
                "浏览页面祖先实色挡住窗口云母");
            RequireMicaFill(Get<Grid>(shell, "SidebarHost").Background, "LayerOnMicaBaseAltFillColorDefault", "侧栏命令层");
            if (shell.TitleStripNow == TitleStrip.Plain)
                Require(IsMicaTransparent(Get<Grid>(shell, "TrailBar").Background), "Plain 标题栏实色挡住云母");
            RequireMicaFill(page.Background, "LayerOnMicaBaseAltFillColorDefault", page.GetType().Name + " 命令层");
            var content = page switch
            {
                HomePage => Get<Grid>(page, "Overlay").Background,
                LibraryPage => Get<Grid>(page, "EmbyContent").Background,
                MusicLibraryPage => Get<Grid>(page, "MusicContent").Background,
                LocalLibraryPage => Get<ListView>(page, "LocalLibraryEntries").Background,
                _ => throw new InvalidOperationException("未登记云母页面：" + page.GetType().Name)
            };
            RequireMicaFill(content, "LayerFillColorDefault", page.GetType().Name + " 内容层");
        }

        void RecordScreen(IntPtr handle, FrameworkElement page, string file, string stage, string theme, int width)
        {
            Require(Native.GetWindowThreadProcessId(handle, out var process) != 0 && process == (uint)Environment.ProcessId
                && Native.IsWindowVisible(handle) && Native.GetWindowRect(handle, out _)
                && Native.GetClientRect(handle, out _), "云母截图缺少本轮窗口归属或实际矩形");
            Native.GetWindowRect(handle, out var outer);
            Native.GetClientRect(handle, out var area);
            screens.Add(new
            {
                Stage = stage, Theme = theme, Width = width, File = file, Handle = $"0x{handle:X}", Process = process,
                Window = new { outer.Left, outer.Top, outer.Width, outer.Height },
                Client = new { area.Width, area.Height }, Page = new { page.ActualWidth, page.ActualHeight },
                Scale = page.XamlRoot.RasterizationScale, HighContrast = new AccessibilitySettings().HighContrast
            });
        }
    }

    private static HttpResponseMessage? MicaBrowseReply(HttpRequestMessage request, CancellationToken token,
        MediaServerKind kind, EmbyItem library, EmbyItem movie, byte[] poster, byte[] backdrop)
    {
        token.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        // FnNas item/list is a read-only POST; Plex watch-state endpoints are GET writes and remain forbidden.
        Require(request.Method == HttpMethod.Get || request.Method == HttpMethod.Head
            || kind == MediaServerKind.FnNas && request.Method == HttpMethod.Post && path.EndsWith("/item/list", StringComparison.Ordinal),
            "云母探针拒绝写入：" + request.Method + " " + path);
        Require(!path.Contains("/:/", StringComparison.Ordinal) && !path.Contains("/Sessions/", StringComparison.Ordinal),
            "云母探针拒绝播放与观看状态端点");
        if (kind == MediaServerKind.FnNas)
        {
            if (path.EndsWith("/mediadb/list", StringComparison.Ordinal)) return FnNasEnvelope(new[] { new { guid = library.Id, name = library.Name, category = "Movie" } });
            if (path.EndsWith("/mediadb/sum", StringComparison.Ordinal)) return FnNasEnvelope(new Dictionary<string, int> { [library.Id] = 1, ["total"] = 1 });
            if (path.EndsWith("/item/list", StringComparison.Ordinal)) return FnNasEnvelope(new
            {
                list = new[] { new { guid = movie.Id, title = movie.Name, type = "Movie", poster = "/mica/poster.webp", backdrops = "/mica/wide.webp", vote_average = 8.1 } }, total = 1
            });
            if (path.Contains("/api/v1/item/", StringComparison.Ordinal)) return FnNasEnvelope(new
            { guid = movie.Id, title = movie.Name, type = "Movie", poster = "/mica/poster.webp", backdrops = "/mica/wide.webp" });
            if (path.Contains("/sys/img/", StringComparison.Ordinal)) return Image(path.Contains("wide", StringComparison.Ordinal) ? backdrop : poster);
            if (path.EndsWith("/play/list", StringComparison.Ordinal) || path.EndsWith("/tag/genres", StringComparison.Ordinal)
                || path.EndsWith("/tag/iso3166", StringComparison.Ordinal)) return FnNasEnvelope(Array.Empty<object>());
            if (path.EndsWith("/tag/list", StringComparison.Ordinal)) return FnNasEnvelope(new { });
            return null;
        }
        if (kind == MediaServerKind.Plex)
        {
            if (path.EndsWith("/library/sections", StringComparison.Ordinal))
                return Json(new { MediaContainer = new { Directory = new[] { new { key = library.Id, title = library.Name, type = "movie", uuid = "mica-library" } } } });
            if (path.Contains("/mica/", StringComparison.Ordinal)) return Image(path.Contains("wide", StringComparison.Ordinal) ? backdrop : poster);
            if (path.EndsWith("/library/sections/" + library.Id, StringComparison.Ordinal))
                return Json(new { MediaContainer = new { Type = new[] { new { key = "1", type = "movie", title = "电影" } } } });
            if (path.EndsWith("/all", StringComparison.Ordinal) || path.EndsWith("/recentlyAdded", StringComparison.Ordinal))
                return Json(new
                {
                    MediaContainer = new
                    {
                        totalSize = 1, size = 1, Metadata = new[]
                        {
                            new { ratingKey = movie.Id, title = movie.Name, type = "movie", summary = movie.Overview,
                                thumb = "/mica/poster.webp", art = "/mica/wide.webp", duration = 180000 }
                        }
                    }
                });
            return path.Contains("/hubs/", StringComparison.Ordinal) || path.Contains("/onDeck", StringComparison.Ordinal)
                || path.Contains("/filter", StringComparison.Ordinal) || path.Contains("/firstCharacter", StringComparison.Ordinal)
                ? Json(new { MediaContainer = new { totalSize = 0, size = 0, Metadata = Array.Empty<object>() } }) : null;
        }
        if (path.EndsWith("/Views", StringComparison.Ordinal)) return Json(new { Items = new[] { library }, TotalRecordCount = 1 });
        if (path.Contains("/Images/", StringComparison.Ordinal)) return Image(path.Contains("Backdrop", StringComparison.Ordinal) ? backdrop : poster);
        if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Json(new[] { movie });
        if (path.EndsWith("/Items/" + movie.Id, StringComparison.Ordinal)) return Json(movie);
        if (path.EndsWith("/Items", StringComparison.Ordinal))
        {
            var music = Query(request, "ParentId") == "mica-music";
            var entry = music ? new EmbyItem { Id = "mica-track", Name = "匿名音乐", Type = EmbyItemType.Audio, Artists = ["匿名音乐人"], Album = "云母曲集" } : movie;
            return Json(new { Items = new[] { entry }, TotalRecordCount = 1 });
        }
        if (path.EndsWith("/Items/Resume", StringComparison.Ordinal) || path.EndsWith("/UserItems/Resume", StringComparison.Ordinal)
            || path.EndsWith("/Shows/NextUp", StringComparison.Ordinal)) return Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 });
        return null;

        static HttpResponseMessage Image(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }

    private static Brush MicaResource(string key) => Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
        ? brush : throw new InvalidOperationException("缺少云母角色资源：" + key);

    private static bool IsMicaTransparent(Brush? brush) => brush is null or SolidColorBrush { Color.A: 0 };
    private static string MicaBrushText(Brush? brush) => brush is SolidColorBrush solid ? solid.Color.ToString() : brush?.GetType().Name ?? "null";

    private static void RequireMicaFill(Brush? brush, string token, string name)
    {
        var highContrast = new AccessibilitySettings().HighContrast;
        var color = (Color)Application.Current.Resources[highContrast ? "SystemColorWindowColor" : token];
        Require(brush is SolidColorBrush solid && solid.Color == color
            && (highContrast || solid.Color.A is > 0 and < 255), name + " 未使用官方低不透明度纯色层：" + MicaBrushText(brush));
    }

    private static void RejectMicaBackground(FrameworkElement element, Brush replacement, Action assertCurrent, string name)
    {
        var property = element switch
        {
            Border => Border.BackgroundProperty,
            Panel => Panel.BackgroundProperty,
            Control => Control.BackgroundProperty,
            _ => throw new InvalidOperationException("云母反例不支持元素：" + element.GetType().Name)
        };
        var previous = element.GetValue(property);
        // Only code-owned transparent ancestors use this; never overwrite ThemeResource expressions.
        try
        {
            element.SetValue(property, replacement);
            ExpectMicaRejection(assertCurrent, name);
        }
        finally { element.SetValue(property, previous); }
        assertCurrent();
    }

    private static void ExpectMicaRejection(Action validate, string name)
    {
        var rejected = false;
        try { validate(); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "云母断言没有拒绝反例：" + name);
    }

    private static void CheckMicaHighContrast()
    {
        foreach (var file in new[] { "Mica.xaml", "MicaCards.xaml" })
        {
            var dictionary = new ResourceDictionary { Source = new Uri("ms-appx:///Theme/" + file) };
            var contrast = dictionary.ThemeDictionaries.TryGetValue("HighContrast", out var value) && value is ResourceDictionary selected
                ? selected : throw new InvalidOperationException(file + " 缺少高对比字典");
            string[] keys = file == "Mica.xaml" ? ["EgMicaCommandBrush", "EgMicaContentBrush", "EgMicaCardBrush"]
                : ["CardBackgroundFillColorDefaultBrush"];
            foreach (var key in keys)
                Require(contrast.TryGetValue(key, out var brush) && brush is SolidColorBrush solid
                    && solid.Color == (Color)Application.Current.Resources["SystemColorWindowColor"],
                    file + "/" + key + " 未绑定系统窗口颜色，高对比可能误读红色 Color token");
        }
    }

    private sealed class MicaProbeServices(IServiceProvider services, LocalLibraryCoordinator? local,
        IServiceProvider? additions = null) : IServiceProvider
    {
        internal IShellActions? Actions { get; set; }
        public object? GetService(Type type) => type == typeof(IShellActions) && Actions is not null ? Actions
            : type == typeof(LocalLibraryCoordinator) && local is not null ? local
            : additions?.GetService(type) ?? services.GetService(type);
    }
}
