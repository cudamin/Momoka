using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual current Shell subscription VM and episode rows, without network, windows or native image decoding.</summary>
internal static class MoviePilotSubscriptionPageStateTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(string shellPath, string sourceRoot)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var root = Path.GetFullPath(sourceRoot);
            var shell = Path.GetFullPath(shellPath);
            if (!File.Exists(Path.Combine(root, "Momoka.sln")) || !File.Exists(shell)
                || !shell.StartsWith(Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(shell) != "Momoka.dll") throw new AssertionException("必须提供当前本树 x64 Release Shell DLL");
            var directory = Path.GetDirectoryName(shell)!;
            AssemblyLoadContext.Default.Resolving += Resolve;
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(shell);
                var type = assembly.GetType("Momoka.Shell.ViewModels.MoviePilotSubscriptionsViewModel", true)!;
                RegisterImageLifetime(assembly);

                Console.WriteLine("订阅分类与分集状态：真实Shell VM，禁止配置／凭据／网络／写入／窗口");
                Console.WriteLine("Shell=" + shell);
                foreach (var address in new[] { "", "http://[invalid" })
                    Test("MP 图片捕获：实际详情和订阅 Attach 无效配置仍可隐藏 " + address, () =>
                    {
                        using var transport = new SubscriptionTransport();
                        using var client = new MoviePilotClient(transport);
                        var settings = new AppSettings();
                        settings.MoviePilot.Url = address;
                        using var service = new MoviePilotService(client, new MoviePilotCredentials(new RejectAttachSecrets()), settings);
                        var subscriptions = Activator.CreateInstance(type)!;
                        type.GetMethod("Attach", Members)!.Invoke(subscriptions, [service, null]);
                        Assert.Equal("Collapsed", type.GetProperty("Visible", Members)!.GetValue(subscriptions)!.ToString());
                        type.GetMethod("Cancel", Members)!.Invoke(subscriptions, null);
                        var mediaType = assembly.GetType("Momoka.Shell.ViewModels.MoviePilotMediaViewModel", true)!;
                        var media = Activator.CreateInstance(mediaType)!;
                        mediaType.GetMethod("Attach", Members)!.Invoke(media,
                            [service, new MoviePilotMedia { Title = "Fixture" }, "", false, null, null, null, CancellationToken.None]);
                        mediaType.GetMethod("Cancel", Members)!.Invoke(media, null);
                        Assert.Equal(0, transport.Writes);
                    });
                foreach (var series in new[] { false, true })
                {
                    var key = series ? Momoka.Emby.HomeLayout.MoviePilotSeries : Momoka.Emby.HomeLayout.MoviePilotMovies;
                    Test("R-036 当前分类空而另一分类有数据 " + key, () =>
                    {
                        using var f = new Fixture(type, key, series ? "movie" : "series");
                        f.Read();
                        Assert.Equal("Visible", f.Get("Visible").ToString(), "共享主页Visible仍是任一分类有数据");
                        Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                        Assert.Equal("Collapsed", f.Get("LoadingVisibility").ToString());
                        Assert.False((bool)f.Get("Loading"));
                        Assert.Equal(0, f.Transport.Writes);
                    });
                    Test("R-036 本类空且另一类旧数据，实际刷新挂起显示读取中 " + key, () =>
                    {
                        using var f = new Fixture(type, key, series ? "movie" : "series");
                        f.Read();
                        f.Transport.Hold = true;
                        var read = f.StartRead();
                        Assert.False(read.IsCompleted);
                        Assert.True((bool)f.Get("Busy"));
                        Assert.True((bool)f.Get("Loading"));
                        Assert.Equal("Visible", f.Get("LoadingVisibility").ToString());
                        Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString());
                        f.Transport.Release();
                        read.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                        Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                        Assert.Equal("Collapsed", f.Get("LoadingVisibility").ToString());
                    });
                    Test("R-036 本类最后条目读取后移除会更新空态通知 " + key, () =>
                    {
                        using var f = new Fixture(type, key, "both");
                        f.Read();
                        Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString());
                        var notifications = new List<string>();
                        ((System.ComponentModel.INotifyPropertyChanged)f.Vm).PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? "");
                        f.Transport.Kind = series ? "movie" : "series";
                        f.Read();
                        Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                        Assert.True(notifications.Contains("EmptyStateVisibility"));
                        Assert.True(notifications.Contains("Loading"));
                        Assert.Equal(0, f.Transport.Writes, "仅模拟确认写后服务器读取列表，不执行真实删除");
                    });
                    Test("R-036 本类错误保留提示且可刷新恢复 " + key, () =>
                    {
                        using var f = new Fixture(type, key, series ? "movie" : "series");
                        f.Read();
                        f.Transport.Fail = true;
                        f.Read();
                        Assert.True((bool)f.Get("NoticeOpen"));
                        Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString(), "业务错误由既有提示/刷新排显示，不与成功空态混同");
                        Assert.Equal("Visible", f.Get("Visible").ToString());
                        f.Transport.Fail = false;
                        f.Read();
                        Assert.False((bool)f.Get("NoticeOpen"));
                        Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                    });
                }
                Test("R-036 同VM分类切换仅改页面投影且立即通知", () =>
                {
                    using var f = new Fixture(type, Momoka.Emby.HomeLayout.MoviePilotMovies, "series");
                    f.Read();
                    var movies = f.Get("Movies");
                    var seriesRows = f.Get("Series");
                    var changes = new List<string>();
                    ((System.ComponentModel.INotifyPropertyChanged)f.Vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? "");
                    f.SetCategory(Momoka.Emby.HomeLayout.MoviePilotSeries);
                    Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString());
                    Assert.True(changes.Contains("EmptyStateVisibility"));
                    Assert.True(ReferenceEquals(movies, f.Get("Movies")));
                    Assert.True(ReferenceEquals(seriesRows, f.Get("Series")));
                    f.SetCategory(Momoka.Emby.HomeLayout.MoviePilotMovies);
                    Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                    f.SetCategory("unknown-page");
                    Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString(), "未知分类退回主页语义，不误删数据");
                    Assert.Equal(0, f.Transport.Writes);
                });
                Test("R-036 主页共享VM保留任一分类语义", () =>
                {
                    using var f = new Fixture(type, null, "series");
                    f.Read();
                    Assert.Equal("Visible", f.Get("Visible").ToString());
                    Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString());
                    f.Transport.Hold = true;
                    var read = f.StartRead();
                    Assert.False((bool)f.Get("Loading"), "共享主页有任一内容时由行顶部细进度显示刷新");
                    f.Transport.Release();
                    read.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                });
                Test("R-036 当前源码页在AttachRow之前设置分类且保留原刷新接线", () =>
                {
                    var page = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionsPage.xaml.cs"));
                    var scope = page.IndexOf("UsePageCategory(request.Key)", StringComparison.Ordinal);
                    Assert.True(scope > page.IndexOf("_owner = new MoviePilotHomeSubscriptions()", StringComparison.Ordinal));
                    Assert.True(scope < page.IndexOf("Subscriptions.AttachRow", StringComparison.Ordinal));
                    Assert.Contains("private void OnRefresh(object sender, RoutedEventArgs e) => _ = ViewModel.ReloadAsync();", page);
                    var xaml = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionsPage.xaml"));
                    Assert.Contains("Visibility=\"{x:Bind ViewModel.EmptyStateVisibility, Mode=OneWay}\"", xaml);
                    Assert.Contains("x:Name=\"RefreshSubscriptions\"", xaml);
                    Assert.Contains("Click=\"OnRefresh\"", xaml);
                });
                RegisterEpisodeRows(assembly, root);
                RegisterDetailLayout(root);
                return TestHarness.Run();
            }
            finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
            Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void RegisterDetailLayout(string root)
    {
        Test("MP 详情布局：头部自然收边，简介与季共左列，网页资料卡参与头部自然高度、下方列表全宽", () =>
        {
            var document = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            XElement Named(string name) => document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
            var primary = Named("BodyPrimary");
            var body = Named("BodyRow");
            Assert.True(ReferenceEquals(primary.Parent, body));
            Assert.True(ReferenceEquals(Named("HeroOverview").Parent, primary));
            Assert.True(ReferenceEquals(Named("SeasonPanel").Parent, primary));
            Assert.True(ReferenceEquals(Named("HeroFacts").Parent, Named("HeroRow")));
            Assert.True(ReferenceEquals(body.Parent, Named("BodyFlow")));
            Assert.True(ReferenceEquals(Named("PeoplePanel").Parent, Named("BodyFlow")));
            Assert.False(document.Descendants().Any(element => (string?)element.Attribute(x + "Name") is "BodyLayout" or "SubscriptionFacts"));
            foreach (var name in new[] { "PeoplePanel", "RecommendationsPanel", "SimilarPanel" })
                Assert.False(Named(name).Attributes().Any(attribute => attribute.Name.LocalName == "Width"));
            Assert.False(Named("HeroFacts").Attributes().Any(attribute => attribute.Name.LocalName == "Height"));
            Assert.False(Named("HeroFacts").Descendants().Any(element => element.Name.LocalName == "ScrollViewer"));
            Assert.Equal("128", (string?)Named("OpenRelatedMedia").Attribute("Width"));
            Assert.Equal("Transparent", (string?)Named("OpenRelatedMedia").Attribute("Background"));
            Assert.Equal("0", (string?)Named("OpenRelatedMedia").Attribute("BorderThickness"));
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBackgroundDisabled" })
                Assert.True(Named("OpenRelatedMedia").Descendants().Any(element => (string?)element.Attribute(x + "Key") == key
                    && (string?)element.Attribute("Color") == "Transparent"));
            Assert.Equal("184", (string?)Named("RelatedArtwork").Attribute("Height"));
            Assert.Equal("Wrap", (string?)Named("RelatedTitle").Attribute("TextWrapping"));
            Assert.False(Named("RelatedTitle").Attributes().Any(attribute => attribute.Name.LocalName is "TextTrimming" or "MaxLines"));
            Assert.False(Named("HeroSection").Attributes().Any(attribute => attribute.Name.LocalName == "MinHeight"));
            Assert.Equal("Top", (string?)Named("HeroSurface").Attribute("VerticalAlignment"));
            Assert.Equal("Top", (string?)Named("HeroOverview").Attribute("VerticalAlignment"));
            Assert.Equal("OnDetailViewChanged", (string?)Named("DetailScroll").Attribute("ViewChanged"));
            var seasons = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotMediaSeasonsView.xaml"));
            var group = seasons.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "SwitchEpisodeGroup");
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", (string?)group.Attribute("Background"));
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBackgroundDisabled" })
                Assert.True(group.Descendants().Any(element => (string?)element.Attribute(x + "Key") == key
                    && ((string?)element.Attribute("ResourceKey"))?.StartsWith("CardBackgroundFillColor", StringComparison.Ordinal) == true));
            Assert.True(seasons.Descendants().Any(element => (string?)element.Attribute(x + "Key") == "ExpanderHeaderBackground"
                && (string?)element.Attribute("ResourceKey") == "CardBackgroundFillColorDefaultBrush"));
            Assert.False(Named("HeroGrid").Descendants().Any(element => element.Name.LocalName == "RowDefinition"
                && (string?)element.Attribute("Height") == "*"));
        });
    }

    private static void RegisterImageLifetime(Assembly shell)
    {
        foreach (var series in new[] { false, true })
            Test("MP 详情：数据读取完成后离页仍取消头图、季图、演职员和推荐图片 " + series, () =>
            {
                using var transport = new StubTransport()
                    .Answer("login/access-token", "{\"access_token\":\"fixture-token\"}")
                    .Answer("dashboard/system", "{}")
                    .Answer("media/42", JsonSerializer.Serialize(new { title = "Fixture", type = series ? "电视剧" : "电影", media_source = "themoviedb", media_id = "42", poster_path = "/poster.jpg", backdrop_path = "/backdrop.jpg", season_info = series ? new[] { new { season_number = 1, name = "Season 1", episode_count = 1, poster_path = "/season.jpg" } } : [] }))
                    .Answer("subscribe/", "[]")
                    .Answer("media/groups/", "[]")
                    .Answer("mediaserver/exists_remote", "{}")
                    .Answer("tmdb/credits/", "[{\"id\":1,\"name\":\"Actor\",\"profile_path\":\"/actor.jpg\"}]")
                    .Answer("tmdb/recommend/", "[{\"title\":\"Related\",\"type\":\"电影\",\"media_source\":\"themoviedb\",\"media_id\":\"43\",\"poster_path\":\"/related.jpg\"}]")
                    .Answer("tmdb/similar/", "[]");
                var settings = new AppSettings();
                settings.MoviePilot.Enabled = true;
                settings.MoviePilot.Url = "http://192.0.2.10:3001";
                settings.MoviePilot.Username = "anonymous";
                settings.MoviePilot.ProtectedPassword = "fixture-password";
                using var client = new MoviePilotClient(transport);
                using var service = new MoviePilotService(client, new MoviePilotCredentials(PassthroughSecretProtector.Instance), settings);
                Assert.True(service.RefreshAvailabilityAsync().GetAwaiter().GetResult());
                var type = shell.GetType("Momoka.Shell.ViewModels.MoviePilotMediaViewModel", true)!;
                var vm = Activator.CreateInstance(type)!;
                var media = new MoviePilotMedia { Title = "Fixture", Type = series ? "电视剧" : "电影", MediaSource = "themoviedb", MediaId = "42", ConnectionStamp = service.ConnectionStamp };
                type.GetMethod("Attach", Members)!.Invoke(vm, [service, media, "", false, null, null, null, CancellationToken.None]);
                var tokens = new List<CancellationToken>();
                Func<string, CancellationToken, Task<byte[]>> fetch = async (_, token) =>
                {
                    tokens.Add(token);
                    await Task.Delay(Timeout.Infinite, token);
                    throw new AssertionException("不解码或生成图片");
                };
                type.GetField("_fetchImage", Members)!.SetValue(vm, fetch);
                try
                {
                    ((Task)type.GetMethod("ReloadAsync", Members)!.Invoke(vm, null)!).GetAwaiter().GetResult();
                    Assert.Equal(series ? 5 : 4, tokens.Count, "真实 Reload 已发起两张头图、季图、演职员和推荐图片");
                    Assert.True(tokens.All(token => !token.IsCancellationRequested));
                    if (series)
                    {
                        var previousSeason = tokens[2];
                        var lifetime = (CancellationTokenSource)type.GetField("_lifetime", Members)!.GetValue(vm)!;
                        ((Task)type.GetMethod("LoadSeasonsAsync", Members)!.Invoke(vm, [lifetime.Token])!).GetAwaiter().GetResult();
                        Assert.True(previousSeason.IsCancellationRequested, "切组重读取消已结束季读取的后台季图");
                        Assert.False(tokens[0].IsCancellationRequested, "切组仍保留当前媒体头图");
                        Assert.False(tokens.Last().IsCancellationRequested);
                    }
                    var previousImages = tokens.ToArray();
                    ((Task)type.GetMethod("ReloadAsync", Members)!.Invoke(vm, null)!).GetAwaiter().GetResult();
                    Assert.True(previousImages.All(token => token.IsCancellationRequested), "刷新取消上一轮后台图片");
                    Assert.True(tokens.Skip(previousImages.Length).All(token => !token.IsCancellationRequested));
                    type.GetMethod("Cancel", Members)!.Invoke(vm, null);
                    Assert.True(tokens.All(token => token.IsCancellationRequested), "Reload 结束后不能切断后台图片与页面的取消链");
                }
                finally { type.GetMethod("Cancel", Members)!.Invoke(vm, null); }
            });
    }

    private static void RegisterEpisodeRows(Assembly shell, string root)
    {
        var subscriptionType = shell.GetType("Momoka.Shell.ViewModels.MoviePilotSubscriptionEpisodeRow", true)!;
        var mediaType = shell.GetType("Momoka.Shell.ViewModels.MoviePilotMediaEpisodeRow", true)!;
        foreach (var (name, number, title, expected) in new[]
        {
            ("有标题保留集号并裁掉两端空白", 1, "  重逢  ", "第 1 集 · 重逢"),
            ("空标题只显示集号", 1, "", "第 1 集"),
            ("空白标题只显示集号", 1, " \t\r\n　", "第 1 集"),
            ("默认标题不重复", 1, "第 1 集", "第 1 集"),
            ("无空格默认标题不重复", 1, "第1集", "第 1 集"),
            ("全角及换行空白默认标题不重复", 1, " \t第　1集\r\n", "第 1 集"),
            ("相似标题不误当默认标题", 1, "第 1 集后记", "第 1 集 · 第 1 集后记"),
            ("电影标题仍显示电影身份", 0, " 心愿 ", "电影 · 心愿"),
            ("电影默认标题不重复", 0, " 电　影\t", "电影")
        })
        {
            Test("订阅分集标题：" + name, () =>
            {
                var row = SubscriptionRow(subscriptionType, new(number, title, "", [], []));
                Assert.Equal(expected, RowProperty(row, "RowTitle"));
                Assert.Equal("搜索资源 · " + expected, RowProperty(row, "SearchName"), "可访问名称与显示标题一致");
                Assert.Equal("播放 · " + expected, RowProperty(row, "PlayName"));
            });
        }
        foreach (var (name, description, visibility) in new[]
        {
            ("空简介", "", "Collapsed"),
            ("空白简介", " \t\r\n　", "Collapsed"),
            ("有简介", "一段关于重逢的故事。", "Visible")
        })
        {
            Test("订阅分集简介：" + name, () =>
            {
                var row = SubscriptionRow(subscriptionType, new(1, "重逢", description, [], []));
                Assert.Equal(visibility, RowProperty(row, "DescriptionVisibility")?.ToString());
                Assert.Equal(description, RowProperty(row, "Description"), "显隐不清空元数据");
            });
            Test("普通分集简介：" + name, () =>
            {
                var row = MediaRow(mediaType, new(1, "重逢", description, "", "", null));
                Assert.Equal(visibility, RowProperty(row, "OverviewVisibility")?.ToString());
                Assert.Equal(description, RowProperty(row, "Overview"));
            });
        }
        foreach (var (name, url) in new[] { ("未提供图片", ""), ("仅有URL未取得图片", "https://art.invalid/episode.jpg") })
        {
            Test("订阅分集无图：" + name, () =>
            {
                var row = SubscriptionRow(subscriptionType, new(1, "重逢", "", [], []) { ArtworkUrl = url });
                Assert.Null(RowProperty(row, "Artwork"));
                Assert.Equal("Collapsed", RowProperty(row, "ArtworkVisibility")?.ToString(), "未取得图片不保留空图片区域");
            });
            Test("普通分集无图：" + name, () =>
            {
                var row = MediaRow(mediaType, new(1, "重逢", "", "", url, null));
                Assert.Null(RowProperty(row, "Artwork"));
                Assert.Equal("Collapsed", RowProperty(row, "ArtworkVisibility")?.ToString());
            });
        }
        Test("季订阅状态动态绑定：心形、提示及名称随原行更新", () =>
        {
            var xaml = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotMediaSeasonsView.xaml"));
            Assert.Contains("Glyph=\"{x:Bind HeartGlyph, Mode=OneWay}\"", xaml);
            Assert.Contains("ToolTipService.ToolTip=\"{x:Bind SubscribeLabel, Mode=OneWay}\"", xaml);
            Assert.Contains("AutomationProperties.Name=\"{x:Bind SubscribeLabel, Mode=OneWay}\"", xaml);
        });
    }

    private static object SubscriptionRow(Type type, MoviePilotSubscriptionEpisode episode)
    {
        // Only the actual row's constructor/properties run. The generic delegate types belong to
        // the loaded Shell; invoking an action would fail before any playback or search work.
        var play = typeof(MoviePilotSubscriptionPageStateTests).GetMethod(nameof(RejectPlay), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type).CreateDelegate(typeof(Func<,>).MakeGenericType(type, typeof(Task)));
        var search = typeof(MoviePilotSubscriptionPageStateTests).GetMethod(nameof(RejectSearch), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type).CreateDelegate(typeof(Action<>).MakeGenericType(type));
        Func<bool> unavailable = () => false;
        return Activator.CreateInstance(type, [episode, unavailable, unavailable, play, unavailable, search])!;
    }

    private static object MediaRow(Type type, MoviePilotMediaEpisode episode) =>
        Activator.CreateInstance(type, [episode, null, (Action)(() => throw new AssertionException("分集状态夹具不得搜索"))])!;

    private static object? RowProperty(object row, string property) =>
        (row.GetType().GetProperty(property, Members) ?? throw new AssertionException("当前Shell分集行缺少属性：" + property)).GetValue(row);

    private static Task RejectPlay<TRow>(TRow row) => throw new AssertionException("分集状态夹具不得播放");
    private static void RejectSearch<TRow>(TRow row) => throw new AssertionException("分集状态夹具不得搜索");

    private sealed class Fixture : IDisposable
    {
        private readonly Type _type;
        private readonly MoviePilotClient _client;
        private readonly MoviePilotService _service;
        public SubscriptionTransport Transport { get; } = new();
        public object Vm { get; }
        public Fixture(Type type, string? key, string kind)
        {
            _type = type;
            Transport.Kind = kind;
            var settings = new AppSettings();
            settings.MoviePilot.Enabled = true;
            settings.MoviePilot.Url = "http://192.0.2.10:3001";
            settings.MoviePilot.Username = "anonymous-fixture";
            settings.MoviePilot.ProtectedPassword = "not-a-secret-fixture";
            _client = new MoviePilotClient(Transport);
            _service = new MoviePilotService(_client, new MoviePilotCredentials(new NoSecrets()), settings);
            var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", Members)!.Invoke(_service, null)!;
            var cache = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
            var session = Activator.CreateInstance(cache, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
            typeof(MoviePilotService).GetField("_session", Members)!.SetValue(_service, session);
            typeof(MoviePilotService).GetField("_availableIdentity", Members)!.SetValue(_service, identity);
            Vm = Activator.CreateInstance(type)!;
            if (key is not null) _type.GetMethod("UsePageCategory", Members)?.Invoke(Vm, [key]); // Old code lacks the page-only rule.
            _type.GetMethod("Attach", Members)!.Invoke(Vm, [_service, null]);
        }
        public void SetCategory(string key) => _type.GetMethod("UsePageCategory", Members)!.Invoke(Vm, [key]);
        public object Get(string property) => _type.GetProperty(property, Members)!.GetValue(Vm)!;
        public Task StartRead() => (Task)_type.GetMethod("ReloadAsync", Members)!.Invoke(Vm, null)!;
        public void Read() => StartRead().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        public void Dispose()
        {
            _type.GetMethod("Cancel", Members)!.Invoke(Vm, null);
            ((IDisposable)Vm).Dispose();
            _service.Dispose(); _client.Dispose(); Transport.Dispose();
        }
        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string text) => throw new AssertionException("夹具不得保护凭据");
            public string Unprotect(string text) => throw new AssertionException("夹具不得解开凭据或登录");
        }
    }

    private sealed class RejectAttachSecrets : ISecretProtector
    {
        public string Protect(string text) => throw new AssertionException("无效配置 Attach 不得保护凭据");
        public string Unprotect(string text) => throw new AssertionException("无效配置 Attach 不得解开凭据");
    }

    private sealed class SubscriptionTransport : HttpMessageHandler
    {
        public string Kind { get; set; } = "series";
        public bool Fail { get; set; }
        public bool Hold { get; set; }
        public int Writes { get; private set; }
        private readonly TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri?.AbsolutePath != "/api/v1/subscribe/")
            {
                Writes++;
                throw new AssertionException("订阅夹具仅允许匿名列表读取，不登录／写入／取图");
            }
            if (Hold) await _pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var body = Fail ? "{\"success\":false,\"message\":\"anonymous fixture error\"}" : JsonSerializer.Serialize(
                (Kind == "both" ? new[] { "movie", "series" } : new[] { Kind }).Select((kind, index) => new
                { id = index + 1, name = "Fixture", type = kind == "series" ? "电视剧" : "电影", media_source = "themoviedb", media_id = "42", state = "N" }));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
        public void Release() => _pending.TrySetResult();
    }
}
