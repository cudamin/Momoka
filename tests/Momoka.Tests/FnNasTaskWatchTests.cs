using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FnNasTaskWatchTests
{
    public static void Register()
    {
        Test("飞牛任务：首次只建基线，新增与状态变化不触发，同库全部任务消失才回调", () => Run(VerifyCompletionAsync));
        Test("飞牛任务：传输失败保留基线，不将失败当空表，恢复后真实消失再回调", () => Run(VerifyFailureAsync));
        Test("飞牛任务：请求结束后才等下一周期，同来源不重叠且重复来源只读一次", () => Run(VerifySerialAsync));
        Test("飞牛任务：释放在途读取拒绝晚回调，异步释放等待实际读取退出", () => Run(VerifyReleaseAsync));
        Test("飞牛任务：当前权限降低后拒绝新请求与晚结果，恢复须重新附加", () => Run(VerifyPermissionsAsync));
        Test("飞牛任务：非飞牛与普通账号不启动，身份退出取消待轮询", () => Run(VerifyEligibilityAsync));
        Test("飞牛任务：重复附加先等旧读取完成，旧代不能回调或与新代重叠", () => Run(VerifyReattachAsync));
        Test("飞牛任务：每次通过既有会话取得最新令牌并保留来源钥匙", () => Run(VerifyTokenAsync));
        Test("飞牛任务：首次读取失败后首次成功仍只建立基线", () => Run(VerifyInitialFailureAsync));
        Test("飞牛任务：等待周期中降权后不发下一次读取", () => Run(VerifyWaitingPermissionAsync));
        Test("飞牛任务：Emby来源不会走飞牛任务接口", () => Run(VerifyOtherProductAsync));
        Test("飞牛任务：旧权限响应即使已恢复管理员也不能跨代回调", () => Run(VerifyPermissionRevisionAsync));
        Test("飞牛任务：请求失败后的等待被取消不再发请求", () => Run(VerifyFailureReleaseAsync));
        Test("飞牛任务：type精确匹配，无状态与祖先类别过滤，空和未知祖先保留JS差集边界", VerifyAncestorValues);
    }

    private static void Run(Func<Task> run) => run().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();

    private static void VerifyAncestorValues()
    {
        var before = Watch.Ancestors(Tasks("""
            [{"type":"TaskItemScrap","ancestor":"a","status":0},
             {"type":"TaskItemScrap","ancestor":"a","status":1},
             {"type":"taskitemscrap","ancestor":"lower"},
             {"type":"TaskThumb","ancestor":"thumb"},
             {"type":"TaskItemScrap","ancestor":"schedule-item-scrap"},
             {"type":"TaskItemScrap","ancestor":""},
             {"type":"TaskItemScrap"},
             {"type":"TaskItemScrap","ancestor":null},
             {"type":"TaskItemScrap","ancestor":1},
             {"type":"TaskItemScrap","ancestor":"1"}]
            """));
        Assert.Equal(7, before.Count, "仅type过滤，重复祖先去重，缺失与null、数字与字符串各自分开");
        Assert.False(Watch.HasRemoved(before, Watch.Ancestors(Tasks("""
            [{"type":"TaskItemScrap","ancestor":"a","status":3},
             {"type":"TaskItemScrap","ancestor":"schedule-item-scrap"},
             {"type":"TaskItemScrap","ancestor":""},
             {"type":"TaskItemScrap"},
             {"type":"TaskItemScrap","ancestor":null},
             {"type":"TaskItemScrap","ancestor":1.0},
             {"type":"TaskItemScrap","ancestor":"1"},
             {"type":"TaskItemScrap","ancestor":"new"}]
            """))));
        Assert.True(Watch.HasRemoved(Watch.Ancestors(Tasks("""[{"type":"TaskItemScrap"}]""")),
            Watch.Ancestors(Tasks("""[{"type":"TaskItemScrap","ancestor":null}]"""))), "JS undefined与null不同");
        var objectTask = Tasks("""[{"type":"TaskItemScrap","ancestor":{"id":"a"}}]""");
        Assert.True(Watch.HasRemoved(Watch.Ancestors(objectTask),
            Watch.Ancestors(Tasks("""[{"type":"TaskItemScrap","ancestor":{"id":"a"}}]"""))),
            "未知对象祖先按JS引用相等，两个响应中的对象不是同一个值");
    }

    private static async Task VerifyCompletionAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"a","status":2}]""");
        Assert.Equal(0, fixture.Changes.Count);
        await fixture.TickAsync();
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"a","status":1},{"type":"TaskItemScrap","ancestor":"b"}]""");
        Assert.Equal(0, fixture.Changes.Count);
        await fixture.TickAsync();
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"b"},{"type":"TaskThumb","ancestor":"a"}]""");
        Assert.Equal(fixture.Scope.Key, fixture.Changes.Single());
        await fixture.TickAsync();
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"b","status":0}]""");
        Assert.Equal(1, fixture.Changes.Count);
    }

    private static async Task VerifyFailureAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"a"}]""");
        await fixture.TickAsync();
        var failed = await fixture.Requests.Reader.ReadAsync();
        failed.Response.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await fixture.WaitForDelayAsync();
        Assert.Equal(0, fixture.Changes.Count);
        await fixture.TickAsync();
        await fixture.ReadAsync("[]");
        Assert.Equal(1, fixture.Changes.Count);
    }

    private static async Task VerifySerialAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope, fixture.Scope], fixture.Changed);
        var first = await fixture.Requests.Reader.ReadAsync();
        Assert.False(fixture.Delays.Reader.TryPeek(out _), "读取尚未结束，不能开始周期等待");
        Assert.Equal(1, fixture.TaskRequests);
        first.Response.SetResult(Reply("[]"));
        await fixture.WaitForDelayAsync();
        Assert.Equal(1, fixture.TaskRequests);
        await fixture.TickAsync();
        var second = await fixture.Requests.Reader.ReadAsync();
        Assert.Equal(2, fixture.TaskRequests);
        Assert.Equal(1, fixture.MaxConcurrent);
        second.Response.SetResult(Reply("[]"));
        await fixture.WaitForDelayAsync();
    }

    private static async Task VerifyReleaseAsync()
    {
        using var fixture = new Fixture(ignoreCancellation: true);
        await fixture.RestoreAsync();
        var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"a"}]""");
        await fixture.TickAsync();
        var pending = await fixture.Requests.Reader.ReadAsync();
        watch.Release();
        var disposed = watch.DisposeAsync().AsTask();
        Assert.True(pending.Token.IsCancellationRequested);
        Assert.False(disposed.IsCompleted, "响应替身尚未离开，Dispose必须等待真实loop结束");
        pending.Response.SetResult(Reply("[]"));
        await disposed;
        Assert.Equal(0, fixture.Changes.Count);
        Assert.Equal(2, fixture.TaskRequests);
        Assert.False(fixture.Delays.Reader.TryPeek(out _));
    }

    private static async Task VerifyPermissionsAsync()
    {
        using var fixture = new Fixture(ignoreCancellation: true);
        await fixture.RestoreAsync();
        var source = fixture.Scope;
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"a"}]""");
        await fixture.TickAsync();
        var pending = await fixture.Requests.Reader.ReadAsync();
        fixture.Permission(false);
        Assert.True(source.Connection.IsAdministrator == true, "旧Connection仍是管理员快照");
        Assert.False(source.IsAdministrator == true);
        pending.Response.SetResult(Reply("[]"));
        await watch.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, fixture.Changes.Count);
        Assert.Equal(2, fixture.TaskRequests);
        fixture.Permission(true);
        await using var restored = fixture.CreateWatch();
        restored.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("[]");
        Assert.Equal(0, fixture.Changes.Count, "恢复重新附加必须重新建基线");
    }

    private static async Task VerifyEligibilityAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        fixture.Permission(false);
        watch.Attach([fixture.Scope], fixture.Changed);
        await watch.DisposeAsync();
        Assert.Equal(0, fixture.TaskRequests);
        fixture.Permission(true);
        await using var active = fixture.CreateWatch();
        active.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("[]");
        fixture.Session.SignOut(fixture.Server, fixture.Account);
        await active.DisposeAsync();
        Assert.Equal(1, fixture.TaskRequests);
        Assert.Equal(0, fixture.Changes.Count);
    }

    private static async Task VerifyReattachAsync()
    {
        using var fixture = new Fixture(ignoreCancellation: true);
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        var first = await fixture.Requests.Reader.ReadAsync();
        watch.Attach([fixture.Scope], fixture.Changed);
        Assert.True(first.Token.IsCancellationRequested);
        Assert.Equal(1, fixture.TaskRequests, "新代必须等旧请求真正退出");
        first.Response.SetResult(Reply("[]"));
        await fixture.ReadAsync("[]");
        Assert.Equal(2, fixture.TaskRequests);
        Assert.Equal(1, fixture.MaxConcurrent);
        Assert.Equal(0, fixture.Changes.Count);
    }

    private static async Task VerifyTokenAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"a"}]""");
        fixture.Session.ReplaceClient(fixture.Scope, fixture.Scope.CurrentConnection with { AccessToken = "NONSECRET-new" });
        await fixture.TickAsync();
        var request = await fixture.Requests.Reader.ReadAsync();
        Assert.Equal("NONSECRET-new", request.Authorization);
        request.Response.SetResult(Reply("[]"));
        await fixture.WaitForDelayAsync();
        Assert.Equal(fixture.Scope.Key, fixture.Changes.Single());
    }

    private static async Task VerifyInitialFailureAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        var pending = await fixture.Requests.Reader.ReadAsync();
        pending.Response.SetResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        await fixture.WaitForDelayAsync();
        await fixture.TickAsync();
        await fixture.ReadAsync("[]");
        Assert.Equal(0, fixture.Changes.Count);
    }

    private static async Task VerifyWaitingPermissionAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"a"}]""");
        fixture.Permission(false);
        await fixture.TickAsync();
        await watch.DisposeAsync();
        Assert.Equal(1, fixture.TaskRequests);
        Assert.Equal(0, fixture.Changes.Count);
    }

    private static async Task VerifyOtherProductAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        var identity = new EmbySessionScope.Identity(new EmbyClient(new EmbyHttp(new StubTransport()),
            fixture.Scope.CurrentConnection with { ServerKind = MediaServerKind.Emby }), fixture.Server, fixture.Account);
        var source = new EmbySessionScope(fixture.Session, identity);
        await using var watch = fixture.CreateWatch();
        watch.Attach([source], fixture.Changed);
        await watch.DisposeAsync();
        Assert.Equal(0, fixture.TaskRequests);
    }

    private static async Task VerifyPermissionRevisionAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        await fixture.ReadAsync("""[{"type":"TaskItemScrap","ancestor":"a"}]""");
        await fixture.TickAsync();
        var pending = await fixture.Requests.Reader.ReadAsync();
        fixture.Permission(false);
        fixture.Permission(true);
        pending.Response.SetResult(Reply("[]"));
        await watch.DisposeAsync();
        Assert.Equal(0, fixture.Changes.Count);
        Assert.Equal(2, fixture.TaskRequests);
    }

    private static async Task VerifyFailureReleaseAsync()
    {
        using var fixture = new Fixture();
        await fixture.RestoreAsync();
        await using var watch = fixture.CreateWatch();
        watch.Attach([fixture.Scope], fixture.Changed);
        var pending = await fixture.Requests.Reader.ReadAsync();
        pending.Response.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await fixture.WaitForDelayAsync();
        watch.Release();
        await watch.DisposeAsync();
        Assert.Equal(1, fixture.TaskRequests);
        Assert.Equal(0, fixture.Changes.Count);
    }

    private static IReadOnlyList<JsonObject> Tasks(string json) => JsonNode.Parse(json)!.AsArray().Cast<JsonObject>().ToArray();
    private static HttpResponseMessage Reply(string data) => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"code\":0,\"data\":" + data + "}", Encoding.UTF8, "application/json") };

    // Reflection keeps the red tests executable before this new Core type exists.
    // Every green operation invokes the actual production type, not a copied loop.
    private sealed class Watch : IAsyncDisposable
    {
        private static Type CoreType => typeof(EmbySession).Assembly.GetType("Momoka.FnNas.FnNasTaskWatch")
            ?? throw new AssertionException("Core 尚未实现飞牛管理员任务监控");
        private readonly object _watch;
        public Watch(Func<CancellationToken, Task> delay) => _watch = Activator.CreateInstance(CoreType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [delay], null)!;
        public void Attach(IReadOnlyList<EmbySessionScope> sources, Action<string> changed) =>
            CoreType.GetMethod("Attach")!.Invoke(_watch, [sources, changed]);
        public void Release() => CoreType.GetMethod("Release")!.Invoke(_watch, null);
        public ValueTask DisposeAsync() => (ValueTask)CoreType.GetMethod("DisposeAsync")!.Invoke(_watch, null)!;
        public static IReadOnlyList<object?> Ancestors(IReadOnlyList<JsonObject> tasks) =>
            (IReadOnlyList<object?>)CoreType.GetMethod("Ancestors", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [tasks])!;
        public static bool HasRemoved(IReadOnlyList<object?> before, IReadOnlyList<object?> after) =>
            (bool)CoreType.GetMethod("HasRemoved", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [before, after])!;
    }

    private sealed record PendingRead(string Authorization, CancellationToken Token)
    {
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "fnnas-task-fixtures", Guid.NewGuid().ToString("N"));
        private readonly bool _ignoreCancellation;
        private bool _disposed;
        private int _concurrent;
        public int MaxConcurrent { get; private set; }
        public int TaskRequests { get; private set; }
        public Channel<PendingRead> Requests { get; } = Channel.CreateUnbounded<PendingRead>();
        public Channel<TaskCompletionSource> Delays { get; } = Channel.CreateUnbounded<TaskCompletionSource>();
        public List<string> Changes { get; } = [];
        public ServerProfile Server { get; } = new() { Url = "https://task-watch.invalid/v/", Name = "anonymous", ServerKind = MediaServerKind.FnNas };
        public AccountProfile Account { get; } = new() { Username = "anonymous", UserId = "u" };
        public EmbySession Session { get; }
        public EmbySessionScope Scope => Session.Capture();

        public Fixture(bool ignoreCancellation = false)
        {
            _ignoreCancellation = ignoreCancellation;
            var settings = new AppSettings();
            settings.Servers.Clear();
            settings.Servers.Add(Server);
            Server.Accounts.Add(Account);
            var vault = new CredentialVault(PassthroughSecretProtector.Instance);
            vault.SetAccessToken(Account, "NONSECRET-old");
            Session = new EmbySession(settings, new SettingsStore(new AppPaths(_root), PassthroughSecretProtector.Instance), vault,
                DeviceIdentity.Create("anonymous-task-watch", "1"), this);
        }

        public async Task RestoreAsync() => Assert.True(await Session.TryRestoreAsync(Server, Account, default));
        public void Permission(bool admin) => Session.ReplaceClient(Scope, Scope.CurrentConnection with { IsAdministrator = admin });
        public Watch CreateWatch() => new(async token =>
        {
            var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await Delays.Writer.WriteAsync(delay, token);
            await delay.Task.WaitAsync(token);
        });
        public void Changed(string key) => Changes.Add(key);
        public async Task WaitForDelayAsync() => await Delays.Reader.WaitToReadAsync();
        public async Task TickAsync() => (await Delays.Reader.ReadAsync()).SetResult();
        public async Task ReadAsync(string data)
        {
            var pending = await Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            pending.Response.SetResult(Reply(data));
            await WaitForDelayAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("task-watch.invalid", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath == "/v/api/v1/task/running")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("", request.RequestUri.Query);
                Assert.Null(request.Content);
                var pending = new PendingRead(request.Headers.GetValues("Authorization").Single(), cancellationToken);
                TaskRequests++;
                MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _concurrent));
                try
                {
                    await Requests.Writer.WriteAsync(pending, cancellationToken);
                    return _ignoreCancellation ? await pending.Response.Task : await pending.Response.Task.WaitAsync(cancellationToken);
                }
                finally { Interlocked.Decrement(ref _concurrent); }
            }
            return request.RequestUri.AbsolutePath switch
            {
                "/v/System/Info/Public" or "/v/emby/System/Info/Public" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "/v/api/v1/sys/version" => Reply("{\"version\":\"0.9.8\",\"mediasrvVersion\":\"0.8.45\"}"),
                "/v/api/v1/sys/config" => Reply("{\"server_guid\":\"anonymous-server\",\"server_name\":\"anonymous\"}"),
                "/v/api/v1/user/info" => Reply("{\"guid\":\"u\",\"username\":\"anonymous\",\"is_admin\":1}"),
                "/v/api/v1/mediadb/list" => Reply("[]"),
                "/v/api/v1/mediadb/sum" => Reply("{}"),
                _ => throw new AssertionException("匿名task测试拒绝未知接口：" + request.RequestUri.AbsolutePath)
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                Session.Dispose();
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            base.Dispose(disposing);
        }
    }
}
