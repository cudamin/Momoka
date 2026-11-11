using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class RemovalCommandTests
{
    internal static void Register()
    {
        Test("飞牛移出命令：取消确认零写，当前版本继续可播", () =>
        {
            using var f = Prepare();
            f.Dialogs.ConfirmResult = ContentDialogResult.None;
            f.Await(f.Command("FnNasRemoveFromLibraryAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(0, f.Transport.Deletes);
            Assert.Equal(0, f.Changed + f.Removed);
            Assert.True(f.CanPlay);
        });
        Test("飞牛移出命令：确认整个条目只保留文件并撤销真详情目标", () =>
        {
            using var f = Prepare();
            f.Transport.ExpectRetainedFile = true;
            f.Await(f.Command("FnNasRemoveFromLibraryAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(1, f.Transport.Deletes);
            Assert.Contains("\"delete_file\":0", f.Transport.Requests.Single(request => request.Method == "DELETE").Body);
            Assert.Equal(1, f.Removed);
            Assert.False(f.CanPlay);
        });
        Test("飞牛移出命令：版本表Secondary仅点名版本，取消与普通权限零写", () =>
        {
            using var f = Prepare();
            f.Transport.ExpectRetainedFile = true;
            f.Dialogs.VersionResult = ContentDialogResult.Secondary;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(1, f.Transport.Deletes);
            Assert.Equal("version-1", f.Transport.DeletedIds.Single());
            Assert.False(f.CanPlay);
        });
        Test("飞牛移出命令：确认期间权限降级与来源撤销零写", () =>
        {
            using var f = Prepare();
            f.Dialogs.BeforeConfirm = () => f.Actions.Permissions = new(IsAdministrator: false);
            f.Await(f.Command("FnNasRemoveFromLibraryAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(0, f.Transport.Deletes);
            Assert.True(f.CanPlay);
        });
        Test("飞牛移出命令：已确认后回调失败仅提示刷新失败，不重复DELETE", () =>
        {
            using var f = Prepare();
            f.Transport.ExpectRetainedFile = true;
            f.Await(f.Command("FnNasRemoveFromLibraryAsync", null, _ => throw new InvalidOperationException("fixture-refresh-failed")));
            Assert.Equal(1, f.Transport.Deletes);
            Assert.True(f.Actions.Notices.Any(row => row.Severity == "Warning" && row.Message.Contains("已移出", StringComparison.Ordinal)));
        });
        Test("飞牛移出命令：版本确认后旧服务器列表重开不能重复DELETE", () =>
        {
            using var f = Prepare();
            f.Transport.ExpectRetainedFile = true;
            f.Dialogs.VersionResult = ContentDialogResult.Secondary;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(1, f.Transport.Deletes);
        });
        Test("飞牛移出命令：连续移出两版滞后清单仍识别最后一版并撤主体", () =>
        {
            using var f = new DeleteFixture(sourceCount: 2);
            f.Actions.Permissions = new(IsAdministrator: true, CanDeleteContent: true);
            f.Transport.FilesJson = """{"code":0,"data":[{"media_guid":"version-1","file_path":"/anonymous/1.mkv"},{"media_guid":"version-2","file_path":"/anonymous/2.mkv"}]}""";
            f.Transport.ExpectRetainedFile = true;
            f.Dialogs.VersionResult = ContentDialogResult.Secondary;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            f.Dialogs.Selection = 1;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(2, f.Transport.Deletes);
            Assert.Equal(1, f.Removed);
            Assert.False(f.CanPlay);
        });
    }
    private static DeleteFixture Prepare()
    {
        var fixture = new DeleteFixture();
        fixture.Actions.Permissions = new(IsAdministrator: true, CanDeleteContent: true);
        return fixture;
    }
}
