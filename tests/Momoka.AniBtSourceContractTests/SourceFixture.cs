using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;

namespace Momoka.Tests;

internal sealed class SourceFixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly RejectSecrets _secrets = new();
    private readonly MoviePilotClient _mpClient;
    private readonly AniBtClient _aniClient;
    internal AppSettings Settings { get; } = new();
    internal AnonymousHandler Replies { get; } = new();
    internal ControlledTransport Transport { get; }
    internal SettingsStore Store { get; }
    internal MoviePilotService MoviePilot { get; }
    internal AniBtService Service { get; }
    internal EmbySession Session { get; }
    internal EmbySessionScope Scope { get; }
    internal EmbySessionScope OtherScope { get; }
    internal EmbyClient Emby => Scope.Client;
    internal EmbyItem Item { get; }
    internal AniBtScrapePlan? Plan { get; private set; }
    internal IReadOnlyList<AniBtAnime> Choices => Enumerable.Repeat(AnonymousFixture.Anime, Replies.FileCount).ToArray();
    internal bool HasConfirmationContract => typeof(AniBtService).GetMethod("PrepareForConfirmationAsync") is not null;

    internal SourceFixture(int historyCount = 0, bool mixed = false, bool movie = false, bool aggregate = true)
    {
        Replies.HistoryCount = historyCount;
        Replies.FileCount = mixed ? historyCount + 1 : Math.Max(1, historyCount);
        Replies.Movie = movie;
        Transport = new ControlledTransport(Replies);
        Settings.AggregateMode = aggregate;
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "https://mp-fixture.invalid/";
        Settings.MoviePilot.Username = "anonymous-fixture";
        Settings.MoviePilot.ProtectedPassword = "non-secret-fixture-marker";
        _mpClient = new MoviePilotClient(Transport);
        MoviePilot = new MoviePilotService(_mpClient, new MoviePilotCredentials(_secrets), Settings);
        var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", Private)!.Invoke(MoviePilot, null)!;
        var cachedType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var cached = Activator.CreateInstance(cachedType, Private | BindingFlags.Public, null,
            [identity, new MoviePilotSession(new Uri("https://mp-fixture.invalid/api/v1/"), "", "anonymous-fixture", true)], null);
        typeof(MoviePilotService).GetField("_session", Private)!.SetValue(MoviePilot, cached);
        typeof(MoviePilotService).GetField("_availableIdentity", Private)!.SetValue(MoviePilot, identity);
        Assert.True(MoviePilot.Available, "只安装匿名内存身份，不执行登录、测试连接或凭据恢复");
        _aniClient = new AniBtClient(Transport);
        Service = new AniBtService(_aniClient, MoviePilot);
        var device = DeviceIdentity.Create("r042-anonymous", "1");
        var paths = new AppPaths(Path.Combine(Environment.CurrentDirectory, "artifacts", "r042", "never-open-settings"));
        Store = new SettingsStore(paths, _secrets);
        Session = new EmbySession(Settings, Store, new CredentialVault(_secrets), device, Transport);
        Scope = AddAnonymousScope("source-a", "emby-fixture.invalid", device);
        OtherScope = AddAnonymousScope("source-b", "emby-replacement.invalid", device);
        // Both online identities are inert fixtures. Initial installation is not a sign-in or a
        // non-aggregate SelectSource (which correctly prunes the second identity).
        typeof(EmbySession).GetField("_current", Private)!.SetValue(Session, Scope);
        Item = Scope.Bind(new EmbyItem
        {
            Id = movie ? "movie-1" : "episode-1",
            Name = "库内原动画",
            Type = movie ? EmbyItemType.Movie : EmbyItemType.Episode,
            SeriesId = movie ? "" : "series-1",
            IndexNumber = 1,
            ParentIndexNumber = 1,
            Genres = ["动画"]
        });
    }

    private EmbySessionScope AddAnonymousScope(string id, string host, DeviceIdentity device)
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri($"https://{host}/emby/"), "", id, id, "匿名服务器", device)
        { IsAdministrator = true };
        var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection),
            new ServerProfile { Id = id }, new AccountProfile { Id = id });
        var scope = new EmbySessionScope(Session, identity);
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(Session)!).Add(scope.Key, scope);
        return scope;
    }

    internal void SwitchToOther()
    {
        Assert.True(Session.SelectSource(OtherScope), "执行实际 SelectSource，而非直接改 IsCurrent 测试值");
        Assert.True(OtherScope.IsCurrent);
        Assert.False(Scope.IsCurrent);
        Assert.Equal(Settings.AggregateMode ? 1 : 2, Store.SaveCalls, "SelectSource 保存一次；非聚合 PruneOthers 撤销来源再保存一次，均仅内存端点");
        if (Settings.AggregateMode)
        {
            Assert.True(Scope.IsAvailable, "R042 前件：聚合来源切换不撤销旧捕获来源");
            Assert.False(Scope.Lifetime.IsCancellationRequested);
            Assert.Equal(2, Session.Sources.Count);
        }
        else
        {
            Assert.False(Scope.IsAvailable);
            Assert.True(Scope.Lifetime.IsCancellationRequested);
            Assert.Equal(1, Session.Sources.Count);
        }
    }

    internal async Task<AniBtScrapePlan> LoadPlanAsync()
    {
        Plan = await Scope.ExecuteAsync((client, token) => Service.PrepareAsync(client, Item, token), CancellationToken.None);
        return Plan;
    }

    // This bridge selects actual API versions only. It does not emulate planning, guards, Claim,
    // submission or outcomes. The old one-click API is exercised solely as historical red evidence.
    internal async Task<object> PrepareConfirmedAsync(Func<bool>? canContinue = null, CancellationToken cancellation = default)
    {
        var plan = Plan ?? await LoadPlanAsync();
        var prepare = typeof(AniBtService).GetMethod("PrepareForConfirmationAsync")
            ?? throw new InvalidOperationException("此用例需要当前封存确认契约");
        return (await Scope.ExecuteAsync((client, token) => InvokeAsync(prepare,
            [client, plan, Choices, token, canContinue]), cancellation))!;
    }

    internal Task<IReadOnlyList<AniBtScrapeResult>> SubmitConfirmedAsync(object prepared,
        Func<bool>? canContinue = null, CancellationToken cancellation = default, EmbyClient? clientOverride = null) =>
        Scope.ExecuteAsync(async (client, token) =>
        {
            var method = typeof(AniBtService).GetMethods().Single(candidate => candidate.Name == "SubmitAsync"
                && candidate.GetParameters()[1].ParameterType == prepared.GetType());
            return (IReadOnlyList<AniBtScrapeResult>)(await InvokeAsync(method,
                [clientOverride ?? client, prepared, token, canContinue]))!;
        }, cancellation);

    internal async Task<IReadOnlyList<AniBtScrapeResult>> ExecuteConfirmedAsync(Func<bool>? canContinue,
        CancellationToken cancellation = default)
    {
        var plan = Plan ?? await LoadPlanAsync();
        if (HasConfirmationContract)
        {
            var prepared = await PrepareConfirmedAsync(canContinue, cancellation);
            Assert.Equal(0, Replies.Writes, "当前两阶段契约在显式确认前必须零写入");
            return await SubmitConfirmedAsync(prepared, canContinue, cancellation);
        }
        var legacy = typeof(AniBtService).GetMethods().Single(candidate => candidate.Name == "SubmitAsync");
        return (IReadOnlyList<AniBtScrapeResult>)(await Scope.ExecuteAsync((client, token) =>
            InvokeAsync(legacy, [client, plan, Choices, token, canContinue]), cancellation))!;
    }

    private async Task<object?> InvokeAsync(MethodInfo method, object?[] arguments)
    {
        Task task;
        try { task = (Task)method.Invoke(Service, arguments)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    internal void AssertWrites(int apply, int refresh, int transfer = 0, int scan = 0)
    {
        Assert.Equal(apply, Replies.ApplyCount, "实际 Apply HTTP 次数");
        Assert.Equal(refresh, Replies.RefreshCount, "实际单条 Refresh HTTP 次数");
        Assert.Equal(transfer, Replies.TransferWrites, "实际 preview:false HTTP 次数");
        Assert.Equal(scan, Replies.Requests.Count(request => request.Path.Contains("Library/Refresh", StringComparison.Ordinal)),
            "实际全库扫描 HTTP 次数");
        Assert.Equal(apply + refresh + scan + transfer, Replies.Writes);
        Assert.False(Transport.Requests.Any(request => request.Write && request.Host == "emby-replacement.invalid"),
            "旧计划绝不改投来源 B；旧来源可用不等于默认来源未变化");
        AssertSafety();
    }

    internal void AssertSafety()
    {
        Assert.Equal(0, Replies.Unexpected, "禁止白名单外 HTTP、认证、播放、native 与媒体路径 IO");
        Assert.Equal(0, Transport.CredentialViolations);
        Assert.Equal(0, _secrets.Accesses, "凭据保护/解密端点从不允许执行");
        Assert.Equal(0, Store.ForbiddenAccesses);
        Assert.False(Directory.Exists(Store.Paths.Root), "设置端点不能创建任何用户数据目录");
    }

    public void Dispose()
    {
        try { AssertSafety(); }
        finally
        {
            Session.Dispose();
            MoviePilot.Dispose();
            _mpClient.Dispose();
            _aniClient.Dispose();
            Transport.Dispose();
        }
    }

    private sealed class RejectSecrets : ISecretProtector
    {
        internal int Accesses { get; private set; }
        public string Protect(string value) { Accesses++; throw new InvalidOperationException("R042 禁止凭据保护"); }
        public string Unprotect(string value) { Accesses++; throw new InvalidOperationException("R042 禁止凭据解密"); }
    }
}

internal sealed record SourceRequest(string Host, string Method, string Path, bool Write);

// Only anonymous responses and an explicit continuation gate; no socket handler is constructed.
internal sealed class ControlledTransport(AnonymousHandler replies) : HttpMessageHandler
{
    private readonly HttpMessageInvoker _responses = new(replies, disposeHandler: false);
    private readonly object _gate = new();
    private readonly List<SourceRequest> _requests = [];
    private RequestPause? _nextPause;
    internal IReadOnlyList<SourceRequest> Requests { get { lock (_gate) return _requests.ToArray(); } }
    internal int CredentialViolations { get; private set; }

    internal RequestPause PauseNext(Func<SourceRequest, bool> matches)
    {
        lock (_gate)
        {
            Assert.Null(_nextPause, "不能覆盖尚未命中的受控暂停");
            return _nextPause = new RequestPause(matches);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (request.Headers.Contains("Cookie") || request.Headers.Authorization?.Parameter is { Length: > 0 }
            || request.Headers.TryGetValues("X-Emby-Token", out var values) && values.Any(value => value.Length > 0))
        {
            CredentialViolations++;
            throw new InvalidOperationException("R042 HTTP 不允许非空令牌或 Cookie");
        }
        var response = await _responses.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var sent = replies.Requests.Last();
        var entry = new SourceRequest(uri.Host, request.Method.Method, uri.PathAndQuery, sent.Write);
        RequestPause? pause;
        lock (_gate)
        {
            _requests.Add(entry);
            pause = _nextPause;
            if (pause?.Matches(entry) == true) _nextPause = null;
            else pause = null;
        }
        if (pause is not null)
        {
            pause.Started.TrySetResult(entry);
            try { await pause.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                pause.CancellationObserved = true;
                response.Dispose();
                throw;
            }
        }
        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _responses.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class RequestPause(Func<SourceRequest, bool> matches) : IDisposable
{
    internal TaskCompletionSource<SourceRequest> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool CancellationObserved { get; set; }
    internal bool Matches(SourceRequest request) => matches(request);
    internal void Resume() => Release.TrySetResult();
    public void Dispose() => Resume();
}
