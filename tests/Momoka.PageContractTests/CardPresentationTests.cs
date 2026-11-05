using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CardPresentationTests
{
    internal static void Register()
    {
        Test("飞牛继续观看：真实卡片按剧名和集名排版、普通横卡保留居中", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false, MediaServerKind.FnNas);
            var item = fixture.Scope.Bind(new EmbyItem { Id = "episode", Type = EmbyItemType.Episode,
                Name = "延康双雄", SeriesName = "牧神记", ParentIndexNumber = 1, IndexNumber = 96 });
            var constructor = typeof(CardItem).GetConstructors().Single();
            var parameters = constructor.GetParameters();
            Assert.True(parameters.Any(parameter => parameter.Name == "resume"), "卡片尚未接收继续观看展示上下文");
            var values = parameters.Select(parameter => parameter.Name switch
            {
                "item" => (object)item, "images" => fixture.Images, "width" => 300,
                "wide" or "centeredText" or "resume" => true, _ => parameter.DefaultValue
            }).ToArray();
            var card = (CardItem)constructor.Invoke(values);
            Assert.Equal("牧神记", card.Title);
            Assert.Equal("S01E96 - 延康双雄", card.Subtitle);
            Assert.Equal(TextAlignment.Left, card.TitleAlignment);
            Assert.Equal("延康双雄", item.Name);
            Assert.Equal(TextAlignment.Center, new CardItem(item, fixture.Images, 300, wide: true, centeredText: true).TitleAlignment);
        }));

        foreach (var kind in new[] { MediaServerKind.Jellyfin, MediaServerKind.Emby, MediaServerKind.FnNas })
            Test("卡片显示名：仅 Jellyfin 清理已确认尾缀 " + kind, () => CardImageContext.Run(async () =>
            {
                await using var fixture = new CardImageFixture(artwork: false, kind);
                var item = fixture.Scope.Bind(new EmbyItem { Id = "title", Name = "聚焦 (2015) {tmdbid=314365}",
                    Type = EmbyItemType.Movie, ProductionYear = 2015 });
                var identity = item.IdentityKey;
                var card = new CardItem(item, fixture.Images, 170);
                Assert.Equal(kind == MediaServerKind.Jellyfin ? "聚焦" : item.Name, card.Title);
                Assert.True(ReferenceEquals(item, card.Item));
                Assert.Equal("聚焦 (2015) {tmdbid=314365}", item.Name);
                Assert.Equal(identity, item.IdentityKey);
                Assert.Equal("2015", card.Subtitle);
            }));

        foreach (var entry in new (string Name, int? Year, string Expected)[]
        {
            ("红胡子 (1965) {tmdbid=3780}", 1965, "红胡子"),
            ("摩加迪沙 (2021) {tmdbid=607844}", 2021, "摩加迪沙"),
            ("电影 (2020) {tmdbid=123}", 2021, "电影 (2020)"),
            ("电影 (2021)", 2021, "电影"),
            ("1917", 2019, "1917"), ("1984", 1984, "1984"),
            ("2001太空漫游", 1968, "2001太空漫游"),
            ("(500) Days of Summer", 2009, "(500) Days of Summer"),
            ("合法 (123)", 2021, "合法 (123)"),
            ("电影 {tmdbid=abc}", 2021, "电影 {tmdbid=abc}"),
            ("电影 {imdbid=123}", 2021, "电影 {imdbid=123}"),
            ("电影 {tmdbid=１２３}", 2021, "电影 {tmdbid=１２３}"),
            ("电影 {tmdbid=123} 后记", 2021, "电影 {tmdbid=123} 后记"),
            ("{tmdbid=123}", null, "{tmdbid=123}"),
            ("(2021)", 2021, "(2021)"), ("", null, ""), ("   ", null, "   ")
        })
            Test("卡片显示名：保守尾部清理 " + entry.Name, () => CardImageContext.Run(async () =>
            {
                await using var fixture = new CardImageFixture(false, MediaServerKind.Jellyfin);
                var item = fixture.Scope.Bind(new EmbyItem { Id = "title", Name = entry.Name,
                    Type = EmbyItemType.Movie, ProductionYear = entry.Year });
                Assert.Equal(entry.Expected, new CardItem(item, fixture.Images, 170).Title);
                Assert.Equal("", new CardItem(item, fixture.Images, 170, title: "").Title);
            }));

        Test("卡片标题：媒体保留两行、人物与目录保留单行", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false);
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Episode })
            {
                var card = new CardItem(fixture.Scope.Bind(new EmbyItem { Id = type, Name = "短标题", Type = type }), fixture.Images, 170);
                Assert.Equal(2, Property<int>(card, "TitleMaxLines"));
                Assert.Equal(40d, Property<double>(card, "TitleBlockHeight"));
            }
            foreach (var type in new[] { EmbyItemType.Person, EmbyItemType.CollectionFolder, EmbyItemType.Folder, EmbyItemType.Genre })
            {
                var card = new CardItem(fixture.Scope.Bind(new EmbyItem { Id = type, Name = "目录", Type = type }), fixture.Images, 170);
                Assert.Equal(1, Property<int>(card, "TitleMaxLines"));
            }
        }));

        Test("卡片货架：新增标题行不挤掉来源，人物保持原高度", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false);
            var media = new CardShelf("继续观看", fixture.Images, 256, true, sourceLine: true);
            Assert.Equal(241d, media.RowHeight);
            Assert.Equal(245d, new CardShelf("演员", fixture.Images, 124, false, indicators: false, mediaTitles: false).RowHeight);
        }));

        Test("卡片占位：只在没有已解码图片时显示，释放与缓存恢复同步", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            var card = fixture.Card;
            Assert.Equal(Visibility.Visible, Property<Visibility>(card, "PlaceholderVisibility"));
            var loading = fixture.Begin();
            await fixture.DeliverAsync(await fixture.Handler.AtAsync(0), "decoded");
            await loading;
            Assert.Equal(Visibility.Collapsed, Property<Visibility>(card, "PlaceholderVisibility"));
            card.ReleasePoster(fixture.Container);
            Assert.Equal(Visibility.Visible, Property<Visibility>(card, "PlaceholderVisibility"));
            await fixture.Ensure();
            Assert.Equal(Visibility.Collapsed, Property<Visibility>(card, "PlaceholderVisibility"));
            Assert.True(fixture.Changes.Contains("PlaceholderVisibility"));
        }));

        Test("卡片占位：未解码 Source 不算成功、拼图首张成功和清空均通知", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false);
            var card = fixture.Card;
            SetPoster(card, new BitmapImage());
            Assert.Equal(Visibility.Visible, Property<Visibility>(card, "PlaceholderVisibility"));
            SetPoster(card, new BitmapImage { PixelWidth = 170, PixelHeight = 255 });
            Assert.Equal(Visibility.Collapsed, Property<Visibility>(card, "PlaceholderVisibility"));
            SetPoster(card, null);
            typeof(CardItem).GetMethod("SetCollageTile", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(card, [0, new BitmapImage { PixelWidth = 100, PixelHeight = 100 }]);
            Assert.Equal(Visibility.Collapsed, Property<Visibility>(card, "PlaceholderVisibility"));
            typeof(CardItem).GetMethod("ClearCollageTiles", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, null);
            Assert.Equal(Visibility.Visible, Property<Visibility>(card, "PlaceholderVisibility"));
        }));

        Test("卡片占位：404保持片名且不重复请求，隐藏卡下标题不隐藏占位名称", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture();
            var loading = fixture.Begin();
            (await fixture.Handler.AtAsync(0)).Missing();
            await loading;
            Assert.Equal(Visibility.Visible, Property<Visibility>(fixture.Card, "PlaceholderVisibility"));
            await fixture.Ensure();
            Assert.Equal(1, fixture.Handler.Count);
            var hidden = new CardItem(fixture.Item, fixture.Images, 170, title: "");
            Assert.Equal(Visibility.Collapsed, hidden.TitleVisibility);
            Assert.Equal("image-fixture", Property<string>(hidden, "PlaceholderTitle"));
        }));

        Test("卡片显示名：Jellyfin人物与目录的合法括号保持原样", () => CardImageContext.Run(async () =>
        {
            await using var fixture = new CardImageFixture(false, MediaServerKind.Jellyfin);
            foreach (var type in new[] { EmbyItemType.Person, EmbyItemType.Folder, EmbyItemType.Genre })
            {
                var item = fixture.Scope.Bind(new EmbyItem { Id = type, Type = type,
                    Name = "原名 (2021) {tmdbid=123}", ProductionYear = 2021 });
                Assert.Equal(item.Name, new CardItem(item, fixture.Images, 170).Title);
            }
        }));
    }

    private static T Property<T>(CardItem card, string name)
    {
        var property = typeof(CardItem).GetProperty(name);
        Assert.NotNull(property, "卡片缺少展示属性 " + name);
        return (T)property!.GetValue(card)!;
    }

    private static void SetPoster(CardItem card, BitmapImage? image) =>
        typeof(CardItem).GetProperty(nameof(CardItem.Poster))!.SetValue(card, image);
}
