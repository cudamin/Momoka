using System.Text;
using System.Text.Json;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Shell;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];
    private static readonly List<object> Results = [];
    private static string _output = "";
    private static int _assertions;

    internal static async Task<int> Main()
    {
        var root = Path.GetFullPath(Environment.CurrentDirectory);
        Ensure(File.Exists(Path.Combine(root, "Momoka.sln")), "Run from the current worktree root");
        Ensure(typeof(AppPaths).Assembly.Location.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "Core must be built in this worktree; no borrowed DLL");
        _output = Path.Combine(root, "artifacts", "r071-r072", "fixture", "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_output);
        RegisterSelfCheck();
        foreach (var kind in Enum.GetValues<ProbeKind>()) RegisterProbe(kind);
        RegisterShellScope();
        RegisterActualProbeCallers();
        RegisterFinalizerBoundaries();
        var failures = 0;
        foreach (var (name, run) in Cases)
        {
            try
            {
                ShellSelfCheck.ResetForFixture();
                Log.UseSink(NullLogSink.Instance);
                await run().WaitAsync(TimeSpan.FromSeconds(10));
                Results.Add(new { name, passed = true });
                Console.WriteLine($"通过 {name}");
            }
            catch (Exception error)
            {
                failures++;
                Results.Add(new { name, passed = false, error = error.ToString() });
                Console.WriteLine($"失败 {name}: {error}");
            }
            finally
            {
                Log.UseSink(NullLogSink.Instance);
            }
        }
        var summary = new { total = Cases.Count, passed = Cases.Count - failures, failed = failures, skipped = 0, assertions = _assertions, output = _output, results = Results };
        File.WriteAllText(Path.Combine(_output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failures}，失败 {failures}，跳过 0；断言 {_assertions}；输出 {_output}");
        return failures == 0 ? 0 : 1;
    }

    private static void RegisterSelfCheck()
    {
        Test("selfcheck-success", async () =>
        {
            var paths = Paths("selfcheck-success");
            var window = new ProbeWindow
            {
                OnClose = () =>
                {
                    Equal(0, ShellSelfCheck.ExitCode, "successful report before close");
                    Ensure(ReadSelf(paths).EndsWith("结果：全部通过" + Environment.NewLine, StringComparison.Ordinal), "complete final report");
                }
            };
            await ShellSelfCheck.RunScheduledForFixture(window, new(paths));
            Equal(1, window.Closed, "actual scheduled close");
        });
        Test("selfcheck-assertion", async () =>
        {
            var paths = Paths("selfcheck-assertion");
            var window = new ProbeWindow();
            await ShellSelfCheck.RunScheduledForFixture(window, new(paths) { Readings = "[失败] A — fixture\n", Failures = 1 });
            Equal(1, ShellSelfCheck.ExitCode, "assertions still fail after successful IO");
            Ensure(ReadSelf(paths).Contains("结果：1 项失败", StringComparison.Ordinal), "failure report persisted");
            Equal(1, window.Closed, "failure closes");
        });
        Test("selfcheck-write-pending", async () =>
        {
            var paths = Paths("selfcheck-write-pending");
            var writes = 0;
            var window = new ProbeWindow();
            await ShellSelfCheck.FinishForFixture(() =>
            {
                var written = ShellSelfCheck.CompleteForFixture(paths, "[通过] A\n结果：全部通过\n", 0, (path, content, encoding) =>
                {
                    Equal(1, ShellSelfCheck.ExitCode, "writer must not observe early success");
                    writes++;
                    File.WriteAllText(path, content, encoding);
                    Equal(1, ShellSelfCheck.ExitCode, "success only after writer returns");
                });
                Ensure(written, "actual CompleteReport returns write success");
                Equal(0, ShellSelfCheck.ExitCode, "after persistent write");
                return Task.CompletedTask;
            }, window.Close);
            Equal(1, writes, "no duplicate final write");
            Equal(1, window.Closed, "real finalizer closes");
        });
        Test("selfcheck-locked", async () =>
        {
            var paths = Paths("selfcheck-locked");
            SeedPending(paths);
            var sink = new RingBufferLogSink();
            Log.UseSink(sink);
            var window = new ProbeWindow();
            using (var locked = new FileStream(SelfFile(paths), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                await ShellSelfCheck.RunScheduledForFixture(window, new(paths));
            Equal(1, ShellSelfCheck.ExitCode, "real final IO failure cannot pass");
            Equal(1, window.Closed, "locked report still reaches actual close");
            Ensure(ReadSelf(paths).Contains("结果：未完成", StringComparison.Ordinal), "pending evidence retained; no final report invented");
            Ensure(sink.Snapshot().Any(entry => entry.Level == LogLevel.Error && entry.Message.Contains("selfcheck-shell.txt", StringComparison.Ordinal)), "IO error recorded");
            Ensure(!sink.Snapshot().Any(entry => entry.Message == "自检全部通过"), "no misleading success log");
        });
        Test("selfcheck-missing", async () =>
        {
            var directory = Path.Combine(_output, "selfcheck-missing");
            Directory.CreateDirectory(directory);
            var blocker = Path.Combine(directory, "root-is-a-file");
            File.WriteAllText(blocker, "fixture");
            var paths = new AppPaths(blocker);
            var window = new ProbeWindow();
            await ShellSelfCheck.RunScheduledForFixture(window, new(paths));
            Equal(1, ShellSelfCheck.ExitCode, "EnsureCreated failure cannot pass");
            Ensure(!File.Exists(SelfFile(paths)), "no final report");
            Equal(1, window.Closed, "directory creation failure closes");
        });
        Test("selfcheck-report-is-directory", async () =>
        {
            var paths = Paths("selfcheck-report-is-directory");
            paths.EnsureCreated();
            Directory.CreateDirectory(SelfFile(paths));
            var window = new ProbeWindow();
            await ShellSelfCheck.RunScheduledForFixture(window, new(paths));
            Equal(1, ShellSelfCheck.ExitCode, "real access failure cannot pass");
            Ensure(!File.Exists(SelfFile(paths)), "directory is not a report");
            Equal(1, window.Closed, "access failure closes");
        });
        foreach (var error in new Exception[] { new IOException("injected final IO"), new UnauthorizedAccessException("injected denied writer") })
        {
            Test("selfcheck-injected-" + error.GetType().Name, async () =>
            {
                var paths = Paths("selfcheck-injected-" + error.GetType().Name);
                var window = new ProbeWindow();
                var attempts = 0;
                await ShellSelfCheck.FinishForFixture(() =>
                {
                    Ensure(!ShellSelfCheck.CompleteForFixture(paths, "[通过] A\n结果：全部通过\n", 0, (_, _, _) =>
                    {
                        attempts++;
                        Equal(1, ShellSelfCheck.ExitCode, "fail-closed before IO");
                        throw error;
                    }), "writer failure returned to caller");
                    return Task.CompletedTask;
                }, window.Close);
                Equal(1, ShellSelfCheck.ExitCode, "injected writer failure nonzero");
                Equal(1, attempts, "no report retry loop");
                Equal(1, window.Closed, "source finalizer still closes");
            });
        }
        Test("selfcheck-partial-write", async () =>
        {
            var paths = Paths("selfcheck-partial-write");
            var window = new ProbeWindow();
            await ShellSelfCheck.FinishForFixture(() =>
            {
                Ensure(!ShellSelfCheck.CompleteForFixture(paths, "[通过] A\n结果：全部通过\n", 0, (path, _, encoding) =>
                {
                    File.WriteAllText(path, "[通过] A — truncated write\n", encoding);
                    throw new IOException("disk full after partial write");
                }), "partial write is not success");
                return Task.CompletedTask;
            }, window.Close);
            Equal(1, ShellSelfCheck.ExitCode, "partial write nonzero");
            Ensure(!ReadSelf(paths).Contains("结果：", StringComparison.Ordinal), "incomplete report preserved for comparator");
            Equal(1, window.Closed, "partial write closes");
        });
        Test("selfcheck-before-report-exception", async () =>
        {
            var paths = Paths("selfcheck-before-report-exception");
            SeedPending(paths);
            var window = new ProbeWindow();
            await ShellSelfCheck.RunScheduledForFixture(window, new(paths) { BeforeReadings = () => throw new InvalidOperationException("fake UI readings failed") });
            Equal(1, ShellSelfCheck.ExitCode, "actual callback catches report generation error");
            Ensure(ReadSelf(paths).Contains("结果：未完成", StringComparison.Ordinal), "no completed report");
            Equal(1, window.Closed, "before-report failure closes");
        });
        Test("selfcheck-after-report-exception", async () =>
        {
            var paths = Paths("selfcheck-after-report-exception");
            var window = new ProbeWindow();
            await ShellSelfCheck.RunScheduledForFixture(window, new(paths)
            {
                DumpUi = true,
                AfterReport = () => Task.FromException(new IOException("capture boundary failed"))
            });
            Equal(1, ShellSelfCheck.ExitCode, "existing callback error contract retained");
            Ensure(ReadSelf(paths).Contains("结果：全部通过", StringComparison.Ordinal), "report existed before later error");
            Equal(1, window.Closed, "post-report failure closes");
        });
        Test("selfcheck-stale-success-reset", async () =>
        {
            var paths = Paths("selfcheck-stale-success-reset");
            Ensure(ShellSelfCheck.CompleteForFixture(paths, "[通过] A\n结果：全部通过\n", 0), "first run completed");
            Equal(0, ShellSelfCheck.ExitCode, "first run success");
            var window = new ProbeWindow();
            await ShellSelfCheck.FinishForFixture(() =>
            {
                Ensure(!ShellSelfCheck.CompleteForFixture(paths, "[通过] A\n结果：全部通过\n", 0, (_, _, _) =>
                {
                    Equal(1, ShellSelfCheck.ExitCode, "reset previous success before final write");
                    throw new IOException("next run report blocked");
                }), "failed next write");
                return Task.CompletedTask;
            }, window.Close);
            Equal(1, ShellSelfCheck.ExitCode, "prior success not reused");
            Equal(1, window.Closed, "next run closes");
        });
    }

    private static void RegisterProbe(ProbeKind kind)
    {
        foreach (var scenario in new[] { "success", "assertion", "first-persistent", "first-transient", "final-persistent", "partial-final" })
        {
            Test(kind + "-" + scenario, async () =>
            {
                var run = new ProbeRun(kind, Path.Combine(_output, kind + "-" + scenario));
                run.MainWrite = (path, content, encoding, attempt) =>
                {
                    if (scenario == "first-persistent" || scenario == "first-transient" && attempt == 1 || scenario == "final-persistent" && attempt >= 2)
                        throw new IOException($"injected {scenario} #{attempt}");
                    if (scenario == "partial-final" && attempt >= 2)
                    {
                        File.WriteAllText(path, content[..Math.Min(10, content.Length)], encoding);
                        throw new IOException("injected partial final write");
                    }
                    File.WriteAllText(path, content, encoding);
                };
                await run.ExecuteAsync(() => run.RecordAsync(scenario != "assertion"));
                run.Verify(scenario == "success" ? 0 : 1);
                Equal(2, run.MainAttempts, "Record plus one final write, never recursive IO");
                if (scenario != "success") Ensure(run.HasFailure, "in-memory failure evidence remains even if final IO fails");
                if (scenario is "assertion" or "first-transient") Ensure(run.SavedFailure, "recovered final writer retains earlier failure");
                if (kind == ProbeKind.Shell) Equal(1, run.ScopeAttempts, "scope still attempted if main writer persistently fails");
            });
        }
        Test(kind + "-final-same-error-instance", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-final-same-error-instance"));
            var failure = new IOException("persistent same object");
            run.MainWrite = (_, _, _, _) => throw failure;
            await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(1);
            Equal(2, run.MainAttempts, "first and final writer reached");
            Ensure(run.FailureCount >= 2, "first and final failure evidence preserved");
        });
        Test(kind + "-write-pending", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-write-pending"));
            run.Code = 0; // A prior run must not leak success into this new run.
            await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(0);
            Ensure(run.Trace.Where(item => item.StartsWith("write:", StringComparison.Ordinal)).All(item => item.EndsWith(":1", StringComparison.Ordinal)),
                "all intermediate/final writers observe nonzero until IO completes");
            Equal("dispose:1", run.Trace[^3], "cleanup before publishing success");
            Equal("code:0", run.Trace[^2], "success only after final reports and cleanup");
            Equal("exit:0", run.Trace[^1], "Exit sees completed success");
        });
        Test(kind + "-body-error", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-body-error"));
            await run.ExecuteAsync(() => Task.FromException(new InvalidOperationException("probe failed before Record")));
            run.Verify(1);
            Equal(1, run.MainAttempts, "final report attempted even before first Record");
            Ensure(run.SavedFailure, "body error persisted");
        });
        Test(kind + "-body-cancelled", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-body-cancelled"));
            await run.ExecuteAsync(() => Task.FromCanceled(new CancellationToken(true)));
            run.Verify(1);
            Ensure(run.SavedFailure, "cancellation is not success");
        });
        Test(kind + "-locked", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-locked"));
            File.WriteAllText(run.MainPath, "pending fixture report");
            using (var locked = new FileStream(run.MainPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(1);
            Equal(2, run.MainAttempts, "actual File.WriteAllText fails at Record and finalization");
            Ensure(run.HasFailure, "real IO error retained in memory");
            Equal("pending fixture report", File.ReadAllText(run.MainPath), "lock did not fake a completed report");
        });
        Test(kind + "-missing-directory", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-missing-directory"), createDirectory: false);
            await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(1);
            Ensure(run.HasFailure, "missing parent failures retained");
            Ensure(!File.Exists(run.MainPath), "no report invented");
            Equal(2, run.MainAttempts, "initial and final missing-path failure");
        });
        Test(kind + "-report-is-directory", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-report-is-directory"));
            Directory.CreateDirectory(run.MainPath);
            await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(1);
            Ensure(run.HasFailure, "real access errors retained");
            Ensure(!File.Exists(run.MainPath), "directory not accepted as final report");
        });
        Test(kind + "-dispose-error", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-dispose-error")) { DisposeError = new InvalidOperationException("fake window dispose failed") };
            await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(1);
            Ensure(run.HasFailure, "dispose failure cannot escape over Exit");
        });
        Test(kind + "-report-and-dispose-errors", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-report-and-dispose-errors")) { DisposeError = new IOException("fake dispose failure") };
            run.MainWrite = (_, _, _, _) => throw new IOException("persistent report failure");
            await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(1);
            Ensure(run.FailureCount >= 3, "report and cleanup failures not lost in memory");
        });
        Test(kind + "-assertion-control-flow", async () =>
        {
            var run = new ProbeRun(kind, Path.Combine(_output, kind + "-assertion-control-flow"));
            var continued = false;
            await run.ExecuteAsync(async () =>
            {
                await run.RecordAsync(false);
                continued = true;
                await run.RecordAsync(true);
            });
            run.Verify(1);
            Equal(kind == ProbeKind.Shell, continued, "subtitle stops; Shell Case continues but retains failed check");
            Ensure(run.SavedFailure, "normal assertion failure retained on disk");
        });
    }

    private static void RegisterShellScope()
    {
        Test("shell-final-scope-persistent", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-final-scope-persistent"));
            run.ScopeWrite = (_, _, _, _) => throw new IOException("final scope IO unavailable");
            await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(1);
            Equal(1, run.ScopeAttempts, "failed scope still attempted once");
            Equal(2, run.MainAttempts, "scope failure cannot skip final main report");
            Ensure(run.SavedFailure, "final main report contains scope failure");
        });
        Test("shell-first-scope-persistent", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-first-scope-persistent"), sections: [ShellProbeSections.Settings]);
            run.ScopeWrite = (_, _, _, _) => throw new IOException("scope blocked on skipped Case and finally");
            await run.ExecuteAsync(() => run.RecordSectionAsync(ShellProbeSections.Library));
            run.Verify(1);
            Equal(2, run.ScopeAttempts, "initial and final scope failures exercised");
            Equal(1, run.MainAttempts, "main failure evidence written despite both scope failures");
            Ensure(run.SavedFailure, "scope failure saved in main report");
        });
        Test("shell-first-scope-transient", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-first-scope-transient"), sections: [ShellProbeSections.Settings]);
            run.ScopeWrite = (path, content, encoding, attempt) =>
            {
                if (attempt == 1) throw new IOException("first scope failed");
                File.WriteAllText(path, content, encoding);
            };
            await run.ExecuteAsync(() => run.RecordSectionAsync(ShellProbeSections.Library));
            run.Verify(1);
            Equal(2, run.ScopeAttempts, "scope eventually written");
            Ensure(run.SavedFailure, "recovery does not erase first scope failure");
            Ensure(File.ReadAllText(run.ScopePath).Contains("未执行检查：1", StringComparison.Ordinal), "skipped area preserved");
        });
        Test("shell-both-final-reports-fail", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-both-final-reports-fail"));
            run.MainWrite = (path, content, encoding, attempt) =>
            {
                if (attempt >= 2) throw new IOException("main final report blocked");
                File.WriteAllText(path, content, encoding);
            };
            run.ScopeWrite = (_, _, _, _) => throw new IOException("scope final report blocked");
            await run.ExecuteAsync(() => run.RecordAsync(true));
            run.Verify(1);
            Equal(2, run.MainAttempts, "main final attempt not suppressed");
            Equal(1, run.ScopeAttempts, "scope final attempt not suppressed");
            Ensure(run.FailureCount >= 2, "both final failures kept in memory");
        });
        Test("shell-empty-checks", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-empty-checks"));
            await run.ExecuteAsync(() => Task.CompletedTask);
            run.Verify(1);
            Equal(0, run.ShellChecks.Count, "empty ordinary run must not be a vacuous pass");
        });
        Test("shell-unregistered-section", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-unregistered-section"));
            await run.ExecuteAsync(() => run.RecordSectionAsync(null));
            run.Verify(1);
            Ensure(run.ShellChecks.Any(check => check.Name == "探针分区" && !check.Passed), "existing registration guard retained");
        });
        Test("shell-selected-section", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-selected-section"), sections: [ShellProbeSections.Settings]);
            await run.ExecuteAsync(async () =>
            {
                await run.RecordAsync(true);
                await run.RecordSectionAsync(ShellProbeSections.Library);
            });
            run.Verify(0);
            Equal(1, run.ShellChecks.Count, "only selected Case executed");
            Ensure(File.ReadAllText(run.ScopePath).Contains("未执行检查：1", StringComparison.Ordinal), "partial scope explicitly retained");
            Ensure(!File.ReadAllText(run.ScopePath).Contains("未执行区域：无", StringComparison.Ordinal), "not mislabeled as full probe");
        });
        Test("shell-inspect-success", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-inspect-success"));
            await run.ExecuteAsync(() => Task.CompletedTask, inspect: true);
            run.Verify(0);
            Equal(0, run.ShellChecks.Count, "inspect retains no-Case behavior only after reports persist");
        });
        Test("shell-inspect-final-failure", async () =>
        {
            var run = new ProbeRun(ProbeKind.Shell, Path.Combine(_output, "shell-inspect-final-failure"));
            run.MainWrite = (_, _, _, _) => throw new IOException("inspect final report failed");
            await run.ExecuteAsync(() => Task.CompletedTask, inspect: true);
            run.Verify(1);
            Ensure(run.HasFailure, "inspect cannot overwrite final IO failure with success");
        });
    }

    private static void RegisterActualProbeCallers()
    {
        foreach (var kind in Enum.GetValues<ProbeKind>())
        {
            foreach (var scenario in new[] { "success", "assertion", "first-persistent", "final-persistent" })
            {
                Test("actual-caller-" + kind + "-" + scenario, async () =>
                {
                    var paths = Paths("actual-caller-" + kind + "-" + scenario);
                    paths.EnsureCreated();
                    var reportPath = Path.Combine(paths.LogDirectory, kind == ProbeKind.Subtitle ? "subtitle-probe.txt" : "shell-probe.json");
                    File.WriteAllText(reportPath, "pending fixture");
                    var window = new ProbeHostWindow();
                    FileStream? locked = null;
                    if (scenario == "first-persistent") locked = Lock();
                    ProbeApplication.Current.Reset();
                    ProbeClientResource.Reset();
                    var options = new StartupOptions(paths)
                    {
                        Window = window,
                        SubtitleBody = record =>
                        {
                            record("A", scenario != "assertion", "isolated UI boundary");
                            if (scenario == "final-persistent") locked = Lock();
                            return Task.CompletedTask;
                        },
                        ShellBody = async record =>
                        {
                            await record("A", scenario != "assertion", ShellProbeSections.Settings);
                            if (scenario == "final-persistent") locked = Lock();
                        }
                    };
                    try
                    {
                        if (kind == ProbeKind.Subtitle) await SubtitleSettingsProbe.RunAsync(options);
                        else await ShellNavigationProbe.RunAsync(options);
                    }
                    finally
                    {
                        locked?.Dispose();
                    }
                    var code = kind == ProbeKind.Subtitle ? SubtitleSettingsProbe.ExitCode : ShellNavigationProbe.ExitCode;
                    Equal(scenario == "success" ? 0 : 1, code, "real production caller exit property");
                    Equal(1, window.Disposed, "real caller reaches window Dispose");
                    Equal(1, ProbeApplication.Current.Exited, "real caller reaches Application.Exit");
                    if (kind == ProbeKind.Subtitle) Equal(2, ProbeClientResource.Disposed, "report failure does not skip outer using resource disposal");
                    if (scenario is "success" or "assertion")
                    {
                        var content = File.ReadAllText(reportPath);
                        Ensure(content.Contains(kind == ProbeKind.Subtitle ? "[" : "\"Passed\"", StringComparison.Ordinal), "real caller writes existing report schema");
                    }
                    if (kind == ProbeKind.Shell) Ensure(File.Exists(Path.Combine(paths.LogDirectory, "shell-probe-scope.txt")), "real Shell caller always attempts scope report");

                    FileStream Lock() => new(reportPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                });
            }
        }
        Test("actual-shell-caller-final-scope-failure", async () =>
        {
            var paths = Paths("actual-shell-caller-final-scope-failure");
            paths.EnsureCreated();
            Directory.CreateDirectory(Path.Combine(paths.LogDirectory, "shell-probe-scope.txt"));
            var window = new ProbeHostWindow();
            ProbeApplication.Current.Reset();
            await ShellNavigationProbe.RunAsync(new(paths)
            {
                Window = window,
                ShellBody = record => record("A", true, ShellProbeSections.Settings)
            });
            Equal(1, ShellNavigationProbe.ExitCode, "real caller reports scope IO failure");
            Equal(1, window.Disposed, "scope failure cannot bypass caller cleanup");
            Equal(1, ProbeApplication.Current.Exited, "scope failure cannot bypass caller Exit");
            Ensure(File.ReadAllText(Path.Combine(paths.LogDirectory, "shell-probe.json")).Contains("\"Passed\": false", StringComparison.Ordinal), "main final report persists scope failure evidence");
        });
    }

    private static void RegisterFinalizerBoundaries()
    {
        Test("finalizer-failure-recorder-throws", async () =>
        {
            var code = 0;
            var close = 0;
            var exit = 0;
            var main = 0;
            var scope = 0;
            await ProbeFinalization.RunAsync(() => Task.FromException(new IOException("body")), () => true,
                () => { main++; throw new IOException("report"); },
                _ => throw new InvalidOperationException("failure evidence recorder broken"),
                () => { close++; throw new InvalidOperationException("dispose"); },
                () => { exit++; Equal(1, code, "all failures stay nonzero at exit"); }, value => code = value,
                () => { scope++; throw new IOException("scope"); });
            Equal(1, code, "failed recorder cannot green the run");
            Equal(1, close, "close still reached");
            Equal(1, exit, "exit still reached");
            Equal(1, main, "main attempted");
            Equal(1, scope, "scope attempted");
        });
        Test("finalizer-passed-evaluation-throws", async () =>
        {
            var code = 0;
            var close = 0;
            var exit = 0;
            var errors = 0;
            await ProbeFinalization.RunAsync(() => Task.CompletedTask, () => throw new InvalidOperationException("invalid assertion summary"),
                () => { }, _ => errors++, () => close++, () => exit++, value => code = value);
            Equal(1, code, "summary exception fails closed");
            Equal(1, close, "summary failure cleanup");
            Equal(1, exit, "summary failure exit");
            Equal(1, errors, "summary error retained");
        });
    }

    private enum ProbeKind { Subtitle, Shell }

    private sealed class ProbeRun
    {
        private readonly ProbeKind _kind;
        private readonly SubtitleProbeReport? _subtitle;
        private readonly ShellProbeReport? _shell;
        internal int Code { get; set; } = 99;
        internal int Disposed { get; private set; }
        internal int Exited { get; private set; }
        internal int MainAttempts { get; private set; }
        internal int ScopeAttempts { get; private set; }
        internal string MainPath { get; }
        internal string ScopePath { get; }
        internal List<string> Trace { get; } = [];
        internal Exception? DisposeError { get; init; }
        internal Action<string, string, Encoding, int>? MainWrite { get; set; }
        internal Action<string, string, Encoding, int>? ScopeWrite { get; set; }
        internal IReadOnlyList<ShellProbeReport.Check> ShellChecks => _shell?.Checks ?? [];
        internal bool HasFailure => FailureCount > 0;
        internal int FailureCount => _subtitle is not null
            ? _subtitle.Text.Split("[失败]", StringSplitOptions.None).Length - 1
            : ShellChecks.Count(check => !check.Passed);
        internal bool SavedFailure => File.ReadAllText(MainPath).Contains(_kind == ProbeKind.Subtitle ? "[失败]" : "\"Passed\": false", StringComparison.Ordinal);

        internal ProbeRun(ProbeKind kind, string directory, bool createDirectory = true, IReadOnlyCollection<string>? sections = null)
        {
            _kind = kind;
            if (createDirectory) Directory.CreateDirectory(directory);
            MainPath = Path.Combine(directory, kind == ProbeKind.Subtitle ? "subtitle-probe.txt" : "shell-probe.json");
            ScopePath = Path.Combine(directory, "shell-probe-scope.txt");
            if (kind == ProbeKind.Subtitle) _subtitle = new(directory, Write);
            else _shell = new(directory, sections, ShellProbeSections.All, Write);
        }

        internal Task RecordAsync(bool ok)
        {
            if (_subtitle is not null)
            {
                _subtitle.Record("A", ok, "isolated fixture");
                return Task.CompletedTask;
            }
            return _shell!.CaseAsync("A", () => ok ? Task.CompletedTask : Task.FromException(new InvalidOperationException("ordinary assertion")), 1, ShellProbeSections.Settings);
        }

        internal Task RecordSectionAsync(string? section) => _shell!.CaseAsync("section case", () => Task.CompletedTask, 1, section);

        internal Task ExecuteAsync(Func<Task> run, bool inspect = false)
        {
            if (_subtitle is not null) return _subtitle.RunAsync(run, Dispose, Exit, SetCode);
            return _shell!.RunAsync(run, inspect, Dispose, Exit, SetCode);
        }

        internal void Verify(int expected)
        {
            Equal(expected, Code, "final code");
            Equal(1, Disposed, "dispose reachable once regardless of report failures");
            Equal(1, Exited, "application Exit reachable once; no unobserved report IO escape");
            Equal("exit:" + expected, Trace[^1], "exit observes final code");
        }

        private void Write(string path, string content, Encoding encoding)
        {
            Equal(1, Code, "no success before writer returns");
            var main = path == MainPath;
            Ensure(main || path == ScopePath, "only isolated report paths allowed");
            var attempt = main ? ++MainAttempts : ++ScopeAttempts;
            Trace.Add($"write:{(main ? "main" : "scope")}:{attempt}:{Code}");
            var write = main ? MainWrite : ScopeWrite;
            if (write is not null) write(path, content, encoding, attempt);
            else File.WriteAllText(path, content, encoding);
        }

        private void Dispose()
        {
            Disposed++;
            Trace.Add("dispose:" + Code);
            if (DisposeError is not null) throw DisposeError;
        }

        private void Exit()
        {
            Exited++;
            Trace.Add("exit:" + Code);
        }

        private void SetCode(int value)
        {
            Code = value;
            Trace.Add("code:" + value);
        }
    }

    private static void Test(string name, Func<Task> run) => Cases.Add((name, run));
    private static AppPaths Paths(string name) => new(Path.Combine(_output, name));
    private static string SelfFile(AppPaths paths) => Path.Combine(paths.LogDirectory, "selfcheck-shell.txt");
    private static string ReadSelf(AppPaths paths) => File.ReadAllText(SelfFile(paths));
    private static void SeedPending(AppPaths paths) => Ensure(ShellSelfCheck.WriteForFixture(paths, "selfcheck-shell.txt", "[通过] A — fixture\n结果：未完成 —— waiting for report\n"), "seed via actual nonfinal writer");
    private static void Ensure(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message) =>
        Ensure(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}");
}
