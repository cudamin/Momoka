using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// The no-argument production client talks to two real, anonymous IPv4 loopback listeners on OS-assigned
/// ports. Receipts observe the next hop, not a mocked handler's configuration. Only the explicitly named
/// HTTPS policy cases inject responses: they do not perform a TLS handshake or validate certificates.
/// </summary>
internal static class MoviePilotLoginRedirectTests
{
    private const string Username = "r031-loopback-user";
    private const string Password = "r031 offline+password&=only";
    private const string AnonymousToken = "r031-anonymous-fixture-token";
    private const string LoginPath = "/api/v1/login/access-token";
    private const string LoginReply = """
        {"access_token":"r031-anonymous-fixture-token","user_name":"r031-loopback-user","super_user":true}
        """;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static readonly HttpStatusCode[] Redirects =
    [
        HttpStatusCode.MovedPermanently, HttpStatusCode.Found, HttpStatusCode.SeeOther,
        HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect
    ];

    public static void Register()
    {
        Test("MoviePilot R031 观察器：收尾后重读最终接受数，不能复用早期零采样",
            () => FinalObservationRejectsLateRequestAsync().GetAwaiter().GetResult());
        Test("MoviePilot R031 观察器：取消前已接受的半请求不能记成零",
            () => PartialRequestObservationAsync().GetAwaiter().GetResult());
        foreach (var status in Redirects)
        {
            Test($"MoviePilot R031 双回环：跨源 {(int)status} 不泄露表单、不转换方法、不接受伪成功",
                () => RedirectAsync(status, sameOrigin: false).GetAwaiter().GetResult());
            Test($"MoviePilot R031 双回环：同源相对 {(int)status} 明确拒绝且不重放登录",
                () => RedirectAsync(status, sameOrigin: true).GetAwaiter().GetResult());
        }
        Test("MoviePilot R031 双回环：同源绝对 308 也不跳转",
            () => RedirectAsync(HttpStatusCode.PermanentRedirect, sameOrigin: true, absoluteLocation: true).GetAwaiter().GetResult());
        foreach (var native in new[] { "SocketsHttpHandler", "HttpClientHandler" })
        {
            foreach (var status in new[] { HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect })
                Test($"MoviePilot R031 双回环：显式原生 {native} 的 {(int)status} 同样拒绝",
                    () => RedirectAsync(status, sameOrigin: false, native: native).GetAwaiter().GetResult());
        }
        Test("MoviePilot R031 双回环：307→308→200 连续跳转首跳失败，不返回伪会话",
            () => ChainAsync(endless: false).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：307↔308 环路的跟随上界为零",
            () => ChainAsync(endless: true).GetAwaiter().GetResult());
        foreach (var status in new[] { HttpStatusCode.MultipleChoices, HttpStatusCode.NotModified, (HttpStatusCode)399 })
            Test($"MoviePilot R031 双回环：非标准跳转 {(int)status} 也明确失败",
                () => RedirectAsync(status, sameOrigin: false).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：缺失 Location 的 307 不解析伪令牌",
            () => RedirectAsync(HttpStatusCode.TemporaryRedirect, sameOrigin: false, missingLocation: true).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：跳转错误不回显 Location 或服务端正文",
            () => RedirectAsync(HttpStatusCode.Found, sameOrigin: false, sensitiveReply: true).GetAwaiter().GetResult());
        foreach (var status in new[] { HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect })
            Test($"MoviePilot R031 HTTPS→HTTP 策略：{(int)status} 注入响应拒绝（不建立 TLS）",
                () => DowngradePolicyAsync(status).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：预先取消不发送登录",
            () => CancellationAsync(beforeSend: true).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：已接收表单后取消保留取消语义且不重放",
            () => CancellationAsync(beforeSend: false).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：读取登录正文时取消也保留取消语义",
            () => CancellationAsync(beforeSend: false, afterHeaders: true).GetAwaiter().GetResult());
        foreach (var status in new[] { HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect })
            Test($"MoviePilot R031 双回环：同源 {(int)status} 的正文停住仍立即拒绝",
                () => StalledRedirectAsync(status).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：先读响应头不丢失登录正文超时边界",
            () => BodyTimeoutAsync().GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：最终来源直接 200 保留登录、表单编码与用户名去空格",
            () => DirectAsync(HttpStatusCode.OK, LoginReply).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：401 保留账号密码错误及原始响应",
            () => DirectAsync(HttpStatusCode.Unauthorized, "{\"message\":\"fixture rejected\"}", "用户名或密码不正确").GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：403 保留无权限错误及原始状态",
            () => DirectAsync(HttpStatusCode.Forbidden, "", "这个账号没有权限").GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：403 仍优先服务端错误消息",
            () => DirectAsync(HttpStatusCode.Forbidden, "{\"message\":\"fixture permission denied\"}", "fixture permission denied").GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：200 缺令牌仍失败且不重试",
            () => DirectAsync(HttpStatusCode.OK, "{}", "没有返回访问令牌").GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：200 非法 JSON 仍失败且不重试",
            () => DirectAsync(HttpStatusCode.OK, "not-json", "无法解析").GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：连接探测不继续登录跳转或返回伪连接成功",
            () => ProbeAsync().GetAwaiter().GetResult());
        foreach (var status in new[] { HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect })
            Test($"MoviePilot R031 双回环：有副作用 JSON POST 的 {(int)status} 不自动重放",
                () => OtherRequestAsync("post", status).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：Bearer GET 的 302 不跟随或接受下一站伪成功",
            () => OtherRequestAsync("get", HttpStatusCode.Found).GetAwaiter().GetResult());
        Test("MoviePilot R031 双回环：图片 GET 的 303 同样不跟随",
            () => OtherRequestAsync("image", HttpStatusCode.SeeOther).GetAwaiter().GetResult());
    }

