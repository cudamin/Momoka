using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class SettingsHiddenLifetimeTests
{
    public static void Register()
    {
        foreach (var hide in new[] { "显式Hide", "标题栏X" })
        {
            TestHarness.Test($"R-026 {hide}后同服切B不重建原生偏好页也不GET", () => HiddenSwitch(hide));
            TestHarness.Test($"R-026 {hide}前已排队来源回调在隐藏后拒绝", () => QueuedThenHidden(hide));
            TestHarness.Test($"R-026 {hide}中管理目录await回来不换分组源", () => AwaitThenHidden(hide));
            TestHarness.Test($"R-026 {hide}中真实偏好读取await取消后不读第二接口", () => NativeAwaitThenHidden(hide));
            TestHarness.Test($"R-026 {hide}→重开恢复订阅和B读取并保草稿集合", () => Reopen(hide));
            TestHarness.Test($"R-026 {hide}前旧队列不能穿过重开活动期", () => QueuedThenReopened(hide));
        }
        TestHarness.Test("R-026 显式内嵌设置页离页后来源通知无GET", ExplicitPageLeave);
        TestHarness.Test("R-026 注销Release结束订阅与在飞回调", SignOutRelease);
        TestHarness.Test("R-026 同服A/B切换在可见期正常重建真实偏好页", VisibleSwitch);
        TestHarness.Test("R-026 隐藏后SelectedCategory通知不启动内嵌页", HiddenSelection);
        TestHarness.Test("R-026 Hide与X重复收起只发送一次Hidden", HideParity);
        TestHarness.Test("R-026 真Close也先释放订阅与目录活动期", RealClose);
    }

    private static (LifetimeFixture Fixture, SettingsPage Page, SettingsWindow Window, EmbySessionScope B) OpenFnNas(FixtureContext context)
    {
        var fixture = new LifetimeFixture(MediaServerKind.FnNas, false);
        var b = fixture.Add("server", "b", MediaServerKind.FnNas, false);
        var page = fixture.NewCachedPage("播放偏好");
        var window = new SettingsWindow(page);
        window.Show(new SettingsRequest(fixture.Services, "播放偏好"));
        context.Await(page.RefreshForFixture());
        AwaitNative(context, page);
        Assert.Equal(1, fixture.SourceSubscribers);
        Assert.True(window.IsOpen);
        Assert.Equal(2, fixture.Reads.PreferenceReads, "执行真实偏好VM的user/info和sys/config");
        return (fixture, page, window, b);
    }

    private static void AwaitNative(FixtureContext context, SettingsPage page)
    {
        Assert.True(page.HostedFrame.Content is FnNasPreferencesPage, "真实偏好page必须已导航");
        var read = ((FnNasPreferencesPage)page.HostedFrame.Content!).ViewModel.LastRead;
        Assert.NotNull(read);
        context.Await(read!);
        Assert.True(ShellRuntime.Current.Get<bool>(((FnNasPreferencesPage)page.HostedFrame.Content!).ViewModel.Inner, "Loaded"));
    }

    private static void Hide(SettingsWindow window, string path)
    {
        if (path == "显式Hide") window.Hide();
        else Assert.True(window.BoundaryWindow.RaiseClosing().Cancel, "X只能隐藏不能真关闭");
        Assert.False(window.IsOpen);
    }

    private static void HiddenSwitch(string path)
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        var navigation = opened.Page.HostedFrame.Navigations;
        var reads = fixture.Reads.PreferenceReads;
        Hide(opened.Window, path);
        fixture.Select(opened.B, notify: true);
        opened.Page.DispatcherQueue.Drain();
        context.Drain();
        Assert.Equal(navigation, opened.Page.HostedFrame.Navigations, "隐藏后不能重建B原生页");
        Assert.Equal(reads, fixture.Reads.PreferenceReads, "隐藏后不能GET");
        Assert.Equal(0, fixture.SourceSubscribers);
        Assert.Equal(0, opened.Page.DispatcherQueue.Count);
    }

    private static void QueuedThenHidden(string path)
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        fixture.Select(opened.B, notify: true);
        Assert.Equal(1, opened.Page.DispatcherQueue.Count, "先验证来源回调确实排队");
        var navigation = opened.Page.HostedFrame.Navigations;
        var reads = fixture.Reads.TotalReads;
        Hide(opened.Window, path);
        opened.Page.DispatcherQueue.Drain();
        context.Drain();
        Assert.Equal(navigation, opened.Page.HostedFrame.Navigations);
        Assert.Equal(reads, fixture.Reads.TotalReads, "取消已排队回调不能靠退订替代");
    }

    private static void AwaitThenHidden(string path)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var page = fixture.NewCachedPage("主页");
        var window = new SettingsWindow(page);
        window.Show(new SettingsRequest(fixture.Services, "主页"));
        context.Await(page.RefreshForFixture());
        var pending = fixture.Reads.HoldPlugin("a");
        var old = page.RefreshForFixture();
        context.PumpUntil(() => fixture.Reads.Started(pending));
        var sourceWrites = page.CategoryGroupsSource.Writes;
        var groups = page.CategoryGroupsSource.Source;
        Hide(window, path);
        pending.Complete("隐藏旧插件");
        context.Await(old);
        Assert.Equal(sourceWrites, page.CategoryGroupsSource.Writes, "await后旧页面回调不能写分组源");
        Assert.True(ReferenceEquals(groups, page.CategoryGroupsSource.Source));
        var reads = fixture.Reads.TotalReads;
        context.Await(page.RefreshForFixture());
        Assert.Equal(reads, fixture.Reads.TotalReads, "隐藏中禁止显式刷新请求");
    }

    private static void NativeAwaitThenHidden(string path)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(MediaServerKind.FnNas, false);
        var user = fixture.Reads.HoldUser("a");
        var page = fixture.NewCachedPage("播放偏好");
        var window = new SettingsWindow(page);
        window.Show(new SettingsRequest(fixture.Services, "播放偏好"));
        context.PumpUntil(() => fixture.Reads.Started(user));
        var native = (FnNasPreferencesPage)page.HostedFrame.Content!;
        var reads = fixture.Reads.TotalReads;
        Hide(window, path);
        user.Reply.TrySetResult(FakeReads.Json("{\"code\":0,\"data\":{\"lan\":\"en\"}}"));
        context.Await(native.ViewModel.LastRead!);
        Assert.Equal(reads, fixture.Reads.TotalReads, "真实被释放原生VM在旧GET结束后不继续sys/config");
        Assert.False(ShellRuntime.Current.Get<bool>(native.ViewModel.Inner, "Loaded"));
    }

    private static void Reopen(string path)
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        var categories = ShellRuntime.Current.List(fixture.Vm, "Categories");
        var sections = ShellRuntime.Current.List(fixture.Vm, "Sections");
        var draft = sections[0]!;
        var row = ShellRuntime.Current.List(draft, "Rows")[0]!;
        var reads = fixture.Reads.PreferenceReads;
        Hide(opened.Window, path);
        fixture.Select(opened.B, notify: true);
        opened.Page.DispatcherQueue.Drain();
        context.Drain();
        Assert.Equal(reads, fixture.Reads.PreferenceReads, "重开前没有隐藏读取");
        opened.Window.Show(new SettingsRequest(fixture.Services, "播放偏好"));
        context.Await(opened.Page.RefreshForFixture());
        AwaitNative(context, opened.Page);
        Assert.Equal(reads + 2, fixture.Reads.PreferenceReads);
        Assert.Equal(1, fixture.SourceSubscribers);
        Assert.True(ReferenceEquals(categories, ShellRuntime.Current.List(fixture.Vm, "Categories")));
        Assert.True(ReferenceEquals(sections, ShellRuntime.Current.List(fixture.Vm, "Sections")));
        Assert.True(ReferenceEquals(draft, sections[0]));
        Assert.Equal("尚未输完", ShellRuntime.Current.Get<string>(row, "Text"));
        Assert.Equal(0, fixture.Settings.Writes, "重开不保存/重载草稿");
    }

    private static void QueuedThenReopened(string path)
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        fixture.Select(opened.B, notify: true);
        Assert.Equal(1, opened.Page.DispatcherQueue.Count);
        Hide(opened.Window, path);
        opened.Window.Show(new SettingsRequest(fixture.Services, "播放偏好"));
        context.Await(opened.Page.RefreshForFixture());
        AwaitNative(context, opened.Page);
        var sourceWrites = opened.Page.CategoryGroupsSource.Writes;
        var reads = fixture.Reads.TotalReads;
        var navigation = opened.Page.HostedFrame.Navigations;
        opened.Page.DispatcherQueue.Drain();
        context.Drain();
        Assert.Equal(sourceWrites, opened.Page.CategoryGroupsSource.Writes, "旧队列不能借新活动期复活");
        Assert.Equal(reads, fixture.Reads.TotalReads);
        Assert.Equal(navigation, opened.Page.HostedFrame.Navigations);
    }

    private static void ExplicitPageLeave()
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        opened.Page.Leave();
        var reads = fixture.Reads.TotalReads;
        fixture.Select(opened.B, notify: true);
        opened.Page.DispatcherQueue.Drain();
        context.Drain();
        Assert.Equal(reads, fixture.Reads.TotalReads);
        Assert.Equal(0, fixture.SourceSubscribers);
    }

    private static void SignOutRelease()
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        fixture.Select(opened.B, notify: true);
        opened.Page.Release(); // Real shell sign-out ReleaseContent route; no account/vault operation.
        var reads = fixture.Reads.TotalReads;
        fixture.Remove(fixture.Source("a"));
        fixture.Remove(opened.B, notify: true);
        opened.Page.DispatcherQueue.Drain();
        context.Drain();
        Assert.Equal(reads, fixture.Reads.TotalReads);
        Assert.Equal(0, fixture.SourceSubscribers);
    }

    private static void VisibleSwitch()
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        var old = opened.Page.HostedFrame.Content;
        fixture.Select(opened.B, notify: true);
        opened.Page.DispatcherQueue.Drain();
        context.Await(opened.Page.RefreshForFixture());
        AwaitNative(context, opened.Page);
        Assert.False(ReferenceEquals(old, opened.Page.HostedFrame.Content));
        Assert.Equal(4, fixture.Reads.PreferenceReads);
        Assert.Equal(1, fixture.SourceSubscribers);
    }

    private static void HiddenSelection()
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        opened.Window.Hide();
        var reads = fixture.Reads.TotalReads;
        var navigation = opened.Page.HostedFrame.Navigations;
        opened.Page.ViewModel.SelectedCategory = "主页";
        opened.Page.ViewModel.SelectedCategory = "播放偏好";
        context.Drain();
        Assert.Equal(reads, fixture.Reads.TotalReads);
        Assert.Equal(navigation, opened.Page.HostedFrame.Navigations);
    }

    private static void HideParity()
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        var hidden = 0;
        opened.Window.Hidden += () => hidden++;
        opened.Window.Hide();
        opened.Window.Hide();
        Assert.True(opened.Window.BoundaryWindow.RaiseClosing().Cancel);
        Assert.Equal(1, hidden, "重复X/Hide都走同一幂等收起路径");
        Assert.Equal(1, opened.Window.BoundaryWindow.Hidden);
    }

    private static void RealClose()
    {
        using var context = new FixtureContext();
        var opened = OpenFnNas(context);
        using var fixture = opened.Fixture;
        opened.Window.Close();
        fixture.Select(opened.B, notify: true);
        var reads = fixture.Reads.TotalReads;
        opened.Page.DispatcherQueue.Drain();
        context.Drain();
        Assert.Equal(reads, fixture.Reads.TotalReads);
        Assert.Equal(0, fixture.SourceSubscribers);
    }
}
