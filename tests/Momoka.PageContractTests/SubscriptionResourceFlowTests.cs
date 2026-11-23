using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 订阅详情资源搜索的新入口（用户令 2026-11-22）：站点表留在订阅详情页，确认后跳到搜索页的 MoviePilot 资源／
/// MoviePilot 字幕来源；详情页不再铺资源结果。真正的像素、焦点和指针输入由运行验收覆盖，这里只钉生产接线与 XAML。
/// </summary>
internal static class SubscriptionResourceFlowTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("订阅详情：站点表挂在本页，点搜索不再铺页内资源结果", () =>
        {
            var pageXaml = Load("Views/MoviePilotSubscriptionPage.xaml");
            var picker = pageXaml.Descendants().Single(element =>
                element.Attribute(X + "Name")?.Value == "SearchSitePicker");
            Assert.Equal("MoviePilotSitePicker", picker.Name.LocalName);
            Assert.Equal("Collapsed", picker.Attribute("Visibility")?.Value);
            Assert.False(pageXaml.Descendants().Any(element => element.Name.LocalName == "MoviePilotResourcePanel"),
                "订阅详情页不再挂资源结果面板");

            var page = Source("Views/MoviePilotSubscriptionPage.xaml.cs");
            Assert.Contains("SearchSitePicker.Attach(SearchSites);", page);
            Assert.Contains("SearchSitePicker.SearchOnConfirm = true;", page);
            Assert.Contains("private async Task SearchFromDetailAsync(", page);
            Assert.Contains("OpenDetailSearch: { } open", page);
            Assert.Contains("SearchSites.Attach(request.Service);", page);
            Assert.Contains("await SearchSitePicker.OpenAsync()", page);
            Assert.Contains("ContentDialogResult.Primary", page);
            Assert.Contains("open(new(media, options, SearchSites.Selection))", page);
            Assert.DoesNotContain("PrepareDetailSearchAsync", page);
            Assert.DoesNotContain("SearchSites.Sites.Count > 0 && await SearchSitePicker.OpenAsync()", page);
            Assert.DoesNotContain("MediaResources", page);
            Assert.DoesNotContain("IShellBackHandler", page);
        });

        Test("订阅详情：标题、IMDb、字幕共享影片身份与选站，保留实际季集", () =>
        {
            var page = Source("Views/MoviePilotSubscriptionPage.xaml.cs");
            Assert.Contains("private void OnSearch(object sender, RoutedEventArgs args) => _ = SearchResourcesAsync(null, null);", page);
            Assert.Contains("private void OnSearchImdb(object sender, RoutedEventArgs args) => _ = SearchResourcesAsync(null, null, \"imdbid\");", page);
            Assert.Contains("private Task SearchSubtitlesAsync() => SearchFromDetailAsync(null, new(Subtitles: true));", page);
            Assert.Contains("SearchFromDetailAsync(season, new(area, episode));", page);
            Assert.Contains("Season = season ?? detail.Media.Season", page);
            Assert.Contains("?? request.Subscription?.Media.Season ?? request.Media?.Season", page);
            Assert.Contains("ReferenceEquals(MediaViewModel.Detail, detail)", page);
            Assert.Contains("request.Service.IsCurrentConnection(media.ConnectionStamp)", page);
            Assert.DoesNotContain("MoviePilotVersionQuery.Keyword(media.Title, season, episode)", page);

            var detail = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Core", "MoviePilot", "MoviePilotMediaDetail.cs"));
            Assert.Contains("public string ImdbId => Text(\"imdb_id\");", detail);
        });

        Test("外壳：详情页的搜索跳到搜索页的 MoviePilot 资源／字幕来源", () =>
        {
            var shell = Source("Views/ShellPage.xaml.cs");
            Assert.Contains("internal void OpenMoviePilotResourceSearch(string keyword)", shell);
            Assert.Contains("SearchSource = 2, MoviePilotResourceSearchNow = true", shell);
            Assert.Contains("internal void OpenMoviePilotDetailSearch(MoviePilotDetailSearch search)", shell);
            Assert.Contains("SearchSource = search.Options.Subtitles ? 3 : 2", shell);
            Assert.Contains("MoviePilotDetailSearch = search", shell);
            Assert.Contains("OpenDetailSearch: OpenMoviePilotDetailSearch", shell);

            // 纯媒体页（搜索/推荐进来的那一页）也摆着同一排搜索键：回调不接，点下去就什么都不发生。
            var mediaStart = shell.IndexOf("private void OpenMoviePilotMedia(MoviePilotMedia media)", StringComparison.Ordinal);
            var mediaEnd = shell.IndexOf("internal void OpenDetail(", StringComparison.Ordinal);
            Assert.True(mediaStart >= 0 && mediaEnd > mediaStart, "OpenMoviePilotMedia 应仍是独立方法");
            var mediaOpen = shell[mediaStart..mediaEnd];
            Assert.Contains("OpenResourceSearch: OpenMoviePilotResourceSearch,", mediaOpen);
            Assert.Contains("OpenDetailSearch: OpenMoviePilotDetailSearch)", mediaOpen);

            var request = Source("Views/LibraryRequest.cs");
            Assert.Contains("public bool MoviePilotResourceSearchNow { get; set; }", request);
            Assert.Contains("public MoviePilot.MoviePilotDetailSearch? MoviePilotDetailSearch { get; set; }", request);
        });

        Test("搜索页：MoviePilot 字幕是第四个来源，AniBT 让到第五", () =>
        {
            var pageXaml = Load("Views/LibraryPage.xaml");
            var tabs = pageXaml.Descendants().Single(element => element.Attribute(X + "Name")?.Value == "SourceTabs");
            var items = tabs.Descendants().Where(element => element.Name.LocalName == "SegmentedItem").ToArray();
            Assert.Equal(5, items.Length);
            Assert.Equal("LibraryPage_MoviePilotSubtitleTab", items[3].Attribute(X + "Uid")?.Value);
            Assert.Equal("LibraryPage_AniBtSourceTab", items[4].Attribute(X + "Uid")?.Value);

            var page = Source("Views/LibraryPage.xaml.cs");
            Assert.Contains("Math.Clamp(SourceTabs.SelectedIndex, 0, 4)", page);
            Assert.Contains("SearchSource != 4 || _settings?.Settings.MoviePilot.AniBtEnabled != false", page);
            Assert.Contains("3 => EnsureMoviePilotSubtitles(),", page);
            Assert.Contains("4 => EnsureAniBtPanel(),", page);
            Assert.Contains("private MoviePilotSubtitlePanel EnsureMoviePilotSubtitles()", page);
            Assert.Contains("AutomationProperties.SetAutomationId(panel, \"MoviePilotSubtitles\");", page);
        });

        Test("搜索页：从详情跳来自动搜，换页回来摆上次结果，搜索页站点表带立即搜索", () =>
        {
            var page = Source("Views/LibraryPage.xaml.cs");
            Assert.Contains("_pendingMoviePilotResourceSearch = !resumed && request.IsSearch && request.MoviePilotResourceSearchNow;", page);
            Assert.Contains("_request!.MoviePilotResourceSearchNow = false;", page);
            Assert.Contains("_pendingMoviePilotDetailSearch = !resumed && request.IsSearch ? request.MoviePilotDetailSearch : null;", page);
            Assert.Contains("_ = panel.SearchAsync(ViewModel.SearchText);", page);
            Assert.Contains("if (!_pendingMoviePilotResourceSearch && _pendingMoviePilotDetailSearch is not { Options.Subtitles: false })", page);
            Assert.Contains("panel.ViewModel.RestoreCached(ViewModel.SearchText);", page);
            Assert.Contains("panel.ViewModel.UseCache(request.Services.GetRequiredService<MoviePilotResourceSearchCache>());", page);
            Assert.Contains("_ = panel.ViewModel.LoadSubtitlesAsync(detailSearch.Media, detailSearch.Options.Episode, detailSearch);", page);
            Assert.Contains("SearchSitePicker.SearchNow = true;", page);
            Assert.Contains("SearchSitePicker.SearchRequested +=", page);
            // 资源与字幕两格共用页面上那一颗站点入口：切来源时跟着换，字幕请求也能带所选站点。
            Assert.Contains("SearchSitePicker.Attach(panel.ViewModel.SearchSites);", page);
            Assert.Contains("SearchSource is 2 or 3 && SearchSourceAvailable", page);
        });

        Test("搜索页：资源和字幕面板已建过时，跨页搜索请求仍会执行", () =>
        {
            var page = Source("Views/LibraryPage.xaml.cs");
            var resourcesStart = page.IndexOf("private MoviePilotVersionsView EnsureMoviePilotResources()", StringComparison.Ordinal);
            var subtitlesStart = page.IndexOf("private MoviePilotSubtitlePanel EnsureMoviePilotSubtitles()", StringComparison.Ordinal);
            var aniBtStart = page.IndexOf("private AniBtSearchPanel EnsureAniBtPanel()", StringComparison.Ordinal);
            Assert.True(resourcesStart >= 0 && subtitlesStart > resourcesStart && aniBtStart > subtitlesStart,
                "三个 MoviePilot／AniBT 面板的确保方法应依次排在 LibraryPage");

            // 面板上次已经建过时不能早返回：搜索页 → 订阅详情 → 再从详情跳回来，请求要照常消费。
            var resources = page[resourcesStart..subtitlesStart];
            Assert.Contains("if (_moviePilotResources is not { } panel)", resources);
            Assert.Contains("if (_pendingMoviePilotDetailSearch is { Options.Subtitles: false } detailSearch)", resources);
            Assert.Contains("_ = panel.ViewModel.SearchDetailAsync(detailSearch);", resources);
            Assert.Contains("else if (_pendingMoviePilotResourceSearch)", resources);
            Assert.Contains("_ = panel.SearchAsync(ViewModel.SearchText);", resources);
            Assert.Contains("_pendingMoviePilotDetailSearch = null;", resources);
            Assert.Contains("_request!.MoviePilotDetailSearch = null;", resources);
            Assert.True(resources.IndexOf("_request!.MoviePilotDetailSearch = null;", StringComparison.Ordinal)
                < resources.IndexOf("_ = panel.ViewModel.SearchDetailAsync(detailSearch);", StringComparison.Ordinal),
                "实际消费前清掉导航请求，历史重入应恢复结果");
            // 面板复用：回到这一格要重读站点偏好，否则详情页刚改的站点会被建面板那一刻的旧选择盖住。
            // 用 ReloadPreference 而不是 Attach：Attach 会 Cancel，把在飞的站点目录读取一起打断。
            Assert.Contains("panel.ViewModel.SearchSites.ReloadPreference();", resources);
            Assert.DoesNotContain("panel.ViewModel.SearchSites.Attach(moviePilot);", resources);
            Assert.DoesNotContain("if (_moviePilotResources is { } existing) return existing;", resources);

            var subtitles = page[subtitlesStart..aniBtStart];
            Assert.Contains("if (_moviePilotSubtitles is not { } panel)", subtitles);
            Assert.Contains("if (_pendingMoviePilotDetailSearch is { Options.Subtitles: true } detailSearch)", subtitles);
            Assert.Contains("_ = panel.ViewModel.LoadSubtitlesAsync(detailSearch.Media, detailSearch.Options.Episode, detailSearch);", subtitles);
            Assert.Contains("_pendingMoviePilotDetailSearch = null;", subtitles);
            Assert.Contains("_request!.MoviePilotDetailSearch = null;", subtitles);
            Assert.True(subtitles.IndexOf("_request!.MoviePilotDetailSearch = null;", StringComparison.Ordinal)
                < subtitles.IndexOf("_ = panel.ViewModel.LoadSubtitlesAsync(detailSearch.Media, detailSearch.Options.Episode, detailSearch);", StringComparison.Ordinal),
                "字幕实际消费前应清掉导航请求");
            Assert.Contains("SearchSitePicker.Attach(panel.ViewModel.SearchSites);", subtitles);
            Assert.Contains("panel.ViewModel.SearchSites.ReloadPreference();", subtitles);
            Assert.DoesNotContain("if (_moviePilotSubtitles is { } existing) return existing;", subtitles);
        });

        Test("搜索页：导航请求的来源在加载后不会被来源标签改回媒体库", () =>
        {
            var page = Source("Views/LibraryPage.xaml.cs");
            Assert.Contains("_requestedSource = request.IsSearch ? request.SearchSource : 0;", page);
            Assert.Contains("SourceTabs.SelectedIndex = _requestedSource;", page);
            var start = page.IndexOf("private void OnSourceChanged", StringComparison.Ordinal);
            var end = page.IndexOf("private void OnSubscriptionMatchOpen", StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "OnSourceChanged 应仍在页面上");
            var changed = page[start..end];
            Assert.Contains("if (_pinningSource && !_sourceChosen && SourceTabs.SelectedIndex != _requestedSource)", changed);
            Assert.Contains("SourceTabs.SelectedIndex = _requestedSource;", changed);
            // 用户自己点过／按过来源标签之后，选择归用户；这里只钉加载时被控件改回的那一次。
            Assert.Contains("private void OnSourceTabsPointerPressed(object sender, PointerRoutedEventArgs e) => _sourceChosen = true;", page);
            Assert.Contains("AddHandler(PointerPressedEvent, new PointerEventHandler(OnSourceTabsPointerPressed)", page);
            Assert.Contains("Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End", page);

            // 控件也可能静默改回（不发 SelectionChanged）：加载完成后再补一手。
            var loadedStart = page.IndexOf("private void OnLoaded(object sender, RoutedEventArgs e)", StringComparison.Ordinal);
            var loadedEnd = page.IndexOf("private void OnUnloaded", StringComparison.Ordinal);
            Assert.True(loadedStart >= 0 && loadedEnd > loadedStart, "OnLoaded 应仍在页面上");
            Assert.Contains("if (_pinningSource && !_sourceChosen && SourceTabs.SelectedIndex != _requestedSource)",
                page[loadedStart..loadedEnd]);
            // 钉定只在来源标签刚落下这一段窗口里生效：第一次排版对上就收手，别挡住之后的程序化切换。
            Assert.Contains("SourceTabs.LayoutUpdated += TrackSourcePin;", page);
            Assert.Contains("private void TrackSourcePin(object? sender, object e)", page);
            Assert.Contains("_pinningSource = false;", page);
        });

        Test("搜索页：换来源先接上新的一格再让旧的收手，补发的来源变更不会取消刚发出的自动搜索", () =>
        {
            var page = Source("Views/LibraryPage.xaml.cs");
            var start = page.IndexOf("private void ApplySource()", StringComparison.Ordinal);
            var end = page.IndexOf("private void OnSearchCategoryChanged", StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "ApplySource 应仍是独立方法");
            var apply = page[start..end];
            // 跨页跳来的自动搜索／字幕读取在 Ensure 里消费；先 Release 会把刚发出去的那一趟取消掉
            //（来源标签加载后补发的 SelectionChanged 会再走一遍 ApplySource）。
            Assert.True(apply.IndexOf("2 => EnsureMoviePilotResources(),", StringComparison.Ordinal)
                < apply.IndexOf("_moviePilotResources?.Release();", StringComparison.Ordinal), "资源格应先接上再收别的面板");
            Assert.Contains("if (!ReferenceEquals(_moviePilotResources, active)) _moviePilotResources?.Release();", apply);
            Assert.Contains("if (!ReferenceEquals(_moviePilotSubtitles, active)) _moviePilotSubtitles?.Release();", apply);
        });

        Test("搜索页：MoviePilot 字幕请求带上所选站点", () =>
        {
            var vm = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "ViewModels", "MoviePilotSubtitleSearchViewModel.cs"));
            Assert.Contains("public MoviePilotSitePickerViewModel SearchSites { get; } = new();", vm);
            Assert.Contains("SearchSites.Attach(service);", vm);
            Assert.Contains("await SearchSites.PrepareSelectionAsync()", vm);
            Assert.Contains("if (!SearchSites.HasSelection)", vm);
            Assert.Contains("service.SearchSubtitlesAsync(media, SearchSites.Selection, episode, token, progress)", vm);
            Assert.DoesNotContain("serverSelection", vm);
            Assert.Contains("SearchSites.SelectionChanged += OnSearchSitesChanged", vm);
            Assert.Contains("SearchSites.SelectionChanged -= OnSearchSitesChanged", vm);
        });
    }

    private static XDocument Load(string relative) => XDocument.Load(Path.Combine(
        FindRoot(), "src", "Momoka.Shell", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Source(string relative) => File.ReadAllText(Path.Combine(
        FindRoot(), "src", "Momoka.Shell", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到当前工作树");
    }
}
