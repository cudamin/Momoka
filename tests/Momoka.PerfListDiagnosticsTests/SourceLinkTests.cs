using System.Security.Cryptography;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SourceLinkTests
{
    internal static void Register()
    {
        Test("PERF 源码关联：当轮抽取的LibraryViewModel哈希等于当前输入", () =>
        {
            var root = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
            var source = Path.Combine(root, "src", "Momoka.Shell", "ViewModels", "LibraryViewModel.cs");
            Assert.True(File.Exists(source), "从本独立项目的Release输出定位当前工作树，不接受未知来源DLL");
            Assert.Equal(LibraryViewModel.FixtureSourceHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
                "构建后源码变化必须重编译，不能使用陈旧抽取产物");
        });
        Test("PERF 运行接线：日志淘汰空态从正式Attach/Written入口，不反射Append或伪造live", () =>
        {
            var root = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
            var source = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "ShellNavigationProbe.Integration.cs"));
            var signature = "    private async Task DiagnosticsEmptyAsync()";
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0 && source.IndexOf(signature, start + signature.Length, StringComparison.Ordinal) < 0);
            var method = source[start..];
            Assert.Contains("vm.Attach(", method);
            Assert.Equal(2, method.Split("sink.Write(", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("Call(vm, \"Append\"", method);
            Assert.DoesNotContain("Field(vm, \"_live\"", method);
            Assert.Contains("!playback.IsPlaying", method);
        });
    }
}
