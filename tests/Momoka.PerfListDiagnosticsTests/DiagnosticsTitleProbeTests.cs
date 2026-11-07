using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DiagnosticsTitleProbeTests
{
    internal static void Register()
    {
        var registration = File.ReadAllText(Path.Combine(Momoka.Shell.ViewModels.LibraryViewModel.FixtureSourceRoot,
            "src/Momoka.Shell/ShellNavigationProbe.cs"));
        Assert.True(registration.Split('\n').Any(line => line.Contains("DiagnosticsInitialTitleAsync,", StringComparison.Ordinal)
            && line.Contains("section: ShellProbeSections.Shell", StringComparison.Ordinal)), "Initial title probe must remain in shell");
        foreach (var mode in new[] { "good", "capture", "binding" })
            Test("R17-014 原样首次标题探针 " + mode, () => SingleThreadPump.Run(async () =>
            {
                var probe = new Momoka.Shell.ShellNavigationProbe { FailCapture = mode == "capture", FailTitleBinding = mode == "binding" };
                Exception? error = null;
                try { await probe.TitleForFixture(); }
                catch (Exception caught) { error = caught; }
                Assert.Equal(mode == "good", error is null);
                if (mode == "capture") Assert.True(error is IOException);
                if (mode == "binding") Assert.True(error is AssertionException);
                Assert.Equal(mode == "binding" ? 0 : mode == "capture" ? 1 : 2, probe.Captures);
                Assert.Equal(0, probe.Mounted, "Capture/binding failure must still remove the page");
            }));
    }
}
