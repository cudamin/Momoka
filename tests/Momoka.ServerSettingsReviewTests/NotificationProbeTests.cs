using Microsoft.UI.Xaml.Controls;
using Momoka.Tests;

internal static class NotificationProbeTests
{
    internal static void Run()
    {
        foreach (var mode in new[] { "good", "save", "test" })
        {
            using var queue = new UiQueue();
            ContentDialog.ManualShow = true;
            try
            {
                queue.Run(async () =>
                {
                    var failure = mode != "good";
                    var probe = new Momoka.Shell.ShellNavigationProbe { FailPendingCaptureMode = failure ? mode : null };
                    Exception? error = null;
                    try { await probe.NotificationForFixture(); }
                    catch (Exception caught) { error = caught; }
                    Assert.Equal(failure, error is IOException, "Capture failure must propagate after finally settles Save/Test");
                    if (!failure) Assert.Null(error);
                    Assert.Equal(mode == "save" ? 1 : mode == "test" ? 3 : 4, probe.Captures);
                });
            }
            finally { ContentDialog.ManualShow = false; }
        }
        Console.WriteLine("R15-004 full notification probe: pending Save/Test and capture-failure cleanup passed (no window).");
    }
}
