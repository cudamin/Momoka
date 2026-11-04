using System.Collections;
using System.Collections.Specialized;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DeferredCardsTests
{
    internal static void Register()
    {
        Test("P3 长剧：发布2000集只暴露全局数量，不提前创建卡片", () => Run(fixture =>
        {
            var shelf = Shelf(fixture);
            var created = 0;
            var changes = new List<NotifyCollectionChangedAction>();
            shelf.Cards.CollectionChanged += (_, args) => changes.Add(args.Action);
            FillDeferred(shelf, Episodes(fixture, 2000), _ => { created++; return "集信息"; });
            Assert.Equal(2000, shelf.Cards.Count);
            Assert.Equal("2000 项", shelf.Note);
            Assert.Equal(0, created, "发布DTO时不得建卡");
            Assert.True(changes.SequenceEqual([NotifyCollectionChangedAction.Reset]), "只发布一次Reset");
            shelf.Clear();
        }));
        Test("P3 长剧：非泛型索引访问1500集只创建该卡并复用实例", () => Run(fixture =>
        {
            var shelf = Shelf(fixture);
            var created = 0;
            FillDeferred(shelf, Episodes(fixture, 2000), _ => { created++; return null; });
            var source = (IList)shelf.Cards;
            var deep = (CardItem)source[1500]!;
            Assert.Equal("episode-1500", deep.Item.Id);
            Assert.True(ReferenceEquals(deep, source[1500]));
            Assert.Equal(1, created, "访问深处不能创建前缀");
            Assert.Equal(1500, source.IndexOf(deep));
            Assert.False(source.Contains(fixture.Card));
            Assert.Equal(-1, source.IndexOf(fixture.Card));
            Assert.Equal(1, created, "Contains/IndexOf不能实现其它槽位");
            shelf.Clear();
        }));
        Test("P3 长剧：Clear和重新填充不创建未访问卡且集合引用稳定", () => Run(fixture =>
        {
            var shelf = Shelf(fixture);
            var cards = shelf.Cards;
            var created = 0;
            FillDeferred(shelf, Episodes(fixture, 2000), _ => { created++; return null; });
            _ = cards[1700];
            shelf.Clear();
            Assert.Equal(1, created, "清理只能释放已经创建的卡");
            Assert.Equal(0, cards.Count);
            FillDeferred(shelf, [fixture.Scope.Bind(Episode(20))], _ => { created++; return null; });
            Assert.True(ReferenceEquals(cards, shelf.Cards));
            Assert.Equal(1, created);
            Assert.Equal("episode-20", cards[0].Item.Id);
            Assert.Equal(2, created);
            shelf.Clear();
        }));
        Test("P3 普通货架：Fill仍立即造卡，增删插入移动通知正常", () => Run(fixture =>
        {
            var shelf = Shelf(fixture);
            var created = 0;
            shelf.Fill([fixture.Scope.Bind(Episode(1)), fixture.Scope.Bind(Episode(2)), fixture.Scope.Bind(Episode(3))],
                _ => { created++; return null; });
            Assert.Equal(3, created);
            var original = shelf.Cards[0];
            var changes = new List<NotifyCollectionChangedAction>();
            shelf.Cards.CollectionChanged += (_, args) => changes.Add(args.Action);
            shelf.Cards.Move(0, 2);
            Assert.True(ReferenceEquals(original, shelf.Cards[2]));
            shelf.Cards.Insert(1, shelf.NewCard(fixture.Scope.Bind(Episode(4))));
            Assert.True(shelf.Cards.Remove(original));
            Assert.True(changes.SequenceEqual([NotifyCollectionChangedAction.Move,
                NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove]));
            Assert.True(shelf.Cards.Select(card => card.Item.Id).SequenceEqual(["episode-2", "episode-4", "episode-3"]));
            shelf.Clear();
        }));
        Test("P3 长剧：删除尚未创建的整季也移除DTO，不创建前缀或丢掉其它实例", () => Run(fixture =>
        {
            var shelf = Shelf(fixture);
            var created = 0;
            FillDeferred(shelf, Episodes(fixture, 2000), _ => { created++; return null; });
            var kept = shelf.Cards[1500];
            RemoveWhere(shelf, item => item.SeasonId == "season-3");
            Assert.Equal(1900, shelf.Cards.Count);
            Assert.Equal(1, created, "删未创建季不能创建其卡片");
            Assert.True(ReferenceEquals(kept, shelf.Cards[1400]));
            Assert.Equal("episode-400", shelf.Cards[300].Item.Id);
            Assert.Equal(2, created);
            shelf.Clear();
        }));
        Test("P3 长剧：标准枚举仍返回完整列表，Materialized清理视图不改变枚举语义", () => Run(fixture =>
        {
            var shelf = Shelf(fixture);
            var created = 0;
            FillDeferred(shelf, Episodes(fixture, 4), _ => { created++; return null; });
            Assert.True(shelf.Cards.Select(card => card.Item.Id).SequenceEqual(
                ["episode-0", "episode-1", "episode-2", "episode-3"]));
            Assert.Equal(4, created);
            shelf.Clear();
        }));
        Test("P3 集合：未实现槽的移除、移动和替换不创建其它卡", () => Run(fixture =>
        {
            var shelf = Shelf(fixture);
            var created = 0;
            FillDeferred(shelf, Episodes(fixture, 6), _ => { created++; return null; });
            var source = (IList)shelf.Cards;
            source.RemoveAt(2);
            shelf.Cards.Move(3, 0);
            source[1] = fixture.Card;
            Assert.Equal(0, created);
            Assert.Equal("episode-4", shelf.Cards[0].Item.Id);
            Assert.True(ReferenceEquals(fixture.Card, shelf.Cards[1]));
            var copy = new object[5];
            ((ICollection)source).CopyTo(copy, 0);
            Assert.Equal(4, created);
            Assert.True(copy.Cast<CardItem>().Select(card => card.Item.Id).SequenceEqual(
                ["episode-4", "image-item", "episode-1", "episode-3", "episode-5"]));
            shelf.Clear();
        }));
        Test("P3 集合：重复进入30次只保留当次访问卡，Clear释放旧工厂和DTO", () => Run(fixture =>
        {
            var shelf = Shelf(fixture);
            var source = shelf.Cards;
            var created = 0;
            for (var visit = 0; visit < 30; visit++)
            {
                FillDeferred(shelf, Episodes(fixture, 2000), _ => { created++; return null; });
                _ = source[1500];
                Assert.Equal(1, source.MaterializedCount);
                shelf.Clear();
                Assert.Equal(0, source.MaterializedCount);
                Assert.Equal(0, source.Count);
            }
            Assert.Equal(30, created);
            Assert.True(ReferenceEquals(source, shelf.Cards));
        }));
    }

    private static CardShelf Shelf(CardImageFixture fixture) => new("单集", fixture.Images, 260, wide: true);
    private static EmbyItem[] Episodes(CardImageFixture fixture, int count) =>
        [.. Enumerable.Range(0, count).Select(index => fixture.Scope.Bind(Episode(index)))];
    private static EmbyItem Episode(int index) => new()
    {
        Id = "episode-" + index,
        Name = "第" + index + "集",
        Type = EmbyItemType.Episode,
        SeriesId = "series",
        SeasonId = "season-" + index / 100,
        ParentIndexNumber = index / 100,
        IndexNumber = index % 100 + 1
    };

    private static void FillDeferred(CardShelf shelf, IReadOnlyList<EmbyItem> items,
        Func<EmbyItem, string?> subtitle) => shelf.FillDeferred(items, subtitle);

    private static void RemoveWhere(CardShelf shelf, Predicate<EmbyItem> match) => shelf.Cards.RemoveWhere(match);

    private static void Run(Action<CardImageFixture> test) => CardImageContext.Run(async () =>
    {
        await using var fixture = new CardImageFixture(artwork: false);
        test(fixture);
    });
}
