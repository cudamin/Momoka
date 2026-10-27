using System.Security.Cryptography;
using System.Text;

namespace Momoka.Tests;

internal sealed record SourceStamp(string Name, string Path, string Sha256, SourceSlice[] Slices);
internal sealed record SourceSlice(string Name, string Start, string End, int Offset, int Length, string Sha256);

internal static class SourceEvidenceTests
{
    internal static void Register()
    {
        foreach (var stamp in FullscreenSourceEvidence.Sources)
        {
            TestHarness.Test("编译输入与运行源码匹配：" + stamp.Name, () => Verify(stamp));
            foreach (var slice in stamp.Slices)
                TestHarness.Test("原样提取与唯一双边界：" + stamp.Name + "." + slice.Name,
                    () => VerifySlice(stamp, slice));
        }
    }

    private static string SourceText(SourceStamp stamp)
    {
        var utf8 = new UTF8Encoding(false, true);
        var text = utf8.GetString(File.ReadAllBytes(stamp.Path));
        return text.StartsWith('\uFEFF') ? text[1..] : text;
    }

    private static void Verify(SourceStamp stamp)
    {
        Assert.True(File.Exists(stamp.Path), "构建所用生产源不存在；必须重新编译，不能使用旧输出");
        Assert.Equal(stamp.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stamp.Path))), stamp.Path);
        Assert.True(Path.IsPathFullyQualified(stamp.Path), "证据保留绝对路径");
        Assert.True(stamp.Slices.Length > 0);
    }

    private static void VerifySlice(SourceStamp stamp, SourceSlice slice)
    {
        var source = SourceText(stamp);
        var start = Unique(source, slice.Start);
        var end = Unique(source, slice.End);
        Assert.True(end > start, slice.Name + "结束必须位于开始之后");
        Assert.Equal(slice.Offset, start);
        Assert.Equal(slice.Length, end - start);
        var extracted = source[start..end];
        Assert.Equal(slice.Sha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(extracted))),
            slice.Name + "的文本必须仍是构建时原样子串（不替换算法或行尾）");
    }

    private static int Unique(string source, string marker)
    {
        var found = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(found >= 0, "缺少生产边界：" + marker);
        Assert.Equal(-1, source.IndexOf(marker, found + marker.Length, StringComparison.Ordinal),
            "整个生产文件中边界必须唯一：" + marker);
        return found;
    }
}
