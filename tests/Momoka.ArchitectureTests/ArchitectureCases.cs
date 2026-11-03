using Mono.Cecil;
using NetArchTest.Rules;

namespace Momoka.Tests;

/// <summary>
/// 把 CLAUDE.md 里「Core 不依赖 UI 框架」「Shell 的服务层不回头摸视图」这些句子变成一扫就红的东西。
///
/// 每条规则都先在这棵树上跑过：当前全部通过，不是照搬别人的架构模板。规则只收真实存在、并且
/// 有明确反例方向的东西——「ViewModels 不许 using Views」这类会在这里立刻红掉十几处，说明它是
/// 一次重构的目标，不是一条今天就能守住的线，所以没有写进来。
///
/// 失败信息必须能直接干活：类型全名 + NetArchTest 给出的依赖解释，而不是只说「有条规则没过」。
/// </summary>
internal static class ArchitectureCases
{
    public static void Register(string corePath, string shellPath)
    {
        var searchDirectories = new[]
        {
            Path.GetDirectoryName(corePath)!,
            Path.GetDirectoryName(shellPath)!,
        };
        var core = Types.FromFile(corePath, searchDirectories);
        var shell = Types.FromFile(shellPath, searchDirectories);

        TestHarness.Test("架构：Core 不依赖 UI 技术栈（WinUI / WinRT / WinForms / System.Drawing）", () =>
            Pass("Core 依赖了 UI 技术栈",
                core.ShouldNot().HaveDependencyOnAny(
                    "Microsoft.UI", "Windows.UI", "Microsoft.Windows", "System.Windows", "System.Drawing")));

        TestHarness.Test("架构：Core 不依赖 Shell", () =>
            Pass("Core 依赖了 Shell",
                core.ShouldNot().HaveDependencyOnAny("Momoka.Shell")));

        TestHarness.Test("架构：Shell 不依赖任何测试程序集", () =>
            Pass("Shell 依赖了测试程序集",
                shell.ShouldNot().HaveDependencyOnAny("Momoka.Tests")));

        TestHarness.Test("架构：Shell.Services 不依赖 Views", () =>
            Pass("服务层依赖了视图",
                shell.That().ResideInNamespaceMatching("^Momoka\\.Shell\\.Services")
                    .ShouldNot().HaveDependencyOnAny("Momoka.Shell.Views"),
                requireSelection: true));

        TestHarness.Test("架构：Shell.Media 不依赖 Views", () =>
            Pass("媒体层依赖了视图",
                shell.That().ResideInNamespaceMatching("^Momoka\\.Shell\\.Media")
                    .ShouldNot().HaveDependencyOnAny("Momoka.Shell.Views"),
                requireSelection: true));

        TestHarness.Test("架构：Shell.Windowing 不依赖 Views", () =>
            Pass("窗口层依赖了视图",
                shell.That().ResideInNamespaceMatching("^Momoka\\.Shell\\.Windowing")
                    .ShouldNot().HaveDependencyOnAny("Momoka.Shell.Views"),
                requireSelection: true));

        TestHarness.Test("架构：Core 的程序集引用只有框架（没有第三方运行时包）", () =>
        {
            // NetArchTest 看的是类型依赖，这一条看的是程序集引用表：Core 的轻量是「不引 NuGet 运行时」
            // （见 Directory.Build.props 注释与 CLAUDE.md），只有引用表能证明它没被悄悄拖进一个包。
            using var assembly = AssemblyDefinition.ReadAssembly(corePath);
            var allowed = new[] { "System.", "Microsoft.Win32.Primitives", "netstandard", "mscorlib" };
            var offenders = assembly.MainModule.AssemblyReferences
                .Select(reference => reference.Name)
                .Where(name => !allowed.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.True(offenders.Length == 0,
                "Core 出现了框架外的程序集引用：" + string.Join("、", offenders));
        });
    }

    private static void Pass(string what, ConditionList conditions, bool requireSelection = false)
    {
        var result = conditions.GetResult();
        var selected = result.SelectedTypesForTesting?.Count() ?? 0;
        // 命名空间拼错时 NetArchTest 会「成功地」检查 0 个类型；这不是通过，是规则失效。
        Assert.True(!requireSelection || selected > 0, $"{what}：规则选中的类型数为 0，先检查命名空间条件。");
        if (result.IsSuccessful) return;

        var failing = (result.FailingTypes ?? [])
            .Select(type => "  · " + type.FullName + (type.Explanation is { } explanation ? " —— " + explanation : ""))
            .ToArray();
        var detail = string.Join("\n", failing.Take(20));
        if (failing.Length > 20) detail += $"\n  ……另有 {failing.Length - 20} 个";
        throw new AssertionException($"{what}（共 {failing.Length} 个类型）：\n{detail}");
    }
}
