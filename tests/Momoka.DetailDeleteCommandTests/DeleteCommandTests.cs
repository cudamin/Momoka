using System.Text.Json;
using Momoka.Emby;
using Microsoft.UI.Xaml.Controls;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DeleteCommandTests
{
    private const string TwoFiles = """{"code":0,"msg":"","data":[{"media_guid":"version-1","file_path":"/anonymous/1.mkv","file_size":1048576,"height":1080},{"media_guid":"version-2","path":"/anonymous/2.mkv","size":2097152,"resolution":"4K"}]}""";
    public static void Register()
    {
        Test("实际FnNasVersions最后版本确认DELETE→removed→真VM清主体", () =>
        {
            using var f = new DeleteFixture();
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(1, f.Transport.Gets);
            Assert.Equal(1, f.Transport.Deletes);
            Assert.Equal("version-1", f.Transport.DeletedIds.Single());
            Assert.Equal(1, f.Dialogs.VersionDialogs);
            Assert.Equal(1, f.Dialogs.Confirmations.Count);
            Assert.Contains("最后一个版本", f.Dialogs.Confirmations[0].Content?.ToString());
            Assert.Equal(1, f.Removed);
            Assert.Equal(0, f.Changed);
            Cleared(f);
        });
        Test("实际FnNasVersions非last撤chosen源→changed→GET失败仍保存活目标", () =>
        {
            using var f = new DeleteFixture(sourceCount: 2);
            f.Transport.FilesJson = TwoFiles;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            f.Context.Until(() => f.Get("Busy") is false);
            Assert.Equal(1, f.Transport.Deletes);
            Assert.Equal(2, f.Transport.Gets);
            Assert.Equal("version-1", f.Transport.DeletedIds.Single());
            Assert.Equal(0, f.Removed);
            Assert.Equal(1, f.Changed);
            Assert.Equal(1, f.Card.Item.MediaSources.Count);
            Assert.Equal("version-2", f.SelectedId);
            Assert.True(f.CanPlay);
            Assert.Equal(1, f.SourceCount);
        });
        Test("实际版本JSON来自media/itemfile，取消列表零写零回调", () =>
        {
            using var f = new DeleteFixture(sourceCount: 2);
            f.Transport.FilesJson = TwoFiles;
            f.Dialogs.VersionResult = ContentDialogResult.None;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(1, f.Transport.Gets);
            Assert.Equal(0, f.Transport.Deletes);
            Assert.Equal(0, f.Changed + f.Removed);
            Assert.Equal(0, f.Dialogs.Confirmations.Count);
            Assert.Equal(2, f.Dialogs.Versions.Count);
            Assert.Equal("/anonymous/2.mkv", f.Dialogs.Versions[1].Path);
            Assert.Equal("4K", f.Dialogs.Versions[1].Resolution);
            Assert.Equal(2097152L, f.Dialogs.Versions[1].Size);
            Assert.True(f.CanPlay);
        });
        Test("实际版本第二层确认取消零DELETE，保留原源", () =>
        {
            using var f = new DeleteFixture();
            f.Dialogs.ConfirmResult = ContentDialogResult.None;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(1, f.Dialogs.Confirmations.Count);
            Assert.Equal(0, f.Transport.Deletes);
            Assert.Equal(0, f.Changed + f.Removed);
            Assert.Equal("version-1", f.SelectedId);
            Assert.True(f.CanPlay);
        });
        Test("实际版本DELETE HTTP失败Guard报错拒绝成功回调/撤源", () =>
        {
            using var f = new DeleteFixture();
            f.Transport.DeleteStatus = System.Net.HttpStatusCode.Forbidden;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(1, f.Transport.Deletes);
            UnchangedAfterFailure(f);
        });
        Test("实际版本DELETE HTTP200业务失败Tell不得伪装成功", () =>
        {
            using var f = new DeleteFixture();
            f.Transport.DeleteJson = """{"code":777,"msg":"anonymous failure","data":null}""";
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(1, f.Transport.Deletes);
            UnchangedAfterFailure(f);
        });
        Test("无XamlRoot实际版本命令零HTTP/零确认", () =>
        {
            using var f = new DeleteFixture();
            f.Owner.XamlRoot = null;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(0, f.Transport.Requests.Count);
            Assert.Equal(0, f.Dialogs.VersionDialogs + f.Dialogs.Confirmations.Count);
            Assert.Equal(0, f.Changed + f.Removed);
        });
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test("实际主体DELETE→真实捕获移除清播放/选项 " + kind, () =>
            {
                using var f = new DeleteFixture(kind);
                f.Await(f.Command(kind == MediaServerKind.FnNas ? "FnNasDeleteAsync" : "DeleteAsync", f.ChangedCallback(), f.RemovedCallback()));
                Assert.Equal(1, f.Transport.Deletes);
                Assert.Equal(0, f.Transport.Gets);
                Assert.Equal(1, f.Removed);
                Assert.Equal(0, f.Changed);
                if (kind == MediaServerKind.FnNas) Assert.Equal(0, f.Transport.DeletedIds.Length);
                Cleared(f);
            });
            Test("实际主体确认取消零HTTP " + kind, () =>
            {
                using var f = new DeleteFixture(kind);
                f.Dialogs.ConfirmResult = ContentDialogResult.None;
                f.Await(f.Command("DeleteAsync", f.ChangedCallback(), f.RemovedCallback()));
                Assert.Equal(1, f.Dialogs.Confirmations.Count);
                Assert.Equal(0, f.Transport.Requests.Count);
                Assert.Equal(0, f.Changed + f.Removed);
                Assert.True(f.CanPlay);
            });
        }
        Test("实际主体DELETE迟到完成跨Attach不清新item", () =>
        {
            using var f = new DeleteFixture();
            f.Transport.HoldDelete = true;
            var pending = f.Command("FnNasDeleteAsync", f.ChangedCallback(), f.RemovedCallback());
            f.Context.Until(() => f.Transport.DeleteEntered.Task.IsCompleted);
            f.Attach("new-item");
            f.Transport.ReleaseDelete();
            f.Await(pending);
            Assert.Equal(1, f.Transport.Deletes);
            Assert.Equal("new-item", ((EmbyItem)f.Get("PlayTarget")!).Id);
            Assert.Equal("new-item", ((EmbyItem)f.Get("CurrentItem")!).Id);
            Assert.True(f.CanPlay);
            Assert.Equal(0, f.Transport.Gets);
        });
        Test("正常真VM GET失败不能伪装主体删除", () =>
        {
            using var f = new DeleteFixture();
            f.Reload();
            Assert.Equal(1, f.Transport.Gets);
            Assert.Equal(0, f.Transport.Deletes);
            Assert.NotNull(f.Get("PlayTarget"));
            Assert.NotNull(f.Get("CurrentItem"));
            Assert.True(f.CanPlay);
        });
        Test("原样OnMore→实际Invoke last路由交回真实removed而非刷新", () =>
        {
            using var f = new DeleteFixture();
            f.More(ItemCommand.ManageVersions);
            f.Context.Until(() => f.Actions.Refreshes == 1);
            Assert.Equal(1, f.Transport.Deletes);
            Assert.Equal(1, f.Transport.Gets);
            Cleared(f);
        });
        Test("选中外源B时实际nonlast删除A仅撤A源，保B同ID源/选择/目标", () =>
        {
            using var f = new DeleteFixture(sourceCount: 2);
            f.Transport.FilesJson = TwoFiles;
            var selected = f.SelectForeignCopy();
            var target = (EmbyItem)f.Get("PlayTarget")!;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            f.Context.Until(() => f.Get("Busy") is false);
            Assert.Equal(1, f.Transport.Deletes);
            Assert.Equal("version-1", f.Transport.DeletedIds.Single());
            Assert.Equal(2, f.SourceCount);
            Assert.True(ReferenceEquals(selected, f.Get("SelectedSource")));
            Assert.True(ReferenceEquals(target, f.Get("PlayTarget")));
            Assert.Equal(1, target.MediaSources.Count);
            Assert.Equal("version-1", f.SelectedId);
            Assert.True(f.CanPlay);
            Assert.Equal(1, f.Changed);
            Assert.Equal(0, f.Removed);
        });
        Test("非last实际DELETE完成后lateApply旧源过滤，不复活已删picker", () =>
        {
            using var f = new DeleteFixture(sourceCount: 2);
            f.Transport.FilesJson = TwoFiles;
            f.Await(f.Command("FnNasVersionsAsync", f.ChangedCallback(), f.RemovedCallback()));
            f.Context.Until(() => f.Get("Busy") is false);
            f.ApplySame(2);
            Assert.Equal(1, ((EmbyItem)f.Get("CurrentItem")!).MediaSources.Count);
            Assert.Equal("version-2", f.SelectedId);
            Assert.True(f.CanPlay);
        });
    }
    private static void Cleared(DeleteFixture f)
    {
        Assert.Null(f.Get("PlayTarget"));
        Assert.Null(f.Get("CurrentItem"));
        Assert.Null(f.Get("SelectedSource"));
        Assert.Equal(0, f.SourceCount);
        Assert.False(f.CanPlay);
        Assert.True(f.Get("NoticeOpen") is true);
        var requests = f.Transport.Requests.Count;
        f.Start();
        f.Reload();
        Assert.Equal(0, f.Actions.Plays);
        Assert.Equal(requests, f.Transport.Requests.Count, "清除后不再GET/播放");
    }
    private static void UnchangedAfterFailure(DeleteFixture f)
    {
        Assert.Equal(0, f.Changed + f.Removed);
        Assert.Equal(0, f.Actions.Refreshes);
        Assert.Equal(1, f.Card.Item.MediaSources.Count);
        Assert.True(f.CanPlay);
        Assert.True(f.Actions.Notices.Any(notice => notice.Severity == "Error"));
    }
}
