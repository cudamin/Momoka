using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual current dialog, caller and Core service; only native picker/UI and HTTP are inert boundaries.</summary>
internal static class JellyfinUploadDialogTests
{
    internal static void Register()
    {
        foreach (var grant in new bool?[] { true, false, null })
            Case($"普通账号上传区明确授权[{grant}]与单版本隐藏选择", async ui =>
            {
                using var f = Fixture(grant: grant);
                await f.OpenAsync();
                Assert.Equal(grant == true ? Visibility.Visible : Visibility.Collapsed, f.Dialog.UploadSection.Visibility);
                Assert.Equal(Visibility.Collapsed, f.Dialog.VersionRow.Visibility);
                await f.CloseAsync();
                Assert.Equal(0, f.Transport.Writes);
            });

        Case("Emby仍隐藏原生上传和多版本选择", async ui =>
        {
            using var f = new SubtitleFixture(uploadGrant: true, multiVersion: true);
            await f.OpenAsync();
            Assert.Equal(Visibility.Collapsed, f.Dialog.UploadSection.Visibility);
            Assert.Equal(Visibility.Collapsed, f.Dialog.VersionRow.Visibility);
            await f.CloseAsync();
        });

        Case("文件选择取消和无草稿提交均零请求", async ui =>
        {
            using var f = Fixture();
            await f.OpenAsync();
            SubtitlePickerBoundary.Pick = () => Task.FromResult<ServerSubtitleFile?>(null);
            f.Dialog.FixturePick();
            await ui.IdleAsync();
            await f.Dialog.FixtureUpload();
            Assert.Equal(Visibility.Collapsed, f.Dialog.UploadDraft.Visibility);
            Assert.Equal(0, f.Transport.Writes);
            Assert.False(f.Dialog.Touched);
            Assert.False(f.Dialog.NeedsRefresh);
            await f.CloseAsync();
        });

        Case("非首版本上传原字节与标记不影响首版本", async ui =>
        {
            using var f = Fixture(multi: true);
            f.Transport.LoseResponse = false;
            await f.OpenAsync();
            Assert.Equal(Visibility.Visible, f.Dialog.VersionRow.Visibility);
            f.Dialog.FixtureVersion(1);
            await ui.IdleAsync();
            await Pick(f, ui);
            f.Dialog.UploadLanguageBox.SelectedIndex = 1;
            f.Dialog.UploadForced.IsOn = true;
            f.Dialog.UploadHearingImpaired.IsOn = true;
            await f.Dialog.FixtureUpload();
            var request = f.Transport.Requests.Single(request => request.Method == "POST");
            Assert.True(request.Path.EndsWith("/Videos/22222222222222222222222222222222/Subtitles", StringComparison.Ordinal));
            using var body = JsonDocument.Parse(f.Transport.LastUploadBody!);
            Assert.Equal("//4tTodlAAo=", body.RootElement.GetProperty("Data").GetString());
            Assert.Equal("eng", body.RootElement.GetProperty("Language").GetString());
            Assert.True(body.RootElement.GetProperty("IsForced").GetBoolean());
            Assert.True(body.RootElement.GetProperty("IsHearingImpaired").GetBoolean());
            Assert.Equal(1, f.Transport.Tracks.Count);
            Assert.Equal(2, f.Transport.TracksB.Count);
            Assert.True(f.Dialog.Touched);
            Assert.Equal(Visibility.Collapsed, f.Dialog.UploadDraft.Visibility);
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
            f.AssertOriginalOnly();
        });

        Case("切版本清草稿候选和删除确认", async ui =>
        {
            using var f = Fixture(multi: true);
            await f.OpenAsync();
            f.Transport.SearchGate = new();
            f.Dialog.FixtureSearch();
            await f.Transport.SearchEntered.Task;
            f.Transport.SearchGate.SetResult([new() { Id = "old", Name = "old-candidate" }]);
            await ui.IdleAsync();
            Assert.Equal(1, f.Dialog.FixtureCandidates.Count);
            await Pick(f, ui);
            f.Dialog.FixtureAskDelete();
            Assert.Equal(Visibility.Visible, f.Dialog.DeleteConfirmation.Visibility);
            f.Dialog.FixtureVersion(1);
            await ui.IdleAsync();
            Assert.Equal(Visibility.Collapsed, f.Dialog.DeleteConfirmation.Visibility);
            Assert.Equal(Visibility.Collapsed, f.Dialog.UploadDraft.Visibility);
            Assert.Equal(0, f.Dialog.FixtureCandidates.Count);
            await f.Dialog.FixtureUpload();
            await f.Dialog.FixtureConfirmDelete();
            Assert.Equal(0, f.Transport.Writes);
            Assert.True(f.Dialog.FixtureTracks[0].Label.Contains("original-b", StringComparison.Ordinal));
            await f.CloseAsync();
        });

        Case("旧版本搜索迟到不填新版本或解除新写忙态", async ui =>
        {
            using var f = Fixture(multi: true);
            await f.OpenAsync();
            f.Transport.SearchGate = new();
            f.Dialog.FixtureSearch();
            await f.Transport.SearchEntered.Task;
            f.Dialog.FixtureVersion(1);
            f.Transport.WriteGate = new();
            await Pick(f, ui, waitIdle: false);
            var upload = f.Dialog.FixtureUpload();
            await f.Transport.WriteEntered.Task;
            f.Transport.SearchGate.SetResult([new() { Id = "late", Name = "late-old" }]);
            await Task.Yield();
            Assert.Equal(0, f.Dialog.FixtureCandidates.Count);
            Assert.True(f.Dialog.FixtureBusy);
            Assert.False(f.Dialog.FixtureClose());
            f.Transport.WriteGate.SetResult();
            await upload;
            await ui.IdleAsync();
            Assert.Equal(1, f.Transport.Writes);
            await f.CloseAsync();
        });

        Case("旧版本刷新迟到不替换新轨道", async ui =>
        {
            using var f = Fixture(multi: true);
            await f.OpenAsync();
            var read = f.Transport.PlanRead(hold: true);
            f.Dialog.FixtureRefresh();
            await read.Entered.Task;
            f.Dialog.FixtureVersion(1);
            read.Gate.SetResult();
            await ui.IdleAsync();
            Assert.True(f.Dialog.FixtureTracks[0].Label.Contains("original-b", StringComparison.Ordinal));
            Assert.Equal(0, f.Transport.Writes);
            await f.CloseAsync();
        });

        foreach (var newRead in new[] { false, true })
            Case($"A刷新迟到{(newRead ? "不覆盖同A更新读取" : "不单独改变服务快照")}且当前删除可用", async ui =>
            {
                using var f = Fixture(multi: true);
                f.Transport.LoseResponse = false;
                await f.OpenAsync();
                var oldRead = f.Transport.PlanRead(hold: true);
                f.Dialog.FixtureRefresh();
                await oldRead.Entered.Task;
                f.Dialog.FixtureVersion(1);
                if (newRead)
                {
                    f.Dialog.FixtureVersion(0);
                    f.Dialog.FixtureRefresh();
                    await Task.Yield();
                    Assert.False(f.Dialog.FixtureBusy);
                }
                oldRead.Gate.SetResult();
                await ui.IdleAsync();
                if (!newRead) f.Dialog.FixtureVersion(0);
                Assert.Equal(0, f.Dialog.VersionBox.SelectedIndex);
                f.Dialog.FixtureAskDelete();
                await f.Dialog.FixtureConfirmDelete();
                Assert.Equal(1, f.Transport.Writes);
                Assert.Equal(0, f.Transport.Tracks.Count);
                Assert.True(f.Dialog.Touched);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
            });

        Case("旧版本文件选择迟到不生成跨版本草稿", async ui =>
        {
            using var f = Fixture(multi: true);
            await f.OpenAsync();
            var picked = new TaskCompletionSource<ServerSubtitleFile?>();
            SubtitlePickerBoundary.Pick = () => picked.Task;
            f.Dialog.FixturePick();
            f.Dialog.FixtureVersion(1);
            picked.SetResult(File());
            await ui.IdleAsync();
            Assert.Equal(Visibility.Collapsed, f.Dialog.UploadDraft.Visibility);
            await f.Dialog.FixtureUpload();
            Assert.Equal(0, f.Transport.Writes);
            await f.CloseAsync();
        });

        Case("上传授权收回立即收起且恢复原草稿", async ui =>
        {
            using var f = Fixture();
            await f.OpenAsync();
            await Pick(f, ui);
            f.Dialog.UploadForced.IsOn = true;
            f.GrantUpload(false);
            await Task.Yield();
            Assert.Equal(Visibility.Collapsed, f.Dialog.UploadSection.Visibility);
            await f.Dialog.FixtureUpload();
            Assert.Equal(0, f.Transport.Writes);
            f.GrantUpload(true);
            await Task.Yield();
            Assert.Equal(Visibility.Visible, f.Dialog.UploadSection.Visibility);
            Assert.Equal(Visibility.Visible, f.Dialog.UploadDraft.Visibility);
            Assert.True(f.Dialog.UploadForced.IsOn);
            await f.CloseAsync();
        });

        Case("写在飞和成功自动回读锁定版本及关闭", async ui =>
        {
            using var f = Fixture(multi: true);
            f.Transport.LoseResponse = false;
            await f.OpenAsync();
            await Pick(f, ui);
            f.Transport.PlanRead(); // Prewrite source validation.
            var read = f.Transport.PlanRead(hold: true);
            var upload = f.Dialog.FixtureUpload();
            await read.Entered.Task;
            Assert.True(f.Dialog.Touched);
            f.Dialog.FixtureVersion(1);
            Assert.Equal(0, f.Dialog.VersionBox.SelectedIndex);
            Assert.False(f.Dialog.FixtureClose());
            await f.Dialog.FixtureUpload();
            Assert.Equal(1, f.Transport.Writes);
            read.Gate.SetResult();
            await upload;
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
        });

        foreach (var indexed in new[] { false, true })
            Case($"204确认后{(indexed ? "回读失败" : "索引未更新")}不重复写入", async ui =>
            {
                using var f = Fixture();
                f.Transport.LoseResponse = false;
                f.Transport.IndexUploads = indexed;
                await f.OpenAsync();
                await Pick(f, ui);
                f.Transport.PlanRead();
                if (indexed) f.Transport.PlanRead(failure: HttpStatusCode.ServiceUnavailable);
                await f.Dialog.FixtureUpload();
                Assert.True(f.Dialog.Touched);
                Assert.True(f.Dialog.NeedsRefresh);
                Assert.True(f.Dialog.Status.Text.Contains("服务器已接收", StringComparison.Ordinal));
                Assert.Equal(Visibility.Collapsed, f.Dialog.UploadDraft.Visibility);
                await f.Dialog.FixtureUpload();
                f.Dialog.FixtureRefresh();
                await ui.IdleAsync();
                Assert.Equal(1, f.Transport.Writes);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
            });

        Case("未知上传只读恢复不能切版继续写", async ui =>
        {
            using var f = Fixture(multi: true);
            await f.OpenAsync();
            await Pick(f, ui);
            await f.Dialog.FixtureUpload();
            Assert.False(f.Dialog.Touched);
            Assert.True(f.Dialog.NeedsRefresh);
            f.Dialog.FixtureVersion(1);
            Assert.Equal(0, f.Dialog.VersionBox.SelectedIndex);
            f.Dialog.FixtureRefresh();
            await ui.IdleAsync();
            await f.Dialog.FixtureUpload();
            f.Dialog.FixtureDownload();
            f.Dialog.FixtureAskDelete();
            await f.Dialog.FixtureConfirmDelete();
            Assert.Equal(1, f.Transport.Writes);
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
        });

        foreach (var deleting in new[] { false, true })
            Case($"未知{(deleting ? "删除" : "下载")}回读失败后A-B-A不能解除写保护", async ui =>
            {
                using var f = Fixture(multi: true);
                await f.OpenAsync();
                if (deleting)
                {
                    f.Dialog.FixtureAskDelete();
                    await f.Dialog.FixtureConfirmDelete();
                }
                else
                {
                    f.Dialog.FixtureDownload("same-candidate");
                    await ui.IdleAsync();
                }
                Assert.Equal(1, f.Transport.Writes);
                Assert.True(f.Dialog.NeedsRefresh);
                f.Transport.PlanRead(failure: HttpStatusCode.ServiceUnavailable);
                f.Dialog.FixtureRefresh();
                await ui.IdleAsync();
                f.Dialog.FixtureVersion(1);
                Assert.Equal(0, f.Dialog.VersionBox.SelectedIndex);
                f.Dialog.FixtureVersion(0);
                f.Dialog.FixtureSearch();
                f.Dialog.FixtureDownload("same-candidate");
                if (f.Dialog.FixtureTracks.Count > 0) f.Dialog.FixtureAskDelete();
                await f.Dialog.FixtureConfirmDelete();
                await f.Dialog.FixtureUpload();
                await ui.IdleAsync();
                Assert.Equal(1, f.Transport.Writes);
                f.Dialog.FixtureRefresh();
                await ui.IdleAsync();
                f.Dialog.FixtureVersion(1);
                Assert.Equal(0, f.Dialog.VersionBox.SelectedIndex);
                Assert.True(f.Dialog.FixtureStale);
                Assert.False(f.Dialog.Touched);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
            });

        Case("下载明确403拒绝可刷新切版后重试", async ui =>
        {
            using var f = Fixture(multi: true);
            f.Transport.WriteFailure = HttpStatusCode.Forbidden;
            await f.OpenAsync();
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            Assert.False(f.Dialog.NeedsRefresh);
            f.Dialog.FixtureRefresh();
            await ui.IdleAsync();
            f.Dialog.FixtureVersion(1);
            Assert.Equal(1, f.Dialog.VersionBox.SelectedIndex);
            f.Transport.WriteFailure = null;
            f.Transport.LoseResponse = false;
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            Assert.Equal(2, f.Transport.Writes);
            Assert.Equal(1, f.Transport.AppliedWrites);
            await f.CloseAsync();
        });

        foreach (var deleting in new[] { false, true })
            Case($"已确认{(deleting ? "删除" : "上传")}回读失败锁版直到只读恢复", async ui =>
            {
                using var f = Fixture(multi: true);
                f.Transport.LoseResponse = false;
                await f.OpenAsync();
                if (!deleting) await Pick(f, ui);
                f.Transport.PlanRead(); // Target/snapshot validation.
                f.Transport.PlanRead(failure: HttpStatusCode.ServiceUnavailable);
                if (deleting)
                {
                    f.Dialog.FixtureAskDelete();
                    await f.Dialog.FixtureConfirmDelete();
                }
                else await f.Dialog.FixtureUpload();
                Assert.True(f.Dialog.Touched);
                Assert.True(f.Dialog.FixtureStale);
                Assert.False(f.Dialog.VersionBox.IsEnabled);
                f.Dialog.FixtureVersion(1);
                Assert.Equal(0, f.Dialog.VersionBox.SelectedIndex);
                f.Dialog.FixtureRefresh();
                await ui.IdleAsync();
                Assert.False(f.Dialog.FixtureStale);
                Assert.True(f.Dialog.VersionBox.IsEnabled);
                f.Dialog.FixtureVersion(1);
                Assert.Equal(1, f.Dialog.VersionBox.SelectedIndex);
                Assert.Equal(1, f.Transport.Writes);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
            });

        Case("选择等待中离页丢弃草稿且服务零写入", async ui =>
        {
            using var f = Fixture();
            await f.OpenAsync();
            var picked = new TaskCompletionSource<ServerSubtitleFile?>();
            SubtitlePickerBoundary.Pick = () => picked.Task;
            f.Dialog.FixturePick();
            f.Owner.XamlRoot = null;
            f.Owner.FixtureUnload();
            picked.SetResult(File());
            await ui.IdleAsync();
            await f.Dialog.FixtureUpload();
            Assert.Equal(0, f.Transport.Writes);
            Assert.Equal(Visibility.Collapsed, f.Dialog.UploadDraft.Visibility);
            await f.CloseAsync();
        });

        Case("仍挂原根的回收Unloaded不撤销字幕上传", async ui =>
        {
            using var f = Fixture();
            f.Transport.LoseResponse = false;
            await f.OpenAsync();
            f.Owner.FixtureUnload();
            await Pick(f, ui);
            await f.Dialog.FixtureUpload();
            Assert.Equal(1, f.Transport.Writes);
            Assert.True(f.Dialog.Touched);
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
        });

        Case("Page卸载即使保留根也丢弃待选文件", async ui =>
        {
            using var f = Fixture();
            await f.OpenAsync();
            var picked = new TaskCompletionSource<ServerSubtitleFile?>();
            SubtitlePickerBoundary.Pick = () => picked.Task;
            f.Dialog.FixturePick();
            f.OwnerPage.FixtureUnload();
            picked.SetResult(File());
            await ui.IdleAsync();
            Assert.Equal(Visibility.Collapsed, f.Dialog.UploadDraft.Visibility);
            await f.Dialog.FixtureUpload();
            Assert.Equal(0, f.Transport.Writes);
            await f.CloseAsync();
            Assert.Equal(0, f.Changed);
        });

        Case("Page离开保留根时已选草稿不可上传", async ui =>
        {
            using var f = Fixture();
            await f.OpenAsync();
            await Pick(f, ui);
            f.OwnerPage.IsLoaded = false;
            await f.Dialog.FixtureUpload();
            Assert.Equal(0, f.Transport.Writes);
            await f.CloseAsync();
            Assert.Equal(0, f.Changed);
        });

        Case("204后Page卸载保留成功并不刷新离开父页", async ui =>
        {
            using var f = Fixture();
            f.Transport.LoseResponse = false;
            await f.OpenAsync();
            await Pick(f, ui);
            f.Transport.AfterUpload = () => f.OwnerPage.FixtureUnload();
            await f.Dialog.FixtureUpload();
            Assert.True(f.Dialog.Touched);
            Assert.True(f.Dialog.NeedsRefresh);
            await f.CloseAsync();
            Assert.Equal(1, f.Transport.Writes);
            Assert.Equal(0, f.Changed);
        });

        Case("204接收后注销保留成功但不刷新新父", async ui =>
        {
            using var f = Fixture();
            f.Transport.LoseResponse = false;
            await f.OpenAsync();
            await Pick(f, ui);
            f.Transport.AfterUpload = f.Revoke;
            await f.Dialog.FixtureUpload();
            Assert.True(f.Dialog.Touched);
            Assert.True(f.Dialog.NeedsRefresh);
            await f.CloseAsync();
            Assert.Equal(0, f.Changed);
            Assert.Equal(1, f.Transport.Writes);
        });
    }

    private static SubtitleFixture Fixture(bool multi = false, bool? grant = true) => new(MediaServerKind.Jellyfin, multiVersion: multi, uploadGrant: grant);
    private static ServerSubtitleFile File() => new("原字节.srt", [0xff, 0xfe, 0x2d, 0x4e, 0x87, 0x65, 0, 0x0a]);
    private static async Task Pick(SubtitleFixture fixture, UiQueue ui, bool waitIdle = true)
    {
        SubtitlePickerBoundary.Pick = () => Task.FromResult<ServerSubtitleFile?>(File());
        fixture.Dialog.FixturePick();
        if (waitIdle) await ui.IdleAsync();
        Assert.Equal(Visibility.Visible, fixture.Dialog.UploadDraft.Visibility);
    }
    private static void Case(string name, Func<UiQueue, Task> action) => Test("Jellyfin 原生字幕 " + name, () =>
    {
        using var ui = new UiQueue();
        ui.Run(() => action(ui));
    });
}
