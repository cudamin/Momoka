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
            Assert.Equal("新影片草稿", fixture.Vm.DanmakuDraft);
        }
        ));
        foreach (var failure in new[] { "wrongargs", "assertion" })
            cases.Add(($"弹幕发送探针 {failure} 保留原始失败并排空待发送请求", async () =>
            {
                await using var fixture = new SendProbeFixture();
                var report = new ShellProbeReport(fixture.Root, null, ["playback"], (_, _, _) => { });
                await report.CaseAsync(failure, () => fixture.RunAsync(failure), 1, "playback");
                var check = report.Checks.Single();
                Assert.False(check.Passed);
                Assert.Contains(failure == "wrongargs" ? "TargetParameterCountException" : "anonymous assertion", check.Detail);
                Assert.DoesNotContain("TimeoutException", check.Detail);
                Assert.True(fixture.Sending!.IsCompleted, "失败回报之前必须排空忽略取消的假 POST");
            }
            ));
        var failures = 0;
        foreach (var (name, run) in cases)
            try { await run(); Console.WriteLine("  通过  " + name); }
            catch (Exception error) { failures++; Console.WriteLine($"  失败  {name}: {error}"); }
        Console.WriteLine($"弹幕发送探针共 {cases.Count} 项，通过 {cases.Count - failures}，失败 {failures}，跳过 0");
        return failures;
    }
}

internal sealed partial class SendProbeFixture : IAsyncDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Momoka-danmaku-send-probe-" + Guid.NewGuid().ToString("N"));
    private readonly DanmakuSendTransport _transport = new();
    internal DandanPlayClient Client { get; }
    internal Host Vm { get; }
    internal Task<long>? Sending { get; private set; }

    internal SendProbeFixture()
    {
        var settings = new AppSettings();
        settings.Danmaku.Enabled = true;
        Client = new(settings, Root, _transport, credentials: new ProbeCredentials());
        Vm = new Host(settings) { ProbeClient = Client, DanmakuDraft = "old draft" };
    }

    public async ValueTask DisposeAsync()
    {
        _transport.Pending?.TrySetCanceled();
        await Client.ShutdownAsync();
        if (Sending is not null)
            try { await Sending; }
            catch (OperationCanceledException) { }
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
