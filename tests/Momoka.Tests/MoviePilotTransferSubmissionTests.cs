using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotTransferSubmissionTests
{
    public static void Register()
    {
        foreach (var failure in new[] { "history-404", "history-json", "history-schema", "history-timeout", "history-500", "dependency-404" })
        {
            Test("整理提交前：" + failure + " 明确未写且预览不可复用", () =>
            {
                using var fixture = new TransferSubmissionFixture();
                var preview = fixture.Preview();
                fixture.Transport.Failure = failure;
                var error = Assert.Catch<MoviePilotTransferBlockedException>(() => fixture.Submit(preview));
                Assert.NotNull(error.InnerException, "保留只读失败原因，不丢失诊断出处");
                if (error.InnerException is MoviePilotException cause) Assert.Equal(cause.Status, error.Status);
                Assert.Equal(0, fixture.Transport.Writes);
                Assert.False(preview.CanSubmit, "消费过的旧预览不能解除后重用");
                var reads = fixture.Transport.Reads;
                Assert.Throws<MoviePilotTransferBlockedException>(() => fixture.Submit(preview));
                Assert.Equal(reads, fixture.Transport.Reads, "旧预览必须在再次核验前拒绝");
                fixture.Transport.Failure = "";
                var fresh = fixture.Preview();
                Assert.True(fresh.CanSubmit);
                fixture.Submit(fresh);
                Assert.Equal(1, fixture.Transport.Writes, "未写失败不留下不可解除的操作账本");
                Assert.Equal(0, fixture.Transport.LoginRequests);
            });
        }
        foreach (var failure in new[] { "history-changed", "dependency-present" })
        {
            Test("整理提交前：原记录或软链接变化仍拦截 " + failure, () =>
            {
                using var fixture = new TransferSubmissionFixture();
                var preview = fixture.Preview();
                fixture.Transport.Failure = failure;
                Assert.Throws<MoviePilotTransferBlockedException>(() => fixture.Submit(preview));
                Assert.Equal(0, fixture.Transport.Writes);
                Assert.False(preview.CanSubmit);
            });
        }
        foreach (var failure in new[] { "write-lost", "write-canceled", "write-json" })
        {
            Test("整理提交后：" + failure + " 保留不明状态和跨预览防重", () =>
            {
                using var fixture = new TransferSubmissionFixture();
                var preview = fixture.Preview();
                fixture.Transport.Failure = failure;
                Assert.Throws<MoviePilotOperationUncertainException>(() => fixture.Submit(preview));
                Assert.Equal(1, fixture.Transport.Writes);
                Assert.False(preview.CanSubmit);
                fixture.Transport.Failure = "";
                var next = fixture.Preview();
                Assert.Throws<MoviePilotOperationBlockedException>(() => fixture.Submit(next));
                Assert.Equal(1, fixture.Transport.Writes, "重新预览不能解除已经发出请求的不明账本");
            });
        }
        foreach (var failure in new[] { "write-rejected", "write-500" })
        {
            Test("整理提交后：明确拒绝与服务错误不误标提交前 " + failure, () =>
            {
                using var fixture = new TransferSubmissionFixture();
                var preview = fixture.Preview();
                fixture.Transport.Failure = failure;
                var error = Assert.Catch<MoviePilotException>(() => fixture.Submit(preview));
                Assert.False(error is MoviePilotTransferBlockedException);
                Assert.Equal(1, fixture.Transport.Writes);
                if (failure == "write-rejected") Assert.Equal(HttpStatusCode.NotFound, error.Status);
                else Assert.True(error is MoviePilotOperationUncertainException);
            });
        }
        Test("整理提交前：调用方先取消仍保留取消类型且零写", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var preview = fixture.Preview();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => fixture.Submit(preview, cancel.Token));
            Assert.Equal(0, fixture.Transport.Writes);
        });
        Test("整理提交前：只读期间取消与写后取消不混同", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var preview = fixture.Preview();
            using var cancel = new CancellationTokenSource();
            fixture.Transport.OnHistoryRead = cancel.Cancel;
            Assert.Throws<OperationCanceledException>(() => fixture.Submit(preview, cancel.Token));
            Assert.Equal(0, fixture.Transport.Writes);
            Assert.False(preview.CanSubmit);
        });
        Test("整理提交前：配置期间改变取消旧核验且不发请求", () =>
        {
            using var fixture = new TransferSubmissionFixture();
            var preview = fixture.Preview();
            fixture.Transport.OnHistoryRead = () => fixture.Settings.MoviePilot.Enabled = false;
            Assert.Throws<OperationCanceledException>(() => fixture.Submit(preview));
            Assert.Equal(0, fixture.Transport.Writes);
        });
        foreach (var background in new[] { false, true })
        {
            Test("整理正常提交：实际写一次并保留后台参数 " + background, () =>
            {
                using var fixture = new TransferSubmissionFixture();
                var preview = fixture.Preview();
                var result = fixture.Submit(preview, background: background);
                Assert.True(result.Success);
                Assert.Equal(1, fixture.Transport.Writes);
                Assert.Equal(background, fixture.Transport.LastBackground);
                Assert.False(preview.CanSubmit);
                Assert.Equal(0, fixture.Transport.LoginRequests);
            });
        }
    }
}

