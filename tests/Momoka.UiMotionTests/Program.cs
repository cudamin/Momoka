using Microsoft.UI.Xaml;
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
        Test("常驻元素第一次显示从下方淡入，落定后停在原位", () =>
        {
            Reset();
            var element = new FrameworkElement();
            var motion = new SurfaceMotion(element);

            motion.Enter();
            Assert.True(motion.IsAnimating);
            Assert.Equal(0d, element.Opacity);
            Assert.Equal(SurfaceMotion.EnterOffset, Drift(element).Y);

            Storyboard.Advance(SurfaceMotion.EnterMilliseconds / 2);
            Assert.True(element.Opacity > 0 && element.Opacity < 1, $"中途不透明度 {element.Opacity}");
            Assert.True(Drift(element).Y > 0 && Drift(element).Y < SurfaceMotion.EnterOffset);

            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
            Assert.False(motion.IsAnimating);
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0d, Drift(element).Y);
        });

        Test("收起先淡出再落定，回调只报一次", () =>
        {
            Reset();
            var element = new FrameworkElement();
            var motion = new SurfaceMotion(element);
            motion.Enter();
            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);

            var landed = 0;
            motion.Exit(landed: () => landed++);
            Storyboard.Advance(SurfaceMotion.ExitMilliseconds / 2);
            Assert.True(element.Opacity > 0 && element.Opacity < 1, $"淡出中途 {element.Opacity}");
            Assert.Equal(0, landed);

            Storyboard.Advance(SurfaceMotion.ExitMilliseconds);
            Assert.Equal(0d, element.Opacity);
            Assert.Equal(SurfaceMotion.ExitOffset, Drift(element).Y);
            Assert.Equal(1, landed);
        });

        Test("收起途中重新显示从当前一帧接上，不闪回", () =>
        {
            Reset();
            var element = new FrameworkElement();
            var motion = new SurfaceMotion(element);
            motion.Enter();
            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);

            motion.Exit();
            Storyboard.Advance(SurfaceMotion.ExitMilliseconds / 2);
            var interrupted = element.Opacity;
            var interruptedY = Drift(element).Y;
            Assert.True(interrupted < 1);

            motion.Enter();
            Assert.True(element.Opacity >= interrupted - 0.001, "重新显示不能跳回全透明");
            Assert.True(Drift(element).Y <= interruptedY + 0.001, "重新显示不能跳回起点位移");

            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0d, Drift(element).Y);
        });

        foreach (var system in new[] { false, true })
            foreach (var exiting in new[] { false, true })
                Test($"浮层进行中关闭{(system ? "系统" : "应用")}动画立即{(exiting ? "收起" : "显示")}", () =>
                {
                    Reset();
                    var element = new FrameworkElement();
                    var motion = new SurfaceMotion(element);
                    var landed = 0;
                    motion.Enter();
                    if (exiting)
                    {
                        Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
                        motion.Exit(landed: () => landed++);
                    }
                    var stale = Storyboard.Active.Single().CapturePostedCompletion();
                    Storyboard.Advance(30);
                    if (system) UISettings.SetAnimationsEnabled(false);
                    else HomeMotion.ApplyPreference(false);
                    Assert.False(motion.IsAnimating);
                    Assert.Equal(exiting ? 0d : 1d, element.Opacity);
                    Assert.Equal(exiting ? SurfaceMotion.ExitOffset : 0d, Drift(element).Y);
                    Assert.Equal(exiting ? 1 : 0, landed);
                    stale();
                    Assert.Equal(exiting ? 1 : 0, landed);
                    Assert.Equal(0, Storyboard.Count);
                });

        Test("浮层旧系统通知与旧完成不能取消反向后的新动画", () =>
        {
            Reset();
            var element = new FrameworkElement();
            var motion = new SurfaceMotion(element);
            motion.Enter();
            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
            var landed = 0;
            motion.Exit(landed: () => landed++);
            var stale = Storyboard.Active.Single().CapturePostedCompletion();
            element.DispatcherQueue.HasThreadAccess = false;
            UISettings.SetAnimationsEnabled(false);
            Assert.Equal(1, element.DispatcherQueue.PendingCount);
            UISettings.SetAnimationsEnabled(true);
            element.DispatcherQueue.HasThreadAccess = true;
            motion.Enter();
            element.DispatcherQueue.Drain();
            stale();
            Assert.True(motion.IsAnimating);
            Assert.Equal(0, landed);
            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0, landed);
        });

        Test("零时长与启动失败立即落定且不保留浮层订阅", () =>
        {
            Reset();
            var element = new FrameworkElement();
            var motion = new SurfaceMotion(element);
            motion.Enter(0);
            Assert.False(motion.IsAnimating);
            var landed = 0;
            motion.Exit(0, landed: () => landed++);
            Assert.Equal(1, landed);
            Storyboard.ThrowOnBegin = true;
            motion.Enter();
            Assert.Equal(1d, element.Opacity);
            motion.Exit(landed: () => landed++);
            Assert.Equal(0d, element.Opacity);
            Assert.Equal(2, landed);
            Assert.False(motion.IsAnimating);
            Assert.Equal(0, UISettings.Created.Sum(settings => settings.SubscriptionCount));
        });

        Test("已经显示着再叫一次不重播也不清零", () =>
        {
            Reset();
            var element = new FrameworkElement();
            var motion = new SurfaceMotion(element);
            motion.Enter();
            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
            var boards = Storyboard.Begun.Count;

            motion.Enter();
            Assert.Equal(boards, Storyboard.Begun.Count);
            Assert.Equal(1d, element.Opacity);
        });

        Test("界面动画关掉时直接落定，收起回调当场兑现", () =>
        {
            Reset();
            var element = new FrameworkElement();
            var motion = new SurfaceMotion(element);
            HomeMotion.ApplyPreference(false);
            try
            {
                motion.Enter();
                Assert.False(motion.IsAnimating);
                Assert.Equal(1d, element.Opacity);
                // 不播动画就不装位移用的 Transform —— 关掉界面动画不该在视觉树上留痕迹。
                Assert.True(element.RenderTransform is null);

                var landed = 0;
                motion.Exit(landed: () => landed++);
                Assert.Equal(1, landed);
                Assert.Equal(1d, element.Opacity);
            }
            finally
            {
                HomeMotion.ApplyPreference(true);
            }
        });

        Test("元素自己摆过别的变换时不抢，只淡入不平移", () =>
        {
            Reset();
            var element = new FrameworkElement { RenderTransform = new TransformGroup() };
            var motion = new SurfaceMotion(element);

            motion.Enter();
            Assert.True(motion.IsAnimating);
            Assert.True(element.RenderTransform is TransformGroup);

            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
            Assert.Equal(1d, element.Opacity);
        });

        Test("真正离树后把值复位，回来不会停在透明上", () =>
        {
            Reset();
            var element = new FrameworkElement();
            var motion = new SurfaceMotion(element);
            motion.Enter();
            Storyboard.Advance(SurfaceMotion.EnterMilliseconds / 3);

            element.XamlRoot = null;
            element.RaiseUnload();
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0d, Drift(element).Y);
            Assert.False(motion.IsAnimating);
        });

        Test("收藏图标按下时弹一下再回到原尺寸", () =>
        {
            Reset();
            var icon = new FrameworkElement { ActualWidth = 20, Height = 20 };
            Assert.True(GlyphPulse.Play(icon));

            var scale = (ScaleTransform)icon.RenderTransform!;
            Assert.Equal(10d, scale.CenterX);
            Assert.Equal(10d, scale.CenterY);

            Storyboard.Advance(GlyphPulse.HalfMilliseconds);
            Assert.True(scale.ScaleX > 1.1, $"峰值 {scale.ScaleX}");
            Assert.True(scale.ScaleY > 1.1);

            Storyboard.Advance(GlyphPulse.HalfMilliseconds);
            Assert.Equal(1d, scale.ScaleX);
            Assert.Equal(1d, scale.ScaleY);
            Assert.Equal(0, Storyboard.Count);
            Assert.Equal(0, icon.UnloadedSubscriptionCount);
        });

        Test("连点两次从原尺寸重新弹，不越弹越大", () =>
        {
            Reset();
            var icon = new FrameworkElement { ActualWidth = 20, Height = 20 };
            GlyphPulse.Play(icon);
            Storyboard.Advance(GlyphPulse.HalfMilliseconds / 2);
            var old = Storyboard.Active.Single();
            var stale = old.CapturePostedCompletion();

            GlyphPulse.Play(icon);
            var scale = (ScaleTransform)icon.RenderTransform!;
            Assert.Equal(1d, scale.ScaleX);
            Assert.Equal(1, Storyboard.Count);
            Assert.Equal(0, old.CompletionSubscriptionCount);
            stale();
            Assert.Equal(1, Storyboard.Count);

            Storyboard.Advance(GlyphPulse.HalfMilliseconds);
            Assert.True(scale.ScaleX > 1.1);
            Storyboard.Advance(GlyphPulse.HalfMilliseconds);
            Assert.Equal(1d, scale.ScaleX);
        });

        foreach (var reason in new[] { "应用关闭", "系统关闭", "离树", "启动失败" })
            Test("脉冲" + reason + "复位缩放并清掉时钟与订阅", () =>
            {
                Reset();
                var icon = new FrameworkElement { ActualWidth = 20, Height = 20 };
                Storyboard.ThrowOnBegin = reason == "启动失败";
                Assert.Equal(reason != "启动失败", GlyphPulse.Play(icon));
                if (reason != "启动失败")
                {
                    Storyboard.Advance(GlyphPulse.HalfMilliseconds);
                    Assert.True(((ScaleTransform)icon.RenderTransform!).ScaleX > 1);
                }
                if (reason == "应用关闭") HomeMotion.ApplyPreference(false);
                if (reason == "系统关闭") UISettings.SetAnimationsEnabled(false);
                if (reason == "离树") icon.RaiseUnload();
                var scale = (ScaleTransform)icon.RenderTransform!;
                Assert.Equal(1d, scale.ScaleX);
                Assert.Equal(1d, scale.ScaleY);
                Assert.Equal(0, Storyboard.Count);
                Assert.Equal(0, icon.UnloadedSubscriptionCount);
                Assert.Equal(0, UISettings.Created.Sum(settings => settings.SubscriptionCount));
            });

        Test("无尺寸图标不创建脉冲；系统回调在UI线程落定", () =>
        {
            Reset();
            var icon = new FrameworkElement { ActualWidth = 0, Height = 20 };
            Assert.False(GlyphPulse.Play(icon));
            Assert.Null(icon.RenderTransform);
            icon.ActualWidth = 20;
            GlyphPulse.Play(icon);
            icon.DispatcherQueue.HasThreadAccess = false;
            UISettings.SetAnimationsEnabled(false);
            Assert.Equal(1, icon.DispatcherQueue.PendingCount);
            Assert.Equal(1, Storyboard.Count);
            icon.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Count);
            Assert.Equal(1d, ((ScaleTransform)icon.RenderTransform!).ScaleX);
        });

        Test("界面动画关掉或元素有别的变换时，图标保持原样", () =>
        {
            Reset();
            var icon = new FrameworkElement { ActualWidth = 20, Height = 20, RenderTransform = new TranslateTransform() };
            Assert.False(GlyphPulse.Play(icon));
            Assert.True(icon.RenderTransform is TranslateTransform);

            var plain = new FrameworkElement { ActualWidth = 20, Height = 20 };
            HomeMotion.ApplyPreference(false);
            try
            {
                Assert.False(GlyphPulse.Play(plain));
                Assert.True(plain.RenderTransform is null);
            }
            finally
            {
                HomeMotion.ApplyPreference(true);
            }

            plain.XamlRoot = null;
            Assert.False(GlyphPulse.Play(plain));
        });

        HomeEntranceTests.Register();
        ShelfFocusTests.Register();

        var result = Run();
        Reset();
        return result;
    }

    private static TranslateTransform Drift(FrameworkElement element) => (TranslateTransform)element.RenderTransform!;

    private static void Reset()
    {
        Storyboard.Reset();
        UISettings.Reset();
        HomeMotion.ApplyPreference(true);
    }
}
