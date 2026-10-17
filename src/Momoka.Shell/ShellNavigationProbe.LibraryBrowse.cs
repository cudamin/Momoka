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
                // 「更多」现在只装电视剧库那两颗播放键（视图三选一已经交给设置面板）。弹出层是独立顶层
                // 窗口，窗口截图拍不到，所以这里读的是菜单对象本身。
                var more = Get<Button>(page, "LibraryMoreButton");
                var moreMenu = (MenuFlyout)more.Flyout!;
                moreMenu.ShowAt(more);
                await SettleAsync();
                if (!moreMenu.IsOpen)
                {
                    // 浮层偶尔在窗口还没拿到焦点时自己收回去；再开一次，仍然是用户点那颗键的同一条路。
                    moreMenu.ShowAt(more);
                    await SettleAsync();
                }
                Require(moreMenu.IsOpen, "更多菜单没有展开");
                Require(moreMenu.Items.OfType<MenuFlyoutSubItem>().All(item => item.Text != "视图"), "更多菜单里还留着旧的视图子菜单");
                var menuPlay = moreMenu.Items.OfType<MenuFlyoutItem>().Where(item => item.Text is "播放" or "随机播放").ToArray();
                Require(menuPlay.Length == 2
                    && menuPlay.All(item => (item.Visibility == Visibility.Visible) == (collection == "tvshows")), "播放与随机播放没有按库类型收进更多");
                await SaveProbeScreenAsync($"library-{collection}-more-screen.png");
                moreMenu.Hide();

                // 设置面板：网页端那颗齿轮里的四组东西。这里走的是用户点齿轮的同一条路 —— 打开浮层、
                // 读面板上的控件、改一处、看请求与版面跟着变。
                var settingsButton = Get<Button>(page, "LibrarySettingsButton");
                Require(settingsButton.Visibility == Visibility.Visible, "媒体库页没有设置入口");
                var openSettings = FrameworkElementAutomationPeer.CreatePeerForElement(settingsButton)
                    ?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                Require(openSettings is not null, "设置入口没有可调用的自动化入口");
                openSettings!.Invoke();
                await SettleAsync();
                Require(page.SettingsOverlay.Visibility == Visibility.Visible, "设置面板没有打开");

                var pane = page.SettingsPane;
                Require(pane.ViewCombo.Items.Count == 7 && pane.SizeCombo.Items.Count == 8
                    && pane.FieldRows.Children.Count == 14, "设置面板的四组东西没有照网页端摆齐");
                Require(pane.SizeSection.Visibility == Visibility.Visible, "海报视图下没有图像大小这一行");
                var before = page.ViewModel.Display;

                // 视图：换成列表，网格要变成一行一条，图像大小那一行同时收起（列表没有格子可缩放）。
                pane.SetView(LibraryView.List);
                await UntilAsync(() => page.ViewModel.View == LibraryView.List && !page.ViewModel.Busy);
                Require(Get<ItemsView>(page, "Cards").Layout is StackLayout, "选了列表视图，网格没有换成一行一条");
                Require(pane.SizeSection.Visibility == Visibility.Collapsed, "列表视图还留着图像大小那一行");

                // 视图：换成表格，行模板换成表格，列头按勾上的字段生成。
                pane.SetView(LibraryView.Table);
                await UntilAsync(() => page.ViewModel.View == LibraryView.Table && !page.ViewModel.Busy);
                Require(page.TableHeader.Visibility == Visibility.Visible
                    && page.TableHeader.Children.Count == page.ViewModel.Fields.Count, "表格视图没有按字段生成列头");
                Require(page.ViewModel.Cards.Count > 0 && page.ViewModel.Cards[0].Cells.Count == page.ViewModel.Fields.Count,
                    "表格视图的行没有按字段分格");

                // 图像大小：回到海报，换到超大档，格子宽度要按档位变，而且卡片是按新宽度重造的。
                pane.SetView(LibraryView.Poster);
                await UntilAsync(() => page.ViewModel.View == LibraryView.Poster && !page.ViewModel.Busy);
                var defaultWidth = page.ViewModel.CellWidth;
                pane.SetCardSize(LibraryCardSize.ExtraLarge);
                await UntilAsync(() => page.ViewModel.CardSize == LibraryCardSize.ExtraLarge && !page.ViewModel.Busy);
                Require(page.ViewModel.CellWidth > defaultWidth
                    && Math.Abs(page.ViewModel.CardWidth - page.ViewModel.CellWidth) < 0.01,
                    $"图像大小换了档，格子宽度没跟着走（{defaultWidth:0} → {page.ViewModel.CellWidth:0}，"
                    + $"卡片 {page.ViewModel.CardWidth}，档位 {page.ViewModel.CardSize}，视图 {page.ViewModel.View}，"
                    + $"面板 {pane.Current.CardSize}）");

                // 显示字段：勾上「导演」，请求里要出现 People，卡片那一行要真的多出导演名。
                pane.SetField(LibraryField.Director, true);
                await UntilAsync(() => page.ViewModel.Fields.Contains(LibraryField.Director) && !page.ViewModel.Busy);
                Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal))
                    .Contains("People", StringComparison.Ordinal), "勾了导演，请求里没有要 People");
                Require(pane.Current.Fields.Contains(LibraryField.Director), "勾了导演，面板上没有记下这一项");

                // 分组到合集：关掉它，请求里要明确发 false（网页端每次都发这一键）。
                pane.SetGroupIntoCollections(false);
                await UntilAsync(() => !page.ViewModel.GroupIntoCollections && !page.ViewModel.Busy);
                Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal))
                    .Contains("GroupItemsIntoCollections=false", StringComparison.Ordinal), "关掉分组，请求里没有发 false");

                await SaveProbeScreenAsync($"library-{collection}-settings-screen.png");
                // 原样还回去，并且是「记住」的那一种：面板每改一处都会写进这个库的记忆，只用
                // remember:false 还回去，下一套库打开时拿到的仍是探针上一趟留下的「超大」。
                page.ViewModel.ApplyDisplay(before, remember: true);
                await UntilAsync(() => !page.ViewModel.Busy);
                page.CloseSettings();
                await SettleAsync();
                Require(page.SettingsOverlay.Visibility == Visibility.Collapsed, "设置面板没有收起");
                Require(page.ViewModel.View == before.View && page.ViewModel.CardSize == before.CardSize
                    && page.ViewModel.Fields.SequenceEqual(before.Fields), "设置面板没有把原来的那一套还回去");
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
