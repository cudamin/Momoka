using System.Net;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Jellyfin's completed HTTP request has no reliable download receipt or candidate-to-track ID.</summary>
internal static class SubtitleReceiptTests
{
    internal static void Register()
    {
        foreach (var version in new[] { "10.10.7", "12.4" })
        {
            Case(version + " 204未新增保留未确认和原父刷新", async ui =>
            {
                using var f = Fixture(version);
                f.Transport.ApplyDownloads = false;
                await f.OpenAsync();
                f.Dialog.FixtureDownload();
                await ui.IdleAsync();
                AssertPending(f);
                Assert.Equal(1, f.Dialog.FixtureTracks.Count);
                Assert.Equal(2, f.Transport.Reads);
                f.Dialog.FixtureDownload();
                f.Dialog.FixtureAskDelete();
                await f.Dialog.FixtureConfirmDelete();
                await ui.IdleAsync();
                Assert.Equal(1, f.Transport.Writes, "A completed but unconfirmed request must not reopen writes");
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
                Assert.Equal(1, f.DisplayedTracks);
                Assert.Equal(3, f.Transport.Reads);
                f.AssertOriginalOnly();
            });

            Case(version + " 204新增轨道只传播已读列表不冒充候选确认", async ui =>
            {
                using var f = Fixture(version);
                await f.OpenAsync();
                f.Dialog.FixtureDownload();
                await ui.IdleAsync();
                AssertPending(f);
                Assert.Equal(2, f.Dialog.FixtureTracks.Count);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
                Assert.Equal(2, f.DisplayedTracks);
                Assert.Equal(1, f.Transport.Writes);
            });
        }

        Case("204延后可见只读刷新不解除未确认写保护", async ui =>
        {
            using var f = Fixture();
            f.Transport.ApplyDownloads = false;
            await f.OpenAsync();
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            AssertPending(f);
            f.Transport.Tracks.Add(new MediaStream { Index = 2, Type = "Subtitle", Codec = "srt", IsExternal = true, Title = "later" });
            f.Dialog.FixtureRefresh();
            await ui.IdleAsync();
            AssertPending(f);
            Assert.Equal(2, f.Dialog.FixtureTracks.Count);
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            Assert.Equal(1, f.Transport.Writes);
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
            Assert.Equal(2, f.DisplayedTracks);
        });

        Case("204确认读失败立即记欠项恢复后仍不宣称成功", async ui =>
        {
            using var f = Fixture();
            f.Transport.ApplyDownloads = false;
            await f.OpenAsync();
            f.Transport.PlanRead(failure: HttpStatusCode.ServiceUnavailable);
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            AssertPending(f);
            Assert.Contains("列表未刷新", f.Dialog.Status.Text);
            f.Transport.PlanRead(failure: HttpStatusCode.ServiceUnavailable);
            f.Dialog.FixtureRefresh();
            await ui.IdleAsync();
            AssertPending(f);
            Assert.Contains("刷新字幕列表失败", f.Dialog.Status.Text);
            f.Dialog.FixtureRefresh();
            await ui.IdleAsync();
            AssertPending(f);
            Assert.Equal(1, f.Dialog.FixtureTracks.Count);
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
            Assert.Equal(1, f.Transport.Writes);
        });

        Case("204自动确认读在飞仍阻止关闭和并发写", async ui =>
        {
            using var f = Fixture();
            await f.OpenAsync();
            var read = f.Transport.PlanRead(hold: true);
            f.Dialog.FixtureDownload();
            var pending = ui.IdleAsync();
            await read.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(f.Dialog.NeedsRefresh, "Record the ambiguous receipt before the confirmation GET settles");
            Assert.False(f.Dialog.Touched);
            Assert.False(f.Dialog.FixtureClose());
            f.Dialog.FixtureDownload();
            f.Dialog.FixtureRefresh();
            Assert.Equal(1, f.Transport.Writes);
            Assert.Equal(2, f.Transport.Reads);
            read.Gate.SetResult();
            await pending;
            AssertPending(f);
            await f.CloseAsync();
        });

        Case("204手动恢复读尚未结束关闭仍交原父", async ui =>
        {
            using var f = Fixture();
            await f.OpenAsync();
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            var status = f.Dialog.Status.Text;
            var read = f.Transport.PlanRead(hold: true);
            f.Dialog.FixtureRefresh();
            var pending = ui.IdleAsync();
            await read.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
            read.Gate.SetResult();
            await pending;
            Assert.Equal(2, f.DisplayedTracks);
            Assert.True(f.Dialog.FixtureClosed);
            Assert.Equal(status, f.Dialog.Status.Text, "Late recovery cannot repaint a closed dialog");
            Assert.False(f.Dialog.Touched);
            Assert.True(f.Dialog.NeedsRefresh);
            Assert.Equal(1, f.Transport.Writes);
        });

        Case("204确认读期间切来源不交新父或追认成功", async ui =>
        {
            using var f = Fixture();
            await f.OpenAsync();
            var read = f.Transport.PlanRead(hold: true);
            f.Dialog.FixtureDownload();
            var pending = ui.IdleAsync();
            await read.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            f.SwitchDefault();
            read.Gate.SetResult();
            await pending;
            Assert.False(f.Dialog.Touched);
            Assert.True(f.Dialog.NeedsRefresh);
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
            Assert.Equal(1, f.Transport.Writes);
            f.AssertOriginalOnly();
        });

        Case("Jellyfin明确403未写拒绝不新造欠项", async ui =>
        {
            using var f = Fixture();
            f.Transport.WriteFailure = HttpStatusCode.Forbidden;
            await f.OpenAsync();
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            Assert.False(f.Dialog.Touched);
            Assert.False(f.Dialog.NeedsRefresh);
            await f.CloseAsync();
            Assert.Equal(0, f.Changed);
            Assert.Equal(1, f.Transport.Reads);
        });

        Case("Emby明确回执仍成功补读并交原父", async ui =>
        {
            using var f = new SubtitleFixture();
            f.Transport.LoseResponse = false;
            await f.OpenAsync();
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            Assert.True(f.Dialog.Touched);
            Assert.Contains("已挂上", f.Dialog.Status.Text);
            Assert.False(f.Dialog.FixtureStale);
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
            Assert.Equal(2, f.DisplayedTracks);
            Assert.Equal(1, f.Transport.Writes);
        });
    }

    private static SubtitleFixture Fixture(string version = "10.10.7")
    {
        var fixture = new SubtitleFixture(MediaServerKind.Jellyfin, version);
        fixture.Transport.LoseResponse = false;
        return fixture;
    }

    private static void AssertPending(SubtitleFixture fixture)
    {
        Assert.False(fixture.Dialog.Touched, "A Jellyfin 204 is not a reliable candidate download confirmation");
        Assert.True(fixture.Dialog.NeedsRefresh);
        Assert.True(fixture.Dialog.FixtureStale, "A read does not resolve the candidate's missing receipt");
        Assert.Contains("请求已返回", fixture.Dialog.Status.Text);
        Assert.Contains("未确认", fixture.Dialog.Status.Text);
        Assert.False(fixture.Dialog.Status.Text.Contains("已挂上", StringComparison.Ordinal));
    }

    private static void Case(string name, Func<UiQueue, Task> scenario) => Test("R04-004 " + name, () =>
    {
        using var ui = new UiQueue();
        ui.Run(() => scenario(ui));
    });
}
