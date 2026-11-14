using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("未导航时图片立即放行", () => Assert.True(Gate().IsCompleted));
        Test("原生回调失去异步上下文时仍取得所属Frame门控", () =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            var expected = Gate();
            Task<Task> native;
            using (ExecutionContext.SuppressFlow()) native = Task.Run<Task>(() => OwnerGate(fixture.Page));
            while (!native.IsCompleted) Thread.Yield();
            if (native.Exception is { } error) throw error.InnerException ?? error;
            Assert.True(ReferenceEquals(expected, native.Result));
            Assert.False(native.Result.IsCompleted);
        });
        Test("详情尚无视觉父节点时使用Page.Frame取得门控", () =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            var page = new Page { Frame = fixture.Frame };
            Assert.True(ReferenceEquals(Gate(), OwnerGate(page)));
        });
        Test("同UI线程另一Frame和非浏览窗口不会串入门控", () =>
        {
            using var first = new Fixture();
            using var second = new Fixture();
            first.Begin();
            var firstGate = Gate();
            second.Begin();
            var secondGate = Gate();
            Assert.True(ReferenceEquals(firstGate, OwnerGate(first.Page)));
            Assert.True(ReferenceEquals(secondGate, OwnerGate(second.Page)));
            Assert.True(OwnerGate(new FrameworkElement()).IsCompletedSuccessfully);
            first.Stop();
            Assert.True(firstGate.IsCompletedSuccessfully);
            Assert.False(secondGate.IsCompleted);
            Assert.True(ReferenceEquals(secondGate, OwnerGate(second.Page)));
        });
        Test("owner捕获的旧Task不会被后续Frame门控替换", () =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            var captured = OwnerGate(fixture.Page);
            fixture.Begin();
            var next = OwnerGate(fixture.Page);
            Assert.True(captured.IsCompletedSuccessfully);
            Assert.False(next.IsCompleted);
            Assert.False(ReferenceEquals(captured, next));
        });
        Test("未进入原生UI树的调用不等待首帧", () =>
        {
            using var fixture = new Fixture();
            fixture.Frame.Unload();
            fixture.Begin();
            Assert.True(Gate().IsCompletedSuccessfully);
        });
        Test("新页先加载并呈现一帧后才在低优先级放行图片", () =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            var gate = Gate();
            Assert.False(gate.IsCompleted);
            fixture.Attach();
            Assert.False(gate.IsCompleted);
            fixture.Page.Load();
            Assert.True(fixture.Frame.Opacity > 0 && fixture.Frame.Opacity < 1);
            Assert.False(gate.IsCompleted);
            CompositionTarget.Render();
            Assert.False(gate.IsCompleted);
            Assert.Equal(DispatcherQueuePriority.Low, fixture.Frame.DispatcherQueue.LastPriority!.Value);
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(gate.IsCompletedSuccessfully);
        });
        Test("页面动效没有退出等待且只动透明度", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            fixture.Attach();
            var board = Storyboard.Created.Last();
            Assert.True(board.IsRunning);
            Assert.Equal(1, board.Children.Count);
            var fade = board.Children.Single();
            Assert.Equal("Opacity", fade.Property);
            Assert.True(ReferenceEquals(fixture.Frame, fade.Target));
            Assert.True(fade.From is > 0 and < 1);
            Assert.Equal(1d, fade.To!.Value);
            Assert.True(fade.BeginTime is null || fade.BeginTime == TimeSpan.Zero);
            Assert.True(fade.Duration.TimeSpan.TotalMilliseconds <= 200);
        });
        Test("快速换页释放旧图片等待且迟到Loaded不能启动旧页", () =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            var oldGate = Gate();
            fixture.Attach();
            var late = fixture.Page.LateLoad();
            fixture.Begin();
            var gate = Gate();
            Assert.True(oldGate.IsCompletedSuccessfully);
            Assert.False(gate.IsCompleted);
            var count = Storyboard.Created.Count;
            late();
            Assert.Equal(count, Storyboard.Created.Count);
            Assert.False(gate.IsCompleted);
            Assert.Equal(0, fixture.Page.LoadedHandlers);
        });
        Test("旧渲染和排队放行不能完成下一页的等待", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            fixture.Attach();
            var late = CompositionTarget.LateRender();
            CompositionTarget.Render();
            fixture.Begin();
            var gate = Gate();
            late();
            fixture.Frame.DispatcherQueue.Drain();
            Assert.False(gate.IsCompleted);
        });
        Test("旧动效完成不能把下一次淡入提前落定", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            fixture.Attach();
            var old = Storyboard.Created.Last();
            var late = old.LateCompletion();
            fixture.Begin();
            fixture.Attach();
            var next = Storyboard.Created.Last();
            late();
            Assert.True(next.IsRunning);
            Assert.True(fixture.Frame.Opacity < 1);
            Assert.Equal(0, old.Handlers);
            next.Complete();
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.False(next.IsRunning);
        });
        Test("离页和身份释放取消动画并放行全部等待", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            var gate = Gate();
            fixture.Attach();
            fixture.Page.Unload();
            Assert.True(gate.IsCompletedSuccessfully);
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.Equal(0, CompositionTarget.HandlerCount);
            Assert.Equal(0, fixture.Page.UnloadedHandlers);
            fixture.Begin();
            gate = Gate();
            fixture.Stop();
            Assert.True(gate.IsCompletedSuccessfully);
            Assert.True(Gate().IsCompletedSuccessfully);
        });
        Test("关闭应用动效即时落定但图片继续等待首帧", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            var gate = Gate();
            fixture.Attach();
            HomeMotion.ApplyPreference(false);
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.False(Storyboard.Created.Last().IsRunning);
            Assert.False(gate.IsCompleted);
            CompositionTarget.Render();
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(gate.IsCompletedSuccessfully);
        });
        Test("系统关闭动效即时落定且不阻塞图片", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            var gate = Gate();
            fixture.Attach();
            UISettings.Created.Last().Change(false);
            fixture.Frame.DispatcherQueue.Drain();
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.False(gate.IsCompleted);
            CompositionTarget.Render();
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(gate.IsCompletedSuccessfully);
        });
        Test("动画关闭时首帧门控仍能放行", () =>
        {
            using var fixture = new Fixture(loaded: true);
            HomeMotion.ApplyPreference(false);
            var count = Storyboard.Created.Count;
            fixture.Begin();
            var gate = Gate();
            fixture.Attach();
            Assert.Equal(count, Storyboard.Created.Count);
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.False(gate.IsCompleted);
            CompositionTarget.Render();
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(gate.IsCompletedSuccessfully);
        });
        Test("渲染后的队列拒绝不会无限等待", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            var gate = Gate();
            fixture.Attach();
            fixture.Frame.DispatcherQueue.Reject = true;
            CompositionTarget.Render();
            Assert.True(gate.IsCompletedSuccessfully);
        });
        Test("原生动画失败仍显示内容并放行图片", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            var gate = Gate();
            Storyboard.FailNextBegin = true;
            fixture.Attach();
            Assert.Equal(1d, fixture.Frame.Opacity);
            CompositionTarget.Render();
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(gate.IsCompletedSuccessfully);
        });
        Test("后台延续持有自己的访问等待而不串入后续导航", () =>
        {
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Begin();
                var old = Gate();
                var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var background = Task.Run(async () =>
                {
                    await resume.Task.ConfigureAwait(false);
                    return Gate();
                });
                fixture.Begin();
                var next = Gate();
                resume.SetResult();
                Assert.True(ReferenceEquals(old, await background));
                Assert.True(old.IsCompletedSuccessfully);
                Assert.False(next.IsCompleted);
            });
        });
        Test("空导航目标立即取消等待", () =>
        {
            using var fixture = new Fixture();
            fixture.Begin();
            var gate = Gate();
            fixture.Frame.Content = null;
            fixture.Attach();
            Assert.True(gate.IsCompletedSuccessfully);
            Assert.Equal(1d, fixture.Frame.Opacity);
        });
        Test("关联动画成功抑制整页淡入但首帧图片门控保持", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin(deferMotion: true);
            var gate = Gate();
            fixture.Attach();
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.False(Storyboard.Created.Any(board => board.IsRunning));
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: true);
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.False(gate.IsCompleted);
            CompositionTarget.Render();
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(gate.IsCompletedSuccessfully);
        });
        Test("关联动画失败恢复原淡入，图片仍等所属Frame首帧", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin(deferMotion: true);
            var gate = Gate();
            fixture.Attach();
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: false);
            Assert.Equal(0.8d, fixture.Frame.Opacity);
            Assert.True(Storyboard.Created.Any(board => board.IsRunning));
            Assert.False(gate.IsCompleted);
            CompositionTarget.Render();
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(gate.IsCompletedSuccessfully);
        });
        Test("关联动画先成功再Loaded不重复整页动效", () =>
        {
            using var fixture = new Fixture();
            fixture.Begin(deferMotion: true);
            fixture.Attach();
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: true);
            fixture.Page.Load();
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.False(Storyboard.Created.Any(board => board.IsRunning));
        });
        foreach (var add in new[] { UISettings.EventFailure.Before, UISettings.EventFailure.After })
            foreach (var remove in new[] { UISettings.EventFailure.Before, UISettings.EventFailure.After })
                Test($"首帧系统事件订阅{add}退订{remove}失败不挂住图片门控", () =>
                {
                    using var fixture = new Fixture(loaded: true);
                    fixture.Begin();
                    var gate = Gate();
                    UISettings.AddFailure = add;
                    UISettings.RemoveFailure = remove;
                    fixture.Attach();
                    Assert.Equal(1d, fixture.Frame.Opacity);
                    Assert.False(gate.IsCompleted);
                    var failed = UISettings.Created.Last();
                    var stale = failed.CaptureChange();
                    fixture.Stop();
                    Assert.True(gate.IsCompletedSuccessfully);
                    UISettings.AddFailure = UISettings.EventFailure.None;
                    UISettings.RemoveFailure = UISettings.EventFailure.None;
                    fixture.Begin();
                    fixture.Attach();
                    var latest = Storyboard.Created.Last();
                    UISettings.Enabled = false;
                    stale();
                    fixture.Frame.DispatcherQueue.Drain();
                    Assert.True(latest.IsRunning, "旧失败观察不能停止新动画");
                    UISettings.Enabled = true;
                    failed.ReleaseFaultEndpoint();
                });
        foreach (var presentation in new[] { BrowseMotion.Native, BrowseMotion.None })
        {
            Test("独立呈现策略仍释放首帧图片，不创建自定义淡入/" + presentation, () =>
            {
                using var fixture = new Fixture(loaded: true);
                var before = Storyboard.Created.Count;
                fixture.Begin(presentation);
                var gate = Gate();
                fixture.Attach();
                Assert.False(gate.IsCompleted);
                Assert.Equal(before, Storyboard.Created.Count);
                BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: false);
                Assert.Equal(before, Storyboard.Created.Count);
                CompositionTarget.Render();
                fixture.Frame.DispatcherQueue.Drain();
                Assert.True(gate.IsCompletedSuccessfully);
                Assert.Equal(1d, fixture.Frame.Opacity);
            });
        }
        Test("关联启动后的迟到失败与完成不再补播整页", () =>
        {
            using var fixture = new Fixture(loaded: true);
            var before = Storyboard.Created.Count;
            fixture.Begin(deferMotion: true);
            fixture.Attach();
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: true);
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: false);
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: true);
            Assert.Equal(before, Storyboard.Created.Count);
            Assert.Equal(1d, fixture.Frame.Opacity);
        });
        Test("关联启动拒绝只补一次淡入，完成后旧失败不能重播", () =>
        {
            using var fixture = new Fixture(loaded: true);
            var before = Storyboard.Created.Count;
            fixture.Begin(deferMotion: true);
            fixture.Attach();
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: false);
            Assert.Equal(before + 1, Storyboard.Created.Count);
            Storyboard.Created.Last().Complete();
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: false);
            Assert.Equal(before + 1, Storyboard.Created.Count);
            Assert.Equal(1d, fixture.Frame.Opacity);
        });
        Test("下一次原生呈现不会继承待决关联的回退", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin(deferMotion: true);
            var oldGate = Gate();
            fixture.Attach();
            var lateFrame = CompositionTarget.LateRender();
            var before = Storyboard.Created.Count;
            fixture.Begin(BrowseMotion.Native);
            var currentGate = Gate();
            fixture.Attach();
            lateFrame();
            BrowsePresentation.ResolveConnectedMotion(fixture.Frame, started: false);
            Assert.True(oldGate.IsCompletedSuccessfully);
            Assert.False(currentGate.IsCompleted);
            Assert.Equal(before, Storyboard.Created.Count);
            CompositionTarget.Render();
            fixture.Frame.DispatcherQueue.Drain();
            Assert.True(currentGate.IsCompletedSuccessfully);
        });
        Test("原生Storyboard.Stop抛错仍放行所有图片与本地透明终值", () =>
        {
            using var fixture = new Fixture(loaded: true);
            fixture.Begin();
            var gate = Gate();
            fixture.Attach();
            var board = Storyboard.Created.Last();
            board.ThrowOnStop = true;
            fixture.Stop();
            Assert.True(gate.IsCompletedSuccessfully);
            Assert.Equal(1d, fixture.Frame.Opacity);
            Assert.Equal(0, board.Handlers);
            board.ThrowOnStop = false;
            board.Stop();
        });
        return Run();
    }

    private static Type Production => Assembly.GetExecutingAssembly().GetType("Momoka.Shell.Views.BrowsePresentation")
        ?? throw new AssertionException("Current production BrowsePresentation is missing");

    private static Task Gate() => (Task)Production.GetMethod("WaitForImagesAsync", BindingFlags.Static | BindingFlags.NonPublic, [])!.Invoke(null, null)!;

    private static Task OwnerGate(DependencyObject owner)
    {
        var method = Production.GetMethod("WaitForImagesAsync", BindingFlags.Static | BindingFlags.NonPublic, [typeof(DependencyObject)])
            ?? throw new AssertionException("Current production owner image gate is missing");
        return (Task)method.Invoke(null, [owner])!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object _presentation;
        public Frame Frame { get; } = new();
        public FrameworkElement Page { get; } = new();
        public Fixture(bool loaded = false)
        {
            HomeMotion.ApplyPreference(true);
            UISettings.Enabled = true;
            UISettings.AddFailure = UISettings.EventFailure.None;
            UISettings.RemoveFailure = UISettings.EventFailure.None;
            Frame.Load();
            Frame.Content = Page;
            Page.VisualParent = Frame;
            if (loaded) Page.Load();
            _presentation = Activator.CreateInstance(Production, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, [Frame], null)!;
        }
        public void Begin(bool deferMotion = false) => Begin(deferMotion ? BrowseMotion.ConnectedPending : BrowseMotion.Fade);
        public void Begin(BrowseMotion motion) => Production.GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_presentation, [motion]);
        public void Attach() => Invoke("Attach");
        public void Stop() => Invoke("Stop");
        private void Invoke(string name) => Production.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_presentation, null);
        public void Dispose()
        {
            Stop();
            Frame.DispatcherQueue.Drain();
            HomeMotion.ApplyPreference(true);
            UISettings.Enabled = true;
            UISettings.AddFailure = UISettings.EventFailure.None;
            UISettings.RemoveFailure = UISettings.EventFailure.None;
        }
    }
}
