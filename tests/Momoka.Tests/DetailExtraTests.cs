using System.Collections;
using System.Net;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class DetailActionReadinessTests
{
    private static void RegisterExtraTests(Assembly assembly, string root)
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test($"花絮详情：{kind} 独立媒体、零起点与原播放请求链", () =>
            {
                using var f = ExtrasFixture(assembly, root, kind);
                f.Seed.UserData = new() { PlaybackPositionTicks = 450_000_000 };
                Await(f.Load());
                var target = f.Get("PlayTarget");
                var selection = ExtraMenu(f);
                Assert.Equal(1, ExtraOptions(selection).Count);
                OpenExtra(f, selection, ExtraOptions(selection)[0]);
                Assert.Equal(1, f.Actions.Plays);
                var item = (EmbyItem)f.Actions.PlaybackArguments![0]!;
                Assert.Equal("extra", item.Id);
                Assert.True(ReferenceEquals(item.Source, f.Seed.Source));
                Assert.Equal(0L, f.Actions.Choice!.StartTicks);
                Assert.True(f.Actions.Choice.ExactStart);
                Assert.True(f.Actions.PlaybackArguments[1] is null);
                Assert.True(f.Actions.PlaybackArguments[3] is null);
                Assert.True(ReferenceEquals(target, f.Get("PlayTarget")));
                Assert.Equal(450_000_000L, f.Seed.ResumeTicks);
                var planner = new PlaybackPlanner(new AppSettings(), new ShaderGroupResolver(new ShaderAutomationSettings()));
                var request = planner.Plan(new PlaybackTicket
                {
                    Item = item, Source = item.MediaSources[0], StartTicks = f.Actions.Choice.StartTicks,
                    ExactStart = f.Actions.Choice.ExactStart
                }, item.Source!.Connection);
                Assert.Equal("extra", request.ItemId);
                Assert.Equal("extra-file", request.MediaSourceId);
                Assert.Equal(0d, request.StartSeconds);
                Assert.Contains("Videos/extra/stream", request.MediaUrl.AbsoluteUri);
            });

            Test($"花絮详情：{kind} 失败留入口，重读恢复且旧菜单失效", () =>
            {
                using var f = ExtrasFixture(assembly, root, kind);
                f.Transport.ExtraStatus = HttpStatusCode.ServiceUnavailable;
                Await(f.Load());
                var failed = ExtraMenu(f);
                Assert.Equal(0, ExtraOptions(failed).Count);
                Assert.True(ExtraError(failed) is string);
                Assert.Equal("Visible", f.Get("ExtraVisibility")!.ToString());
                f.Transport.ExtraStatus = HttpStatusCode.OK;
                var restored = ExtraMenu(f);
                Assert.Equal(1, ExtraOptions(restored).Count);
                Assert.True(ExtraError(restored) is null);
                Assert.False((bool)Invoke(f.Vm, "OwnsExtras", failed)!);
                Assert.Equal(2, f.Transport.ExtraRequests);
            });

            Test($"花絮详情：{kind} 实际空态隐藏，重新读取详情后恢复", () =>
            {
                using var f = ExtrasFixture(assembly, root, kind);
                f.Transport.Extras.Clear();
                Await(f.Load());
                Assert.Equal(0, ExtraOptions(ExtraMenu(f)).Count);
                Assert.Equal("Collapsed", f.Get("ExtraVisibility")!.ToString());
                Assert.True(Invoke(f.Vm, "GetExtrasAsync") is Task empty);
                f.Transport.Extras.Add(Extra());
                Await(f.Load());
                Assert.Equal("Visible", f.Get("ExtraVisibility")!.ToString());
                Assert.Equal(1, ExtraOptions(ExtraMenu(f)).Count);
                Assert.Equal(2, f.Transport.ExtraRequests);
            });

            Test($"花絮详情：{kind} 同身份重认证重新绑定来源仍可从零播放", () =>
            {
                using var f = ExtrasFixture(assembly, root, kind);
                Await(f.Load());
                f.Transport.BeforeExtrasReply = f.ReplaceClient;
                f.Transport.ExtraStatus = HttpStatusCode.Unauthorized;
                var selection = ExtraMenu(f);
                Assert.Equal(1, ExtraOptions(selection).Count);
                var item = (EmbyItem)ExtraOptions(selection)[0]!.GetType().GetProperty("Item")!.GetValue(ExtraOptions(selection)[0])!;
                Assert.False(ReferenceEquals(item.Source, f.Seed.Source), "重试应由新的Scope绑定");
                Assert.Equal(f.Seed.Source!.Key, item.Source!.Key);
                Assert.True(ReferenceEquals(f.Seed.Source.UserStateOwner, item.Source.UserStateOwner));
                OpenExtra(f, selection, ExtraOptions(selection)[0]);
                Assert.Equal(1, f.Actions.Plays);
                Assert.Equal(0L, f.Actions.Choice!.StartTicks);
                Assert.True(f.Actions.Choice.ExactStart);
                Assert.Equal(2, f.Transport.ExtraRequests);
            });
        }

        Test("花絮详情：相同账号键但不同在线所有者不能借菜单播放", () =>
        {
            using var f = ExtrasFixture(assembly, root);
            using var other = ExtrasFixture(assembly, root);
            Await(f.Load());
            var selection = ExtraMenu(f);
            var option = ExtraOptions(selection)[0]!;
            var item = (EmbyItem)option.GetType().GetProperty("Item")!.GetValue(option)!;
            Assert.Equal(f.Seed.Source!.Key, other.Seed.Source!.Key);
            item.Source = other.Seed.Source;
            OpenExtra(f, selection, option);
            Assert.Equal(0, f.Actions.Plays);
        });

        Test("花絮详情：Jellyfin 没有数量不显示也不发送读取", () =>
        {
            using var f = ExtrasFixture(assembly, root, MediaServerKind.Jellyfin, count: 0);
            Await(f.Load());
            Assert.Equal("Collapsed", f.Get("ExtraVisibility")!.ToString());
            var task = (Task)Invoke(f.Vm, "GetExtrasAsync")!;
            Await(task);
            Assert.True(Result(task) is null);
            Assert.Equal(0, f.Transport.ExtraRequests);
        });

        Test("花絮详情：拒绝正片回流、无效编号、音频、虚拟、离线与重复", () =>
        {
            using var f = ExtrasFixture(assembly, root);
            f.Transport.Extras.Add(f.Seed);
            f.Transport.Extras.Add(new() { Id = "", Type = EmbyItemType.Video });
            f.Transport.Extras.Add(new() { Id = "bad/path", Type = EmbyItemType.Video });
            f.Transport.Extras.Add(new() { Id = "audio", Type = EmbyItemType.Audio });
            f.Transport.Extras.Add(new() { Id = "virtual", Type = EmbyItemType.Video, LocationType = "Virtual" });
            f.Transport.Extras.Add(new() { Id = "offline", Type = EmbyItemType.Video, LocationType = "Offline" });
            f.Transport.Extras.Add(new() { Id = "folder", Type = EmbyItemType.Series, IsFolder = true });
            f.Transport.Extras.Add(f.Transport.Extras[0]);
            Await(f.Load());
            Assert.Equal(1, ExtraOptions(ExtraMenu(f)).Count);
        });

        Test("花絮详情：服务器声明伪造来源不能借请求来源播放", () =>
        {
            using var f = ExtrasFixture(assembly, root);
            f.Transport.Extras.Add(new() { Id = "foreign", Type = EmbyItemType.Video, ServerId = "other-server" });
            Await(f.Load());
            Assert.Equal(1, ExtraOptions(ExtraMenu(f)).Count);
        });

        foreach (var mutation in new[] { "Id", "Source", "Folder", "Location", "ServerId" })
            Test("花絮详情：选项生成后变更 " + mutation + " 拒绝起播", () =>
            {
                using var f = ExtrasFixture(assembly, root);
                Await(f.Load());
                var selection = ExtraMenu(f);
                var option = ExtraOptions(selection)[0]!;
                var item = (EmbyItem)option.GetType().GetProperty("Item")!.GetValue(option)!;
                if (mutation == "Id") item.Id = "replacement";
                else if (mutation == "Source") item.Source = null;
                else if (mutation == "Folder") item.IsFolder = true;
                else if (mutation == "Location") item.LocationType = "Offline";
                else item.ServerId = "foreign-server";
                OpenExtra(f, selection, option);
                Assert.Equal(0, f.Actions.Plays);
            });

        foreach (var change in new[] { "Cancel", "Reload", "Reattach", "NewMenu", "SignOut" })
            Test("花絮详情：旧菜单在 " + change + " 后失效", () =>
            {
                using var f = ExtrasFixture(assembly, root);
                Await(f.Load());
                var selection = ExtraMenu(f);
                var option = ExtraOptions(selection)[0];
                Task? pending = null;
                if (change == "Reload")
                {
                    f.Transport.HoldDetail = true;
                    pending = f.Load();
                    f.Transport.WaitForDetail();
                }
                else if (change == "Reattach") f.Reattach(f.Transport.Catalog["episode"]);
                else if (change == "NewMenu") ExtraMenu(f);
                else if (change == "SignOut") f.SignOut();
                else Invoke(f.Vm, "Cancel");
                OpenExtra(f, selection, option);
                Assert.Equal(0, f.Actions.Plays);
                f.Transport.DetailRelease.TrySetResult(true);
                if (pending is not null) Await(pending);
            });

        foreach (var change in new[] { "Cancel", "Reattach", "SignOut", "NewMenu" })
            Test("花絮详情：" + change + " 后迟到列表不生成菜单或落入新页", () =>
            {
                using var f = ExtrasFixture(assembly, root);
                Await(f.Load());
                f.Transport.HoldExtras = true;
                var pending = (Task)Invoke(f.Vm, "GetExtrasAsync")!;
                Await(f.Transport.ExtrasRequested.Task);
                if (change == "Reattach") f.Reattach(f.Transport.Catalog["episode"]);
                else if (change == "SignOut") f.SignOut();
                else if (change == "NewMenu")
                {
                    f.Transport.HoldExtras = false;
                    ExtraMenu(f);
                }
                else Invoke(f.Vm, "Cancel");
                f.Transport.ExtrasRelease.TrySetResult(true);
                Await(pending);
                Assert.True(Result(pending) is null);
                if (change != "NewMenu") Assert.Equal("Collapsed", f.Get("ExtraVisibility")!.ToString());
            });

        Test("花絮详情：另一菜单生成的选项不能借当前菜单播放", () =>
        {
            using var f = ExtrasFixture(assembly, root);
            Await(f.Load());
            var first = ExtraMenu(f);
            var option = ExtraOptions(first)[0];
            var second = ExtraMenu(f);
            OpenExtra(f, second, option);
            Assert.Equal(0, f.Actions.Plays);
        });

        Test("花絮详情：仅关闭菜单立即撤销旧选择且入口可重新打开", () =>
        {
            using var f = ExtrasFixture(assembly, root);
            Await(f.Load());
            var selection = ExtraMenu(f);
            var option = ExtraOptions(selection)[0];
            var close = f.Vm.GetType().GetMethod("CloseExtras", Members);
            Assert.NotNull(close, "关闭菜单必须撤销已生成的播放选择");
            close!.Invoke(f.Vm, [selection]);
            Assert.False((bool)Invoke(f.Vm, "OwnsExtras", selection)!);
            OpenExtra(f, selection, option);
            Assert.Equal(0, f.Actions.Plays);
            Assert.Equal("Visible", f.Get("ExtraVisibility")!.ToString());
            var reopened = ExtraMenu(f);
            OpenExtra(f, reopened, ExtraOptions(reopened)[0]);
            Assert.Equal(1, f.Actions.Plays);
        });

        Test("花絮详情：关闭在飞菜单拒绝迟到结果但保留入口", () =>
        {
            using var f = ExtrasFixture(assembly, root);
            Await(f.Load());
            f.Transport.HoldExtras = true;
            var pending = (Task)Invoke(f.Vm, "GetExtrasAsync")!;
            Await(f.Transport.ExtrasRequested.Task);
            Invoke(f.Vm, "CloseExtras", (object?)null);
            f.Transport.ExtrasRelease.TrySetResult(true);
            Await(pending);
            Assert.True(Result(pending) is null);
            Assert.Equal("Visible", f.Get("ExtraVisibility")!.ToString());
            Assert.Equal(0, f.Actions.Plays);
        });

        Test("花絮详情：旧关闭不会撤销新菜单且旧重试意图失效", () =>
        {
            using var f = ExtrasFixture(assembly, root);
            Await(f.Load());
            var old = ExtraMenu(f);
            Invoke(f.Vm, "CloseExtras", old);
            Assert.True((bool)Invoke(f.Vm, "CanRetryExtras", old)!);
            var current = ExtraMenu(f);
            Invoke(f.Vm, "CloseExtras", old);
            Assert.True((bool)Invoke(f.Vm, "OwnsExtras", current)!);
            Assert.False((bool)Invoke(f.Vm, "CanRetryExtras", old)!);
            OpenExtra(f, current, ExtraOptions(current)[0]);
            Assert.Equal(1, f.Actions.Plays);
        });
    }

    private static Fixture ExtrasFixture(Assembly assembly, string root, MediaServerKind kind = MediaServerKind.Emby, int count = 1)
    {
        var f = new Fixture(assembly, root, Shape.Movie, kind: kind,
            version: kind == MediaServerKind.Jellyfin ? "10.10.7" : "4.10.1.0");
        typeof(EmbyItem).GetProperty("MediaType")?.SetValue(f.Seed, "Video");
        typeof(EmbyItem).GetProperty("SpecialFeatureCount")?.SetValue(f.Seed, count);
        f.Transport.Extras.Add(Extra());
        return f;
    }

    private static EmbyItem Extra() => new()
    {
        Id = "extra", Name = "幕后花絮", Type = EmbyItemType.Video,
        UserData = new() { PlaybackPositionTicks = 300_000_000 },
        MediaSources = [new() { Id = "extra-file", Container = "mkv", RunTimeTicks = 600_000_000 }]
    };

    private static object ExtraMenu(Fixture f)
    {
        var method = f.Vm.GetType().GetMethod("GetExtrasAsync", Members);
        Assert.NotNull(method, "尚未实现实际详情花絮入口");
        var task = (Task)method!.Invoke(f.Vm, null)!;
        Await(task);
        Assert.NotNull(Result(task), "花絮菜单未返回");
        return Result(task)!;
    }

    private static object? Result(Task task) => task.GetType().GetProperty("Result")!.GetValue(task);
    private static IList ExtraOptions(object selection) => (IList)selection.GetType().GetProperty("Options")!.GetValue(selection)!;
    private static object? ExtraError(object selection) => selection.GetType().GetProperty("Error")!.GetValue(selection);
    private static void OpenExtra(Fixture f, object selection, object? option) => Await((Task)Invoke(f.Vm, "OpenExtraAsync", selection, option)!);
}
