#if ITEM_USER_STATE_ORDER_FIXTURE
using System.Net;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Controls;
using static Momoka.Tests.ItemUserStateFixture;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Source-linked real card commands with windowless click/value boundaries and fake HTTP only.</summary>
internal static class ItemUserStateOrderTests
{
    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("R-016: real ItemCommands/CardItem/Core; in-memory user-state requests, no login/config/native/playback.");
        Register();
        return TestHarness.Run();
    }

    public static void Register()
    {
        FnNasUserStateCardTests.Register();
        Test("R-016夹具：完成的异常不能被泵洗成成功", () =>
            Assert.Throws<InvalidOperationException>(() => ItemUserStateContext.Run(() => Task.FromException(new InvalidOperationException("synthetic pump failure")))));
        Test("R-016夹具：完成的取消不能被泵洗成成功", () =>
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<TaskCanceledException>(() => ItemUserStateContext.Run(() => Task.FromCanceled(canceled.Token)));
        });

        foreach (var watchedFirst in new[] { true, false })
            foreach (var reverse in new[] { true, false })
                foreach (var menuFirst in new[] { true, false })
                {
                    Case($"R-016：{(watchedFirst ? "已看→收藏" : "收藏→已看")}，{(reverse ? "逆序" : "顺序")}整份响应，首入口{(menuFirst ? "菜单" : "hover")}", async () =>
                    {
                        await using var f = new ItemUserStateFixture();
                        if (menuFirst) f.Menu(watchedFirst ? ItemCommand.MarkPlayed : ItemCommand.Favorite);
                        else f.Hover(watchedFirst ? CardAction.Watched : CardAction.Favorite);
                        var early = f.Handler.At(0);
                        if (menuFirst) f.Hover(watchedFirst ? CardAction.Favorite : CardAction.Watched, episode: true);
                        else f.Menu(watchedFirst ? ItemCommand.Favorite : ItemCommand.MarkPlayed);
                        var late = f.Handler.At(1);
                        Assert.Equal(watchedFirst ? "PlayedItems" : "FavoriteItems", early.Field);
                        Assert.Equal(watchedFirst ? "FavoriteItems" : "PlayedItems", late.Field);
                        var before = State(played: watchedFirst, favorite: !watchedFirst, position: 0, percentage: 100, unplayed: 0);
                        var after = State(played: true, favorite: true, position: 0, percentage: 100, unplayed: 0);
                        if (reverse) { await f.ReplyAsync(late, after); await f.ReplyAsync(early, before); }
                        else { await f.ReplyAsync(early, before); await f.ReplyAsync(late, after); }
                        Assert.True(f.Card.Watched, "both endpoint-confirmed bits must survive");
                        Assert.True(f.Card.IsFavorite, "both endpoint-confirmed bits must survive");
                        Assert.Equal(1, f.Shell.WatchedReports.Count);
                        Assert.Equal(1, f.WatchedCallbacks);
                        Assert.Equal(1, f.Shell.FavoriteRefreshes.Count);
                        Assert.True(f.Changes.Contains(nameof(CardItem.Watched)));
                        Assert.True(f.Changes.Contains(nameof(CardItem.IsFavorite)));
                        Assert.Equal("标记为未观看", f.Card.WatchedActionTip);
                        Assert.Equal("取消收藏", f.Card.FavoriteActionTip);
                        f.Hover(CardAction.Favorite);
                        var next = f.Handler.At(2);
                        Assert.Equal(HttpMethod.Delete, next.Method, "next hover direction uses the merged state");
                        await f.ReplyAsync(next, null, noContent: true);
                        Assert.False(f.Card.IsFavorite);
                        Assert.True(f.Card.Watched);
                    });
                }

        Case("R-016：不能全局丢旧已看响应，较新收藏快照尚未包含已看", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            f.Hover(CardAction.Favorite);
            await f.ReplyAsync(f.Handler.At(1), State(favorite: true));
            await f.ReplyAsync(f.Handler.At(0), State(played: true, favorite: false, position: 0, percentage: 100, unplayed: 0));
            Assert.True(f.Card.Watched);
            Assert.True(f.Card.IsFavorite);
            Assert.Equal(0L, f.Card.Item.UserData!.PlaybackPositionTicks);
        });

        Case("R-016：不能全局丢旧收藏响应，较新已看快照尚未包含收藏", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Favorite);
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(1), State(played: true, favorite: false, position: 0, percentage: 100, unplayed: 0));
            await f.ReplyAsync(f.Handler.At(0), State(played: false, favorite: true));
            Assert.True(f.Card.Watched);
            Assert.True(f.Card.IsFavorite);
            Assert.Equal(0L, f.Card.Item.UserData!.PlaybackPositionTicks);
        });

        foreach (var watchedFirst in new[] { true, false })
        {
            Case($"R-016：{(watchedFirst ? "已看" : "收藏")}先发，进度与计数由在飞已看响应确认，收藏旧快照不能恢复", async () =>
            {
                await using var f = new ItemUserStateFixture(type: EmbyItemType.Series);
                if (watchedFirst) { f.Hover(CardAction.Watched); f.Hover(CardAction.Favorite); }
                else { f.Hover(CardAction.Favorite); f.Hover(CardAction.Watched); }
                var watched = f.Handler.At(watchedFirst ? 0 : 1);
                var favorite = f.Handler.At(watchedFirst ? 1 : 0);
                var date = new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
                var confirmed = State(played: true, position: 0, percentage: 100, playCount: 8, unplayed: 0, lastPlayed: date);
                await f.ReplyAsync(watched, confirmed);
                await f.ReplyAsync(favorite, State(favorite: true, position: 50_000, percentage: 50, playCount: 1, unplayed: 7));
                Assert.True(f.Card.IsFavorite);
                WatchedFields(confirmed, f.Card.Item.UserData!);
                Assert.Equal("", f.Card.UnplayedText);
                Assert.Equal(0.0, f.Card.Progress);
            });
        }

        foreach (var reverse in new[] { true, false })
        {
            Case($"R-016：未看清进度与收藏并发，{(reverse ? "逆序" : "顺序")}响应不能复活续播点", async () =>
            {
                await using var f = new ItemUserStateFixture(State(played: true));
                f.Menu(ItemCommand.MarkUnplayed);
                f.Hover(CardAction.Favorite);
                var cleared = State(position: 0, percentage: 0, playCount: 0, unplayed: 4);
                var stale = State(played: true, favorite: true);
                if (reverse) { await f.ReplyAsync(f.Handler.At(1), stale); await f.ReplyAsync(f.Handler.At(0), cleared); }
                else { await f.ReplyAsync(f.Handler.At(0), cleared); await f.ReplyAsync(f.Handler.At(1), stale); }
                Assert.False(f.Card.Watched);
                Assert.True(f.Card.IsFavorite);
                WatchedFields(cleared, f.Card.Item.UserData!);
                Assert.Equal(0, f.Shell.WatchedReports.Count);
                Assert.Equal(0, f.WatchedCallbacks);
            });
        }

        foreach (var noContent in new[] { true, false })
            foreach (var reverse in new[] { true, false })
            {
                Case($"R-016：并发{(noContent ? "204" : "JSON null")}，{(reverse ? "逆序" : "顺序")}成功只补各自字段", async () =>
                {
                    await using var f = new ItemUserStateFixture();
                    f.Hover(CardAction.Watched);
                    f.Hover(CardAction.Favorite);
                    await f.ReplyAsync(f.Handler.At(reverse ? 1 : 0), null, noContent);
                    await f.ReplyAsync(f.Handler.At(reverse ? 0 : 1), null, noContent);
                    Assert.True(f.Card.Watched);
                    Assert.True(f.Card.IsFavorite);
                    Assert.Equal(40_000L, f.Card.Item.UserData!.PlaybackPositionTicks, "played no-body fallback must not invent a server progress answer");
                    Assert.Equal(1, f.Shell.WatchedReports.Count);
                    Assert.Equal(1, f.Shell.FavoriteRefreshes.Count);
                });
            }

        Case("R-016：初始UserData为空，null和204仍合并两位", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Card.Item.UserData = null;
            f.Hover(CardAction.Watched);
            f.Hover(CardAction.Favorite);
            await f.ReplyAsync(f.Handler.At(1), null, noContent: true);
            await f.ReplyAsync(f.Handler.At(0), null);
            Assert.True(f.Card.Watched);
            Assert.True(f.Card.IsFavorite);
        });

        Case("R-016：收藏单独请求仍可更新服务器进度、计数与日期快照", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Favorite);
            var confirmed = State(played: true, favorite: true, position: 0, percentage: 100, playCount: 9, unplayed: 0,
                lastPlayed: new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero));
            await f.ReplyAsync(f.Handler.At(0), confirmed);
            WatchedFields(confirmed, f.Card.Item.UserData!);
            Assert.True(f.Card.IsFavorite);
        });

        Case("R-016：非重叠顺序操作保留整份服务器快照的价值", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(0), State(played: true, position: 0, unplayed: 0));
            f.Hover(CardAction.Favorite);
            var newer = State(played: true, favorite: true, position: 0, percentage: 100, playCount: 10, unplayed: 0);
            await f.ReplyAsync(f.Handler.At(1), newer);
            WatchedFields(newer, f.Card.Item.UserData!);
        });

        Case("R-016：已看500失败不补成功字段与通知，收藏成功独立保留", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            f.Hover(CardAction.Favorite);
            await f.ReplyAsync(f.Handler.At(1), State(favorite: true));
            await f.FailAsync(f.Handler.At(0));
            Assert.False(f.Card.Watched);
            Assert.True(f.Card.IsFavorite);
            Assert.Equal(1, f.Shell.Notices.Count);
            Assert.Equal(InfoBarSeverity.Error, f.Shell.Notices[0].Severity);
            Assert.Equal(0, f.Shell.WatchedReports.Count);
            Assert.Equal(0, f.WatchedCallbacks);
        });

        Case("R-016：收藏500失败不补收藏，已看成功独立保留", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Favorite);
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(1), State(played: true, position: 0, unplayed: 0));
            await f.FailAsync(f.Handler.At(0));
            Assert.True(f.Card.Watched);
            Assert.False(f.Card.IsFavorite);
            Assert.Equal(1, f.Shell.Notices.Count);
            Assert.Equal(0, f.Shell.FavoriteRefreshes.Count);
        });

        Case("R-016：同字段已看→未看逆序，旧已看不得复活状态或触发撤格补报", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Menu(ItemCommand.MarkPlayed);
            f.Menu(ItemCommand.MarkUnplayed);
            var latest = State(position: 0, percentage: 0, playCount: 0, unplayed: 4);
            await f.ReplyAsync(f.Handler.At(1), latest);
            await f.ReplyAsync(f.Handler.At(0), State(played: true, position: 0, percentage: 100, unplayed: 0));
            Assert.False(f.Card.Watched);
            WatchedFields(latest, f.Card.Item.UserData!);
            Assert.Equal(0, f.Shell.WatchedReports.Count);
            Assert.Equal(0, f.WatchedCallbacks);
        });

        Case("R-016：同字段收藏→取消逆序，旧收藏不得复活或刷新", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Favorite);
            ItemCommands.Run(ItemCommand.Unfavorite, f.Session, f.Shell, f.Owner, f.Card);
            await f.ReplyAsync(f.Handler.At(1), State(favorite: false));
            await f.ReplyAsync(f.Handler.At(0), State(favorite: true));
            Assert.False(f.Card.IsFavorite);
            Assert.Equal(1, f.Shell.FavoriteRefreshes.Count);
        });

        foreach (var watched in new[] { true, false })
        {
            Case($"R-016：同字段{(watched ? "已看" : "收藏")}较新请求失败，不能全局丢掉较早已确认成功", async () =>
            {
                await using var f = new ItemUserStateFixture();
                f.Hover(watched ? CardAction.Watched : CardAction.Favorite);
                ItemCommands.Run(watched ? ItemCommand.MarkUnplayed : ItemCommand.Unfavorite, f.Session, f.Shell, f.Owner, f.Card);
                await f.FailAsync(f.Handler.At(1));
                await f.ReplyAsync(f.Handler.At(0), State(played: watched, favorite: !watched));
                Assert.Equal(watched, f.Card.Watched);
                Assert.Equal(!watched, f.Card.IsFavorite);
                Assert.Equal(1, f.Shell.Notices.Count);
            });
        }

        Case("R-016：同字段重复已看逆序只用最新进度与一次副作用", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            f.Hover(CardAction.Watched);
            var latest = State(played: true, position: 0, percentage: 100, playCount: 7, unplayed: 0);
            await f.ReplyAsync(f.Handler.At(1), latest);
            await f.ReplyAsync(f.Handler.At(0), State(played: true, position: 10_000, percentage: 10, playCount: 3, unplayed: 5));
            WatchedFields(latest, f.Card.Item.UserData!);
            Assert.Equal(1, f.Shell.WatchedReports.Count);
            Assert.Equal(1, f.WatchedCallbacks);
        });

        foreach (var noContent in new[] { true, false })
        {
            Case($"R-016：未看{(noContent ? "204" : "null")}与旧收藏整份响应，仍清空进度", async () =>
            {
                await using var f = new ItemUserStateFixture(State(played: true));
                f.Hover(CardAction.Watched);
                f.Hover(CardAction.Favorite);
                await f.ReplyAsync(f.Handler.At(0), null, noContent);
                await f.ReplyAsync(f.Handler.At(1), State(played: true, favorite: true));
                Assert.False(f.Card.Watched);
                Assert.True(f.Card.IsFavorite);
                Assert.Equal(0L, f.Card.Item.UserData!.PlaybackPositionTicks);
                Assert.Equal(0.0, f.Card.Item.UserData.PlayedPercentage);
                Assert.Equal(2, f.Card.Item.UserData.PlayCount, "no-body fallback must preserve unknown count");
                Assert.Equal(3, f.Card.Item.UserData.UnplayedItemCount, "no-body fallback must preserve unknown count");
            });

            Case($"R-016：较新收藏{(noContent ? "204" : "null")}与较早已看整份响应，都要保留", async () =>
            {
                await using var f = new ItemUserStateFixture();
                f.Hover(CardAction.Watched);
                f.Hover(CardAction.Favorite);
                await f.ReplyAsync(f.Handler.At(1), null, noContent);
                await f.ReplyAsync(f.Handler.At(0), State(played: true, favorite: false, position: 0, unplayed: 0));
                Assert.True(f.Card.Watched);
                Assert.True(f.Card.IsFavorite);
                Assert.Equal(0L, f.Card.Item.UserData!.PlaybackPositionTicks);
            });

            Case($"R-016：同字段较新未看{(noContent ? "204" : "null")}，较早已看整份响应不得复活", async () =>
            {
                await using var f = new ItemUserStateFixture();
                f.Menu(ItemCommand.MarkPlayed);
                f.Menu(ItemCommand.MarkUnplayed);
                await f.ReplyAsync(f.Handler.At(1), null, noContent);
                await f.ReplyAsync(f.Handler.At(0), State(played: true, position: 0, percentage: 100));
                Assert.False(f.Card.Watched);
                Assert.Equal(0L, f.Card.Item.UserData!.PlaybackPositionTicks);
                Assert.Equal(0, f.Shell.WatchedReports.Count);
                Assert.Equal(0, f.WatchedCallbacks);
            });
        }

        Case("R-016：同字段未看→已看逆序，旧未看不得清除新进度", async () =>
        {
            await using var f = new ItemUserStateFixture(State(played: true));
            f.Menu(ItemCommand.MarkUnplayed);
            ItemCommands.Run(ItemCommand.MarkPlayed, f.Session, f.Shell, f.Owner, f.Card, watched: _ => { });
            var latest = State(played: true, position: 0, percentage: 100, playCount: 4, unplayed: 0);
            await f.ReplyAsync(f.Handler.At(1), latest);
            await f.ReplyAsync(f.Handler.At(0), State(position: 0, percentage: 0, playCount: 0, unplayed: 4));
            WatchedFields(latest, f.Card.Item.UserData!);
            Assert.Equal(1, f.Shell.WatchedReports.Count);
        });

        Case("R-016：同字段取消收藏→收藏逆序，旧取消不得盖新收藏", async () =>
        {
            await using var f = new ItemUserStateFixture(State(favorite: true));
            f.Menu(ItemCommand.Unfavorite);
            ItemCommands.Run(ItemCommand.Favorite, f.Session, f.Shell, f.Owner, f.Card);
            await f.ReplyAsync(f.Handler.At(1), State(favorite: true));
            await f.ReplyAsync(f.Handler.At(0), State(favorite: false));
            Assert.True(f.Card.IsFavorite);
            Assert.Equal(1, f.Shell.FavoriteRefreshes.Count);
        });

        foreach (var failureFirst in new[] { true, false })
        {
            Case($"R-016：同字段较新未看失败，较早成功{(failureFirst ? "后到" : "先到")}都保留，失败释放在飞状态", async () =>
            {
                await using var f = new ItemUserStateFixture();
                f.Menu(ItemCommand.MarkPlayed);
                f.Menu(ItemCommand.MarkUnplayed);
                if (failureFirst) { await f.FailAsync(f.Handler.At(1)); await f.ReplyAsync(f.Handler.At(0), State(played: true)); }
                else { await f.ReplyAsync(f.Handler.At(0), State(played: true)); await f.FailAsync(f.Handler.At(1)); }
                Assert.True(f.Card.Watched);
                Assert.Equal(1, f.Shell.WatchedReports.Count);
                Assert.Equal(1, f.WatchedCallbacks);
                f.Hover(CardAction.Favorite);
                var fresh = State(played: true, favorite: true, position: 0, percentage: 100, playCount: 12, unplayed: 0);
                await f.ReplyAsync(f.Handler.At(2), fresh);
                WatchedFields(fresh, f.Card.Item.UserData!);
                Assert.True(f.Card.IsFavorite);
            });
        }

        Case("R-016：同字段顺序确认仍应用两次不同方向", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Menu(ItemCommand.MarkPlayed);
            f.Menu(ItemCommand.MarkUnplayed);
            await f.ReplyAsync(f.Handler.At(0), State(played: true));
            Assert.True(f.Card.Watched);
            await f.ReplyAsync(f.Handler.At(1), State(position: 0, percentage: 0));
            Assert.False(f.Card.Watched);
            Assert.Equal(1, f.Shell.WatchedReports.Count);
            Assert.Equal(1, f.WatchedCallbacks);
        });

        Case("R-016：旧同字段响应不漏掉已确认收藏，也不复活旧已看", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Menu(ItemCommand.MarkPlayed);
            f.Hover(CardAction.Favorite);
            f.Menu(ItemCommand.MarkUnplayed);
            await f.ReplyAsync(f.Handler.At(2), State(position: 0, percentage: 0));
            await f.ReplyAsync(f.Handler.At(1), State(played: true, favorite: true));
            await f.ReplyAsync(f.Handler.At(0), State(played: true, favorite: false));
            Assert.False(f.Card.Watched);
            Assert.True(f.Card.IsFavorite);
            Assert.Equal(0L, f.Card.Item.UserData!.PlaybackPositionTicks);
            Assert.Equal(0, f.Shell.WatchedReports.Count);
        });

        Case("R-016：整份响应只读，合并不就地污染原始或响应DTO", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var original = f.Card.Item.UserData!;
            f.Hover(CardAction.Watched);
            f.Hover(CardAction.Favorite);
            var old = State(played: true, favorite: false, position: 0);
            var current = State(played: true, favorite: true, position: 0);
            await f.ReplyAsync(f.Handler.At(1), current);
            await f.ReplyAsync(f.Handler.At(0), old);
            Assert.False(original.Played);
            Assert.False(original.IsFavorite);
            Assert.False(old.IsFavorite);
            Assert.True(current.IsFavorite);
            Assert.True(f.Card.IsFavorite);

            // The fake HTTP deserializes a new DTO; verify response immutability separately through
            // the actual Core merger with the exact response object (not the serialized input copy).
            var order = new ItemUserStateOrder();
            var change = order.BeginFavorite(true);
            var merged = order.Apply(change, original, current)!;
            order.Complete(change);
            Assert.False(ReferenceEquals(original, merged));
            Assert.False(ReferenceEquals(current, merged));
            Assert.False(original.IsFavorite);
            Assert.True(current.IsFavorite);
            Assert.Equal(40_000L, original.PlaybackPositionTicks);
            Assert.Equal(0L, current.PlaybackPositionTicks);
        });

        Case("R-016：同一EmbyItem的两个CardItem共用状态顺序", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var other = f.NewCard(f.Card.Item);
            ItemCommands.ToggleWatched(f.Session, f.Shell, f.Card);
            ItemCommands.ToggleFavorite(f.Session, f.Shell, other);
            await f.ReplyAsync(f.Handler.At(1), State(played: true, favorite: true, position: 0));
            await f.ReplyAsync(f.Handler.At(0), State(played: true, favorite: false, position: 0));
            Assert.True(f.Card.Watched);
            Assert.True(other.IsFavorite);
        });

        Case("R-016：不同条目不能被较新另一卡请求全局压掉", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var other = f.NewCard(f.Scope.Bind(new EmbyItem { Id = "state-other", Name = "other", Type = EmbyItemType.Movie, UserData = State() }));
            ItemCommands.ToggleWatched(f.Session, f.Shell, f.Card);
            ItemCommands.ToggleFavorite(f.Session, f.Shell, other);
            await f.ReplyAsync(f.Handler.At(1), State(favorite: true));
            await f.ReplyAsync(f.Handler.At(0), State(played: true, position: 0));
            Assert.True(f.Card.Watched);
            Assert.False(f.Card.IsFavorite);
            Assert.True(other.IsFavorite);
            Assert.False(other.Watched);
        });

        Case("R-016：条目身份在请求中改变，旧响应不能应用到改绑目标", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            f.Card.Item.Id = "state-rebound";
            await f.ReplyAsync(f.Handler.At(0), State(played: true, position: 0));
            Assert.False(f.Card.Watched);
            Assert.Equal(0, f.Shell.WatchedReports.Count);
            Assert.Equal(0, f.WatchedCallbacks);
        });

        Case("R-016：同字段收藏快照逆序，最新进度全字段保留", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Favorite);
            f.Hover(CardAction.Favorite);
            var latest = State(played: true, favorite: true, position: 0, percentage: 100, playCount: 8, unplayed: 0,
                lastPlayed: new DateTimeOffset(2026, 10, 23, 11, 0, 0, TimeSpan.Zero));
            await f.ReplyAsync(f.Handler.At(1), latest);
            await f.ReplyAsync(f.Handler.At(0), State(favorite: true));
            WatchedFields(latest, f.Card.Item.UserData!);
            Assert.Equal(1, f.Shell.FavoriteRefreshes.Count);
        });

        Test("R-016：UserData新增字段时必须重新核对状态组归属，不能静默漏复制", () =>
        {
            var properties = typeof(EmbyUserData).GetProperties().Where(property => property.CanWrite)
                .Select(property => property.Name).Order(StringComparer.Ordinal);
            Assert.Equal("IsFavorite,LastPlayedDate,PlayCount,PlaybackPositionTicks,Played,PlayedPercentage,UnplayedItemCount",
                string.Join(',', properties));
        });

        foreach (var commands in new ItemCommand[][]
        {
            [ItemCommand.MarkPlayed, ItemCommand.MarkUnplayed, ItemCommand.Favorite, ItemCommand.Unfavorite],
            [ItemCommand.MarkPlayed, ItemCommand.Favorite, ItemCommand.MarkUnplayed, ItemCommand.Unfavorite],
            [ItemCommand.MarkPlayed, ItemCommand.Favorite, ItemCommand.Unfavorite, ItemCommand.MarkUnplayed],
            [ItemCommand.Favorite, ItemCommand.Unfavorite, ItemCommand.MarkPlayed, ItemCommand.MarkUnplayed],
            [ItemCommand.Favorite, ItemCommand.MarkPlayed, ItemCommand.Unfavorite, ItemCommand.MarkUnplayed],
            [ItemCommand.Favorite, ItemCommand.MarkPlayed, ItemCommand.MarkUnplayed, ItemCommand.Unfavorite]
        })
        {
            Case("R-016：四请求全响应排列与混合空体 / " + string.Join('→', commands), async () =>
            {
                // Six independent-field issue interleavings, each with all 24 delivery permutations.
                // Full bodies and alternating 204 bodies both execute real production HTTP and UI apply.
                foreach (var responseOrder in Permutations([0, 1, 2, 3]))
                    foreach (var mixedEmpty in new[] { false, true })
                    {
                        await using var f = new ItemUserStateFixture();
                        var responses = new EmbyUserData[4];
                        var played = false;
                        var favorite = false;
                        for (var i = 0; i < commands.Length; i++)
                        {
                            ItemCommands.Run(commands[i], f.Session, f.Shell, f.Owner, f.Card);
                            if (commands[i] is ItemCommand.MarkPlayed or ItemCommand.MarkUnplayed)
                                played = commands[i] == ItemCommand.MarkPlayed;
                            else favorite = commands[i] == ItemCommand.Favorite;
                            responses[i] = State(played, favorite, position: 0, percentage: played ? 100 : 0);
                        }
                        foreach (var i in responseOrder)
                            await f.ReplyAsync(f.Handler.At(i), mixedEmpty && i % 2 == 0 ? null : responses[i],
                                noContent: mixedEmpty && i % 2 == 0);
                        Assert.False(f.Card.Watched, "latest successful watched direction is unplayed");
                        Assert.False(f.Card.IsFavorite, "latest successful favorite direction is unfavorite");
                        Assert.Equal(0L, f.Card.Item.UserData!.PlaybackPositionTicks);
                        Assert.Equal(0.0, f.Card.Item.UserData.PlayedPercentage);
                        Assert.Equal(0, f.Shell.Notices.Count);
                    }
            });
        }

        Case("R-016：来源撤销后迟到成功不应用、不补报、不刷新", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            f.Hover(CardAction.Favorite);
            var initial = f.Card.Item.UserData;
            f.RevokeSource();
            await f.ReplyAsync(f.Handler.At(1), State(played: true, favorite: true));
            await f.ReplyAsync(f.Handler.At(0), State(played: true));
            Assert.True(ReferenceEquals(initial, f.Card.Item.UserData));
            Assert.False(f.Card.Watched);
            Assert.False(f.Card.IsFavorite);
            Assert.Equal(0, f.Shell.WatchedReports.Count);
            Assert.Equal(0, f.Shell.FavoriteRefreshes.Count);
            Assert.Equal(0, f.Shell.Notices.Count);
            Assert.True(f.Handler.At(0).Token.IsCancellationRequested);
            f.Hover(CardAction.Watched);
            Assert.Equal(2, f.Handler.Count, "revoked source cannot fall back to a default identity");
        });

        Case("R-016：Ask已完成但UI应用排队时撤销来源，仍拒绝迟到回写", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var context = (ItemUserStateContext)SynchronizationContext.Current!;
            f.Hover(CardAction.Watched);
            context.PauseCommands = true;
            try
            {
                f.Handler.At(0).Complete(State(played: true, position: 0), noContent: false);
                await ItemUserStateContext.UntilAsync(() => context.PostedCommands == 1, "real request must finish before source revocation");
                f.RevokeSource();
            }
            finally { context.ResumeCommands(); }
            await ItemUserStateContext.UntilAsync(() => context.CompletedCommands == 1, "queued command did not complete");
            Assert.False(f.Card.Watched);
            Assert.Equal(0, f.Shell.WatchedReports.Count);
            Assert.Equal(0, f.WatchedCallbacks);
            Assert.Equal(0, f.Shell.Notices.Count);
        });

        Case("R-016：正常换默认不撤销原卡来源，逆序响应与后续动作仍在原账号", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            f.Hover(CardAction.Favorite);
            f.SwitchDefault();
            await f.ReplyAsync(f.Handler.At(1), State(played: true, favorite: true, position: 0));
            await f.ReplyAsync(f.Handler.At(0), State(played: true, favorite: false, position: 0));
            Assert.True(f.Card.Watched);
            Assert.True(f.Card.IsFavorite);
            f.Hover(CardAction.Favorite);
            Assert.Equal(HttpMethod.Delete, f.Handler.At(2).Method);
            await f.ReplyAsync(f.Handler.At(2), null, noContent: true);
            Assert.False(f.Card.IsFavorite);
            Assert.True(f.Card.Watched);
        });
    }

    private static IEnumerable<int[]> Permutations(int[] values)
    {
        if (values.Length == 0) { yield return []; yield break; }
        foreach (var value in values)
            foreach (var rest in Permutations(values.Where(candidate => candidate != value).ToArray()))
                yield return [value, .. rest];
    }

    private static void Case(string name, Func<Task> run) => Test(name, () => ItemUserStateContext.Run(run));
    private static void WatchedFields(EmbyUserData expected, EmbyUserData actual)
    {
        Assert.Equal(expected.Played, actual.Played, "Played");
        Assert.Equal(expected.PlaybackPositionTicks, actual.PlaybackPositionTicks, "PlaybackPositionTicks");
        Assert.Equal(expected.PlayedPercentage, actual.PlayedPercentage, "PlayedPercentage");
        Assert.Equal(expected.PlayCount, actual.PlayCount, "PlayCount");
        Assert.Equal(expected.UnplayedItemCount, actual.UnplayedItemCount, "UnplayedItemCount");
        Assert.Equal(expected.LastPlayedDate, actual.LastPlayedDate, "LastPlayedDate");
    }
}
#endif
