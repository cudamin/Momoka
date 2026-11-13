using System.Reflection;
using System.Collections;
using System.Net;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class DetailActionReadinessTests
{
    private static void RegisterTrailerTests(Assembly assembly, string root)
    {
        Test("预告片：详情没有资源时隐藏入口", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            var visibility = f.Vm.GetType().GetProperty("TrailerVisibility", Members);
            Assert.True(visibility is not null, "详情页尚无预告片入口状态");
            Assert.Equal("Collapsed", visibility!.GetValue(f.Vm)?.ToString());
        });

        Test("预告片：本地条目独立身份从头播放，不继承正片队列", () =>
        {
            using var f = TrailersFixture(assembly, root);
            Await(f.Load());
            var target = f.Get("PlayTarget");
            var selection = TrailerMenu(f);
            var option = TrailerOptions(selection)[0];
            OpenTrailer(f, selection, option);
            Assert.Equal(1, f.Actions.Plays);
            var item = (EmbyItem)f.Actions.PlaybackArguments![0]!;
            Assert.Equal("trailer", item.Id);
            Assert.Equal(f.Seed.Source!.Key, item.Source!.Key);
            Assert.Equal(0L, f.Actions.Choice!.StartTicks);
            Assert.True(f.Actions.Choice.ExactStart);
            Assert.True(f.Actions.PlaybackArguments[1] is null);
            Assert.True(f.Actions.PlaybackArguments[3] is null);
            Assert.True(ReferenceEquals(target, f.Get("PlayTarget")));
        });

        Test("预告片：远程链接仅交浏览器，不读取本地接口或启动播放器", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            f.Seed.RemoteTrailers = [new() { Name = "官方预告", Url = "https://trailers.invalid/watch?v=one" }];
            Await(f.Load());
            Assert.Equal("Visible", f.Get("TrailerVisibility")!.ToString());
            var selection = TrailerMenu(f);
            OpenTrailer(f, selection, TrailerOptions(selection)[0]);
            Assert.Equal("https://trailers.invalid/watch?v=one", f.Launcher.Url);
            Assert.Equal(0, f.Actions.Plays);
            Assert.Equal(0, f.Transport.TrailerRequests);
        });

        Test("预告片：本地读取失败保留外链与重试，成功后清除错误", () =>
        {
            using var f = TrailersFixture(assembly, root);
            f.Seed.RemoteTrailers = [new() { Url = "https://trailers.invalid/remote" }];
            f.Transport.TrailerStatus = HttpStatusCode.ServiceUnavailable;
            Await(f.Load());
            var failed = TrailerMenu(f);
            Assert.Equal(1, TrailerOptions(failed).Count);
            Assert.True(failed.GetType().GetProperty("Error")!.GetValue(failed) is string);
            Assert.Equal("Visible", f.Get("TrailerVisibility")!.ToString());
            OpenTrailer(f, failed, TrailerOptions(failed)[0]);
            Assert.Equal("https://trailers.invalid/remote", f.Launcher.Url);
            f.Transport.TrailerStatus = HttpStatusCode.OK;
            var retried = TrailerMenu(f);
            Assert.Equal(2, TrailerOptions(retried).Count);
            Assert.True(retried.GetType().GetProperty("Error")!.GetValue(retried) is null);
            Assert.Equal(2, f.Transport.TrailerRequests);
        });

        Test("预告片：服务端本地空列表隐藏，重新加载后可恢复", () =>
        {
            using var f = TrailersFixture(assembly, root);
            f.Transport.Trailers.Clear();
            Await(f.Load());
            Assert.Equal(0, TrailerOptions(TrailerMenu(f)).Count);
            Assert.Equal("Collapsed", f.Get("TrailerVisibility")!.ToString());
            Await(f.Load());
            Assert.Equal("Visible", f.Get("TrailerVisibility")!.ToString());
        });

        Test("预告片：拒绝正片回流、空ID、虚拟条目和重复项", () =>
        {
            using var f = TrailersFixture(assembly, root);
            f.Transport.Trailers.Add(f.Seed);
            f.Transport.Trailers.Add(new() { Id = "", Type = EmbyItemType.Trailer });
            f.Transport.Trailers.Add(new() { Id = "virtual", Type = EmbyItemType.Series });
            f.Transport.Trailers.Add(f.Transport.Trailers[0]);
            Await(f.Load());
            Assert.Equal(1, TrailerOptions(TrailerMenu(f)).Count);
        });

        foreach (var change in new[] { "Cancel", "Reload", "Reattach", "NewMenu" })
            Test("预告片：旧菜单在" + change + "后不能启动", () =>
            {
                using var f = TrailersFixture(assembly, root);
                Await(f.Load());
                var selection = TrailerMenu(f);
                var option = TrailerOptions(selection)[0];
                Task? pending = null;
                if (change == "Reload")
                {
                    f.Transport.HoldDetail = true;
                    pending = f.Load();
                }
                else if (change == "Reattach") f.Reattach(f.Transport.Catalog["episode"]);
                else if (change == "NewMenu") TrailerMenu(f);
                else Invoke(f.Vm, "Cancel");
                OpenTrailer(f, selection, option);
                Assert.Equal(0, f.Actions.Plays);
                Assert.True(f.Launcher.Url is null);
                f.Transport.DetailRelease.TrySetResult(true);
                if (pending is not null) Await(pending);
            });

        Test("预告片：离页后迟到列表不能恢复入口或生成菜单", () =>
        {
            using var f = TrailersFixture(assembly, root);
            Await(f.Load());
            f.Transport.HoldTrailers = true;
            var pending = (Task)Invoke(f.Vm, "GetTrailersAsync")!;
            Await(f.Transport.TrailerRequested.Task);
            Invoke(f.Vm, "Cancel");
            f.Transport.TrailerRelease.TrySetResult(true);
            Await(pending);
            Assert.True(pending.GetType().GetProperty("Result")!.GetValue(pending) is null);
            Assert.Equal("Collapsed", f.Get("TrailerVisibility")!.ToString());
        });

        Test("预告片：选项被更换身份及另一菜单选项均拒绝", () =>
        {
            using var f = TrailersFixture(assembly, root);
            Await(f.Load());
            var first = TrailerMenu(f);
            var option = TrailerOptions(first)[0];
            var second = TrailerMenu(f);
            OpenTrailer(f, second, option);
            var currentOption = TrailerOptions(second)[0];
            ((EmbyItem)currentOption!.GetType().GetProperty("Item")!.GetValue(currentOption)!).Id = f.Seed.Id;
            OpenTrailer(f, second, currentOption);
            Assert.Equal(0, f.Actions.Plays);
        });

        Test("预告片：浏览器启动失败显示恢复信息", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            f.Seed.RemoteTrailers = [new() { Url = "https://trailers.invalid/remote" }];
            f.Launcher.Fail = true;
            Await(f.Load());
            var selection = TrailerMenu(f);
            OpenTrailer(f, selection, TrailerOptions(selection)[0]);
            Assert.True(f.Get("NoticeOpen") is true);
            Assert.Equal("打开预告片失败", (string)f.Get("NoticeTitle")!);
        });
    }

    private static Fixture TrailersFixture(Assembly assembly, string root)
    {
        var fixture = new Fixture(assembly, root, Shape.Movie);
        fixture.Seed.LocalTrailerCount = 1;
        fixture.Transport.Trailers.Add(new()
        {
            Id = "trailer",
            Name = "正式预告",
            Type = EmbyItemType.Trailer,
            UserData = new() { PlaybackPositionTicks = 300_000_000 }
        });
        return fixture;
    }

    private static object TrailerMenu(Fixture f)
    {
        var task = (Task)Invoke(f.Vm, "GetTrailersAsync")!;
        Await(task);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static IList TrailerOptions(object selection) => (IList)selection.GetType().GetProperty("Options")!.GetValue(selection)!;
    private static void OpenTrailer(Fixture f, object selection, object? option) => Await((Task)Invoke(f.Vm, "OpenTrailerAsync", selection, option)!);

    private sealed class TrailerServices(object launcher) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType.Name == "ISystemLauncher" ? launcher : null;
    }
}

public class TrailerLauncherProxy : DispatchProxy
{
    public string? Url { get; private set; }
    public bool Fail { get; set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "OpenUrl") throw new AssertionException("禁止未登记启动操作");
        if (Fail) throw new InvalidOperationException("匿名浏览器拒绝启动");
        Url = (string)args![0]!;
        return null;
    }
}
