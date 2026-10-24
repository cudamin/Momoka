using System.Reflection;

namespace Momoka.Tests;

/// <summary>Assert actual Attach parameter order/defaults without permitting full-page/native Reload.</summary>
internal static class BoundaryCompatibilityTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] RequiredNames =
    [
        "settings", "shaders", "fonts", "paths", "launcher", "startup", "audioDevices", "pushSubtitleStyle",
        "pushScreenshotDirectory", "pushScreenshotOptions", "moviePilot", "moviePilotCredentials", "updates", "capabilities"
    ];

    internal static void Register()
    {
        TestHarness.Test("Attach真实前14参数顺序保留，全部可选尾参数从实际默认值补足", () =>
        {
            var method = ShellRuntime.Current.Type("SettingsViewModel").GetMethod("Attach", Members)!;
            var parameters = method.GetParameters();
            Assert.True(parameters.Take(RequiredNames.Length).Select(parameter => parameter.Name).SequenceEqual(RequiredNames));
            var supplied = RequiredNames.Select(_ => (object?)new object()).ToArray();
            var completed = ShellRuntime.CompleteOptionalArguments(method, supplied);
            Assert.Equal(parameters.Length, completed.Length, "不能硬编码16或17参数");
            for (var index = 0; index < supplied.Length; index++) Assert.True(ReferenceEquals(supplied[index], completed[index]));
            foreach (var parameter in parameters.Skip(supplied.Length))
            {
                Assert.True(parameter.HasDefaultValue, "新增必需尾参数必须失败，不可静默null");
                Assert.Equal(parameter.DefaultValue, completed[parameter.Position]);
            }
        });
        TestHarness.Test("Attach真实images及dandan参数不被可选尾部补齐覆盖", () =>
        {
            var method = ShellRuntime.Current.Type("SettingsViewModel").GetMethod("Attach", Members)!;
            var parameters = method.GetParameters();
            Assert.Equal("images", parameters[RequiredNames.Length].Name);
            Assert.Equal("dandanCredentials", parameters[RequiredNames.Length + 1].Name);
            var supplied = parameters.Take(RequiredNames.Length + 2).Select(_ => (object?)new object()).ToArray();
            var completed = ShellRuntime.CompleteOptionalArguments(method, supplied);
            for (var index = 0; index < supplied.Length; index++) Assert.True(ReferenceEquals(supplied[index], completed[index]));
            Assert.Equal(parameters.Length, completed.Length);
        });
        TestHarness.Test("Attach缺必需参数与多余参数均保持fail-closed", () =>
        {
            var method = ShellRuntime.Current.Type("SettingsViewModel").GetMethod("Attach", Members)!;
            Assert.Throws<AssertionException>(() => ShellRuntime.CompleteOptionalArguments(method, new object?[RequiredNames.Length - 1]));
            Assert.Throws<AssertionException>(() => ShellRuntime.CompleteOptionalArguments(method, new object?[method.GetParameters().Length + 1]));
        });
        TestHarness.Test("Attach适配不把未知可选参数一律变null，显式null仍保留", () =>
        {
            var method = typeof(BoundaryCompatibilityTests).GetMethod(nameof(DefaultProbe), BindingFlags.Static | BindingFlags.NonPublic)!;
            var required = new object();
            var completed = ShellRuntime.CompleteOptionalArguments(method, [required]);
            Assert.True(ReferenceEquals(required, completed[0]));
            Assert.Equal("kept-default", completed[1]);
            Assert.Equal(7, completed[2]);
            var explicitNull = ShellRuntime.CompleteOptionalArguments(method, [required, null]);
            Assert.Null(explicitNull[1]);
            Assert.Equal(7, explicitNull[2]);
        });
        TestHarness.Test("Attach页面wrapper接受真实可选依赖但仍拒绝整页UI重建", () =>
        {
            using var fixture = new LifetimeFixture();
            var page = fixture.NewCachedPage("主页");
            var error = Assert.Catch<InvalidOperationException>(() => page.ViewModel.Attach(
                fixture.Settings, null!, null!, fixture.Paths, null!, null!, null!,
                () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
                null!, null!, null!, fixture.Capabilities));
            Assert.Contains("不允许完整 UI 重建", error.Message);
            var extra = Enumerable.Repeat<object?>(null, ShellRuntime.Current.Type("SettingsViewModel")
                .GetMethod("Attach", Members)!.GetParameters().Length - RequiredNames.Length + 1).ToArray();
            Assert.Throws<AssertionException>(() => page.ViewModel.Attach(
                fixture.Settings, null!, null!, fixture.Paths, null!, null!, null!,
                () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
                null!, null!, null!, fixture.Capabilities, extra));
        });
    }

    private static void DefaultProbe(object required, string? optional = "kept-default", int count = 7)
    {
        _ = required;
        _ = optional;
        _ = count;
    }
}
