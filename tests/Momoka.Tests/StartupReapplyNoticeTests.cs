using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using Momoka.Diagnostics;
using Momoka.Shell.Platform;

namespace Momoka.Tests;

/// <summary>
/// The full verbatim production ReapplyAndReloadAsync and Reload prefix with inert theme/render endpoints,
/// followed by the actual anonymous VM's StartupRow and ReapplyPlaybackPreferencesAsync. Never executes
/// user reset/import/file selection, registry, themes, UI, native or playback.
/// </summary>
internal static class StartupReapplyNoticeTests
{
    internal static int Run(string shellPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("R-065 共享恢复尾部 startup Error 归属回归（原样生产方法／实际匿名 VM，无主题／UI／注册表）");
        try
        {
            var shell = new StartupShellFixture(shellPath, sourceRoot);
            Register(shell);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Register(StartupShellFixture shell)
    {
        void Case(string name, Func<Task> run) => TestHarness.Test("R-065 shared tail：" + name,
            () => run().GetAwaiter().GetResult());

        TestHarness.Test("R-065 默认恢复／导入两 caller 只 await 同一生产共享尾部（源码契约）", () =>
        {
            var source = File.ReadAllText(Path.Combine(shell.SourceRoot, "src", "Momoka.Shell", "ViewModels", "SettingsViewModel.cs"));
            foreach (var (begin, end) in new[]
            {
                ("    private async Task RestoreDefaultsAsync()", "    private async Task BackupAsync()"),
                ("    private async Task RestoreFromFileAsync()", "    internal async Task<bool> ProbeSubtitleRestoreAsync()")
            })
            {
                var first = source.IndexOf(begin, StringComparison.Ordinal);
                var last = source.IndexOf(end, first, StringComparison.Ordinal);
                Assert.True(first >= 0 && last > first);
                var body = Regex.Replace(source[first..last], @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);
                Assert.Equal(1, Regex.Matches(body, @"await ReapplyAndReloadAsync\(").Count);
                Assert.Equal(0, Regex.Matches(body, @"ReapplyPlaybackPreferencesAsync\(").Count);
                Assert.Equal(0, Regex.Matches(body, @"InfoBarSeverity\.Success").Count);
            }
        });

        foreach (var caller in new[] { "恢复默认", "导入" })
        {
            foreach (var approval in new[] { false, true })
            {
                foreach (var kind in new[] { "权限", "安全", "IO" })
                {
                    Case($"{caller} Reload 的 {(approval ? "Approval" : "Run")} {kind}初读失败不被成功推送覆盖", async () =>
                    {
                        var state = new StartupReapplyFixture(shell);
                        var error = Error(kind);
                        if (approval) state.Memory.ApprovalFailure = error;
                        else state.Memory.RunFailure = error;
                        await state.Run(caller);
                        Console.WriteLine($"    {caller}/{(approval ? "Approval" : "Run")}/{kind}：Reload曾Error={state.ReloadObservedError}；"
                            + $"最终={shell.Get<object>(state.Vm, "NoticeSeverity")}；行={shell.Get<bool>(state.Row!, "IsOn")}");
                        Assert.True(state.ReloadObservedError, "actual StartupRow did publish Error during this Reload");
                        Assert.False(shell.Get<bool>(state.Row!, "IsOn"));
                        RequireStartupError(shell, state, error);
                        RequirePushes(state);
                        Assert.Equal(0, state.Settings.Saves);
                        Assert.Equal(0, state.Memory.Writes.Count);
                    });
                }
            }
            foreach (var enabled in new[] { false, true })
            {
                Case($"{caller} 无 startup 读失败且初值{enabled}，原成功反馈照旧", async () =>
                {
                    var state = new StartupReapplyFixture(shell);
                    state.Memory.RunValue = enabled ? state.Command : null;
                    await state.Run(caller);
                    Assert.False(state.ReloadObservedError);
                    Assert.Equal(enabled, shell.Get<bool>(state.Row!, "IsOn"));
                    Assert.Equal("Success", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
                    Assert.Equal(caller, shell.Get<string>(state.Vm, "NoticeMessage"));
                    RequirePushes(state);
                });
            }
            Case($"{caller} 无本次startup错误但保存失败，原未保存Warning照旧", async () =>
            {
                var state = new StartupReapplyFixture(shell);
                state.Settings.HasUnsavedChanges = true;
                await state.Run(caller);
                Assert.Equal("Warning", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
                Assert.Contains("尚未保存", shell.Get<string>(state.Vm, "NoticeMessage"));
                RequirePushes(state);
            });
            Case($"{caller} 本次startup读失败且保存失败，不被末尾Warning覆盖", async () =>
            {
                var state = new StartupReapplyFixture(shell);
                var error = Error("权限");
                state.Settings.HasUnsavedChanges = true;
                state.Memory.RunFailure = error;
                await state.Run(caller);
                RequireStartupError(shell, state, error);
                Assert.True(shell.Get<bool>(state.Vm, "HasUnsavedChanges"), "the existing retry-save state is not cleared");
                RequirePushes(state);
            });
            Case($"{caller} 上一行startup错误但本次Reload成功，不笼统禁止成功反馈", async () =>
            {
                var state = new StartupReapplyFixture(shell);
                state.Memory.RunFailure = Error("权限");
                state.ReloadRow();
                Assert.Equal("Error", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
                state.Memory.RunFailure = null;
                await state.Run(caller);
                Assert.Equal("Success", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
                Assert.Equal(caller, shell.Get<string>(state.Vm, "NoticeMessage"));
            });
            Case($"{caller} 此前其他Error不属于本次Reload，不阻止原成功", async () =>
            {
                var state = new StartupReapplyFixture(shell);
                shell.Notify(state.Vm, "其他操作", "先前无关错误", "Error");
                await state.Run(caller);
                Assert.Equal("Success", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
                Assert.Equal(caller, shell.Get<string>(state.Vm, "NoticeMessage"));
            });
            Case($"{caller} 已捕获本次Reload错误，等待后新Reload成功不能借用其归属", async () =>
            {
                var state = new StartupReapplyFixture(shell);
                var error = Error("权限");
                state.Memory.RunFailure = error;
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                state.ReloadFinish = gate.Task;
                var contract = new StartupReapplyContract(state);
                var run = contract.Run(caller);
                Assert.False(run.IsCompleted);
                state.Memory.RunFailure = null;
                state.ReloadFinish = Task.CompletedTask;
                await contract.ReloadOnly();
                gate.SetResult();
                await run;
                RequireStartupError(shell, state, error);
            });
            Case($"{caller} 本次Reload成功，等待后另一行读错不能借用旧尾部拦成功", async () =>
            {
                var state = new StartupReapplyFixture(shell);
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                state.ReloadFinish = gate.Task;
                var run = state.Run(caller);
                Assert.False(run.IsCompleted);
                state.Memory.RunFailure = Error("权限");
                state.ReloadRow();
                gate.SetResult();
                await run;
                Assert.Equal("Success", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
                Assert.Equal(caller, shell.Get<string>(state.Vm, "NoticeMessage"));
            });
        }
        foreach (var screenshot in new[] { false, true })
        {
            Case($"startup读失败伴随{(screenshot ? "截图" : "字幕")}推送失败，保留原推送失败不改成功规则", async () =>
            {
                var state = new StartupReapplyFixture(shell);
                state.Memory.RunFailure = Error("权限");
                var error = new IOException("anonymous push failure");
                if (screenshot) state.ScreenshotError = error;
                else state.SubtitleError = error;
                await state.Run("restore");
                Assert.Equal("Error", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
                Assert.Equal(screenshot ? "当前播放的截图选项未完全更新" : "当前播放的字幕外观未完全更新",
                    shell.Get<string>(state.Vm, "NoticeTitle"));
                Assert.Contains(error.Message, shell.Get<string>(state.Vm, "NoticeMessage"));
                RequirePushes(state);
            });
            Case($"startup读失败伴随{(screenshot ? "截图" : "字幕")}推送取消，仍无成功覆盖", async () =>
            {
                var state = new StartupReapplyFixture(shell);
                var readError = Error("权限");
                state.Memory.RunFailure = readError;
                if (screenshot) state.ScreenshotError = new OperationCanceledException();
                else state.SubtitleError = new OperationCanceledException();
                await state.Run("restore");
                RequireStartupError(shell, state, readError);
                RequirePushes(state);
            });
        }
        Case("单独播放偏好尾部无Reload归属，旧startup字段不笼统拦截原成功", async () =>
        {
            var state = new StartupReapplyFixture(shell);
            state.Memory.RunFailure = Error("权限");
            state.ReloadRow();
            await state.Push("direct", null);
            Assert.Equal("Success", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
            Assert.Equal("direct", shell.Get<string>(state.Vm, "NoticeMessage"));
        });
    }

    private static Exception Error(string kind) => kind switch
    {
        "权限" => new UnauthorizedAccessException("anonymous startup read denied"),
        "安全" => new SecurityException("anonymous startup security denied"),
        _ => new IOException("anonymous startup read IO")
    };
    private static void RequireStartupError(StartupShellFixture shell, StartupReapplyFixture state, Exception error)
    {
        Assert.True(shell.Get<bool>(state.Vm, "NoticeOpen"));
        Assert.Equal("Error", shell.Get<object>(state.Vm, "NoticeSeverity").ToString());
        Assert.Equal("读取开机自启状态失败", shell.Get<string>(state.Vm, "NoticeTitle"));
        Assert.Contains(Failure.Describe(error), shell.Get<string>(state.Vm, "NoticeMessage"));
        Assert.Contains("无法确认当前状态", shell.Get<string>(state.Vm, "NoticeMessage"));
    }
    private static void RequirePushes(StartupReapplyFixture state)
    {
        Assert.Equal(1, state.SubtitlePushes);
        Assert.Equal(1, state.ScreenshotPushes);
        Assert.True(state.Reloads >= 1);
        Assert.Equal(0, state.Memory.Writes.Count);
        Assert.Equal(0, state.Settings.Saves);
    }
}

internal sealed class StartupReapplyFixture
{
    private readonly StartupShellFixture _shell;
    internal object Vm { get; }
    internal StartupMemorySettings Settings { get; }
    internal MemoryStartupRegistry Memory { get; }
    internal string Command => Momoka.Configuration.StartupEntry.Command(Environment.ProcessPath!);
    internal object? Row { get; private set; }
    internal bool ReloadObservedError { get; private set; }
    internal int Reloads { get; private set; }
    internal int SubtitlePushes { get; private set; }
    internal int ScreenshotPushes { get; private set; }
    internal Exception? SubtitleError { get; set; }
    internal Exception? ScreenshotError { get; set; }
    internal Task ReloadFinish { get; set; } = Task.CompletedTask;
    internal string? StartupReloadFailure
    {
        get => _shell.StartupReloadFailure(Vm);
        set => _shell.SetStartupReloadFailure(Vm, value);
    }

    internal StartupReapplyFixture(StartupShellFixture shell)
    {
        _shell = shell;
        Memory = StartupRegistryBoundary.Reset();
        Memory.RunValue = Command;
        var page = shell.Attach(new StartupRegistration(), subtitlePush: () =>
        {
            SubtitlePushes++;
            return SubtitleError is { } error ? Task.FromException(error) : Task.CompletedTask;
        }, screenshotPush: () =>
        {
            ScreenshotPushes++;
            return ScreenshotError is { } error ? Task.FromException(error) : Task.CompletedTask;
        });
        Vm = page.Vm;
        Settings = page.Settings;
    }
    internal void SyncSaveState() => _shell.Call(Vm, "SyncSaveState");
    internal void ReloadRow()
    {
        Reloads++;
        Row = _shell.Row(Vm);
        ReloadObservedError = _shell.Get<bool>(Vm, "NoticeOpen") && _shell.Get<object>(Vm, "NoticeSeverity").ToString() == "Error";
    }
    internal Task Run(string notice) => new StartupReapplyContract(this).Run(notice);
    internal Task Push(string notice, string? startupReadFailure)
    {
        var method = Vm.GetType().GetMethod("ReapplyPlaybackPreferencesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // The first red runs the unmodified pre-P2 one-parameter method; green executes its final same body.
        return (Task)_shell.Call(Vm, "ReapplyPlaybackPreferencesAsync",
            method.GetParameters().Length == 1 ? [notice] : [notice, startupReadFailure])!;
    }
}
