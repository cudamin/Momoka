using Momoka.Infrastructure;
using Momoka.Shell;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterInspectOwnership()
    {
        foreach (var writerThrows in new[] { false, true })
        {
            Test("automatic-case-late-failure-" + (writerThrows ? "preserves-writer-error" : "keeps-timeout-and-details"), async () =>
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var wrote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var cleanupFailure = new InvalidOperationException("late actual capture cleanup failed");
                var writerFailure = new IOException("first timeout report write failed");
                var attempts = 0;
                var cleanupFinished = false;
                var paths = Paths("automatic-case-late-failure-" + writerThrows);
                paths.EnsureCreated();
                var report = new ShellProbeReport(paths.LogDirectory, null, ShellProbeSections.All,
                    (path, text, encoding) =>
                    {
                        attempts++;
                        wrote.TrySetResult();
                        if (writerThrows) throw writerFailure;
                        File.WriteAllText(path, text, encoding);
                    });
                var running = report.CaseAsync("late cleanup case", async () =>
                {
                    try { await release.Task; }
                    finally { cleanupFinished = true; }
                    throw cleanupFailure;
                }, 1, ShellProbeSections.Motion);
                Exception? observed = null;
                try
                {
                    await wrote.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Ensure(!running.IsCompleted && !cleanupFinished, "timeout/writer failure keeps the real owner");
                    Ensure(report.Checks is [{ Passed: false }], "one initial failed case retained");
                }
                finally
                {
                    release.TrySetResult();
                    try { await running; }
                    catch (Exception error) { observed = error; }
                }
                Ensure(cleanupFinished, "late cleanup finished before Case returned");
                Ensure(report.Checks is [{ Passed: false }], "late failure augments one failed case, never duplicates it");
                var detail = report.Checks[0].Detail;
                Ensure(detail.Contains(nameof(TimeoutException), StringComparison.Ordinal), "original timeout survives");
                Ensure(detail.Contains(cleanupFailure.ToString(), StringComparison.Ordinal), "late exception with stack survives");
                Equal(writerThrows ? 1 : 2, attempts, "only persist newly added late details after an initially successful write");
                Ensure(writerThrows ? ReferenceEquals(writerFailure, observed) : observed is null,
                    "initial writer exception propagates unchanged after recording the late failure");
                if (!writerThrows)
                    Ensure(File.ReadAllText(Path.Combine(paths.LogDirectory, "shell-probe.json"))
                        .Contains("late actual capture cleanup failed", StringComparison.Ordinal), "late detail persisted in actual report");
            });
        }
        Test("automatic-case-synchronous-failure-recorded-once", async () =>
        {
            var failure = new InvalidOperationException("synchronous run delegate failed");
            var writes = 0;
            var report = new ShellProbeReport(Paths("automatic-case-sync").LogDirectory, null, ShellProbeSections.All,
                (_, _, _) => writes++);
            await report.CaseAsync("synchronous case", () => throw failure, 1, ShellProbeSections.Motion);
            Ensure(report.Checks is [{ Passed: false }], "synchronous failure recorded");
            Equal(failure.ToString(), report.Checks[0].Detail, "original synchronous exception unchanged");
            Equal(1, writes, "no running task or duplicate write invented");
        });
        Test("automatic-case-asynchronous-failure-not-duplicated", async () =>
        {
            var failure = new IOException("already completed task failure");
            var writes = 0;
            var report = new ShellProbeReport(Paths("automatic-case-async").LogDirectory, null, ShellProbeSections.All,
                (_, _, _) => writes++);
            await report.CaseAsync("completed failure case", () => Task.FromException(failure), 1, ShellProbeSections.Motion);
            Ensure(report.Checks is [{ Passed: false }], "task failure recorded once");
            Ensure(report.Checks[0].Detail.Contains("already completed task failure", StringComparison.Ordinal)
                && !report.Checks[0].Detail.Contains("迟到任务收尾异常", StringComparison.Ordinal),
                "draining the same exception does not duplicate details or replace the first recorded stack");
            Equal(1, writes, "same failure does not trigger another write");
        });
        Test("automatic-case-late-failure-preserves-nested-check", async () =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var report = new ShellProbeReport(Paths("automatic-case-nested-late").LogDirectory, null, ShellProbeSections.All,
                (_, _, _) => { });
            var running = report.CaseAsync("outer timeout", async () =>
            {
                await report.CaseAsync("nested success", () => Task.CompletedTask, 1, ShellProbeSections.Motion);
                await release.Task;
                throw new IOException("outer late failure");
            }, 0, ShellProbeSections.Motion);
            Ensure(!running.IsCompleted, "outer timeout retains its real owner");
            release.SetResult();
            await running;
            Ensure(report.Checks is [{ Name: "nested success", Passed: true, Detail: "" }, { Name: "outer timeout", Passed: false }],
                "late details belong to the outer recorded check without corrupting a nested check");
            Ensure(report.Checks[1].Detail.Contains("outer late failure", StringComparison.Ordinal), "outer failure retained");
        });
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
