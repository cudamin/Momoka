using Momoka.Infrastructure;
using Momoka.Shell;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterInspectOwnership()
    {
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

