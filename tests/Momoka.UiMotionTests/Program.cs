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
        });

        Test("连点两次从原尺寸重新弹，不越弹越大", () =>
        {
            Reset();
            var icon = new FrameworkElement { ActualWidth = 20, Height = 20 };
            GlyphPulse.Play(icon);
            Storyboard.Advance(GlyphPulse.HalfMilliseconds / 2);

            GlyphPulse.Play(icon);
            var scale = (ScaleTransform)icon.RenderTransform!;
            Assert.Equal(1d, scale.ScaleX);

            Storyboard.Advance(GlyphPulse.HalfMilliseconds);
            Assert.True(scale.ScaleX > 1.1);
            Storyboard.Advance(GlyphPulse.HalfMilliseconds);
            Assert.Equal(1d, scale.ScaleX);
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

        return Run();
    }

    private static TranslateTransform Drift(FrameworkElement element) => (TranslateTransform)element.RenderTransform!;

    private static void Reset()
    {
        Storyboard.Reset();
        UISettings.Reset();
        HomeMotion.Reset();
    }
}
