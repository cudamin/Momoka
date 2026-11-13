using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using CommunityToolkit.WinUI.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("平台边界拒绝停止从未注册的Translation属性", () =>
        {
            var visual = ElementCompositionPreview.GetElementVisual(new Microsoft.UI.Xaml.Controls.Border());
            Assert.False(visual.Properties.TranslationRegistered);
            Assert.Throws<ArgumentException>(() => visual.StopAnimation("Translation"));
            visual.StopAnimation("Opacity");
        });
        Test("首次导航Reset早于Loaded和implicit hook也先注册Translation", () =>
        {
            HomeMotion.ApplyPreference(true);
            UISettings.SetAnimationsEnabled(true);
            var page = new DetailPage();
            var visual = ElementCompositionPreview.GetElementVisual(page.StickyHeader);
            Assert.False(visual.Properties.TranslationRegistered);
            Assert.Null(Implicit.GetShowAnimations(page.StickyHeader));
            page.ResetHeader();
            Assert.True(visual.Properties.TranslationRegistered);
            Assert.Equal(Vector3.Zero, visual.Properties.Translation);
            Assert.Equal(1d, visual.Opacity);
            Assert.Equal(Visibility.Collapsed, page.StickyHeader.Visibility);
            Assert.True(visual.Stopped.SetEquals(["Opacity", "Translation"]));
            Assert.False(page.Active);
            page.ReleaseHeader();
        });
        Test("启动前关闭应用动画的首次Loaded仍能安全清理Translation", () =>
        {
            HomeMotion.ApplyPreference(false);
            UISettings.SetAnimationsEnabled(true);
            var page = new DetailPage();
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(page.StickyHeader);
                Assert.False(visual.Properties.TranslationRegistered);
                page.LoadHeader();
                page.DispatcherQueue.Drain();
                Assert.True(visual.Properties.TranslationRegistered);
                Assert.Null(Implicit.GetShowAnimations(page.StickyHeader));
                page.Scroll(120);
                Assert.True(page.Shown);
                Assert.Equal(0, Implicit.StartedShows(page.StickyHeader));
            }
            finally
            {
                page.ReleaseHeader();
                HomeMotion.ApplyPreference(true);
            }
        });
        Test("未上树平台延后注册时Reset主动建立Translation再停止", () =>
        {
            ElementCompositionPreview.DeferTranslationRegistration = true;
            var page = new DetailPage();
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(page.StickyHeader);
                Assert.False(visual.Properties.TranslationRegistered);
                page.ResetHeader();
                Assert.True(visual.Properties.TranslationRegistered);
                Assert.True(visual.Stopped.Contains("Translation"));
                Assert.Equal(Vector3.Zero, visual.Properties.Translation);
            }
            finally
            {
                page.ReleaseHeader();
                ElementCompositionPreview.DeferTranslationRegistration = false;
            }
        });
        Test("主播放按钮刚少于半切不出现，正好半切显示", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(119.999);
            Assert.False(f.Page.Shown);
            f.Page.Scroll(120);
            Assert.True(f.Page.Shown);
            Assert.Equal(Visibility.Visible, f.Page.StickyHeader.Visibility);
            Assert.True(f.Page.StickyHeader.IsHitTestVisible);
        });
        Test("按钮完全消失后仍提供浮层播放入口", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(300);
            Assert.True(f.Page.Shown);
            Assert.True(ReferenceEquals(f.Page.ViewModel.PlayCommand, f.Page.StickyPlayButton.Command));
            Assert.Equal(0, f.Page.ViewModel.PlayCommand.Executions);
        });
        Test("未向下滚动，即使按钮坐标负也不触发", () =>
        {
            using var f = new Fixture();
            f.Page.ColumnPlayButton.LayoutY = -100;
            f.Page.Scroll(0);
            Assert.False(f.Page.Shown);
            f.Page.Scroll(-1);
            Assert.False(f.Page.Shown);
        });
        Test("按钮还在视口底部之外不误触顶部卡片", () =>
        {
            using var f = new Fixture();
            f.Page.ColumnPlayButton.LayoutY = 900;
            f.Page.Scroll(10);
            Assert.False(f.Page.Shown);
        });
        Test("四DIP滞回抑制临界往返，超过阈值才退场", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.Scroll(119);
            f.Page.Scroll(116);
            Assert.True(f.Page.Shown);
            Assert.Equal(1, Implicit.StartedShows(f.Page.StickyHeader));
            f.Page.Scroll(115.999);
            Assert.False(f.Page.Shown);
            Assert.Equal(1, Implicit.StartedHides(f.Page.StickyHeader));
        });
        Test("小按钮滞回不超过高度的十分之一", () =>
        {
            using var f = new Fixture();
            f.Page.ColumnPlayButton.ActualHeight = 20;
            f.Page.Scroll(110);
            f.Page.Scroll(108);
            Assert.True(f.Page.Shown);
            f.Page.Scroll(107.999);
            Assert.False(f.Page.Shown);
        });
        Test("宽窄接入遵守IsCompact而不按窗口宽猜主播放按钮", () =>
        {
            using var f = new Fixture();
            f.Page.Body.ActualWidth = 360;
            f.Page.Scroll(150);
            Assert.True(f.Page.Shown, "窄窗商店布局仍用Column主播放按钮");
            Assert.Equal(0, f.Page.CompactPlayButton.TransformCalls);
            f.Page.ViewModel.IsCompact = true;
            f.Page.ViewModel.Notify(nameof(DetailViewModel.IsCompact));
            f.Drain();
            Assert.False(f.Page.Shown, "Compact按钮尚未滚到");
            Assert.Equal(1, f.Page.CompactPlayButton.TransformCalls);
            f.Page.Scroll(220);
            Assert.True(f.Page.Shown);
        });
        Test("量TransformToVisual真实位置，不使用静态Hero高度推阈值", () =>
        {
            using var f = new Fixture();
            f.Page.ColumnPlayButton.LayoutY = 341;
            f.Page.Scroll(360.5);
            Assert.False(f.Page.Shown);
            f.Page.Scroll(361);
            Assert.True(f.Page.Shown);
        });
        Test("显示中重复滚动和重复布局不会重播入场", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            for (var i = 0; i < 20; i++)
            {
                f.Page.Scroll(140 + i);
                f.Page.RaiseLayout();
            }
            f.Drain();
            Assert.Equal(1, Implicit.StartedShows(f.Page.StickyHeader));
            Assert.Equal(0, Implicit.StartedHides(f.Page.StickyHeader));
        });
        Test("退场中快速反向不重置Opacity和Translation起点", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.Scroll(110);
            var visual = ElementCompositionPreview.GetElementVisual(f.Page.StickyHeader);
            visual.Opacity = 0.45;
            visual.Properties.InsertVector3("Translation", new(0, -9, 0));
            var writes = visual.Properties.TranslationWrites;
            f.Page.Scroll(120);
            Assert.Equal(0.45, visual.Opacity);
            Assert.Equal(new Vector3(0, -9, 0), visual.Properties.Translation);
            Assert.Equal(writes, visual.Properties.TranslationWrites);
            Assert.Equal(2, Implicit.StartedShows(f.Page.StickyHeader));
            Assert.Equal(1, Implicit.StartedHides(f.Page.StickyHeader));
        });
        Test("附属读取Busy不会剥夺已经就绪的播放入口", () =>
        {
            using var f = new Fixture();
            f.Page.ViewModel.Busy = true;
            f.Page.ViewModel.IsReady = false;
            f.Page.Scroll(120);
            Assert.True(f.Page.Shown, "PlayCommand可用时Busy不是浮层能力条件");
            Assert.Equal(0, f.Page.ViewModel.PlayCommand.Executions);
        });
        Test("尚未确认播放目标的CanExecute拒绝悬浮入口", () =>
        {
            using var f = new Fixture();
            f.Page.ViewModel.PlayCommand.Allowed = false;
            f.Page.Scroll(120);
            Assert.False(f.Page.Shown);
            Assert.False(f.Page.StickyHeader.IsHitTestVisible);
        });
        Test("播放目标失效由CanExecuteChanged驱动立即收起并清动效", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.ViewModel.PlayCommand.Allowed = false;
            f.Page.ViewModel.PlayCommand.NotifyCanExecuteChanged();
            f.Drain();
            f.AssertHiddenWithoutAnimations();
            Assert.Equal(0, Implicit.StartedHides(f.Page.StickyHeader));
        });
        Test("隐藏播放区域的属性通知自动收起整个卡片", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.ViewModel.PlayVisibility = Visibility.Collapsed;
            f.Page.ViewModel.Notify(nameof(DetailViewModel.PlayVisibility));
            f.Drain();
            f.AssertHiddenWithoutAnimations();
        });
        Test("能力恢复时不需重新进入页面，滚动位置允许就重新显示", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.ViewModel.PlayCommand.Allowed = false;
            f.Page.ViewModel.PlayCommand.NotifyCanExecuteChanged();
            f.Drain();
            f.Page.ViewModel.PlayCommand.Allowed = true;
            f.Page.ViewModel.PlayCommand.NotifyCanExecuteChanged();
            f.Drain();
            Assert.True(f.Page.Shown);
            Assert.Equal(2, Implicit.StartedShows(f.Page.StickyHeader));
        });
        foreach (var invalid in new[] { "零视口", "零按钮宽", "零按钮高", "按钮收起", "页面断树" })
            Test(invalid + "不提供悬浮播放入口", () =>
            {
                using var f = new Fixture();
                f.Page.Scroll(120);
                switch (invalid)
                {
                    case "零视口": f.Page.Body.ActualHeight = 0; break;
                    case "零按钮宽": f.Page.ColumnPlayButton.ActualWidth = 0; break;
                    case "零按钮高": f.Page.ColumnPlayButton.ActualHeight = 0; break;
                    case "按钮收起": f.Page.ColumnPlayButton.Visibility = Visibility.Collapsed; break;
                    case "页面断树": f.Page.XamlRoot = null; break;
                }
                f.Page.UpdateHeader();
                f.AssertHiddenWithoutAnimations();
            });
        Test("布局变换被平台拒绝时收起，异常不逃逸到Dispatcher", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.ColumnPlayButton.ThrowOnTransform = true;
            f.Page.UpdateHeader();
            f.AssertHiddenWithoutAnimations();
        });
        foreach (var error in new Exception[] { new ArgumentException("Disconnected target"), new COMException("Element closed", unchecked((int)0x80000013)) })
            Test("平台" + error.GetType().Name + "拒绝变换时安全收起", () =>
            {
                using var f = new Fixture();
                f.Page.Scroll(120);
                f.Page.ColumnPlayButton.TransformError = error;
                f.Page.RaiseLayout();
                f.Drain();
                f.AssertHiddenWithoutAnimations();
            });
        Test("播放按钮已离开当前XamlRoot时不跨树测量", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            var calls = f.Page.ColumnPlayButton.TransformCalls;
            f.Page.ColumnPlayButton.XamlRoot = new();
            f.Page.UpdateHeader();
            f.AssertHiddenWithoutAnimations();
            Assert.Equal(calls, f.Page.ColumnPlayButton.TransformCalls);
        });
        Test("一批LayoutUpdated和属性通知仅排一次更新", () =>
        {
            using var f = new Fixture();
            var before = f.Page.DispatcherQueue.EnqueueAttempts;
            for (var i = 0; i < 100; i++)
            {
                f.Page.RaiseLayout();
                f.Page.ViewModel.Notify(nameof(DetailViewModel.PageWidth));
            }
            Assert.Equal(1, f.Page.DispatcherQueue.PendingCount);
            Assert.Equal(before + 1, f.Page.DispatcherQueue.EnqueueAttempts);
            f.Drain();
            Assert.False(f.Page.Queued);
        });
        Test("Dispatcher拒绝入队后下一次布局可恢复", () =>
        {
            using var f = new Fixture();
            f.Page.DispatcherQueue.RejectEnqueue = true;
            f.Page.Body.VerticalOffset = 120;
            f.Page.RaiseLayout();
            Assert.False(f.Page.Queued);
            Assert.False(f.Page.Shown);
            f.Page.DispatcherQueue.RejectEnqueue = false;
            f.Page.RaiseLayout();
            f.Drain();
            Assert.True(f.Page.Shown);
        });
        Test("重入Loaded幂等，不增加VM命令和布局订阅", () =>
        {
            using var f = new Fixture();
            f.Page.LoadHeader();
            f.Page.LoadHeader();
            Assert.Equal(1, f.Page.ViewModel.Subscriptions);
            Assert.Equal(1, f.Page.ViewModel.PlayCommand.Subscriptions);
            Assert.Equal(1, f.Page.LayoutSubscriptions);
            Assert.Equal(1, HomeMotion.Subscriptions);
            Assert.Equal(1, UISettings.Created.Sum(settings => settings.Subscriptions));
        });
        Test("Release不用等Unloaded就清理卡片和所有外部订阅", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.ReleaseHeader();
            f.AssertStopped();
            Assert.NotNull(f.Page.XamlRoot, "仍在树上的Release同样要收尾");
        });
        Test("真Unloaded收尾后旧属性通知不再排工作", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.RaiseUnloaded();
            f.AssertStopped();
            f.Drain();
            f.Page.ViewModel.Notify(nameof(DetailViewModel.IsCompact));
            f.Page.ViewModel.PlayCommand.NotifyCanExecuteChanged();
            Assert.Equal(0, f.Page.DispatcherQueue.PendingCount);
        });
        Test("离页后的旧排队更新不能重新打开卡片", () =>
        {
            using var f = new Fixture();
            f.Page.Body.VerticalOffset = 120;
            f.Page.RaiseLayout();
            var stale = f.Page.DispatcherQueue.TakeNext();
            f.Page.ReleaseHeader();
            stale();
            f.AssertStopped();
        });
        Test("离页再重入的旧回调不能清掉新页面更新票", () =>
        {
            using var f = new Fixture();
            f.Page.RaiseLayout();
            var stale = f.Page.DispatcherQueue.TakeNext();
            f.Page.ReleaseHeader();
            f.Page.Body.VerticalOffset = 120;
            f.Page.LoadHeader();
            Assert.True(f.Page.Queued);
            stale();
            Assert.True(f.Page.Queued, "旧代不能更改新代排队标记");
            f.Drain();
            Assert.True(f.Page.Shown);
            Assert.Equal(1, f.Page.ViewModel.Subscriptions);
        });
        Test("回滚收起时只把卡片播放焦点交还原播放按钮", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.StickyPlayButton.Focus(FocusState.Keyboard);
            f.Page.Scroll(110);
            Assert.True(ReferenceEquals(f.Page.ColumnPlayButton, FocusManager.GetFocusedElement(f.Page.XamlRoot!)));
            Assert.Equal(1, f.Page.ColumnPlayButton.FocusCalls);
            Assert.False(f.Page.StickyHeader.IsHitTestVisible);
        });
        Test("卡片没焦点时收起不抢别的操作焦点", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            f.Page.XamlRoot!.FocusedElement = f.Page.Body;
            f.Page.Scroll(110);
            Assert.True(ReferenceEquals(f.Page.Body, FocusManager.GetFocusedElement(f.Page.XamlRoot)));
            Assert.Equal(0, f.Page.ColumnPlayButton.FocusCalls);
        });
        foreach (var unavailable in new[] { "收起", "禁用" })
            Test("原播放按钮" + unavailable + "时收起卡片不强行给它焦点", () =>
            {
                using var f = new Fixture();
                f.Page.Scroll(120);
                f.Page.StickyPlayButton.Focus(FocusState.Keyboard);
                if (unavailable == "收起") f.Page.ColumnPlayButton.Visibility = Visibility.Collapsed;
                else f.Page.ColumnPlayButton.IsEnabled = false;
                f.Page.Scroll(110);
                Assert.Equal(0, f.Page.ColumnPlayButton.FocusCalls);
                Assert.False(f.Page.StickyHeader.IsHitTestVisible);
            });
        Test("紧凑布局退场焦点回给Compact主播放按钮", () =>
        {
            using var f = new Fixture();
            f.Page.ViewModel.IsCompact = true;
            f.Page.Scroll(220);
            f.Page.StickyPlayButton.Focus(FocusState.Keyboard);
            f.Page.Scroll(210);
            Assert.Equal(1, f.Page.CompactPlayButton.FocusCalls);
            Assert.Equal(0, f.Page.ColumnPlayButton.FocusCalls);
        });
        foreach (var system in new[] { false, true })
            Test("关闭" + (system ? "系统" : "应用") + "动画当场清动画但保留当前显隐", () =>
            {
                using var f = new Fixture();
                f.Page.Scroll(120);
                var visual = ElementCompositionPreview.GetElementVisual(f.Page.StickyHeader);
                visual.Opacity = 0.5;
                visual.Properties.InsertVector3("Translation", new(0, -12, 0));
                if (system) UISettings.SetAnimationsEnabled(false);
                else HomeMotion.ApplyPreference(false);
                f.Drain();
                Assert.True(f.Page.Shown);
                Assert.Equal(Visibility.Visible, f.Page.StickyHeader.Visibility);
                Assert.Null(Implicit.GetShowAnimations(f.Page.StickyHeader));
                Assert.Null(Implicit.GetHideAnimations(f.Page.StickyHeader));
                Assert.Equal(1d, visual.Opacity);
                Assert.Equal(Vector3.Zero, visual.Properties.Translation);
                Assert.True(visual.Stopped.SetEquals(["Opacity", "Translation"]));
                f.Page.Scroll(110);
                Assert.False(f.Page.Shown);
                Assert.Equal(0, Implicit.StartedHides(f.Page.StickyHeader));
            });
        foreach (var system in new[] { false, true })
            Test("动画预先关闭的" + (system ? "系统" : "应用") + "偏好下仍能立即开合", () =>
            {
                using var f = new Fixture();
                if (system) UISettings.SetAnimationsEnabled(false);
                else HomeMotion.ApplyPreference(false);
                f.Drain();
                f.Page.Scroll(120);
                Assert.True(f.Page.Shown);
                Assert.Equal(0, Implicit.StartedShows(f.Page.StickyHeader));
                Assert.Equal(1d, ElementCompositionPreview.GetElementVisual(f.Page.StickyHeader).Opacity);
                f.Page.Scroll(110);
                Assert.False(f.Page.Shown);
            });
        Test("显示中重新启用动画不重新播放入场或清零内容", () =>
        {
            using var f = new Fixture();
            HomeMotion.ApplyPreference(false);
            f.Page.Scroll(120);
            HomeMotion.ApplyPreference(true);
            f.Page.UpdateHeader();
            Assert.True(f.Page.Shown);
            Assert.Equal(0, Implicit.StartedShows(f.Page.StickyHeader));
            Assert.Equal(1d, ElementCompositionPreview.GetElementVisual(f.Page.StickyHeader).Opacity);
        });
        Test("系统偏好旧回调在离页重入后不能覆盖新状态", () =>
        {
            using var f = new Fixture();
            UISettings.SetAnimationsEnabled(false);
            var stale = f.Page.DispatcherQueue.TakeNext();
            f.Page.ReleaseHeader();
            UISettings.SetAnimationsEnabled(true);
            f.Page.LoadHeader();
            f.Drain();
            f.Page.Scroll(120);
            var show = Implicit.GetShowAnimations(f.Page.StickyHeader);
            stale();
            Assert.True(ReferenceEquals(show, Implicit.GetShowAnimations(f.Page.StickyHeader)));
            Assert.True(f.Page.Shown);
        });
        Test("Gallery进退场参数可追踪且From留空支持反向连续", () =>
        {
            using var f = new Fixture();
            f.Page.Scroll(120);
            var show = Implicit.GetShowAnimations(f.Page.StickyHeader)!;
            var hide = Implicit.GetHideAnimations(f.Page.StickyHeader)!;
            Assert.Equal(2, show.Count);
            Assert.Equal(2, hide.Count);
            var enterTranslation = (TranslationAnimation)show[0];
            var enterOpacity = (OpacityAnimation)show[1];
            var exitTranslation = (TranslationAnimation)hide[0];
            var exitOpacity = (OpacityAnimation)hide[1];
            Assert.Equal(TimeSpan.FromMilliseconds(400), enterTranslation.Duration);
            Assert.Equal(TimeSpan.FromMilliseconds(200), enterOpacity.Duration);
            Assert.Equal(TimeSpan.FromMilliseconds(200), exitTranslation.Duration);
            Assert.Equal(TimeSpan.FromMilliseconds(100), exitOpacity.Duration);
            Assert.Null(enterTranslation.From);
            Assert.Null(exitTranslation.From);
            Assert.Null(enterOpacity.From);
            Assert.Null(exitOpacity.From);
            Assert.Equal("0", enterTranslation.To);
            Assert.Equal("0,-24,0", exitTranslation.To);
        });
        Test("360DIP隐藏缩略海报和按钮文字，恢复520DIP时还原", () =>
        {
            using var f = new Fixture();
            f.Page.Body.ActualWidth = 360;
            f.Page.Scroll(120);
            Assert.Equal(new Thickness(12, 12, 12, 0), f.Page.StickyHeader.Margin);
            Assert.Equal(new Thickness(12), f.Page.StickyHeader.Padding);
            Assert.Equal(8d, f.Page.StickyHeaderContent.ColumnSpacing);
            Assert.Equal(Visibility.Collapsed, f.Page.StickyPoster.Visibility);
            Assert.Equal(Visibility.Collapsed, f.Page.StickyPlayText.Visibility);
            f.Page.Body.ActualWidth = 519.999;
            f.Page.UpdateHeader();
            Assert.Equal(Visibility.Collapsed, f.Page.StickyPoster.Visibility);
            f.Page.Body.ActualWidth = 520;
            f.Page.UpdateHeader();
            Assert.Equal(new Thickness(32, 12, 32, 0), f.Page.StickyHeader.Margin);
            Assert.Equal(16d, f.Page.StickyHeaderContent.ColumnSpacing);
            Assert.Equal(Visibility.Visible, f.Page.StickyPoster.Visibility);
            Assert.Equal(Visibility.Visible, f.Page.StickyPlayText.Visibility);
        });
        Test("窄窗晚到封面通知不重新挤出标题，空图恢复也不留海报空白", () =>
        {
            using var f = new Fixture();
            f.Page.Body.ActualWidth = 360;
            f.Page.Scroll(120);
            f.Page.ViewModel.StillImage = new();
            f.Page.ViewModel.Notify(nameof(DetailViewModel.StillImage));
            f.Drain();
            Assert.Equal(Visibility.Collapsed, f.Page.StickyPoster.Visibility);
            f.Page.ViewModel.StillImage = null;
            f.Page.Body.ActualWidth = 600;
            f.Page.UpdateHeader();
            Assert.Equal(Visibility.Collapsed, f.Page.StickyPoster.Visibility);
        });
        Test("海报和副标题空态规则不制造占位内容", () =>
        {
            Assert.Equal(Visibility.Collapsed, DetailPage.HeaderPosterVisibility(null));
            Assert.Equal(Visibility.Visible, DetailPage.HeaderPosterVisibility(new ImageSource()));
            foreach (var empty in new[] { null, "", "  " })
                Assert.Equal(Visibility.Collapsed, DetailPage.HeaderSubtitleVisibility(empty));
            Assert.Equal(Visibility.Visible, DetailPage.HeaderSubtitleVisibility("S2:E7 - 测试"));
        });
        Test("XAML卡片与正文同级，浮层不重排滚动内容", () =>
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            var header = Named(xaml, "StickyHeader");
            var body = Named(xaml, "Body");
            Assert.True(ReferenceEquals(header.Parent, body.Parent));
            Assert.Equal("Collapsed", header.Attribute("Visibility")?.Value);
            Assert.Equal("False", header.Attribute("IsHitTestVisible")?.Value);
            Assert.Equal("Top", header.Attribute("VerticalAlignment")?.Value);
            Assert.Equal("{ThemeResource AcrylicInAppFillColorDefaultBrush}", header.Attribute("Background")?.Value);
        });
        Test("XAML缩略资料直接复用原详情绑定和播放命令", () =>
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            Assert.Equal("{x:Bind ViewModel.StillImage, Mode=OneWay}", Named(xaml, "StickyPoster").Attribute("Source")?.Value);
            var header = Named(xaml, "StickyHeader");
            Assert.True(header.Descendants().Any(element => element.Attribute("Text")?.Value == "{x:Bind ViewModel.Title, Mode=OneWay}"));
            Assert.True(header.Descendants().Any(element => element.Attribute("Text")?.Value == "{x:Bind ViewModel.Subline, Mode=OneWay}"
                && element.Attribute("Visibility")?.Value.Contains("HeaderSubtitleVisibility", StringComparison.Ordinal) == true));
            foreach (var name in new[] { "ColumnPlayButton", "CompactPlayButton", "StickyPlayButton" })
                Assert.Equal("{x:Bind ViewModel.PlayCommand}", Named(xaml, name).Attribute("Command")?.Value);
        });
        Test("XAML图标播放仍保留实时可访问名称和提示", () =>
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            var play = Named(xaml, "StickyPlayButton");
            Assert.Equal("DetailStickyPlayButton", play.Attribute("AutomationProperties.AutomationId")?.Value);
            Assert.Equal("{x:Bind ViewModel.PlayText, Mode=OneWay}", play.Attribute("AutomationProperties.Name")?.Value);
            Assert.Equal("{x:Bind ViewModel.PlayText, Mode=OneWay}", play.Attribute("ToolTipService.ToolTip")?.Value);
            Assert.Equal("{x:Bind ViewModel.PlayVisibility, Mode=OneWay}", play.Attribute("Visibility")?.Value);
        });
        Test("页面入口实际接入初始化释放和滚动更新", () =>
        {
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "DetailPage.xaml.cs"));
            Assert.Contains("PrepareStickyHeader();", Section(code, "    public DetailPage()", "    private void OnThemeChanged"));
            Assert.Contains("StopStickyHeader();", Section(code, "    public void Release()", "    private void PaintOtherInfo()"));
            Assert.Contains("UpdateStickyHeader();", Section(code, "    private void OnBodyViewChanged", "    private void PaintWash"));
        });
        return Run();
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到当前测试产物所属工作树。");
    }

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == name);

    private static string Section(string source, string start, string end)
    {
        var first = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(first >= 0, "生产源码缺失起始边界：" + start);
        Assert.True(source.IndexOf(start, first + start.Length, StringComparison.Ordinal) < 0, "生产源码起始边界重复：" + start);
        var last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(last > first, "生产源码缺失结束边界：" + end);
        return source[first..last];
    }

    private sealed class Fixture : IDisposable
    {
        public DetailPage Page { get; }
        public Fixture()
        {
            HomeMotion.ApplyPreference(true);
            UISettings.SetAnimationsEnabled(true);
            Page = new();
            Page.LoadHeader();
            Drain();
        }
        public void Drain() => Page.DispatcherQueue.Drain();
        public void AssertHiddenWithoutAnimations()
        {
            Assert.False(Page.Shown);
            Assert.Equal(Visibility.Collapsed, Page.StickyHeader.Visibility);
            Assert.False(Page.StickyHeader.IsHitTestVisible);
            Assert.Null(Implicit.GetShowAnimations(Page.StickyHeader));
            Assert.Null(Implicit.GetHideAnimations(Page.StickyHeader));
        }
        public void AssertStopped()
        {
            AssertHiddenWithoutAnimations();
            Assert.False(Page.Active);
            Assert.Equal(0, Page.ViewModel.Subscriptions);
            Assert.Equal(0, Page.ViewModel.PlayCommand.Subscriptions);
            Assert.Equal(0, Page.LayoutSubscriptions);
            Assert.Equal(0, HomeMotion.Subscriptions);
            Assert.Equal(0, UISettings.Created.Sum(settings => settings.Subscriptions));
        }
        public void Dispose()
        {
            Page.ReleaseHeader();
            Drain();
        }
    }
}
