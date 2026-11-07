using System.Collections;
using System.Collections.Specialized;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual Release detail VM and cards with anonymous read-only HTTP, no XAML/native window.</summary>
internal static class DetailDeferredCardsTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(string shellPath, string root)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var shell = new ShellFixture(shellPath, root);
            Test("P3 入口契约：更多菜单先保留当前集，封面入口不物化集卡", () =>
            {
                var source = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "DetailPage.xaml.cs"));
                var more = MethodSource(source, "private void OnMoreClicked(");
                var ensure = more.IndexOf("ViewModel.EnsureCurrentEpisodeCard();", StringComparison.Ordinal);
                var construct = more.IndexOf("new CardItem(", StringComparison.Ordinal);
                Assert.True(ensure >= 0 && construct > ensure,
                    "实际OnMoreClicked必须在构造临时菜单CardItem前保留当前集状态卡");
                Assert.False(MethodSource(source, "internal bool OpenCoverPanel(")
                    .Contains("EnsureCurrentEpisodeCard", StringComparison.Ordinal), "封面入口不负责更多菜单状态保留");
            });
            Case("P3 真详情：2000集保留播放全量索引，深处只按需造卡", async () =>
            {
                using var fixture = new Fixture(shell, 2000);
                await fixture.Load();
                Assert.Equal(2000, fixture.Episodes.Count);
                Assert.Equal(100, fixture.Cards.Count);
                Assert.True(fixture.Created <= 1, "episode页最多先保留当前那张状态卡");
                Assert.Equal("episode-1500", fixture.CardItem(fixture.Cards[0]!).Id);
                Assert.True(fixture.Created <= 2);
                Assert.Equal(0, fixture.Get<int>("EpisodeFocus"));
                Assert.True(fixture.Episodes[499].Id == "episode-499" && fixture.Episodes[1500].Id == "episode-1500");
            });
            Case("P3 真详情：离页清掉按需源，不复活卡片或图片需求", async () =>
            {
                using var fixture = new Fixture(shell, 2000);
                await fixture.Load();
                var cards = fixture.Cards;
                fixture.Invoke("Cancel");
                Assert.Equal(0, cards.Count);
                Assert.Equal(0, fixture.Created);
                Assert.Equal(1, fixture.Transport.EpisodeReads);
            });
            Case("P3 真详情：读取取消后旧2000集响应不回填", async () =>
            {
                using var fixture = new Fixture(shell, 2000);
                var gate = fixture.Transport.Hold = new();
                var loading = fixture.Load();
                await gate.Entered.Task;
                fixture.Invoke("Cancel");
                gate.Release.TrySetResult();
                try { await loading; }
                catch (OperationCanceledException) { }
                Assert.Equal(0, fixture.Cards.Count);
                Assert.Equal(0, fixture.Episodes.Count);
                Assert.Equal(0, fixture.Created);
            });
            Case("P3 真详情：Reset通知中取消不能迟到提交播放目标或信息", async () =>
            {
                using var fixture = new Fixture(shell, 2000, EmbyItemType.Series);
                ((INotifyCollectionChanged)fixture.Cards).CollectionChanged += (_, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Reset && fixture.Cards.Count > 0)
                        fixture.Invoke("Cancel");
                };
                await fixture.Load();
                Assert.Null(fixture.Get<EmbyItem?>("PlayTarget"));
                Assert.Equal(0, fixture.Cards.Count);
                Assert.Equal(0, fixture.Created);
            });
            Case("P3 真详情：重新Attach不会让旧读取填新货架", async () =>
            {
                using var fixture = new Fixture(shell, 2000);
                var gate = fixture.Transport.Hold = new();
                var loading = fixture.Load();
                await gate.Entered.Task;
                var old = fixture.Cards;
                fixture.Attach("new-episode");
                gate.Release.TrySetResult();
                try { await loading; }
                catch (OperationCanceledException) { }
                Assert.Equal(0, fixture.Cards.Count);
                Assert.Equal(0, old.Count);
                Assert.Equal("new-episode", fixture.Get<EmbyItem>("CurrentItem").Id);
            });
            Case("P3 真详情：季选择投影本季，删除未访问季保持其它实例", async () =>
            {
                using var fixture = new Fixture(shell, 2000);
                await fixture.Load();
                var keep = fixture.Cards[0]!;
                var deletedSeason = fixture.Scope.Bind(new EmbyItem
                { Id = "season-3", Name = "第3季", Type = EmbyItemType.Season, SeriesId = "series", IndexNumber = 3 });
                var seasonCard = fixture.CreateCard(deletedSeason);
                var shelf = fixture.Get<object>("SeasonShelf");
                ((IList)shell.Get<object>(shelf, "Cards")).Add(seasonCard);
                var publishedEpisodes = -1;
                ((INotifyCollectionChanged)fixture.Cards).CollectionChanged += (_, _) =>
                    publishedEpisodes = fixture.Episodes.Count;
                await (Task)fixture.Invoke("RemoveAsync", seasonCard)!;
                Assert.Equal(1900, fixture.Episodes.Count);
                Assert.Equal(100, fixture.Cards.Count);
                Assert.Equal(-1, publishedEpisodes, "删除未展示季不应重发当前季集合通知");
                Assert.True(ReferenceEquals(keep, fixture.Cards[0]));
                Assert.True(fixture.Created <= 2, "删除一个季不能物化其它1999集");
                Assert.Equal("episode-400", fixture.Episodes[300].Id);
            });
            Case("P3 真详情：More当前集只保留一张状态卡，临时菜单回收后首次滚入仍显示确认状态", async () =>
            {
                using var fixture = new Fixture(shell, 2000);
                await fixture.Load();
                // Exercise the actual VM state-preservation method with real CardItem publication.
                // The source contract above separately checks the OnMoreClicked caller ordering;
                // neither check claims rendered menu or physical input coverage.
                var otherSeason = fixture.Scope.Bind(new EmbyItem
                {
                    Id = "season-19",
                    Type = EmbyItemType.Season,
                    SeriesId = "series",
                    IndexNumber = 19,
                    Name = "第19季"
                });
                var seasons = (IList)fixture.Get<object>("Seasons");
                seasons.Add(otherSeason);
                seasons.Add(fixture.Scope.Bind(new EmbyItem
                {
                    Id = "season-15",
                    Type = EmbyItemType.Season,
                    SeriesId = "series",
                    IndexNumber = 15,
                    Name = "第15季"
                }));
                fixture.Invoke("SelectEpisodeSeason", otherSeason);
                fixture.EnsureCurrentEpisode();
                var temporary = PublishMenuState(fixture);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Assert.False(temporary.IsAlive, "临时菜单卡应可回收");
                fixture.Invoke("SelectEpisodeSeason", seasons[1]);
                var card = fixture.Cards[0]!;
                Assert.True(shell.Get<bool>(card, "Watched"));
                Assert.True(shell.Get<bool>(card, "IsFavorite"));
                Assert.True(fixture.Created <= 1, "保留菜单状态不能创建全部集");
            });
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string MethodSource(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "真实详情页缺少入口：" + signature);
        var body = source.IndexOf('{', start);
        var depth = 0;
        for (var index = body; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }
        throw new AssertionException("真实入口的源码边界不完整：" + signature);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PublishMenuState(Fixture fixture)
    {
        var menu = fixture.CreateCard(fixture.Get<EmbyItem>("CurrentItem"));
        fixture.Call(menu, "ApplyUserState", new EmbyUserData { Played = true, IsFavorite = true }, true);
        return new WeakReference(menu);
    }

    private static void Case(string name, Func<Task> run) => Test(name, () => SingleThread.Run(run));

    private sealed class Fixture : IDisposable
    {
        private readonly ShellFixture _shell;
        private readonly EmbySession _session;
        private readonly EmbyImageStore _images;
        private readonly FakeSettings _settings = new();
        private readonly object _actions;
        private readonly Type _cardType;
        private EmbyItem _detail;

        public DetailReadTransport Transport { get; }
        public EmbySessionScope Scope { get; }
        public object Vm { get; }
        public IList Cards => (IList)Get<object>("EpisodeShelf").GetType().GetProperty("Cards")!.GetValue(Get<object>("EpisodeShelf"))!;
        public IReadOnlyList<EmbyItem> Episodes => Get<IReadOnlyList<EmbyItem>>("Episodes");
        public int Created => Cards.GetType().GetProperty("MaterializedCount", Members) is { } count
            ? (int)count.GetValue(Cards)! : Cards.Count;

        public Fixture(ShellFixture shell, int count, string type = EmbyItemType.Episode)
        {
            _shell = shell;
            Transport = new(count);
            var paths = new AppPaths(Path.Combine(shell.Root, "artifacts", "p3-long-series", "fixtures", Guid.NewGuid().ToString("N")));
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var secrets = new NoSecretIo();
            _session = new(_settings.Settings, new SettingsStore(paths, secrets), new CredentialVault(secrets), device, Transport);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri("https://p3-detail.invalid/emby/"), "placeholder-only",
                "fixture", "fixture", "fixture", device);
            Scope = new(_session, new(new EmbyClient(http, connection), new ServerProfile { Id = "p3" }, new AccountProfile { Id = "p3" }));
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!).Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, Scope);
            _images = new(_session, paths.ImageCacheDirectory);
            var assembly = typeof(ShellFixture).GetField("_viewModel", Members)!.GetValue(shell) as Type;
            var shellAssembly = assembly!.Assembly;
            _actions = DispatchProxy.Create(shellAssembly.GetType("Momoka.Shell.Views.IShellActions", true)!, typeof(DetailDeleteActionsProxy));
            _cardType = shellAssembly.GetType("Momoka.Shell.Views.CardItem", true)!;
            Vm = Activator.CreateInstance(shellAssembly.GetType("Momoka.Shell.ViewModels.DetailViewModel", true)!)!;
            _detail = Scope.Bind(DetailReadTransport.Episode(1500));
            if (type == EmbyItemType.Series) _detail = Scope.Bind(new EmbyItem { Id = "series", Name = "长剧", Type = type });
            Attach(_detail.Id);
        }

        public void Attach(string id)
        {
            _detail = Scope.Bind(new EmbyItem
            {
                Id = id,
                Name = "匿名长剧",
                Type = _detail.Type,
                SeriesId = "series",
                SeasonId = "season-15",
                SeasonName = "第15季",
                ParentIndexNumber = 15,
                IndexNumber = 1,
                UserData = new(),
                MediaSources = _detail.MediaSources
            });
            var requestType = Vm.GetType().Assembly.GetType("Momoka.Shell.Views.DetailRequest", true)!;
            var request = Activator.CreateInstance(requestType, nonPublic: true)!;
            requestType.GetProperty("Item")!.SetValue(request, _detail);
            Invoke("Attach", request, _actions, _settings, _session, _images);
            // Set only the server DTO. Running the actual episode reader preserves production
            // request/publication control flow without loading artwork or issuing unrelated HTTP.
            Vm.GetType().GetField("_detail", Members)!.SetValue(Vm, _detail);
        }

        public Task Load()
        {
            var token = (CancellationToken)Call(Vm, "BeginLoad")!;
            return (Task)Invoke("LoadEpisodesAsync", null, _detail.Type == EmbyItemType.Series, token)!;
        }

        public void EnsureCurrentEpisode() => Invoke("EnsureCurrentEpisodeCard");

        public object? Invoke(string name, params object?[] arguments) => Call(Vm, name, arguments);
        public object? Call(object target, string name, params object?[] arguments)
        {
            var method = target.GetType().GetMethods(Members).Single(candidate => candidate.Name == name
                && candidate.GetParameters().Length == arguments.Length);
            try { return method.Invoke(target, arguments); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
        public T Get<T>(string name) => _shell.Get<T>(Vm, name);
        public EmbyItem CardItem(object card) => _shell.Get<EmbyItem>(card, "Item");
        public object CreateCard(EmbyItem item) => _cardType.GetConstructors().Single().Invoke(
            _cardType.GetConstructors().Single().GetParameters().Select(parameter => parameter.Name switch
            { "item" => (object)item, "images" => _images, "width" => 170, _ => parameter.DefaultValue }).ToArray());
        public void Dispose() { ((IDisposable)Vm).Dispose(); _session.Dispose(); }
    }

    private sealed class NoSecretIo : ISecretProtector
    {
        public string Protect(string text) => throw new AssertionException("测试禁止保存凭据");
        public string Unprotect(string text) => throw new AssertionException("测试禁止读取凭据");
    }

    private sealed class ReadGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DetailReadTransport(int count) : HttpMessageHandler
    {
        public int EpisodeReads { get; private set; }
        public ReadGate? Hold { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.True(request.Method == HttpMethod.Get && request.RequestUri?.Host == "p3-detail.invalid",
                "只允许匿名服务器GET，不允许登录、播放或写入");
            var uri = request.RequestUri!;
            Assert.True(uri.AbsolutePath.EndsWith("/Shows/series/Episodes", StringComparison.Ordinal), "只应读取全剧集列表");
            Assert.False(uri.Query.Contains("SeasonId=", StringComparison.Ordinal), "长剧读数保持原全剧网络语义");
            EpisodeReads++;
            var json = JsonSerializer.Serialize(new ItemsResult
            { Items = Enumerable.Range(0, count).Reverse().Select(Episode).ToList(), TotalRecordCount = count });
            if (Hold is { } gate)
            {
                Hold = null;
                gate.Entered.TrySetResult();
                await gate.Release.Task.ConfigureAwait(false); // Late success deliberately ignores cancellation.
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        internal static EmbyItem Episode(int index) => new()
        {
            Id = "episode-" + index,
            Name = "第" + index + "集",
            Type = EmbyItemType.Episode,
            SeriesId = "series",
            SeasonId = "season-" + index / 100,
            SeasonName = "第" + index / 100 + "季",
            ParentIndexNumber = index / 100,
            IndexNumber = index % 100 + 1,
            PremiereDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index),
            UserData = new(),
            MediaSources = [new MediaSource
            {
                Id = "source-" + index, ItemId = "episode-" + index, Path = "/anonymous/episode.mkv",
                MediaStreams = [new MediaStream { Type = "Video", Codec = "h264" }]
            }]
        };
    }
}
