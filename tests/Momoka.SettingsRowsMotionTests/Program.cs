using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("启用状态不再挂系统固定时长过渡，静止时没有动画", () => With(fixture =>
        {
            Assert.True(fixture.Motion.HasTransitions);
            Assert.Equal(0, fixture.List.ItemContainerTransitions!.Count);
            fixture.AssertClean();
        }));
        Test("隐藏缓存分组的全局LayoutUpdated不重复写Height或触发布局循环", () => With(fixture =>
        {
            fixture.Section.IsVisible = false;
            var writes = fixture.Surface.SetValueCalls;
            for (var index = 0; index < 100; index++) fixture.List.RaiseLayout();
            Assert.Equal(writes, fixture.Surface.SetValueCalls);
            fixture.AssertClean();
        }));
        Test("静止淡出副本的全局布局通知不重建Clip或反复安排Popup", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            var popup = Popup.Created.Single();
            var canvas = (Canvas)popup.Child!;
            // The board first locks the card to its starting height; one geometry notification
            // updates that slice. Identical later layout notifications must then be no-ops.
            fixture.List.RaiseLayout();
            var clip = canvas.Clip;
            var writes = fixture.Surface.SetValueCalls;
            for (var index = 0; index < 100; index++) fixture.List.RaiseLayout();
            Assert.True(ReferenceEquals(clip, canvas.Clip));
            Assert.Equal(writes, fixture.Surface.SetValueCalls);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("关闭动画的首次构造和后续数据更新均即时落定", () =>
        {
            using var fixture = new MotionFixture(enabled: false);
            fixture.RemoveDependencies();
            fixture.Layout();
            Assert.False(fixture.Motion.HasTransitions);
            Assert.Equal(0, fixture.List.DispatcherQueue.PendingCount);
            fixture.AssertClean();
        });
        Test("同步删除从属数据，不替换 Rows、上级行、容器或偏好", () => With(fixture =>
        {
            var rows = fixture.Section.Rows;
            var parent = fixture.Visual(fixture.Parent);
            fixture.RemoveDependencies();
            Assert.Equal(2, rows.Count);
            Assert.True(ReferenceEquals(rows, fixture.Section.Rows));
            Assert.True(ReferenceEquals(rows, fixture.List.ItemsSource));
            Assert.True(ReferenceEquals(parent, fixture.Visual(fixture.Parent)));
            Assert.Equal(73, fixture.QualityPreference);
            Assert.Equal(2, fixture.CompressionPreference);
            Assert.True(fixture.Motion.IsAnimating, "低优先级排队也算未落定");
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("真实请求包含删除淡出、保留行补位与高度且全为160ms无stagger", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            var board = fixture.Start();
            Assert.True(board.Children.Any(animation => animation.Target is ContentPresenter && animation.Property == "Opacity" && animation.To == 0));
            Assert.True(board.Children.Any(animation => animation.Target is TranslateTransform && animation.Property == "Y"));
            Assert.True(board.Children.Any(animation => ReferenceEquals(animation.Target, fixture.Surface) && animation.Property == "Height"));
            Assert.True(board.Children.All(animation => animation.Duration.TimeSpan.TotalMilliseconds == 160 && animation.BeginTime == TimeSpan.Zero));
            Assert.True(board.Children.All(animation => animation.EasingFunction?.EasingMode == EasingMode.EaseOut));
            Assert.True(board.Children.Single(animation => animation.Property == "Height").EnableDependentAnimation);
            Assert.Equal(1, Storyboard.Count);
        }));
        Test("删除动画159ms仍在走，160ms由Completed真实落定而非timer", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            var board = fixture.Start();
            Storyboard.Advance(159);
            Assert.True(fixture.Motion.IsAnimating);
            Assert.Equal(1, Storyboard.Count);
            Storyboard.Advance(1);
            fixture.AssertClean();
            Assert.Equal(0, board.CompletionSubscriptionCount);
        }));
        Test("恢复数据当场可用，新行从透明到不透明在同一160ms落定", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Finish();
            fixture.RestoreDependencies();
            Assert.Equal(4, fixture.Section.Rows.Count);
            Assert.True(fixture.Motion.IsAnimating);
            var board = fixture.Start();
            var quality = fixture.Visual(fixture.Quality).Target;
            Assert.Equal(0d, quality.Opacity);
            Assert.True(board.Children.Any(animation => ReferenceEquals(animation.Target, quality) && animation.Property == "Opacity" && animation.From == 0 && animation.To == 1));
            Storyboard.Advance(80);
            Assert.True(quality.Opacity > 0 && quality.Opacity < 1);
            Assert.True(fixture.Motion.IsAnimating);
            Storyboard.Advance(80);
            fixture.AssertClean();
        }));
        Test("淡出副本与真实控件分开且所有子节点不命中、不tab、不进control树", () => With(fixture =>
        {
            var original = fixture.Visual(fixture.Quality);
            fixture.RemoveDependencies();
            fixture.Start();
            Assert.False(fixture.List.VisualChildren.Contains(original.Container));
            var popup = Popup.Created.Single();
            Assert.True(popup.IsOpen);
            Assert.False(fixture.Page.VisualChildren.Contains(popup), "不改父页面布局树");
            var canvas = (Canvas)popup.Child!;
            Assert.Equal(2, canvas.Children.Count);
            foreach (var element in Descendants(canvas).OfType<UIElement>())
            {
                Assert.False(element.IsHitTestVisible);
                Assert.False(element.IsTabStop);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(element));
            }
            Assert.True(canvas.Children.OfType<ContentPresenter>().Any(copy => ReferenceEquals(copy.Content, fixture.Quality)));
            Assert.True(canvas.Children.OfType<ContentPresenter>().All(copy => ReferenceEquals(copy.ContentTemplateSelector, fixture.List.ItemTemplateSelector)));
            Assert.Equal(fixture.List.ActualTheme, popup.RequestedTheme);
            Storyboard.Advance(80);
            Assert.True(canvas.Children.All(copy => copy.Opacity > 0 && copy.Opacity < 1));
            Storyboard.Advance(80);
            fixture.AssertClean();
        }));
        Test("上级开关一直全透明度、可命中、可tab，补位只动模板根", () => With(fixture =>
        {
            var parent = fixture.Visual(fixture.Parent);
            var tail = fixture.Visual(fixture.Tail);
            fixture.RemoveDependencies();
            fixture.Start();
            Assert.Equal(1d, parent.Target.Opacity);
            Assert.Null(parent.Target.RenderTransform);
            Assert.True(parent.Target.IsHitTestVisible && parent.Target.IsTabStop);
            Assert.True(ReferenceEquals(parent, fixture.Visual(fixture.Parent)));
            Assert.Null(tail.Container.RenderTransform);
            Assert.True(double.IsNaN(tail.Container.Height));
            Assert.NotNull(tail.Target.RenderTransform);
            Assert.True(ReferenceEquals(tail.Container, fixture.List.ContainerFromItem(fixture.Tail)));
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("同一轮连续删除只排一份工作并基于最终Rows布局", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            Assert.Equal(1, fixture.List.DispatcherQueue.PendingCount);
            fixture.Start();
            Assert.Equal(1, Storyboard.Begun.Count);
            Assert.Equal(2, ((Canvas)Popup.Created.Single().Child!).Children.Count);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("同轮关开关开净结果相同不闪烁、不制造假淡入淡出", () => With(fixture =>
        {
            var parent = fixture.Visual(fixture.Parent);
            fixture.RemoveDependencies();
            fixture.RestoreDependencies();
            fixture.RemoveDependencies();
            fixture.RestoreDependencies();
            Assert.True(fixture.Motion.IsAnimating);
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            Assert.Equal(0, Popup.Created.Count);
            Assert.True(ReferenceEquals(parent, fixture.Visual(fixture.Parent)));
            fixture.AssertClean();
        }));
        Test("同轮恢复新实例但标签类型相同视为同一可见行不多播", () => With(fixture =>
        {
            fixture.Section.Rows.Remove(fixture.Quality);
            var replacement = new TestRow(fixture.Quality.Label);
            fixture.Section.Rows.Insert(1, replacement);
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            Assert.Equal(0, Popup.Created.Count);
            Assert.Equal(1d, fixture.Visual(replacement).Target.Opacity);
            fixture.AssertClean();
        }));
        Test("同标签不同类型不能误复用，等高替换仍有真实淡入淡出", () => With(fixture =>
        {
            fixture.Section.Rows[1] = new OtherTestRow(fixture.Quality.Label);
            var board = fixture.Start();
            Assert.False(board.Children.Any(animation => animation.Property == "Height"));
            Assert.True(board.Children.Any(animation => animation.Property == "Opacity" && animation.To == 1));
            Assert.True(board.Children.Any(animation => animation.Target is ContentPresenter && animation.To == 0));
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("集合Move同步排序，只补位不淡变、不制造删除副本", () => With(fixture =>
        {
            fixture.Section.Rows.Move(3, 1);
            Assert.True(ReferenceEquals(fixture.Tail, fixture.Section.Rows[1]));
            var board = fixture.Start();
            Assert.True(board.Children.All(animation => animation.Property == "Y"));
            Assert.Equal(0, Popup.Created.Count);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("运行中反向恢复从当前高度补位，不从旧终点跳回", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            var first = fixture.Start();
            Storyboard.Advance(80);
            var height = fixture.Surface.ActualHeight;
            var tailY = FrameworkElement.DriftY(fixture.Visual(fixture.Tail).Target.RenderTransform);
            fixture.RestoreDependencies();
            Assert.True(fixture.Motion.IsAnimating);
            Assert.Equal(height, fixture.Surface.Height);
            var second = fixture.Start();
            Assert.False(ReferenceEquals(first, second));
            Assert.Equal(height, second.Children.Single(animation => animation.Property == "Height").From!.Value);
            Assert.True(Math.Abs(FrameworkElement.DriftY(fixture.Visual(fixture.Tail).Target.RenderTransform) - (tailY - 102)) < 0.001);
            Assert.Equal(0, first.CompletionSubscriptionCount);
            Assert.Equal(1, Storyboard.Count);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("十二次运行中快切只保留最后一趟，最后160ms全部可见落定", () => With(fixture =>
        {
            for (var index = 0; index < 12; index++)
            {
                if (index % 2 == 0) fixture.RemoveDependencies();
                else fixture.RestoreDependencies();
                fixture.List.DispatcherQueue.Drain();
                Assert.Equal(1, Storyboard.Count);
                Storyboard.Advance(40);
            }
            Assert.Equal(4, fixture.Section.Rows.Count);
            fixture.Finish();
            Assert.True(Popup.Created.All(popup => !popup.IsOpen));
            fixture.AssertClean();
        }));
        Test("淡入半途中同轮重新移除恢复从当前alpha接着播不重闪0", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Finish();
            fixture.RestoreDependencies();
            fixture.Start();
            Storyboard.Advance(40);
            var opacity = fixture.Visual(fixture.Quality).Target.Opacity;
            fixture.RemoveDependencies();
            fixture.RestoreDependencies();
            var board = fixture.Start();
            Assert.Equal(opacity, fixture.Visual(fixture.Quality).Target.Opacity);
            Assert.Equal(opacity, board.Children.Single(animation => ReferenceEquals(animation.Target, fixture.Visual(fixture.Quality).Target) && animation.Property == "Opacity").From!.Value);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("Stop取消进行中动画并清理透明、位移、固定高、剪裁和副本", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            Storyboard.Advance(70);
            fixture.Motion.Stop();
            fixture.AssertClean();
        }));
        Test("Stop取消排队工作，过期dispatcher回调不得再开动画", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Motion.Stop();
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("过期Completed已投递也不能结束替代的新动画", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            var old = fixture.Start();
            var lateCompletion = old.CapturePostedCompletion();
            Storyboard.Advance(40);
            fixture.RestoreDependencies();
            var current = fixture.Start();
            lateCompletion();
            Assert.True(fixture.Motion.IsAnimating);
            Assert.True(ReferenceEquals(current, Storyboard.Active.Single()));
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("应用减少动画偏好变化同步终止进行中的全部属性", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            HomeMotion.ApplyPreference(false);
            Assert.False(fixture.Motion.HasTransitions);
            fixture.AssertClean();
        }));
        Test("应用减少动画偏好关闭时排队行当场落定且不会晚到重启", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            HomeMotion.ApplyPreference(false);
            fixture.AssertClean();
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("关动画后再开不复活旧队列，只有新数据变化会再播", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            HomeMotion.ApplyPreference(false);
            HomeMotion.ApplyPreference(true);
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.Layout();
            fixture.RestoreDependencies();
            fixture.Start();
            Assert.True(fixture.Motion.HasTransitions && fixture.Motion.IsAnimating);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("系统减少动画事件在UI线程即时清理且与应用偏好共同遵循", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            UISettings.SetAnimationsEnabled(false);
            Assert.False(fixture.Motion.HasTransitions);
            fixture.AssertClean();
            fixture.RestoreDependencies();
            fixture.Layout();
            Assert.Equal(0, fixture.List.DispatcherQueue.PendingCount);
            fixture.AssertClean();
        }));
        Test("系统后台减少动画通知优先于排队行动画落定", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.List.DispatcherQueue.HasThreadAccess = false;
            UISettings.SetAnimationsEnabled(false);
            Assert.Equal(2, fixture.List.DispatcherQueue.PendingCount);
            fixture.List.DispatcherQueue.DrainOne();
            fixture.AssertClean();
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("分组隐藏同步取消进行中，恢复分组不复活旧动画", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            fixture.Section.IsVisible = false;
            fixture.AssertClean();
            fixture.Section.IsVisible = true;
            fixture.AssertClean();
        }));
        Test("分组隐藏取消排队并允许隐藏态立即更新数据", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Section.IsVisible = false;
            fixture.RestoreDependencies();
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(4, fixture.Section.Rows.Count);
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("任一父级Visibility收起都当场落定，不等LayoutUpdated", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            fixture.Page.Visibility = Visibility.Collapsed;
            fixture.AssertClean();
        }));
        Test("父级隐藏再恢复不会借旧generation晚到播放", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Scroll.Visibility = Visibility.Collapsed;
            fixture.RestoreDependencies();
            fixture.Scroll.Visibility = Visibility.Visible;
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("排队后离树即使未收到Unloaded，回调也必须清理固定高", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.List.XamlRoot = null;
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("仍Loaded的重复Unloaded不释放有效接线", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            fixture.List.RaiseUnload();
            Assert.True(fixture.Motion.IsAnimating);
            Assert.Equal(1, fixture.List.LayoutSubscriptionCount);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("真正Unloaded自动Detach全部事件并清理运行中副本", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            fixture.List.IsLoaded = false;
            fixture.List.RaiseUnload();
            fixture.AssertClean();
            Assert.Equal(0, fixture.List.LayoutSubscriptionCount);
            Assert.Equal(0, fixture.List.UnloadedSubscriptionCount);
            Assert.Equal(0, fixture.SystemPreference.SubscriptionCount);
            Assert.Equal(0, HomeMotion.PreferenceSubscriptionCount);
            Assert.False(fixture.Motion.HasTransitions);
        }));
        Test("Dispose可重复调用，排队工作和之后的CollectionChanged都无效", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Motion.Dispose();
            fixture.Motion.Dispose();
            fixture.RestoreDependencies();
            fixture.List.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("Dispose后晚到的偏好回调不得改新控制器的Height或Clip", () => With(fixture =>
        {
            fixture.List.DispatcherQueue.HasThreadAccess = false;
            UISettings.SetAnimationsEnabled(false);
            var old = fixture.Motion;
            fixture.Reattach();
            fixture.List.DispatcherQueue.HasThreadAccess = true;
            UISettings.SetAnimationsEnabled(true);
            fixture.RemoveDependencies();
            var queuedHeight = fixture.Surface.Height;
            var queuedClip = fixture.Surface.Clip;
            fixture.List.DispatcherQueue.DrainOne();
            Assert.Equal(queuedHeight, fixture.Surface.Height, "旧偏好回调不能清理新控制器的排队高度");
            Assert.True(ReferenceEquals(queuedClip, fixture.Surface.Clip));
            fixture.Start();
            var height = fixture.Surface.Height;
            var clip = fixture.Surface.Clip;
            old.Stop();
            old.ApplyPreference();
            Assert.Equal(height, fixture.Surface.Height);
            Assert.True(ReferenceEquals(clip, fixture.Surface.Clip));
            Assert.True(fixture.Motion.IsAnimating);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("Dispatcher拒绝排队立即撤销临时Height与Clip", () => With(fixture =>
        {
            fixture.List.DispatcherQueue.RejectEnqueue = true;
            fixture.RemoveDependencies();
            Assert.Equal(2, fixture.Section.Rows.Count);
            Assert.Equal(0, Storyboard.Begun.Count);
            fixture.AssertClean();
        }));
        Test("Storyboard.Begin抛错回退仍恢复所有行和副本", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            Storyboard.ThrowOnBegin = true;
            fixture.List.DispatcherQueue.Drain();
            fixture.AssertClean();
        }));
        Test("UpdateLayout抛错不留下排队或透明固定高剪裁", () => With(fixture =>
        {
            fixture.List.OnUpdateLayout = () => throw new InvalidOperationException("Injected layout failure");
            fixture.RemoveDependencies();
            fixture.List.DispatcherQueue.Drain();
            fixture.AssertClean();
        }));
        Test("窗口横向尺寸变化中断高度动画并全部落定", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            fixture.Surface.Resize(fixture.Surface.ActualWidth - 20, fixture.Surface.NaturalHeight);
            fixture.AssertClean();
        }));
        Test("高度帧更新剪裁跟随当前高度但不会取消动画", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            Storyboard.Advance(70);
            fixture.Surface.Resize(fixture.Surface.ActualWidth, fixture.Surface.NaturalHeight);
            Assert.True(fixture.Motion.IsAnimating);
            Assert.Equal(fixture.Surface.ActualHeight, fixture.Surface.Clip!.Rect.Height);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("滚动后删除副本跟着位置且被ScrollView可见区裁住", () => With(fixture =>
        {
            fixture.Scroll.NaturalHeight = 120;
            fixture.Surface.LayoutY = -20;
            fixture.Layout();
            fixture.RemoveDependencies();
            fixture.Start();
            var popup = Popup.Created.Single();
            var canvas = (Canvas)popup.Child!;
            var before = popup.VerticalOffset;
            fixture.Surface.LayoutY -= 30;
            fixture.List.RaiseLayout();
            Assert.Equal(before, popup.VerticalOffset, "越过视口上边的旧卡只画可见切片");
            Assert.Equal(0d, canvas.Clip!.Rect.Top);
            Assert.True(canvas.Clip.Rect.Bottom <= fixture.Scroll.ActualHeight);
            Assert.Equal(canvas.Height, canvas.Clip.Rect.Height);
            var firstCopy = (ContentPresenter)canvas.Children[0];
            Assert.True(firstCopy.LayoutY <= 7);
            Assert.True(popup.VerticalOffset >= fixture.Scroll.TransformToVisual(null).TransformPoint(new()).Y);
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("无Border父级不伪造行动画或排队", () =>
        {
            using var fixture = new MotionFixture(attachSurface: false);
            fixture.RemoveDependencies();
            Assert.Equal(0, fixture.List.DispatcherQueue.PendingCount);
            fixture.AssertClean();
        });
        Test("没有行模板时不创建空副本但补位高度仍受控落定", () => With(fixture =>
        {
            fixture.List.ItemTemplateSelector = null;
            fixture.RemoveDependencies();
            var board = fixture.Start();
            Assert.Equal(0, Popup.Created.Count);
            Assert.True(board.Children.Any(animation => animation.Property == "Height"));
            fixture.Finish();
            fixture.AssertClean();
        }));
        Test("已有本地透明度与Transform原对象在取消后精确恢复", () => With(fixture =>
        {
            var target = fixture.Visual(fixture.Tail).Target;
            var original = new TranslateTransform { Y = 5 };
            target.Opacity = 0.8;
            target.RenderTransform = original;
            fixture.Layout();
            fixture.RemoveDependencies();
            fixture.Start();
            fixture.Motion.Stop();
            Assert.Equal(0.8, target.Opacity);
            Assert.True(ReferenceEquals(original, target.RenderTransform));
            Assert.Equal(5d, original.Y);
            Assert.True(double.IsNaN(fixture.Surface.Height));
            Assert.Null(fixture.Surface.Clip);
        }));
        Test("未设本地Opacity或Transform的行落定后仍Unset不固定局部值", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Finish();
            fixture.RestoreDependencies();
            fixture.Finish();
            foreach (var visual in fixture.AllVisuals)
            {
                Assert.True(ReferenceEquals(DependencyProperty.UnsetValue, visual.Target.ReadLocalValue(UIElement.OpacityProperty)));
                Assert.True(ReferenceEquals(DependencyProperty.UnsetValue, visual.Target.ReadLocalValue(UIElement.RenderTransformProperty)));
            }
            fixture.AssertClean();
        }));
        Test("清空最后一行直接落定不保留失效整卡或淡出副本", () => With(fixture =>
        {
            fixture.RemoveDependencies();
            fixture.Start();
            fixture.Section.Rows.Clear();
            Assert.Equal(0, fixture.Section.Rows.Count);
            fixture.AssertClean();
        }));
        return Run();
    }

    private static void With(Action<MotionFixture> run)
    {
        using var fixture = new MotionFixture();
        run(fixture);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in root.VisualChildren)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
