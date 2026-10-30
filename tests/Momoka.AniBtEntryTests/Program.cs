using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Controls;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static string _root = "";
    private static int Main(string[] args)
    {
        if (args is not [var root]) throw new ArgumentException("传入本夹具当前工作树绝对路径");
        _root = Path.GetFullPath(root);
        if (!File.Exists(Path.Combine(_root, "tests", "Momoka.AniBtEntryTests", "Momoka.AniBtEntryTests.csproj")))
            throw new ArgumentException("Root is not this test project workspace");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Register();
        return Run();
    }

    private static void Case(string name, Func<Task> work) => Test("R040 " + name, () =>
    {
        using var ui = new UiQueue();
        ui.Run(work);
    });

    private static void Register()
    {
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex })
            Case($"{kind}独立版本搜索进入真实命令且不开放识别写入", () =>
            {
                using var f = new EntryFixture(_root, knownAnime: true, kind: kind);
                var menu = f.Build();
                Assert.False(f.HasScrape(menu));
                EntryFixture.ClickSearch(menu, ItemCommand.MoviePilotVersions);
                EntryFixture.ClickSearch(menu, ItemCommand.AniBtVersions);
                Assert.Equal(1, f.Actions.MoviePilotSearches);
                Assert.Equal(1, f.Actions.AniBtSearches);
                f.DirectScrape();
                Assert.Equal(0, f.Transport.Writes);
                return Task.CompletedTask;
            });

        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
            Test("R040 pure capability only removes identification for " + type, () =>
            {
                var item = new EmbyItem { Type = type, Genres = ["动画"] };
                var normalPlan = ItemMenu.For(item, moviePilotEnabled: true);
                var secondaryPlan = ItemMenu.For(item, moviePilotEnabled: true, aniBtScrapeAvailable: false);
                var normal = Commands(normalPlan);
                var secondary = Commands(secondaryPlan);
                Assert.True(normal.Where(command => command != ItemCommand.AniBtScrape).SequenceEqual(secondary));
                Assert.False(secondary.Contains(ItemCommand.AniBtScrape));
                Assert.Equal(2, secondaryPlan.Single(row => row.Command == ItemCommand.MoviePilotVersions).Children!.Count);
            });
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            foreach (var type in new[] { EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
                Case(kind + "非动漫" + type + "菜单直接调用内置识别，保留可用性及当前来源守卫", () =>
                {
                    using var f = new EntryFixture(_root, knownAnime: false, kind: kind);
                    f.Card.Item.Type = type;
                    f.Card.Item.Genres.Add("剧情");
                    var menu = f.Build();
                    Assert.Equal("AniBT 识别", EntryFixture.Scrape(menu).Text);
                    Assert.False(menu.Items.OfType<MenuFlyoutSubItem>().Any(row => row.Tag is ItemCommand.MoviePilotVersions));
                    EntryFixture.Scrape(menu).ClickForFixture();
                    Assert.Equal(1, f.Actions.Scrapes);
                    Assert.Equal(0, f.Transport.Reads + f.Transport.Writes + f.Transport.Unexpected);
                    f.Actions.SetMoviePilot(false);
                    Assert.False(f.HasScrape(f.Build()));
                    f.Actions.SetMoviePilot(true);
                    Assert.True(f.HasScrape(f.Build()));
                    f.Select(f.B);
                    Assert.False(f.HasScrape(f.Build()));
                    f.DirectScrape();
                    Assert.Equal(1, f.Actions.Scrapes);
                    return Task.CompletedTask;
                });
        Case("打开时MP同步断线不留下后续来源监听", () =>
        {
            using var f = new EntryFixture(_root);
            MenuFlyout.ShownForFixture = _ => f.Actions.SetMoviePilot(false);
            try
            {
                ItemCommands.Show(f.Session, f.Actions, f.Owner, f.Card, position: null);
                Assert.Equal(0, f.SourceListeners);
            }
            finally { MenuFlyout.ShownForFixture = null; }
            return Task.CompletedTask;
        });
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Case(kind + "当前动画入口正常", () =>
            {
                using var f = new EntryFixture(_root, kind: kind);
                var menu = f.Build();
                Assert.True(f.HasScrape(menu));
                EntryFixture.Scrape(menu).ClickForFixture();
                Assert.Equal(1, f.Actions.Scrapes);
                Assert.Equal(0, f.Transport.Reads + f.Transport.Writes);
                return Task.CompletedTask;
            });
            Case(kind + "次级动画无死入口但保独立搜索", () =>
            {
                using var f = new EntryFixture(_root, kind: kind);
                f.Select(f.B);
                Assert.True(f.A.IsAvailable);
                Assert.False(f.A.IsCurrent);
                var menu = f.Build();
                Assert.False(f.HasScrape(menu));
                EntryFixture.ClickSearch(menu, ItemCommand.MoviePilotVersions);
                EntryFixture.ClickSearch(menu, ItemCommand.AniBtVersions);
                Assert.Equal(1, f.Actions.MoviePilotSearches);
                Assert.Equal(1, f.Actions.AniBtSearches);
                Assert.Equal(0, f.Actions.Scrapes);
                return Task.CompletedTask;
            });
        }
        Case("当前主页实际MenuRead与构建计划一致", () =>
        {
            using var f = new EntryFixture(_root);
            var page = new HomeMenuContract(f.Session, f.Actions, f.Card);
            Assert.True(page.MenuRead() is { Ok: true });
            return Task.CompletedTask;
        });
        Case("次级主页实际MenuRead与隐藏识别计划一致", () =>
        {
            using var f = new EntryFixture(_root);
            f.Select(f.B);
            var page = new HomeMenuContract(f.Session, f.Actions, f.Card);
            Assert.True(page.MenuRead() is { Ok: true });
            return Task.CompletedTask;
        });
        Case("未附加主页MenuRead仍为空", () =>
        {
            Assert.Null(new HomeMenuContract().MenuRead());
            return Task.CompletedTask;
        });
        Case("MP不可用不留依赖组", () =>
        {
            using var f = new EntryFixture(_root);
            f.Actions.SetMoviePilot(false);
            var menu = f.Build();
            Assert.False(menu.Items.Any(row => row.Tag is ItemCommand.MoviePilotVersions or ItemCommand.MoviePilotReorganizeMenu));
            Assert.False(f.HasScrape(menu));
            f.DirectScrape();
            Assert.Equal(0, f.Actions.Scrapes);
            return Task.CompletedTask;
        });
        Case("设置关闭 AniBT 后识别和版本来源都消失且直接命令被拒绝", () =>
        {
            using var f = new EntryFixture(_root);
            f.Actions.SetAniBt(false);
            var menu = f.Build();
            Assert.False(f.HasScrape(menu));
            Assert.False(menu.Items.OfType<MenuFlyoutSubItem>().Any(row => row.Tag is ItemCommand.MoviePilotVersions));
            f.DirectScrape();
            Assert.Equal(0, f.Actions.Scrapes);
            f.Actions.SetAniBt(true);
            Assert.True(f.HasScrape(f.Build()));
            return Task.CompletedTask;
        });
        Case("打开菜单时关闭 AniBT 收起嵌套识别且旧行不会复活", () =>
        {
            using var f = new EntryFixture(_root);
            var menu = f.Show();
            var oldScrape = EntryFixture.Scrape(menu);
            Assert.True(menu.OpenForFixture);
            f.Actions.SetAniBt(false);
            Assert.False(menu.OpenForFixture);
            f.Actions.SetAniBt(true);
            oldScrape.ClickForFixture();
            Assert.Equal(0, f.Actions.Scrapes);
            Assert.True(f.HasScrape(f.Build()));
            return Task.CompletedTask;
        });
        Case("菜单中 AniBT 开启原地补回子项且 MoviePilot 操作保持", () =>
        {
            using var f = new EntryFixture(_root);
            f.Actions.SetAniBt(false);
            var menu = f.Show();
            Assert.True(menu.OpenForFixture);
            Assert.False(f.HasScrape(menu));
            f.Actions.SetAniBt(true);
            Assert.True(menu.OpenForFixture);
            Assert.True(f.HasScrape(menu));
            EntryFixture.ClickSearch(menu, ItemCommand.MoviePilotVersions);
            Assert.Equal(1, f.Actions.MoviePilotSearches);
            menu.Hide();
            return Task.CompletedTask;
        });
        Case("非当前来源直接旧命令拒绝", () =>
        {
            using var f = new EntryFixture(_root);
            f.Select(f.B);
            f.DirectScrape();
            Assert.Equal(0, f.Actions.Scrapes);
            return Task.CompletedTask;
        });
        Case("已构建旧菜单点击不交新管理身份", () =>
        {
            using var f = new EntryFixture(_root);
            var row = EntryFixture.Scrape(f.Build());
            f.Select(f.B);
            row.ClickForFixture();
            Assert.Equal(0, f.Actions.Scrapes);
            return Task.CompletedTask;
        });
        Case("撤销来源旧命令拒绝", () =>
        {
            using var f = new EntryFixture(_root);
            f.RevokeA();
            f.DirectScrape();
            Assert.Equal(0, f.Actions.Scrapes);
            return Task.CompletedTask;
        });
        Case("切回原来源重建恢复入口", () =>
        {
            using var f = new EntryFixture(_root);
            f.Select(f.B);
            Assert.False(f.HasScrape(f.Build()));
            f.Select(f.A);
            Assert.True(f.HasScrape(f.Build()));
            f.DirectScrape();
            Assert.Equal(1, f.Actions.Scrapes);
            return Task.CompletedTask;
        });
        Case("菜单打开后来源切换收起并取消监听", () =>
        {
            using var f = new EntryFixture(_root);
            var menu = f.Show();
            Assert.True(menu.OpenForFixture);
            Assert.Equal(1, f.SourceListeners);
            f.Select(f.B);
            Assert.False(menu.OpenForFixture);
            Assert.Equal(1, menu.HideCountForFixture);
            Assert.Equal(0, f.SourceListeners);
            return Task.CompletedTask;
        });
        Case("菜单正常关闭不泄漏来源监听", () =>
        {
            using var f = new EntryFixture(_root);
            var menu = f.Show();
            menu.Hide();
            Assert.Equal(0, f.SourceListeners);
            f.Select(f.B);
            Assert.Equal(1, menu.HideCountForFixture);
            return Task.CompletedTask;
        });
        Case("排队来源事件不二次关闭已关菜单", () =>
        {
            using var f = new EntryFixture(_root);
            var held = new Queue<Action>();
            f.Owner.DispatcherQueue.EnqueueForFixture = action => { held.Enqueue(action); return true; };
            var menu = f.Show();
            f.Select(f.B);
            menu.Hide();
            while (held.TryDequeue(out var action)) action();
            Assert.Equal(1, menu.HideCountForFixture);
            Assert.Equal(0, f.SourceListeners);
            return Task.CompletedTask;
        });
        Case("来源切走切回在队列前已拒绝旧识别click", () =>
        {
            using var f = new EntryFixture(_root);
            var held = new Queue<Action>();
            f.Owner.DispatcherQueue.EnqueueForFixture = action => { held.Enqueue(action); return true; };
            var menu = f.Show();
            var row = EntryFixture.Scrape(menu);
            f.Select(f.B);
            f.Select(f.A);
            row.ClickForFixture();
            Assert.Equal(0, f.Actions.Scrapes);
            while (held.TryDequeue(out var action)) action();
            Assert.False(menu.OpenForFixture);
            Assert.Equal(1, menu.HideCountForFixture);
            return Task.CompletedTask;
        });
        Case("来源切走切回排队后必关旧菜单且迟到GET不恢复", async () =>
        {
            using var f = new EntryFixture(_root, knownAnime: false);
            var held = new Queue<Action>();
            f.Owner.DispatcherQueue.EnqueueForFixture = action => { held.Enqueue(action); return true; };
            var menu = f.Show();
            await f.Transport.Started.Task;
            f.Select(f.B);
            f.Select(f.A);
            while (held.TryDequeue(out var action)) action();
            var wasClosed = !menu.OpenForFixture;
            f.Transport.Release();
            await SynchronizationContextIdle();
            Assert.True(wasClosed);
            Assert.True(f.HasScrape(menu));
            Assert.False(menu.Items.OfType<MenuFlyoutSubItem>().Any(row => row.Tag is ItemCommand.MoviePilotVersions));
            EntryFixture.Scrape(menu).ClickForFixture();
            Assert.Equal(0, f.Actions.Scrapes);
            Assert.Equal(0, f.SourceListeners);
        });
        Case("来源切走切回GET先于Hide回调也不能增加旧识别", async () =>
        {
            using var f = new EntryFixture(_root, knownAnime: false);
            var held = new Queue<Action>();
            f.Owner.DispatcherQueue.EnqueueForFixture = action => { held.Enqueue(action); return true; };
            var menu = f.Show();
            await f.Transport.Started.Task;
            f.Select(f.B);
            f.Select(f.A);
            f.Transport.Release();
            await SynchronizationContextIdle();
            Assert.True(f.HasScrape(menu));
            Assert.False(menu.Items.OfType<MenuFlyoutSubItem>().Any(row => row.Tag is ItemCommand.MoviePilotVersions));
            EntryFixture.Scrape(menu).ClickForFixture();
            Assert.Equal(0, f.Actions.Scrapes);
            while (held.TryDequeue(out var action)) action();
            Assert.False(menu.OpenForFixture);
        });
        Case("仍属相同来源通知不误撤销当前菜单", () =>
        {
            using var f = new EntryFixture(_root);
            var menu = f.Show();
            f.Select(f.A);
            Assert.True(menu.OpenForFixture);
            EntryFixture.Scrape(menu).ClickForFixture();
            Assert.Equal(1, f.Actions.Scrapes);
            return Task.CompletedTask;
        });
        Case("次级延迟动画补读只加搜索不加识别", async () =>
        {
            using var f = new EntryFixture(_root, knownAnime: false);
            f.Select(f.B);
            var menu = f.Show();
            await f.Transport.Started.Task;
            f.Transport.Release();
            await SynchronizationContextIdle();
            Assert.False(f.HasScrape(menu));
            EntryFixture.ClickSearch(menu, ItemCommand.AniBtVersions);
            Assert.Equal(1, f.Actions.AniBtSearches);
            Assert.Equal(1, f.Transport.Reads);
            Assert.Equal(0, f.Transport.Writes + f.Transport.Unexpected);
        });
        Case("当前非动漫识别即时显示，延迟动画补读只增加搜索不重复识别", async () =>
        {
            using var f = new EntryFixture(_root, knownAnime: false);
            var menu = f.Show();
            Assert.True(f.HasScrape(menu));
            await f.Transport.Started.Task;
            f.Transport.Release();
            await SynchronizationContextIdle();
            Assert.True(f.HasScrape(menu));
            Assert.Equal(1, EntryFixture.Reorganize(menu).Items.Count(row => row.Tag is ItemCommand.AniBtScrape));
            Assert.True(menu.Items.OfType<MenuFlyoutSubItem>().Any(row => row.Tag is ItemCommand.MoviePilotVersions));
            Assert.Equal(1, f.Transport.Reads);
        });
        foreach (var change in new[] { "switch", "revoke", "close", "disconnect" })
            Case("动画GET期间" + change + "迟到不复活入口", async () =>
            {
                using var f = new EntryFixture(_root, knownAnime: false);
                var menu = f.Show();
                await f.Transport.Started.Task;
                switch (change)
                {
                    case "switch": f.Select(f.B); break;
                    case "revoke": f.RevokeA(); break;
                    case "close": menu.Hide(); break;
                    case "disconnect": f.Actions.SetMoviePilot(false); break;
                }
                f.Transport.Release();
                await SynchronizationContextIdle();
                Assert.True(f.HasScrape(menu));
                Assert.False(menu.Items.OfType<MenuFlyoutSubItem>().Any(row => row.Tag is ItemCommand.MoviePilotVersions));
                EntryFixture.Scrape(menu).ClickForFixture();
                Assert.Equal(0, f.Actions.Scrapes);
                Assert.Equal(0, f.Transport.Writes + f.Transport.Unexpected);
            });
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex })
            Case(kind + "沿原provider限制不误增入口", () =>
            {
                using var f = new EntryFixture(_root, kind: kind);
                Assert.False(f.HasScrape(f.Build()));
                f.DirectScrape();
                Assert.Equal(0, f.Actions.Scrapes);
                return Task.CompletedTask;
            });
    }

    /// <summary>菜单命令按层级摊平：父项在前，子项随后 —— 识别入口住在「重新整理」组里。</summary>
    private static IEnumerable<ItemCommand?> Commands(IReadOnlyList<ItemMenuEntry> rows) =>
        rows.SelectMany(row => row.Children is { } children
            ? new[] { row.Command }.Concat(Commands(children))
            : new[] { row.Command });

    private static Task SynchronizationContextIdle() => ((UiQueue)SynchronizationContext.Current!).IdleAsync();
}
