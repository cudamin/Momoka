#if ITEM_USER_STATE_ORDER_FIXTURE
using System.Net;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Controls;
using static Momoka.Tests.ItemUserStateFixture;

namespace Momoka.Tests;

internal static class FnNasUserStateCardTests
{
    public static void Register()
    {
        foreach (var watched in new[] { true, false })
            foreach (var turnOn in new[] { true, false })
                Case($"R-024 真卡片{(watched ? "已看" : "收藏")}={turnOn}确认后503，保留状态并警告刷新", async () =>
                {
                    await using var f = Native(watched, !turnOn);
                    f.Hover(watched ? CardAction.Watched : CardAction.Favorite);
                    f.Handler.At(0).Complete(null, noContent: false);
                    await ItemUserStateContext.UntilAsync(() => f.Handler.Count == 2, "confirmed native write did not start separate read");
                    Assert.Equal(turnOn, watched ? f.Card.Watched : f.Card.IsFavorite);
                    Assert.Equal("ReadUserData", f.Handler.At(1).Field);
                    f.Handler.At(1).Fail(HttpStatusCode.ServiceUnavailable);
                    await ItemUserStateContext.UntilAsync(() => f.PendingChanges == 0, "native refresh did not release pending order");
                    Assert.Equal(turnOn, watched ? f.Card.Watched : f.Card.IsFavorite);
                    Assert.Equal(1, f.Shell.Notices.Count);
                    Assert.Equal(InfoBarSeverity.Warning, f.Shell.Notices[0].Severity);
                    Assert.Contains("已保存", f.Shell.Notices[0].Message);
                    Assert.Contains("刷新失败", f.Shell.Notices[0].Message);
                    Assert.Equal(2, f.Handler.Count, "只一次写和一次读，不重发确认写");
                });
        Case("R-024 真卡片写入失败不翻状态、不补读或触发已看回调", async () =>
        {
            await using var f = Native(watched: true, initial: false);
            f.Menu(ItemCommand.MarkPlayed);
            f.Handler.At(0).Fail();
            await ItemUserStateContext.UntilAsync(() => f.PendingChanges == 0, "failed native write left pending state");
            Assert.False(f.Card.Watched);
            Assert.Equal(1, f.Handler.Count);
            Assert.Equal(0, f.WatchedCallbacks);
            Assert.Equal(InfoBarSeverity.Error, f.Shell.Notices.Single().Severity);
        });
        Case("R-024 真卡片补读权限失败仍是错误，不伪造读回成功或重发写", async () =>
        {
            await using var f = Native(watched: false, initial: false);
            f.Hover(CardAction.Favorite);
            f.Handler.At(0).Complete(null, noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.Handler.Count == 2, "missing native followup");
            f.Handler.At(1).Fail(HttpStatusCode.Forbidden);
            await ItemUserStateContext.UntilAsync(() => f.PendingChanges == 0, "permission failure left pending state");
            Assert.True(f.Card.IsFavorite);
            Assert.Equal(InfoBarSeverity.Error, f.Shell.Notices.Single().Severity);
            Assert.Contains("已保存", f.Shell.Notices.Single().Message);
            Assert.Equal(2, f.Handler.Count);
        });
        Case("R-024 真卡片确认后来源撤销，不接迟到补读或新默认账号", async () =>
        {
            await using var f = Native(watched: true, initial: false);
            f.Hover(CardAction.Watched);
            f.Handler.At(0).Complete(null, noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.Handler.Count == 2, "missing native followup");
            Assert.True(f.Card.Watched);
            f.RevokeSource();
            f.Handler.At(1).Complete(State(played: false, favorite: true), noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.PendingChanges == 0, "revocation left native command pending");
            Assert.True(f.Card.Watched);
            Assert.False(f.Card.IsFavorite);
            Assert.Equal(0, f.Shell.Notices.Count);
        });
        Case("R-024 已看/收藏并发，迟到独立补读不能覆盖另一组确认位", async () =>
        {
            await using var f = Native(watched: true, initial: false);
            f.Hover(CardAction.Watched);
            f.Handler.At(0).Complete(null, noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.Handler.Count == 2, "missing first followup");
            f.Hover(CardAction.Favorite);
            f.Handler.At(2).Complete(null, noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.Handler.Count == 4, "missing second followup");
            f.Handler.At(3).Complete(State(played: false, favorite: false), noContent: false);
            f.Handler.At(1).Complete(State(played: false, favorite: false), noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.PendingChanges == 0, "concurrent native followups did not finish");
            Assert.True(f.Card.Watched);
            Assert.True(f.Card.IsFavorite);
            Assert.Equal(0, f.Shell.Notices.Count);
        });
        Case("R-024 新未看已确认，旧已看补读不得复活状态或进度", async () =>
        {
            await using var f = Native(watched: true, initial: false);
            f.Menu(ItemCommand.MarkPlayed);
            f.Handler.At(0).Complete(null, noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.Handler.Count == 2, "missing old followup");
            f.Menu(ItemCommand.MarkUnplayed);
            f.Handler.At(2).Complete(null, noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.Handler.Count == 4, "missing new followup");
            f.Handler.At(3).Complete(State(played: false, position: 0), noContent: false);
            f.Handler.At(1).Complete(State(played: true, position: 40_000), noContent: false);
            await ItemUserStateContext.UntilAsync(() => f.PendingChanges == 0, "same-field native followups did not finish");
            Assert.False(f.Card.Watched);
            Assert.Equal(0L, f.Card.Item.ResumeTicks);
            Assert.Equal(1, f.WatchedCallbacks);
        });
    }
    private static ItemUserStateFixture Native(bool watched, bool initial) => new(
        State(played: watched && initial, favorite: !watched && initial), kind: MediaServerKind.FnNas);
    private static void Case(string name, Func<Task> action) => TestHarness.Test(name, () => ItemUserStateContext.Run(action));
}
#endif
