using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LibraryBrowseAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var oldTheme = ThemeHost.Current;
        var poster = await FixtureImageAsync();
        fixture.Transport.Reply = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            if (Query(request, "ParentId") is null) return Task.FromResult(FakeTransport.Standard(request));
            var path = request.RequestUri.AbsolutePath;
            var index = path.EndsWith("/Genres", StringComparison.Ordinal) || path.EndsWith("/Studios", StringComparison.Ordinal);
            var type = index ? path.EndsWith("/Genres", StringComparison.Ordinal) ? "Genre" : "Studio"
                : (Query(request, "IncludeItemTypes") ?? EmbyItemType.Movie).Split(',')[0];
            var sample = new EmbyItem { Id = "library-sample-" + type, Name = index ? "科幻与动画" : "离线媒体库内容", Type = type };
            sample.ImageTags["Primary"] = "fixture";
            return Task.FromResult(Json(new { Items = new[] { sample }, TotalRecordCount = 1 }));
        };
        var frame = new Frame();
        _root.Children.Add(frame);
        LibraryPage? page = null;
        try
        {
            foreach (var collection in new[] { "movies", "tvshows" })
            {
                var request = new LibraryRequest { Services = fixture.Services, Title = "离线媒体库", ParentId = "library-a", CollectionType = collection };
                page?.Release();
                frame.Navigate(typeof(LibraryPage), request);
                page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
                Require(page.ViewModel.Cards.Count == 1, "媒体库首屏未载入");
                var tabs = Get<WrapRow>(page, "LibrarySections");
                var labels = string.Join(',', tabs.Children.OfType<ToggleButton>().Select(tab => tab.Content));
                Require(labels == (collection == "movies" ? "影片,推荐,预告片,合集,类型,收藏,文件夹" : "节目,推荐,预告,收藏,类型,播出平台,集,文件夹"), "分类顺序与网页不同");
                Require(Get<Button>(page, "PlayAllButton").Visibility == (collection == "movies" ? Visibility.Visible : Visibility.Collapsed), "电影与电视剧的播放工具栏未分开");
                Require(Get<TextBlock>(page, "LibraryItemCount").Text == "共 1 项", "工具栏总数未更新");
                foreach (var width in new[] { 1100, 760, 520, 360 })
                {
                    ResizeProbeClient(width, 800);
                    await LayoutAsync(page);
                    var surface = Get<Border>(page, "LibrarySectionsSurface");
                    var box = surface.TransformToVisual(page).TransformBounds(new(0, 0, surface.ActualWidth, surface.ActualHeight));
                    Require(box.Left >= -1 && box.Right <= page.ActualWidth + 1, "分类栏被裁切");
                    foreach (var tab in tabs.Children.OfType<ToggleButton>())
                    {
                        var peer = new ToggleButtonAutomationPeer(tab);
                        Require(peer.GetAutomationId() == "LibrarySection" + tab.Tag && peer.GetName() == tab.Content.ToString(), "分类无法稳定定位或读出名称");
                    }
                    var toolbar = Get<Grid>(page, "ToolbarSurface");
                    var controls = toolbar.TransformToVisual(page).TransformBounds(new(0, 0, toolbar.ActualWidth, toolbar.ActualHeight));
                    Require(controls.Left >= -1 && controls.Right <= page.ActualWidth + 1 && controls.Top >= box.Bottom, "工具栏未在分类下方或被裁切");
                    await SaveReturnFrameAsync(page, $"library-{collection}-{width}.png");
                }
                ResizeProbeClient(900, 700);
                await LayoutAsync(page);
                foreach (var theme in UiThemes.All)
                {
                    ThemeHost.Apply(theme);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"library-{collection}-{theme.Id}-screen.png");
                }
                async Task SelectAsync(LibrarySection section)
                {
                    var tab = Get<WrapRow>(page, "LibrarySections").Children.OfType<ToggleButton>().Single(value => (LibrarySection)value.Tag == section);
                    ((IToggleProvider)new ToggleButtonAutomationPeer(tab).GetPattern(PatternInterface.Toggle)).Toggle();
                    await UntilAsync(() => page.ViewModel.Section == section && page.ViewModel.IsReady && !page.ViewModel.Busy);
                    Require(request.Section == section && Get<WrapRow>(page, "LibrarySections").Children.OfType<ToggleButton>().Count(value => value.IsChecked == true) == 1, "分类选择未保存或不是单选");
                }
                page.ViewModel.ChooseSort(EmbySortBy.Find(EmbySortBy.DateAdded)!);
                await UntilAsync(() => !page.ViewModel.Busy);
                page.ViewModel.Filters.Genres.Add("动画");
                page.ViewModel.ApplyFilters();
                await UntilAsync(() => !page.ViewModel.Busy);
                await LayoutAsync(page);
                Require(Get<Button>(page, "FilterButton").ActualWidth >= 52, "筛选数字徽标被窄按钮裁切");
                await SaveProbeScreenAsync($"library-{collection}-filter-badge-screen.png");
                await SelectAsync(LibrarySection.Genres);
                Require(Get<Button>(page, "FilterButton").Visibility == Visibility.Collapsed
                    && Get<WrapRow>(page, "LibraryFilterChips").Visibility == Visibility.Collapsed, "类型索引仍有筛选残留");
                page.ViewModel.Open(page.ViewModel.Cards[0]);
                await UntilAsync(() => request.BrowseGroup is not null && page.ViewModel.IsReady && !page.ViewModel.Busy);
                Require(Get<Button>(page, "FilterButton").Visibility == Visibility.Visible
                    && fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal)).Contains("Genres=", StringComparison.Ordinal), "类型下钻没有真正发出作用域查询");
                await page.ViewModel.LeaveBrowseGroupAsync();
                await SelectAsync(LibrarySection.Items);
                Require(page.ViewModel.SortKey == EmbySortBy.DateAdded && page.ViewModel.Filters.Genres.Contains("动画"), "分类切回丢失原排序筛选");
                await SelectAsync(LibrarySection.Favorites);
                Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal)).Contains("IsFavorite", StringComparison.Ordinal), "库内收藏未带固定条件");
                await SelectAsync(collection == "movies" ? LibrarySection.Collections : LibrarySection.Episodes);
                Require(page.ViewModel.Cards[0].Item.Type == (collection == "movies" ? EmbyItemType.BoxSet : EmbyItemType.Episode), "合集或单集查询没有切换类型");
                if (collection == "tvshows")
                {
                    await SelectAsync(LibrarySection.Studios);
                    page.ViewModel.Open(page.ViewModel.Cards[0]);
                    await UntilAsync(() => request.BrowseGroup is not null && page.ViewModel.IsReady && !page.ViewModel.Busy);
                    Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal)).Contains("Studios=", StringComparison.Ordinal), "播出平台没有真正下钻");
                }
                await SelectAsync(LibrarySection.Recommended);
                Require(Get<Button>(page, "SortButton").Visibility == Visibility.Collapsed && Get<Button>(page, "FilterButton").Visibility == Visibility.Collapsed, "推荐还显示不生效的排序筛选");
                await SelectAsync(LibrarySection.Folders);
                Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal)).Contains("Recursive=false", StringComparison.Ordinal), "文件夹被拍平");
                await SelectAsync(LibrarySection.Items);
                page.BuildSortMenu();
                var menu = Get<MenuFlyout>(page, "SortMenu");
                Require(menu.Items.OfType<RadioMenuFlyoutItem>().Count(value => value.IsChecked) == 2, "排序字段与方向没有分别显示选中状态");
                // 「更多」是这两套工具栏里唯一一个把东西藏起来的入口：视图的三档形状、电视剧库的播放
                // 与随机播放都在它里面。弹出层是独立顶层窗口，窗口截图拍不到（同目录那张 -more-screen.png
                // 上什么菜单都没有），所以这里读的是菜单对象本身：它开没开、里面装着什么。
                var more = Get<Button>(page, "LibraryMoreButton");
                var moreMenu = (MenuFlyout)more.Flyout!;
                moreMenu.ShowAt(more);
                await SettleAsync();
                Require(moreMenu.IsOpen, "更多菜单没有展开");
                Require(moreMenu.Items.OfType<MenuFlyoutSubItem>().Any(item => item.Text == "视图"), "更多菜单里没有视图子菜单");
                var menuPlay = moreMenu.Items.OfType<MenuFlyoutItem>().Where(item => item.Text is "播放" or "随机播放").ToArray();
                Require(menuPlay.Length == 2
                    && menuPlay.All(item => (item.Visibility == Visibility.Visible) == (collection == "tvshows")), "播放与随机播放没有按库类型收进更多");
                var viewItems = Get<MenuFlyoutSubItem>(page, "ViewButton").Items.OfType<RadioMenuFlyoutItem>().ToArray();
                Require(string.Join(',', viewItems.Select(item => item.Tag)) == "Poster,Thumb,List"
                    && viewItems.All(item => Enum.TryParse<LibraryView>(item.Tag as string, out _)), "视图菜单项与三档形状对不上");
                Require(viewItems.Count(item => item.IsChecked) == 1
                    && viewItems.Single(item => item.IsChecked).Tag as string == page.ViewModel.View.ToString(), "视图菜单没有标出当前视图");
                await SaveProbeScreenAsync($"library-{collection}-more-screen.png");
                moreMenu.Hide();
                // 子菜单项在展开之前不在树里，也就没有可点的自动化入口（试过：Expand 之后 peer 仍为空），
                // 所以这里验的是能悄悄坏掉的那一半 —— Tag 能不能解析成三档形状（上面已验），以及换了形状
                // 之后网格真的换了模板与布局。点下去会调用 ChooseView，走的正是这一条。
                page.ViewModel.ChooseView(LibraryView.List);
                await UntilAsync(() => !page.ViewModel.Busy);
                Require(Get<ItemsView>(page, "Cards").Layout is StackLayout, "选了列表视图，网格没有换成一行一条");
                page.ViewModel.ChooseView(LibraryView.Poster);
                await UntilAsync(() => !page.ViewModel.Busy);
                Require(Get<ItemsView>(page, "Cards").Layout is UniformGridLayout, "视图切回海报没有恢复网格");
            }
            Require(fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)), "媒体库浏览产生写请求");
            var pending = Pending<HttpResponseMessage>();
            fixture.Transport.Reply = (request, _) => Query(request, "IncludeItemTypes") == "Episode"
                ? pending.Task : Task.FromResult(FakeTransport.Standard(request));
            var old = page!.ViewModel.ChooseSectionAsync(LibrarySection.Episodes);
            Require(page.ViewModel.Cards.Count == 0, "切换分类仍显示上一类的旧列表");
            await page.ViewModel.ChooseSectionAsync(LibrarySection.Items);
            pending.SetResult(Json(new { Items = new[] { Episode("a") }, TotalRecordCount = 1 }));
            await old;
            Require(page.ViewModel.Section == LibrarySection.Items && page.ViewModel.Cards.All(card => card.Item.Type != EmbyItemType.Episode), "迟到的单集查询覆盖了节目列表");
            fixture.Transport.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            await page.ViewModel.ChooseSectionAsync(LibrarySection.Episodes);
            Require(page.ViewModel.Cards.Count == 0 && page.ViewModel.TotalLabel == "加载失败", "分类载入失败仍冒充旧列表或加载中");
        }
        finally
        {
            page?.Release();
            _root.Children.Remove(frame);
            ThemeHost.Apply(oldTheme);
            ResizeInspect(1420, 980);
        }
    }
}
