using System.Security.Cryptography;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SourceLinkTests
{
    internal static void Register() => Test("PERF 源码关联：当轮抽取的LibraryViewModel哈希等于当前输入", () =>
    {
        var root = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var source = Path.Combine(root, "src", "Momoka.Shell", "ViewModels", "LibraryViewModel.cs");
        Assert.True(File.Exists(source), "从本独立项目的Release输出定位当前工作树，不接受未知来源DLL");
        Assert.Equal(LibraryViewModel.FixtureSourceHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
            "构建后源码变化必须重编译，不能使用陈旧抽取产物");
    });
}
