using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

// These are wiring contracts. Native animation frames, clipping and timing require window acceptance.
internal static class HomeLibraryCoverMotionTests
{
    internal static void Register()
    {
        Test("主页媒体库封面动画契约：前三种预设接入动画，静态回退不启动", () =>
        {
            var source = Source("PosterCard.LibraryCover.cs");
            Assert.Contains("highContrast || preset == HomeLibraryCoverPresets.Gradient || images.Count == 0", source);
            var animated = Block(source, "if (!textOnly)");
            var imageBuilder = Block(source, "void AddImage(");
            foreach (var preset in new[] { "Stack", "Diagonal", "Wall" })
            {
                var marker = "case HomeLibraryCoverPresets." + preset + ":";
                Assert.Contains(marker, animated);
                var start = imageBuilder.IndexOf(marker, StringComparison.Ordinal);
                Assert.True(start >= 0, marker);
                var end = imageBuilder.IndexOf("break;", start, StringComparison.Ordinal);
                Assert.True(end > start, marker);
                var branch = imageBuilder[start..end];
                Assert.Contains("Animate(", branch);
            }
            Assert.DoesNotContain("case HomeLibraryCoverPresets.Gradient:", animated);
            Assert.Contains("LibraryCoverMotion", source);
            Assert.Contains("if (motion.Children.Count > 0)", source);
        });
        Test("主页媒体库封面动画契约：循环往返只改变绘制变换，不用逐帧布局计时器", () =>
        {
            var source = Source("PosterCard.LibraryCover.cs");
            var animation = Block(source, "void Animate(");
            Assert.Contains("AutoReverse = true", animation);
            Assert.Contains("RepeatBehavior = RepeatBehavior.Forever", animation);
            Assert.Contains("Storyboard.SetTarget(animation, target)", animation);
            Assert.Contains("Storyboard.SetTargetProperty(animation, property)", animation);
            Assert.Contains("CompositeTransform", source);
            Assert.DoesNotContain("DispatcherTimer", source);
            Assert.DoesNotContain("EnableDependentAnimation = true", source);
        });
        Test("主页媒体库封面动画契约：卸载退订，离开视口或宿主隐藏立即停止，返回可恢复", () =>
        {
            var source = Source("LibraryCoverMotion.cs");
            var loaded = Block(source, "private void OnLoaded(");
            var unloaded = Block(source, "private void OnUnloaded(");
            Assert.Contains("cover.Loaded += OnLoaded", source);
            Assert.Contains("cover.Unloaded += OnUnloaded", source);
            Assert.Contains("cover.EffectiveViewportChanged += OnViewportChanged", source);
            foreach (var subscription in new[] { "_root.Changed", "HomeMotion.PreferenceChanged", "_settings.AnimationsEnabledChanged" })
            {
                Assert.Contains(subscription + " +=", loaded);
                Assert.Contains(subscription + " -=", unloaded);
            }
            Assert.Contains("_loaded = false", unloaded);
            Assert.Contains("_inViewport = false", unloaded);
            Assert.Contains("Refresh();", unloaded);
            var refresh = Block(source, "private void Refresh(");
            Assert.Contains("_loaded && _inViewport", refresh);
            Assert.Contains("IsHostVisible: true", refresh);
            Assert.Contains("HomeMotion.AnimationsEnabled", refresh);
            Assert.Contains("if (run == _running) return", refresh);
            Assert.Contains("if (run) _board.Begin()", refresh);
            Assert.Contains("else _board.Stop()", refresh);
            Assert.Contains("DispatcherQueue.TryEnqueue(Refresh)", source);
        });
        Test("主页媒体库封面动画契约：覆盖时清描边，关闭恢复样式，斜切图超出裁切边界", () =>
        {
            var source = Source("PosterCard.LibraryCover.cs");
            Assert.Contains("if (preset is null) Art.ClearValue(Border.BorderThicknessProperty)", source);
            Assert.Contains("else Art.BorderThickness = new Thickness(0)", source);
            Assert.True(source.IndexOf("Art.ClearValue", StringComparison.Ordinal)
                < source.IndexOf("if (preset is null) return null", StringComparison.Ordinal));
            Assert.Contains("AddImage(images[0], -2, -2, 212, 121)", source);
            Assert.Contains("Clip = new RectangleGeometry { Rect = new Rect(0, 0, 208, 117) }", source);
        });
    }

    private static string Source(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "Momoka.Shell", "Views", file));
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
