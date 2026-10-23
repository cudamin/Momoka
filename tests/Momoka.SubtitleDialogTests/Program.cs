using System.Net;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private enum Mutation { Download, Delete }

    private static int Main()
    {
        foreach (var mutation in Enum.GetValues<Mutation>())
        {
            Case(mutation + " 首笔响应丢失直接Closed交父一次", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                await Mutate(f, ui, mutation);
                AssertUnknown(f, mutation);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed, "The first ambiguous write must hand off its refresh debt");
                Assert.Equal(ExpectedTracks(mutation), f.DisplayedTracks);
                Assert.True(f.Dialog.NeedsRefresh);
                Assert.Equal(InitialReads(mutation) + 1, f.Transport.Reads);
                Assert.Equal(1, f.Transport.Writes);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed, "Closed must not replay changed");
            });

            foreach (var failed in new[] { false, true })
            {
                Case(mutation + " 恢复GET挂起ReadClosed迟到" + (failed ? "失败" : "成功"), async ui =>
                {
                    using var f = new SubtitleFixture();
                    await f.OpenAsync();
                    await Mutate(f, ui, mutation);
                    var before = f.Dialog.FixtureTracks.Select(track => track.Label).ToArray();
                    var plan = f.Transport.PlanRead(hold: true, failure: failed ? HttpStatusCode.ServiceUnavailable : null);
                    f.Dialog.FixtureRefresh();
                    await plan.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.True(f.Dialog.FixtureBusy);
                    try
                    {
                        await f.CloseAsync();
                        Assert.Equal(1, f.Changed, "Hand off while recovery GET is still held");
                        Assert.False(plan.Gate.Task.IsCompleted);
                    }
                    finally { plan.Gate.TrySetResult(); }
                    await ui.IdleAsync();
                    Assert.Equal(1, f.Changed, "ReadClosed must hand off before/independently of its GET completion");
                    Assert.Equal(ExpectedTracks(mutation), f.DisplayedTracks);
                    Assert.True(f.Dialog.NeedsRefresh);
                    Assert.False(f.Dialog.Touched);
                    Assert.Equal(string.Join("|", before), string.Join("|", f.Dialog.FixtureTracks.Select(track => track.Label)),
                        "A late GET cannot repopulate the closed dialog");
                    Assert.Equal(InitialReads(mutation) + 2, f.Transport.Reads);
                    Assert.Equal(1, f.Transport.Writes, "Recovery is read-only");
                });
            }

            Case(mutation + " 响应不明好回读只GET再交父", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                await Mutate(f, ui, mutation);
                f.Dialog.FixtureRefresh();
                await ui.IdleAsync();
                Assert.False(f.Dialog.Touched, "A good GET is not a successful write acknowledgement");
                Assert.True(f.Dialog.NeedsRefresh);
                Assert.False(f.Dialog.FixtureStale);
                Assert.Equal(ExpectedTracks(mutation), f.Dialog.FixtureTracks.Count);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
                Assert.Equal(InitialReads(mutation) + 2, f.Transport.Reads);
                Assert.Equal(1, f.Transport.Writes);
            });

            Case(mutation + " 响应不明回读失败仍欠刷新", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                await Mutate(f, ui, mutation);
                f.Transport.PlanRead(failure: HttpStatusCode.ServiceUnavailable);
                f.Dialog.FixtureRefresh();
                await ui.IdleAsync();
                Assert.False(f.Dialog.Touched);
                Assert.True(f.Dialog.FixtureStale);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
                Assert.True(f.Dialog.NeedsRefresh);
                Assert.Equal(ExpectedTracks(mutation), f.DisplayedTracks);
                Assert.Equal(1, f.Transport.Writes);
            });

            Case(mutation + " 真写在飞互斥关闭重复写恢复读", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                f.Transport.WriteGate = new();
                Task pending;
                if (mutation == Mutation.Download)
                {
                    f.Dialog.FixtureDownload();
                    pending = ui.IdleAsync();
                }
                else
                {
                    f.Dialog.FixtureAskDelete();
                    pending = f.Dialog.FixtureConfirmDelete();
                }
                await f.Transport.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(f.Dialog.FixtureClose(), "Real mutating close guard remains in force");
                Assert.False(f.Dialog.FixtureClosed);
                var reads = f.Transport.Reads;
                f.Dialog.FixtureDownload();
                await f.Dialog.FixtureConfirmDelete();
                f.Dialog.FixtureRefresh();
                Assert.Equal(1, f.Transport.Writes);
                Assert.Equal(reads, f.Transport.Reads);
                f.Transport.WriteGate.SetResult();
                await pending;
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
                Assert.Equal(1, f.Transport.Writes);
            });

            Case(mutation + " 不明未恢复不盲目重放", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                await Mutate(f, ui, mutation);
                f.Dialog.FixtureDownload();
                if (f.Dialog.FixtureTracks.Count > 0) f.Dialog.FixtureAskDelete();
                await f.Dialog.FixtureConfirmDelete();
                await ui.IdleAsync();
                Assert.Equal(1, f.Transport.Writes);
                Assert.Equal(InitialReads(mutation), f.Transport.Reads);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
            });

            Case(mutation + " 非首笔已确认后再不明不加写", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                f.Transport.LoseResponse = false;
                f.Dialog.FixtureDownload("first");
                await ui.IdleAsync();
                Assert.True(f.Dialog.Touched);
                f.Transport.LoseResponse = true;
                await Mutate(f, ui, mutation);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
                Assert.Equal(2, f.Transport.Writes);
                Assert.Equal(mutation == Mutation.Download ? 3 : 1, f.DisplayedTracks);
            });

            Case(mutation + " 正常成功补读失败保留R024", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                f.Transport.LoseResponse = false;
                if (mutation == Mutation.Delete) f.Transport.PlanRead(); // Actual deletion snapshot pre-read.
                f.Transport.PlanRead(failure: HttpStatusCode.ServiceUnavailable);
                await Mutate(f, ui, mutation);
                Assert.True(f.Dialog.Touched, "Confirmed success is still distinct from supplementary read failure");
                Assert.True(f.Dialog.FixtureStale);
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
                Assert.Equal(ExpectedTracks(mutation), f.DisplayedTracks);
                Assert.Equal(1, f.Transport.Writes);
            });

            Case(mutation + " 首笔不明关闭重开读新轨不重写", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                await Mutate(f, ui, mutation);
                var old = f.Dialog;
                await f.CloseAsync();
                await f.OpenAsync();
                Assert.Equal(ExpectedTracks(mutation), f.Dialog.FixtureTracks.Count);
                Assert.False(f.Dialog.Touched);
                Assert.False(f.Dialog.NeedsRefresh, "A new dialog cannot inherit an old operation as a successful write");
                old.FixtureDownload();
                old.FixtureRefresh();
                Assert.True(old.FixtureClose());
                await f.CloseAsync();
                Assert.Equal(1, f.Changed);
                Assert.Equal(1, f.Transport.Writes);
                Assert.Equal(InitialReads(mutation) + 2, f.Transport.Reads);
            });

            Case(mutation + " 欠项后离页真实父Cancel拒绝读取", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                await Mutate(f, ui, mutation);
                f.Parent.Cancel();
                await f.CloseAsync();
                Assert.Equal(1, f.Changed, "Caller hands off once; the real parent lifetime entry refuses inactive loading");
                Assert.Equal(InitialReads(mutation), f.Transport.Reads);
                Assert.Equal(1, f.Transport.Writes);
            });

            foreach (var revoked in new[] { false, true })
            {
                Case(mutation + " 欠项后" + (revoked ? "来源退出" : "默认切换") + "不借新来源读", async ui =>
                {
                    using var f = new SubtitleFixture();
                    await f.OpenAsync();
                    await Mutate(f, ui, mutation);
                    if (revoked) f.Revoke(); else f.SwitchDefault();
                    f.Dialog.FixtureRefresh();
                    await ui.IdleAsync();
                    await f.CloseAsync();
                    Assert.Equal(0, f.Changed, "Keep the caller's captured IsCurrent guard");
                    Assert.Equal(InitialReads(mutation), f.Transport.Reads);
                    Assert.True(f.Dialog.NeedsRefresh, "Source cancellation does not erase an already-incurred debt");
                    Assert.Equal(1, f.Transport.Writes);
                    f.AssertOriginalOnly();
                });
            }
        }

        Case("取消删除确认零写零欠项", async ui =>
        {
            using var f = new SubtitleFixture();
            await f.OpenAsync();
            f.Dialog.FixtureAskDelete();
            Assert.Contains("无法撤销", f.Dialog.DeleteConfirmationText.Text);
            f.Dialog.FixtureCancelDelete();
            await f.Dialog.FixtureConfirmDelete();
            await f.CloseAsync();
            AssertNoDebt(f, reads: 1, writes: 0);
        });
        Case("删除权限预核403零写零欠项", async ui =>
        {
            using var f = new SubtitleFixture();
            await f.OpenAsync();
            f.Transport.PlanRead(failure: HttpStatusCode.Forbidden);
            f.Dialog.FixtureAskDelete();
            await f.Dialog.FixtureConfirmDelete();
            await f.CloseAsync();
            AssertNoDebt(f, reads: 2, writes: 0);
        });
        Case("删除快照预核变化零写零欠项", async ui =>
        {
            using var f = new SubtitleFixture();
            await f.OpenAsync();
            f.Transport.Tracks[0].Title = "changed-by-other-client";
            f.Dialog.FixtureAskDelete();
            await f.Dialog.FixtureConfirmDelete();
            await f.CloseAsync();
            AssertNoDebt(f, reads: 2, writes: 0);
        });
        Case("下载候选预核失败零写零欠项", async ui =>
        {
            using var f = new SubtitleFixture();
            await f.OpenAsync();
            f.Dialog.FixtureDownload("");
            await ui.IdleAsync();
            await f.CloseAsync();
            AssertNoDebt(f, reads: 1, writes: 0);
        });
        Case("下载明确403拒绝不假造刷新欠项", async ui =>
        {
            using var f = new SubtitleFixture();
            await f.OpenAsync();
            f.Transport.WriteFailure = HttpStatusCode.Forbidden;
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            await f.CloseAsync();
            AssertNoDebt(f, reads: 1, writes: 1);
            Assert.Equal(0, f.Transport.AppliedWrites);
        });
        Case("委托入口身份取消零写零欠项", async ui =>
        {
            using var f = new SubtitleFixture();
            await f.OpenAsync();
            f.Revoke();
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
            await f.CloseAsync();
            AssertNoDebt(f, reads: 1, writes: 0);
        });
        Case("未写的手动GET失败不无端刷新父", async ui =>
        {
            using var f = new SubtitleFixture();
            await f.OpenAsync();
            f.Transport.PlanRead(failure: HttpStatusCode.ServiceUnavailable);
            f.Dialog.FixtureRefresh();
            await ui.IdleAsync();
            await f.CloseAsync();
            AssertNoDebt(f, reads: 2, writes: 0);
        });
        Case("未写的成功手动GET保留既有父刷新", async ui =>
        {
            using var f = new SubtitleFixture();
            await f.OpenAsync();
            f.Dialog.FixtureRefresh();
            await ui.IdleAsync();
            await f.CloseAsync();
            Assert.Equal(1, f.Changed);
            Assert.True(f.Dialog.NeedsRefresh);
            Assert.False(f.Dialog.Touched);
            Assert.Equal(3, f.Transport.Reads);
            Assert.Equal(0, f.Transport.Writes);
        });
        foreach (var loss in new Exception[] { new HttpRequestException("Virtual response lost"), new OperationCanceledException("Virtual transport timeout") })
        {
            Case("首笔已落实" + loss.GetType().Name + "只交刷新义务", async ui =>
            {
                using var f = new SubtitleFixture();
                await f.OpenAsync();
                f.Transport.ResponseLoss = loss;
                f.Dialog.FixtureDownload();
                await ui.IdleAsync();
                await f.CloseAsync();
                Assert.Equal(1, f.Transport.AppliedWrites);
                Assert.Equal(1, f.Changed);
                Assert.False(f.Dialog.Touched);
                Assert.True(f.Dialog.NeedsRefresh);
                Assert.Equal(1, f.Transport.Writes);
            });
        }
        return Run();
    }

    private static void Case(string name, Func<UiQueue, Task> scenario) => Test("R056 " + name, () =>
    {
        using var ui = new UiQueue();
        ui.Run(() => scenario(ui));
    });

    private static async Task Mutate(SubtitleFixture f, UiQueue ui, Mutation mutation)
    {
        if (mutation == Mutation.Download)
        {
            f.Dialog.FixtureDownload();
            await ui.IdleAsync();
        }
        else
        {
            f.Dialog.FixtureAskDelete();
            await f.Dialog.FixtureConfirmDelete();
        }
    }
    private static int InitialReads(Mutation mutation) => mutation == Mutation.Delete ? 2 : 1;
    private static int ExpectedTracks(Mutation mutation) => mutation == Mutation.Delete ? 0 : 2;

    private static void AssertUnknown(SubtitleFixture f, Mutation mutation)
    {
        Assert.False(f.Dialog.Touched, "Do not turn a lost response into a successful write");
        Assert.True(f.Dialog.FixtureStale);
        Assert.False(f.Dialog.FixtureBusy);
        Assert.True(f.Dialog.NeedsRefresh, "The debt is recorded before any close or recovery GET");
        Assert.Equal(ExpectedTracks(mutation), f.Transport.Tracks.Count, "The fake server really changed before throw");
        Assert.Equal(InitialReads(mutation), f.Transport.Reads, "Unknown acknowledgement cannot auto-GET or auto-write");
        Assert.Equal(1, f.Transport.Writes);
    }

    private static void AssertNoDebt(SubtitleFixture f, int reads, int writes)
    {
        Assert.False(f.Dialog.Touched);
        Assert.False(f.Dialog.NeedsRefresh);
        Assert.Equal(0, f.Changed);
        Assert.Equal(reads, f.Transport.Reads);
        Assert.Equal(writes, f.Transport.Writes);
    }
}
