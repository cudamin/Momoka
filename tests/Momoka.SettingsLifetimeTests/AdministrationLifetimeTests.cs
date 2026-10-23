using Momoka.Emby;
using Momoka.Services;

namespace Momoka.Tests;

internal static class AdministrationLifetimeTests
{
    public static void Register()
    {
        foreach (var kind in new[] { MediaServerKind.Jellyfin, MediaServerKind.Emby })
        {
            TestHarness.Test($"R-018 {kind} A挂起→普通B先隐藏→A空表晚到仍隐藏", () => OrdinaryAccount(kind));
            TestHarness.Test($"R-018 {kind} 新管理员B插件先完成不被A旧空表清掉", () => NewAdministrator(kind));
            TestHarness.Test($"R-018 {kind} 同身份新刷新插件先完成拒绝旧代次", () => SameAccountGeneration(kind));
            TestHarness.Test($"R-018 {kind} 等待中权限丢失重核并隐藏", () => PermissionLost(kind));
            TestHarness.Test($"R-018 {kind} 离页后拒绝旧插件结果并拒绝新增请求", () => LeavePage(kind));
            TestHarness.Test($"R-018 {kind} 离页→重开恢复正常插件与集合身份", () => Reopen(kind));
        }
        TestHarness.Test("R-018 单服→多服新来源目录先完成拒绝单服旧结果", SingleToMulti);
        TestHarness.Test("R-018 多服→单服拒绝多服旧插件结果", MultiToSingle);
        TestHarness.Test("R-018 同服务器来源集合改变而未启动新刷新拒绝旧提交", SourceSetChanged);
        TestHarness.Test("R-018 默认账号A→B但来源集合不变拒绝A旧结果", DefaultChangedWithoutRefresh);
        TestHarness.Test("R-018 同键身份撤销→重建拒绝旧结果", ReplacedIdentity);
        TestHarness.Test("R-018 不同产品切换保留新飞牛账号目录", ProductChanged);
        TestHarness.Test("R-018 权限Probe等待中切普通B拒绝旧后续插件请求", ProbeChangedAccount);
        TestHarness.Test("R-018 普通账号恢复管理员正常读取插件", PermissionRestored);
        TestHarness.Test("R-018 未Attach与Dispose后刷新没有HTTP和目录变动", UnattachedAndDisposed);
    }

