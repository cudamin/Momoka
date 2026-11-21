using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Shell;
using Momoka.Tests;

namespace DanmakuReview;

internal static class SendProbeTests
{
    internal static async Task<int> RunAsync()
    {
        var cases = new List<(string Name, Func<Task> Run)>();
        cases.Add(("弹幕发送探针换片调用匹配实际可选参数并保留新草稿", async () =>
        {
            await using var fixture = new SendProbeFixture();
            await fixture.RunAsync("").WaitAsync(TimeSpan.FromSeconds(1));
            await fixture.VerifySendingAsync(expectSuccess: true);
            Assert.Equal("新影片草稿", fixture.Vm.DanmakuDraft);
        }
        ));
        foreach (var failure in new[] { "wrongargs", "assertion" })
            cases.Add(($"弹幕发送探针 {failure} 保留原始失败并排空待发送请求", async () =>
            {
                await using var fixture = new SendProbeFixture();
                var report = new ShellProbeReport(fixture.Root, null, ["playback"], (_, _, _) => { });
                await report.CaseAsync(failure, () => fixture.RunAsync(failure), 1, "playback");
                await fixture.VerifySendingAsync(expectSuccess: false);
                var check = report.Checks.Single();
                Assert.False(check.Passed);
                Assert.Contains(failure == "wrongargs" ? "TargetParameterCountException" : "anonymous assertion", check.Detail);
                Assert.DoesNotContain("TimeoutException", check.Detail);
                Assert.True(fixture.Sending!.IsCompleted, "失败回报之前必须排空忽略取消的假 POST");
            }
            ));
        foreach (var shutdownFirst in new[] { false, true })
            cases.Add(($"弹幕发送收尾确定时序：先{(shutdownFirst ? "关闭客户端" : "取消响应")}保留既有终态", async () =>
            {
                using var transport = new OrderedCancellationTransport();
                await using var fixture = new SendProbeFixture(transport, () => transport.Pending.TrySetCanceled());
                var sending = fixture.StartSending();
                await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
                Task? shutdown = null;
                if (shutdownFirst)
                {
                    shutdown = fixture.Client.ShutdownAsync();
                    await transport.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
                    Assert.False(shutdown.IsCompleted, "关闭仍须等待忽略取消的假 POST");
                }
                transport.Pending.SetCanceled();
                var outcome = await fixture.VerifySendingAsync(expectSuccess: false);
                Assert.Equal(shutdownFirst ? SendProbeFixture.CanceledOutcome : SendProbeFixture.UnconfirmedOutcome, outcome);
                Assert.True(sending.IsCompleted);
                if (shutdown is not null) await shutdown;
            }
            ));
        cases.Add(("弹幕发送收尾：已验证终态不会覆盖随后断言错误", async () =>
        {
            var expected = new InvalidOperationException("original assertion after sending");
            Exception? actual = null;
            try
            {
                await using var fixture = new SendProbeFixture();
                var report = new ShellProbeReport(fixture.Root, null, ["playback"], (_, _, _) => { });
                await report.CaseAsync("assertion", () => fixture.RunAsync("assertion"), 1, "playback");
                await fixture.VerifySendingAsync(expectSuccess: false);
                throw expected;
            }
            catch (Exception error) { actual = error; }
            Assert.True(ReferenceEquals(expected, actual), "收尾覆盖了原始断言");
        }
        ));
        cases.Add(("弹幕发送终态：未知异常与成功用例的取消均不能当作通过", async () =>
        {
            var unexpected = new DandanPlayException("unexpected send failure");
            Exception? actual = null;
            try { await SendProbeFixture.VerifyOutcomeAsync(Task.FromException<long>(unexpected), expectSuccess: false); }
            catch (Exception error) { actual = error; }
            Assert.True(ReferenceEquals(unexpected, actual), "未知弹幕异常被接纳");
            actual = null;
            try { await SendProbeFixture.VerifyOutcomeAsync(Task.FromCanceled<long>(new CancellationToken(true)), expectSuccess: true); }
            catch (Exception error) { actual = error; }
            Assert.True(actual is OperationCanceledException, "成功用例的取消被接纳");
        }
        ));
        var failures = 0;
        foreach (var (name, run) in cases)
            try { await run(); Console.WriteLine("  通过  " + name); }
            catch (Exception error) { failures++; Console.WriteLine($"  失败  {name}: {error}"); }
        Console.WriteLine($"弹幕发送探针共 {cases.Count} 项，通过 {cases.Count - failures}，失败 {failures}，跳过 0");
        return failures;
    }

