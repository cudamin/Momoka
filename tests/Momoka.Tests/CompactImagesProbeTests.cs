using System.Collections;
using System.Security.Cryptography;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CompactImagesProbeTests
{
    internal static void Register()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
        Assert.NotNull(root);
        foreach (var (path, hash) in CompactProbeInputs.Hashes)
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root!.FullName, path)))),
                "拒绝建后改动的订阅图片探针旧产物：" + path);

        Test("订阅图片前件：屏外零/一行均真实实现并滚入目标视口", () => Run(async () =>
        {
            var fixture = new CompactImagesFixture();
            await fixture.Run(0, 1);
            Assert.Equal("0,1", string.Join(',', fixture.Repeater.Created));
            Assert.Equal(1, fixture.Repeater.Element(0).BringCalls);
            Assert.Equal(1, fixture.Repeater.Element(1).BringCalls);
            Assert.True(fixture.Repeater.Element(0).InViewport);
            Assert.True(fixture.Repeater.Element(1).InViewport);
            Assert.Equal(0, fixture.Foreign.Created.Count);
        }));
        Test("订阅图片前件：刷新复用屏外首行仍需滚回，不重建其他集合", () => Run(async () =>
        {
            var fixture = new CompactImagesFixture();
            var retained = fixture.Repeater.GetOrCreateElement(0);
            await fixture.Run(0);
            Assert.True(ReferenceEquals(retained, fixture.Repeater.Element(0)));
            Assert.Equal(1, retained.BringCalls);
            Assert.True(retained.InViewport);
            Assert.Equal(0, fixture.Foreign.Created.Count);
        }));
        Test("订阅图片前件：相同列表名但错误集合不能接管或提前通过", () => Run(async () =>
        {
            var fixture = new CompactImagesFixture();
            fixture.Repeater.ItemsSource = new object[2];
            Assert.True(await Failure(() => fixture.Run(0)) is InvalidOperationException);
            Assert.Equal(0, fixture.Repeater.Created.Count);
            Assert.Equal(0, fixture.Foreign.Created.Count);
        }));
        Test("订阅图片前件：滚入请求尚未落实时等待实际视口", () => Run(async () =>
        {
            var fixture = new CompactImagesFixture { AutoReveal = false };
            var target = fixture.Repeater.GetOrCreateElement(0);
            var running = fixture.Run(0);
            try
            {
                await Task.WhenAny(running, fixture.BringRequested.Task).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(running.IsCompleted);
                Assert.False(target.InViewport);
            }
            finally
            {
                target.Reveal();
                await running;
            }
        }));
        Test("订阅图片前件：目标始终屏外必须原截止失败，不加载图片洗绿", () => Run(async () =>
        {
            var fixture = new CompactImagesFixture { AutoReveal = false };
            var target = fixture.Repeater.GetOrCreateElement(0);
            Assert.True(await Failure(() => fixture.Run(0)) is TimeoutException);
            Assert.Equal(1, target.BringCalls);
            Assert.False(target.InViewport);
        }));
    }

    private static void Run(Func<Task> run) => run().WaitAsync(TimeSpan.FromSeconds(6)).GetAwaiter().GetResult();
    private static async Task<Exception?> Failure(Func<Task> run)
    {
        try { await run(); return null; }
        catch (Exception error) { return error; }
    }
}

// Framework boundaries only: the source-linked probe chooses the current collection and elements.
internal sealed class CompactImagesFixture
{
    internal object[] Rows { get; } = [new(), new()];
    internal CompactProbeViewport Viewport { get; } = new();
    internal CompactProbeSeasons Seasons { get; } = new();
    internal CompactProbeRepeater Repeater { get; }
    internal CompactProbeRepeater Foreign { get; } = new() { Name = "EpisodeStatistics", ItemsSource = new object[2] };
    internal bool AutoReveal = true;
    internal TaskCompletionSource BringRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal CompactImagesFixture()
    {
        Repeater = new() { Name = "EpisodeStatistics", ItemsSource = Rows };
        Viewport.Add(Seasons);
        Seasons.Add(Foreign);
        Seasons.Add(Repeater);
        Repeater.Prepare = element =>
        {
            element.BringRequested = () =>
            {
                BringRequested.TrySetResult();
                if (AutoReveal) element.Reveal();
            };
        };
    }

    internal Task Run(params int[] indices) => ShellNavigationProbe.PrepareCompactImagesForFixture(Seasons, Rows, indices);
}

internal class CompactProbeElement
{
    internal string Name { get; set; } = "";
    internal object? Parent { get; private set; }
    internal List<CompactProbeElement> Children { get; } = [];
    internal object XamlRoot { get; } = new();
    internal double ActualWidth { get; private set; } = 500;
    internal double ActualHeight { get; private set; } = 80;
    internal double Left { get; private set; }
    internal double Top { get; private set; } = -300;
    internal int BringCalls;
    internal Action? BringRequested;
    internal bool InViewport => Top >= 0 && Top + ActualHeight <= 500;
    internal void Add(CompactProbeElement child) { child.Parent = this; Children.Add(child); }
    internal void StartBringIntoView() { BringCalls++; BringRequested?.Invoke(); }
    internal void UpdateLayout() { }
    internal void Reveal() => Top = 20;
    internal CompactProbeTransform TransformToVisual(CompactProbeElement anchor) => new(Left, Top);
}

internal sealed class CompactProbeSeasons : CompactProbeElement;
internal sealed class CompactProbeViewport : CompactProbeElement
{
    internal new double ActualWidth => 600;
    internal new double ActualHeight => 500;
}
internal sealed class CompactProbeRepeater : CompactProbeElement
{
    private readonly Dictionary<int, CompactProbeElement> _elements = [];
    internal IEnumerable? ItemsSource { get; set; }
    internal List<int> Created { get; } = [];
    internal Action<CompactProbeElement>? Prepare;
    internal CompactProbeElement GetOrCreateElement(int index)
    {
        if (ItemsSource is not ICollection rows || index < 0 || index >= rows.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (_elements.TryGetValue(index, out var element)) return element;
        element = new();
        _elements.Add(index, element);
        Created.Add(index);
        Add(element);
        Prepare?.Invoke(element);
        return element;
    }
    internal CompactProbeElement Element(int index) => _elements[index];
}

internal readonly record struct CompactProbeTransform(double X, double Y)
{
    internal CompactProbePoint TransformPoint(CompactProbePoint point) => new(point.X + X, point.Y + Y);
}
internal readonly record struct CompactProbePoint(double X, double Y);
internal static class CompactProbeVisualTree
{
    internal static int GetChildrenCount(CompactProbeElement root) => root.Children.Count;
    internal static CompactProbeElement GetChild(CompactProbeElement root, int index) => root.Children[index];
    internal static CompactProbeElement? GetParent(CompactProbeElement root) => root.Parent as CompactProbeElement;
}
