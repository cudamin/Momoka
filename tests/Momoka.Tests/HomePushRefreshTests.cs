using System.Reflection;
using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomePushRefreshTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Register(ShellFixture shell, string root)
    {
        Test("首页推送：页面不再启动全来源定时入库查询", () =>
        {
            var page = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "HomePage.xaml.cs"));
            Assert.DoesNotContain("WatchInterval", page);
            Assert.DoesNotContain("ViewModel.AutoRefreshAsync()", page);
            Assert.DoesNotContain("_watch.Start()", page);
        });
        foreach (var phase in new[] { "foreground", "background", "baseline", "checking" })
        {
            Test("首页推送：" + phase + " 读取期间的通知在完成后消费，不依赖定时兜底", () => SingleThread.Run(async () =>
            {
                using var fixture = new Fixture(shell, 3, firstKind: MediaServerKind.Plex);
                var home = fixture.CreateLoadingHome();
                var queue = InstallQueue(home);
                var gate = new Gate(true);
                Task pending = Task.CompletedTask;
                try
                {
                    await (Task)shell.Invoke(home, "ReloadAsync")!;
                    await Supplement(home);
                    if (phase is "baseline" or "checking") fixture.Transport.HomeWatch = gate;
                    else fixture.Transport.HomeResumeGates["a"] = gate;
                    pending = (Task)shell.Invoke(home, phase == "background" ? "BackgroundRefreshAsync"
                        : phase == "checking" ? "AutoRefreshAsync" : "ReloadAsync")!;
                    await gate.Entered.Task;
                    if (phase == "baseline") await pending;
                    var before = fixture.RequestCount;
                    shell.Invoke(home, "OnRealtimeLibraryChanged", fixture.Source("a").Key);
                    queue.RunOne();
                    Assert.Equal(before, fixture.RequestCount, "在途请求不能被通知叠加");
                    gate.Release.TrySetResult();
                    await pending;
                    await Supplement(home);
                    Assert.True(queue.Pending > 0, "已记录的通知应在读取完成后重新排队");
                    while (queue.Pending > 0) queue.RunOne();
                    await UntilAsync(() => !Field<bool>(home, "_watchBusy"));
                    Assert.True(fixture.RequestCount > before, "无需第二条推送或计时器就应补核对");
                }
                finally
                {
                    gate.Release.TrySetResult();
                    await pending;
                    await Supplement(home);
                    ((IDisposable)home).Dispose();
                }
            }));
        }

        Test("首页推送：连续通知合并一次；取消后的排队通知不再请求", () => SingleThread.Run(async () =>
        {
            using var fixture = new Fixture(shell, 3, firstKind: MediaServerKind.Plex);
            var home = fixture.CreateLoadingHome();
            var queue = InstallQueue(home);
            try
            {
                await (Task)shell.Invoke(home, "ReloadAsync")!;
                await Supplement(home);
                for (var index = 0; index < 10; index++) shell.Invoke(home, "OnRealtimeLibraryChanged", fixture.Source("a").Key);
                Assert.Equal(1, queue.Pending);
                var before = fixture.RequestCount;
                queue.RunOne();
                await UntilAsync(() => !Field<bool>(home, "_watchBusy"));
                Assert.Equal(before + 1, fixture.RequestCount, "相同列表只核对一次，不重读货架");
                shell.Invoke(home, "OnRealtimeLibraryChanged", fixture.Source("a").Key);
                shell.Invoke(home, "Cancel");
                before = fixture.RequestCount;
                while (queue.Pending > 0) queue.RunOne();
                Assert.Equal(before, fixture.RequestCount);
            }
            finally { ((IDisposable)home).Dispose(); }
        }));

        Test("首页推送：较晚基线不能吸收尚未显示的新入库", () => SingleThread.Run(async () =>
        {
            using var fixture = new Fixture(shell, 3, firstKind: MediaServerKind.Plex);
            var home = fixture.CreateLoadingHome();
            var queue = InstallQueue(home);
            var gate = fixture.Transport.HomeWatch = new Gate(true);
            fixture.Transport.HomeWatchUsesCurrentResult = true;
            try
            {
                await (Task)shell.Invoke(home, "ReloadAsync")!;
                await gate.Entered.Task;
                fixture.Transport.HomeLatestCount = 2;
                shell.Invoke(home, "OnRealtimeLibraryChanged", fixture.Source("a").Key);
                queue.RunOne();
                gate.Release.TrySetResult();
                await Supplement(home);
                while (queue.Pending > 0) queue.RunOne();
                await UntilAsync(() => !Field<bool>(home, "_watchBusy"));
                var shelves = ((System.Collections.IEnumerable)shell.Get<object>(home, "Shelves")).Cast<object>();
                var library = shelves.Single(shelf => shell.Get<string?>(shelf, "LibraryId") == "library");
                var cards = ((System.Collections.IEnumerable)shell.Get<object>(library, "Cards")).Cast<object>();
                Assert.True(cards.Any(card => shell.Get<EmbyItem>(card, "Item").Id == "a-latest-1"),
                    "基线晚到不能把旧货架没有展示的新媒体当成已处理");
            }
            finally { gate.Release.TrySetResult(); await Supplement(home); ((IDisposable)home).Dispose(); }
        }));
    }

    private static HomeReactivationDispatcher InstallQueue(object home)
    {
        var field = home.GetType().GetField("_dispatcher", Members)!;
        var queue = DispatchProxy.Create(field.FieldType, typeof(HomeReactivationDispatcher));
        field.SetValue(home, queue);
        return (HomeReactivationDispatcher)queue;
    }

    private static T Field<T>(object home, string name) => (T)home.GetType().GetField(name, Members)!.GetValue(home)!;
    private static Task Supplement(object home) => Field<Task>(home, "_supplement");

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, deadline.Token);
    }
}
