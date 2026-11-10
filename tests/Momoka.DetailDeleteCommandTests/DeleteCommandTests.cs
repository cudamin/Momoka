using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DeleteCommandTests
{
    private const string TwoFiles = """{"code":0,"msg":"","data":[{"media_guid":"version-1","file_path":"/anonymous/1.mkv","file_size":1048576,"height":1080},{"media_guid":"version-2","path":"/anonymous/2.mkv","size":2097152,"resolution":"4K"}]}""";
    public static void Register()
    {
        Test("飞牛续播命令：真实等待确认期间来源退出，不写入也不回调", () =>
        {
            using var f = new DeleteFixture();
            f.Card.Item.IsFnNasResumeRecord = true;
            f.Dialogs.PendingConfirm = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshed = 0;
            var operation = f.Command("FnNasRemoveContinueAsync", () => refreshed++, f.RemovedCallback());
            f.Context.Drain();
            Assert.Equal(1, f.Dialogs.Confirmations.Count);
            Assert.False(operation.IsCompleted);
            f.Revoke();
            f.Dialogs.PendingConfirm.SetResult(ContentDialogResult.Primary);
            f.Await(operation);
            Assert.Equal(0, f.Transport.ContinueDeletes);
            Assert.Equal(0, refreshed);
        });
        Test("飞牛续播命令：确认提交在飞时拒绝同项重入，完成可重试", () =>
        {
            using var f = new DeleteFixture();
            f.Card.Item.IsFnNasResumeRecord = true;
            f.Transport.HoldContinue = true;
            var refreshed = 0;
            var first = f.Command("FnNasRemoveContinueAsync", () => refreshed++, f.RemovedCallback());
            f.Context.Await(f.Transport.ContinueEntered.Task);
            f.Await(f.Command("FnNasRemoveContinueAsync", () => refreshed++, f.RemovedCallback()));
            Assert.Equal(1, f.Transport.ContinueDeletes);
            Assert.Equal(1, f.Dialogs.Confirmations.Count);
            f.Transport.ReleaseContinue();
            f.Await(first);
            Assert.Equal(1, refreshed);
            f.Await(f.Command("FnNasRemoveContinueAsync", () => refreshed++, f.RemovedCallback()));
            Assert.Equal(2, f.Transport.ContinueDeletes);
        });
        Test("飞牛续播命令：确认后仅删除播放记录，刷新不走媒体删除", () =>
        {
            using var f = new DeleteFixture();
            f.Card.Item.IsFnNasResumeRecord = true;
            var original = f.Card.Item.UserData;
            var refreshed = 0;
            f.Await(f.Command("FnNasRemoveContinueAsync", () => refreshed++, f.RemovedCallback()));
            Assert.Equal(1, f.Transport.ContinueDeletes);
            Assert.Equal(0, f.Transport.Deletes);
            Assert.Equal(1, refreshed);
            Assert.Equal(0, f.Removed);
            Assert.True(ReferenceEquals(original, f.Card.Item.UserData));
            Assert.Equal("移除", f.Dialogs.Confirmations.Single().PrimaryButtonText);
            Assert.Contains("断点续播", (string)f.Dialogs.Confirmations.Single().Content!);
        });
        foreach (var boundary in new[] { "cancel", "revoked", "ordinary", "denied", "detail" })
            Test("飞牛续播命令：" + boundary + "不得刷新或误删媒体", () =>
            {
                using var f = new DeleteFixture();
                f.Card.Item.IsFnNasResumeRecord = boundary != "ordinary";
                if (boundary == "cancel") f.Dialogs.ConfirmResult = ContentDialogResult.None;
                if (boundary == "revoked") f.Dialogs.BeforeConfirm = f.Revoke;
                if (boundary == "denied") f.Transport.ContinueStatus = System.Net.HttpStatusCode.Forbidden;
                if (boundary == "detail")
                {
                    f.Attach("item");
                    f.Card.Item.IsFnNasResumeRecord = true;
                    typeof(CardItem).GetProperty(nameof(CardItem.AllowResumeRemoval))!.SetValue(f.Card, false);
                }
                var refreshed = 0;
                f.Await(f.Command("FnNasRemoveContinueAsync", () => refreshed++, f.RemovedCallback()));
                Assert.Equal(boundary == "denied" ? 1 : 0, f.Transport.ContinueDeletes);
                Assert.Equal(0, refreshed);
                Assert.Equal(0, f.Removed);
                Assert.Equal(0, f.Transport.Deletes);
            });
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test($"合集实际命令：{kind}移除成员只发送关系请求且保留媒体", () =>
            {
                using var f = new DeleteFixture(kind);
                f.Card.CollectionMembership = new CollectionMembership(f.Scope, "set", f.Card.Item.Id, default);
                f.Actions.Permissions = new(IsAdministrator: false, CanManageCollections: true);
                var changed = 0;
                var command = typeof(Momoka.Shell.Views.ItemCommands).GetMethod("RemoveCollectionMemberAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                f.Await((Task)command.Invoke(null, [f.Scope, new CommandActions(f.Actions), f.Card, (Action)(() => changed++)])!);
                Assert.Equal(1, f.Transport.CollectionWrites);
                Assert.Equal(0, f.Transport.MediaDeletes);
                Assert.Equal(1, changed);
                Assert.True(f.CanPlay);
                Assert.Equal(1, f.SourceCount);
            });
        }
        Test("合集实际命令：取消页面上下文后零写零刷新", () =>
        {
            using var f = new DeleteFixture(MediaServerKind.Emby);
            using var page = new CancellationTokenSource();
            f.Card.CollectionMembership = new CollectionMembership(f.Scope, "set", f.Card.Item.Id, page.Token);
            page.Cancel();
            var changed = 0;
            var command = typeof(Momoka.Shell.Views.ItemCommands).GetMethod("RemoveCollectionMemberAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            f.Await((Task)command.Invoke(null, [f.Scope, new CommandActions(f.Actions), f.Card, (Action)(() => changed++)])!);
            Assert.Equal(0, f.Transport.CollectionWrites);
            Assert.Equal(0, changed);
        });
        Test("合集实际命令：写失败保留卡片，零刷新并反馈错误", () =>
        {
            using var f = new DeleteFixture(MediaServerKind.Jellyfin);
            f.Card.CollectionMembership = new CollectionMembership(f.Scope, "set", f.Card.Item.Id, default);
            f.Actions.Permissions = new(IsAdministrator: false, CanManageCollections: true);
            f.Transport.DeleteStatus = System.Net.HttpStatusCode.Forbidden;
            var changed = 0;
            var command = typeof(Momoka.Shell.Views.ItemCommands).GetMethod("RemoveCollectionMemberAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            f.Await((Task)command.Invoke(null, [f.Scope, new CommandActions(f.Actions), f.Card, (Action)(() => changed++)])!);
            Assert.Equal(1, f.Transport.CollectionWrites);
            Assert.Equal(0, changed);
            Assert.True(f.CanPlay);
            Assert.True(f.Actions.Notices.Any(notice => notice.Severity == "Error"));
        });
        Test("合集实际命令：关系写入成功时已离页，不刷新新页面", () =>
        {
            using var f = new DeleteFixture(MediaServerKind.Jellyfin);
            using var page = new CancellationTokenSource();
            f.Card.CollectionMembership = new CollectionMembership(f.Scope, "set", f.Card.Item.Id, page.Token);
            f.Actions.Permissions = new(IsAdministrator: false, CanManageCollections: true);
            f.Transport.HoldDelete = true;
            var changed = 0;
            var command = typeof(ItemCommands).GetMethod("RemoveCollectionMemberAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var writing = (Task)command.Invoke(null, [f.Scope, new CommandActions(f.Actions), f.Card, (Action)(() => changed++)])!;
            f.Context.Until(() => f.Transport.DeleteEntered.Task.IsCompleted);
            page.Cancel();
            f.Transport.ReleaseDelete();
            f.Await(writing);
            Assert.Equal(1, f.Transport.CollectionWrites);
            Assert.Equal(0, changed);
            Assert.Equal(0, f.Transport.MediaDeletes);
            Assert.True(f.CanPlay);
        });

        Test("R07-007 删除确认期间明确降权，提交前拒绝旧确认", () =>
        {
            using var f = new DeleteFixture(MediaServerKind.Emby);
            f.Dialogs.BeforeConfirm = () => f.Actions.Permissions = new(CanDeleteContent: false);
            f.Await(f.Command("DeleteAsync", f.ChangedCallback(), f.RemovedCallback()));
            Assert.Equal(0, f.Transport.Deletes);
            Assert.Equal(0, f.Removed);
            Assert.True(f.CanPlay);
        });
        Test("R07-007 旧直接菜单入口明确禁止权限时不打开确认", () =>
        {
            using var f = new DeleteFixture(MediaServerKind.Emby);
            f.Actions.Permissions = new(CanDeleteContent: false);
            f.More(ItemCommand.Delete);
            f.Context.Drain();
            Assert.Equal(0, f.Dialogs.Confirmations.Count);
            Assert.Equal(0, f.Transport.Deletes);
        });
        Test("真实确认入口：库样式与普通/MP样式分离，关闭仍不代表同意", () =>
        {
            using var context = new FixtureContext();
            context.Await(CheckAsync());
            async Task CheckAsync()
            {
                var owner = new Microsoft.UI.Xaml.FrameworkElement();
                var inspected = 0;
                var library = await Momoka.Shell.Views.ConfirmDialog.ForLibrary(owner, dialog =>
                {
                    Assert.True(ReferenceEquals(dialog.Style, Microsoft.UI.Xaml.Application.Current.Resources["EgLibraryDialogSurfaceStyle"]));
                    Assert.True(ReferenceEquals(dialog.Resources["ContentDialogTopOverlay"], Microsoft.UI.Xaml.Application.Current.Resources["LayerFillColorDefaultBrush"]));
                    Assert.Equal(ContentDialogButton.Close, dialog.DefaultButton);
                    inspected++;
                    return Task.FromResult(ContentDialogResult.None);
                })("离线库确认", "不删除媒体", "删除");
                var ordinary = await Momoka.Shell.Views.ConfirmDialog.For(owner, dialog =>
                {
                    Assert.Null(dialog.Style);
                    Assert.False(dialog.Resources.ContainsKey("ContentDialogTopOverlay"));
                    inspected++;
                    return Task.FromResult(ContentDialogResult.None);
                })("普通确认", "不修改任何数据", "确定");
                Assert.False(library);
                Assert.False(ordinary);
                Assert.Equal(2, inspected, "确认框安全吞异常不能伪装检查通过");
            }
        });
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
            Assert.Equal(3, f.Transport.Gets);
            Assert.Equal(1, f.Transport.GenreGets);
            Assert.Equal(1, f.Transport.SubjectGets);
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
            Assert.Equal(2, f.Transport.Gets);
            Assert.Equal(1, f.Transport.GenreGets);
            Assert.Equal(1, f.Transport.SubjectGets);
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
            Assert.Equal(1, f.Transport.GenreGets);
            Assert.Equal(1, f.Transport.SubjectGets);
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
            Assert.Equal(1, f.Transport.GenreGets);
            Assert.Equal(1, f.Transport.SubjectGets);
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
