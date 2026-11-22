using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

// Source contracts preserve the requested static presets; corner pixels still require window acceptance.
internal static class HomeLibraryCoverStaticTests
{
    internal static void Register()
    {
        Test("主页媒体库静态封面：保留两种构图及无图、高对比和纯文字回退", () =>
        {
            var source = Source("PosterCard.LibraryCover.cs");
            Assert.Contains("highContrast || preset == HomeLibraryCoverPresets.Gradient || images.Count == 0", source);
            var pictures = Block(source, "if (!textOnly)");
            foreach (var preset in new[] { "Stack", "Wall" })
            {
                var marker = "case HomeLibraryCoverPresets." + preset + ":";
                Assert.Contains(marker, pictures);
            }
            Assert.DoesNotContain("case HomeLibraryCoverPresets.Gradient:", pictures);
            Assert.Contains("RenderTransform = new RotateTransform { Angle = rotation }", source);
            Assert.Contains("return new Viewbox { Stretch = Stretch.Fill, Child = canvas }", source);
        });
        Test("主页媒体库静态封面：叠卡使用三张海报、直立前卡和静态柔影", () =>
        {
            var source = Source("PosterCard.LibraryCover.cs");
            var left = source.IndexOf("AddStackCard(StackPicture(1), 108, 22, 56, 82, -14, 0.52)", StringComparison.Ordinal);
            var right = source.IndexOf("AddStackCard(StackPicture(2), 145, 21, 56, 82, 14, 0.68)", StringComparison.Ordinal);
            var front = source.IndexOf("AddStackCard(StackPicture(0), 126, 13, 62, 92)", StringComparison.Ordinal);
            Assert.True(left >= 0 && left < right && right < front, "后卡先展开，前卡最后直立覆盖");
            Assert.Contains("BitmapImage StackPicture(int position) => images[Math.Min(position, images.Count - 1)]", source);
            var card = Block(source, "void AddStackCard(");
            Assert.Contains("RenderTransform = new RotateTransform { Angle = rotation }", card);
            Assert.Contains("Windows.UI.Color.FromArgb(22, 0, 0, 0)", card);
            Assert.Contains("Windows.UI.Color.FromArgb(42, 0, 0, 0)", card);
            Assert.Contains("canvas.Background = preset == HomeLibraryCoverPresets.Stack && !highContrast", source);
            Assert.Contains("? StackGradient(start, end, palette.Accent) : Gradient(start, end)", source);
        });
        Test("主页媒体库静态封面：不创建循环动画或保留动画生命周期订阅", () =>
        {
            var source = Source("PosterCard.LibraryCover.cs");
            foreach (var animation in new[] { "Storyboard", "DoubleAnimation", "DispatcherTimer", "LibraryCoverMotion", "PreferenceChanged +=", "AnimationsEnabledChanged +=" })
                Assert.DoesNotContain(animation, source);
            Assert.False(File.Exists(ViewPath("LibraryCoverMotion.cs")), "静态预设不保留已退役的动画生命周期类");
        });
        Test("主页媒体库静态封面：保留去白边修复，关闭覆盖恢复描边", () =>
        {
            var source = Source("PosterCard.LibraryCover.cs");
            Assert.Contains("if (preset is null) Art.ClearValue(Border.BorderThicknessProperty)", source);
            Assert.Contains("else Art.BorderThickness = new Thickness(0)", source);
            Assert.True(source.IndexOf("Art.ClearValue", StringComparison.Ordinal)
                < source.IndexOf("if (preset is null) return null", StringComparison.Ordinal));
            Assert.Contains("Clip = new RectangleGeometry { Rect = new Rect(0, 0, 208, 117) }", source);
        });
    }

    private static string Source(string file) => File.ReadAllText(ViewPath(file));

    private static string ViewPath(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return Path.Combine(directory.FullName, "src", "Momoka.Shell", "Views", file);
        throw new InvalidOperationException("本树源码缺失");
    }

    private static string Block(string source, string marker)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, marker);
        var start = source.IndexOf('{', markerIndex);
        Assert.True(start >= 0, marker);
        var depth = 0;
        for (var index = start; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[(start + 1)..index];
        }
        throw new AssertionException("缺少完整代码块：" + marker);
    }
}
