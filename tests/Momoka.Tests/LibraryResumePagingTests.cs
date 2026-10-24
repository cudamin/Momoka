using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>
/// Runs the actual LibraryViewModel, ResumeSlot and CardItem against an injected HTTP handler.
/// Sources are installed in fixture memory, without sign-in/restore, secret IO, WinUI activation or playback.
/// The single-thread pump models dispatch ordering, not a real WinUI window or its bindings.
/// </summary>
internal static class LibraryResumePagingTests
{
    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("继续观看真实 VM 分页回归（假 HTTP／单线程调度；无登录、窗口或播放）");
        try
        {
            var shell = new ShellFixture(shellAssemblyPath, sourceRoot);
            Register(shell);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Register(ShellFixture shell)
    {
        Case("R-013 首页仍使用 Resume 路由，20/21 条保留下一页", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            Assert.Equal(20, fixture.Cards.Count);
            Assert.True(fixture.HasMore);
            Assert.Equal("共 21 项", fixture.TotalLabel);
            Assert.Equal("a:0", fixture.Transport.Asked());
        });
        Case("R-013 补入无续播下一集后，从19读取原未读尾项", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            await fixture.Watch("a-0", "synthetic-1");
            Assert.Equal(20, fixture.Cards.Count);
            Assert.Equal("共 20 项", fixture.TotalLabel);
            Assert.True(fixture.HasMore);
            await fixture.Load(reset: false);
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.True(fixture.Ids().Contains("synthetic-1"));
            Assert.Equal("a:0,a:19", fixture.Transport.Asked());
            Assert.False(fixture.HasMore);
        });
        Case("R-013 已读完的20条补下一集，不伪造服务器总数或多读", async () =>
        {
            using var fixture = new Fixture(shell, 20);
            await fixture.Load();
            await fixture.Watch("a-0", "synthetic-1");
            Assert.Equal(20, fixture.Cards.Count);
            Assert.Equal("共 19 项", fixture.TotalLabel);
            Assert.False(fixture.HasMore);
            await fixture.Load(reset: false);
            Assert.Equal("a:0", fixture.Transport.Asked());
        });
        foreach (var kind in new[] { "movie", "no-next", "existing-next" })
            Case($"R-013 {kind} 撤格后原未读尾项仍可读取", async () =>
            {
                using var fixture = new Fixture(shell, 21);
                if (kind == "movie") fixture.Transport.Rows["a"][0].Type = EmbyItemType.Movie;
                await fixture.Load();
                await fixture.Watch("a-0", kind == "existing-next" ? "a-1" : null);
                Assert.Equal(19, fixture.Cards.Count);
                Assert.Equal("共 20 项", fixture.TotalLabel);
                await fixture.Load(reset: false);
                Assert.True(fixture.Ids().Contains("a-20"));
                Assert.Equal("a:0,a:19", fixture.Transport.Asked());
                Assert.Equal(20, fixture.Ids().Distinct(StringComparer.Ordinal).Count());
            });
        Case("R-013 合成下一集再次已看，不再次回退服务器游标", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            await fixture.Watch("a-0", "synthetic-1");
            await fixture.Watch("synthetic-1", "synthetic-2");
            Assert.Equal("共 20 项", fixture.TotalLabel);
            await fixture.Load(reset: false);
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.True(fixture.Ids().Contains("synthetic-2"));
            Assert.Equal("a:0,a:19", fixture.Transport.Asked());
        });
        Case("R-013 已存在的原始下一集再次已看，只回退各自原记录", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            await fixture.Watch("a-0", "a-1");
            await fixture.Watch("a-1", "synthetic-2");
            Assert.Equal("共 19 项", fixture.TotalLabel);
            await fixture.Load(reset: false);
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.Equal("a:0,a:18", fixture.Transport.Asked());
        });
        foreach (var captureBefore in new[] { true, false })
            Case($"R-013 追加在飞时已看，拒绝{(captureBefore ? "旧快照" : "移位空页")}并从19重读", async () =>
            {
                using var fixture = new Fixture(shell, 21);
                await fixture.Load();
                var gate = fixture.Transport.HoldResume(captureBefore);
                var append = fixture.Load(reset: false);
                await gate.Entered.Task;
                await fixture.Watch("a-0", "synthetic-1");
                gate.Release.TrySetResult();
                await append;
                Assert.False(fixture.Ids().Contains("a-20"), "作废页不得发布任何条目");
                Assert.True(fixture.HasMore, "作废空页不能耗尽有效游标");
                Assert.False(fixture.Get<bool>("Busy"));
                await fixture.Load(reset: false);
                Assert.True(fixture.Ids().Contains("a-20"));
                Assert.Equal("a:0,a:20,a:19", fixture.Transport.Asked());
            });
        Case("R-013 HTTP已完成但UI尚未发布时已看，不消费排队的旧分页", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            var gate = fixture.Transport.HoldResume(captureBefore: true);
            var append = fixture.Load(reset: false);
            await gate.Entered.Task;
            gate.Release.TrySetResult();
            // 暂留在同一调度回调内：网络线程可以完成，VM发布回调只能排队，不能先执行。
            SingleThread.RunOnePost();
            SingleThread.WaitForPosts(1);
            Assert.Equal(20, fixture.CursorConsumed, "未发布旧页不能提前消费游标");
            await fixture.Watch("a-0", "synthetic-1");
            await append;
            Assert.False(fixture.Ids().Contains("a-20"));
            Assert.True(fixture.HasMore, "未发布旧页不能提前消费游标");
            await fixture.Load(reset: false);
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.Equal("a:0,a:20,a:19", fixture.Transport.Asked());
        });
        Case("R-013 下一集补读挂起期间，续播分页已经回退到19", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            var gate = fixture.Transport.HoldNext();
            var marked = fixture.Watch("a-0", "synthetic-1");
            await gate.Entered.Task;
            await fixture.Load(reset: false);
            Assert.Equal("a:0,a:19", fixture.Transport.Asked());
            Assert.True(fixture.Ids().Contains("a-20"));
            gate.Release.TrySetResult();
            await marked;
            Assert.True(fixture.Ids().Contains("synthetic-1"));
            Assert.Equal("共 20 项", fixture.TotalLabel);
        });
        Case("R-013 下一集与在飞分页尾项相同，不插重复卡也不重复扣总数", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            var gate = fixture.Transport.HoldNext();
            var marked = fixture.Watch("a-0", "a-20");
            await gate.Entered.Task;
            await fixture.Load(reset: false);
            gate.Release.TrySetResult();
            await marked;
            Assert.Equal(1, fixture.Ids().Count(id => id == "a-20"));
            Assert.Equal(20, fixture.Cards.Count);
            Assert.Equal("共 20 项", fixture.TotalLabel);
            Assert.False(fixture.HasMore);
        });
        Case("R-013 合成下一集后来进入服务器续播页，消费记录但不重复显示", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            await fixture.Watch("a-0", "synthetic-1");
            fixture.Transport.Rows["a"].Add(Item("synthetic-1"));
            await fixture.Load(reset: false);
            Assert.Equal(1, fixture.Ids().Count(id => id == "synthetic-1"));
            Assert.Equal("共 21 项", fixture.TotalLabel);
            Assert.False(fixture.HasMore);
            await fixture.Watch("synthetic-1", "synthetic-2");
            Assert.Equal("共 20 项", fixture.TotalLabel);
            Assert.False(fixture.HasMore);
        });
        Case("R-013 删除原始续播卡回退游标，删除合成槽不回退", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            await fixture.Watch("a-0", "synthetic-1");
            await fixture.Remove("synthetic-1");
            Assert.Equal("共 20 项", fixture.TotalLabel);
            await fixture.Remove("a-1");
            Assert.Equal("共 19 项", fixture.TotalLabel);
            await fixture.Load(reset: false);
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.Equal("a:0,a:18", fixture.Transport.Asked());
            Assert.False(fixture.HasMore);
        });
        Case("R-013 删除原始卡取消旧分页，迟到页不消费新偏移", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            var gate = fixture.Transport.HoldResume(captureBefore: true);
            var append = fixture.Load(reset: false);
            await gate.Entered.Task;
            await fixture.Remove("a-0");
            gate.Release.TrySetResult();
            await append;
            Assert.False(fixture.Ids().Contains("a-20"));
            await fixture.Load(reset: false);
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.Equal("a:0,a:20,a:19", fixture.Transport.Asked());
        });
        Case("R-013 单来源读取失败保留失败提示，刷新能重新读取", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            fixture.Transport.FailNextResume = true;
            await fixture.Load();
            Assert.True(fixture.Get<bool>("NoticeOpen"));
            Assert.False(fixture.Get<bool>("Busy"));
            Assert.True(fixture.HasMore, "读取失败不得当作已读空页");
            await fixture.Load();
            Assert.Equal(20, fixture.Cards.Count);
            Assert.True(fixture.HasMore);
            Assert.Equal("a:0,a:0", fixture.Transport.Asked());
        });
        Case("R-013 刷新重建服务器消费账，不把旧合成槽当原始记录", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            await fixture.Watch("a-0", "synthetic-1");
            await fixture.Load();
            Assert.False(fixture.Ids().Contains("synthetic-1"));
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.Equal("共 20 项", fixture.TotalLabel);
            Assert.False(fixture.HasMore);
            Assert.Equal("a:0,a:0", fixture.Transport.Asked());
        });
        Case("R-013 刷新取代旧下一集补读，不把旧槽插入新查询", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            var gate = fixture.Transport.HoldNext();
            var marked = fixture.Watch("a-0", "synthetic-1");
            await gate.Entered.Task;
            await fixture.Load();
            gate.Release.TrySetResult();
            await marked;
            Assert.False(fixture.Ids().Contains("synthetic-1"));
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.Equal("共 20 项", fixture.TotalLabel);
        });
        Case("R-013 离页取消后，迟到下一集不改变卡片", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            var gate = fixture.Transport.HoldNext();
            var marked = fixture.Watch("a-0", "synthetic-1");
            await gate.Entered.Task;
            fixture.Invoke("Cancel");
            var before = fixture.Ids();
            gate.Release.TrySetResult();
            await marked;
            Assert.Equal(string.Join(',', before), string.Join(',', fixture.Ids()));
            Assert.False(fixture.Get<bool>("Busy"));
        });
        Case("R-013 来源撤销后不接受补读，不改用默认来源", async () =>
        {
            using var fixture = new Fixture(shell, 21, secondSource: true, aggregate: false);
            await fixture.Load();
            var gate = fixture.Transport.HoldNext();
            var marked = fixture.Watch("a-0", "synthetic-1");
            await gate.Entered.Task;
            fixture.Source("a").IdentityState.Lifetime.Cancel();
            fixture.SelectDefault("b");
            gate.Release.TrySetResult();
            await marked;
            Assert.False(fixture.Ids().Contains("synthetic-1"));
            Assert.Equal("a:0", fixture.Transport.Asked());
            Assert.False(fixture.HasMore && fixture.Get<bool>("Busy"));
        });
        Case("R-013 只换默认账号不撤销原来源，仍分页原账号", async () =>
        {
            using var fixture = new Fixture(shell, 21, secondSource: true, aggregate: false);
            await fixture.Load();
            fixture.SelectDefault("b");
            await fixture.Watch("a-0", "synthetic-1");
            await fixture.Load(reset: false);
            Assert.Equal("a:0,a:19", fixture.Transport.Asked());
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.False(fixture.Ids().Any(id => id.StartsWith("b-", StringComparison.Ordinal)));
        });
        Case("R-013 多来源只回退原条目所属来源，合成槽不占任一游标", async () =>
        {
            using var fixture = new Fixture(shell, 21, secondSource: true, aggregate: true);
            await fixture.Load();
            await fixture.Watch("a-0", "synthetic-1");
            Assert.Equal("共 41 项", fixture.TotalLabel);
            await fixture.Load(reset: false);
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.True(fixture.Ids().Contains("b-20"));
            Assert.Equal("a:0,b:0,a:19,b:20", fixture.Transport.Asked());
            Assert.False(fixture.HasMore);
        });
        Case("R-013 多来源追加取消不消费另一个来源的未发布页", async () =>
        {
            using var fixture = new Fixture(shell, 21, secondSource: true, aggregate: true);
            await fixture.Load();
            var gate = fixture.Transport.HoldResume(captureBefore: true);
            var append = fixture.Load(reset: false);
            await gate.Entered.Task;
            await fixture.Watch("a-0", "synthetic-1");
            gate.Release.TrySetResult();
            await append;
            Assert.False(fixture.Ids().Contains("a-20"));
            Assert.False(fixture.Ids().Contains("b-20"));
            await fixture.Load(reset: false);
            Assert.Equal("a:0,b:0,a:20,b:20,a:19,b:20", fixture.Transport.Asked());
            Assert.True(fixture.Ids().Contains("a-20"));
            Assert.True(fixture.Ids().Contains("b-20"));
        });
        Case("R-013 补齐播放清单也按原始消费量请求，不执行播放命令", async () =>
        {
            using var fixture = new Fixture(shell, 21);
            await fixture.Load();
            await fixture.Watch("a-0", "synthetic-1");
            var task = (Task)fixture.Invoke("PlayableItemsAsync")!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            var items = (List<EmbyItem>)result.GetType().GetField("Item1")!.GetValue(result)!;
            Assert.True(items.Any(item => item.Id == "a-20"));
            Assert.True(items.Any(item => item.Id == "synthetic-1"));
            Assert.Equal("a:0,a:19", fixture.Transport.Asked());
            Assert.Equal(20, fixture.Cards.Count, "只收集清单，不改变已显示页或游标");
            Assert.True(fixture.HasMore);
        });
    }

    private static void Case(string name, Func<Task> body) => TestHarness.Test(name, () => SingleThread.Run(body));

    internal sealed class ShellFixture
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly Type _viewModel;
        private readonly Type _request;
        private readonly Type _actions;
        public string Root { get; }

        public ShellFixture(string shellAssemblyPath, string sourceRoot)
        {
            Root = Path.GetFullPath(sourceRoot);
            var path = Path.GetFullPath(shellAssemblyPath);
            var releaseRoot = Path.Combine(Root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar;
            if (!File.Exists(Path.Combine(Root, "Momoka.sln")) || !File.Exists(path)
                || !path.StartsWith(releaseRoot, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path) != "Momoka.dll")
                throw new InvalidOperationException("必须提供指定工作树当前 x64 Release Shell DLL");
            var directory = Path.GetDirectoryName(path)!;
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            };
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            _viewModel = assembly.GetType("Momoka.Shell.ViewModels.LibraryViewModel", throwOnError: true)!;
            _request = assembly.GetType("Momoka.Shell.Views.LibraryRequest", throwOnError: true)!;
            _actions = assembly.GetType("Momoka.Shell.Views.IShellActions", throwOnError: true)!;
        }

        public object Create(EmbySession session, EmbyImageStore images, ISettingsService settings, EmbySessionScope? source, bool favorites = false)
        {
            var vm = Activator.CreateInstance(_viewModel)!;
            var request = favorites
                ? _request.GetMethod("Favorites")!.Invoke(null, [new NoServices()])!
                : _request.GetMethod("ForRow")!.Invoke(null, [new NoServices(), HomeLayout.HomeRowTarget.Resume, source])!;
            if (favorites) _request.GetProperty("FavoriteAccountKey")!.SetValue(request, source?.Key);
            var actions = DispatchProxy.Create(_actions, typeof(LibraryResumeActionsProxy));
            Invoke(vm, "Attach", request, actions, settings, session, images, new ServerCapabilities(session), null);
            return vm;
        }

        public object? Invoke(object target, string name, params object?[] arguments)
        {
            var method = target.GetType().GetMethod(name, Members)
                ?? throw new AssertionException($"真实 {target.GetType().Name} 缺少入口 {name}");
            try { return method.Invoke(target, arguments); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
        public T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;
        public void Set(object target, string name, object value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly ShellFixture _shell;
        private readonly EmbySession _session;
        private readonly Dictionary<string, EmbySessionScope> _sources = new(StringComparer.Ordinal);
        private readonly object _vm;
        private bool _viewDisposed;
        public ResumeTransport Transport { get; } = new();
        public FakeSettings Settings { get; } = new();
        public object Request => _vm.GetType().GetField("_request", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_vm)!;
        public List<object> Cards => ((System.Collections.IEnumerable)Get<object>("Cards")).Cast<object>().ToList();
        public bool HasMore => Get<bool>("HasMore");
        public string TotalLabel => Get<string>("TotalLabel");

        public Fixture(ShellFixture shell, int count, bool secondSource = false, bool aggregate = false,
            bool favorites = false, MediaServerKind secondKind = MediaServerKind.Emby, bool thirdSource = false)
        {
            _shell = shell;
            var settings = Settings;
            settings.Settings.AggregateMode = secondSource;
            var root = Path.Combine(shell.Root, "artifacts", "staged-repair", "batch02", "resume-fixtures", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(Path.Combine(root, "no-settings|IO-forbidden"));
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = new EmbySession(settings.Settings, new SettingsStore(paths, new NoSecrets()),
                new CredentialVault(new NoSecrets()), device, Transport);
            AddSource("a", count, device);
            if (secondSource) AddSource("b", count, device, secondKind);
            if (thirdSource) AddSource("c", count, device, MediaServerKind.FnNas);
            SelectDefault("a");
            var images = new EmbyImageStore(_session, Path.Combine(root, "images"));
            _vm = shell.Create(_session, images, settings, aggregate ? null : Source("a"), favorites);
            Assert.Equal(0, Transport.Requests.Count, "Attach 不能登录、读取目录或保存配置");
        }

        private void AddSource(string host, int count, DeviceIdentity device, MediaServerKind kind = MediaServerKind.Emby)
        {
            var server = new ServerProfile
            {
                Id = "server-" + host,
                Name = "fixture-" + host,
                Url = "https://" + host + ".invalid",
                ServerKind = kind
            };
            var account = new AccountProfile { Id = "account-" + host, Username = "fixture" };
            var connection = new EmbyConnection(new Uri("https://" + host + ".invalid/" + (kind == MediaServerKind.FnNas ? "v/" : "emby/")),
                "placeholder-only", "fixture-user", "fixture", server.Name, device)
            { ServerKind = kind };
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection), server, account);
            var scope = new EmbySessionScope(_session, identity);
            var registered = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            registered.Add(scope.Key, scope);
            _sources.Add(host, scope);
            Transport.Rows[host] = Enumerable.Range(0, count).Select(index => Item(host + "-" + index)).ToList();
        }

        public EmbySessionScope Source(string host) => _sources[host];
        public void SelectDefault(string host) => typeof(EmbySession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_session, Source(host));
        public T Get<T>(string name) => _shell.Get<T>(_vm, name);
        public int CursorConsumed => ((AggregateQueryCursor)_vm.GetType()
            .GetField("_cursor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_vm)!).ConsumedCount;
        public object? Invoke(string name, params object?[] args) => _shell.Invoke(_vm, name, args);
        public void Set(string name, object value) => _shell.Set(_vm, name, value);
        public T Read<T>(object target, string name) => _shell.Get<T>(target, name);
        public ItemQuery Query() => (ItemQuery)Invoke("BuildQuery", 0)!;
        public Task<bool> Choose(string? key) => (Task<bool>)Invoke("ChooseFavoriteAccountAsync", key)!;
        public Task Load(bool reset = true) => (Task)Invoke("LoadAsync", reset)!;
        public string[] Ids() => Cards.Select(card => _shell.Get<EmbyItem>(card, "Item").Id).ToArray();

        public async Task Remove(string id)
        {
            var card = Cards.Single(card => _shell.Get<EmbyItem>(card, "Item").Id == id);
            var removed = _shell.Get<EmbyItem>(card, "Item");
            var host = removed.Source!.Connection.ApiBase.Host.Split('.')[0];
            Transport.Rows[host].RemoveAll(item => item.Id == id);
            var result = await (Task<bool>)Invoke("RemoveAsync", card, Get<CancellationToken>("QueryToken"), true)!;
            Assert.True(result);
        }

        public Task Watch(string id, string? nextId)
        {
            var card = Cards.Single(card => _shell.Get<EmbyItem>(card, "Item").Id == id);
            var watched = _shell.Get<EmbyItem>(card, "Item");
            var host = watched.Source!.Connection.ApiBase.Host.Split('.')[0];
            Transport.Rows[host].RemoveAll(item => item.Id == id);
            watched.UserData ??= new EmbyUserData();
            watched.UserData.Played = true;
            Transport.Next[(host, watched.SeriesId!)] = nextId is null ? null : Item(nextId, resumable: false);
            return (Task)Invoke("AfterWatchedAsync", card)!;
        }

        public void DisposeView()
        {
            if (_viewDisposed) return;
            _viewDisposed = true;
            ((IDisposable)_vm).Dispose();
        }

        public void Dispose()
        {
            DisposeView();
            _session.Dispose();
        }
    }

    private static EmbyItem Item(string id, bool resumable = true) => new()
    {
        Id = id,
        Name = id,
        Type = EmbyItemType.Episode,
        SeriesId = "series-" + id,
        IndexNumber = 1,
        ParentIndexNumber = 1,
        RunTimeTicks = TimeSpan.FromMinutes(20).Ticks,
        UserData = new EmbyUserData { PlaybackPositionTicks = resumable ? TimeSpan.FromMinutes(5).Ticks : 0 }
    };

    internal sealed class ResumeTransport : HttpMessageHandler
    {
        private Gate? _resume;
        private Gate? _next;
        private readonly Dictionary<string, Gate> _tags = new(StringComparer.Ordinal);
        public Dictionary<string, List<EmbyItem>> Rows { get; } = new(StringComparer.Ordinal);
        public Dictionary<(string Host, string Series), EmbyItem?> Next { get; } = [];
        public ConcurrentQueue<string> Requests { get; } = new();
        public bool FailNextResume { get; set; }
        public bool FailReleasedTag { get; set; }
        public ConcurrentQueue<(string Host, string Body)> FavoriteBodies { get; } = new();
        public ConcurrentQueue<string> TagHosts { get; } = new();
        private readonly ConcurrentQueue<string> _pages = new();
        public string Asked() => string.Join(',', _pages);
        public Gate HoldResume(bool captureBefore) => _resume = new Gate(captureBefore);
        public Gate HoldNext() => _next = new Gate(captureBefore: true);
        public Gate HoldTags(string host) => _tags[host] = new Gate(captureBefore: true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.True(request.Method == HttpMethod.Get || (request.Method == HttpMethod.Post
                && uri.AbsolutePath.EndsWith("/favorite/list", StringComparison.Ordinal)), "夹具只允许媒体/标签读取");
            Assert.True(uri.Host is "a.invalid" or "b.invalid" or "c.invalid", "禁止夹具以外的来源");
            Requests.Enqueue(uri.AbsolutePath);
            var host = uri.Host.Split('.')[0];
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
            string json;
            if (uri.AbsolutePath.EndsWith("/Items/Resume", StringComparison.Ordinal))
            {
                var offset = int.Parse(query["StartIndex"], System.Globalization.CultureInfo.InvariantCulture);
                var limit = int.Parse(query["Limit"], System.Globalization.CultureInfo.InvariantCulture);
                _pages.Enqueue(host + ":" + offset);
                if (FailNextResume)
                {
                    FailNextResume = false;
                    throw new IOException("fixture resume failure");
                }
                string Page() => JsonSerializer.Serialize(new ItemsResult { Items = Rows[host].Skip(offset).Take(limit).ToList(), TotalRecordCount = Rows[host].Count });
                var gate = _resume;
                var before = gate?.CaptureBefore == true ? Page() : null;
                if (gate is not null)
                {
                    gate.Entered.TrySetResult();
                    await gate.Release.Task.ConfigureAwait(false);
                    if (ReferenceEquals(_resume, gate)) _resume = null;
                }
                // Intentionally ignore cancellation to prove production rejects a late success/empty page.
                json = before ?? Page();
            }
            else if (uri.AbsolutePath.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
            {
                var next = Next.GetValueOrDefault((host, query["SeriesId"]));
                var gate = _next;
                if (gate is not null)
                {
                    gate.Entered.TrySetResult();
                    await gate.Release.Task.ConfigureAwait(false);
                    if (ReferenceEquals(_next, gate)) _next = null;
                }
                json = JsonSerializer.Serialize(new ItemsResult { Items = next is null ? [] : [next] });
            }
            else if (uri.AbsolutePath.EndsWith("/favorite/list", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                FavoriteBodies.Enqueue((host, body));
                json = JsonSerializer.Serialize(new
                {
                    code = 0,
                    data = new { list = new[] { new { guid = host + "-favorite", title = host + " favorite", type = "Movie", is_favorite = 1 } }, total = 1 }
                });
            }
            else if (uri.AbsolutePath.EndsWith("/tag/list", StringComparison.Ordinal))
            {
                TagHosts.Enqueue(host);
                if (_tags.TryGetValue(host, out var gate))
                {
                    gate.Entered.TrySetResult();
                    await gate.Release.Task.ConfigureAwait(false);
                    _tags.Remove(host);
                    if (FailReleasedTag) throw new IOException("fixture stale tags");
                }
                json = JsonSerializer.Serialize(new { code = 0, data = new { genres = new[] { host + "-genre" } } });
            }
            else if (uri.AbsolutePath.EndsWith("/tag/genres", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/tag/iso3166", StringComparison.Ordinal))
                json = "{\"code\":0,\"data\":[]}";
            else if (uri.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal))
                json = JsonSerializer.Serialize(new ItemsResult { Items = Rows[host], TotalRecordCount = Rows[host].Count });
            else throw new AssertionException("未授权的假 HTTP 路由：" + uri.AbsolutePath);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    internal sealed class Gate(bool captureBefore)
    {
        public bool CaptureBefore { get; } = captureBefore;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int PageSize => 20;
        public event EventHandler? SaveStateChanged { add { } remove { } }
        public bool HasUnsavedChanges => false;
        public bool TrySave() => throw new AssertionException("禁止保存配置");
        public void Save() => throw new AssertionException("禁止保存配置");
        public ServerProfile ResolveServer(string url) => throw new AssertionException("禁止账号发现");
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new AssertionException("禁止账号发现");
        public void ExportPreferences(string path) => throw new AssertionException("禁止配置 IO");
        public AppSettings ReadBackup(string path) => throw new AssertionException("禁止配置 IO");
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new AssertionException("夹具不调用容器或产品入口");
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("禁止任何凭据 IO");
        public string Unprotect(string cipherText) => throw new AssertionException("禁止任何凭据 IO");
    }

    internal sealed class SingleThread : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _work = new();
        public override void Post(SendOrPostCallback callback, object? state) => _work.Add((callback, state));
        public static void RunOnePost()
        {
            var pump = (SingleThread)Current!;
            Assert.True(pump._work.TryTake(out var work, TimeSpan.FromSeconds(15)),
                "网络完成必须排入当前调度队列");
            work.Callback(work.State);
        }
        public static void WaitForPosts(int count)
        {
            var pump = (SingleThread)Current!;
            Assert.True(SpinWait.SpinUntil(() => pump._work.Count >= count, TimeSpan.FromSeconds(15)),
                "网络完成和游标提交必须排入当前调度队列");
        }
        public static void Run(Func<Task> action)
        {
            var previous = Current;
            var pump = new SingleThread();
            SetSynchronizationContext(pump);
            try
            {
                var task = action();
                while (!task.IsCompleted)
                {
                    if (!pump._work.TryTake(out var work, TimeSpan.FromSeconds(15)))
                        throw new AssertionException("单线程夹具等待超时，不以跳过代替失败");
                    work.Callback(work.State);
                }
                task.GetAwaiter().GetResult();
            }
            finally
            {
                SetSynchronizationContext(previous);
                pump._work.Dispose();
            }
        }
    }
}

/// <summary>The actual VM receives the real action contract, but this fixture cannot navigate or play.</summary>
public class LibraryResumeActionsProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new AssertionException("离线分页不允许外壳动作：" + targetMethod?.Name);
}