    private static async Task FinalObservationRejectsLateRequestAsync()
    {
        await using var receiver = new LoopbackServer(_ => Reply.Ok(LoginReply));
        using var sender = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = new CancellationTokenSource(Deadline);
        Assert.Equal(0, receiver.Count, "最初没有请求，不能把这一刻的零复用为最终证据");
        await sender.ConnectAsync(IPAddress.Loopback, receiver.Origin.Port, timeout.Token);
        await sender.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            "POST /r031-late HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 128\r\n\r\nbody="), timeout.Token);
        await receiver.Accepted.WaitAsync(timeout.Token);
        await receiver.StopAsync();
        Assert.Throws<AssertionException>(() => receiver.VerifyFinalCount(0));
        receiver.VerifyFinalCount(1);
        Assert.Equal(0, receiver.Requests.Length, "未完成正文仍否决最终零请求");
    }

    /// <summary>先停止并等待两个观察器，再核最终计数；运行中的采样不能作为零下一跳证据。</summary>
    private static async Task CompleteObservationAsync(LoopbackServer source, LoopbackServer next, int expectedSource = 1)
    {
        await source.StopAsync();
        await next.StopAsync();
        source.VerifyFinalCount(expectedSource);
        next.VerifyFinalCount(0);
    }

    private static async Task PartialRequestObservationAsync()
    {
        var receiver = new LoopbackServer(_ => Reply.Ok(LoginReply));
        using var sender = new TcpClient(AddressFamily.InterNetwork);
        var beforeCancel = 0;
        var afterCancel = 0;
        try
        {
            using var timeout = new CancellationTokenSource(Deadline);
            await sender.ConnectAsync(IPAddress.Loopback, receiver.Origin.Port, timeout.Token);
            // A deliberately incomplete anonymous request; no password and no complete body to enqueue.
            await sender.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
                "POST /r031-partial HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 128\r\n\r\nbody="), timeout.Token);
            await receiver.Accepted.WaitAsync(timeout.Token);
            beforeCancel = receiver.Count;
            Assert.Equal(0, receiver.Requests.Length, "半请求不能伪装成已完整解析的请求");
        }
        // Keep the sender open until the receiver's cancellation has completed: an early FIN would
        // test EOF handling instead of cleanup cancellation and can race the server's read.
        finally
        {
            await receiver.DisposeAsync();
            afterCancel = receiver.AcceptedCount;
        }
        Assert.Equal(1, beforeCancel, "TCP 已接受，不等完整正文才计入观察次数");
        Assert.Equal(1, afterCancel, "清理取消不得把半请求从已接受计数中抹掉");
    }

    private static async Task RedirectAsync(HttpStatusCode status, bool sameOrigin,
        bool absoluteLocation = false, string? native = null, bool missingLocation = false, bool sensitiveReply = false)
    {
        await using var next = new LoopbackServer(_ => Reply.Ok(LoginReply));
        LoopbackServer? source = null;
        source = new LoopbackServer(request => request.Target == LoginPath
            ? new Reply(status, sensitiveReply ? "{\"message\":\"r031-do-not-echo-body\"}" : LoginReply,
                missingLocation ? null : sameOrigin
                    ? absoluteLocation ? new Uri(source!.Origin, "r031-final").AbsoluteUri : "/r031-final"
                    : new Uri(next.Origin, sensitiveReply ? "r031-do-not-echo-location" : "r031-final").AbsoluteUri)
            : Reply.Ok(LoginReply));
        await using var sourceLifetime = source;
        using var client = CreateClient(native);
        using var timeout = new CancellationTokenSource(Deadline);
        var attempt = await LoginAsync(client, Base(source), timeout.Token);
        await CompleteObservationAsync(source, next);
        Receipt($"redirect-{(int)status}-{(sameOrigin ? "same" : "cross")}-{native ?? "default"}" +
            $"-absolute={absoluteLocation}-missing={missingLocation}-sensitive={sensitiveReply}", source, next,
            attempt.Session is not null, attempt.Error, native ?? "default-production-constructor");
        Assert.Equal(0, next.CredentialReceipts, "下一站实际收到用户名或密码的次数必须为零");
        Assert.Equal(0, next.Count, "301/302/303 也不能转换成 GET 后继续接受伪成功");
        AssertLoginOnce(source);
        AssertRedirect(attempt, status);
        if (sensitiveReply)
        {
            Assert.DoesNotContain("r031-do-not-echo", attempt.Error!.Message);
            Assert.Null(((MoviePilotException)attempt.Error).Body, "跳转回复不保留可回显敏感内容的正文");
        }
    }

    private static async Task ChainAsync(bool endless)
    {
        LoopbackServer? source = null;
        await using var next = new LoopbackServer(_ => new Reply(HttpStatusCode.PermanentRedirect, LoginReply,
            new Uri(source!.Origin, endless ? "api/v1/login/access-token" : "r031-final").AbsoluteUri));
        source = new LoopbackServer(request => !endless && request.Target == "/r031-final"
            ? Reply.Ok(LoginReply)
            : new Reply(HttpStatusCode.TemporaryRedirect, LoginReply, new Uri(next.Origin, "r031-hop").AbsoluteUri));
        await using var sourceLifetime = source;
        using var client = new MoviePilotClient();
        using var timeout = new CancellationTokenSource(Deadline);
        var attempt = await LoginAsync(client, Base(source), timeout.Token);
        await CompleteObservationAsync(source, next);
        Receipt(endless ? "endless-chain" : "finite-chain", source, next, attempt.Session is not null, attempt.Error);
        Assert.Equal(0, next.CredentialReceipts);
        Assert.Equal(0, next.Count, "跳转链跟随上界为零，不依赖默认的自动跳转次数上限");
        AssertLoginOnce(source);
        AssertRedirect(attempt, HttpStatusCode.TemporaryRedirect);
    }

    private static async Task DowngradePolicyAsync(HttpStatusCode status)
    {
        // Deliberately a response-only scope: no TLS socket, certificate trust, or real downgrade transport.
        var handler = new DowngradeResponse(status);
        using var client = new MoviePilotClient(handler);
        var attempt = await LoginAsync(client, new Uri("https://127.0.0.1:18443/"), CancellationToken.None);
        Console.WriteLine("R031-POLICY " + JsonSerializer.Serialize(new
        {
            scenario = $"https-to-http-{(int)status}", transport = "injected-response-not-TLS",
            handlerCalls = handler.Calls, initialScheme = handler.InitialScheme,
            sessionReturned = attempt.Session is not null, errorType = attempt.Error?.GetType().Name
        }));
        Assert.Equal(1, handler.Calls, "响应策略不得主动发起下一跳");
        Assert.Equal("https", handler.InitialScheme);
        AssertRedirect(attempt, status);
    }

    private static async Task CancellationAsync(bool beforeSend, bool afterHeaders = false)
    {
        await using var next = new LoopbackServer(_ => Reply.Ok(LoginReply));
        await using var source = new LoopbackServer(_ => afterHeaders ? new Reply(HttpStatusCode.OK, LoginReply, HoldBody: true) : null);
        using var client = new MoviePilotClient();
        using var deadline = new CancellationTokenSource(Deadline);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        if (beforeSend) cancel.Cancel();
        var flight = LoginAsync(client, Base(source), cancel.Token);
        if (!beforeSend)
        {
            await (afterHeaders ? source.HeadersSent : source.Received).WaitAsync(deadline.Token);
            cancel.Cancel();
        }
        var attempt = await flight.WaitAsync(deadline.Token);
        await CompleteObservationAsync(source, next, beforeSend ? 0 : 1);
        Receipt(beforeSend ? "cancel-before-send" : afterHeaders ? "cancel-response-body" : "cancel-in-flight", source, next,
            attempt.Session is not null, attempt.Error);
        Assert.Null(attempt.Session);
        Assert.True(attempt.Error is OperationCanceledException, "调用方取消不能变成连接超时或普通登录失败");
        Assert.False(attempt.Error is MoviePilotUnreachableException);
        Assert.Equal(0, next.Count);
        if (beforeSend) Assert.Equal(0, source.Count);
        else AssertLoginOnce(source);
    }

    private static async Task StalledRedirectAsync(HttpStatusCode status)
    {
        await using var next = new LoopbackServer(_ => Reply.Ok(LoginReply));
        await using var source = new LoopbackServer(_ => new Reply(status, LoginReply, "/r031-final", HoldBody: true));
        using var client = new MoviePilotClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var attempt = await LoginAsync(client, Base(source), timeout.Token);
        await CompleteObservationAsync(source, next);
        Receipt($"same-origin-stalled-body-{(int)status}", source, next, attempt.Session is not null, attempt.Error);
        AssertLoginOnce(source);
        Assert.Equal(0, next.Count);
        AssertRedirect(attempt, status);
        Assert.False(timeout.IsCancellationRequested, "不等待跳转正文，收到响应头后拒绝，而不是拖到取消");
    }

    private static async Task BodyTimeoutAsync()
    {
        await using var next = new LoopbackServer(_ => Reply.Ok(LoginReply));
        await using var source = new LoopbackServer(_ => new Reply(HttpStatusCode.OK, LoginReply, HoldBody: true));
        using var client = new MoviePilotClient();
        // Shorten only this real client's deadline to keep the test offline and bounded; no handler is injected.
        var field = typeof(MoviePilotClient).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        var http = (HttpClient)field!.GetValue(client)!;
        Assert.Equal(TimeSpan.FromSeconds(120), http.Timeout, "保留生产传输的总超时配置");
        http.Timeout = TimeSpan.FromMilliseconds(300);
        using var timeout = new CancellationTokenSource(Deadline);
        var attempt = await LoginAsync(client, Base(source), timeout.Token);
        await CompleteObservationAsync(source, next);
        Receipt("login-body-timeout", source, next, attempt.Session is not null, attempt.Error);
        AssertLoginOnce(source);
        Assert.Equal(0, next.Count);
        Assert.Null(attempt.Session);
        Assert.True(attempt.Error is MoviePilotUnreachableException, "先读头也不能把正文超时变成无界读取或用户取消");
        Assert.Contains("超时", attempt.Error!.Message);
        Assert.False(timeout.IsCancellationRequested, "失败来自客户端正文期限，不是外层测试取消");
    }

    private static async Task DirectAsync(HttpStatusCode status, string body, string? expectedError = null)
    {
        await using var next = new LoopbackServer(_ => Reply.Ok(LoginReply));
        await using var source = new LoopbackServer(_ => new Reply(status, body));
        using var client = new MoviePilotClient();
        using var timeout = new CancellationTokenSource(Deadline);
        var apiBase = Base(source);
        var attempt = await LoginAsync(client, apiBase, timeout.Token);
        await CompleteObservationAsync(source, next);
        Receipt($"direct-{(int)status}-{expectedError ?? "success"}", source, next,
            attempt.Session is not null, attempt.Error);
        AssertLoginOnce(source);
        Assert.Equal(0, next.Count);
        if (expectedError is null)
        {
            Assert.Null(attempt.Error);
            Assert.NotNull(attempt.Session);
            Assert.Equal(apiBase, attempt.Session!.ApiBase);
            Assert.True(attempt.Session.AccessToken == AnonymousToken, "只核对匿名令牌，不输出令牌");
            Assert.Equal(Username, attempt.Session.UserName);
            Assert.True(attempt.Session.SuperUser);
        }
        else
        {
            Assert.Null(attempt.Session);
            Assert.True(attempt.Error is MoviePilotException);
            Assert.False(attempt.Error is MoviePilotTokenExpiredException, "登录拒绝不能触发令牌过期重登");
            var error = (MoviePilotException)attempt.Error!;
            Assert.Contains(expectedError, error.Message);
            if (status != HttpStatusCode.OK)
            {
                Assert.Equal<HttpStatusCode?>(status, error.Status);
                Assert.Equal(body, error.Body, "保留原有 401/403 状态和正文错误语义");
            }
        }
    }

    private static async Task ProbeAsync()
    {
        await using var next = new LoopbackServer(_ => Reply.Ok(LoginReply));
        await using var source = new LoopbackServer(request => request.Target == LoginPath
            ? new Reply(HttpStatusCode.TemporaryRedirect, LoginReply, new Uri(next.Origin, "r031-final").AbsoluteUri)
            : Reply.Ok("{\"success\":true,\"data\":{\"version\":\"r031-fixture\"}}"));
        using var client = new MoviePilotClient();
        using var timeout = new CancellationTokenSource(Deadline);
        MoviePilotStatus? result = null;
        var error = await CaptureAsync(async () =>
        {
            result = await new MoviePilotProbe(client).RunAsync(Base(source), Username, Password, timeout.Token);
        });
        await CompleteObservationAsync(source, next);
        Receipt("connection-probe", source, next, result is not null, error);
        Assert.Equal(0, next.CredentialReceipts);
        Assert.Equal(0, next.Count);
        AssertLoginOnce(source);
        Assert.Null(result, "登录被拒后不读 dashboard/配置或返回伪连接成功");
        AssertRedirect(new LoginAttempt(null, error), HttpStatusCode.TemporaryRedirect);
    }

    private static async Task OtherRequestAsync(string kind, HttpStatusCode status)
    {
        const string success = "{\"success\":true,\"data\":{\"id\":1}}";
        await using var next = new LoopbackServer(_ => Reply.Ok(success));
        await using var source = new LoopbackServer(_ => new Reply(status, success, new Uri(next.Origin, "r031-write").AbsoluteUri));
        using var client = new MoviePilotClient();
        using var timeout = new CancellationTokenSource(Deadline);
        var error = await CaptureAsync(async () =>
        {
            if (kind == "post") await client.PostAsync(Base(source), AnonymousToken, "subscribe/", new { name = "r031-fixture" }, timeout.Token);
            else if (kind == "get") await client.GetAsync(Base(source), AnonymousToken, "dashboard/system", timeout.Token);
            else await client.GetBytesAsync(new Uri(source.Origin, "r031-image"), timeout.Token);
        });
        await CompleteObservationAsync(source, next);
        Receipt($"{kind}-{(int)status}", source, next, error is null, error);
        Assert.Equal(0, next.Count, "API 和图片同属此客户端，不为方便放开其它请求的自动跳转");
        Assert.Equal(1, source.Count, "匿名写请求只被初始站接收一次，不能借重定向重放");
        Assert.Equal(kind == "post" ? "POST" : "GET", source.Requests[0].Method);
        Assert.Equal(kind != "image", source.Requests[0].HasAuthorization);
        Assert.True(error is MoviePilotException);
        Assert.Equal<HttpStatusCode?>(status, ((MoviePilotException)error!).Status);
    }

    private static MoviePilotClient CreateClient(string? native) => native switch
    {
        "SocketsHttpHandler" => new MoviePilotClient(new SocketsHttpHandler()),
        "HttpClientHandler" => new MoviePilotClient(new HttpClientHandler()),
        _ => new MoviePilotClient()
    };

    private static Uri Base(LoopbackServer server)
    {
        // Follow the production user-input normalization; do not bypass its scheme/host/explicit-port rules.
        Assert.True(MoviePilotAddress.TryNormalize(server.Origin.AbsoluteUri + "#/dashboard", out var address, out var error), error);
        Assert.NotNull(address);
        return address!;
    }

    private sealed record LoginAttempt(MoviePilotSession? Session, Exception? Error);

    private static async Task<LoginAttempt> LoginAsync(MoviePilotClient client, Uri address, CancellationToken token)
    {
        MoviePilotSession? session = null;
        var error = await CaptureAsync(async () => { session = await client.SignInAsync(address, " " + Username + " ", Password, token); });
        return new LoginAttempt(session, error);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> work)
    {
        try { await work(); return null; }
        catch (Exception error) { return error; }
    }

    private static void AssertLoginOnce(LoopbackServer source)
    {
        Assert.Equal(1, source.Count, "登录只发一次，不自动副作用重放");
        var request = source.Requests[0];
        Assert.Equal("POST", request.Method);
        Assert.Equal(LoginPath, request.Target);
        Assert.Contains("application/x-www-form-urlencoded", request.ContentType);
        Assert.True(request.HasUsername && request.HasPassword, "初始站真实收到完整编码后的占位表单");
        Assert.False(request.HasAuthorization, "登录仍以表单而非已有 Bearer 鉴权");
    }

    private static void AssertRedirect(LoginAttempt attempt, HttpStatusCode status)
    {
        Assert.Null(attempt.Session, "禁止把下一站的伪 200 当作登录成功");
        Assert.True(attempt.Error is MoviePilotException);
        var error = (MoviePilotException)attempt.Error!;
        Assert.Equal<HttpStatusCode?>(status, error.Status, "拒绝保留原始 30x，不伪装为 401 或令牌过期");
        Assert.False(error is MoviePilotTokenExpiredException);
        Assert.Contains("不自动跳转", error.Message);
        Assert.Contains("最终", error.Message, "错误应提示连接配置指向最终来源");
        Assert.Null(error.Body, "跳转正文可能回显凭据，不带入错误对象");
    }

    private static void Receipt(string scenario, LoopbackServer source, LoopbackServer next,
        bool returned, Exception? error, string transport = "default-production-constructor") =>
        Console.WriteLine("R031-LOOPBACK " + JsonSerializer.Serialize(new
        {
            scenario, transport, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            source = source.Snapshot(), next = next.Snapshot(), sessionOrResultReturned = returned,
            errorType = error?.GetType().Name, errorStatus = (int?)(error as MoviePilotException)?.Status
        }));

    private sealed class DowngradeResponse(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string InitialScheme { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            InitialScheme = request.RequestUri!.Scheme;
            var response = new HttpResponseMessage(status) { Content = new StringContent(LoginReply) };
            response.Headers.Location = new Uri("http://127.0.0.1:18080/r031-final");
            return Task.FromResult(response);
        }
    }

    private sealed record Reply(HttpStatusCode Status, string Body, string? Location = null, bool HoldBody = false)
    {
        public static Reply Ok(string body) => new(HttpStatusCode.OK, body);
    }

    // Keep the wire fixture local to these MoviePilot tests. It never opens a non-loopback address,
    // persists a raw body/token, reads user settings, or shares a listener with another suite.
    private sealed record ReceivedRequest(string Method, string Target, string ContentType,
        int BodyBytes, bool HasUsername, bool HasPassword, bool HasAuthorization);

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _headersSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<ReceivedRequest> _requests = new();
        private readonly Func<ReceivedRequest, Reply?> _respond;
        private readonly Task _serve;
        private int _acceptCount;
        private int? _finalCount;
        private bool _pendingAtStop;
        private Task? _finish;

        public LoopbackServer(Func<ReceivedRequest, Reply?> respond)
        {
            _respond = respond;
            _listener.Start(); // Port 0 reserves each port atomically, with no probe/rebind race or fixed-port collision.
            Origin = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _serve = ServeAsync();
        }

        public Uri Origin { get; }
        public int AcceptedCount => Volatile.Read(ref _acceptCount);
        public Task Accepted => _accepted.Task;
        public Task Received => _received.Task;
        public Task HeadersSent => _headersSent.Task;
        public ReceivedRequest[] Requests => _requests.ToArray();
        // Count at TCP accept, not after the body is complete. Pending connections also invalidate zero-hop claims.
        public int Count => _finalCount ?? (Volatile.Read(ref _acceptCount) + (_listener.Pending() ? 1 : 0));
        public int CredentialReceipts => _requests.Count(request => request.HasUsername || request.HasPassword);

        public object Snapshot() => new
        {
            origin = Origin.AbsoluteUri, received = Count, accepted = AcceptedCount, completed = _requests.Count, credentialReceipts = CredentialReceipts,
            usernameReceipts = _requests.Count(request => request.HasUsername),
            passwordReceipts = _requests.Count(request => request.HasPassword), requests = Requests
        };

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Interlocked.Increment(ref _acceptCount);
                    _accepted.TrySetResult();
                    await using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var first = await reader.ReadLineAsync(_stop.Token);
                    if (first is null) continue;
                    var start = first.Split(' ');
                    if (start.Length != 3) throw new InvalidDataException("Invalid loopback HTTP request line");
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                    {
                        var colon = line.IndexOf(':');
                        if (colon <= 0) throw new InvalidDataException("Invalid loopback HTTP header");
                        headers[line[..colon]] = line[(colon + 1)..].Trim();
                    }
                    var length = headers.TryGetValue("Content-Length", out var text)
                        ? int.Parse(text, CultureInfo.InvariantCulture) : 0;
                    if (length is < 0 or > 65536 || headers.ContainsKey("Transfer-Encoding"))
                        throw new InvalidDataException("Unexpected loopback fixture request framing");
                    var buffer = new char[length]; // The tested form and JSON bodies are ASCII on the wire.
                    var read = 0;
                    while (read < length)
                    {
                        var count = await reader.ReadAsync(buffer.AsMemory(read), _stop.Token);
                        if (count == 0) throw new EndOfStreamException("Incomplete loopback request body");
                        read += count;
                    }
                    var body = new string(buffer);
                    var request = new ReceivedRequest(start[0], start[1], headers.GetValueOrDefault("Content-Type", ""),
                        length, HasValue(body, "username", Username), HasValue(body, "password", Password), headers.ContainsKey("Authorization"));
                    _requests.Enqueue(request);
                    _received.TrySetResult();
                    if (_respond(request) is not { } reply)
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                        continue;
                    }
                    var bytes = Encoding.UTF8.GetBytes(reply.Body);
                    var response = $"HTTP/1.1 {(int)reply.Status} Fixture\r\nContent-Type: application/json\r\n" +
                        $"Content-Length: {bytes.Length}\r\nConnection: close\r\n" +
                        (reply.Location is null ? "" : $"Location: {reply.Location}\r\n") + "\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                    _headersSent.TrySetResult();
                    if (reply.HoldBody) await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                    await stream.WriteAsync(bytes, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        }

        private static bool HasValue(string body, string name, string expected) => body.Split('&').Any(field =>
        {
            var equals = field.IndexOf('=');
            return equals >= 0 && field[..equals] == name &&
                Uri.UnescapeDataString(field[(equals + 1)..].Replace('+', ' ')) == expected;
        });

        public Task StopAsync() => _finish ??= StopCoreAsync();

        public void VerifyFinalCount(int expected)
        {
            Assert.True(_finish?.IsCompletedSuccessfully == true, "最终观察必须在停止并等待接收线程后断言");
            Assert.False(_pendingAtStop, "关闭前未接收的排队连接也不能丢掉");
            Assert.Equal(expected, _finalCount!.Value, "最终接受数包含收尾期间已接收的半请求");
        }

        private async Task StopCoreAsync()
        {
            _pendingAtStop = _listener.Pending();
            await _stop.CancelAsync();
            _pendingAtStop |= _listener.Pending();
            _listener.Stop();
            try { await _serve.WaitAsync(Deadline); }
            finally
            {
                // _serve has exited: no count increment can race this final sample.
                _finalCount = AcceptedCount;
                _stop.Dispose();
            }
        }

        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