    private static void OrdinaryAccount(MediaServerKind kind)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(kind);
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        var b = fixture.Add("server", "b", kind, false);
        fixture.Select(b);
        context.Await(fixture.Refresh());
        Assert.True(fixture.Hidden);
        a.Complete();
        context.Await(old);
        Assert.True(fixture.Hidden, "旧A的空表不能重新打开普通B管理目录");
        Assert.False(fixture.Labels.Contains("控制台"));
    }

    private static void NewAdministrator(MediaServerKind kind)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(kind);
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        fixture.Select(fixture.Add("server", "b", kind));
        var b = fixture.Reads.HoldPlugin("b");
        var fresh = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(b));
        b.Complete("B插件");
        context.Await(fresh);
        Assert.True(fixture.Labels.Contains("B插件"));
        a.Complete();
        context.Await(old);
        Assert.True(fixture.Labels.Contains("B插件"), "A旧空表不能清B插件");
        Assert.False(fixture.Labels.Contains("A插件"));
    }

    private static void SameAccountGeneration(MediaServerKind kind)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(kind);
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        var next = fixture.Reads.HoldPlugin("a");
        var fresh = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(next));
        next.Complete("新插件");
        context.Await(fresh);
        a.Complete("旧插件");
        context.Await(old);
        Assert.True(fixture.Labels.Contains("新插件"));
        Assert.False(fixture.Labels.Contains("旧插件"));
    }

    private static void PermissionLost(MediaServerKind kind)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(kind);
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        fixture.Permission(fixture.Source("a"), false);
        a.Complete("不应显示");
        context.Await(old);
        Assert.True(fixture.Hidden, "等待后必须读取当前权限，不用等待前visible");
        Assert.False(fixture.Labels.Contains("不应显示"));
    }

    private static void LeavePage(MediaServerKind kind)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(kind);
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        fixture.Cancel();
        var labels = fixture.Labels;
        a.Complete("离页晚到");
        context.Await(old);
        Assert.True(labels.SequenceEqual(fixture.Labels));
        var reads = fixture.Reads.TotalReads;
        context.Await(fixture.Refresh());
        Assert.Equal(reads, fixture.Reads.TotalReads, "Inactive不发新请求");
    }

    private static void Reopen(MediaServerKind kind)
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(kind);
        var categories = ShellRuntime.Current.List(fixture.Vm, "Categories");
        var sections = ShellRuntime.Current.List(fixture.Vm, "Sections");
        var draft = ShellRuntime.Current.Draft(fixture.Vm);
        fixture.Cancel();
        fixture.Attach();
        var ticket = fixture.Reads.HoldPlugin("a");
        var refresh = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(ticket));
        ticket.Complete("重开插件");
        context.Await(refresh);
        Assert.True(fixture.Labels.Contains("重开插件"));
        Assert.True(ReferenceEquals(categories, ShellRuntime.Current.List(fixture.Vm, "Categories")));
        Assert.True(ReferenceEquals(sections, ShellRuntime.Current.List(fixture.Vm, "Sections")));
        Assert.Equal("尚未输完", ShellRuntime.Current.Get<string>(draft, "Text"));
    }

    private static void SingleToMulti()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        fixture.Add("second-server", "b", MediaServerKind.FnNas, false);
        context.Await(fixture.Refresh());
        var labels = fixture.Labels;
        a.Complete("旧单服插件");
        context.Await(old);
        Assert.True(labels.SequenceEqual(fixture.Labels), "旧单服不能重排当前多服目录");
        Assert.Equal(3, ((System.Collections.IEnumerable)ShellRuntime.Current.Get<object>(fixture.Vm, "CategoryGroups")).Cast<object>().Count());
    }

    private static void MultiToSingle()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var b = fixture.Add("second-server", "b", MediaServerKind.FnNas, false);
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        fixture.Remove(b);
        context.Await(fixture.Refresh());
        var labels = fixture.Labels;
        a.Complete("旧多服插件");
        context.Await(old);
        Assert.True(labels.SequenceEqual(fixture.Labels));
    }

    private static void SourceSetChanged()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        var labels = fixture.Labels;
        fixture.Add("server", "b", MediaServerKind.Jellyfin);
        a.Complete("旧来源集合插件");
        context.Await(old);
        Assert.True(labels.SequenceEqual(fixture.Labels), "即使默认来源没变也必须核来源集合");
    }

    private static void DefaultChangedWithoutRefresh()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var b = fixture.Add("server", "b", MediaServerKind.Jellyfin);
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        var groups = ShellRuntime.Current.Get<object>(fixture.Vm, "CategoryGroups");
        fixture.Select(b);
        a.Complete("旧A");
        context.Await(old);
        Assert.True(ReferenceEquals(groups, ShellRuntime.Current.Get<object>(fixture.Vm, "CategoryGroups")), "默认身份变更拒绝旧提交");
    }

    private static void ReplacedIdentity()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        var groups = ShellRuntime.Current.Get<object>(fixture.Vm, "CategoryGroups");
        fixture.Remove(fixture.Source("a"));
        fixture.Select(fixture.Add("server", "a", MediaServerKind.Jellyfin));
        a.Complete("旧同键");
        context.Await(old);
        Assert.True(ReferenceEquals(groups, ShellRuntime.Current.Get<object>(fixture.Vm, "CategoryGroups")), "键相同仍要核捕获作用域可用性");
    }

    private static void ProductChanged()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture();
        var a = fixture.Reads.HoldPlugin("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        fixture.Remove(fixture.Source("a"));
        fixture.Select(fixture.Add("server", "b", MediaServerKind.FnNas, false));
        context.Await(fixture.Refresh());
        var labels = fixture.Labels;
        a.Complete("Jellyfin旧插件");
        context.Await(old);
        Assert.True(labels.SequenceEqual(fixture.Labels));
        Assert.True(fixture.Labels.Contains("播放偏好"));
    }

    private static void ProbeChangedAccount()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(MediaServerKind.Emby, null);
        var probe = fixture.Reads.HoldSystem("a");
        var old = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(probe));
        fixture.Select(fixture.Add("server", "b", MediaServerKind.Emby, false));
        context.Await(fixture.Refresh());
        var reads = fixture.Reads.PluginReads;
        probe.Reply.TrySetResult(FakeReads.Json("{\"Version\":\"1.0.0\"}"));
        context.Await(old);
        Assert.Equal(reads, fixture.Reads.PluginReads, "旧Probe之后不能改用B身份继续请求");
        Assert.True(fixture.Hidden);
    }

    private static void PermissionRestored()
    {
        using var context = new FixtureContext();
        using var fixture = new LifetimeFixture(MediaServerKind.Jellyfin, false);
        context.Await(fixture.Refresh());
        Assert.True(fixture.Hidden);
        fixture.Permission(fixture.Source("a"), true);
        var a = fixture.Reads.HoldPlugin("a");
        var fresh = fixture.Refresh();
        context.PumpUntil(() => fixture.Reads.Started(a));
        a.Complete("恢复插件");
        context.Await(fresh);
        Assert.False(fixture.Hidden);
        Assert.True(fixture.Labels.Contains("恢复插件"));
    }

    private static void UnattachedAndDisposed()
    {
        using var context = new FixtureContext();
        var vm = ShellRuntime.Current.New("SettingsViewModel");
        context.Await((Task)ShellRuntime.Current.Invoke(vm, "RefreshServerAdministrationAsync")!);
        using var fixture = new LifetimeFixture();
        ShellRuntime.Current.Invoke(fixture.Vm, "Dispose");
        var reads = fixture.Reads.TotalReads;
        context.Await(fixture.Refresh());
        Assert.Equal(reads, fixture.Reads.TotalReads);
    }
}
