using System.Net;
using System.Reflection;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Plex;
using Momoka.Services;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PlexLibraryViewTests
{
    internal static int Run(string shellPath, string root)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var shell = new ShellFixture(shellPath, root);
        foreach (var operation in new[] { "RetryPlexFiltersAsync", "ClearPlexFiltersAsync" })
            Test("Plex 实际VM：原生排序目录变空后普通浏览恢复 " + operation, () => SingleThread.Run(async () =>
            {
                using var f = new Fixture(shell);
                await f.Load();
                Assert.Equal("titleSort", f.Get<string>("SortKey"));
                var reads = f.Transport.Requests.Count(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal));
                f.Transport.Empty = true;
                await (Task)shell.Invoke(f.Vm, operation)!;
                Assert.False(f.Get<bool>("NoticeOpen"));
                Assert.True(f.Transport.Requests.Count(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal)) > reads);
                Assert.True(f.Many("Cards").Count > 0);
                Assert.Equal(EmbySortBy.Name, f.Query().SortBy);
                Assert.Null(f.Query().PlexSortKey);
            }));
        Test("Plex 实际VM：正式页面取消恢复后首读与暖返回仍可筛选", () => SingleThread.Run(async () =>
        {
            using var f = new Fixture(shell);
            shell.Invoke(f.Vm, "Cancel");
            shell.Invoke(f.Vm, "Resume", false);
            await f.Load();
            Assert.Equal(4, f.Many("PlexFilterRows").Count);
            Assert.False(f.Get<bool>("PlexFilterLoading"));
            await f.Select("genre", "7");
            shell.Invoke(f.Vm, "Cancel");
            var request = f.Vm.GetType().GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Vm)!;
            Assert.True((bool)shell.Invoke(f.Vm, "TryResumeNavigation", request, f.Settings)!);
            await f.Select("genre", "8");
            Assert.Equal("8", f.Settings.Settings.Ui.PlexFilters.Single().Value.Values["genre"].Single());
            Assert.False(f.Get<bool>("PlexFilterLoading"));
        }));
        Test("Plex 实际VM：显式仅媒体类型遇空目录拒绝，初次未选类型可浏览", () => SingleThread.Run(async () =>
        {
            using var f = new Fixture(shell);
            f.Transport.Empty = true;
            await f.Load();
            Assert.True(f.Get<bool>("IsReady"));
            f.Transport.Empty = false;
            await (Task)shell.Invoke(f.Vm, "RetryPlexFiltersAsync")!;
            await (Task)shell.Invoke(f.Vm, "SelectPlexTypeAsync", "4")!;
            Assert.Equal("4", f.Settings.Settings.Ui.PlexFilters.Single().Value.Type);
            f.Transport.Empty = true;
            f.Reattach();
            Assert.True(f.Query().PlexFilters!.HasConditions);
            Assert.Equal(true, f.Query().PlexFilters!.TypeIsExplicit);
            var reads = f.Transport.Requests.Count(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal));
            await f.Load();
            Assert.Equal(reads, f.Transport.Requests.Count(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal)));
            Assert.True(f.Get<string>("PlexFilterError").Length > 0);
            await (Task)shell.Invoke(f.Vm, "ClearPlexFiltersAsync")!;
            Assert.True(f.Transport.Requests.Count(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal)) > reads);
        }));
        Test("Plex 实际VM：同字段选项响应逆序完成保留最后选择", () => SingleThread.Run(async () =>
        {
            using var f = new Fixture(shell);
            await f.Load();
            f.Transport.OptionGates = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
            var first = f.Select("genre", "7");
            await f.Transport.FirstOptionEntered.Task;
            var second = f.Select("genre", "8");
            await f.Transport.SecondOptionEntered.Task;
            f.Transport.OptionGates[1].SetResult();
            await second;
            f.Transport.OptionGates[0].SetResult();
            await first;
            Assert.Equal("8", f.Query().PlexFilters!.Values["genre"].Single());
            Assert.Equal("8", f.Settings.Settings.Ui.PlexFilters.Single().Value.Values["genre"].Single());
        }));
        Test("Plex 实际VM：目录加载后筛选排序显现，选择写入同源库且查询带原生值", () => SingleThread.Run(async () =>
        {
            using var f = new Fixture(shell);
            await f.Load();
            Assert.Equal("Visible", f.Get<object>("FilterVisibility").ToString());
            Assert.Equal(4, f.Many("PlexFilterRows").Count);
            Assert.Equal("titleSort", f.Get<string>("SortKey"));
            await f.Select("genre", "7");
            Assert.Contains("genre=7", f.Transport.Requests.Last().Query);
            Assert.Equal(1, f.Get<int>("FilterCount"));
            Assert.Equal("7", f.Settings.Settings.Ui.PlexFilters.Single().Value.Values["genre"].Single());
            Assert.Equal(0, f.Settings.Settings.Ui.Filters.Count);
            Assert.Equal(0, f.Query().StartIndex);
        }));
        Test("Plex 实际VM：字母计数落在正确卡片，筛选变化后缓存身份不同", () => SingleThread.Run(async () =>
        {
            using var f = new Fixture(shell);
            await f.Load();
            var before = f.ContentKey();
            var jump = await (Task<(int Index, string Letter)?>)shell.Invoke(f.Vm, "JumpToAsync", "B")!;
            Assert.Equal((3, "B"), jump!.Value);
            Assert.True(f.Transport.Requests.Any(uri => uri.AbsolutePath.EndsWith("/firstCharacter", StringComparison.Ordinal)));
            await f.Select("year", "2025");
            Assert.False(before == f.ContentKey());
            var count = f.Transport.Requests.Count;
            f.Scope.IdentityState.Lifetime.Cancel();
            await f.Select("genre", "7");
            Assert.Equal(count, f.Transport.Requests.Count);
        }));
        Test("Plex 实际VM：目录读取失败可重试，空目录隐藏，恢复后重新显示", () => SingleThread.Run(async () =>
        {
            using var f = new Fixture(shell);
            f.Transport.Fail = true;
            await f.Load();
            Assert.True(f.Get<string>("PlexFilterError").Length > 0);
            f.Transport.Fail = false;
            await (Task)shell.Invoke(f.Vm, "RetryPlexFiltersAsync")!;
            Assert.Equal(4, f.Many("PlexFilterRows").Count);
            Assert.Equal("", f.Get<string>("PlexFilterError"));
            f.Transport.Empty = true;
            await (Task)shell.Invoke(f.Vm, "RetryPlexFiltersAsync")!;
            Assert.Equal("Collapsed", f.Get<object>("FilterVisibility").ToString());
            f.Transport.Empty = false;
            await (Task)shell.Invoke(f.Vm, "RetryPlexFiltersAsync")!;
            Assert.Equal("Visible", f.Get<object>("FilterVisibility").ToString());
        }));
        Test("Plex 实际VM：失效的保存选择不能读取全库，清除后可以恢复", () => SingleThread.Run(async () =>
        {
            using var f = new Fixture(shell);
            // The product chooses its own per-library key; seed it through a first attach rather than guess that format.
            await f.Load();
            await f.Select("genre", "7");
            var key = f.Settings.Settings.Ui.PlexFilters.Keys.Single();
            f.Settings.Settings.Ui.PlexFilters[key].Values["genre"] = ["missing"];
            f.Reattach();
            var reads = f.Transport.Requests.Count(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal));
            await f.Load();
            Assert.Equal(reads, f.Transport.Requests.Count(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal)));
            Assert.Equal("Visible", f.Get<object>("RetryVisibility").ToString());
            await (Task)shell.Invoke(f.Vm, "ClearPlexFiltersAsync")!;
            Assert.True(f.Transport.Requests.Count(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal)) > reads);
        }));
        Test("Plex 实际VM：离页后的迟到目录不回填新访问", () => SingleThread.Run(async () =>
        {
            using var f = new Fixture(shell);
            f.Transport.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = f.Load();
            await f.Transport.Entered.Task;
            shell.Invoke(f.Vm, "Cancel");
            f.Transport.Gate.SetResult();
            await loading;
            Assert.Equal(0, f.Many("PlexFilterRows").Count);
            Assert.False(f.Get<bool>("PlexFilterOpen"));
        }));
        return TestHarness.Run();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ShellFixture _shell;
        private readonly EmbySession _session;
        private readonly EmbyImageStore _images;
        private readonly object _request;
        public ViewTransport Transport { get; } = new();
        public FakeSettings Settings { get; } = new();
        public EmbySessionScope Scope { get; }
        public object Vm { get; private set; }
        public Fixture(ShellFixture shell)
        {
            _shell = shell;
            var device = new DeviceIdentity("plex-library-fixture", "fixture", "fixture", "1");
            var paths = new AppPaths(Path.Combine(shell.Root, "artifacts", "plex-library-vm", "IO|forbidden"));
            _session = new EmbySession(Settings.Settings, new SettingsStore(paths, new NoSecrets()), new CredentialVault(new NoSecrets()), device, Transport);
            var connection = new EmbyConnection(new Uri("https://view-plex.invalid/"), "placeholder-only", "user", "user", "server", device) { ServerKind = MediaServerKind.Plex };
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            Scope = new EmbySessionScope(_session, new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = "plex-view", ServerKind = MediaServerKind.Plex }, new AccountProfile { Id = "view-account" }));
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!).Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_session, Scope);
            _images = new EmbyImageStore(_session, Path.Combine(shell.Root, "artifacts", "plex-library-vm", "images"));
            var assembly = ShellAssembly();
            _request = Activator.CreateInstance(assembly.GetType("Momoka.Shell.Views.LibraryRequest", true)!)!;
            shell.Set(_request, "Services", new NoServices());
            shell.Set(_request, "Title", "离线库");
            shell.Set(_request, "Source", Scope);
            shell.Set(_request, "ParentId", "1");
            shell.Set(_request, "CollectionType", "movies");
            Vm = Create();
        }
        private static Assembly ShellAssembly() => AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetType("Momoka.Shell.Views.LibraryRequest") is not null);
        private object Create() => _shell.CreateLibrary(_session, _images, Settings, Scope, new BrowseSnapshotStore(), _request, "movies", null, LibrarySection.Items);
        public void Reattach() { ((IDisposable)Vm).Dispose(); Vm = Create(); }
        public Task Load() => (Task)_shell.Invoke(Vm, "LoadAsync", true)!;
        public Task Select(string field, string value) => (Task)_shell.Invoke(Vm, "SelectPlexFilterAsync", field, value)!;
        public T Get<T>(string name) => _shell.Get<T>(Vm, name);
        public List<object> Many(string name) => ((System.Collections.IEnumerable)Get<object>(name)).Cast<object>().ToList();
        public ItemQuery Query() => (ItemQuery)_shell.Invoke(Vm, "BuildQuery", 0)!;
        public string ContentKey() => (string)_shell.Invoke(Vm, "NavigationContentKey")!;
        public void Dispose() { ((IDisposable)Vm).Dispose(); _session.Dispose(); Transport.Dispose(); }
    }

    private sealed class ViewTransport : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public bool Empty { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource[]? OptionGates { get; set; }
        public TaskCompletionSource FirstOptionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondOptionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _optionReads;
        public List<Uri> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            lock (Requests) Requests.Add(uri);
            if (uri.AbsolutePath == "/library/sections/1/genre" && OptionGates is { } gates)
            {
                var read = Interlocked.Increment(ref _optionReads) - 1;
                if (read < gates.Length)
                {
                    (read == 0 ? FirstOptionEntered : SecondOptionEntered).TrySetResult();
                    await gates[read].Task;
                }
            }
            if (uri.AbsolutePath == "/library/sections/1" && Gate is { } gate) { Entered.TrySetResult(); await gate.Task; }
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var json = uri.AbsolutePath switch
            {
                "/library/sections" => """{"Directory":[{"key":"1","type":"movie","title":"电影","filters":true}]}""",
                "/library/sections/1" => Empty ? """{"Type":[]}""" : """{"Type":[{"type":"movie","key":"/library/sections/1/all?type=1","title":"电影","Filter":[{"filter":"genre","filterType":"string","key":"/library/sections/1/genre?type=1","title":"类型"},{"filter":"year","filterType":"integer","key":"/library/sections/1/year?type=1","title":"年份"},{"filter":"unwatched","filterType":"boolean","title":"已看状态"},{"filter":"resolution","filterType":"string","key":"/library/sections/1/resolution?type=1","title":"分辨率"}],"Sort":[{"key":"titleSort","descKey":"titleSort:desc","title":"标题","default":"asc","firstCharacterKey":"firstCharacter"},{"key":"addedAt","descKey":"addedAt:desc","title":"添加时间","default":"desc"}]}]}""",
                "/library/sections/1/genre" => """{"Directory":[{"key":"7","title":"动画"},{"key":"8","title":"剧情"}]}""",
                "/library/sections/1/year" => """{"Directory":[{"key":"2025","title":"2025"}]}""",
                "/library/sections/1/resolution" => """{"Directory":[{"key":"4k","title":"4K"}]}""",
                "/library/sections/1/firstCharacter" => """{"Directory":[{"key":"A","title":"A","size":3},{"key":"B","title":"B","size":40}]}""",
                "/library/sections/1/all" => JsonSerializer.Serialize(new { Metadata = Enumerable.Range(0, 43).Skip(Header("X-Plex-Container-Start")).Take(Header("X-Plex-Container-Size")).Select(index => new { ratingKey = index.ToString(), type = "movie", title = (index < 3 ? "A" : "B") + index, titleSort = (index < 3 ? "A" : "B") + index }), totalSize = 43 }),
                _ => throw new AssertionException("未授权的夹具请求 " + uri.AbsolutePath)
            };
            if (uri.AbsolutePath == "/library/sections/1" && !Empty)
            {
                var container = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
                container["Type"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"type":"episode","key":"/library/sections/1/all?type=4","title":"单集","Filter":[],"Sort":[{"key":"titleSort","descKey":"titleSort:desc","title":"标题"}]}"""));
                json = container.ToJsonString();
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"MediaContainer\":" + json + "}") };
            int Header(string key) => request.Headers.TryGetValues(key, out var values) && int.TryParse(values.Single(), out var value) ? value : 0;
        }
    }
    private sealed class NoServices : IServiceProvider { public object? GetService(Type serviceType) => null; }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("禁止凭据 IO");
        public string Unprotect(string cipherText) => throw new AssertionException("禁止凭据 IO");
    }
}
