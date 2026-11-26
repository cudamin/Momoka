using Momoka.Diagnostics;
using Momoka.Shell;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterSelfCheckEvidence()
    {
        var names = new[] { "selfcheck-playback-return.json", "selfcheck-info-geometry.json", "selfcheck-notifications-layout.json" };
        for (var index = 0; index < names.Length; index++)
        {
            var selected = index;
            var name = names[selected];
            Test("Q011-evidence-success-" + selected, async () =>
            {
                var paths = Paths("Q011-evidence-success-" + selected);
                paths.EnsureCreated();
                ShellSelfCheck.BindEvidenceForFixture(new(paths));
                ShellSelfCheck.EvidenceWriterForFixture(selected)("anonymous-success");
                Equal("anonymous-success", File.ReadAllText(Path.Combine(paths.LogDirectory, name)), "production delegate writes its own evidence file");
                var window = new ProbeWindow();
                await ShellSelfCheck.RunScheduledForFixture(window, new(paths));
                Equal(0, ShellSelfCheck.ExitCode, "complete evidence permits actual final callback success");
                Equal(1, window.Closed, "successful evidence still closes exactly once");
            });
            Test("Q011-evidence-directory-" + selected, async () =>
            {
                var paths = Paths("Q011-evidence-directory-" + selected);
                paths.EnsureCreated();
                var file = Path.Combine(paths.LogDirectory, name);
                Directory.CreateDirectory(file);
                var sink = new RingBufferLogSink();
                Log.UseSink(sink);
                ShellSelfCheck.BindEvidenceForFixture(new(paths));
                ShellSelfCheck.EvidenceWriterForFixture(selected)("must-not-be-persisted");
                Ensure(!File.Exists(file), "a directory is not JSON evidence");
                Ensure(sink.Snapshot().Any(entry => entry.Level == LogLevel.Error && entry.Message.Contains(name, StringComparison.Ordinal)), "real IO failure is logged without escaping the Action");
                var window = new ProbeWindow();
                await ShellSelfCheck.RunScheduledForFixture(window, new(paths));
                Equal(1, ShellSelfCheck.ExitCode, "discarded bool cannot turn missing evidence into success");
                Equal(1, window.Closed, "auxiliary IO failure cannot bypass actual scheduled close");
                Ensure(ReadSelf(paths).Contains("[失败] 必需证据", StringComparison.Ordinal), "final report describes missing required evidence");
                Ensure(ReadSelf(paths).Contains("结果：1 项失败", StringComparison.Ordinal), "report-only comparison remains red");
                Ensure(!sink.Snapshot().Any(entry => entry.Message == "自检全部通过"), "no misleading all-passed log after evidence failure");
            });
            Test("Q011-evidence-lock-then-recover-" + selected, async () =>
            {
                var paths = Paths("Q011-evidence-lock-then-recover-" + selected);
                paths.EnsureCreated();
                var file = Path.Combine(paths.LogDirectory, name);
                File.WriteAllText(file, "before-lock");
                ShellSelfCheck.BindEvidenceForFixture(new(paths));
                using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    ShellSelfCheck.EvidenceWriterForFixture(selected)("must-fail-while-locked");
                Equal(1, ShellSelfCheck.ExitCode, "locked auxiliary evidence fails closed immediately");
                ShellSelfCheck.EvidenceWriterForFixture(selected)("recovered");
                Equal("recovered", File.ReadAllText(file), "a later event can persist its real reading");
                var window = new ProbeWindow();
                await ShellSelfCheck.RunScheduledForFixture(window, new(paths));
                Equal(1, ShellSelfCheck.ExitCode, "later successful helper and final writes cannot erase earlier failure");
                Equal(1, window.Closed, "recovered IO still reaches the actual scheduled close");
                Ensure(ReadSelf(paths).Contains("结果：1 项失败", StringComparison.Ordinal), "recovered helper write does not wash the report green");
            });
        }
    }
}
