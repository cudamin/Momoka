using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class DetailActionReadinessTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(string shellPath, string sourceRoot)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var root = Path.GetFullPath(sourceRoot);
            var dll = Path.GetFullPath(shellPath);
            if (!File.Exists(dll) || !dll.StartsWith(Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new AssertionException("必须提供本树当前Shell Release DLL");
            var directory = Path.GetDirectoryName(dll)!;
            AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(directory, name.Name + ".dll"))
                ? context.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : null;
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
            RegisterShapeTests(assembly, root);

            Test("P1 主体Apply前不提前放开按钮", () =>
            {
                using var f = new Fixture(assembly, root, Shape.Movie);
                f.Transport.HoldDetail = true;
                var load = f.Load();
                f.Transport.WaitForDetail();
                Assert.True(f.Busy, "主条目仍在读取");
                Assert.False(f.Can("ToggleWatchedCommand"), "卡片预览不是已确认主体");
                Assert.False(f.Can("PlayCommand"), "卡片预览不是已确认播放目标");
                f.Transport.DetailRelease.TrySetResult(true);
                Await(load);
                Assert.True(f.Can("ToggleWatchedCommand"));
                Assert.True(f.Can("PlayCommand"));
            });

            Test("P1 电影主体Apply后按钮不再等相似推荐", () =>
            {
                using var f = new Fixture(assembly, root, Shape.Movie);
                f.Transport.HoldDetail = true;
                f.Transport.HoldSimilar = true;
                var load = f.Load();
                f.Transport.WaitForDetail();
                var watchedChanged = 0;
                ((ICommand)f.Get("ToggleWatchedCommand")!).CanExecuteChanged += (_, _) => Interlocked.Increment(ref watchedChanged);
                f.Transport.DetailRelease.TrySetResult(true);
                f.Transport.WaitForSimilar();
                Assert.True(f.Busy, "相似推荐未完成时整页仍应为 Busy");
                Assert.True(f.Can("PlayCommand"), "电影播放目标已在 Apply 建立");
                Assert.True(f.Can("RestartCommand"));
                Assert.True(f.Can("ToggleWatchedCommand"), "已观看不必等相似推荐");
                Assert.True(f.Can("ToggleFavoriteCommand"), "收藏不必等相似推荐");
                Assert.True(watchedChanged > 0, "主体就绪必须通知已观看命令");
                f.Transport.SimilarRelease.TrySetResult(true);
                Await(load);
                Assert.False(f.Busy);
            });

            Test("P1 单集主体Apply后播放不等兄弟集", () =>
            {
                using var f = new Fixture(assembly, root, Shape.Episode);
                f.Transport.HoldEpisodes = true;
                f.Transport.HoldSimilar = true;
                var load = f.Load();
                f.Transport.WaitForEpisodes();
                Assert.True(f.Busy, "兄弟集与相似推荐仍在读取");
                Assert.True(f.Can("PlayCommand"), "单集自己的播放目标已在 Apply 建立");
                Assert.True(f.Can("ToggleWatchedCommand"));
                f.Transport.EpisodesRelease.TrySetResult(true);
                f.Transport.SimilarRelease.TrySetResult(true);
                Await(load);
                Assert.False(f.Busy);
            });

            Test("P1 剧集播放等播放目标，已观看不附属链", () =>
            {
                using var f = new Fixture(assembly, root, Shape.Series);
                f.Transport.HoldEpisodes = true;
                f.Transport.HoldSimilar = true;
                var load = f.Load();
                f.Transport.WaitForEpisodes();
                Assert.True(f.Busy);
                Assert.True(f.Can("ToggleWatchedCommand"), "剧集主体已确认，已观看可先恢复");
                Assert.False(f.Can("PlayCommand"), "播放目标来自集列表，尚未就绪");
                f.Transport.EpisodesRelease.TrySetResult(true);
                Until(() => f.Can("PlayCommand"), "集列表返回后播放目标应恢复");
                Assert.True(f.Busy, "相似推荐仍挂起，整页仍为 Busy");
                f.Transport.SimilarRelease.TrySetResult(true);
                Await(load);
                Assert.False(f.Busy);
            });

            Test("P1 主体读取失败保持按钮禁用并报错", () =>
            {
                using var f = new Fixture(assembly, root, Shape.Movie);
                f.Transport.DetailStatus = HttpStatusCode.ServiceUnavailable;
                Await(f.Load());
                Assert.False(f.Busy);
                Assert.False(f.Can("ToggleWatchedCommand"));
                Assert.False(f.Can("PlayCommand"));
                Assert.True(f.Get("NoticeOpen") is true);
            });

            Test("P1 相似推荐失败不收回已恢复按钮", () =>
            {
                using var f = new Fixture(assembly, root, Shape.Movie);
                f.Transport.SimilarStatus = HttpStatusCode.InternalServerError;
                Await(f.Load());
                Assert.False(f.Busy);
                Assert.True(f.Can("ToggleWatchedCommand"));
                Assert.True(f.Can("PlayCommand"));
                Assert.True(f.Get("NoticeOpen") is not true, "相似推荐失败不应变成整页错误");
            });

            Test("P1 离页取消后迟到附属响应不能恢复按钮", () =>
            {
                using var f = new Fixture(assembly, root, Shape.Movie);
                f.Transport.HoldSimilar = true;
                var load = f.Load();
                f.Transport.WaitForSimilar();
                Assert.True(f.Can("ToggleWatchedCommand"));
                Invoke(f.Vm, "Cancel");
                Assert.False(f.Can("ToggleWatchedCommand"));
                f.Transport.SimilarRelease.TrySetResult(true);
                Await(load);
                Assert.False(f.Can("ToggleWatchedCommand"));
                Assert.False(f.Can("PlayCommand"));
            });

            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static object? Invoke(object target, string name, params object?[] args)
    {
        try { return target.GetType().GetMethod(name, Members)!.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private static void Await(Task task)
    {
        try { task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); }
        catch (TimeoutException) { throw new AssertionException("等待详情加载超时"); }
    }

    private static void Until(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new AssertionException("等待超时：" + message);
            Thread.Sleep(10);
        }
    }

    private enum Shape { Movie, Episode, Series, Season }

    private sealed class Fixture : IDisposable
    {
        private readonly Assembly _assembly;
        private readonly FakeSettings _settings = new();
        private readonly EmbySession _session;
        private readonly EmbySessionScope _scope;
        private readonly EmbyImageStore _images;
        public ReadinessTransport Transport { get; } = new();
        public DetailDeleteActionsProxy Actions { get; }
        public object Vm { get; }
        public UiSettings Ui => _settings.Settings.Ui;
        public EmbyItem Seed { get; }
        public bool Busy => Get("Busy") is true;

        public Fixture(Assembly assembly, string root, Shape shape, bool stub = false)
        {
            _assembly = assembly;
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var secrets = new NoSecrets();
            var output = Path.Combine(root, "artifacts", "p1-detail-actions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            _session = new EmbySession(_settings.Settings, new SettingsStore(new AppPaths(output + "|no-settings"), secrets), new CredentialVault(secrets), device, Transport);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri("https://detail-readiness.invalid/emby/"), "placeholder-only", "fixture", "fixture", "fixture", device)
            { ServerKind = MediaServerKind.Emby };
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = "fixture", ServerKind = MediaServerKind.Emby }, new AccountProfile { Id = "fixture" });
            _scope = new EmbySessionScope(_session, identity);
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!).Add(_scope.Key, _scope);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, _scope);
            _images = new EmbyImageStore(_session, Path.Combine(output, "images"));
            Actions = (DetailDeleteActionsProxy)DispatchProxy.Create(assembly.GetType("Momoka.Shell.Views.IShellActions", true)!, typeof(DetailDeleteActionsProxy));
            Vm = Activator.CreateInstance(assembly.GetType("Momoka.Shell.ViewModels.DetailViewModel", true)!)!;

            var series = _scope.Bind(new EmbyItem
            {
                Id = "series",
                Name = "anonymous series",
                Type = EmbyItemType.Series
            });
            var season = _scope.Bind(new EmbyItem
            {
                Id = "season",
                Name = "anonymous season",
                Type = EmbyItemType.Season,
                SeriesId = series.Id,
                IndexNumber = 1,
                ParentIndexNumber = 1
            });
            var episode = _scope.Bind(new EmbyItem
            {
                Id = "episode",
                Name = "anonymous episode",
                Type = EmbyItemType.Episode,
                SeriesId = series.Id,
                SeasonId = "season",
                IndexNumber = 1,
                ParentIndexNumber = 1,
                RunTimeTicks = 600_000_000,
                MediaSources = [Source(episodeId: "episode")]
            });
            var movie = _scope.Bind(new EmbyItem
            {
                Id = "movie",
                Name = "anonymous movie",
                Type = EmbyItemType.Movie,
                RunTimeTicks = 600_000_000,
                MediaSources = [Source(episodeId: "movie")]
            });
            Transport.Catalog["series"] = series;
            Transport.Catalog["season"] = season;
            Transport.Catalog["episode"] = episode;
            Transport.Catalog["movie"] = movie;
            Transport.Seasons.Add(season);
            Transport.Episodes.Add(episode);
            Transport.Similar.Add(_scope.Bind(new EmbyItem
            {
                Id = "similar",
                Name = "anonymous similar",
                Type = EmbyItemType.Movie
            }));

            Seed = shape switch { Shape.Episode => episode, Shape.Series => series, Shape.Season => season, _ => movie };
            Seed.IsStub = stub;
            Reattach(Seed);
        }

        public void Reattach(EmbyItem item)
        {
            var assembly = _assembly;
            var request = Activator.CreateInstance(assembly.GetType("Momoka.Shell.Views.DetailRequest", true)!, nonPublic: true)!;
            request.GetType().GetProperty("Item")!.SetValue(request, item);
            Invoke(Vm, "Attach", request, Actions, _settings, _session, _images);
        }

        public Task Load() => (Task)Vm.GetType().GetMethod("ReloadAsync", BindingFlags.Instance | BindingFlags.Public)!.Invoke(Vm, null)!;

        public object? Get(string name) => Vm.GetType().GetProperty(name, Members)!.GetValue(Vm);

        public bool Can(string command) => ((ICommand)Get(command)!).CanExecute(null);

        public void Dispose()
        {
            Transport.DetailRelease.TrySetResult(true);
            Transport.EpisodesRelease.TrySetResult(true);
            Transport.SimilarRelease.TrySetResult(true);
            ((IDisposable)Vm).Dispose();
            _session.Dispose();
        }

        private static MediaSource Source(string episodeId) => new()
        {
            Id = "source-" + episodeId,
            ItemId = episodeId,
            Path = "/anonymous/" + episodeId + ".mkv",
            Container = "mkv",
            Size = 123_456_789,
            RunTimeTicks = 600_000_000
        };

        private sealed class FakeSettings : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public int PageSize => 20;
            public event EventHandler? SaveStateChanged { add { } remove { } }
            public bool HasUnsavedChanges => false;
            public bool TrySave() => throw new AssertionException("不得保存设置");
            public void Save() => throw new AssertionException("不得保存设置");
            public ServerProfile ResolveServer(string url) => throw new AssertionException("不得读取账号");
            public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new AssertionException("不得读取账号");
            public void ExportPreferences(string path) => throw new AssertionException("不得设置IO");
            public AppSettings ReadBackup(string path) => throw new AssertionException("不得设置IO");
        }

        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string text) => throw new AssertionException("禁止凭据保护");
            public string Unprotect(string text) => throw new AssertionException("禁止凭据读取或登录");
        }
    }

    public sealed class ReadinessTransport : HttpMessageHandler
    {
        public Dictionary<string, EmbyItem> Catalog { get; } = new(StringComparer.Ordinal);
        public List<EmbyItem> Seasons { get; } = [];
        public List<EmbyItem> Episodes { get; } = [];
        public List<EmbyItem> Similar { get; } = [];
        public TaskCompletionSource<bool> DetailRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> DetailRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> EpisodesRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> EpisodesRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SimilarRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SimilarRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldDetail { get; set; }
        public bool HoldEpisodes { get; set; }
        public bool HoldSimilar { get; set; }
        public HttpStatusCode DetailStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode SimilarStatus { get; set; } = HttpStatusCode.OK;

        public void WaitForDetail() => Wait(DetailRequested, "详情请求");
        public void WaitForEpisodes() => Wait(EpisodesRequested, "集列表请求");
        public void WaitForSimilar() => Wait(SimilarRequested, "相似推荐请求");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/Images/", StringComparison.OrdinalIgnoreCase)) return Status(HttpStatusCode.NotFound);

            if (path.EndsWith("/Similar", StringComparison.OrdinalIgnoreCase))
            {
                SimilarRequested.TrySetResult(true);
                if (HoldSimilar) await SimilarRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return SimilarStatus == HttpStatusCode.OK ? Json(Items(Similar)) : Status(SimilarStatus);
            }

            if (path.Contains("/Seasons", StringComparison.OrdinalIgnoreCase))
                return Json(Items(Seasons));

            if (path.Contains("/Episodes", StringComparison.OrdinalIgnoreCase))
            {
                EpisodesRequested.TrySetResult(true);
                if (HoldEpisodes) await EpisodesRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return Json(Items(Episodes));
            }

            if (path.Contains("/Items/", StringComparison.OrdinalIgnoreCase))
            {
                DetailRequested.TrySetResult(true);
                if (HoldDetail) await DetailRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (DetailStatus != HttpStatusCode.OK) return Status(DetailStatus);
                var id = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
                return Catalog.TryGetValue(id, out var item) ? Json(item) : Status(HttpStatusCode.NotFound);
            }

            if (path.EndsWith("/Items", StringComparison.OrdinalIgnoreCase)) return Json(Items([]));
            return Status(HttpStatusCode.NotFound);
        }

        private static object Items(IReadOnlyList<EmbyItem> items) =>
            new { Items = items, TotalRecordCount = items.Count };

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, EmbyHttp.Json), Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Status(HttpStatusCode status) => new(status)
        {
            Content = new StringContent("")
        };

        private static void Wait(TaskCompletionSource<bool> signal, string what)
        {
            try { signal.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (TimeoutException) { throw new AssertionException("等待" + what + "超时"); }
        }
    }
}
