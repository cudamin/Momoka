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

            // 2026-11-01 用户令：Jellyfin 的混合内容库改走网页端 list.html，不再是本客户端自造的三栏。
            // 真服务器 10.10.7 的混合库 CollectionType 为 null，list.js 按 ParentId 取子项；
            // 这里用 44 个文件夹对齐用户截图里的「44 的 1-44」。
            page.Release();
            fixture.Transport.Reply = (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
                if (Query(request, "ParentId") == "jf-mixed")
                {
                    var folders = Enumerable.Range(1, 44).Select(index =>
                    {
                        var folder = new EmbyItem
                        {
                            Id = $"jf-folder-{index:00}",
                            Name = $"文件夹 {index:00}",
                            Type = EmbyItemType.Folder,
                            IsFolder = true
                        };
                        folder.ImageTags[EmbyImageStore.Primary] = "fixture";
                        return folder;
                    }).ToArray();
                    return Task.FromResult(Json(new { Items = folders, TotalRecordCount = folders.Length }));
                }
                return Task.FromResult(JellyfinResponse(request, poster));
            };
            frame.Navigate(typeof(LibraryPage), new LibraryRequest
            {
                Services = fixture.Services,
                Title = "混合内容",
                ParentId = "jf-mixed"
            });
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.IsJellyfinListPage && !page.ViewModel.IsSectionLibrary
                && !page.ViewModel.IsFolderDrillDown, "Jellyfin 混合内容根没有按 list 页处理");
            Require(TabLabels(page).Length == 0, $"Jellyfin 混合内容页仍画了分区签：{TabLabels(page)}");
            var mixedTitle = Get<TextBlock>(page, "LibraryTitle");
            Require(mixedTitle.Visibility == Visibility.Visible && Math.Abs(mixedTitle.FontSize - 20) < 0.1,
                $"Jellyfin 混合内容标题字号不是 20（{mixedTitle.FontSize}）");
            Require(Get<TextBlock>(page, "LibraryItemCount").Text == "44 的 1-44"
                && page.ViewModel.TotalLabel == "44 的 1-44",
                $"混合内容计数不是 list.html 的 ListPaging（{page.ViewModel.TotalLabel}）");
            Require(page.ViewModel.SortKey == "IsFolder,SortName" && page.ViewModel.SortLabel == "文件夹"
                && page.ViewModel.ToolbarSortLabel == "排序方式：文件夹",
                $"混合内容默认排序不是文件夹升序（{page.ViewModel.ToolbarSortLabel}）");
            Require(SortLabels(page) == "名称,公众评分,影评人评分,加入日期,播放日期,文件夹,家长分级,播放次数,发行日期,播放时长",
                $"Jellyfin 混合内容排序菜单与 list.js 不同：{SortLabels(page)}");
            var mixedQuery = Uri.UnescapeDataString(fixture.Transport.Requests
                .Last(value => value.Contains("ParentId=jf-mixed", StringComparison.Ordinal)));
            Require(!mixedQuery.Contains("Recursive=true", StringComparison.Ordinal)
                && !mixedQuery.Contains("IncludeItemTypes=", StringComparison.Ordinal),
                $"混合内容根不是按 list.js 取子项：{mixedQuery}");
            Require(Get<Button>(page, "PlayAllButton").Visibility == Visibility.Collapsed
                && Get<Button>(page, "PlayRandomButton").Visibility == Visibility.Collapsed,
                "混合内容根仍露出播放/随机播放；list.js 对 CollectionFolder 收起这两颗");
            Require(Get<Button>(page, "FilterButton").Visibility == Visibility.Visible,
                "混合内容根没有筛选键");
            Require(Get<Button>(page, "JellyfinMoreButton").Visibility == Visibility.Visible,
                "混合内容根没有更多键（list.html 的 btnViewSettings）");
            Require(Get<Button>(page, "JellyfinMoreButton").Content is FontIcon { Glyph: "\u22EE" },
                "更多键不是网页端的竖排三点");
            Require(Get<Button>(page, "JellyfinLayoutButton").Visibility == Visibility.Collapsed
                && Get<Button>(page, "LibrarySettingsButton").Visibility == Visibility.Collapsed,
                "混合内容根仍露出布局/设置键");
            Require(Get<Button>(page, "SortButton").Visibility == Visibility.Visible,
                "混合内容根没有排序键");
            await LayoutAsync(page);
            var mixedToolbar = Get<Grid>(page, "ToolbarSurface");
            var mixedToolbarBox = mixedToolbar.TransformToVisual(page)
                .TransformBounds(new(0, 0, mixedToolbar.ActualWidth, mixedToolbar.ActualHeight));
            Require(Math.Abs(mixedToolbarBox.Left + mixedToolbarBox.Width / 2 - page.ActualWidth / 2) <= 2,
                "混合内容工具条没有居中");

            page.ShowFilterPanel();
            await SettleAsync();
            Require(string.Join(',', page.FilterPane.RowLabels) == "播放状态,类型,视频类型,功能",
                $"Jellyfin 混合内容筛选行与 filtermenu.js 不同：{string.Join(',', page.FilterPane.RowLabels)}");
            var mixedStatus = page.FilterPane.ViewModel.Rows.OfType<ViewModels.FilterChecksRow>()
                .Single(row => row.Label == "播放状态");
            Require(string.Join(',', mixedStatus.Values.Select(check => check.Label)) == "未播放,已播放,我的最爱,继续观看",
                "Jellyfin 混合内容播放状态不是 filtermenu.js 的四颗复选");
            page.CloseFilter();
            await SettleAsync();

            Require(string.Join(',', page.BuildJellyfinMoreMenu()) == "封面图,横幅图,光盘封面图,徽标,缩略图,列表",
                $"Jellyfin 混合内容图片类型不是 viewSettings 的六项：{string.Join(',', page.BuildJellyfinMoreMenu())}");
            page.ShowJellyfinMoreMenu();
            await SettleAsync();
            await SaveProbeScreenAsync("jellyfin-list-more-menu-screen.png");
            page.HideJellyfinMoreMenu();
            page.ViewModel.SetJellyfinListShowTitles(false);
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(!page.ViewModel.JellyfinListShowsTitles && !page.ViewModel.Fields.Contains(LibraryField.Title),
                "关闭显示标题后卡片仍带着标题字段");
            page.ViewModel.SetJellyfinListShowTitles(true);
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.JellyfinListShowsTitles, "显示标题没有恢复");
            ResizeProbeClient(1100, 800);
            await LayoutAsync(page);
            await SaveProbeScreenAsync("jellyfin-list-1100-screen.png");
            ResizeProbeClient(760, 900);
            await LayoutAsync(page);
            await SaveProbeScreenAsync("jellyfin-list-760-screen.png");

            // 文件夹下钻页与库根是同一个 list 组件；区别是 itemType 为 Folder 时 list.js 会露出播放/随机播放。
            page.Release();

            // 主页「接下来看」那一排点进去的页：网页端 list.html?type=nextup。Jellyfin 上直接问
            // Shows/NextUp（分页并要总数），逐集列下一集；排序、筛选、随机播放与字母条照 list.js 收起。
            frame.Navigate(typeof(LibraryPage), LibraryRequest.ForRow(fixture.Services, HomeLayout.HomeRowTarget.NextUp));
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.Cards.Count == 1 && page.ViewModel.Cards[0].Item.Type == EmbyItemType.Episode
                && page.ViewModel.Cards[0].Item.Id == "next",
                "Jellyfin 接下来看页没有列出 Shows/NextUp 的下一集");
            Require(Get<Button>(page, "SortButton").Visibility == Visibility.Collapsed
                && Get<Button>(page, "FilterButton").Visibility == Visibility.Collapsed,
                "Jellyfin 接下来看页仍显示排序或筛选");
            Require(Get<Button>(page, "PlayRandomButton").Visibility == Visibility.Collapsed
                && Get<Button>(page, "PlayAllButton").Visibility == Visibility.Visible,
                "Jellyfin 接下来看页的播放键与网页端不同");
            Require(Get<TextBlock>(page, "LibraryItemCount").Text == "共 1 项",
                $"Jellyfin 接下来看页读数不对（{Get<TextBlock>(page, "LibraryItemCount").Text}）");
            var nextRequest = fixture.Transport.Requests.LastOrDefault(
                request => request.Contains("Shows/NextUp", StringComparison.Ordinal)) ?? "";
            Require(nextRequest.Contains("StartIndex=0", StringComparison.Ordinal)
                && nextRequest.Contains("EnableTotalRecordCount=true", StringComparison.Ordinal),
                $"Jellyfin 接下来看页没有按分页和总数取数：{nextRequest}");
            await LayoutAsync(page);
            await SaveProbeScreenAsync("jellyfin-nextup-page-screen.png");
            page.Release();

            fixture.Transport.Reply = (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
                if (Query(request, "ParentId") == "jf-folder")
                {
                    var movie = new EmbyItem { Id = "jf-folder-movie", Name = "文件夹里的电影", Type = EmbyItemType.Movie };
                    movie.ImageTags[EmbyImageStore.Primary] = "fixture";
                    return Task.FromResult(Json(new { Items = new[] { movie }, TotalRecordCount = 1 }));
                }
                return Task.FromResult(JellyfinResponse(request, poster));
            };
            frame.Navigate(typeof(LibraryPage), new LibraryRequest
            {
                Services = fixture.Services,
                Title = "文件夹",
                ParentId = "jf-folder",
                ParentType = EmbyItemType.Folder
            });
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.IsJellyfinListPage && page.ViewModel.IsFolderDrillDown,
                "Jellyfin 文件夹页没有走 list 组件");
            Require(Get<TextBlock>(page, "LibraryTitle").Visibility == Visibility.Collapsed,
                "Jellyfin 文件夹页仍显示页内标题");
            Require(Get<TextBlock>(page, "LibraryItemCount").Text == "1 的 1-1",
                $"Jellyfin 文件夹页计数不是 ListPaging（{Get<TextBlock>(page, "LibraryItemCount").Text}）");
            Require(Get<Button>(page, "PlayAllButton").Visibility == Visibility.Visible
                && Get<Button>(page, "PlayRandomButton").Visibility == Visibility.Visible,
                "Jellyfin 文件夹页没有按 list.js 露出播放/随机播放");
            Require(Get<Button>(page, "FilterButton").Visibility == Visibility.Visible
                && Get<Button>(page, "JellyfinMoreButton").Visibility == Visibility.Visible,
                "Jellyfin 文件夹页缺少筛选或更多键");
            await LayoutAsync(page);
            await SaveProbeScreenAsync("jellyfin-folder-list-screen.png");
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
