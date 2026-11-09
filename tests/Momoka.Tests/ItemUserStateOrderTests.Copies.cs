#if ITEM_USER_STATE_ORDER_FIXTURE
using System.Net;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.ItemUserStateFixture;

namespace Momoka.Tests;

internal static partial class ItemUserStateOrderTests
{
    private static void RegisterCardCopies()
    {
        Case("卡片等待首帧时来源撤销，不放行图片请求", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var presentation = new TaskCompletionSource();
            var card = f.NewCard(f.Scope.Bind(new EmbyItem
            {
                Id = "pending-artwork", Type = EmbyItemType.Movie,
                ImageTags = new Dictionary<string, string> { ["Primary"] = "anonymous-artwork" }
            }));
            var loading = card.EnsurePosterAsync(presentation.Task);
            Assert.False(loading.IsCompleted);
            f.RevokeSource();
            await loading.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(presentation.Task.IsCompleted);
            Assert.True(card.Poster is null);
        });

        Case("状态副本：已确认后迟到的旧DTO不能用无正文收藏回滚已看", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var waiting = new TaskCompletionSource<EmbyItem>();
            var read = f.Scope.ExecuteAsync((_, _) => waiting.Task, CancellationToken.None);
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(0), State(played: true));
            waiting.SetResult(new EmbyItem
            {
                Id = f.Card.Item.Id, Type = EmbyItemType.Movie, UserData = State(played: false)
            });
            var delayed = f.NewCard(await read);
            Assert.True(delayed.Watched);
            ItemCommands.ToggleFavorite(f.Session, f.Shell, delayed);
            await f.ReplyAsync(f.Handler.At(1), null, noContent: true);
            Assert.True(f.Card.Watched && delayed.Watched);
            Assert.True(f.Card.IsFavorite && delayed.IsFavorite);
        });

        Case("状态副本：确认之后的新服务器读取仍能更新观看状态", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(0), State(played: true));
            var item = await f.Scope.ExecuteAsync((_, _) => Task.FromResult(new EmbyItem
            {
                Id = f.Card.Item.Id, Type = EmbyItemType.Movie, UserData = State(played: false, position: 17_000)
            }), CancellationToken.None);
            var refreshed = f.NewCard(item);
            Assert.False(refreshed.Watched);
            Assert.Equal(17_000L, refreshed.Item.UserData!.PlaybackPositionTicks);
            ItemCommands.ToggleFavorite(f.Session, f.Shell, refreshed);
            await f.ReplyAsync(f.Handler.At(1), null, noContent: true);
            Assert.False(f.Card.Watched);
            Assert.True(f.Card.IsFavorite);
        });

        Case("状态副本：各DTO与内部确认快照不共享可变UserData", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var waiting = new TaskCompletionSource<EmbyItem>();
            var read = f.Scope.ExecuteAsync((_, _) => waiting.Task, CancellationToken.None);
            var other = f.NewCard(f.Scope.Bind(new EmbyItem { Id = f.Card.Item.Id, Type = EmbyItemType.Movie, UserData = State() }));
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(0), State(played: true));
            other.Item.UserData!.Played = false;
            Assert.True(f.Card.Watched);
            waiting.SetResult(new EmbyItem { Id = f.Card.Item.Id, Type = EmbyItemType.Movie, UserData = State() });
            Assert.True(f.NewCard(await read).Watched);
        });

        Case("状态副本：同身份不同DTO同步收藏并按最新值反向操作", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var duplicate = f.NewCard(f.Scope.Bind(new EmbyItem
            {
                Id = f.Card.Item.Id, Type = EmbyItemType.Movie, UserData = State()
            }));
            var changed = new List<string>();
            duplicate.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? "");
            f.Hover(CardAction.Favorite);
            await f.ReplyAsync(f.Handler.At(0), State(favorite: true));
            Assert.True(duplicate.IsFavorite);
            Assert.True(changed.Contains(nameof(CardItem.IsFavorite)));
            ItemCommands.ToggleFavorite(f.Session, f.Shell, duplicate);
            Assert.Equal(HttpMethod.Delete, f.Handler.At(1).Method);
            await f.ReplyAsync(f.Handler.At(1), State(favorite: false));
            Assert.False(f.Card.IsFavorite);
            Assert.False(duplicate.IsFavorite);
        });

        Case("状态副本：不同DTO的重叠请求共享字段顺序", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var duplicate = f.NewCard(f.Scope.Bind(new EmbyItem
            {
                Id = f.Card.Item.Id, Type = EmbyItemType.Movie, UserData = State()
            }));
            ItemCommands.ToggleWatched(f.Session, f.Shell, f.Card);
            ItemCommands.ToggleFavorite(f.Session, f.Shell, duplicate);
            await f.ReplyAsync(f.Handler.At(1), State(favorite: true));
            await f.ReplyAsync(f.Handler.At(0), State(played: true));
            Assert.True(f.Card.IsFavorite && duplicate.IsFavorite);
            Assert.True(f.Card.Watched && duplicate.Watched);
            Assert.Equal(f.Card.WatchedRevision, duplicate.WatchedRevision);
            Assert.Equal(1L, duplicate.WatchedRevision);
        });

        Case("状态副本：旧已看后续不跨越未看再已看的确认版本", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(0), State(played: true));
            var first = f.Card.WatchedRevision;
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(1), State(played: false));
            f.Hover(CardAction.Watched);
            await f.ReplyAsync(f.Handler.At(2), State(played: true));
            Assert.True(f.Card.Watched);
            Assert.True(f.Card.WatchedRevision > first);
        });

        Case("状态副本：另一个在线身份的同ID保持独立", async () =>
        {
            await using var a = new ItemUserStateFixture();
            await using var b = new ItemUserStateFixture();
            a.Hover(CardAction.Favorite);
            await a.ReplyAsync(a.Handler.At(0), State(favorite: true));
            Assert.True(a.Card.IsFavorite);
            Assert.False(b.Card.IsFavorite);
            Assert.Equal(0, b.Handler.Count);
        });
    }
}
#endif
