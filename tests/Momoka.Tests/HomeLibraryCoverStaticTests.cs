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
            Assert.False(File.Exists(PathOf("src", "Momoka.Shell", "Views", "LibraryCoverMotion.cs")), "静态预设不保留已退役的动画生命周期类");
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
        Test("主页媒体库静态封面：九宫格图片取齐后一次发布", () =>
        {
            var source = Source("CardItem.HomeLibraryCover.cs");
            Assert.Contains("var arrived = new List<BitmapImage>();", source);
            Assert.Contains("先攒齐再一次性通知", source);
            var guard = source.IndexOf("if (!Current() || !_wanted || arrived.Count == 0) return;", StringComparison.Ordinal);
            var publish = source.IndexOf("Raise(nameof(HomeLibraryCoverImages));", Math.Max(guard, 0), StringComparison.Ordinal);
            var finish = source.IndexOf("finally", Math.Max(guard, 0), StringComparison.Ordinal);
            Assert.True(guard >= 0 && publish > guard && finish > publish, "整批图片落定后才通知一次");
            Assert.DoesNotContain("_homeLibraryCoverImages.Add(bitmap);\n                    Raise(nameof(HomeLibraryCoverImages));", source);
        });
        Test("主页媒体库静态封面：订阅入口改用各订阅海报并等库排落屏", () =>
        {
            var covers = ViewModelSource("HomeViewModel.LibraryCovers.cs");
            Assert.Contains("ApplySubscriptionLibraryCover(subscription, preset)", covers);
            Assert.Contains("ApplyHomeLibraryCoverImages(preset, tiles)", covers);
            Assert.Contains("!_settledLibraryRows.Contains(rowKey)", covers);
            var subscriptions = ViewModelSource("HomeViewModel.MoviePilotLibrary.cs");
            Assert.Contains("MoviePilotLibraryArtwork.WallPosters", subscriptions);
            Assert.Contains("MoviePilotLibraryArtwork.CoverPosters", subscriptions);
            Assert.Contains("MoviePilotLibraryEntry", subscriptions);
            Assert.Contains("_settledLibraryRows.Add(load.Row.Key)", Read("src/Momoka.Shell/ViewModels/HomeViewModel.cs"));
            Assert.Contains("if (_homeLibraryCoverFromOwner) return;", Source("CardItem.cs"));
            Assert.Contains("LoadTilesAsync", Read("src/Momoka.Shell/Media/MoviePilotLibraryCoverLoader.cs"));
        });
    }

    private static string Source(string file) => Read("src/Momoka.Shell/Views", file);

    private static string ViewModelSource(string file) => Read("src/Momoka.Shell/ViewModels", file);

    private static string Read(params string[] parts) => File.ReadAllText(PathOf(parts));

    private static string PathOf(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return Path.Combine([directory.FullName, .. parts]);
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
