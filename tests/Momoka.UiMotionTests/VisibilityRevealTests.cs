using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 绑定的 Visibility 面板入场：只响应收起后再次显示的那一次，退出立即复位，
/// 关动画、离树和别的 RenderTransform 都不抢元素自己的状态。
/// </summary>
internal static class VisibilityRevealTests
{
    internal static void Register()
    {
        Test("面板首次可见不播，收起再显示从下方淡入", () =>
        {
            Reset();
            var element = new FrameworkElement();
            VisibilityReveal.SetIsEnabled(element, true);
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0, Storyboard.Count);

            element.Visibility = Visibility.Collapsed;
            Assert.Equal(1d, element.Opacity);

            element.Visibility = Visibility.Visible;
            Assert.Equal(1, Storyboard.Count);
            Assert.Equal(0d, element.Opacity);
            var drift = (TranslateTransform)element.RenderTransform!;
            Assert.Equal(VisibilityReveal.EnterOffset, drift.Y);

            Storyboard.Advance(VisibilityReveal.EnterMilliseconds / 2);
            Assert.True(element.Opacity > 0 && element.Opacity < 1, $"中途不透明度 {element.Opacity}");
            Assert.True(drift.Y > 0 && drift.Y < VisibilityReveal.EnterOffset);

            Storyboard.Advance(VisibilityReveal.EnterMilliseconds);
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0d, drift.Y);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("面板收起打断入场并立即复位", () =>
        {
            Reset();
            var element = new FrameworkElement();
            VisibilityReveal.SetIsEnabled(element, true);
            element.Visibility = Visibility.Collapsed;
            element.Visibility = Visibility.Visible;
            Storyboard.Advance(40);
            Assert.True(element.Opacity is > 0 and < 1);

            element.Visibility = Visibility.Collapsed;
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0d, ((TranslateTransform)element.RenderTransform!).Y);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("动画关闭时面板直接显示，不创建时钟", () =>
        {
            Reset();
            var element = new FrameworkElement();
            VisibilityReveal.SetIsEnabled(element, true);
            HomeMotion.ApplyPreference(false);

            element.Visibility = Visibility.Collapsed;
            element.Visibility = Visibility.Visible;
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("元素没有 XamlRoot 时直接落定", () =>
        {
            Reset();
            var element = new FrameworkElement { XamlRoot = null };
            VisibilityReveal.SetIsEnabled(element, true);

            element.Visibility = Visibility.Collapsed;
            element.Visibility = Visibility.Visible;
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("面板自己的 RenderTransform 不被换成位移", () =>
        {
            Reset();
            var scale = new ScaleTransform();
            var element = new FrameworkElement { RenderTransform = scale };
            VisibilityReveal.SetIsEnabled(element, true);

            element.Visibility = Visibility.Collapsed;
            element.Visibility = Visibility.Visible;
            Assert.True(ReferenceEquals(scale, element.RenderTransform));
            Assert.Equal(0d, element.Opacity);
            Storyboard.Advance(VisibilityReveal.EnterMilliseconds);
            Assert.Equal(1d, element.Opacity);
        });

        Test("关掉附加属性后不再订阅可见性", () =>
        {
            Reset();
            var element = new FrameworkElement();
            VisibilityReveal.SetIsEnabled(element, true);
            Assert.Equal(1, element.PropertySubscriptionCount);
            VisibilityReveal.SetIsEnabled(element, false);
            Assert.Equal(0, element.PropertySubscriptionCount);

            element.Visibility = Visibility.Collapsed;
            element.Visibility = Visibility.Visible;
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("系统动画关闭时面板不再淡入", () =>
        {
            Reset();
            var element = new FrameworkElement();
            VisibilityReveal.SetIsEnabled(element, true);
            UISettings.SetAnimationsEnabled(false);

            element.Visibility = Visibility.Collapsed;
            element.Visibility = Visibility.Visible;
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0, Storyboard.Count);
        });
    }

    private static void Reset()
    {
        Storyboard.Reset();
        UISettings.Reset();
        HomeMotion.ApplyPreference(true);
    }
}
