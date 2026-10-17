using System.Net;
using System.Reflection;
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
            // 继续观看那一条没有 IncludeItemTypes（它就是 Resume），推荐页那一排装的是单集。
            var resume = path.EndsWith("/Items/Resume", StringComparison.Ordinal);
            var type = index ? path.EndsWith("/Genres", StringComparison.Ordinal) ? "Genre" : "Studio"
                : resume ? EmbyItemType.Episode
                : (Query(request, "IncludeItemTypes") ?? EmbyItemType.Movie).Split(',')[0];
            var sample = new EmbyItem { Id = "library-sample-" + type, Name = index ? "科幻与动画" : "离线媒体库内容", Type = type };
            sample.ImageTags["Primary"] = "fixture";
            // 推荐页三排卡片底下那两行字要真的能读出来：剧集写年份区间，单集写「SxxExx - 集名」。
            if (type == EmbyItemType.Series)
            {
                sample.ProductionYear = 2006;
                sample.Status = "Continuing";
                sample.UserData = new EmbyUserData { UnplayedItemCount = 3 };
            }
            else if (type == EmbyItemType.Episode)
            {
                sample.Name = "敏北";
                sample.ParentIndexNumber = 1;
                sample.IndexNumber = 7;
            }
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
                // 推荐页：电视剧库是三排货架（继续观看／最新集／最近发布的剧集，2026-10-17 用户令），
                // 电影库照旧是网格（网页端电影库的推荐是另一组行，这一轮没做）。
                if (collection == "tvshows")
                {
                    Require(string.Join(',', page.ViewModel.RecommendedShelves.Select(shelf => shelf.Title)) == "继续观看,最新集,最近发布的剧集", "推荐页三排标题与网页不同");
                    Require(Get<ScrollViewer>(page, "RecommendedScroll").Visibility == Visibility.Visible
                        && Get<ItemsRepeater>(page, "RecommendedRows").Visibility == Visibility.Visible
                        && page.TableScroll.Visibility == Visibility.Collapsed, "推荐页没有把网格换成三排货架");
                    Require(page.ViewModel.RecommendedShelves.All(shelf => shelf.Cards.Count == 1), "推荐页有一排没载入");
                    Require(page.ViewModel.RecommendedShelves[0].Cards[0].Subtitle == "S01E07 - 敏北", "继续观看那一排的集号行不对");
                    Require(page.ViewModel.RecommendedShelves[1].Cards[0].Subtitle == "2006 - 现在"
                        && page.ViewModel.RecommendedShelves[1].Cards[0].UnplayedVisibility == Visibility.Visible, "最新集那一排的年份或未看角标不对");
                    Require(page.ViewModel.RecommendedShelves[2].Cards[0].Subtitle == "S01E07 - 敏北", "最近发布的剧集那一排的集号行不对");
                    Require(fixture.Transport.Requests.Any(value => value.Contains("Items/Resume", StringComparison.Ordinal)
                        && value.Contains("ParentId=library-a", StringComparison.Ordinal)), "继续观看没有收在本库");
                    // 两帧：第一屏（继续观看 + 最新集）与滚到底（最近发布的剧集）。三排都在屏上，
                    // 靠这一对实图，不靠「集合里有三个元素」。
                    ResizeProbeClient(1100, 800);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync("library-tvshows-recommended-screen.png");
                    Get<ScrollViewer>(page, "RecommendedScroll").ChangeView(null, double.MaxValue, null, disableAnimation: true);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync("library-tvshows-recommended-bottom-screen.png");
                    Get<ScrollViewer>(page, "RecommendedScroll").ChangeView(null, 0, null, disableAnimation: true);
                    ResizeProbeClient(900, 700);
                    await LayoutAsync(page);
                }
                else
                {
                    Require(page.ViewModel.RecommendedShelves.Count == 0 && page.TableScroll.Visibility == Visibility.Visible
                        && Get<ScrollViewer>(page, "RecommendedScroll").Visibility == Visibility.Collapsed, "电影库的推荐不该变成三排");
                }
                await SelectAsync(LibrarySection.Folders);
                Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal)).Contains("Recursive=false", StringComparison.Ordinal), "文件夹被拍平");
                await SelectAsync(LibrarySection.Items);
                page.BuildSortMenu();
                var menu = Get<MenuFlyout>(page, "SortMenu");
                Require(menu.Items.OfType<RadioMenuFlyoutItem>().Count(value => value.IsChecked) == 2, "排序字段与方向没有分别显示选中状态");
                // 设置面板：网页端那颗齿轮里的四组东西。这里走的是用户点齿轮的同一条路 —— 打开面板、
                // 读面板上的控件、改一处、看请求与版面跟着变。网页端那颗 ⋯ 本身就是设置键，所以这一页
                // 只留一颗齿轮，不再有第二个「更多」。
                Require(page.GetType().GetField("LibraryMoreButton",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) is null,
                    "工具栏上还留着与设置重复的「更多」按钮");
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
                Require(Get<ItemsView>(page, "CardWall").Layout is StackLayout, "选了列表视图，网格没有换成一行一条");
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

                // 点面板外面那片空位也要能关 —— 用户点的是遮罩本身（铺满整层那颗按钮），不是 ✕。
                var closeByOutside = FrameworkElementAutomationPeer.CreatePeerForElement(page.SettingsScrim)
                    ?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                Require(closeByOutside is not null, "面板外面那片空位没有可点的自动化入口");
                closeByOutside!.Invoke();
                await SettleAsync();
                Require(page.SettingsOverlay.Visibility == Visibility.Collapsed, "点面板外面没有关掉设置");
                Require(page.ViewModel.View == before.View && page.ViewModel.CardSize == before.CardSize
                    && page.ViewModel.Fields.SequenceEqual(before.Fields), "设置面板没有把原来的那一套还回去");

                // 表格视图的横向滚动：勾满字段时一行比页面宽，从前是从右边裁掉。画布要按列宽变宽，
                // 列头与每一行在同一个画布里，多出来的部分由外面那层滚动器滚。
                page.ViewModel.ApplyDisplay(new LibraryDisplay
                {
                    View = LibraryView.Table,
                    CardSize = page.ViewModel.CardSize,
                    GroupIntoCollections = page.ViewModel.GroupIntoCollections,
                    Fields =
                    [
                        LibraryField.Title, LibraryField.Genres, LibraryField.Overview,
                        LibraryField.Director, LibraryField.Tagline, LibraryField.Studios
                    ]
                });
                await UntilAsync(() => page.ViewModel.View == LibraryView.Table && !page.ViewModel.Busy);
                await LayoutAsync(page);
                var rowWidth = page.ViewModel.Cards[0].Cells.Sum(cell => cell.Width) + 24;
                Require(page.TableScroll.HorizontalScrollMode == ScrollMode.Enabled, "表格视图没有打开横向滚动");
                Require(page.TableCanvas.ActualWidth >= rowWidth - 2,
                    $"表格画布没有按列宽变宽（画布 {page.TableCanvas.ActualWidth:0}，各列之和 {rowWidth:0}）");
                Require(page.TableCanvas.ActualWidth > page.TableScroll.ActualWidth + 1,
                    $"表格画布没有比可见区域宽（画布 {page.TableCanvas.ActualWidth:0}，可见 {page.TableScroll.ActualWidth:0}）");
                await SaveProbeScreenAsync($"library-{collection}-table-wide-screen.png");
                page.TableScroll.ChangeView(160, null, null, disableAnimation: true);
                await SettleAsync();
                await LayoutAsync(page);
                Require(page.TableScroll.HorizontalOffset > 1,
                    $"表格没有真的横向滚起来（偏移 {page.TableScroll.HorizontalOffset:0}）");

                // 换成海报（回到打开面板之前那一套），画布要交回页面宽度、横向滚动关掉 —— 卡片墙的格子
                // 按页面宽算，画布留着表格那一轮的宽度，格子就会按那个宽度铺。
                page.ViewModel.ApplyDisplay(before, remember: true);
                await UntilAsync(() => page.ViewModel.View == before.View && !page.ViewModel.Busy);
                await LayoutAsync(page);
                Require(page.TableScroll.HorizontalScrollMode == ScrollMode.Disabled
                    && double.IsNaN(page.TableCanvas.Width), "离开表格视图后画布还留着横向滚动那一套");

                // 文件夹页勾了「文件名」与「媒体容器」：前者从 Path 取、后者要 MediaSources，请求里就得真的
                // 带上它；别的页不该跟着多出这两段（那些页没有这两个开关，漏过去就是关不掉的字）。
                await SelectAsync(LibrarySection.Folders);
                var folders = page.ViewModel.Display;
                var folderRequests = fixture.Transport.Requests.Count;
                page.ViewModel.ApplyDisplay(new LibraryDisplay
                {
                    View = folders.View,
                    CardSize = folders.CardSize,
                    GroupIntoCollections = folders.GroupIntoCollections,
                    Fields = [.. folders.Fields, LibraryField.Filename, LibraryField.Container]
                });
                await UntilAsync(() => page.ViewModel.Fields.Contains(LibraryField.Filename) && !page.ViewModel.Busy);
                await SettleAsync();
                // 这一页每次重载都会先问一次总数（Limit=0，不带 Fields），所以看的是「这一轮有没有一条
                // 带了 MediaSources 的」，而不是最后一条。
                Require(fixture.Transport.Requests.Skip(folderRequests)
                    .Any(value => value.Contains("MediaSources", StringComparison.Ordinal)),
                    "勾了媒体容器，请求里没有要 MediaSources");

                var itemRequests = fixture.Transport.Requests.Count;
                await SelectAsync(LibrarySection.Items);
                await SettleAsync();
                Require(!page.ViewModel.Fields.Contains(LibraryField.Filename), "文件夹页的文件名漏到了节目页");
                Require(!fixture.Transport.Requests.Skip(itemRequests)
                    .Any(value => value.Contains("MediaSources", StringComparison.Ordinal)),
                    "节目页还在替文件夹页要 MediaSources");

                await SelectAsync(LibrarySection.Folders);
                page.ViewModel.ApplyDisplay(folders, remember: true);
                await UntilAsync(() => !page.ViewModel.Busy);
                if (collection == "tvshows")
                {
                    // 集页的字段表比别的页多两行：所属剧集与集号（网页端 Episode 那一支先 push 这两列）。
                    await SelectAsync(LibrarySection.Episodes);
                    var gear = Get<Button>(page, "LibrarySettingsButton");
                    ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(gear)!
                        .GetPattern(PatternInterface.Invoke)!).Invoke();
                    await SettleAsync();
                    Require(page.SettingsPane.FieldRows.Children.Count == 16,
                        $"集页的字段表不是十六项（{page.SettingsPane.FieldRows.Children.Count}）");
                    page.CloseSettings();
                    await SettleAsync();
                    await SelectAsync(LibrarySection.Items);
                }
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