    private sealed class OrderedCancellationTransport : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<HttpResponseMessage> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Entered.TrySetResult();
            return await Pending.Task;
        }
    }
}

internal sealed partial class SendProbeFixture : IAsyncDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Momoka-danmaku-send-probe-" + Guid.NewGuid().ToString("N"));
    private readonly DanmakuSendTransport _transport = new();
    private readonly Action? _releasePending;
    internal DandanPlayClient Client { get; }
    internal Host Vm { get; }
    internal Task<long>? Sending { get; private set; }
    private Task<long>? _observedSending;
    internal const string CanceledOutcome = "canceled";
    internal const string UnconfirmedOutcome = "unconfirmed";
    private const string UnconfirmedSend = "发送结果未确认，可能已经送达；请勿立即重复发送。";

    internal SendProbeFixture(HttpMessageHandler? transport = null, Action? releasePending = null)
    {
        _releasePending = releasePending;
        var settings = new AppSettings();
        settings.Danmaku.Enabled = true;
        Client = new(settings, Root, transport ?? _transport, credentials: new ProbeCredentials());
        Vm = new Host(settings) { ProbeClient = Client, DanmakuDraft = "old draft" };
    }

    internal Task<long> StartSending() => Sending = Client.SendCommentAsync(230001, 90, "anonymous draft");

    internal async Task<string> VerifySendingAsync(bool expectSuccess)
    {
        var sending = Sending ?? throw new InvalidOperationException("发送任务尚未建立");
        try { return await VerifyOutcomeAsync(sending, expectSuccess); }
        finally
        {
            // 验证器已把未知异常或意外成功作为测试失败抛出；收尾不能再用同一异常盖住该失败。
            if (sending.IsCompleted) _observedSending = sending;
        }
    }

    internal static async Task<string> VerifyOutcomeAsync(Task<long> sending, bool expectSuccess)
    {
        try
        {
            var cid = await sending.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(expectSuccess, "失败清理不应得到成功回执");
            Assert.Equal(14L, cid);
            return "confirmed";
        }
        catch (OperationCanceledException) when (!expectSuccess) { return CanceledOutcome; }
        catch (DandanPlayException error) when (!expectSuccess && error.Message == UnconfirmedSend)
        {
            return UnconfirmedOutcome;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _transport.Pending?.TrySetCanceled();
        _releasePending?.Invoke();
        await Client.ShutdownAsync();
        if (Sending is { } sending)
        {
            if (ReferenceEquals(sending, _observedSending)) _ = sending.Exception;
            else await sending;
        }
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    // Native playback and WinUI lifetime are boundaries; real HTTP/client shutdown and source
    // reset/cleanup are under test. These substitutes cannot establish actual page cleanup.
    private sealed class ProbePlayback { internal object? _current; }
    private sealed class ProbePage
    {
        internal void Detach() { }
        internal void Shutdown() { }
        internal void ReleaseVideoSurface() { }
    }
    private sealed class ProbeRoot { internal List<ProbePage> Children { get; } = []; }
    private static void Field(ProbePlayback playback, string name, object? value)
    { _ = name; playback._current = value; }
    private sealed class ProbeCredentials : IDandanPlayCredentialSource
    {
        public Task<DandanPlayCredential> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DandanPlayCredential("anonymous-probe", "anonymous-secret", "fixture"));
        public void Invalidate() { }
    }
}

internal sealed partial class Host
{
    internal DandanPlayClient? ProbeClient { get; init; }
    internal Task ShutdownAsync() => ProbeClient?.ShutdownAsync() ?? Task.CompletedTask;
}
