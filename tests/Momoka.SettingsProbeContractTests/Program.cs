using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is ["core-settings", var tests, var coreRoot]) return RunCoreSettings(tests, coreRoot);
        if (args is not [var shell, var root])
        {
            Console.Error.WriteLine("需要本树当前 Release Shell DLL 与工作树绝对路径；不运行 App 或桌面 UI。");
            return 2;
        }
        VerifyProductionInputs();
        var runtimeShell = ShellFixtureInput(shell, root);
        Console.WriteLine("离线 VM 来源：" + Path.GetFullPath(shell) + "；SHA256=" + HashFile(shell));
        Console.WriteLine("生产源码输入：" + SettingsProbeInputs.SourceRoot);
        foreach (var input in SettingsProbeInputs.Sha256) Console.WriteLine(input.Key + " SHA256=" + input.Value);
        _ = new ShellRuntime(runtimeShell, root);
        Console.WriteLine("Settings 探针活动期／动效契约；实际 VM 和原样页面／行方法，无真实 UI／网络／注册表／播放。");
        Register();
        return TestHarness.Run();
    }

    private static void Register()
    {
        Test("旧 VM-only 夹具没有开启页面活动期，管理刷新确实不执行", () => WithPage((context, fixture, page) =>
        {
            Assert.False(page.Active);
            context.Await(page.RefreshForContract());
            Assert.Equal(0, fixture.Reads.PluginReads);
            SameStrings(["Momoka"], GroupNames(page));
        }));
        Test("旧 VM-only 夹具的分类事件和 Loaded 均不开始入场", () => WithPage((_, _, page) =>
        {
            page.RaiseLoaded();
            page.SelectedCategory = "界面";
            Assert.Equal(0, page.DispatcherQueue.Count);
            Assert.Equal(0, page.HostedReads);
        }));
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test($"补齐活动期后实际 {kind} 管理目录显隐→恢复", () => WithPage((context, fixture, page) =>
            {
                ShellNavigationProbe.BeginVisitForContract(page);
                context.Await(page.RefreshForContract());
                var expected = kind == MediaServerKind.Emby
                    ? new[] { "Momoka", "Emby Server", "设备", "高级" }
                    : new[] { "Momoka", "服务器", "设备", "电视直播", "插件", "高级" };
                SameStrings(expected, GroupNames(page));
                Assert.True(fixture.Reads.PluginReads > 0);
                fixture.Permission(fixture.Source("a"), false);
                context.Await(page.RefreshForContract());
                SameStrings(["Momoka"], GroupNames(page));
                fixture.Permission(fixture.Source("a"), true);
                context.Await(page.RefreshForContract());
                SameStrings(expected, GroupNames(page));
                Assert.True(page.CategorySourceWrites >= 4);
            }, kind));
        }
        Test("真实多来源目录按服务器排列，默认收起；Jellyfin撤权隐藏、飞牛仍保留帐号", () => WithPage((context, fixture, page) =>
        {
            _ = fixture.Add("server-b", "b", MediaServerKind.Jellyfin);
            _ = fixture.Add("server-c", "c", MediaServerKind.FnNas);
            ShellNavigationProbe.BeginVisitForContract(page);
            context.Await(page.RefreshForContract());
            var sources = fixture.Capabilities.Sources.OrderBy(source => source.ServerProfileId, StringComparer.Ordinal).ToArray();
            SameStrings(new[] { "Momoka" }.Concat(sources.Select(source => source.Label)), GroupNames(page));
            var groups = Groups(page).Skip(1).ToArray();
            Assert.True(groups.All(group => ShellRuntime.Current.Get<bool>(group, "Collapsible")));
            Assert.True(groups.All(group => !ShellRuntime.Current.Get<bool>(group, "IsExpanded")));
            fixture.Permission(fixture.Source("b"), false);
            context.Await(page.RefreshForContract());
            Assert.Equal(3, Groups(page).Length);
            fixture.Permission(fixture.Source("c"), false);
            context.Await(page.RefreshForContract());
            var fnNas = Groups(page).Single(group => ShellRuntime.Current.Get<string?>(group, "SourceKey") == fixture.Source("c").Key);
            var entries = ShellRuntime.Current.Get<IEnumerable>(fnNas, "Entries").Cast<object>();
            SameStrings(["修改密码", "播放偏好", "外观"], entries.Select(entry => ShellRuntime.Current.Get<string>(entry, "Label")));
            fixture.Permission(fixture.Source("b"), true);
            context.Await(page.RefreshForContract());
            Assert.Equal(4, Groups(page).Length);
        }, MediaServerKind.Emby));
        Test("真实默认守卫拒绝旧刷新；Hide后迟到插件不换源，重开可恢复", () => WithPage((context, fixture, page) =>
        {
            ShellNavigationProbe.BeginVisitForContract(page);
            var pending = fixture.Reads.HoldPlugin("a");
            var refresh = page.RefreshForContract();
            context.PumpUntil(() => fixture.Reads.Started(pending));
            page.ReleaseHosted();
            var writes = page.CategorySourceWrites;
            pending.Complete("obsolete-plugin");
            context.Await(refresh);
            Assert.Equal(writes, page.CategorySourceWrites);
            Assert.False(fixture.Labels.Contains("obsolete-plugin"));
            ShellNavigationProbe.BeginVisitForContract(page);
            context.Await(page.RefreshForContract());
            Assert.True(page.Active);
            Assert.True(page.CategorySourceWrites > writes);
        }));
        Test("旧 fake 空Sources是合法单服契约，补活动期后Emby三组准确出现", () => WithPage((context, fixture, page) =>
        {
            var capabilities = ShellNavigationProbe.NewFakeCapabilitiesForContract();
            Assert.Equal(0, capabilities.Sources.Count);
            ShellRuntime.Current.Attach(fixture.Vm, fixture.Settings, fixture.Paths, capabilities);
            ShellNavigationProbe.BeginVisitForContract(page);
            context.Await(page.RefreshForContract());
            SameStrings(["Momoka", "Emby Server", "设备", "高级"], GroupNames(page));
        }));
        Test("原多服fake取真实Sources，产品种类由capability指定、在线身份仍受Scope守卫", () => WithPage((context, fixture, page) =>
        {
            // The real Shell probe restored all three via its default Emby SelectAsync, then provided
            // mixed product kinds in FakeCapabilities. Do not silently reinterpret those data inputs.
            var b = fixture.Add("server-b", "b", MediaServerKind.Emby);
            var c = fixture.Add("server-c", "c", MediaServerKind.Emby);
            var capabilities = ShellNavigationProbe.NewFakeCapabilitiesForContract();
            var sources = new[]
            {
                new Momoka.Services.ServerSettingsSource(fixture.Source("a"), MediaServerKind.Emby, true),
                new Momoka.Services.ServerSettingsSource(b, MediaServerKind.Jellyfin, true),
                new Momoka.Services.ServerSettingsSource(c, MediaServerKind.FnNas, true)
            };
            SetCapability(capabilities, "InterfaceFlavor", ServerInterfaceFlavor.Generic);
            SetCapability(capabilities, "Sources", sources);
            ShellRuntime.Current.Attach(fixture.Vm, fixture.Settings, fixture.Paths, capabilities);
            ShellNavigationProbe.BeginVisitForContract(page);
            context.Await(page.RefreshForContract());
            SameStrings(new[] { "Momoka" }.Concat(sources.Select(source => source.Label)), GroupNames(page));
            Assert.True(sources.All(source => source.IsAvailable));
            Assert.Equal(1, sources.Count(source => source.IsCurrent));
            var scoped = Groups(page).Skip(1).Select(group => ShellRuntime.Current.Get<string?>(group, "SourceKey"));
            SameStrings(sources.Select(source => source.Key), scoped.Select(key => key!));
            SetCapability(capabilities, "Sources", new[] { sources[0], sources[1] with { IsAdministrator = false }, sources[2] });
            context.Await(page.RefreshForContract());
            SameStrings(["Momoka", sources[0].Label, sources[2].Label], GroupNames(page));
            SetCapability(capabilities, "Sources", sources);
            context.Await(page.RefreshForContract());
            Assert.Equal(4, Groups(page).Length);
        }, MediaServerKind.Emby));
        Test("开始观察发生在父层恢复一刻，完成后不会把结束态当无动画", () => WithPage((context, _, page) =>
        {
            page.RaiseLoaded();
            var section = page.AddEntranceSection();
            ShellNavigationProbe.BeginVisitForContract(page);
            ShellNavigationProbe.ContractPage = page;
            ShellNavigationProbe.CompleteBeforeWaitReturns = true; // 先送开始回调，时钟完成后才恢复探针 continuation。
            context.Await(ShellNavigationProbe.CheckEntrance(page, 1100));
            Assert.Equal(1d, section.Opacity);
            Assert.True(section.RenderTransform is TranslateTransform { Y: 0 });
        }));
        foreach (var width in new[] { 1100, 760 })
        {
            Test($"原探针淡入和上浮判据实际开始／完成读取（契约宽{width}）", () => WithPage((context, _, page) =>
            {
                page.RaiseLoaded();
                var section = page.AddEntranceSection();
                ShellNavigationProbe.BeginVisitForContract(page);
                ShellNavigationProbe.ContractPage = page;
                context.Await(ShellNavigationProbe.CheckEntrance(page, width));
                Assert.Equal(0d, section.Opacity);
                Assert.True(section.RenderTransform is TranslateTransform { Y: > 0 });
                Assert.Equal(1d, page.SettingsLayout.Opacity);
                Assert.Null(page.SettingsContent.RenderTransform);
                Storyboard.CompleteAll();
                Assert.Equal(1d, section.Opacity);
                Assert.True(section.RenderTransform is TranslateTransform { Y: 0 });
            }));
            Test($"没有实际可入场分组时原动画判据仍失败（契约宽{width}）", () => WithPage((context, _, page) =>
            {
                page.RaiseLoaded();
                ShellNavigationProbe.BeginVisitForContract(page);
                ShellNavigationProbe.ContractPage = page;
                var error = Assert.Catch<InvalidOperationException>(() => context.Await(ShellNavigationProbe.CheckEntrance(page, width)));
                Assert.Contains("设置分组没有实际淡入和上浮", error.Message);
            }));
            Test($"原快速开关判据：关闭动画当场清理高度／裁剪／队列（契约宽{width}）", () => WithScreenshot((context, fixture, page, section, rows) =>
            {
                ShellNavigationProbe.BeginVisitForContract(page);
                var toggle = new ToggleSwitch { Toggled = () => ToggleLossless(fixture.Vm) };
                var surface = (Border)VisualTreeHelper.GetParent(rows)!;
                surface.Height = 100;
                surface.Clip = new();
                context.Await(ShellNavigationProbe.CheckRapidToggle(page, rows, toggle, fixture.Settings.Settings.Mpv));
                Assert.True(page.IsContentMotionSettled);
                Assert.False(page.HasConditionalTransitions(rows));
                Assert.True(double.IsNaN(surface.Height));
                Assert.Null(surface.Clip);
                Assert.Equal(73, fixture.Settings.Settings.Mpv.ScreenshotWebpQuality);
                page.DrainRows();
                Assert.True(page.IsContentMotionSettled, "禁动画前排队的回调不能重新启动");
            }));
            Test($"旧未接线夹具的原快速开关判据仍失败（契约宽{width}）", () => WithScreenshot((context, fixture, page, _, rows) =>
            {
                // Rows now owns its preference observer. Missing the page visit alone is no
                // longer an unwired driver: omit the actual rows Loaded handler and keep the
                // stale layout readings that the original production Require must still reject.
                Assert.False(page.HasConditionalTransitions(rows));
                var surface = (Border)VisualTreeHelper.GetParent(rows)!;
                surface.Height = 100;
                surface.Clip = new();
                var toggle = new ToggleSwitch { Toggled = () => ToggleLossless(fixture.Vm) };
                var error = Assert.Catch<InvalidOperationException>(() => context.Await(ShellNavigationProbe.CheckRapidToggle(page, rows, toggle, fixture.Settings.Settings.Mpv)));
                Assert.Contains("卡片没有恢复自适应高度或裁剪残留", error.Message);
            }, attachRows: false));
        }
        Test("实际落定等待不会把有尺寸但未Loaded的页算完成", () => WithPage((context, _, page) =>
        {
            Assert.False(page.IsLoaded);
            Assert.True(page.ActualWidth > 0 && page.ActualHeight > 0);
            Assert.Throws<TimeoutException>(() => context.Await(ShellNavigationProbe.WaitForSettingsSettledForContract(page)));
            page.RaiseLoaded();
            context.Await(ShellNavigationProbe.WaitForSettingsSettledForContract(page));
        }));
        Test("行的IsLoaded先到而真实Loaded handler未接线时必须继续等待", () => WithPage((context, fixture, page) =>
        {
            fixture.Settings.Settings.Mpv.ScreenshotFormat = "webp";
            var section = ShellRuntime.Current.Invoke(fixture.Vm, "ScreenshotCard")!;
            ShellRuntime.Current.List(fixture.Vm, "Sections").Add(section);
            page.SelectedCategory = "播放行为";
            var rows = page.AddRows(section, attach: false);
            Assert.True(rows.IsLoaded);
            Assert.Throws<TimeoutException>(() => context.Await(ShellNavigationProbe.WaitForSettingsRowsForContract(page, rows)));
            page.AttachRowsForContract(rows);
            context.Await(ShellNavigationProbe.WaitForSettingsRowsForContract(page, rows));
            Assert.True(page.HasConditionalTransitions(rows));
        }));
        Test("真实截图卡原地更新WebP行，格式与无损恢复原偏好、上级对象保留", () => WithScreenshot((_, fixture, page, section, rows) =>
        {
            ShellNavigationProbe.BeginVisitForContract(page);
            var collection = ShellRuntime.Current.Get<object>(section, "Rows");
            var control = Row(fixture.Vm, "WebP 无损");
            ToggleLossless(fixture.Vm);
            Assert.False(Labels(section).Contains("WebP 质量"));
            Assert.False(Labels(section).Contains("WebP 压缩"));
            Assert.True(page.HasConditionalMotion(rows));
            Assert.True(ReferenceEquals(control, Row(fixture.Vm, "WebP 无损")));
            ToggleLossless(fixture.Vm);
            Assert.True(Labels(section).Contains("WebP 质量"));
            Assert.True(ReferenceEquals(collection, ShellRuntime.Current.Get<object>(section, "Rows")));
            fixture.Settings.Settings.Mpv.ScreenshotFormat = "png";
            ShellRuntime.Current.Invoke(fixture.Vm, "RefreshScreenshotCard");
            Assert.False(Labels(section).Any(label => label.StartsWith("WebP", StringComparison.Ordinal)));
            fixture.Settings.Settings.Mpv.ScreenshotFormat = "webp";
            ShellRuntime.Current.Invoke(fixture.Vm, "RefreshScreenshotCard");
            Assert.True(Labels(section).Contains("WebP 质量"));
            Assert.Equal(73, fixture.Settings.Settings.Mpv.ScreenshotWebpQuality);
            Assert.Equal(2, fixture.Settings.Settings.Mpv.ScreenshotWebpCompression);
        }));
        Test("Hide仍Loaded后重入不替换草稿；重复重入不重复订阅分类或动效", () => WithPage((_, fixture, page) =>
        {
            var sections = ShellRuntime.Current.Get<object>(fixture.Vm, "Sections");
            page.RaiseLoaded();
            ShellNavigationProbe.BeginVisitForContract(page);
            page.SelectedCategory = "界面";
            var count = page.HostedReads;
            page.ReleaseHosted();
            Assert.True(page.IsLoaded);
            page.SelectedCategory = "字幕";
            Assert.Equal(count, page.HostedReads);
            ShellNavigationProbe.BeginVisitForContract(page);
            ShellNavigationProbe.BeginVisitForContract(page);
            var listeners = (Delegate?)typeof(HomeMotion).GetField("PreferenceChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            Assert.Equal(1, listeners!.GetInvocationList().Count(callback => ReferenceEquals(callback.Target, page)));
            page.SelectedCategory = "视频输出";
            Assert.Equal(count + 1, page.HostedReads);
            Assert.True(ReferenceEquals(sections, ShellRuntime.Current.Get<object>(fixture.Vm, "Sections")));
        }));
        Test("分类切换复位滚动，同页重开保留位置，托管页也使用短促刷新", () => WithPage((_, _, page) =>
        {
            page.RaiseLoaded();
            ShellNavigationProbe.BeginVisitForContract(page);
            page.AddEntranceSection();
            page.SelectedCategory = "界面";
            page.DispatcherQueue.Drain();
            var resets = page.SettingsScroll.ScrollResets;
            Assert.True(resets > 0);
            page.QueueEntranceForContract();
            page.DispatcherQueue.Drain();
            Assert.Equal(resets, page.SettingsScroll.ScrollResets);
            page.SelectedCategory = "字幕";
            page.DispatcherQueue.Drain();
            Assert.Equal(resets + 1, page.SettingsScroll.ScrollResets);
            Assert.True(SettingsPage.PageTransitionDurationMs <= 200);
            var hosted = new FrameworkElement { XamlRoot = new() };
            page.AddVisual(hosted);
            HomeMotion.Reveal(hosted);
            Assert.True(hosted.RenderTransform is TranslateTransform { Y: SettingsPage.PageTransitionDistance });
            var hostedBoard = Storyboard.Active.Single(board => board.Children.Any(animation => ReferenceEquals(animation.Target, hosted)));
            Assert.True(hostedBoard.Children.All(animation => animation.Duration.TimeSpan == TimeSpan.FromMilliseconds(180) && animation.BeginTime == TimeSpan.Zero));
            HomeMotion.Stop(hosted);
            var ordinary = new FrameworkElement { XamlRoot = new() };
            HomeMotion.Reveal(ordinary);
            var ordinaryBoard = Storyboard.Active.Single(board => board.Children.Any(animation => ReferenceEquals(animation.Target, ordinary)));
            Assert.True(ordinaryBoard.Children.All(animation => animation.Duration.TimeSpan == TimeSpan.FromMilliseconds((int)typeof(HomeMotion).GetField("DurationMs", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!)));
            HomeMotion.Stop(ordinary);
            page.ReleaseHosted();
        }));
        Test("隐藏前排队分类动画在Hide后失效；重入新分类正常开始", () => WithPage((_, _, page) =>
        {
            page.RaiseLoaded();
            var section = page.AddEntranceSection();
            ShellNavigationProbe.BeginVisitForContract(page);
            page.SelectedCategory = "界面";
            page.ReleaseHosted();
            page.DispatcherQueue.Drain();
            Assert.Equal(1d, section.Opacity);
            Assert.Equal(0, Storyboard.Count);
            ShellNavigationProbe.BeginVisitForContract(page);
            page.SelectedCategory = "字幕";
            page.DispatcherQueue.Drain();
            Assert.Equal(0d, section.Opacity);
            Assert.True(section.RenderTransform is TranslateTransform { Y: > 0 });
        }));
        Test("Drain前重复排队和Loaded仍保留唯一分类滚动复位", () => WithPage((_, _, page) =>
        {
            ShellNavigationProbe.BeginVisitForContract(page);
            page.AddEntranceSection();
            page.SelectedCategory = "界面";
            page.RaiseLoaded();
            page.QueueEntranceForContract();
            page.RaiseLoaded();
            Assert.Equal(0, page.SettingsScroll.ScrollResets);
            Assert.True(page.DispatcherQueue.Count >= 3);
            page.DispatcherQueue.Drain();
            Assert.Equal(1, page.SettingsScroll.ScrollResets);
            Assert.Equal(ScrollingAnimationMode.Disabled, page.SettingsScroll.LastOptions!.AnimationMode);
            Assert.Equal(ScrollingSnapPointsMode.Ignore, page.SettingsScroll.LastOptions.SnapPointsMode);
            page.RaiseLoaded();
            page.QueueEntranceForContract();
            page.DispatcherQueue.Drain();
            Assert.Equal(1, page.SettingsScroll.ScrollResets, "同页Loaded不能再次复位");
            page.SelectedCategory = "字幕";
            page.QueueEntranceForContract();
            page.RaiseLoaded();
            page.DispatcherQueue.Drain();
            Assert.Equal(2, page.SettingsScroll.ScrollResets);
        }));
        Test("搜索负责滚动时分类入场不再抢零偏移，正常分类仍复位", () => WithPage((_, _, page) =>
        {
            ShellNavigationProbe.BeginVisitForContract(page);
            page.RaiseLoaded();
            page.AddEntranceSection();
            page.PendingSearchForContract = true;
            page.SelectedCategory = "界面";
            page.DispatcherQueue.Drain();
            Assert.Equal(0, page.SettingsScroll.ScrollResets);
            page.PendingSearchForContract = false;
            page.SelectedCategory = "字幕";
            page.DispatcherQueue.Drain();
            Assert.Equal(1, page.SettingsScroll.ScrollResets);
        }));
        Test("分类队列拒绝或Layout失败不记完成，重试仍复位", () => WithPage((_, _, page) =>
        {
            ShellNavigationProbe.BeginVisitForContract(page);
            page.RaiseLoaded();
            page.DispatcherQueue.Drain();
            var resets = page.SettingsScroll.ScrollResets;
            page.DispatcherQueue.RejectEnqueue = true;
            page.SelectedCategory = "界面";
            Assert.Equal(1d, page.SettingsContent.Opacity);
            page.DispatcherQueue.RejectEnqueue = false;
            page.SettingsContent.OnUpdateLayout = () => throw new InvalidOperationException("离线Layout失败");
            page.QueueEntranceForContract();
            page.DispatcherQueue.Drain();
            Assert.Equal(resets, page.SettingsScroll.ScrollResets);
            Assert.Equal(1d, page.SettingsContent.Opacity);
            page.SettingsContent.OnUpdateLayout = null;
            page.QueueEntranceForContract();
            page.DispatcherQueue.Drain();
            Assert.Equal(resets + 1, page.SettingsScroll.ScrollResets);
        }));
        Test("Rows wrapper稳定枚举真实身份与类型，容器不因显隐重建", () => WithScreenshot((_, fixture, page, section, rows) =>
        {
            var wrapper = (Momoka.Shell.ViewModels.SettingSection)rows.Tag!;
            var actual = ShellRuntime.Current.Get<IList>(section, "Rows");
            var collection = wrapper.Rows;
            Assert.True(ReferenceEquals(actual, collection.Inner));
            Assert.Equal(actual.Count, collection.Count);
            for (var index = 0; index < actual.Count; index++)
            {
                Assert.True(ReferenceEquals(actual[index], collection[index].Inner));
                Assert.True(ReferenceEquals(collection[index], collection.ElementAt(index)));
                Assert.Equal(actual[index]!.GetType(), collection[index].GetType().GenericTypeArguments.Single());
                Assert.Equal(ShellRuntime.Current.Get<string>(actual[index]!, "Label"), collection[index].Label);
                Assert.True(ReferenceEquals(rows.ContainerFromItem(actual[index]!), rows.ContainerFromItem(collection[index])));
            }
            var control = collection.Single(row => row.Label == "WebP 无损");
            var container = rows.ContainerFromItem(control);
            ToggleLossless(fixture.Vm);
            ToggleLossless(fixture.Vm);
            Assert.True(ReferenceEquals(collection, wrapper.Rows));
            Assert.True(ReferenceEquals(control, collection.Single(row => row.Label == "WebP 无损")));
            Assert.True(ReferenceEquals(container, rows.ContainerFromItem(control)));
            Assert.Equal(actual.Count, collection.Count);
        }));
        Test("实际删除淡出补位与高度请求同为160ms，Completed直接落定", () => WithScreenshot((_, _, page, section, rows) =>
        {
            var actual = ShellRuntime.Current.Get<IList>(section, "Rows");
            var removed = actual[0]!;
            var survivor = actual[1]!;
            var container = rows.ContainerFromItem(survivor)!;
            var target = (FrameworkElement)VisualTreeHelper.GetChild(container, 0);
            Assert.True(ReferenceEquals(DependencyProperty.UnsetValue, target.ReadLocalValue(UIElement.RenderTransformProperty)));
            actual.RemoveAt(0);
            Assert.True(page.HasConditionalMotion(rows));
            rows.DispatcherQueue.Drain();
            Assert.True(ReferenceEquals(container, rows.ContainerFromItem(survivor)));
            Assert.True(rows.ItemContainerTransitions is { Count: 0 });
            Assert.True(page.HasConditionalTransitions(rows), "兼容名字表示可控动画可用，不是系统Transition");
            var board = Storyboard.Active.Single();
            Assert.True(board.Children.Any(animation => animation.Property == "Opacity"));
            Assert.True(board.Children.Any(animation => animation.Property == "Y"));
            Assert.True(board.Children.Any(animation => animation.Property == "Height" && animation.EnableDependentAnimation));
            Assert.True(board.Children.All(animation => animation.Duration.TimeSpan == TimeSpan.FromMilliseconds(160) && animation.BeginTime == TimeSpan.Zero));
            var popup = Popup.Created.Last();
            Assert.True(popup.IsOpen);
            var copy = ((Canvas)popup.Child!).Children.OfType<ContentPresenter>().Single();
            Assert.True(ReferenceEquals(removed, ((Momoka.Shell.ViewModels.SettingRow)copy.Content!).Inner));
            foreach (var element in Elements(popup).OfType<UIElement>())
            {
                Assert.False(element.IsHitTestVisible);
                Assert.False(element.IsTabStop);
                if (!ReferenceEquals(element, popup)) Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(element));
            }
            Storyboard.Advance(159);
            Assert.True(page.HasConditionalMotion(rows));
            Storyboard.Advance(1);
            Assert.True(page.IsContentMotionSettled);
            Assert.Equal(0, board.CompletionSubscriptions);
            Assert.False(popup.IsOpen);
            Assert.Null(popup.Child);
            Assert.True(ReferenceEquals(DependencyProperty.UnsetValue, target.ReadLocalValue(UIElement.RenderTransformProperty)));
            var surface = (Border)VisualTreeHelper.GetParent(rows)!;
            Assert.True(double.IsNaN(surface.Height));
            Assert.Null(surface.Clip);
        }));
        Test("替换行动画不受旧Completed清理，Release取消副本和队列", () => WithScreenshot((_, _, page, section, rows) =>
        {
            var actual = ShellRuntime.Current.Get<IList>(section, "Rows");
            var first = actual[0]!;
            actual.RemoveAt(0);
            rows.DispatcherQueue.Drain();
            var old = Storyboard.Active.Single();
            var posted = old.CapturePostedCompletion();
            actual.Insert(0, first);
            rows.DispatcherQueue.Drain();
            var current = Storyboard.Active.Single();
            Assert.False(ReferenceEquals(old, current));
            posted();
            Assert.True(page.HasConditionalMotion(rows));
            Assert.True(ReferenceEquals(current, Storyboard.Active.Single()));
            page.ReleaseHosted();
            Assert.Equal(0, Storyboard.Count);
            Assert.Equal(0, current.CompletionSubscriptions);
            Assert.True(Popup.Created.All(popup => !popup.IsOpen && popup.Child is null));
            rows.DispatcherQueue.Drain();
            Assert.True(page.IsContentMotionSettled);
        }));
        Test("祖先隐藏和后台系统减少动画及时取消真实行队列", () => WithScreenshot((_, fixture, page, _, rows) =>
        {
            ToggleLossless(fixture.Vm);
            Assert.True(page.HasConditionalMotion(rows));
            page.SettingsLayout.Visibility = Visibility.Collapsed;
            Assert.False(page.HasConditionalMotion(rows));
            rows.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Count);
            page.SettingsLayout.Visibility = Visibility.Visible;
            ToggleLossless(fixture.Vm);
            rows.DispatcherQueue.HasThreadAccess = false;
            Windows.UI.ViewManagement.UISettings.SystemAnimations = false;
            Assert.True(rows.DispatcherQueue.Count >= 2);
            rows.DispatcherQueue.DrainOne(); // 系统High通知必须先于Low行动画。
            Assert.False(page.HasConditionalMotion(rows));
            Assert.False(page.HasConditionalTransitions(rows));
            rows.DispatcherQueue.Drain();
            Assert.Equal(0, Storyboard.Count);
            page.ReleaseHosted();
            Assert.False(page.HasConditionalMotion(rows));
            rows.RaiseUnloaded(); // 缓存页Hide只Stop；真实离树才Detach并移除系统/祖先订阅。
            Assert.Equal(0, Windows.UI.ViewManagement.UISettings.Subscribers);
            Assert.Equal(0, rows.PropertySubscriptions);
        }));
        Test("屏外分组不入场；系统关动画仍按原系统偏好不播", () => WithPage((_, _, page) =>
        {
            page.RaiseLoaded();
            var offscreen = page.AddEntranceSection(1200);
            ShellNavigationProbe.BeginVisitForContract(page);
            page.SelectedCategory = "界面";
            page.DispatcherQueue.Drain();
            Assert.Equal(1d, offscreen.Opacity);
            Windows.UI.ViewManagement.UISettings.SystemAnimations = false;
            page.SelectedCategory = "字幕";
            page.DispatcherQueue.Drain();
            Assert.Equal(1d, page.SettingsContent.Opacity);
            Assert.Equal(0, Storyboard.Count);
        }));
    }

    private static string HashFile(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

    private static void VerifyProductionInputs()
    {
        foreach (var input in SettingsProbeInputs.Sha256)
            Assert.Equal(input.Value, HashFile(Path.Combine(SettingsProbeInputs.SourceRoot, input.Key)), "生产输入已变，必须重新编译：" + input.Key);
    }

    private static string ShellFixtureInput(string supplied, string root)
    {
        var shell = Path.GetFullPath(supplied);
        var local = Path.Combine(Path.GetFullPath(root), "src", "Momoka.Shell", "bin", "x64", "Release", "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
        if (string.Equals(shell, local, StringComparison.OrdinalIgnoreCase)) return shell;
        var expectedSuffix = Path.Combine("src", "Momoka.Shell", "bin", "x64", "Release", "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
        Assert.True(Path.IsPathFullyQualified(supplied) && shell.EndsWith(Path.DirectorySeparatorChar + expectedSuffix, StringComparison.OrdinalIgnoreCase), "外部输入只能是明确主树的标准Release输出");
        Assert.Equal(HashFile(shell), HashFile(local), "本树离线VM副本必须与主树current output字节一致");
        return local; // 保留共享ShellRuntime的本树标准路径验证，不改其控制流。
    }

    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Elements(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void WithPage(Action<FixtureContext, LifetimeFixture, SettingsPage> run, MediaServerKind kind = MediaServerKind.Jellyfin)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(kind);
        var page = fixture.NewCachedPage("主页");
        HomeMotion.ApplyPreference(true);
        Windows.UI.ViewManagement.UISettings.SystemAnimations = true;
        try { run(context, fixture, page); }
        finally
        {
            page.ReleaseHosted();
            page.DrainRows();
            page.DispatcherQueue.Drain();
            page.CompleteRows();
            page.RaiseUnloaded();
            ShellNavigationProbe.ContractPage = null;
            ShellNavigationProbe.CompleteBeforeWaitReturns = false;
            HomeMotion.ApplyPreference(true);
            Windows.UI.ViewManagement.UISettings.SystemAnimations = true;
        }
    }

    private static void WithScreenshot(Action<FixtureContext, LifetimeFixture, SettingsPage, object, ItemsControl> run, bool attachRows = true)
    {
        WithPage((context, fixture, page) =>
        {
            var runtime = ShellRuntime.Current;
            fixture.Settings.Settings.Mpv.ScreenshotFormat = "webp";
            fixture.Settings.Settings.Mpv.ScreenshotWebpLossless = false;
            fixture.Settings.Settings.Mpv.ScreenshotWebpQuality = 73;
            fixture.Settings.Settings.Mpv.ScreenshotWebpCompression = 2;
            var section = runtime.Invoke(fixture.Vm, "ScreenshotCard")!;
            runtime.List(fixture.Vm, "Sections").Add(section);
            page.SelectedCategory = "播放行为";
            var rows = page.AddRows(section, attachRows);
            run(context, fixture, page, section, rows);
        });
    }

    private static int RunCoreSettings(string testsPath, string root)
    {
        var expected = Path.Combine(Path.GetFullPath(root), "tests", "Momoka.Tests", "bin", "Release", "net10.0", "Momoka.Tests.dll");
        Assert.Equal(expected, Path.GetFullPath(testsPath), "只执行本树当前 Release Tests DLL");
        Assert.True(File.Exists(testsPath), "必须先成功编译当前 Tests 与 Core");
        var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(expected);
        Console.WriteLine("本树当前已编译 Core Settings／ServerSettings／StartupEntry 离线相关套件：" + expected);
        foreach (var suite in new[] { "SettingsTests", "ServerSettingsTests", "StartupEntryTests" })
            assembly.GetType("Momoka.Tests." + suite, throwOnError: true)!.GetMethod("Register", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
        return (int)assembly.GetType("Momoka.Tests.TestHarness", throwOnError: true)!.GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null)!;
    }

    private static void SetCapability(Momoka.Services.IServerCapabilities capabilities, string property, object value) =>
        capabilities.GetType().GetProperty(property)!.SetValue(capabilities, value);
    private static void SameStrings(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(string.Join("|", expected), string.Join("|", actual));
    private static object[] Groups(SettingsPage page) => ((IEnumerable)page.ViewModel.CategoryGroups).Cast<object>().ToArray();
    private static IEnumerable<string> GroupNames(SettingsPage page) => Groups(page).Select(group => ShellRuntime.Current.Get<string>(group, "Name"));
    private static IEnumerable<string> Labels(object section) => ShellRuntime.Current.Get<IEnumerable>(section, "Rows").Cast<object>()
        .Select(row => ShellRuntime.Current.Get<string>(row, "Label"));
    private static object Row(object vm, string label) => ShellRuntime.Current.List(vm, "Sections").Cast<object>()
        .SelectMany(section => ShellRuntime.Current.Get<IEnumerable>(section, "Rows").Cast<object>())
        .Single(row => ShellRuntime.Current.Get<string>(row, "Label") == label);
    private static void ToggleLossless(object vm)
    {
        var row = Row(vm, "WebP 无损");
        ShellRuntime.Current.Set(row, "IsOn", !ShellRuntime.Current.Get<bool>(row, "IsOn"));
    }
}
