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
    /// <summary>2026-10-19 网页截图：电影库的影片、预告片、收藏共用这份十九条菜单。</summary>
    private const string MovieSortLabels =
        "IMDb 评分,分辨率,加入日期,发行日期,媒体容器,家长评分,导演,帧率,年份,影评人评分,播放日期,播放时长,播放次数,文件名,文件尺寸,标题,比特率,视频编解码器,随机";

    /// <summary>文件夹与混合内容「电影和节目」共用的二十五条菜单。</summary>
    private const string FolderSortLabels =
        "上次添加内容日期,专辑,专辑艺人,作曲家,分辨率,加入日期,发行日期,媒体容器,家长评分,导演,帧率,年份,影评人评分,播放日期,播放时长,播放次数,文件名,文件尺寸,标题,比特率,社区评分,编号,艺人,视频编解码器,随机";

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
            // 用户网页端这两页都是 0 项（没装 Trailers 插件、没有收藏）。探针照空态跑，
            // 才能验电影库「预告片」「收藏」的工具栏、排序、筛选与设置，以及没有可播行时收起播放键。
            if (Query(request, "IncludeItemTypes") == EmbyItemType.Trailer
                || Query(request, "Filters")?.Contains("IsFavorite", StringComparison.Ordinal) == true)
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            var path = request.RequestUri.AbsolutePath;
            var index = path.EndsWith("/Genres", StringComparison.Ordinal) || path.EndsWith("/Studios", StringComparison.Ordinal);
            // 继续观看那一条没有 IncludeItemTypes（它就是 Resume），推荐页那一排装的是单集。
            var resume = path.EndsWith("/Items/Resume", StringComparison.Ordinal);
            var type = index ? path.EndsWith("/Genres", StringComparison.Ordinal) ? "Genre" : "Studio"
                : resume ? EmbyItemType.Episode
                : (Query(request, "IncludeItemTypes") ?? EmbyItemType.Movie).Split(',')[0];
            var sample = new EmbyItem { Id = "library-sample-" + type, Name = index ? "科幻与动画" : "离线媒体库内容", Type = type };
            // 索引页的两类图各挂各的那一格：类型的四格封面是 Primary，播出平台的台标是 Thumb
            // （本机 4.10.1.0 实测 23 个有图的台全在 Thumb 里）。两格都挂上就分不出卡片到底要的哪一格了。
            if (type == EmbyItemType.Studio) sample.ImageTags["Thumb"] = "fixture";
            else sample.ImageTags["Primary"] = "fixture";
            if (type == EmbyItemType.Studio) sample.PrimaryImageAspectRatio = 16.0 / 9.0;
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
                sample.SeriesName = "离线剧集";
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
            // 三套分类：电影库没有「合集」，混合内容库没有推荐／预告／收藏。电视剧库放最后，
            // 末尾那段「迟到响应与载入失败」用的就是它的「集」页。
            foreach (var collection in new[] { "movies", "mixed", "tvshows" })
            {
                var libraryId = "library-" + collection;
                var request = new LibraryRequest { Services = fixture.Services, Title = "离线媒体库", ParentId = libraryId, CollectionType = collection };
                page?.Release();
                frame.Navigate(typeof(LibraryPage), request);
                page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
                Require(page.ViewModel.Cards.Count == 1, "媒体库首屏未载入");
                var tabs = Get<WrapRow>(page, "LibrarySections");
                var labels = string.Join(',', tabs.Children.OfType<ToggleButton>().Select(tab => tab.Content));
                var expectedSections = collection switch
                {
                    "movies" => "影片,推荐,预告片,类型,收藏,文件夹",
                    "mixed" => "电影和节目,类型,文件夹",
                    _ => "节目,推荐,预告,收藏,类型,播出平台,集,文件夹"
                };
                Require(labels == expectedSections, $"分类顺序与网页不同（{collection}：{labels}）");
                Require(Get<Button>(page, "PlayAllButton").Visibility == (collection == "tvshows" ? Visibility.Collapsed : Visibility.Visible), $"{collection} 的播放工具栏不对");
                Require(Get<TextBlock>(page, "LibraryItemCount").Text == "共 1 项", "工具栏总数未更新");
                // 两张网页截图：电影库的「影片」开在加入日期降序，混合内容库开在标题升序；
                // 电视剧库的「节目」也是标题升序。每个库用不同的 ParentId，记忆键互不相干。
                if (collection == "movies")
                    Require(page.ViewModel.SortKey == EmbySortBy.DateAdded && page.ViewModel.SortDescending
                        && page.ViewModel.SortLabel == "加入日期",
                        $"电影库「影片」的默认排序不是加入日期降序（{page.ViewModel.SortLabel}）");
                else
                    Require(page.ViewModel.SortKey == EmbySortBy.Name && !page.ViewModel.SortDescending
                        && page.ViewModel.SortLabel == "标题",
                        $"{collection} 的默认排序不是标题升序（{page.ViewModel.SortLabel}）");
                foreach (var width in new[] { 1100, 760, 520, 360 })
                {
                    ResizeProbeClient(width, 800);
                    await LayoutAsync(page);
                    var title = Get<TextBlock>(page, "LibraryTitle");
                    Require(title.Visibility == Visibility.Visible && title.Text == page.ViewModel.Heading,
                        $"左上角没有库名（{title.Text}）");
                    var titleBox = title.TransformToVisual(page).TransformBounds(new(0, 0, title.ActualWidth, title.ActualHeight));
                    Require(titleBox.Left >= -1 && titleBox.Left < page.ActualWidth / 2, "库名不在左上角");
                    var surface = Get<Border>(page, "LibrarySectionsSurface");
                    var box = surface.TransformToVisual(page).TransformBounds(new(0, 0, surface.ActualWidth, surface.ActualHeight));
                    Require(box.Left >= -1 && box.Right <= page.ActualWidth + 1, "分类栏被裁切");
                    Require(box.Top >= titleBox.Bottom - 1, "分类栏没有落到库名下方");
                    Require(Math.Abs(box.Left + box.Width / 2 - page.ActualWidth / 2) <= 2, "分类栏没有居中");
                    foreach (var line in tabs.Children.OfType<ToggleButton>().GroupBy(tab =>
                        Math.Round(tab.TransformToVisual(page).TransformPoint(new(0, 0)).Y)))
                    {
                        var bounds = line.Select(tab => tab.TransformToVisual(page).TransformBounds(new(0, 0, tab.ActualWidth, tab.ActualHeight))).ToArray();
                        Require(Math.Abs((bounds.Min(value => value.Left) + bounds.Max(value => value.Right)) / 2 - page.ActualWidth / 2) <= 2,
                            "分类栏换行之后该行没有居中");
                    }
                    foreach (var tab in tabs.Children.OfType<ToggleButton>())
                    {
                        var peer = new ToggleButtonAutomationPeer(tab);
                        Require(peer.GetAutomationId() == "LibrarySection" + tab.Tag && peer.GetName() == tab.Content.ToString(), "分类无法稳定定位或读出名称");
                    }
                    var toolbar = Get<Grid>(page, "ToolbarSurface");
                    var controls = toolbar.TransformToVisual(page).TransformBounds(new(0, 0, toolbar.ActualWidth, toolbar.ActualHeight));
                    Require(controls.Left >= -1 && controls.Right <= page.ActualWidth + 1 && controls.Top >= box.Bottom, "工具栏未在分类下方或被裁切");
                    Require(Math.Abs(controls.Left + controls.Width / 2 - page.ActualWidth / 2) <= 2, "工具栏没有居中");
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
                // 电影库的「预告片」与「收藏」在网页端是普通网格：有排序、筛选、设置，
                // 但空页时没有可播行，播放/随机播放收起。这三样与电影「影片」共用同一份菜单。
                async Task CheckMovieOptionPanelsAsync(string name, int filterRows, string filterLabels, int fieldRows)
                {
                    var entries = page.ViewModel.SortEntries().Select(entry => entry.Option.Label).ToArray();
                    Require(string.Join(',', entries) == MovieSortLabels,
                        $"这一页的排序菜单与电影「影片」不同：{string.Join(',', entries)}");
                    Require(Get<Button>(page, "SortButton").Visibility == Visibility.Visible
                        && Get<Button>(page, "FilterButton").Visibility == Visibility.Visible
                        && Get<Button>(page, "LibrarySettingsButton").Visibility == Visibility.Visible,
                        "这一页缺少排序、筛选或设置");
                    page.ShowFilterPanel();
                    await SettleAsync();
                    Require(page.FilterOverlay.Visibility == Visibility.Visible, "这一页的筛选面板没有打开");
                    Require(page.FilterPane.RowLabels.Count == filterRows
                        && string.Join(',', page.FilterPane.RowLabels) == filterLabels,
                        $"这一页的筛选面板与网页端不同：{string.Join(',', page.FilterPane.RowLabels)}");
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"library-movies-{name}-filter-screen.png");
                    page.CloseFilter();
                    await SettleAsync();
                    var gear = Get<Button>(page, "LibrarySettingsButton");
                    var invoke = FrameworkElementAutomationPeer.CreatePeerForElement(gear)
                        ?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                    Require(invoke is not null, "这一页的设置入口没有可调用的自动化入口");
                    invoke!.Invoke();
                    await SettleAsync();
                    Require(page.SettingsOverlay.Visibility == Visibility.Visible, "这一页的设置面板没有打开");
                    Require(page.SettingsPane.FieldRows.Children.Count == fieldRows,
                        $"这一页的设置字段数不对（{page.SettingsPane.FieldRows.Children.Count}，期望 {fieldRows}）");
                    var labels = page.SettingsPane.FieldRows.Children.OfType<Grid>()
                        .Select(row => row.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "").ToArray();
                    Require(labels.Contains("IMDb 评分"), $"电影设置面板的评分行不对：{string.Join(',', labels)}");
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"library-movies-{name}-settings-screen.png");
                    page.CloseSettings();
                    await SettleAsync();
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
                await CheckIndexSectionAsync(page, request, fixture, "genres", EmbyItemType.Genre, collection);
                page.ViewModel.Open(page.ViewModel.Cards[0]);
                await UntilAsync(() => request.BrowseGroup is not null && page.ViewModel.IsReady && !page.ViewModel.Busy);
                Require(Get<Button>(page, "FilterButton").Visibility == Visibility.Visible
                    && fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal)).Contains("Genres=", StringComparison.Ordinal), "类型下钻没有真正发出作用域查询");
                // 下钻之后那一页装的是剧集，不是类型名：卡片要退回普通海报（方卡与「只有标题」的字段表都属于
                // 索引页）。字段表那一头问的是「这一页认不认那十四项」而不是「画了几项」—— 这一页生效的仍是
                // 用户勾过的字段，而索引页上刚默认只勾了标题。
                Require(page.ViewModel.IndexKind == LibraryIndexKind.None
                    && LibraryDisplayCatalog.FieldsFor(page.ViewModel.Section, LibraryView.Poster,
                        page.ViewModel.IndexKind, page.ViewModel.CollectionType).Count
                        == (collection == "tvshows" ? 14 : 22), "类型下钻之后还按索引页画");
                var drilledCard = page.ViewModel.Cards[0];
                Require(Math.Abs(drilledCard.PosterHeight - Math.Round(drilledCard.CardWidth * 1.5)) <= 1,
                    $"类型下钻之后的卡片不是 2:3 的海报（宽 {drilledCard.CardWidth}，高 {drilledCard.PosterHeight}）");
                await page.ViewModel.LeaveBrowseGroupAsync();
                await SelectAsync(LibrarySection.Items);
                Require(page.ViewModel.SortKey == EmbySortBy.DateAdded && page.ViewModel.Filters.Genres.Contains("动画"), "分类切回丢失原排序筛选");

                if (collection != "mixed")
                {
                    await SelectAsync(LibrarySection.Favorites);
                    Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal))
                        .Contains("IsFavorite", StringComparison.Ordinal), "库内收藏未带固定条件");
                    if (collection == "tvshows")
                    {
                        // 电视剧收藏是两排货架，网页端没有工具栏；电影收藏是普通网格，要留着。
                        Require(Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Collapsed,
                            "电视剧收藏货架仍显示工具栏");
                    }
                    else
                    {
                        Require(Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Visible, "电影收藏页没有工具栏");
                        Require(page.ViewModel.SortLabel == "加入日期" && page.ViewModel.SortDescending,
                            $"电影收藏默认排序不是加入日期降序（{page.ViewModel.SortLabel}）");
                        Require(Get<Button>(page, "PlayAllButton").Visibility == Visibility.Collapsed,
                            "电影收藏空页仍显示播放");
                        await CheckMovieOptionPanelsAsync("favorites", 27,
                            "播放状态,收藏,重制,类型,家长评分,工作室,年份,媒体容器,视频编解码器,视频类型,分辨率,3D,音频编解码器,音频布局,音频语言,字幕,字幕编解码器,字幕语言,预告片,附加篇,主题曲,主题视频,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id",
                            22);
                    }
                }

                if (collection == "movies")
                {
                    await SelectAsync(LibrarySection.Trailers);
                    Require(page.ViewModel.Cards.Count == 0, "预告片空页仍有卡片");
                    Require(Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Visible, "预告片页没有工具栏");
                    Require(Get<Button>(page, "PlayAllButton").Visibility == Visibility.Collapsed,
                        "预告片空页仍显示播放");
                    Require(page.ViewModel.SortLabel == "标题" && !page.ViewModel.SortDescending,
                        $"预告片默认排序不是标题升序（{page.ViewModel.SortLabel}）");
                    await CheckMovieOptionPanelsAsync("trailers", 10,
                        "播放状态,收藏,分辨率,3D,字幕,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id", 22);
                }
                else if (collection == "tvshows")
                {
                    await SelectAsync(LibrarySection.Episodes);
                    Require(page.ViewModel.Cards[0].Item.Type == EmbyItemType.Episode, "集查询没有切换类型");
                    await SelectAsync(LibrarySection.Studios);
                    await CheckIndexSectionAsync(page, request, fixture, "studios", EmbyItemType.Studio, collection);
                    page.ViewModel.Open(page.ViewModel.Cards[0]);
                    await UntilAsync(() => request.BrowseGroup is not null && page.ViewModel.IsReady && !page.ViewModel.Busy);
                    Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal)).Contains("Studios=", StringComparison.Ordinal), "播出平台没有真正下钻");
                    await page.ViewModel.LeaveBrowseGroupAsync();
                    await UntilAsync(() => !page.ViewModel.Busy);
                }
                else
                {
                    // 混合内容库只有三个分区：推荐、预告、收藏、集与播出平台一个都不该冒出来。
                    Require(page.ViewModel.BrowseSections.All(entry => entry.Section is
                        LibrarySection.Items or LibrarySection.Genres or LibrarySection.Folders),
                        "混合内容库混进了别的库的分区");
                }

                if (collection != "mixed")
                {
                    await SelectAsync(LibrarySection.Recommended);
                    Require(Get<Button>(page, "SortButton").Visibility == Visibility.Collapsed
                        && Get<Button>(page, "FilterButton").Visibility == Visibility.Collapsed, "推荐还显示不生效的排序筛选");
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
                            && value.Contains($"ParentId={libraryId}", StringComparison.Ordinal)), "继续观看没有收在本库");
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
                }
                await SelectAsync(LibrarySection.Folders);
                Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal)).Contains("Recursive=false", StringComparison.Ordinal), "文件夹被拍平");
                Require(Get<Button>(page, "FilterButton").Visibility == Visibility.Collapsed, "文件夹页还有筛选键");
                Require(page.ViewModel.SortLabel == "文件名" && !page.ViewModel.SortDescending,
                    $"文件夹默认排序不是文件名升序（{page.ViewModel.SortLabel}）");
                var folderEntries = page.ViewModel.SortEntries().Select(entry => entry.Option.Label).ToArray();
                Require(string.Join(',', folderEntries) == FolderSortLabels,
                    $"文件夹排序菜单与网页端不同：{string.Join(',', folderEntries)}");
                var folderGear = Get<Button>(page, "LibrarySettingsButton");
                var folderInvoke = FrameworkElementAutomationPeer.CreatePeerForElement(folderGear)
                    ?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                Require(folderInvoke is not null, "文件夹设置入口没有可调用的自动化入口");
                folderInvoke!.Invoke();
                await SettleAsync();
                Require(page.SettingsPane.FieldRows.Children.Count == 22,
                    $"文件夹设置字段数不对（{page.SettingsPane.FieldRows.Children.Count}）");
                var folderLabels = page.SettingsPane.FieldRows.Children.OfType<Grid>()
                    .Select(row => row.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "").ToArray();
                Require(folderLabels.Contains("社区评分"), $"文件夹设置的评分行不对：{string.Join(',', folderLabels)}");
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"library-{collection}-folders-settings-screen.png");
                page.CloseSettings();
                await SettleAsync();
                await SelectAsync(LibrarySection.Items);
                var sortLabels = page.BuildSortMenu();
                Require(page.SortList.Items.Count == sortLabels.Count && sortLabels.Count > 0, "排序菜单没有按条目填满");
                Require(string.Join(',', sortLabels) == string.Join(',', sortLabels.OrderBy(value => value, StringComparer.Ordinal)),
                    "排序菜单没有按字段名排序");
                var ticked = page.ViewModel.SortEntries().Where(entry => entry.IsChecked).ToList();
                Require(ticked.Count == 1 && ticked[0].Option.ValueOf(page.ViewModel.SortPreset) == page.ViewModel.SortKey,
                    "排序菜单里选中的不是当前那一项，或者不止一项");
                Require(ticked[0].ArrowVisibility == Visibility.Visible
                    && page.ViewModel.SortEntries().Where(entry => !entry.IsChecked).All(entry => entry.ArrowVisibility == Visibility.Collapsed),
                    "方向箭头没有只出现在选中那一行");
                if (collection == "tvshows")
                {
                    // 网页端「电视节目-节目」那一列：十三条，按字段名排。缺一条、多一条或换了顺序，
                    // 这一页就与截图不是同一个菜单。
                    Require(string.Join(',', sortLabels)
                        == "IMDb 评分,上次发布集日期,上次添加集日期,加入日期,发行日期,家长评分,导演,年份,影评人评分,播放日期,播放时长,标题,随机",
                        "电视剧库的排序菜单与网页端不同");
                }
                else if (collection == "movies")
                {
                    Require(string.Join(',', sortLabels) == MovieSortLabels,
                        $"电影库的排序菜单与网页端不同：{string.Join(',', sortLabels)}");
                }
                else
                {
                    // 混合内容库的「电影和节目」给的是文件夹那整份菜单（专辑、艺人、编号都在）。
                    Require(string.Join(',', sortLabels) == FolderSortLabels,
                        $"混合内容库的排序菜单与网页端不同：{string.Join(',', sortLabels)}");
                }
                await SaveProbeScreenAsync($"library-{collection}-sort-screen.png");

                // 筛选面板：网页端 modules/filtermenu 那一块。这里走用户点那颗筛选键的同一条路 ——
                // 打开面板、读它的行、改一处、看请求跟着变。
                page.CloseFilter();
                var filterButton = Get<Button>(page, "FilterButton");
                var openFilter = FrameworkElementAutomationPeer.CreatePeerForElement(filterButton)
                    ?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                Require(openFilter is not null, "筛选键没有可调用的自动化入口");
                openFilter!.Invoke();
                await SettleAsync();
                Require(page.FilterOverlay.Visibility == Visibility.Visible, "筛选面板没有打开");
                Require(page.FilterPane.EveryTemplateResolves, "筛选面板有行没有可用的模板");
                await CheckLibraryPanelSizingAsync(page, page.FilterPane, $"library-{collection}-filter");

                var rows = page.FilterPane.RowLabels;
                if (collection == "tvshows")
                {
                    // 截图那一页：八行无标题的，功能四条，元数据两条，缺少元数据三条 —— 一行不多一行不少。
                    // 媒体信息整组不出现：剧集本身没有流，服务器对它答不了画质、3D 与字幕。
                    Require(string.Join(',', rows)
                        == "播放状态,收藏,重制,类型,家长评分,工作室,年份,状态,预告片,附加篇,主题曲,主题视频,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id",
                        $"电视剧库的筛选面板与网页端不同：{string.Join(',', rows)}");
                }
                else if (collection == "mixed")
                {
                    // 2026-10-19 网页截图：混合内容没有「重制」「标签」「画质」与「状态」，
                    // 媒体信息只有媒体容器、分辨率、3D、字幕这几项，后面接着功能、元数据与缺少元数据。
                    Require(string.Join(',', rows)
                        == "播放状态,收藏,类型,家长评分,工作室,年份,媒体容器,分辨率,3D,字幕,预告片,附加篇,主题曲,主题视频,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id",
                        $"混合内容库的筛选面板不对：{string.Join(',', rows)}");
                }
                else
                {
                    // 2026-10-19 电影网页截图：没有「标签」「画质」「状态」，媒体信息是整组十一行。
                    Require(string.Join(',', rows)
                        == "播放状态,收藏,重制,类型,家长评分,工作室,年份,媒体容器,视频编解码器,视频类型,分辨率,3D,音频编解码器,音频布局,音频语言,字幕,字幕编解码器,字幕语言,预告片,附加篇,主题曲,主题视频,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id",
                        $"电影库的筛选面板不对：{string.Join(',', rows)}");
                }

                // 这一趟之前已经开着「类型：动画」，所以比的是「多了两条」，不是「等于两条」。
                var filterCountBefore = page.ViewModel.FilterCount;
                var playState = page.FilterPane.ViewModel.Rows.OfType<FilterSelectRow>().First(row => row.Label == "播放状态");
                // 网页端只在 Movie／Episode／Trailer／Audio／MusicVideo／Video 的 visibleSettings 里放
                // IsResumable（listcontroller.js 的 getVisibleFilters），剧集没有断点，所以「节目」这一页
                // 的下拉里没有「可继续播放」（EmbyFilterBy.ChoicesFor 的 NotFor 就是这一条）。
                Require(string.Join(',', playState.Choices)
                    == (collection == "tvshows" ? "全部,已播放,未播放" : "全部,已播放,未播放,可继续播放"),
                    $"播放状态的选项不对：{string.Join(',', playState.Choices)}");
                playState.SelectedIndex = 2;
                await UntilAsync(() => !page.ViewModel.Busy);
                Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal))
                    .Contains("IsUnplayed", StringComparison.Ordinal), "播放状态没有真的发出去");

                var favorite = page.FilterPane.ViewModel.Rows.OfType<FilterSwitchRow>().First(row => row.Label == "收藏");
                favorite.IsOn = true;
                await UntilAsync(() => !page.ViewModel.Busy);
                Require(fixture.Transport.Requests.Last(value => value.Contains("ParentId", StringComparison.Ordinal))
                    .Contains("IsFavorite", StringComparison.Ordinal), "收藏开关没有真的发出去");
                Require(page.ViewModel.FilterCount == filterCountBefore + 2 && page.FilterPane.ViewModel.Rows.Count == rows.Count,
                    $"改了筛选之后徽标或面板行数不对（徽标 {filterCountBefore} → {page.ViewModel.FilterCount}，行 {page.FilterPane.ViewModel.Rows.Count}/{rows.Count}）");
                await SaveProbeScreenAsync($"library-{collection}-filter-screen.png");

                // 清空会整份重建，旧行对象不再代表屏上那一行 —— 所以读的是重建后的那一批。
                page.FilterPane.ViewModel.ClearCommand.Execute(null);
                await UntilAsync(() => !page.ViewModel.Busy);
                Require(page.ViewModel.Filters.IsBlank
                    && page.FilterPane.ViewModel.Rows.OfType<FilterSelectRow>().First(row => row.Label == "播放状态").SelectedIndex == 0
                    && !page.FilterPane.ViewModel.Rows.OfType<FilterSwitchRow>().First(row => row.Label == "收藏").IsOn,
                    "清空筛选之后控件没有回到全部");

                page.CloseFilter();
                await SettleAsync();
                Require(page.FilterOverlay.Visibility == Visibility.Collapsed, "筛选面板关不上");
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
                await CheckLibraryPanelSizingAsync(page, page.SettingsPane, $"library-{collection}-settings");

                var pane = page.SettingsPane;
                var expectedFieldRows = collection == "tvshows" ? 14 : 22;
                Require(pane.ViewCombo.Items.Count == 7 && pane.SizeCombo.Items.Count == 8
                    && pane.FieldRows.Children.Count == expectedFieldRows,
                    $"设置面板的字段表不对（{pane.FieldRows.Children.Count} 行，期望 {expectedFieldRows}）");
                var fieldLabels = pane.FieldRows.Children.OfType<Grid>()
                    .Select(row => row.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "").ToArray();
                Require(fieldLabels.Contains(collection == "mixed" ? "社区评分" : "IMDb 评分"),
                    $"设置面板的评分行不对：{string.Join(',', fieldLabels)}");
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
                    // 卡片视图与表格视图的可用字段分别核对。
                    await SelectAsync(LibrarySection.Episodes);
                    var gear = Get<Button>(page, "LibrarySettingsButton");
                    ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(gear)!
                        .GetPattern(PatternInterface.Invoke)!).Invoke();
                    await SettleAsync();
                    Require(page.SettingsPane.FieldRows.Children.Count == 21,
                        $"集页的字段表不是二十一项（{page.SettingsPane.FieldRows.Children.Count}）");
                    Require(page.ViewModel.View == LibraryView.Poster && !page.ViewModel.GroupIntoCollections
                        && page.ViewModel.Fields.SequenceEqual(new[] { LibraryField.Title }), "集页默认显示设置不匹配参考图");
                    Require(page.ViewModel.SortKey == LibrarySortCatalog.EpisodeOrder && page.ViewModel.SortLabel == "剧集", "集页默认排序不对");
                    Require(page.ViewModel.Cards[0].Title == "离线剧集" && page.ViewModel.Cards[0].Subtitle == "S1:E7 - 敏北", "集页两行标题不对");
                    page.SettingsPane.SetField(LibraryField.Title, false);
                    await UntilAsync(() => !page.ViewModel.Busy);
                    Require(page.ViewModel.Cards[0].Title.Length == 0 && page.ViewModel.Cards[0].Subtitle.Length == 0,
                        "集页关闭标题后仍显示旧文案");
                    page.SettingsPane.SetField(LibraryField.Title, true);
                    await UntilAsync(() => !page.ViewModel.Busy);
                    page.SettingsPane.SetView(LibraryView.Table);
                    await UntilAsync(() => !page.ViewModel.Busy);
                    Require(page.SettingsPane.FieldRows.Children.Count == 23, "集页表格没有剧名和集号两列");
                    page.SettingsPane.SetView(LibraryView.Poster);
                    await UntilAsync(() => !page.ViewModel.Busy);
                    foreach (var width in new[] { 1100, 760, 360 })
                    {
                        ResizeProbeClient(width, 800);
                        await LayoutAsync(page);
                        await SaveProbeScreenAsync($"library-tvshows-episodes-settings-{width}-screen.png");
                    }
                    page.CloseSettings();
                    foreach (var width in new[] { 1100, 760, 360 })
                    {
                        ResizeProbeClient(width, 800);
                        await LayoutAsync(page);
                        await SaveProbeScreenAsync($"library-tvshows-episodes-{width}-screen.png");
                    }

                    // 用户令 2026-10-18「封面随窗口大小变化，互相之间不要留太大的空隙」。量的是活的格子与活的
                    // 卡片：卡片比格子窄出来的那一截，就是两张封面之间多出来的那条缝。窗口宽了 180 像素，封面
                    // 要跟着变大 —— 从前它钉死在 300，多出来的宽度全摊成了缝（1280 的窗口上两张封面之间空
                    // 114 像素，比封面自己还显眼）。
                    //
                    // 假库里这一类只有一张（下面几条都按「第一张」断言），所以这里问不出「一行排满」——
                    // 「格子是几列、铺没铺满」问的是版面自己的数，不是这一行里有几张卡。
                    ResizeProbeClient(1100, 800);
                    await LayoutAsync(page);
                    // 窗口这次是被三档索引页那一段放小又放回来的：滚动器的尺寸变化、视图模型重算格宽、版面重排
                    // 三件事不在同一拍上，直接量会量到「视图模型已经算出 342、版面还按上一轮的 422 排」那一帧。
                    // 等活的版面把格宽摊成视图模型算出来的那个数再量 —— 这一条问的是最终版面，不是中间那一帧。
                    // 等不到就是真没重排：下面那两条断言会把两个数一起报出来。
                    try
                    {
                        await UntilAsync(() =>
                        {
                            page.UpdateLayout();
                            var live = page.GridPaint();
                            return live.Cell > 0 && Math.Abs(live.Cell - page.ViewModel.CellWidth) <= 1;
                        });
                    }
                    catch (TimeoutException)
                    {
                    }
                    var at1100 = page.GridPaint();
                    var sizing = page.Sizing;
                    var liveLayout = (UniformGridLayout)Get<ItemsView>(page, "CardWall").Layout;
                    var detail = $"卡片 {at1100.Card:0}、格子 {at1100.Cell:0}、墙 {at1100.Wall:0}、视口 {at1100.Viewport:0}"
                        + $"、版面上的格宽 {sizing.CellWidth:0}、视图模型的格宽 {page.ViewModel.CellWidth:0}"
                        + $"、卡片自己的宽度 {page.ViewModel.Cards[0].CardWidth:0}"
                        + $"、分区 {page.ViewModel.Section}、索引 {page.ViewModel.IndexKind}、视图 {page.ViewModel.View}"
                        + $"、最小宽 {page.ViewModel.CardMinWidth}、行高 {page.ViewModel.RowHeight:0}"
                        + $"、活版面的最小宽 {liveLayout.MinItemWidth:0}、活版面的最小高 {liveLayout.MinItemHeight:0}"
                        + $"、卡片类型 {page.ViewModel.Cards[0].Item.Type}";
                    Require(at1100.Cell > 0 && Math.Abs(at1100.Card - at1100.Cell) <= 1,
                        $"卡片没填满格子，两张封面之间多出一条缝（{detail}）");
                    Require(Math.Abs(at1100.Cell - sizing.CellWidth) <= 1,
                        $"格宽换了，版面还按 {sizing.CellWidth:0} 排 —— 列数没跟着重算（{detail}）");

                    ResizeProbeClient(1280, 800);
                    await LayoutAsync(page);
                    await UntilAsync(() => page.ViewModel.CardWidth > at1100.Card + 20);
                    var at1280 = page.GridPaint();
                    Require(at1280.Card > at1100.Card + 20 && Math.Abs(at1280.Card - at1280.Cell) <= 1,
                        $"窗口从 1100 宽到 1280 宽，封面只从 {at1100.Card:0} 变成 {at1280.Card:0}（格子 {at1280.Cell:0}）");
                    ResizeProbeClient(1100, 800);
                    await LayoutAsync(page);

                    page.ShowFilterPanel();
                    await LayoutAsync(page);
                    Require(string.Join(',', page.FilterPane.RowLabels) ==
                        "播放状态,收藏,重制,类型,工作室,年份,媒体容器,视频编解码器,视频类型,分辨率,3D,音频编解码器,音频布局,音频语言,字幕,字幕编解码器,字幕语言,预告片,附加篇,主题曲,主题视频,已锁定,概要,IMDb Id,Tvdb Id,MovieDb Id", "集页筛选行不对");
                    ResizeProbeClient(760, 900);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync("library-tvshows-episodes-filter-screen.png");
                    var filterScroll = Get<ScrollViewer>(page.FilterPane, "FilterScroll");
                    filterScroll.ChangeView(null, double.MaxValue, null, disableAnimation: true);
                    await LayoutAsync(page);
                    Require(filterScroll.VerticalOffset > 0, "集页筛选面板没有滚动到下半部");
                    await SaveProbeScreenAsync("library-tvshows-episodes-filter-bottom-screen.png");
                    page.CloseFilter();
                    page.ShowSortMenu();
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync("library-tvshows-episodes-sort-screen.png");
                    Get<Flyout>(page, "SortMenu").Hide();
                    await SelectAsync(LibrarySection.Folders);
                    Require(page.ViewModel.SortLabel == "文件名" && page.ViewModel.Cards[0].FolderFrame, "文件夹默认排序或外框不对");
                    ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(gear)!
                        .GetPattern(PatternInterface.Invoke)!).Invoke();
                    await LayoutAsync(page);
                    Require(page.SettingsPane.FieldRows.Children.Count == 22, "文件夹设置字段不对");
                    await SaveProbeScreenAsync("library-tvshows-folders-settings-screen.png");
                    page.CloseSettings();
                    foreach (var width in new[] { 1100, 760, 360 })
                    {
                        ResizeProbeClient(width, 800);
                        await LayoutAsync(page);
                        await SaveProbeScreenAsync($"library-tvshows-folders-{width}-screen.png");
                    }
                    ResizeProbeClient(760, 900);
                    await LayoutAsync(page);
                    page.ShowSortMenu();
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync("library-tvshows-folders-sort-screen.png");
                    Get<Flyout>(page, "SortMenu").Hide();
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

    /// <summary>
    /// 索引页（类型、播出平台）那一段：2026-10-18 用户令「复刻网页端电视节目的播出平台和类型两个分区」。
    /// <para>
    /// 两页从前是一块只有名字的圆角块 —— 没有图、写死 170×72、跟「图像大小」和窗口宽度都没关系。这一条
    /// 问的是改完之后那几件事：是否有真图、形状对不对（类型 1:1、播出平台 16:9）、名字是不是居中且只有一行、
    /// 设置面板是不是只剩「标题」并且收起了「分组」、以及 1100／760／360 三档下卡片没有被窗口裁掉。
    /// </para>
    /// <para>
    /// 假传输给每一条都挂了 <c>Primary</c> 标签（见 <see cref="LibraryBrowseAsync"/> 里那个 fixture），
    /// 类型与播出平台都取得到图，所以这里量得到「图真的画上去了」。
    /// </para>
    /// </summary>
    private async Task CheckIndexSectionAsync(LibraryPage page, LibraryRequest request, Fixture fixture,
        string name, string itemType, string collection)
    {
        var indexKind = itemType == EmbyItemType.Genre ? LibraryIndexKind.Genre : LibraryIndexKind.Studio;
        Require(page.ViewModel.IndexKind == indexKind, $"「{name}」这一页没有认出自己是索引页");

        // 卡片上只有图和一个居中的名字：没有第二行、没有角标、没有播放键。
        var card = page.ViewModel.Cards[0];
        Require(card.CardWidth == page.ViewModel.CellWidth,
            $"索引卡的宽度没有跟着格宽走（卡 {card.CardWidth}，格 {page.ViewModel.CellWidth}）");
        Require(card.Subtitle.Length == 0 && card.Title == card.Item.Name,
            $"索引卡底下不该有第二行字（标题「{card.Title}」、副标题「{card.Subtitle}」）");
        Require(card.TitleAlignment == TextAlignment.Center, "索引卡的名字没有居中");
        Require(card.PlayVisibility == Visibility.Collapsed && card.UserStateVisibility == Visibility.Collapsed,
            "索引卡上出现了播放键或已观看/收藏按钮");
        var expectedHeight = Math.Round(card.CardWidth * (indexKind == LibraryIndexKind.Genre ? 1 : 9.0 / 16.0));
        Require(Math.Abs(card.PosterHeight - expectedHeight) <= 1,
            $"索引卡的形状不对（{indexKind}：宽 {card.CardWidth}，高 {card.PosterHeight}，期望 {expectedHeight}）");
        Require(card.PosterStretch == (indexKind == LibraryIndexKind.Studio
                ? Microsoft.UI.Xaml.Media.Stretch.Uniform : Microsoft.UI.Xaml.Media.Stretch.UniformToFill),
            "台标没有留边（或者四格封面被留了边）");

        // 请求真的问到了图，而且问的是这一页该问的那一格：类型的四格封面是 Primary，播出平台的台标是
        // Thumb（假数据也照这个分：见 LibraryBrowseAsync 里那个 fixture，两类各只挂一格）。
        await UntilAsync(() => card.Poster is not null);
        Require(card.Poster is not null, $"「{name}」的索引卡没有取到图");
        var wantedImageType = indexKind == LibraryIndexKind.Genre ? "Primary" : "Thumb";
        Require(fixture.Transport.Requests.Any(value => value.Contains($"/Images/{wantedImageType}", StringComparison.Ordinal)),
            $"「{name}」的索引卡没有按 {wantedImageType} 取图");

        // 「显示字段」只剩标题，「分组」整组收起；视图与图像大小两行还在（索引页认这两样）。
        var gear = Get<Button>(page, "LibrarySettingsButton");
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(gear)!
            .GetPattern(PatternInterface.Invoke)!).Invoke();
        await SettleAsync();
        Require(page.SettingsPane.FieldRows.Children.Count == 1, $"索引页的字段表不止一项（{page.SettingsPane.FieldRows.Children.Count}）");
        Require(page.SettingsPane.GroupSection.Visibility == Visibility.Collapsed, "索引页还留着「将项目分组到合集中」");
        Require(page.SettingsPane.SizeSection.Visibility == Visibility.Visible, "索引页把「图像大小」也收起来了");

        // 图像大小换一档：索引卡要跟着变大（从前这两页完全不认这个设置）。
        var defaultWidth = page.ViewModel.CellWidth;
        page.SettingsPane.SetCardSize(LibraryCardSize.ExtraLarge);
        await UntilAsync(() => page.ViewModel.CardSize == LibraryCardSize.ExtraLarge && !page.ViewModel.Busy);
        Require(page.ViewModel.CellWidth > defaultWidth,
            $"索引页换了图像大小，格宽没变（{defaultWidth:0} → {page.ViewModel.CellWidth:0}）");
        page.SettingsPane.SetCardSize(LibraryCardSize.Default);
        await UntilAsync(() => !page.ViewModel.Busy);
        page.CloseSettings();
        await SettleAsync();

        // 三档窗口：卡片不越界、列数随宽度变。
        foreach (var width in new[] { 1100, 760, 360 })
        {
            ResizeProbeClient(width, 800);
            await LayoutAsync(page);
            await UntilAsync(() => !page.ViewModel.Busy);
            var paint = page.GridPaint();
            Require(paint.Card > 0 && paint.Card <= page.ActualWidth + 1,
                $"「{name}」在 {width} 宽的窗口里卡片被裁掉了（卡 {paint.Card:0}，页 {page.ActualWidth:0}）");
            Require(Math.Abs(paint.Card - paint.Cell) <= 1,
                $"「{name}」在 {width} 宽时卡片没填满格子（卡 {paint.Card:0}，格 {paint.Cell:0}）");
            await SaveProbeScreenAsync($"library-{collection}-{name}-{width}-screen.png");
        }

        ResizeProbeClient(900, 700);
        await LayoutAsync(page);
        await UntilAsync(() => !page.ViewModel.Busy);
    }

    private async Task CheckLibraryPanelSizingAsync(LibraryPage page, FrameworkElement pane, string name)
    {
        double previousWidth = 0, previousHeight = 0;
        foreach (var size in new[] { (Width: 360, Height: 500), (Width: 760, Height: 640), (Width: 1100, Height: 900), (Width: 1600, Height: 1040) })
        {
            ResizeProbeClient(size.Width, size.Height);
            await LayoutAsync(page);
            var box = pane.TransformToVisual(page).TransformBounds(new(0, 0, pane.ActualWidth, pane.ActualHeight));
            var root = Get<Grid>(page, "PageRoot");
            var expected = Momoka.Infrastructure.LibraryPanelSize.For(root.ActualWidth, root.ActualHeight);
            Require(Math.Abs(pane.ActualWidth - expected.Width) <= 2 && Math.Abs(pane.ActualHeight - expected.Height) <= 2,
                $"弹窗尺寸没有跟窗口走：{box}，期望 {expected}");
            Require(box.Left >= -1 && box.Top >= -1 && box.Right <= page.ActualWidth + 1 && box.Bottom <= page.ActualHeight + 1,
                $"弹窗被主窗口裁切：{box}");
            Require(Math.Abs(box.Left + box.Width / 2 - page.ActualWidth / 2) <= 2
                && Math.Abs(box.Top + box.Height / 2 - page.ActualHeight / 2) <= 2, "弹窗没有居中");
            Require(pane.ActualWidth > previousWidth && pane.ActualHeight > previousHeight, "窗口放大后弹窗没有扩大");
            previousWidth = pane.ActualWidth;
            previousHeight = pane.ActualHeight;
            var scroll = Descendants(pane).OfType<ScrollViewer>().First(value => value.ActualHeight > 0 && value.HorizontalScrollMode == ScrollMode.Disabled);
            scroll.ChangeView(null, double.MaxValue, null, disableAnimation: true);
            await LayoutAsync(page);
            Require(scroll.ScrollableHeight == 0 || scroll.VerticalOffset > 0, "弹窗正文不能滚到底");
            scroll.ChangeView(null, 0, null, disableAnimation: true);
            await LayoutAsync(page);
            await SaveProbeScreenAsync($"{name}-{size.Width}x{size.Height}-screen.png");
        }
        ResizeProbeClient(900, 700);
        await LayoutAsync(page);
    }
}
