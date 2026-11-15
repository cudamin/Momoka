using Momoka.Infrastructure;
using Momoka.Shell;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterInspectOwnership()
    {
        foreach (var writerThrows in new[] { false, true })
        {
            Test("automatic-case-" + (writerThrows ? "writer-failure-drains-owner" : "timeout-drains-owner"), async () =>
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var wrote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var cleanupFinished = false;
                var nextStarted = false;
                var paths = Paths("automatic-case-owner-" + writerThrows);
                paths.EnsureCreated();
                var report = new ShellProbeReport(paths.LogDirectory, null, ShellProbeSections.All,
                    (path, text, encoding) =>
                    {
                        wrote.TrySetResult();
                        if (writerThrows) throw new IOException("automatic report writer failed");
                        File.WriteAllText(path, text, encoding);
                    });
                var sequence = RunSequenceAsync();
                try
                {
                    await wrote.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Ensure(report.Checks is [{ Passed: false }], "timeout is recorded before the owner drains");
                    Ensure(report.Checks[0].Detail.Contains(nameof(TimeoutException), StringComparison.Ordinal),
                        "actual timeout remains the failed evidence");
                    Ensure(!sequence.IsCompleted && !cleanupFinished && !nextStarted,
                        "automatic sequence cannot hand the window to the next case while cleanup owns it");
                }
                finally
                {
                    release.TrySetResult();
                    try { await sequence; }
                    catch (IOException) when (writerThrows) { }
                }
                Ensure(cleanupFinished, "real cleanup finished before CaseAsync returned");
                Equal(!writerThrows, nextStarted, "writer failure propagates after cleanup; ordinary timeout may continue safely");
                Ensure(!report.Checks[0].Passed, "late completion cannot erase the timeout");

                async Task RunSequenceAsync()
                {
                    await report.CaseAsync("delayed capture", async () =>
                    {
                        try { await release.Task; }
                        finally { cleanupFinished = true; }
                    }, 1, ShellProbeSections.Motion);
                    await report.CaseAsync("next case", () =>
                    {
                        Ensure(cleanupFinished, "next case observes the previous owner's real finalization");
                        nextStarted = true;
                        return Task.CompletedTask;
                    }, 1, ShellProbeSections.Motion);
                }
            });
        }
        Test("inspect-check-materials-section", async () =>
        {
            var fixture = new InspectCheckFixture(() => Task.CompletedTask,
                (path, text, encoding) => File.WriteAllText(path, text, encoding), Paths("inspect-materials-section"));
            fixture.Register(ShellProbeSections.Materials, 540);
            await fixture.Action!();
            Equal(ShellProbeSections.Materials, fixture.ObservedSection, "explicit materials section reaches the real Case");
            Equal(540, fixture.ObservedTimeout, "system-state inspection timeout reaches the real Case");
            Ensure(fixture.Checks is [{ Passed: true }], "materials success recorded by the real writer");
        });
        foreach (var writerThrows in new[] { false, true })
        {
            Test("inspect-check-" + (writerThrows ? "writer-fails-after-timeout" : "timeout-retains-owner"), async () =>
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var wrote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var finalized = false;
                var writeAttempts = 0;
                var fixture = new InspectCheckFixture(async () =>
                {
                    entered.SetResult();
                    try { await release.Task; }
                    finally { finalized = true; }
                }, (path, text, encoding) =>
                {
                    writeAttempts++;
                    wrote.SetResult();
                    if (writerThrows) throw new IOException("report writer fixture failure");
                    File.WriteAllText(path, text, encoding);
                }, Paths("inspect-owner-" + writerThrows));
                fixture.Register();
                var running = fixture.Action!();
                try
                {
                    await entered.Task;
                    Equal(Visibility.Collapsed, fixture.Layout.Visibility, "layout hidden while check owns it");
                    await wrote.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    // Let the Case continuation enter AddCheck's actual finally before checking ownership.
                    await Task.WhenAny(running, Task.Delay(100));
                    Ensure(fixture.Checks is [{ Passed: false }], "real CaseAsync recorded the timeout");
                    Ensure(fixture.Checks[0].Detail.Contains(nameof(TimeoutException), StringComparison.Ordinal), "real timeout evidence retained");
                    Ensure(!finalized, "real check is still delayed after Case timeout");
                    Ensure(!running.IsCompleted, "AddCheck must still await actual check after timeout/report failure");
                    Equal(Visibility.Collapsed, fixture.Layout.Visibility, "timeout must not reveal controls for another check");
                }
                finally
                {
                    release.TrySetResult();
                    try { await running; }
                    catch (IOException) when (writerThrows) { }
                }
                Ensure(finalized, "check finally completed before handback");
                Equal(Visibility.Visible, fixture.Layout.Visibility, "controls restored only after real check finalization");
                Equal(1, writeAttempts, "no retry or duplicate report write");
                Equal(writerThrows, running.IsFaulted, "report writer exception remains observable after owner drains");
            });
        }
    }
}
