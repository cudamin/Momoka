using System.Net;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LibraryFavoritesAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var image = await FixtureImageAsync(400, 600);
        var series = new EmbyItem
        {
            Id = "favorite-show",
            Name = "碧蓝之海",
            Type = EmbyItemType.Series,
            ProductionYear = 2018,
            Status = "Continuing",
            UserData = new EmbyUserData { IsFavorite = true }
        };
        series.ImageTags["Primary"] = "favorites-fixture";
        var episode = new EmbyItem
        {
            Id = "favorite-episode",
            Name = "败北",
            Type = EmbyItemType.Episode,
            SeriesName = "BORDER 警视厅搜查一课杀人犯搜查",
            SeriesId = "favorite-border",
            ParentIndexNumber = 1,
            IndexNumber = 7,
            UserData = new EmbyUserData { IsFavorite = true }
        };
        episode.ImageTags["Primary"] = "favorites-fixture";
        var shows = new List<EmbyItem> { series };
        var episodes = new List<EmbyItem> { episode };
        var failShows = false;
        var waitEpisodes = false;
        var duplicatePage = false;
        var waiting = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (Query(request, "ParentId") == "library-a" && Query(request, "Filters") == "IsFavorite")
            {
                var showRow = Query(request, "IncludeItemTypes") == "Series";
                if (showRow && failShows) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                if (!showRow && waitEpisodes) return waiting.Task;
                var source = showRow ? shows : episodes;
                var start = int.Parse(Query(request, "StartIndex") ?? "0");
                var limit = int.Parse(Query(request, "Limit") ?? "100");
                if (!showRow && duplicatePage && start > 0)
                    return Task.FromResult(Json(new { Items = new[] { source[0], source[^1] }, TotalRecordCount = 102 }));
                return Task.FromResult(Json(new { Items = source.Skip(start).Take(limit).ToArray(), TotalRecordCount = source.Count }));
            }
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        _root.Children.Add(frame);
        LibraryPage? page = null;
        try
        {
            var request = new LibraryRequest
            {
                Services = fixture.Services,
                Title = "电视节目",
                CollectionType = "tvshows",
                ParentId = "library-a",
                Section = LibrarySection.Favorites
            };
            frame.Navigate(typeof(LibraryPage), request);
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.FavoriteRows.Count == 2 && page.ViewModel.Cards.Count == 2
                && page.ViewModel.VisibleFavoriteRows.Count == 2, "收藏节目与集没有各自载入");
            Require(string.Join(',', page.ViewModel.FavoriteRows.Select(row => row.Title)) == "收藏节目,收藏集", "收藏两排标题或顺序不对");
            Require(page.FavoritesScroll.Visibility == Visibility.Visible && page.TableScroll.Visibility == Visibility.Collapsed
                && Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Collapsed, "收藏页仍显示网格或工具栏");
            var show = page.ViewModel.FavoriteRows[0];
            var episodeRow = page.ViewModel.FavoriteRows[1];
            Require(show.Cards.Single().Subtitle == "2018 - 现在" && show.Cards.Single().CardWidth == 148,
                "收藏节目没有网页年份和竖版海报尺寸");
            Require(episodeRow.Cards.Single().Title == episode.SeriesName && episodeRow.Cards.Single().Subtitle == "S1:E7 - 败北"
                && episodeRow.Cards.Single().CardWidth == 216, "收藏集没有剧名、季集号或横卡尺寸");
            Require(!episodeRow.Cards[0].IsUpcoming && episodeRow.Cards[0].PlayVisibility == Visibility.Visible,
                "收藏集被误当成不可播放的虚拟预告");
            var relevant = fixture.Transport.Requests.Where(value => value.Contains("ParentId=library-a", StringComparison.Ordinal)
                && value.Contains("IsFavorite", StringComparison.Ordinal)).ToArray();
            Require(relevant.Length == 2 && relevant.All(value => value.Contains("SortBy=SortName", StringComparison.Ordinal)
                && !value.Contains("GroupItemsIntoCollections", StringComparison.Ordinal)), "收藏没有两条独立的本库查询");
            foreach (var size in new[] { (Width: 1100, Height: 900), (Width: 760, Height: 800), (Width: 360, Height: 800) })
            {
                ResizeProbeClient(size.Width, size.Height);
                await LayoutAsync(page);
                await UntilAsync(() => Descendants(page.FavoritesGroups).OfType<PosterCard>().Count() >= 2);
                var header = Get<Border>(page, "LibrarySectionsSurface");
                var box = header.TransformToVisual(page).TransformBounds(new(0, 0, header.ActualWidth, header.ActualHeight));
                Require(Math.Abs(box.Left + box.Width / 2 - page.ActualWidth / 2) <= 2, "收藏分类栏不居中");
                for (var index = 0; index < page.ViewModel.VisibleFavoriteRows.Count; index++)
                {
                    var group = (FrameworkElement)page.FavoritesGroups.TryGetElement(index)!;
                    var repeater = Descendants(group).OfType<ItemsRepeater>().First();
                    var button = (Button)repeater.TryGetElement(0)!;
                    var card = (PosterCard)button.Content;
                    var art = Get<Border>(card, "Art");
                    var artBox = art.TransformToVisual(page).TransformBounds(new(0, 0, art.ActualWidth, art.ActualHeight));
                    Require(artBox.Left >= -1 && artBox.Right <= page.ActualWidth + 1, "收藏卡片在窄窗口被裁切");
                    Require(Get<TextBlock>(card, "TitleLine").TextAlignment == TextAlignment.Center
                        && Get<TextBlock>(card, "SubLine").TextAlignment == TextAlignment.Center, "收藏卡片文字不居中");
                    Require(Math.Abs(art.ActualHeight - Math.Round(art.ActualWidth * (index == 0 ? 1.5 : 9d / 16))) <= 1,
                        "收藏卡片比例不对");
                    Require(Get<Image>(card, "Poster").Stretch == (index == 0 ? Stretch.UniformToFill : Stretch.Uniform),
                        "收藏集里的竖图没有完整保留在横卡中");
                }
                await SaveProbeScreenAsync($"library-tvshows-favorites-{size.Width}-screen.png");
            }
            ResizeProbeClient(1100, 900);
            await LayoutAsync(page);
            var rootBefore = page.ViewModel.FavoriteRows.ToArray();
            shows.Clear();
            await page.ViewModel.ReloadAsync();
            await LayoutAsync(page);
            Require(page.ViewModel.VisibleFavoriteRows.Count == 1 && ReferenceEquals(page.ViewModel.VisibleFavoriteRows[0], episodeRow)
                && page.ViewModel.FavoriteRows.SequenceEqual(rootBefore), "只有收藏集时留下空节目区域或重建两排");
            var singleGroup = (FrameworkElement)page.FavoritesGroups.TryGetElement(0)!;
            var singleTop = singleGroup.TransformToVisual(page.FavoritesScroll).TransformPoint(new(0, 0)).Y;
            Require(singleTop < 4, "空收藏节目排仍留下纵向空白");
            await SaveProbeScreenAsync("library-tvshows-favorites-episodes-only-screen.png");
            shows.Add(series);
            episodes.Clear();
            await page.ViewModel.ReloadAsync();
            Require(page.ViewModel.VisibleFavoriteRows.Count == 1 && page.ViewModel.VisibleFavoriteRows[0].Shelf == FavoriteShelf.Shows,
                "只有收藏节目时仍显示空收藏集排");
            shows.Clear();
            await page.ViewModel.ReloadAsync();
            Require(page.ViewModel.VisibleFavoriteRows.Count == 0 && page.ViewModel.ShowEmptyNotice
                && page.ViewModel.EmptyNotice.Contains("暂无收藏", StringComparison.Ordinal), "双空收藏没有明确空态");
            await LayoutAsync(page);
            await SaveProbeScreenAsync("library-tvshows-favorites-empty-screen.png");
            shows.Add(series);
            episodes.Add(episode);
            failShows = true;
            await page.ViewModel.ReloadAsync();
            await LayoutAsync(page);
            Require(show.Error.Length > 0 && show.RetryVisibility == Visibility.Visible
                && episodeRow.Cards.Count == 1 && !page.ViewModel.ShowEmptyNotice, "一排失败掩盖其它收藏或冒充空态");
            await SaveProbeScreenAsync("library-tvshows-favorites-partial-error-screen.png");
            failShows = false;
            var failedGroup = (FrameworkElement)page.FavoritesGroups.TryGetElement(0)!;
            var retry = Descendants(failedGroup).OfType<Button>().Single(button => button.Name == "FavoriteRetry");
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(retry)!.GetPattern(PatternInterface.Invoke)!).Invoke();
            await UntilAsync(() => !show.Loading && show.Cards.Count == 1);
            Require(show.Error.Length == 0 && episodeRow.Cards[0].Item.Source?.Key == show.Cards[0].Item.Source?.Key, "收藏重试没有恢复同一账号");

            var extra = new EmbyItem
            {
                Id = "favorite-page-last",
                Name = "下一页的集",
                Type = EmbyItemType.Episode,
                SeriesName = "BORDER",
                ParentIndexNumber = 1,
                IndexNumber = 8,
                UserData = new EmbyUserData { IsFavorite = true }
            };
            episodes.Clear();
            episodes.AddRange(Enumerable.Range(0, 101).Select(index => new EmbyItem
            {
                Id = "favorite-page-" + index,
                Name = "收藏集 " + index,
                Type = EmbyItemType.Episode,
                SeriesName = "BORDER",
                ParentIndexNumber = 1,
                IndexNumber = index + 1,
                UserData = new EmbyUserData { IsFavorite = true }
            }));
            episodes.Add(extra);
            duplicatePage = true;
            await page.ViewModel.ReloadAsync();
            Require(episodeRow.ReadCount == 100 && episodeRow.Cards.Count == 100 && episodeRow.MoreVisibility == Visibility.Visible,
                "收藏集第一页没有独立分页入口");
            var retainedShow = show.Cards[0];
            var reads = fixture.Transport.Requests.Count;
            await page.ViewModel.LoadFavoriteRowAsync(episodeRow);
            Require(episodeRow.ReadCount == 102 && episodeRow.Cards.Count == 101 && !episodeRow.HasMore
                && ReferenceEquals(show.Cards[0], retainedShow), "收藏集分页重复、漏项或重建节目卡");
            Require(fixture.Transport.Requests.Skip(reads).Any(value => value.Contains("StartIndex=100", StringComparison.Ordinal)
                && value.Contains("IncludeItemTypes=Episode", StringComparison.Ordinal)), "收藏集分页起点没有用服务器返回条数");

            await LayoutAsync(page);
            Require(page.FavoritesScroll.ScrollableHeight > 400, $"收藏分页没有撑开竖向内容（高度 {page.FavoritesScroll.ScrollableHeight:0}）");
            page.FavoritesScroll.ChangeView(null, 400, null, disableAnimation: true);
            await UntilAsync(() => page.FavoritesScroll.VerticalOffset > 0);
            await LayoutAsync(page);
            Require(page.FavoritesScroll.VerticalOffset > 0, "收藏卡片不能竖向滚动");
            var offset = page.FavoritesScroll.VerticalOffset;
            var removed = episodeRow.Cards[5];
            Require(await page.ViewModel.RemoveAsync(removed, page.ViewModel.QueryToken, deleted: false), "取消收藏未移除单集");
            await LayoutAsync(page);
            Require(!episodeRow.Cards.Contains(removed) && !page.ViewModel.Cards.Contains(removed)
                && ReferenceEquals(show.Cards[0], retainedShow) && Math.Abs(page.FavoritesScroll.VerticalOffset - offset) <= 1,
                "取消收藏改变另一排或跳回第一页");
            page.Release();
            Require(Math.Abs(request.ScrollOffset - offset) <= 1, "收藏页离开没有保存滚动位置");
            duplicatePage = false;
            frame.Navigate(typeof(LibraryPage), request);
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            await LayoutAsync(page);
            Require(Math.Abs(page.FavoritesScroll.VerticalOffset - offset) <= 2, "返回收藏页没有恢复滚动位置");

            waiting = Pending<HttpResponseMessage>();
            waitEpisodes = true;
            var late = page.ViewModel.ReloadAsync();
            await UntilAsync(() => page.ViewModel.FavoriteRows[1].Loading);
            await page.ViewModel.ChooseSectionAsync(LibrarySection.Items);
            waiting.SetResult(Json(new { Items = new[] { episode }, TotalRecordCount = 1 }));
            await late;
            Require(page.ViewModel.Section == LibrarySection.Items && page.FavoritesScroll.Visibility == Visibility.Collapsed
                && page.ViewModel.Cards.All(card => card.Item.Id != episode.Id), "迟到收藏集覆盖了节目列表");
            Require(fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)), "收藏页验证产生真实写操作");
        }
        finally
        {
            waiting.TrySetResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            page?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
