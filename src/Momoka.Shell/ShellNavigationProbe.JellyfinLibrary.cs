using System.Net;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell;

/// <summary>
/// Jellyfin's library browser, checked against a fake Jellyfin transport: the web client's tab order,
/// its sort catalogue and defaults, its checkbox filter rows, its layout menu, and the three movie
/// suggestion shelves. No real server is contacted and no library is written to.
/// </summary>
internal sealed partial class ShellNavigationProbe
{
    private async Task JellyfinLibraryAsync()
    {
        using var fixture = CreateFixture();
        var poster = await FixtureImageAsync();
        // The restore path probes the product and the current user before the first page exists, so the
        // fake transport has to answer those before SelectAsync, not only the library requests after it.
        fixture.Transport.Reply = (request, _) => Task.FromResult(JellyfinResponse(request, poster));
        await fixture.SelectAsync(kind: MediaServerKind.Jellyfin);

        var frame = new Frame();
        _root.Children.Add(frame);
        LibraryPage? page = null;
        try
        {
            var movies = new LibraryRequest
            {
                Services = fixture.Services,
                Title = "离线 Jellyfin 电影",
                ParentId = "jf-movies",
                CollectionType = "movies"
            };
            frame.Navigate(typeof(LibraryPage), movies);
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.IsJellyfin, "探针没有落在 Jellyfin 库上");

            Require(TabLabels(page) == "电影,建议,预告片,我的最爱,合集,类型",
                $"Jellyfin 电影库标签与网页端不同：{TabLabels(page)}");
            Require(page.ViewModel.SortLabel == "名称" && page.ViewModel.SortKey == "SortName,ProductionYear"
                && !page.ViewModel.SortDescending, $"Jellyfin 电影默认排序不对（{page.ViewModel.SortLabel}）");
            Require(SortLabels(page) == "名称,随机,公众评分,影评人评分,加入日期,播放日期,家长分级,播放次数,发行日期,播放时长",
                $"Jellyfin 电影排序菜单不对：{SortLabels(page)}");
            Require(Get<Button>(page, "LibrarySettingsButton").Visibility == Visibility.Collapsed,
                "Jellyfin 库仍然露出 Emby 的设置齿轮");
            Require(Get<Button>(page, "JellyfinLayoutButton").Visibility == Visibility.Visible,
                "Jellyfin 库没有布局按钮");
            Require(Get<Button>(page, "FilterButton").Visibility == Visibility.Visible,
                "Jellyfin 库没有筛选按钮");

            var layoutLabels = page.BuildJellyfinLayoutMenu();
            Require(string.Join(',', layoutLabels) == "横幅图,列表,海报,海报卡片,缩略图,缩略图卡片",
                $"Jellyfin 布局菜单不对：{string.Join(',', layoutLabels)}");
            page.ShowJellyfinLayoutMenu();
            await LayoutAsync(page);
            await SaveProbeScreenAsync("jellyfin-library-layout-menu-screen.png");
            page.HideJellyfinLayoutMenu();
            await SettleAsync();

            page.ShowFilterPanel();
            await SettleAsync();
            Require(page.FilterOverlay.Visibility == Visibility.Visible, "Jellyfin 筛选面板没有打开");
            Require(string.Join(',', page.FilterPane.RowLabels) == "播放状态,功能,类型,家长分级,标签,视频类型,年份",
                $"Jellyfin 电影筛选行与网页端不同：{string.Join(',', page.FilterPane.RowLabels)}");
            var status = page.FilterPane.ViewModel.Rows.OfType<ViewModels.FilterChecksRow>()
                .Single(row => row.Label == "播放状态");
            Require(string.Join(',', status.Values.Select(check => check.Label)) == "已播放,未播放,可恢复播放,我的最爱",
                "Jellyfin 播放状态不是一组复选");
            await LayoutAsync(page);
            await SaveProbeScreenAsync("jellyfin-library-filter-screen.png");
            page.CloseFilter();
            await SettleAsync();

            page.ViewModel.ChooseJellyfinView(LibraryView.PosterCard);
            await UntilAsync(() => page.ViewModel.View == LibraryView.PosterCard && !page.ViewModel.Busy);
            await LayoutAsync(page);
            var card = Descendants(Get<ItemsView>(page, "CardWall")).OfType<PosterCard>().FirstOrDefault();
            Require(card?.Card is { CardLayoutVisibility: Visibility.Visible }, "海报卡片没有画出 Jellyfin 的卡片底");
            await SaveReturnFrameAsync(page, "jellyfin-library-poster-card.png");

            await SelectTabAsync(page, LibrarySection.Collections);
            Require(string.Join(',', page.ViewModel.SortEntries().Select(entry => entry.Option.Label))
                == "名称,公众评分,加入日期,家长分级,发行日期", "Jellyfin 合集排序菜单与网页端不同");
            Require(Get<Button>(page, "FilterButton").Visibility == Visibility.Collapsed,
                "Jellyfin 合集页不该有筛选");
            Require(Get<Button>(page, "JellyfinLayoutButton").Visibility == Visibility.Visible,
                "Jellyfin 合集页没有布局按钮");
            Require(fixture.Transport.Requests.Last(request => request.Contains("IncludeItemTypes=BoxSet", StringComparison.Ordinal))
                is not null, "Jellyfin 合集页没有问 BoxSet");
            await SaveReturnFrameAsync(page, "jellyfin-library-collections.png");

            await SelectTabAsync(page, LibrarySection.Recommended);
            await UntilAsync(() => page.ViewModel.IsRecommendedPage && !page.ViewModel.Busy
                && page.ViewModel.RecommendedShelves.Count == 3
                && page.ViewModel.RecommendedShelves.All(shelf => shelf.Cards.Count > 0));
            Require(page.ViewModel.RecommendedShelves.Select(shelf => shelf.Title)
                .SequenceEqual(["继续观看", "最新电影", "为你推荐"], StringComparer.Ordinal),
                $"Jellyfin 电影建议货架不对：{string.Join(',', page.ViewModel.RecommendedShelves.Select(shelf => shelf.Title))}");
            await LayoutAsync(page);
            await SaveReturnFrameAsync(page, "jellyfin-library-suggestions.png");

            page.Release();
            var shows = new LibraryRequest
            {
                Services = fixture.Services,
                Title = "离线 Jellyfin 剧集",
                ParentId = "jf-shows",
                CollectionType = "tvshows"
            };
            frame.Navigate(typeof(LibraryPage), shows);
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(TabLabels(page) == "节目,建议,即将播出,类型,制片发行商,剧集",
                $"Jellyfin 剧集库标签与网页端不同：{TabLabels(page)}");

            await SelectTabAsync(page, LibrarySection.Episodes);
            Require(string.Join(',', page.ViewModel.JellyfinViews().Select(view => view.ToString())) == "List,Poster,PosterCard",
                "Jellyfin 剧集布局菜单与网页端不同");
            Require(string.Join(',', page.ViewModel.SortEntries().Select(entry => entry.Option.Label))
                == "名称,TVDB 评分,加入日期,首映日期,播放日期,家长分级,播放次数,播放时长",
                "Jellyfin 剧集排序菜单与网页端不同");
            page.ShowFilterPanel();
            await SettleAsync();
            Require(string.Join(',', page.FilterPane.RowLabels) == "播放状态,剧集,功能,类型,家长分级,标签,视频类型,年份",
                $"Jellyfin 剧集筛选行与网页端不同：{string.Join(',', page.FilterPane.RowLabels)}");
            page.CloseFilter();
            await SettleAsync();
            await LayoutAsync(page);
            await SaveReturnFrameAsync(page, "jellyfin-library-episodes.png");
        }
        finally
        {
            page?.Release();
            _root.Children.Remove(frame);
        }
    }

    private static string TabLabels(LibraryPage page) =>
        string.Join(',', Get<WrapRow>(page, "LibrarySections").Children.OfType<ToggleButton>().Select(tab => tab.Content));

    private static string SortLabels(LibraryPage page) =>
        string.Join(',', page.ViewModel.SortEntries().Select(entry => entry.Option.Label));

    private static async Task SelectTabAsync(LibraryPage page, LibrarySection section)
    {
        var tab = Get<WrapRow>(page, "LibrarySections").Children.OfType<ToggleButton>()
            .Single(value => value.Tag is LibrarySection current && current == section);
        ((IToggleProvider)new ToggleButtonAutomationPeer(tab).GetPattern(PatternInterface.Toggle)).Toggle();
        await UntilAsync(() => page.ViewModel.Section == section && page.ViewModel.IsReady && !page.ViewModel.Busy);
    }

    private static HttpResponseMessage JellyfinResponse(HttpRequestMessage request, byte[] poster)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("/Images/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) };
        if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
            return Json(new { ProductName = "Jellyfin", ServerName = "离线 Jellyfin", Version = "10.10.7", Id = "jf-server" });
        if (path.EndsWith("/Users/Me", StringComparison.Ordinal))
            return Json(new { Id = "user", Name = "离线用户", Policy = new { IsAdministrator = true } });
        if (path.EndsWith("/Views", StringComparison.Ordinal))
            return Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 });
        if (path.EndsWith("/Items/Filters", StringComparison.Ordinal))
            return Json(new { Genres = new[] { "动画" }, OfficialRatings = new[] { "PG-13" }, Tags = new[] { "科幻" }, Years = new[] { "2024" } });
        if (path.EndsWith("/Movies/Recommendations", StringComparison.Ordinal))
            return Json(new[]
            {
                new { Items = new[] { Item("rec", EmbyItemType.Movie) }, RecommendationType = "SimilarToRecentlyPlayed", BaselineItemName = "离线电影" }
            });
        if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
            return Json(new[] { Item("latest", Query(request, "IncludeItemTypes") == EmbyItemType.Episode
                ? EmbyItemType.Episode : EmbyItemType.Movie) });
        if (path.EndsWith("/UserItems/Resume", StringComparison.Ordinal) || path.EndsWith("/Items/Resume", StringComparison.Ordinal))
            return Json(new { Items = new[] { Item("resume", EmbyItemType.Movie) }, TotalRecordCount = 1 });
        if (path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
            return Json(new { Items = new[] { Item("next", EmbyItemType.Episode) }, TotalRecordCount = 1 });
        if (path.EndsWith("/Genres", StringComparison.Ordinal))
            return Json(new { Items = new[] { Item("genre", EmbyItemType.Genre) }, TotalRecordCount = 1 });
        if (path.EndsWith("/Studios", StringComparison.Ordinal))
            return Json(new { Items = new[] { Item("studio", EmbyItemType.Studio) }, TotalRecordCount = 1 });
        if (path.EndsWith("/Items", StringComparison.Ordinal))
        {
            if (int.TryParse(Query(request, "StartIndex"), out var start) && start > 0)
                return Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 1 });
            var type = Query(request, "IncludeItemTypes") switch
            {
                "BoxSet" => EmbyItemType.BoxSet,
                "Episode" => EmbyItemType.Episode,
                "Series" => EmbyItemType.Series,
                "Trailer" => EmbyItemType.Trailer,
                _ => EmbyItemType.Movie
            };
            return Json(new { Items = new[] { Item("library", type) }, TotalRecordCount = 1 });
        }
        return Json(Array.Empty<object>());
    }

    private static EmbyItem Item(string prefix, string type)
    {
        var item = new EmbyItem { Id = prefix, Name = "离线 " + prefix, Type = type };
        item.ImageTags[type == EmbyItemType.Episode ? EmbyImageStore.Thumb : EmbyImageStore.Primary] = "fixture";
        if (type == EmbyItemType.Series)
        {
            item.ProductionYear = 2024;
            item.Status = "Continuing";
            item.UserData = new EmbyUserData { UnplayedItemCount = 2 };
        }
        else if (type == EmbyItemType.Episode)
        {
            item.SeriesName = "离线剧集";
            item.ParentIndexNumber = 1;
            item.IndexNumber = 7;
        }
        return item;
    }
}
