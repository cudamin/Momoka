using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Security;
using System.Security.Cryptography;
using Momoka.Configuration;
using Momoka.Services;
using Momoka.Shell.Platform;

namespace Momoka.Tests;

/// <summary>
/// The whole source-linked platform implementation plus the explicitly supplied actual Shell VM/row.
/// Memory failures prove control flow under those inputs, never real registry permissions or WinUI.
/// </summary>
internal static class StartupStateContractTests
{
    internal static int Run(string shellPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("R-065 完整启动登记源码／真实 SettingsViewModel 匿名隔离契约；不是部署、ACL 或真实 UI 验证");
        try
        {
            var shell = new StartupShellFixture(shellPath, sourceRoot);
            Console.WriteLine("模式：正确读取／Apply／已有 Error 反馈契约；旧反例日志独立保留，部署与实际 UI 未验");
            Register(shell);
            StartupEntryTests.Register();
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
        void Case(string name, Action run) => TestHarness.Test(
            "正确契约：" + name, run);

        foreach (var enabled in new[] { false, true })
        {
            Case($"R-065 已确认初值 {enabled} 由完整源码读出，真实行初始化不写", () =>
            {
                var memory = Seed(enabled);
                var startup = new StartupRegistration();
                Assert.Equal(enabled, startup.IsEnabled);
                var page = shell.Attach(startup);
                var row = shell.Row(page.Vm);
                Assert.Equal(enabled, shell.Get<bool>(row, "IsOn"));
                Assert.Equal(0, memory.Writes.Count);
                Assert.Equal(0, page.Settings.Saves);
                Assert.False(shell.Get<bool>(page.Vm, "NoticeOpen"));
            });
            Case($"R-065 实际 VM 正常切换至 {!enabled} 后准确回读，无递归写入", () =>
            {
                var memory = Seed(enabled);
                var page = shell.Attach(new StartupRegistration());
                var row = shell.Row(page.Vm);
                shell.Set(row, "IsOn", !enabled);
                Assert.Equal(!enabled, new StartupRegistration().IsEnabled);
                Assert.Equal(!enabled, shell.Get<bool>(row, "IsOn"));
                Assert.Equal(1, page.Startup.Requests.Count);
                Assert.True(page.Startup.Results.Single());
                Assert.False(shell.Get<bool>(page.Vm, "NoticeOpen"));
                Assert.Equal(!enabled ? 2 : 1, memory.Writes.Count);
            });
            Case($"R-065 拒写但准确读为 {enabled} 保留既有回拨和错误", () =>
            {
                var memory = Seed(enabled);
                var page = shell.Attach(new StartupRegistration());
                var row = shell.Row(page.Vm);
                memory.WriteFailure = _ => new UnauthorizedAccessException("fixture denied write");
                shell.Set(row, "IsOn", !enabled);
                Assert.Equal(enabled, shell.Get<bool>(row, "IsOn"));
                Assert.Equal(enabled, new StartupRegistration().IsEnabled);
                Assert.False(page.Startup.Results.Single());
                Assert.Equal(1, page.Startup.Requests.Count, "Reseed is not another user write");
                Assert.Equal(1, memory.Writes.Count);
                RequireError(shell, page.Vm);
                Assert.Contains(enabled ? "没能关掉" : "没能打开", (shell.Get<string?>(page.Vm, "NoticeMessage") ?? ""));
            });
        }

        Case("R-065 Run 缺失／Approval 缺失是确定状态，不是读取失败", () =>
        {
            var memory = Seed(false);
            memory.RunExists = false;
            memory.ApprovalExists = false;
            Assert.False(new StartupRegistration().IsEnabled);
            memory.RunExists = true;
            memory.RunValue = Command;
            Assert.True(new StartupRegistration().IsEnabled);
            Assert.Equal(0, memory.Writes.Count);
        });
        Case("R-065 正常读取任务管理器禁用标记，不误判 Run 存在即开启", () =>
        {
            var memory = Seed(true);
            memory.ApprovalValue = new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            var page = shell.Attach(new StartupRegistration());
            Assert.False(shell.Get<bool>(shell.Row(page.Vm), "IsOn"));
            Assert.Equal(Command, memory.RunValue);
            Assert.False(shell.Get<bool>(page.Vm, "NoticeOpen"));
        });

        foreach (var kind in new[] { "权限", "安全", "IO" })
        {
            foreach (var approval in new[] { false, true })
            {
                var key = approval ? "Approval" : "Run";
                Case($"R-065 {key} {kind}读取失败不能返回确定 bool", () =>
                {
                    var memory = Seed(true);
                    memory.ApprovalValue = new byte[] { 3 };
                    var error = ReadError(kind);
                    if (approval) memory.ApprovalFailure = error;
                    else memory.RunFailure = error;
                    bool? actual = null;
                    var observed = ReadFailure(() => actual = new StartupRegistration().IsEnabled);
                    Console.WriteLine($"    匿名 {key}/{kind}：bool={actual?.ToString() ?? "未返回"}；异常={observed?.GetType().Name ?? "无"}");
                    Assert.NotNull(observed, "IsEnabled 的事实契约不能把读取失败当值不存在；旧实现悄悄返回 bool");
                    Assert.Equal(error, observed);
                    Assert.Null(actual, "failed reads never publish a confirmed bool");
                    Assert.Equal(0, memory.Writes.Count);
                });
                Case($"R-065 真实 VM 初读 {key} {kind}失败必须保留错误信息", () =>
                {
                    var memory = Seed(true);
                    memory.ApprovalValue = new byte[] { 3 };
                    var error = ReadError(kind);
                    if (approval) memory.ApprovalFailure = error;
                    else memory.RunFailure = error;
                    var page = shell.Attach(new StartupRegistration());
                    object? row = null;
                    var observed = ReadFailure(() => row = shell.Row(page.Vm));
                    Console.WriteLine($"    实际 VM 初读 {key}/{kind}：行={(row is null ? "未建成" : shell.Get<bool>(row, "IsOn"))}；"
                        + $"通知={shell.Get<bool>(page.Vm, "NoticeOpen")}；异常={observed?.GetType().Name ?? "无"}");
                    Assert.Null(observed, "the actual row contains read failure rather than aborting the settings card");
                    Assert.NotNull(row);
                    Assert.False(shell.Get<bool>(row!, "IsOn"), "keep the existing two-state default, not a new unknown control");
                    RequireReadError(shell, page.Vm, error);
                    Assert.DoesNotContain("仍会在登录后启动", (shell.Get<string?>(page.Vm, "NoticeMessage") ?? ""));
                    Assert.DoesNotContain("不会在登录后启动", (shell.Get<string?>(page.Vm, "NoticeMessage") ?? ""));
                    Assert.Equal(0, memory.Writes.Count);
                    Assert.Equal(0, page.Settings.Saves);
                });
            }
            Case($"R-065 拒删后 Run {kind}回读失败，实际 VM 不能静默吞 Apply=false", () =>
            {
                var memory = Seed(true);
                var page = shell.Attach(new StartupRegistration());
                var row = shell.Row(page.Vm);
                memory.WriteFailure = _ => new UnauthorizedAccessException("fixture refused delete");
                var error = ReadError(kind);
                memory.AfterWriteAttempt = () => memory.RunFailure = error;
                var observed = ReadFailure(() => shell.Set(row, "IsOn", false));
                Console.WriteLine($"    注入回执：Run仍在={memory.RunValue is not null}；行={shell.Get<bool>(row, "IsOn")}；"
                    + $"Apply={string.Join(',', page.Startup.Results)}；通知={shell.Get<bool>(page.Vm, "NoticeOpen")}");
                Assert.Equal(Command, memory.RunValue, "the denied anonymous delete must not alter stored data");
                Assert.False(page.Startup.Results.Single());
                Assert.Null(observed);
                Assert.True(shell.Get<bool>(row, "IsOn"), "read-back failure retains the prior confirmed value, never the unconfirmed requested off state");
                RequireReadError(shell, page.Vm, error);
                Assert.DoesNotContain("仍会在登录后启动", (shell.Get<string?>(page.Vm, "NoticeMessage") ?? ""));
                Assert.DoesNotContain("不会在登录后启动", (shell.Get<string?>(page.Vm, "NoticeMessage") ?? ""));
                Assert.Equal(1, page.Startup.Requests.Count);
            });
            Case($"R-065 启用拒清 Approval 且 {kind}回读失败，真实 VM 不能冒充启用成功", () =>
            {
                var memory = Seed(true);
                memory.ApprovalValue = new byte[] { 3 };
                var page = shell.Attach(new StartupRegistration());
                var row = shell.Row(page.Vm);
                Assert.False(shell.Get<bool>(row, "IsOn"), "初值确认为任务管理器禁用");
                var error = ReadError(kind);
                memory.WriteFailure = key => key == StartupRegistryBoundary.ApprovalKey
                    ? new UnauthorizedAccessException("anonymous approval cleanup denied") : null;
                memory.AfterWriteAttempt = () => memory.ApprovalFailure = error;
                var observed = ReadFailure(() => shell.Set(row, "IsOn", true));
                Assert.False(page.Startup.Results.Single());
                Assert.Equal(Command, memory.RunValue);
                Assert.True(StartupEntry.TurnedOff((byte[])memory.ApprovalValue!), "禁用标记原样保留；不据此猜真实 Windows 登录行为");
                Assert.Null(observed);
                Assert.False(shell.Get<bool>(row, "IsOn"), "failed confirmation retains the initially confirmed disabled state");
                RequireReadError(shell, page.Vm, error);
                Assert.Equal(1, page.Startup.Requests.Count);
                Assert.Equal(2, memory.Writes.Count);
            });
            Case($"R-065 写入完成但 Run {kind}回读失败，Apply 不能答成功或逃逸", () =>
            {
                var memory = Seed(true);
                var error = ReadError(kind);
                memory.AfterWriteAttempt = () => memory.RunFailure = error;
                bool? applied = null;
                var observed = ReadFailure(() => applied = new StartupRegistration().Apply(false));
                Assert.Null(observed, "Apply 的失败返回契约不能逃逸回读 IO");
                Assert.Equal<bool?>(false, applied, "unknown read-back is not confirmed disabled");
                Assert.Null(memory.RunValue, "the anonymous write completed; only confirmation failed");
            });
        }

        Case("R-065 两格部分写成后拒绝 Approval，准确回读等于目标也不能吞失败", () =>
        {
            var memory = Seed(false);
            var page = shell.Attach(new StartupRegistration());
            var row = shell.Row(page.Vm);
            memory.WriteFailure = key => key == StartupRegistryBoundary.ApprovalKey
                ? new UnauthorizedAccessException("fixture refused approval cleanup") : null;
            shell.Set(row, "IsOn", true);
            Assert.False(page.Startup.Results.Single());
            Assert.True(new StartupRegistration().IsEnabled, "accurate final bool happens to equal requested value");
            RequireError(shell, page.Vm);
            Assert.Contains("没能确认", shell.Get<string>(page.Vm, "NoticeMessage"));
            Assert.Equal(1, page.Startup.Requests.Count);
            Assert.Equal(2, memory.Writes.Count);
        });
        Case("R-065 操作后状态被另一写入改回，Apply true 也保留既有回拨防护", () =>
        {
            Seed(false);
            var page = shell.Attach(new StartupRegistration(), afterApply: _ => StartupRegistryBoundary.CurrentUser.RunValue = null);
            var row = shell.Row(page.Vm);
            shell.Set(row, "IsOn", true);
            Assert.True(page.Startup.Results.Single());
            Assert.False(shell.Get<bool>(row, "IsOn"));
            RequireError(shell, page.Vm);
        });
        foreach (var initial in new[] { false, true })
        {
            foreach (var kind in new[] { "权限", "安全", "IO" })
            {
                Case($"R-065 已写至 {!initial} 但回读{kind}失败，真实行只保留 {initial} 旧读数和错误", () =>
                {
                    var memory = Seed(initial);
                    var page = shell.Attach(new StartupRegistration());
                    var row = shell.Row(page.Vm);
                    var error = ReadError(kind);
                    memory.AfterWriteAttempt = () => memory.RunFailure = error;
                    var observed = ReadFailure(() => shell.Set(row, "IsOn", !initial));
                    Assert.Null(observed);
                    Assert.False(page.Startup.Results.Single());
                    Assert.Equal(initial, shell.Get<bool>(row, "IsOn"), "never label an unconfirmed target as fact");
                    Assert.Equal(!initial ? Command : null, memory.RunValue, "anonymous write did complete before confirmation failed");
                    RequireReadError(shell, page.Vm, error);
                    Assert.Equal(1, page.Startup.Requests.Count, "Reseed cannot reapply or undo the completed platform write");
                    Assert.Equal(!initial ? 2 : 1, memory.Writes.Count);
                });
            }
            Case($"R-065 {initial} 准确拒写后本行成功，已有错误反馈关闭且不新增成功 UX", () =>
            {
                var memory = Seed(initial);
                var page = shell.Attach(new StartupRegistration());
                var row = shell.Row(page.Vm);
                memory.WriteFailure = _ => new UnauthorizedAccessException("anonymous write denied");
                shell.Set(row, "IsOn", !initial);
                RequireError(shell, page.Vm);
                memory.WriteFailure = null;
                shell.Set(row, "IsOn", !initial);
                Assert.Equal(!initial, shell.Get<bool>(row, "IsOn"));
                Assert.Equal(2, page.Startup.Requests.Count);
                Assert.True(page.Startup.Results.Last());
                Assert.False(shell.Get<bool>(page.Vm, "NoticeOpen"), "clear only the retained failure, no new success banner");
            });
        }
        Case("R-065 初读失败后按原开关确认成功，撤本行 Error 并保留二态结构", () =>
        {
            var memory = Seed(true);
            var error = ReadError("权限");
            memory.RunFailure = error;
            var page = shell.Attach(new StartupRegistration());
            var row = shell.Row(page.Vm);
            RequireReadError(shell, page.Vm, error);
            Assert.False(shell.Get<bool>(row, "IsOn"));
            memory.RunFailure = null;
            shell.Set(row, "IsOn", true);
            Assert.True(page.Startup.Results.Single());
            Assert.True(shell.Get<bool>(row, "IsOn"));
            Assert.False(shell.Get<bool>(page.Vm, "NoticeOpen"));
        });
        Case("R-065 无可信初值且操作／回读继续失败，不让用户目标冒充确认状态", () =>
        {
            var memory = Seed(true);
            var error = ReadError("权限");
            memory.RunFailure = error;
            var page = shell.Attach(new StartupRegistration());
            var row = shell.Row(page.Vm);
            memory.WriteFailure = _ => new UnauthorizedAccessException("anonymous write denied");
            shell.Set(row, "IsOn", true);
            Assert.False(page.Startup.Results.Single());
            Assert.False(shell.Get<bool>(row, "IsOn"));
            RequireReadError(shell, page.Vm, error);
            Assert.Equal(1, page.Startup.Requests.Count);
            Assert.Equal(Command, memory.RunValue);
        });
        Case("R-065 本行后续成功不清除其他操作的已有 Error", () =>
        {
            var memory = Seed(true);
            var page = shell.Attach(new StartupRegistration());
            var row = shell.Row(page.Vm);
            memory.WriteFailure = _ => new UnauthorizedAccessException("anonymous write denied");
            shell.Set(row, "IsOn", false);
            RequireError(shell, page.Vm);
            shell.Set(page.Vm, "NoticeMessage", "另一操作的错误");
            memory.WriteFailure = null;
            shell.Set(row, "IsOn", false);
            Assert.True(page.Startup.Results.Last());
            RequireError(shell, page.Vm);
            Assert.Equal("另一操作的错误", shell.Get<string>(page.Vm, "NoticeMessage"));
        });

        Case("R-065 同消息不同标题的其他 Error 不归本行成功尾部清除", () =>
        {
            var memory = Seed(true);
            var page = shell.Attach(new StartupRegistration());
            var row = shell.Row(page.Vm);
            memory.WriteFailure = _ => new UnauthorizedAccessException("anonymous write denied");
            shell.Set(row, "IsOn", false);
            RequireError(shell, page.Vm);
            var message = shell.Get<string>(page.Vm, "NoticeMessage");
            shell.Set(page.Vm, "NoticeTitle", "另一操作");
            memory.WriteFailure = null;
            shell.Set(row, "IsOn", false);
            Assert.True(page.Startup.Results.Last());
            RequireError(shell, page.Vm);
            Assert.Equal("另一操作", shell.Get<string>(page.Vm, "NoticeTitle"));
            Assert.Equal(message, shell.Get<string>(page.Vm, "NoticeMessage"));
        });

        foreach (var enabled in new[] { false, true })
        {
            Case($"R-065 自检窄调用者读 {enabled} 一次，不写并保留原检查名", () =>
            {
                var memory = Seed(enabled);
                var read = StartupSelfCheckContract.Report(new StartupSelfCheckRow(enabled, "启动项不进设置文件"), new StartupRegistration());
                Assert.True(read.Ok);
                Assert.Contains(enabled ? "机器上是开" : "机器上是关", read.Detail);
                Assert.Equal(1, memory.RunReads);
                Assert.Equal(1, memory.ApprovalReads);
                Assert.Equal(0, memory.Writes.Count);
            });
        }
        foreach (var approval in new[] { false, true })
        {
            foreach (var kind in new[] { "权限", "安全", "IO" })
            {
                Case($"R-065 自检 {(approval ? "Approval" : "Run")} {kind}读失败只记原检查失败不逃逸", () =>
                {
                    var memory = Seed(true);
                    var error = ReadError(kind);
                    if (approval) memory.ApprovalFailure = error;
                    else memory.RunFailure = error;
                    var read = StartupSelfCheckContract.Report(new StartupSelfCheckRow(true, "启动项不进设置文件"), new StartupRegistration());
                    Assert.False(read.Ok);
                    Assert.Contains("读取开机自启状态失败", read.Detail);
                    Assert.Contains(Momoka.Diagnostics.Failure.Describe(error), read.Detail);
                    Assert.Contains("无法确认当前状态", read.Detail);
                    Assert.DoesNotContain("机器上是开", read.Detail);
                    Assert.DoesNotContain("机器上是关", read.Detail);
                    Assert.Equal(1, memory.RunReads);
                    Assert.Equal(approval ? 1 : 0, memory.ApprovalReads);
                    Assert.Equal(0, memory.Writes.Count);
                });
            }
        }
        Case("R-065 自检不可用仍通过设计隐藏，不访问任何注册表替身", () =>
        {
            var memory = Seed(true);
            StartupRegistryBoundary.Packaged = true;
            memory.RunFailure = ReadError("权限");
            memory.ApprovalFailure = ReadError("权限");
            var read = StartupSelfCheckContract.Report(null, new StartupRegistration());
            Assert.True(read.Ok);
            Assert.Contains("按设计整个不显示", read.Detail);
            Assert.Equal(0, memory.RunReads);
            Assert.Equal(0, memory.ApprovalReads);
            Assert.Equal(0, memory.Writes.Count);
        });
        Case("R-065 自检读取准确但行不符，原检查仍失败", () =>
        {
            Seed(true);
            var read = StartupSelfCheckContract.Report(new StartupSelfCheckRow(false, "启动项不进设置文件"), new StartupRegistration());
            Assert.False(read.Ok);
        });
        Case("R-065 不可用整行不创建、不读取；恢复后执行真实 StartupRow", () =>
        {
            var memory = Seed(true);
            memory.RunFailure = new UnauthorizedAccessException("must not read while unavailable");
            StartupRegistryBoundary.Packaged = true;
            var startup = new StartupRegistration();
            var page = shell.Attach(startup);
            var contract = new StartupAvailabilityContract(startup, () => shell.Row(page.Vm));
            Assert.Equal(0, contract.Build().Count);
            Assert.Equal(0, memory.RunReads);
            Assert.Equal(0, memory.ApprovalReads);
            Assert.Equal(0, memory.Writes.Count);
            StartupRegistryBoundary.Packaged = false;
            memory.RunFailure = null;
            var rows = contract.Build();
            Assert.Equal(1, rows.Count);
            Assert.True(shell.Get<bool>(rows[0], "IsOn"));
            Assert.Equal(0, page.Settings.Saves);
        });
        Case("R-065 平台替身拒绝任何别的注册表路径和值名", () =>
        {
            var memory = StartupRegistryBoundary.Reset();
            Assert.Throws<AssertionException>(() => memory.OpenSubKey("unrelated-path"));
            using var key = memory.OpenSubKey(StartupRegistryBoundary.RunKey)!;
            Assert.Throws<AssertionException>(() => key.GetValue("unrelated-value"));
            Assert.Throws<AssertionException>(() => key.DeleteValue("unrelated-value", throwOnMissingValue: false));
            Assert.Equal(0, memory.Writes.Count);
        });
        Case("R-065 当前独立读取异常不写盘、不调用未附加服务器／native／播放", () =>
        {
            var memory = Seed(true);
            var page = shell.Attach(new StartupRegistration());
            _ = shell.Row(page.Vm);
            Assert.Equal(0, page.Settings.Saves);
            Assert.Equal(0, memory.Writes.Count);
            Assert.Equal(0, page.Startup.Requests.Count);
        });
        Case("R-065 未 Attach 不可用门控不借空依赖创建行", () =>
            Assert.Equal(0, new StartupAvailabilityContract(null, () => throw new AssertionException("must remain hidden")).Build().Count));
    }

    private static string Command => StartupEntry.Command(Environment.ProcessPath!);
    private static MemoryStartupRegistry Seed(bool enabled)
    {
        var memory = StartupRegistryBoundary.Reset();
        memory.RunValue = enabled ? Command : null;
        return memory;
    }
    private static Exception ReadError(string kind) => kind switch
    {
        "权限" => new UnauthorizedAccessException("anonymous read denied"),
        "安全" => new SecurityException("anonymous security read denied"),
        _ => new IOException("anonymous registry read failed")
    };
    private static Exception? ReadFailure(Action read)
    {
        try { read(); return null; }
        catch (TargetInvocationException error) when (error.InnerException is not null) { return error.InnerException; }
        catch (Exception error) { return error; }
    }
    private static void RequireReadError(StartupShellFixture shell, object vm, Exception error)
    {
        RequireError(shell, vm);
        Assert.Equal("读取开机自启状态失败", shell.Get<string>(vm, "NoticeTitle"));
        Assert.Contains(Momoka.Diagnostics.Failure.Describe(error), shell.Get<string>(vm, "NoticeMessage"));
        Assert.Contains("无法确认当前状态", shell.Get<string>(vm, "NoticeMessage"));
        Assert.Contains("系统启动项中核对", shell.Get<string>(vm, "NoticeMessage"));
        Assert.DoesNotContain("仍会在登录后启动", shell.Get<string>(vm, "NoticeMessage"));
        Assert.DoesNotContain("不会在登录后启动", shell.Get<string>(vm, "NoticeMessage"));
    }
    private static void RequireError(StartupShellFixture shell, object vm)
    {
        Assert.True(shell.Get<bool>(vm, "NoticeOpen"), "the actual VM must not silently report a confirmed startup state");
        Assert.Equal("Error", shell.Get<object>(vm, "NoticeSeverity").ToString());
        Assert.True(shell.Get<string?>(vm, "NoticeMessage") is { Length: > 0 }, "the failure must retain an actionable explanation");
    }
}

internal sealed class StartupShellFixture
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Type _vm;
    private readonly Type _startup;
    internal string SourceRoot { get; }
    internal StartupShellFixture(string assemblyPath, string sourceRoot)
    {
        var root = Path.GetFullPath(sourceRoot);
        SourceRoot = root;
        var compiledRoot = new DirectoryInfo(Path.GetDirectoryName(typeof(StartupStateContractTests).Assembly.Location)!);
        while (!File.Exists(Path.Combine(compiledRoot.FullName, "Momoka.sln")))
            compiledRoot = compiledRoot.Parent ?? throw new InvalidOperationException("test assembly is not in its source worktree");
        Assert.Equal(compiledRoot.FullName, root, "source root must be the same worktree as this source-linked test assembly");
        var expected = Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release",
            "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
        Assert.Equal(expected, Path.GetFullPath(assemblyPath), "only this tree's standard current Release Shell output");
        Assert.True(File.Exists(expected), "build this tree's current Shell before running the fixture");
        foreach (var relative in new[] { "src/Momoka.Shell/Platform/StartupRegistration.cs", "src/Momoka.Shell/ViewModels/SettingsViewModel.cs" })
        {
            var path = Path.Combine(root, relative);
            if (relative.EndsWith("StartupRegistration.cs", StringComparison.Ordinal))
            {
                var source = File.ReadAllText(path);
                Assert.False(source.Contains("Microsoft.Win32.Registry", StringComparison.Ordinal)
                    || source.Contains("global::", StringComparison.Ordinal), "fixture refuses qualified platform escapes before executing any linked method");
                Assert.True(source.Contains("using Microsoft.Win32;", StringComparison.Ordinal)
                    && source.Contains("Registry.CurrentUser", StringComparison.Ordinal)
                    && source.Contains("Windows.ApplicationModel.Package.Current", StringComparison.Ordinal),
                    "review namespace substitute boundaries again when production endpoint shape changes");
            }
            Console.WriteLine($"source={path}; sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}");
        }
        Console.WriteLine($"shell={expected}; sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expected)))}");
        var directory = Path.GetDirectoryName(expected)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        var shell = AssemblyLoadContext.Default.LoadFromAssemblyPath(expected);
        _vm = shell.GetType("Momoka.Shell.ViewModels.SettingsViewModel", throwOnError: true)!;
        _startup = shell.GetType("Momoka.Shell.Platform.IStartupRegistration", throwOnError: true)!;
        Assert.False(shell == typeof(StartupRegistration).Assembly, "VM and the complete source-linked platform are distinct actual assemblies");
    }
    internal (object Vm, StartupMemorySettings Settings, StartupContractProxy Startup) Attach(
        IStartupRegistration platform, Action<bool>? afterApply = null, Func<Task>? subtitlePush = null, Func<Task>? screenshotPush = null)
    {
        var vm = Activator.CreateInstance(_vm)!;
        Assert.True(vm is INotifyPropertyChanged, "actual generated observable VM");
        var settings = new StartupMemorySettings();
        var startup = (StartupContractProxy)DispatchProxy.Create(_startup, typeof(StartupContractProxy));
        startup.Platform = platform;
        startup.AfterApply = afterApply;
        var attach = _vm.GetMethod("Attach", Members)!;
        var args = attach.GetParameters().Select(parameter => parameter.Name switch
        {
            "settings" => (object)settings,
            "startup" => startup,
            "pushSubtitleStyle" => subtitlePush,
            "pushScreenshotOptions" => screenshotPush,
            _ => null
        }).ToArray();
        Invoke(vm, attach, args);
        return (vm, settings, startup);
    }
    internal object Row(object vm)
    {
        var row = Invoke(vm, _vm.GetMethod("StartupRow", Members)!, [])!;
        Assert.Equal("Momoka.Shell.ViewModels.SettingToggleRow", row.GetType().FullName);
        Assert.True(row is INotifyPropertyChanged, "actual MVVM-generated row, not a copied toggle");
        return row;
    }
    internal T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;
    internal void Set(object target, string name, object value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
    internal object? Call(object vm, string name, params object?[] args) =>
        Invoke(vm, _vm.GetMethod(name, Members) ?? throw new AssertionException("missing actual VM method " + name), args);
    internal string? StartupReloadFailure(object vm)
    {
        var field = _vm.GetField("_startupReloadReadFailure", Members);
        return field?.GetValue(vm) as string;
    }
    internal void SetStartupReloadFailure(object vm, string? message)
    {
        var field = _vm.GetField("_startupReloadReadFailure", Members);
        field?.SetValue(vm, message); // Missing in the pre-P2 source only; red fixture then cannot retain an owner.
    }
    internal void ResetStartupReloadFailure(object vm)
    {
        var field = _vm.GetField("_startupReloadReadFailure", Members)
            ?? throw new AssertionException("actual VM missing per-Reload startup failure owner");
        field.SetValue(vm, null);
    }
    internal void Notify(object vm, string? title, string? message, string severity)
    {
        var kind = _vm.GetProperty("NoticeSeverity", Members)!.PropertyType;
        Call(vm, "Notify", title, message, Enum.Parse(kind, severity));
    }
    private static object? Invoke(object target, MethodInfo method, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}

internal sealed class StartupMemorySettings : ISettingsService
{
    public AppSettings Settings { get; } = new();
    public int PageSize => 50;
    public bool HasUnsavedChanges { get; set; }
    public event EventHandler? SaveStateChanged { add { } remove { } }
    internal int Saves { get; private set; }
    public bool TrySave() { Saves++; return true; }
    public void Save() => throw new InvalidOperationException("no production setting save");
    public ServerProfile ResolveServer(string url) => throw new InvalidOperationException("no accounts/servers");
    public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new InvalidOperationException("no accounts/servers");
    public void ExportPreferences(string path) => throw new InvalidOperationException("no settings file IO");
    public AppSettings ReadBackup(string path) => throw new InvalidOperationException("no settings file IO");
}

public class StartupContractProxy : DispatchProxy
{
    internal IStartupRegistration Platform { get; set; } = null!;
    internal Action<bool>? AfterApply { get; set; }
    internal List<bool> Requests { get; } = [];
    internal List<bool> Results { get; } = [];
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_Available") return Platform.Available;
        if (targetMethod?.Name == "get_IsEnabled") return Platform.IsEnabled;
        if (targetMethod?.Name == "Apply")
        {
            var value = (bool)args![0]!;
            Requests.Add(value);
            var applied = Platform.Apply(value);
            Results.Add(applied);
            AfterApply?.Invoke(value);
            return applied;
        }
        throw new InvalidOperationException("unexpected startup contract member " + targetMethod?.Name);
    }
}
