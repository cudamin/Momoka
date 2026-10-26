using System.Reflection;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("禁动画时Rules.Move不排队、不制造before、不报回弹", () => With(fixture =>
        {
            HomeMotion.ApplyPreference(false);
            fixture.Row.Rules.Move(0, 1);
            Assert.False(fixture.Motion.IsAnimating);
            Assert.False(fixture.Motion.HasSpring);
            Assert.Equal(0, fixture.List.DispatcherQueue.PendingCount);
            Assert.Null(Before(fixture.Motion));
        }));
        Test("Stop立即取消queue和before，旧回调不能重新测量或开动画", () => With(fixture =>
        {
            fixture.Move();
            Assert.True(fixture.Motion.IsAnimating);
            Assert.NotNull(Before(fixture.Motion));
            fixture.Motion.Stop();
            Assert.False(fixture.Motion.IsAnimating);
            Assert.Null(Before(fixture.Motion));
            var reads = fixture.LayoutReads;
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(reads, fixture.LayoutReads);
            Assert.Equal(0, Storyboard.Begun.Count);
        }));
        Test("关闭应用动画偏好同步清理排队和快照", () => With(fixture =>
        {
            fixture.Move();
            HomeMotion.ApplyPreference(false);
            Assert.False(fixture.Motion.IsAnimating);
            Assert.Null(Before(fixture.Motion));
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
        }));
        Test("关闭应用动画偏好同步清理正在回弹的位移", () => With(fixture =>
        {
            fixture.Move();
            fixture.Start();
            Assert.True(fixture.Motion.HasSpring);
            HomeMotion.ApplyPreference(false);
            fixture.AssertClean();
        }));
        Test("旧generation回调不得消费替代queue或before", () => With(fixture =>
        {
            fixture.Move();
            fixture.Motion.Stop();
            fixture.Layout();
            fixture.Row.Rules.Move(1, 2);
            var before = Before(fixture.Motion);
            var reads = fixture.LayoutReads;
            Assert.Equal(2, fixture.List.DispatcherQueue.PendingCount);
            fixture.List.DispatcherQueue.DrainOne();
            Assert.True(fixture.Motion.IsAnimating);
            Assert.True(ReferenceEquals(before, Before(fixture.Motion)));
            Assert.Equal(reads, fixture.LayoutReads);
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.List.DispatcherQueue.Drain();
            Assert.True(fixture.Motion.HasSpring);
        }));
        Test("关闭再开动画不能复活旧queue，只能响应新Rules变化", () => With(fixture =>
        {
            fixture.Move();
            HomeMotion.ApplyPreference(false);
            HomeMotion.ApplyPreference(true);
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.Layout();
            fixture.Row.Rules.Move(1, 2);
            fixture.Start();
            Assert.True(fixture.Motion.HasSpring);
        }));
        Test("原生拖拽中关闭动画仍结束两阶段一次但不排queue", () => With(fixture =>
        {
            fixture.Motion.BeginDrag();
            var dragged = fixture.Row.Rules[0];
            fixture.Row.Rules.RemoveAt(0);
            Assert.Equal(1, fixture.Row.BeginCalls);
            Assert.Equal(0, fixture.Row.EndCalls);
            HomeMotion.ApplyPreference(false);
            fixture.Row.Rules.Add(dragged);
            fixture.Motion.EndDrag();
            Assert.Equal(1, fixture.Row.EndCalls);
            Assert.Equal(0, fixture.List.DispatcherQueue.PendingCount);
            Assert.Null(Before(fixture.Motion));
            fixture.AssertClean();
        }));
        Test("禁动画仍支持拖拽Remove加Add并完整提交两阶段", () => With(fixture =>
        {
            HomeMotion.ApplyPreference(false);
            fixture.Motion.BeginDrag();
            var dragged = fixture.Row.Rules[0];
            fixture.Row.Rules.RemoveAt(0);
            fixture.Row.Rules.Add(dragged);
            fixture.Motion.EndDrag();
            Assert.Equal(1, fixture.Row.BeginCalls);
            Assert.Equal(1, fixture.Row.EndCalls);
            Assert.Equal(3, fixture.Row.Rules.Count);
            Assert.True(ReferenceEquals(dragged, fixture.Row.Rules.Last()));
            Assert.Equal(0, fixture.List.DispatcherQueue.PendingCount);
            fixture.AssertClean();
        }));
        Test("拖拽中Stop只取消动画不越权结束业务两阶段", () => With(fixture =>
        {
            fixture.Motion.BeginDrag();
            fixture.Motion.Stop();
            var dragged = fixture.Row.Rules[0];
            fixture.Row.Rules.RemoveAt(0);
            fixture.Row.Rules.Add(dragged);
            Assert.Equal(0, fixture.Row.EndCalls);
            fixture.Motion.EndDrag();
            Assert.Equal(1, fixture.Row.EndCalls);
            fixture.Start();
            fixture.AssertClean();
        }));
        Test("BeginDrag失效上一轮queue且新拖拽快照不被旧callback吃掉", () => With(fixture =>
        {
            fixture.Move();
            fixture.Motion.BeginDrag();
            var dragged = fixture.Row.Rules[0];
            fixture.Row.Rules.RemoveAt(0);
            fixture.Row.Rules.Add(dragged);
            fixture.Motion.EndDrag();
            var before = Before(fixture.Motion);
            fixture.List.DispatcherQueue.DrainOne();
            Assert.True(ReferenceEquals(before, Before(fixture.Motion)));
            Assert.True(fixture.Motion.IsAnimating);
            fixture.List.DispatcherQueue.Drain();
            Assert.True(fixture.Motion.HasSpring);
        }));
        Test("Detach清理queue和快照且后续移动不产生动画", () => With(fixture =>
        {
            fixture.Move();
            fixture.Motion.Detach();
            Assert.Null(Before(fixture.Motion));
            Assert.False(fixture.Motion.IsAnimating);
            fixture.Row.Rules.Move(0, 1);
            var reads = fixture.LayoutReads;
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(reads, fixture.LayoutReads);
            Assert.Equal(0, Storyboard.Begun.Count);
            Assert.Equal(0, fixture.List.LayoutSubscriberCount);
        }));
        Test("Detach结束尚未结束拖拽恰好一次并不再排queue", () => With(fixture =>
        {
            fixture.Motion.BeginDrag();
            var dragged = fixture.Row.Rules[0];
            fixture.Row.Rules.RemoveAt(0);
            fixture.Row.Rules.Add(dragged);
            fixture.Motion.Detach();
            fixture.Motion.Detach();
            fixture.Motion.EndDrag();
            Assert.Equal(1, fixture.Row.BeginCalls);
            Assert.Equal(1, fixture.Row.EndCalls);
            Assert.Equal(0, fixture.List.DispatcherQueue.PendingCount);
            fixture.AssertClean();
        }));
        Test("离树callback取消queue后不得TransformToVisual测量", () => With(fixture =>
        {
            fixture.Move();
            fixture.List.XamlRoot = null;
            fixture.List.DispatcherQueue.Drain();
            Assert.False(fixture.Motion.IsAnimating);
            Assert.Null(Before(fixture.Motion));
            Assert.Equal(0, Storyboard.Begun.Count);
        }));
        Test("已经离树的Rules更新不排队", () => With(fixture =>
        {
            fixture.List.XamlRoot = null;
            fixture.Move();
            Assert.Equal(0, fixture.List.DispatcherQueue.PendingCount);
            Assert.False(fixture.Motion.IsAnimating);
            Assert.Null(Before(fixture.Motion));
        }));
        Test("Dispatcher拒绝入队立即清理before与queue", () => With(fixture =>
        {
            fixture.List.DispatcherQueue.RejectEnqueue = true;
            fixture.Move();
            Assert.False(fixture.Motion.IsAnimating);
            Assert.Null(Before(fixture.Motion));
            Assert.Equal(0, Storyboard.Begun.Count);
        }));
        Test("正常Rules.Move仍同一轮换位两行回弹、380ms关键帧不改", () => With(fixture =>
        {
            var rows = fixture.Row.Rules;
            var rule = rows[0];
            fixture.Move();
            fixture.Start();
            Assert.True(ReferenceEquals(rows, fixture.Row.Rules));
            Assert.True(ReferenceEquals(rule, rows[1]));
            Assert.Equal(2, Storyboard.Active.Count);
            foreach (var board in Storyboard.Active)
            {
                var frames = board.Children.Single();
                Assert.Equal("Y", frames.Property);
                Assert.Equal(380d, frames.Duration.TimeSpan.TotalMilliseconds);
                Assert.True(frames.KeyFrames.Select(frame => frame.KeyTime.TimeSpan.TotalMilliseconds).SequenceEqual(new[] { 0d, 230d, 310d, 380d }));
                Assert.Equal(0d, frames.KeyFrames.Last().Value);
            }
            Storyboard.Advance(379);
            Assert.True(fixture.Motion.IsAnimating);
            Storyboard.Advance(1);
            fixture.AssertClean();
        }));
        Test("同轮两次Rules.Move仅一份queue仍取最终布局", () => With(fixture =>
        {
            fixture.Move();
            fixture.Row.Rules.Move(1, 2);
            Assert.Equal(1, fixture.List.DispatcherQueue.PendingCount);
            fixture.Start();
            Assert.True(fixture.Motion.HasSpring);
        }));
        Test("活动回弹Stop立即将所有真实模板根Y清零", () => With(fixture =>
        {
            fixture.Move();
            fixture.Start();
            Storyboard.Advance(120);
            fixture.Motion.Stop();
            fixture.AssertClean();
        }));
        Test("已投递旧Completed不得删掉同根的新board或留下非零位移", () => With(fixture =>
        {
            fixture.Move();
            fixture.Start();
            var posted = Storyboard.Active.Select(board => board.CapturePostedCompletion()).ToArray();
            fixture.Motion.Stop();
            fixture.Layout();
            fixture.Row.Rules.Move(1, 0);
            fixture.Start();
            var active = Storyboard.Active.ToArray();
            foreach (var complete in posted) complete();
            Assert.True(fixture.Motion.HasSpring);
            Assert.True(active.SequenceEqual(Storyboard.Active));
            Storyboard.Advance(380);
            fixture.AssertClean();
        }));
        Test("无位移拖拽仍结束一次且不生成假回弹", () => With(fixture =>
        {
            fixture.Motion.BeginDrag();
            fixture.Motion.EndDrag();
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(1, fixture.Row.BeginCalls);
            Assert.Equal(1, fixture.Row.EndCalls);
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("DragItemsStarting边界拒绝非单项或非规则不改业务状态", () => With(fixture =>
        {
            var args = new DragItemsStartingEventArgs();
            fixture.Motion.BeginDrag(args);
            Assert.True(args.Cancel);
            Assert.Equal(0, fixture.Row.BeginCalls);
            args = new DragItemsStartingEventArgs();
            args.Items.Add(new object());
            fixture.Motion.BeginDrag(args);
            Assert.True(args.Cancel);
            Assert.Equal(0, fixture.Row.BeginCalls);
        }));
        Test("布局期间关闭动画不能继续给after开回弹", () => With(fixture =>
        {
            fixture.Move();
            fixture.AfterLayout = () => HomeMotion.ApplyPreference(false);
            fixture.Start();
            fixture.AssertClean();
            Assert.Null(Before(fixture.Motion));
        }));
        return Run();
    }

    private static object? Before(SettingsPriorityMotion motion) => typeof(SettingsPriorityMotion)
        .GetField("_before", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(motion);

    private static void With(Action<PriorityFixture> run)
    {
        using var fixture = new PriorityFixture();
        run(fixture);
    }
}

internal sealed class PriorityFixture : IDisposable
{
    internal ListView List { get; } = new();
    internal SettingTitleRulesRow Row { get; } = new();
    internal Dictionary<TitleRuleChoice, FrameworkElement> Roots { get; } = [];
    internal SettingsPriorityMotion Motion { get; }
    internal int LayoutReads { get; private set; }
    internal Action? AfterLayout { get; set; }

    internal PriorityFixture()
    {
        HomeMotion.Reset();
        Storyboard.Reset();
        foreach (var term in new[] { "双语", "SDH", "简体" })
        {
            var rule = new TitleRuleChoice(term);
            Row.Rules.Add(rule);
            var container = new FrameworkElement();
            var wrapper = new FrameworkElement();
            var root = new FrameworkElement { Name = "PriorityRuleRoot", RenderTransform = new() };
            List.AddChild(container);
            container.AddChild(wrapper);
            wrapper.AddChild(root);
            List.Containers.Add(rule, container);
            Roots.Add(rule, root);
        }
        List.LayoutReading = () =>
        {
            LayoutReads++;
            for (var index = 0; index < Row.Rules.Count; index++)
                List.Containers[Row.Rules[index]].LayoutY = index * 50;
            AfterLayout?.Invoke();
        };
        Motion = new SettingsPriorityMotion(List, Row);
    }

    internal void Move() => Row.Rules.Move(0, 1);
    internal void Layout() => List.UpdateLayout();
    internal void Start() => List.DispatcherQueue.Drain();
    internal void AssertClean()
    {
        Assert.False(Motion.IsAnimating);
        Assert.False(Motion.HasSpring);
        Assert.Equal(0, Storyboard.Active.Count);
        foreach (var root in Roots.Values) Assert.Equal(0d, root.RenderTransform!.Y);
    }
    public void Dispose()
    {
        Motion.Detach();
        Assert.Equal(0, List.LayoutSubscriberCount);
        Assert.Equal(0, HomeMotion.Subscribers);
        Storyboard.Reset();
        HomeMotion.Reset();
    }
}