/// <summary>真实服务，匿名内存会话；绝不调用登录或密文保护器。</summary>
internal sealed class TransferSubmissionFixture : IDisposable
{
    public AppSettings Settings { get; } = new();
    public TransferSubmissionTransport Transport { get; } = new();
    private readonly MoviePilotClient _client;
    public MoviePilotService Service { get; }

    public TransferSubmissionFixture()
    {
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "http://192.0.2.10:3001";
        Settings.MoviePilot.Username = "anonymous-fixture";
        Settings.MoviePilot.ProtectedPassword = "not-a-secret-fixture";
        _client = new MoviePilotClient(Transport);
        Service = new MoviePilotService(_client, new MoviePilotCredentials(new RejectSecretAccess()), Settings);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(Service, null)!;
        var sessionType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var session = Activator.CreateInstance(sessionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
        typeof(MoviePilotService).GetField("_session", flags)!.SetValue(Service, session);
        typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(Service, identity);
    }

    public MoviePilotTransferHistory History() => Complete(Service.FindTransferHistoriesAsync("Original", CancellationToken.None)).Single();

    public MoviePilotTransferPreview Preview()
    {
        var history = History();
        var request = new MoviePilotTransferRequest
        {
            Files = [new("source", history.TargetPath)],
            Histories = [history],
            TargetStorage = "local",
            TargetPath = "/library",
            TransferType = "softlink",
            MediaSource = "themoviedb",
            MediaId = "1",
            Type = MoviePilotTransferRequest.TypeMovie,
            Reorganize = true
        };
        var preview = Complete(Service.TransferPreviewAsync(request, [history.TransferFile], CancellationToken.None));
        Assert.True(preview.CanSubmit, "夹具必须产生路径匹配且可提交的真实预览");
        return preview;
    }

    public MoviePilotTransferResult Submit(MoviePilotTransferPreview preview, CancellationToken cancellationToken = default,
        bool background = false) => Complete(Service.TransferSubmitAsync(preview, background, cancellationToken));

    // The in-memory handler completes immediately. Never block a continuation or use a timeout to wash green.
    public static T Complete<T>(Task<T> task)
    {
        Complete((Task)task);
        return task.Result;
    }

    public static void Complete(Task task)
    {
        if (!task.IsCompleted) throw new AssertionException("匿名内存夹具的任务未同步完成，不能拿阻塞等待掩盖调度变化");
        if (task.IsCanceled) throw new TaskCanceledException(task);
        if (task.Exception is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw();
    }

    public void Dispose()
    {
        Service.Dispose();
        _client.Dispose();
        Transport.Dispose();
    }

    private sealed class RejectSecretAccess : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("夹具不得保护真实或占位凭据");
        public string Unprotect(string cipherText) => throw new AssertionException("夹具不得解开凭据或进入登录");
    }
}

internal sealed class TransferSubmissionTransport : HttpMessageHandler
{
    public string Failure { get; set; } = "";
    public Action? OnHistoryRead { get; set; }
    public int Reads { get; private set; }
    public int Previews { get; private set; }
    public int Writes { get; private set; }
    public int LoginRequests { get; private set; }
    public bool LastBackground { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri ?? throw new AssertionException("夹具需要明确请求地址");
        cancellationToken.ThrowIfCancellationRequested();
        if (url.AbsolutePath.Contains("login", StringComparison.Ordinal))
        {
            LoginRequests++;
            throw new AssertionException("本夹具禁止登录；匿名内存会话必须已注入");
        }
        if (url.AbsolutePath == "/api/v1/history/transfer")
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Reads++;
            OnHistoryRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            var dependency = Uri.UnescapeDataString(url.Query).Contains("title=/library/original.mkv", StringComparison.Ordinal);
            if (Failure == "history-404" || Failure == "dependency-404" && dependency) return Reply("missing", HttpStatusCode.NotFound);
            if (Failure == "history-json") return Reply("{");
            if (Failure == "history-schema") return Reply("{\"success\":true,\"data\":{\"list\":[]}}");
            if (Failure == "history-500") return Reply("server-error", HttpStatusCode.InternalServerError);
            if (Failure == "history-timeout") throw new HttpRequestException("anonymous fixture read interrupted");
            var history = HistoryJson(changed: Failure == "history-changed");
            return Reply("{\"success\":true,\"data\":{\"total\":" + (Failure == "dependency-present" && dependency ? "2" : "1") +
                ",\"list\":[" + history + (Failure == "dependency-present" && dependency ? "," + HistoryJson(dependent: true) : "") + "]}}");
        }
        if (url.AbsolutePath == "/api/v1/storage/list")
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            return Reply("{\"success\":true,\"data\":[{\"storage\":\"local\",\"type\":\"file\",\"path\":\"/downloads/source.mkv\"}]}");
        }
        if (url.AbsolutePath == "/api/v1/transfer/manual")
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var preview = body.RootElement.GetProperty("preview").GetBoolean();
            if (preview) Previews++;
            else
            {
                Writes++;
                LastBackground = url.Query == "?background=true";
                if (Failure == "write-lost") throw new HttpRequestException("anonymous fixture write response lost");
                if (Failure == "write-canceled") throw new OperationCanceledException("anonymous fixture write interrupted");
                if (Failure == "write-json") return Reply("{");
                if (Failure == "write-rejected") return Reply("missing", HttpStatusCode.NotFound);
                if (Failure == "write-500") return Reply("failed", HttpStatusCode.InternalServerError);
            }
            return Reply("{\"success\":true,\"data\":{\"items\":[{\"source\":\"/downloads/source.mkv\",\"target\":\"/library/new.mkv\",\"success\":true,\"state\":\"completed\"}]}}");
        }
        throw new AssertionException("匿名夹具拒绝未登记接口：" + url.AbsolutePath);
    }

    private static string HistoryJson(bool changed = false, bool dependent = false) => JsonSerializer.Serialize(new
    {
        id = dependent ? 2 : 1,
        title = "Original",
        status = true,
        mode = "softlink",
        type = "电影",
        src = dependent ? "/library/original.mkv" : "/downloads/source.mkv",
        dest = dependent ? "/library/dependent.mkv" : "/library/original.mkv",
        media_source = "themoviedb",
        media_id = changed ? "2" : "1",
        seasons = "",
        episodes = "",
        src_fileitem = new { storage = "local", type = "file", path = dependent ? "/library/original.mkv" : "/downloads/source.mkv" },
        dest_fileitem = new { storage = "local", type = "file", path = dependent ? "/library/dependent.mkv" : "/library/original.mkv" }
    });

    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
