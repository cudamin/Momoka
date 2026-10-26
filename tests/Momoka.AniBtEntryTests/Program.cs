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
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
            Test("R040 pure capability only removes identification for " + type, () =>
            {
                var item = new EmbyItem { Type = type, Genres = ["动画"] };
                var normal = ItemMenu.For(item, moviePilotEnabled: true);
                var secondary = ItemMenu.For(item, moviePilotEnabled: true, aniBtScrapeAvailable: false);
                Assert.True(normal.Where(row => row.Command != ItemCommand.AniBtScrape).Select(row => row.Command)
                    .SequenceEqual(secondary.Select(row => row.Command)));
                Assert.False(secondary.Any(row => row.Command == ItemCommand.AniBtScrape));
                Assert.Equal(2, secondary.Single(row => row.Command == ItemCommand.MoviePilotVersions).Children!.Count);
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
                menu.Items.OfType<MenuFlyoutItem>().Single(row => row.Tag is ItemCommand.AniBtScrape).ClickForFixture();
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
            Assert.False(menu.Items.Any(row => row.Tag is ItemCommand.MoviePilotVersions or ItemCommand.MoviePilotReorganize or ItemCommand.AniBtScrape));
            f.DirectScrape();
            Assert.Equal(0, f.Actions.Scrapes);
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
            var row = f.Build().Items.OfType<MenuFlyoutItem>().Single(row => row.Tag is ItemCommand.AniBtScrape);
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
            var row = menu.Items.OfType<MenuFlyoutItem>().Single(row => row.Tag is ItemCommand.AniBtScrape);
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
            Assert.False(f.HasScrape(menu));
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
            Assert.False(f.HasScrape(menu));
            while (held.TryDequeue(out var action)) action();
            Assert.False(menu.OpenForFixture);
        });
        Case("仍属相同来源通知不误撤销当前菜单", () =>
        {
            using var f = new EntryFixture(_root);
            var menu = f.Show();
            f.Select(f.A);
            Assert.True(menu.OpenForFixture);
            menu.Items.OfType<MenuFlyoutItem>().Single(row => row.Tag is ItemCommand.AniBtScrape).ClickForFixture();
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
        Case("当前延迟动画补读正常增加识别", async () =>
        {
            using var f = new EntryFixture(_root, knownAnime: false);
            var menu = f.Show();
            await f.Transport.Started.Task;
            f.Transport.Release();
            await SynchronizationContextIdle();
            Assert.True(f.HasScrape(menu));
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
                Assert.False(f.HasScrape(menu));
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

    private static Task SynchronizationContextIdle() => ((UiQueue)SynchronizationContext.Current!).IdleAsync();
}
