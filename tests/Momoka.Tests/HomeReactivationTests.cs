using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Text.Json;
using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomeReactivationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    internal static void Register(ShellFixture shell)
    {
        Case("主页实例重入：同配置成功页立即复用原货架、卡片和轮播", async () =>
        {
            using var fixture = SafeFixture(shell);
            var home = CreateHome(shell, fixture, banner: true);
            try
            {
                await Load(shell, home);
                var shelves = Values(shell, home, "Shelves");
                var cards = Cards(shell, home);
                var slides = Values(shell, home, "Slides");
                Assert.True(shelves.Length > 0 && cards.Length > 0 && slides.Length > 0);
                var before = fixture.RequestCount;
                shell.Invoke(home, "Cancel");
                Assert.True(Reactivate(shell, home));
                Assert.Equal(before, fixture.RequestCount, "重新激活不得先联网");
                Assert.True(shelves.SequenceEqual(Values(shell, home, "Shelves")));
                Assert.True(cards.SequenceEqual(Cards(shell, home)));
                Assert.True(slides.SequenceEqual(Values(shell, home, "Slides")));
                Assert.True(shell.Get<bool>(home, "IsReady"));
                var changes = 0;
                ((INotifyCollectionChanged)shell.Get<object>(home, "Shelves")).CollectionChanged += (_, _) => changes++;
                await (Task)shell.Invoke(home, "BackgroundRefreshAsync")!;
                await Supplement(home);
                Assert.True(cards.SequenceEqual(Cards(shell, home)), "相同服务器结果不替换保留卡片");
                Assert.Equal(0, changes);
            }
            finally { ((IDisposable)home).Dispose(); }
        });

        foreach (var change in new[] { "directory-name", "directory-type", "directory-image", "row-title", "row-visible",
            "row-order", "banner", "carousel-source", "carousel-media", "carousel-count", "carousel-dwell", "placement",
            "control-color", "control-opacity", "acrylic", "source-owner", "removed" })
        {
            Case("主页实例重入：改变 " + change + " 拒绝旧页面并清除内容", async () =>
            {
                using var fixture = SafeFixture(shell);
                var home = CreateHome(shell, fixture);
                try
                {
                    await Load(shell, home);
                    shell.Invoke(home, "Cancel");
                    var libraries = ((IReadOnlyList<EmbyItem>)Field(home, "_libraryViews")!).ToArray();
                    var ui = fixture.Settings.Settings.Ui;
                    object? session = null;
                    using var other = change == "source-owner" ? SafeFixture(shell) : null;
                    switch (change)
                    {
                        case "directory-name": libraries = [Copy(libraries[0], item => item.Name += " changed")]; break;
                        case "directory-type": libraries = [Copy(libraries[0], item => item.CollectionType = "tvshows")]; break;
                        case "directory-image": libraries = [Copy(libraries[0], item => item.ImageTags = new() { ["Primary"] = "changed" })]; break;
                        case "row-title": ui.HomeRows[0].Title += " changed"; break;
                        case "row-visible": ui.HomeRows[0].Visible = !ui.HomeRows[0].Visible; break;
                        case "row-order": ui.HomeRows.Reverse(); break;
                        case "banner": ui.ShowHomeBanner = !ui.ShowHomeBanner; break;
                        case "carousel-source": ui.CarouselSource = CarouselSource.Random; break;
                        case "carousel-media": ui.CarouselMedia = CarouselMediaType.Series; break;
                        case "carousel-count": ui.CarouselCount++; break;
                        case "carousel-dwell": ui.CarouselSeconds++; break;
                        case "placement": ui.HomeLibraryPlacement = HomeLibraryPlacement.OnCarousel; break;
                        case "control-color": ui.ControlBackgroundColor = "#123456"; break;
                        case "control-opacity": ui.ControlBackgroundOpacity = 10; break;
                        case "acrylic": ui.UseAcrylicControls = !ui.UseAcrylicControls; break;
                        case "source-owner": session = other!.Session; break;
                        case "removed":
                            var removed = new RemovedMedia();
                            removed.Remember(shell.Get<EmbyItem>(Cards(shell, home)[0], "Item"));
                            shell.Invoke(home, "UseRemovedMedia", removed);
                            break;
                    }
                    Assert.False(Reactivate(shell, home, libraries, session), "变化后的页面不能复用旧账号、旧配置或旧图片");
                    Assert.Equal(0, Values(shell, home, "Shelves").Length);
                    Assert.Equal(0, Values(shell, home, "Slides").Length);
                }
                finally { ((IDisposable)home).Dispose(); }
            });
        }

        Case("主页实例重入：读取失败页不可作为成功页面复用", async () =>
        {
            using var fixture = SafeFixture(shell);
            var home = CreateHome(shell, fixture);
            fixture.Transport.FailHomeResumeHosts.Add("a");
            try
            {
                await Load(shell, home);
                Assert.True(shell.Get<bool>(home, "NoticeOpen"));
                shell.Invoke(home, "Cancel");
                Assert.False(Reactivate(shell, home));
                Assert.Equal(0, Values(shell, home, "Shelves").Length);
            }
            finally { ((IDisposable)home).Dispose(); }
        });

        Case("主页实例重入：成功后后台权限失败清空，不沿用旧成功标记", async () =>
        {
            using var fixture = SafeFixture(shell);
            var home = CreateHome(shell, fixture);
            try
            {
                await Load(shell, home);
                Assert.True(Cards(shell, home).Length > 0);
                fixture.Transport.RejectHomeResumePermission = true;
                await (Task)shell.Invoke(home, "BackgroundRefreshAsync")!;
                Assert.True(shell.Get<bool>(home, "NoticeOpen"));
                Assert.Equal(0, Values(shell, home, "Shelves").Length, "权限失败不继续显示旧内容");
                shell.Invoke(home, "Cancel");
                Assert.False(Reactivate(shell, home), "已清空的权限失败页不属于可复用的成功页");
                Assert.Equal(0, Values(shell, home, "Slides").Length);
            }
            finally { ((IDisposable)home).Dispose(); }
        });

        Case("主页实例重入：成功后前台刷新挂起再离页，不沿用旧成功标记", async () =>
        {
            using var fixture = SafeFixture(shell);
            var home = CreateHome(shell, fixture);
            var gate = new Gate(true);
            var loading = Task.CompletedTask;
            try
            {
                await Load(shell, home);
                fixture.Transport.HomeResumeGates["a"] = gate;
                loading = (Task)shell.Invoke(home, "ReloadAsync")!;
                await gate.Entered.Task;
                shell.Invoke(home, "Cancel");
                Assert.False(Reactivate(shell, home), "未完成的前台刷新已经改动内容，不能复用旧完成标记");
                Assert.Equal(0, Values(shell, home, "Shelves").Length);
                gate.Release.TrySetResult();
                await loading;
                Assert.Equal(0, Values(shell, home, "Shelves").Length, "前台旧响应不能重新填入离页内容");
            }
            finally
            {
                gate.Release.TrySetResult(); await loading; await Supplement(home);
                ((IDisposable)home).Dispose();
            }
        });

        foreach (var auto in new[] { false, true })
        {
            Case("主页实例重入：旧" + (auto ? "定时检查" : "后台刷新") + "完成不解除新访问的在途标记", async () =>
            {
                using var fixture = SafeFixture(shell);
                var home = CreateHome(shell, fixture);
                Task old = Task.CompletedTask;
                Task current = Task.CompletedTask;
                var oldGate = new Gate(true);
                var currentGate = new Gate(true);
                try
                {
                    await Load(shell, home);
                    if (auto) fixture.Transport.HomeWatch = oldGate;
                    else fixture.Transport.HomeResumeGates["a"] = oldGate;
                    old = (Task)shell.Invoke(home, auto ? "AutoRefreshAsync" : "BackgroundRefreshAsync")!;
                    await oldGate.Entered.Task;
                    shell.Invoke(home, "Cancel");
                    Assert.True(Reactivate(shell, home));
                    fixture.Transport.HomeWatch = null;
                    fixture.Transport.HomeResumeGates["a"] = currentGate;
                    current = (Task)shell.Invoke(home, "BackgroundRefreshAsync")!;
                    await currentGate.Entered.Task;
                    var before = fixture.RequestCount;
                    oldGate.Release.TrySetResult();
                    await old;
                    Assert.True((bool)Field(home, "_watchBusy")!, "旧 finally 不能清掉新访问的在途刷新");
                    await (Task)shell.Invoke(home, "AutoRefreshAsync")!;
                    Assert.Equal(before, fixture.RequestCount, "旧完成不能让定时检查叠到当前刷新上");
                }
                finally
                {
                    oldGate.Release.TrySetResult(); currentGate.Release.TrySetResult();
                    await old; await current; await Supplement(home);
                    ((IDisposable)home).Dispose();
                }
            });
        }
        Case("主页实例重入：旧实时排队不消费新访问通知", async () =>
        {
            using var fixture = SafeFixture(shell);
            var home = CreateHome(shell, fixture);
            var queue = (HomeReactivationDispatcher)Field(home, "_dispatcher")!;
            try
            {
                await Load(shell, home);
                shell.Invoke(home, "OnRealtimeLibraryChanged", fixture.Source("a").Key);
                Assert.Equal(1, queue.Pending);
                shell.Invoke(home, "Cancel");
                Assert.True(Reactivate(shell, home));
                shell.Invoke(home, "OnRealtimeLibraryChanged", fixture.Source("a").Key);
                Assert.Equal(2, queue.Pending, "新访问的通知不能被旧 scheduled 位拦住");
                var before = fixture.RequestCount;
                queue.RunOne();
                Assert.Equal(before, fixture.RequestCount, "旧队列回调不能借用新访问发出请求");
                queue.RunOne();
                for (var attempt = 0; attempt < 100 && (bool)Field(home, "_watchBusy")!; attempt++) await Task.Delay(5);
                Assert.True(fixture.RequestCount > before, "新访问仍消费自己的库通知");
            }
            finally { ((IDisposable)home).Dispose(); }
        });
    }

    private static Fixture SafeFixture(ShellFixture shell) => new(shell, 3, firstKind: MediaServerKind.Plex);

    private static object CreateHome(ShellFixture shell, Fixture fixture, bool banner = false)
    {
        var home = fixture.CreateLoadingHome(banner);
        foreach (var row in fixture.Settings.Settings.Ui.HomeRows)
            row.Visible = row.Key == HomeLayout.Resume || HomeLayout.LibraryId(row.Key) is not null;
        shell.Invoke(home, "BuildShelves");
        var contract = home.GetType().GetField("_dispatcher", Members)!.FieldType;
        home.GetType().GetField("_dispatcher", Members)!.SetValue(home, DispatchProxy.Create(contract, typeof(HomeReactivationDispatcher)));
        return home;
    }

    private static bool Reactivate(ShellFixture shell, object home, IReadOnlyList<EmbyItem>? libraries = null, object? session = null) =>
        (bool)shell.Invoke(home, "TryReactivate", Field(home, "_actions"), libraries ?? Field(home, "_libraryViews"),
            Field(home, "_settings"), session ?? Field(home, "_session"), Field(home, "_images"), Field(home, "_dispatcher"))!;

    private static async Task Load(ShellFixture shell, object home)
    {
        await (Task)shell.Invoke(home, "ReloadAsync")!;
        await Supplement(home);
    }

    private static object? Field(object home, string name) => home.GetType().GetField(name, Members)!.GetValue(home);
    private static Task Supplement(object home) => (Task)Field(home, "_supplement")!;
    private static object[] Values(ShellFixture shell, object home, string name) => ((IEnumerable)shell.Get<object>(home, name)).Cast<object>().ToArray();
    private static object[] Cards(ShellFixture shell, object home) => Values(shell, home, "Shelves")
        .SelectMany(shelf => ((IEnumerable)shell.Get<object>(shelf, "Cards")).Cast<object>()).ToArray();
    private static EmbyItem Copy(EmbyItem original, Action<EmbyItem> mutate)
    {
        var copy = JsonSerializer.Deserialize<EmbyItem>(JsonSerializer.Serialize(original))!;
        copy.Source = original.Source;
        mutate(copy);
        return copy;
    }
    private static void Case(string name, Func<Task> body) => Test(name, () => SingleThread.Run(body));
}

internal class HomeReactivationDispatcher : DispatchProxy
{
    private readonly Queue<Action> _work = new();
    internal int Pending => _work.Count;
    internal void RunOne() => _work.Dequeue()();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "Post") { _work.Enqueue((Action)args![0]!); return true; }
        if (targetMethod?.Name == "Run") { ((Action)args![0]!)(); return null; }
        throw new AssertionException("仅允许界面无关的调度接口");
    }
}
