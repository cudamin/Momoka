using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 悬停放大（2026-11-13）：卡片整卡抬一档、预览面板换弹簧撑开。两处都参考 WinUI Gallery 的
/// <c>Samples/XamlCompInterop</c>（固定提交 6de91fe）。这里钉住参数与实现路径，防止被悄悄改回
/// 「整卡不放大」或退回一段 keyframe；实际手感、遮挡和减弱动效下的落定仍靠运行截图与探针。
/// </summary>
internal static class HoverMotionTests
{
    internal static void Register()
    {
        Test("悬停放大：卡片整卡抬到 1.03，抬起用 BackEase 过冲", () =>
        {
            var source = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            Assert.Contains("private const double HoverScale = 1.03;", source);
            Assert.Contains("var lifted = _hovered || _previewOpen;", source);
            Assert.Contains("new BackEase", source);
            Assert.Contains("XamlCompInterop", source);
            Assert.Contains("6de91fe", source);
        });

        Test("悬停放大：预览面板用弹簧驱动 Progress，不再用关键帧", () =>
        {
            var source = Read("src/Momoka.Shell/Views/CardPreview.xaml.cs");
            Assert.Contains("CreateSpringScalarAnimation()", source);
            Assert.Contains("spring.Target = \"Progress\";", source);
            Assert.Contains("spring.DampingRatio", source);
            Assert.Contains("spring.Period", source);
            Assert.Contains("6de91fe", source);
            Assert.DoesNotContain("CreateScalarKeyFrameAnimation", source);
        });
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory, "找不到方案根目录");
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
