using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomeEntranceTests
{
    internal static void Register()
    {
        foreach (var system in new[] { false, true })
            Test("生产页面入场进行中关闭" + (system ? "系统" : "应用") + "动画直接落定", () =>
            {
                Reset();
                var element = new FrameworkElement();
                HomeMotion.Reveal(element, 0, 180, 12);
                var board = Storyboard.Active.Single();
                var stale = board.CapturePostedCompletion();
                Storyboard.Advance(30);
                Assert.True(element.Opacity is > 0 and < 1);
                if (system) UISettings.SetAnimationsEnabled(false);
                else HomeMotion.ApplyPreference(false);
                Assert.Equal(1d, element.Opacity);
                Assert.Equal(0d, ((TranslateTransform)element.RenderTransform!).Y);
                Assert.Equal(0, Storyboard.Count);
                Assert.Equal(0, board.CompletionSubscriptionCount);
                Assert.Equal(0, element.UnloadedSubscriptionCount);
                stale();
                Assert.Equal(1d, element.Opacity);
            });

        Test("生产入场旧完成和系统通知不影响后来重播", () =>
        {
            Reset();
            var element = new FrameworkElement();
            HomeMotion.Reveal(element, 0, 180, 12);
            var stale = Storyboard.Active.Single().CapturePostedCompletion();
            element.DispatcherQueue.HasThreadAccess = false;
            UISettings.SetAnimationsEnabled(false);
            UISettings.SetAnimationsEnabled(true);
            element.DispatcherQueue.HasThreadAccess = true;
            HomeMotion.Reveal(element, 0, 180, 12);
            element.DispatcherQueue.Drain();
            stale();
            Assert.Equal(1, Storyboard.Count);
            Assert.Equal(0d, element.Opacity);
            Storyboard.Advance(180);
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("生产入场零时长和平台启动失败保持内容可读", () =>
        {
            Reset();
            var element = new FrameworkElement { Opacity = 0.4, RenderTransform = new TranslateTransform { Y = 8 } };
            HomeMotion.Reveal(element, 0, 0);
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0d, ((TranslateTransform)element.RenderTransform).Y);
            Assert.Equal(0, Storyboard.Count);
            Storyboard.ThrowOnBegin = true;
            HomeMotion.Reveal(element, 0);
            Assert.Equal(1d, element.Opacity);
            Assert.Equal(0d, ((TranslateTransform)element.RenderTransform).Y);
            Assert.Equal(0, UISettings.Created.Sum(settings => settings.SubscriptionCount));
        });

        Test("货架只动画已实现模板，缺目标不强制实现且回收取消等待", () =>
        {
            Reset();
            var root = new FrameworkElement();
            var repeater = new ItemsRepeater { ItemsSourceView = new(10_000) };
            var directChild = new FrameworkElement();
            var target = new FrameworkElement { IsLoaded = false };
            directChild.AddVisualChild(target);
            repeater.Prepare(0, directChild);
            repeater.Prepare(1, new FrameworkElement());
            HomeMotion.Enter(root, repeater, 3);
            Assert.Equal(3, repeater.TryGetCount);
            Assert.Equal(1, target.LoadedSubscriptionCount);
            Assert.Equal(0d, ((TranslateTransform?)directChild.RenderTransform)?.Y ?? 0);
            repeater.Clear(0);
            Assert.Equal(0, target.LoadedSubscriptionCount);
            Assert.Equal(0, target.UnloadedSubscriptionCount);
            target.RaiseLoaded();
            Assert.Equal(0, Storyboard.Count);
            HomeMotion.StopEnter(repeater);
        });

        Test("页面离树取消货架入场与待加载事件，后续实现不再播放", () =>
        {
            Reset();
            var root = new FrameworkElement();
            var repeater = new ItemsRepeater { ItemsSourceView = new(2) };
            var directChild = new FrameworkElement();
            var target = new FrameworkElement { IsLoaded = false };
            directChild.AddVisualChild(target);
            repeater.Prepare(0, directChild);
            HomeMotion.Enter(root, repeater, 2);
            Assert.Equal(1, target.LoadedSubscriptionCount);
            root.RaiseUnload();
            Assert.Equal(0, target.LoadedSubscriptionCount);
            var later = new FrameworkElement();
            later.AddVisualChild(new FrameworkElement());
            repeater.Prepare(1, later);
            Assert.Equal(0, Storyboard.Count);
            Assert.Equal(0, root.UnloadedSubscriptionCount);
        });

        Test("动画关闭不登记等加载入场；重新开启只作用于新调用", () =>
        {
            Reset();
            var root = new FrameworkElement();
            var repeater = new ItemsRepeater { ItemsSourceView = new(1) };
            var directChild = new FrameworkElement();
            var target = new FrameworkElement { IsLoaded = false };
            directChild.AddVisualChild(target);
            repeater.Prepare(0, directChild);
            HomeMotion.ApplyPreference(false);
            HomeMotion.Enter(root, repeater, 1);
            Assert.Equal(0, target.LoadedSubscriptionCount);
            HomeMotion.ApplyPreference(true);
            target.RaiseLoaded();
            Assert.Equal(0, Storyboard.Count);
            HomeMotion.Enter(root, repeater, 1);
            Assert.Equal(1, Storyboard.Count);
            HomeMotion.StopEnter(repeater);
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
