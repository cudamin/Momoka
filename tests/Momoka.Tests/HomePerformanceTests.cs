using System.Collections;
using System.Collections.Specialized;
using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomePerformanceTests
{
    public static int Run(string shellPath, string root)
    {
        var shell = new ShellFixture(shellPath, root);
        Case("聚合主页：库总数和后台检查挂起不阻塞内容可用", async () =>
        {
            using var fixture = new Fixture(shell, 2, secondSource: true);
            fixture.Transport.HomeTotals = new Gate(true);
            fixture.Transport.HomeWatch = new Gate(true);
            var home = fixture.CreateLoadingHome();
            var load = (Task)shell.Invoke(home, "ReloadAsync")!;
            bool ready;
            try
            {
                await fixture.Transport.HomeTotals.Entered.Task;
                await fixture.Transport.HomeWatch.Entered.Task;
                await Task.WhenAny(load, Task.Delay(1000));
                ready = load.IsCompletedSuccessfully && shell.Get<bool>(home, "IsReady")
                    && shell.Get<int>(home, "LoadedCount") == 6;
            }
            finally
            {
                fixture.Transport.HomeTotals.Release.TrySetResult();
                fixture.Transport.HomeWatch.Release.TrySetResult();
                await load;
                await Supplement(home);
                ((IDisposable)home).Dispose();
            }
            Assert.True(ready, "辅助请求仍阻塞可用的继续观看和最近添加");
        });
        Case("聚合主页：刷新保留货架行且不重置集合", async () =>
        {
            foreach (var overlay in new[] { false, true })
            {
                using var fixture = new Fixture(shell, 2, secondSource: true);
                var home = fixture.CreateLoadingHome();
                try
                {
                    await (Task)shell.Invoke(home, "ReloadAsync")!;
                    await Supplement(home);
                    if (overlay) shell.Invoke(home, "SetLibraryOverlay", true);
                    var shelves = shell.Get<object>(home, "Shelves");
                    var before = ((IEnumerable)shelves).Cast<object>().ToArray();
                    var changes = new List<NotifyCollectionChangedAction>();
                    ((INotifyCollectionChanged)shelves).CollectionChanged += (_, args) => changes.Add(args.Action);
                    await (Task)shell.Invoke(home, "ReloadAsync")!;
                    await Supplement(home);
                    Assert.True(before.SequenceEqual(((IEnumerable)shelves).Cast<object>()));
                    Assert.Equal(0, changes.Count, "内容刷新不应移除、重建相同货架行");
                    Assert.Equal(overlay, shell.Get<bool>(home, "LibraryOnBanner"));
                }
                finally { await Supplement(home); ((IDisposable)home).Dispose(); }
            }
        });
        Case("聚合主页：辅助读数完成更新原货架且保留来源", async () =>
        {
            using var fixture = new Fixture(shell, 2, secondSource: true);
            fixture.Transport.HomeTotals = new Gate(true);
            var home = fixture.CreateLoadingHome();
            try
            {
                await (Task)shell.Invoke(home, "ReloadAsync")!;
                var rows = Rows(home).Where(row => shell.Get<string?>(row, "LibraryId") is not null).ToArray();
                Assert.Equal(2, rows.Length);
                Assert.True(rows.All(row => shell.Get<string>(row, "Note") == "1 项"));
                fixture.Transport.HomeTotals.Release.TrySetResult();
                await Supplement(home);
                Assert.True(rows.All(row => shell.Get<string>(row, "Note") == "125 项"));
                Assert.Equal(2, rows.Select(row => shell.Get<Momoka.Emby.EmbySessionScope>(row, "Source").Key).Distinct().Count());
            }
            finally
            {
                fixture.Transport.HomeTotals.Release.TrySetResult();
                await Supplement(home);
                ((IDisposable)home).Dispose();
            }
        });
        Case("聚合主页：新加载和离页均拒绝旧辅助回填", async () =>
        {
            foreach (var cancel in new[] { false, true })
            {
                using var fixture = new Fixture(shell, 2, secondSource: true);
                var gate = fixture.Transport.HomeTotals = new Gate(true);
                var home = fixture.CreateLoadingHome();
                try
                {
                    await (Task)shell.Invoke(home, "ReloadAsync")!;
                    var old = Supplement(home);
                    var rows = Rows(home).ToArray();
                    if (cancel) shell.Invoke(home, "Cancel");
                    else
                    {
                        fixture.Transport.HomeTotals = null;
                        await (Task)shell.Invoke(home, "ReloadAsync")!;
                        await Supplement(home);
                    }
                    var notes = rows.Select(row => shell.Get<string>(row, "Note")).ToArray();
                    var changed = 0;
                    foreach (var row in rows)
                        ((System.ComponentModel.INotifyPropertyChanged)row).PropertyChanged += (_, _) => changed++;
                    gate.Release.TrySetResult();
                    await old;
                    Assert.True(notes.SequenceEqual(rows.Select(row => shell.Get<string>(row, "Note"))));
                    Assert.Equal(0, changed, "旧响应不能通知已离页或新代次货架");
                }
                finally
                {
                    gate.Release.TrySetResult();
                    await Supplement(home);
                    ((IDisposable)home).Dispose();
                }
            }
        });
        Case("聚合主页：最近全部轮播复用每来源前24项指纹", async () =>
        {
            using var fixture = new Fixture(shell, 2, secondSource: true);
            fixture.Transport.HomeLatestCount = 30;
            var home = fixture.CreateLoadingHome(banner: true);
            try
            {
                await (Task)shell.Invoke(home, "ReloadAsync")!;
                await Supplement(home);
                Assert.Equal(2, fixture.Transport.HomeLatestRequests.Count(request => request.Parent is null),
                    "每账号应只读取一份轮播列表，省掉重复的全服最近添加");
                Assert.True(fixture.Transport.HomeLatestRequests.Where(request => request.Parent is null).All(request => request.Limit == 30));
                await (Task)shell.Invoke(home, "AutoRefreshAsync")!;
                Assert.Equal(4, fixture.Transport.HomeLatestRequests.Count(request => request.Parent is null),
                    "30项候选只取前24项作为基线，否则定时检查会误判并重载");
                Assert.True(fixture.Transport.HomeLatestRequests.Where(request => request.Parent is null).All(request => request.Limit == 30),
                    "轮播和定时检查必须使用相同Limit以避免服务器分组差异");
                Assert.Equal(2, fixture.Transport.HomeLatestRequests.Count(request => request.Parent is not null),
                    "无入库变化不应再次读取库货架");
            }
            finally { await Supplement(home); ((IDisposable)home).Dispose(); }
        });
        Case("聚合主页：轮播类型筛选仍单独检查全部最新入库", async () =>
        {
            using var fixture = new Fixture(shell, 2, secondSource: true);
            fixture.Settings.Settings.Ui.CarouselMedia = Momoka.Emby.CarouselMediaType.Movies;
            var home = fixture.CreateLoadingHome(banner: true);
            try
            {
                await (Task)shell.Invoke(home, "ReloadAsync")!;
                await Supplement(home);
                Assert.Equal(4, fixture.Transport.HomeLatestRequests.Count(request => request.Parent is null));
                Assert.Equal(2, fixture.Transport.HomeLatestRequests.Count(request => request.Parent is null && request.Limit == 24));
                Assert.Equal(2, fixture.Transport.HomeLatestRequests.Count(request => request.Parent is null && request.Limit == 30));
            }
            finally { await Supplement(home); ((IDisposable)home).Dispose(); }
        });
        Case("聚合主页：快来源总数独立回填且慢总数不阻塞入库核对", async () =>
        {
            using var fixture = new Fixture(shell, 2, secondSource: true);
            var slow = fixture.Transport.HomeTotalGates["b"] = new Gate(true);
            var home = fixture.CreateLoadingHome();
            try
            {
                await (Task)shell.Invoke(home, "ReloadAsync")!;
                await Task.WhenAny(Supplement(home), Task.Delay(100));
                var rows = Rows(home).Where(row => shell.Get<string?>(row, "LibraryId") is not null).ToArray();
                var first = rows.Single(row => shell.Get<Momoka.Emby.EmbySessionScope>(row, "Source").ServerProfileId == "server-a");
                var second = rows.Single(row => shell.Get<Momoka.Emby.EmbySessionScope>(row, "Source").ServerProfileId == "server-b");
                Assert.Equal("125 项", shell.Get<string>(first, "Note"), "已返回的总数不应等另一来源");
                Assert.Equal("1 项", shell.Get<string>(second, "Note"));
                var requests = fixture.Transport.HomeLatestRequests.Count;
                await (Task)shell.Invoke(home, "AutoRefreshAsync")!;
                Assert.Equal(requests + 2, fixture.Transport.HomeLatestRequests.Count,
                    "慢总数不应阻止已建立基线后的轻量入库核对");
            }
            finally
            {
                slow.Release.TrySetResult();
                await Supplement(home);
                ((IDisposable)home).Dispose();
            }
        });
        Case("聚合主页：来源退出后的迟到总数不回填", async () =>
        {
            using var fixture = new Fixture(shell, 2, secondSource: true);
            var gate = fixture.Transport.HomeTotals = new Gate(true);
            var home = fixture.CreateLoadingHome();
            try
            {
                await (Task)shell.Invoke(home, "ReloadAsync")!;
                var rows = Rows(home).Where(row => shell.Get<string?>(row, "LibraryId") is not null).ToArray();
                fixture.RevokeHomeSource("b");
                gate.Release.TrySetResult();
                await Supplement(home);
                Assert.Equal("125 项", shell.Get<string>(rows.Single(row => shell.Get<Momoka.Emby.EmbySessionScope>(row, "Source").IsAvailable), "Note"));
                Assert.Equal("1 项", shell.Get<string>(rows.Single(row => !shell.Get<Momoka.Emby.EmbySessionScope>(row, "Source").IsAvailable), "Note"));
            }
            finally { gate.Release.TrySetResult(); await Supplement(home); ((IDisposable)home).Dispose(); }
        });
        Case("聚合主页：库总数失败仍保留内容并回退卡片数", async () =>
        {
            using var fixture = new Fixture(shell, 2, secondSource: true);
            fixture.Transport.FailHomeTotals = true;
            var home = fixture.CreateLoadingHome();
            try
            {
                await (Task)shell.Invoke(home, "ReloadAsync")!;
                await Supplement(home);
                Assert.True(shell.Get<bool>(home, "IsReady"));
                Assert.False(shell.Get<bool>(home, "NoticeOpen"), "辅助失败不应抢占内容错误提示");
                Assert.True(Rows(home).Where(row => shell.Get<string?>(row, "LibraryId") is not null)
                    .All(row => shell.Get<string>(row, "Note") == "1 项"));
            }
            finally { await Supplement(home); ((IDisposable)home).Dispose(); }
        });
        Case("主页暖缓存：同身份版面恢复不联网，后台相同内容不重建货架", async () =>
        {
            using var fixture = new Fixture(shell, 2, secondSource: true);
            var store = new BrowseSnapshotStore();
            var first = fixture.CreateLoadingHome(snapshots: store);
            try
            {
                await (Task)shell.Invoke(first, "ReloadAsync")!;
                await Supplement(first);
                Assert.Equal(1, store.HomeKeys.Count, "整页读取成功后应写入快照");
            }
            finally { ((IDisposable)first).Dispose(); }

            var before = fixture.RequestCount;
            var second = fixture.CreateLoadingHome(snapshots: store);
            try
            {
                Assert.True((bool)shell.Invoke(second, "RestoreSnapshot")!, "同一身份和版面应命中暖缓存");
                Assert.Equal(before, fixture.RequestCount, "暖恢复不能先联网");

                var rowsBefore = Rows(second).ToArray();
                var cardsBefore = rowsBefore.SelectMany(row =>
                    ((IEnumerable)shell.Get<object>(row, "Cards")).Cast<object>()).ToArray();
                var changes = 0;
                ((INotifyCollectionChanged)shell.Get<object>(second, "Shelves")).CollectionChanged += (_, _) => changes++;

                await (Task)shell.Invoke(second, "BackgroundRefreshAsync")!;
                await Supplement(second);

                Assert.True(fixture.RequestCount > before, "后台核对仍要问一次服务器");
                Assert.Equal(0, changes, "内容没变时不重建货架");
                Assert.True(rowsBefore.SequenceEqual(Rows(second).ToArray()), "货架对象应保持不变");
                var cardsAfter = Rows(second).SelectMany(row =>
                    ((IEnumerable)shell.Get<object>(row, "Cards")).Cast<object>()).ToArray();
                Assert.True(cardsBefore.SequenceEqual(cardsAfter), "内容没变时不应替换卡片对象");
            }
            finally { ((IDisposable)second).Dispose(); }
        });
        return TestHarness.Run();

        IEnumerable<object> Rows(object home) => ((IEnumerable)shell.Get<object>(home, "Shelves")).Cast<object>();
        Task Supplement(object home) => (Task)home.GetType().GetField("_supplement",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(home)!;
    }

    private static void Case(string name, Func<Task> body) => Test(name, () => SingleThread.Run(body));
}
