using System.Collections.Concurrent;
using System.Net;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>同号媒体来自两个独立账号；所有 HTTP、图片和账号均由离线夹具提供。</summary>
    private async Task AggregateBrowsingAsync()
    {
        using var fixture = CreateFixture();
        var image = await FixtureImageAsync(400, 600);
        var queries = new ConcurrentQueue<(string Host, string? Parent, string? Term, int Offset)>();
        var images = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        var late = new Dictionary<string, TaskCompletionSource<HttpResponseMessage>>(StringComparer.Ordinal);
        var entered = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        fixture.Transport.Reply = (request, _) =>
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            var host = uri.Host;
            if (path.Contains("/Images/", StringComparison.Ordinal))
            {
                images.TryAdd(host, true);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            }
            if (path.EndsWith("/Views", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = new[] { AggregateLibrary() }, TotalRecordCount = 1 }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
                return Task.FromResult(Json(Enumerable.Range(0, 4).Select(index => AggregateMovie(index, host)).ToArray()));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(AggregatePage(host, "", 0, 2, 2));
            if (path.EndsWith("/Shows/NextUp", StringComparison.Ordinal)) return Task.FromResult(Items(0, 0));
            if (path.EndsWith("/Items/shared-0", StringComparison.Ordinal))
                return Task.FromResult(Json(AggregateMovie(0, host)));
            if (path.EndsWith("/Items", StringComparison.Ordinal))
            {
                var term = Query(request, "SearchTerm");
                var parent = Query(request, "ParentId");
                var offset = int.TryParse(Query(request, "StartIndex"), out var parsed) ? parsed : 0;
                queries.Enqueue((host, parent, term, offset));
                if (term == "迟到查询")
                {
                    entered.TryAdd(host, true);
                    return late[host].Task;
                }
                if (term == "来源掉线" && host == "b.invalid")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                return Task.FromResult(AggregatePage(host, term ?? "", offset));
            }
            return Task.FromResult(FakeTransport.Standard(request));
        };

        await fixture.SelectAsync("a.invalid", "账号甲");
        fixture.Session.Server!.Name = "家庭库";
        var first = fixture.Session.Capture();
        await fixture.SelectAsync("b.invalid", "账号乙");
        fixture.Session.Server!.Name = "外网库";
        var second = fixture.Session.Capture();
        var libraries = await SourceLibraries.LoadAsync(fixture.Session, CancellationToken.None);
        Require(libraries.Items.Count == 2 && libraries.Items.Select(item => item.IdentityKey).Distinct().Count() == 2,
            "相同媒体库 ID 被聚合目录合并");
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.CarouselCount = 4;
        fixture.Settings.Ui.CarouselSeconds = 60;
        fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(
            [new() { Key = HomeLayout.Libraries }, new() { Key = HomeLayout.Resume }], libraries.Items));

        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            ResizeProbeClient(1280, 1000);
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, libraries.Items, null));
            var home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.ViewModel.LibraryShelf?.Cards.Count == 2);
            var shelves = home.ViewModel.Shelves.Where(shelf => shelf.LibraryId is not null).ToArray();
            Require(shelves.Length == 2 && shelves.Select(shelf => shelf.Source?.Key).Distinct().Count() == 2,
                "主页没有分别显示两个来源的最近添加货架");
            Require(shelves.All(shelf => shelf.Cards.Count == 4 && shelf.Cards.All(card => card.Item.Source?.Key == shelf.Source?.Key)),
                "主页货架中的媒体用了另一个来源");
            var resume = home.ViewModel.Shelves.Single(shelf => shelf.HomeKey == HomeLayout.Resume);
            Require(resume.Cards.Count == 4 && resume.Cards.Select(card => card.Item.Id).Distinct().Count() == 2
                && resume.Cards.Select(card => card.Item.IdentityKey).Distinct().Count() == 4,
                "主页继续观看没有保留两个账号的相同媒体 ID");
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 1000);
                await LayoutAsync(home);
                await Task.WhenAll(home.ViewModel.LibraryShelf!.Cards.Select(card => card.EnsurePosterAsync()));
                AssertAggregateLabels(home, first, second);
                await SaveProbeScreenAsync($"aggregate-home-{width}-screen.png");
            }
            Require(images.ContainsKey("a.invalid") && images.ContainsKey("b.invalid"), "主页图片没有分别读取两个来源");
            var normalReply = fixture.Transport.Reply;
            fixture.Transport.Reply = (request, token) => Query(request, "ParentId") == "shared-library"
                && request.RequestUri!.Host == "a.invalid" && request.RequestUri.AbsolutePath.EndsWith("/Items/Latest", StringComparison.Ordinal)
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
                    : normalReply!(request, token);
            await home.ViewModel.ReloadAsync();
            Require(home.ViewModel.NoticeOpen && home.ViewModel.NoticeTitle == "最近添加 · 电影",
                "来源读取失败提示不应追加服务器名称或用户名");
            fixture.Transport.Reply = normalReply;
            home.Release();

            // 默认账号仍是乙，从甲的库下钻和打开详情必须始终回到甲。
            foreach (var source in new[] { first, second })
            {
                var library = libraries.Items.Single(item => item.Source?.Key == source.Key);
                var request = LibraryRequest.For(fixture.Services, library) with { CollectionType = library.CollectionType };
                Require(request.Source?.Key == source.Key, "媒体库导航载荷丢失来源");
                frame.Navigate(typeof(LibraryPage), request);
                var page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.IsReady && page.LoadedCount == 20);
                Require(page.ViewModel.Cards.All(card => card.Item.Source?.Key == source.Key), "具体媒体库混入另一个来源");
                var selected = page.ViewModel.Cards[0].Item;
                Require(request.Child(selected).Source?.Key == source.Key, "下层导航丢失账号来源");
                page.Release();
                frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, selected));
                var detail = (DetailPage)frame.Content;
                await UntilAsync(() => detail.IsReady && detail.ViewModel.CurrentItem is not null);
                Require(detail.ViewModel.CurrentItem?.Source?.Key == source.Key
                    && detail.ViewModel.PlayTarget?.Source?.Key == source.Key,
                    "详情或播放目标被相同 ID 的默认来源替换");
                detail.Release();
            }

            queries.Clear();
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            var search = (LibraryPage)frame.Content;
            await search.SearchTypingAsync("共享影片");
            await UntilAsync(() => search.IsReady && search.LoadedCount == 40);
            Require(search.ViewModel.Cards.Select(card => card.Item.Id).Distinct().Count() == 20
                && search.ViewModel.Cards.Select(card => card.Item.IdentityKey).Distinct().Count() == 40,
                "自动搜索没有保留两个来源相同 ID 的结果");
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(search);
                AssertAggregateLabels(search, first, second);
                await SaveProbeScreenAsync($"aggregate-search-{width}-screen.png");
            }
            await search.ViewModel.LoadUntilAsync(45);
            Require(search.LoadedCount == 80 && !queries.Any(query => query.Term == "共享影片" && query.Offset == 40)
                && new[] { "a.invalid", "b.invalid" }.All(host => queries.Any(query => query.Host == host
                    && query.Term == "共享影片" && query.Offset == 20)),
                "聚合下一页错用了总结果数，没有使用每个来源自己的偏移");
            await search.ViewModel.SearchAsync("来源掉线");
            Require(search.LoadedCount == 20 && search.ViewModel.NoticeOpen
                && search.ViewModel.Cards.All(card => card.Item.Source?.Key == first.Key),
                "一个来源失败阻止了其他来源的搜索结果");

            late["a.invalid"] = Pending<HttpResponseMessage>();
            late["b.invalid"] = Pending<HttpResponseMessage>();
            var stale = search.ViewModel.SearchAsync("迟到查询");
            await UntilAsync(() => entered.Count == 2);
            await search.ViewModel.SearchAsync("新查询");
            foreach (var (host, reply) in late) reply.TrySetResult(AggregatePage(host, "迟到查询", 0));
            await stale;
            Require(search.ViewModel.Cards.Count == 40 && search.ViewModel.Cards.All(card => card.Title.StartsWith("新查询", StringComparison.Ordinal)),
                "被取消的聚合搜索迟到后覆盖新结果");
            Require(fixture.Actions.Plays == 0, "聚合浏览探针意外请求播放");
        }
        finally
        {
            foreach (var (host, reply) in late) reply.TrySetResult(AggregatePage(host, "迟到查询", 0));
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }

    private static void AssertAggregateLabels(FrameworkElement page, params EmbySessionScope[] sources)
    {
        var showServer = sources.Length > 1;
        var home = page as HomePage;
        var resume = home?.ViewModel.Shelves.FirstOrDefault(shelf => shelf.HomeKey == HomeLayout.Resume);
        var cards = Descendants(page).OfType<PosterCard>().Where(card => card.Card is not null && VisibleArea(card, page) > 0).ToArray();
        foreach (var source in sources)
        {
            var matches = cards.Where(card => card.Card!.Scope?.Key == source.Key).ToArray();
            Require(matches.Length > 0, "屏上没有显示所需来源的卡片");
            foreach (var card in matches)
            {
                var item = card.Card!;
                var expected = item.Item.Type == EmbyItemType.CollectionFolder ? "" : item.Item.CardSubtitle;
                var subtitle = Get<TextBlock>(card, "SubLine");
                Require(subtitle.Text == expected, "浏览卡片不应追加服务器名称或用户名");
                Require(item.RowInfo == (ItemDetail.EpisodeRowInfo(item.Item) is { Length: > 0 } info ? info : null),
                    "列表行不应追加来源文字");
                if (expected.Length > 0)
                    Require(subtitle.Visibility == Visibility.Visible && subtitle.ActualHeight > 0, "卡片原有文字没有呈现出来");

                // 继续观看那一排：多账号时季集下面多一行服务器名，单账号时整行收起。
                var expectedSource = showServer && resume is not null && resume.Cards.Contains(item)
                    ? item.Item.Source?.ServerName ?? "" : "";
                var sourceLine = Get<TextBlock>(card, "SourceLine");
                Require(sourceLine.Text == expectedSource,
                    expectedSource.Length > 0 ? "继续观看卡没有写服务器名" : "非继续观看卡不应追加服务器名");
                Require(sourceLine.Visibility == (expectedSource.Length > 0 ? Visibility.Visible : Visibility.Collapsed),
                    "继续观看卡的服务器名行显隐不对");
            }
        }

        if (home is null) return;

        // 媒体库那一排：多账号时每张卡的标题是「服务器名 · 库名」。
        if (home.ViewModel.LibraryShelf is { } libraryShelf)
        {
            foreach (var card in libraryShelf.Cards)
            {
                var expected = SourceCaption.Prefix(card.Item.Name, card.Item.Source, showServer);
                Require(card.Title == expected, expected != card.Item.Name
                    ? "多账号主页媒体库名称前没有服务器名" : "单账号主页媒体库名称不应带服务器名");
            }
            foreach (var card in cards.Where(card => libraryShelf.Cards.Contains(card.Card!)))
                Require(Get<TextBlock>(card, "TitleLine").Text == card.Card!.Title,
                    "媒体库卡片的标题绑定没有跟随服务器名前缀");
        }

        foreach (var shelf in home.ViewModel.Shelves.Where(shelf => shelf.Source is not null))
        {
            var library = home.ViewModel.LibraryShelf!.Cards.Single(card => HomeLayout.LibraryKey(card.Item) == shelf.HomeKey);
            var expected = SourceCaption.Prefix(HomeLayout.LibraryTitle(library.Item.Name), shelf.Source, showServer);
            Require(shelf.Title == expected, showServer
                ? "多账号主页最近添加标题前没有服务器名"
                : "单账号主页最近添加标题不应带服务器名");
            foreach (var head in Descendants(home).OfType<ShelfHead>().Where(head => ReferenceEquals(head.Tag, shelf)))
                Require(head.Title == expected, "主页货架标题绑定没有跟随多账号服务器名");
        }
        foreach (var slide in home.ViewModel.Slides)
            Require(slide.Caption == HomeCarousel.Caption(slide.Item), "轮播不应追加来源文字");
        var banner = Get<HomeBanner>(home, "Banner");
        if (home.ViewModel.Slides.Count > 0)
        {
            var caption = Get<TextBlock>(banner, "CaptionText");
            Require(home.ViewModel.Slides.Any(slide => slide.Caption == caption.Text), "轮播字幕没有使用当前文案");
        }
    }

    private static EmbyItem AggregateLibrary() => new()
    {
        Id = "shared-library",
        Name = "电影",
        Type = EmbyItemType.CollectionFolder,
        CollectionType = "movies",
        ImageTags = new() { ["Primary"] = "shared-poster" },
        BackdropImageTags = ["shared-backdrop"]
    };

    private static EmbyItem AggregateMovie(int index, string host, string term = "共享影片") => new()
    {
        Id = "shared-" + index,
        Name = (term.Length > 0 ? term : "共享影片") + " " + (index + 1),
        Type = EmbyItemType.Movie,
        ParentId = "shared-library",
        ProductionYear = 2024,
        ProviderIds = new() { ["Tmdb"] = (80000 + index).ToString() },
        ImageTags = new() { ["Primary"] = "shared-poster" },
        BackdropImageTags = ["shared-backdrop"],
        RunTimeTicks = TimeSpan.FromMinutes(100).Ticks,
        MediaSources =
        [
            new() { Id = "shared-file", Name = host == "a.invalid" ? "1080p 版本" : "4K 版本", Container = "mkv" },
            new() { Id = "shared-file-alternative", Name = "备用版本", Container = "mkv" }
        ]
    };

    private static HttpResponseMessage AggregatePage(string host, string term, int start, int total = 45, int limit = 20) =>
        Json(new
        {
            Items = Enumerable.Range(start, Math.Clamp(total - start, 0, limit))
            .Select(index => AggregateMovie(index, host, term)).ToArray(),
            TotalRecordCount = total
        });
}
