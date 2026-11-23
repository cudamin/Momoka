using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private static async Task<(MoviePilotSiteDialog Dialog, Task<ContentDialogResult> Opening)> OpenSiteDialogAsync(
        MoviePilotSitePicker picker, string context)
    {
        picker.ViewModel.RefreshConnection();
        RequireSitePickerOpenable(picker, context);
        var opening = picker.OpenAsync();
        await UntilAsync(() => picker.CurrentDialog is not null || opening.IsFaulted);
        if (opening.IsFaulted)
            throw new InvalidOperationException($"{context} 站点弹窗创建失败", opening.Exception);
        if (picker.CurrentDialog is not { } dialog)
            throw new TimeoutException($"{context} 站点弹窗未出现");
        return (dialog, opening);
    }

    private static void RequireSitePickerOpenable(MoviePilotSitePicker picker, string context)
    {
        var service = Get<MoviePilotService?>(picker.ViewModel, "_service");
        var root = picker.XamlRoot is not null;
        var current = picker.ViewModel.IsCurrentConnection;
        if (!root || !current)
            throw new InvalidOperationException($"{context} 站点入口不可打开：root={root}，current={current}，"
                + $"stamp={Get<string>(picker.ViewModel, "_connectionStamp")}，available={service?.Available}，"
                + $"editing={Get<bool>(picker.ViewModel, "_editing")}，loaded={Get<bool>(picker.ViewModel, "_loaded")}");
    }

    private static void ClickDialogPrimary(ContentDialog dialog)
    {
        var primary = (Button)DialogDismiss.TemplatePart(dialog, "PrimaryButton")!;
        ((IInvokeProvider)new ButtonAutomationPeer(primary).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private async Task MoviePilotSearchSitesAsync()
    {
        var server = new SearchProbeServer();
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        Require(await service.RefreshAvailabilityAsync(), "假 MoviePilot 连接失败");
        var view = new MoviePilotVersionsView();
        var precise = new MoviePilotResourcePanel();
        _root.Children.Add(view);
        try
        {
            view.Attach(service, fixture.Launcher);
            await LayoutAsync(view);
            await view.SearchAsync("测试资源");
            Require(!server.Paths.Last().Contains("sites=", StringComparison.Ordinal), "默认站点不应发送具体编号");
            var picker = Get<MoviePilotSitePicker>(view, "SearchSitePicker");
            var (dialog, opening) = await OpenSiteDialogAsync(picker, "版本窗口");
            var draft = dialog.ViewModel;
            await UntilAsync(() => draft.Sites.Count == 3);
            await LayoutAsync(dialog);
            // 取消勾选默认站点、改站点都不清空已经铺出来的资源（2026-10-29 用户令）；下一次搜索才换掉它们。
            view.ViewModel.Browser.SetResults([new MoviePilotResourceRow(new MoviePilotResource
            {
                Title = "站点切换前的资源", SiteName = "站点甲", Size = 2147483648,
                TorrentInfo = System.Text.Json.JsonSerializer.SerializeToElement(new { title = "fixture" })
            }, _ => Task.CompletedTask, _ => { })]);
            view.ViewModel.ShowEmptyNotice = false;
            Require(dialog.ViewModel.UseDefault && dialog.ViewModel.Selection is null && dialog.ViewModel.SelectedCount == 0,
                "普通关键词窗口没有沿用默认站点选择");
            Require(view.ViewModel.Versions.Count == 1, "打开站点弹窗清空了已有结果");
            var rows = Descendants(dialog).OfType<ToggleButton>().Where(box => box.Tag is MoviePilotSearchSiteRow).ToArray();
            Require(rows.Length == 3, "站点网格未加载或包含已停用站点");
            rows[0].IsChecked = true;
            rows[0].IsChecked = false;
            Require(view.ViewModel.Versions.Count == 1, "取消站点勾选清空了已有结果");
            rows[0].IsChecked = true;
            rows[1].IsChecked = true;
            await UntilAsync(() => draft.Selection?.Ids.Count == 2);
            Require(dialog.IsPrimaryButtonEnabled, "选好站点后确认按钮仍不可用");
            // RenderTargetBitmap 抓弹层拿不到主题与文字（见上一轮那张空白的返帧），面板外观另存真实屏幕帧。
            await SaveProbeScreenAsync("search-sites-picker-screen.png");
            await SaveReturnFrameAsync((FrameworkElement)dialog.Content, "search-sites-picker.png");
            ClickDialogPrimary(dialog);
            Require(await opening == ContentDialogResult.Primary, "站点网格没有以确认关闭");
            Require(view.ViewModel.Versions.Count == 1, "确认站点清空了已有结果");
            Require(view.ViewModel.NoticeOpen && view.ViewModel.NoticeMessage?.Contains("重搜", StringComparison.Ordinal) == true,
                "确认站点后没有说明要重新搜索才生效");
            Require(picker.ViewModel.Summary == "已选 2 个站点", "站点选择未显示在入口上");
            await view.SearchAsync("测试资源");
            Require(Uri.UnescapeDataString(server.Paths.Last()).Contains("sites=1,2", StringComparison.Ordinal), "所选站点未进入关键词搜索请求");
            view.ViewModel.SearchSites.ClearSelectionCommand.Execute(null);
            var count = server.Paths.Count;
            await view.SearchAsync("测试资源");
            Require(server.Paths.Count == count && view.ViewModel.ShowEmptyNotice, "清空站点后误发默认搜索");
            view.ViewModel.SearchSites.SelectAllCommand.Execute(null);
            await view.SearchAsync("测试资源");
            Require(Uri.UnescapeDataString(server.Paths.Last()).Contains("sites=1,2,3", StringComparison.Ordinal), "全选不应退回默认站点");
            var delayed = Pending<HttpResponseMessage>();
            server.PendingResource = delayed;
            var loading = view.SearchAsync("迟到资源");
            view.ViewModel.SearchSites.UseDefault = true;
            delayed.TrySetResult(Json(new[] { new { torrent_info = new { title = "旧结果" } } }));
            await loading;
            Require(view.ViewModel.Versions.Count == 0 && !view.ViewModel.Busy, "更改站点后旧结果复活");
            server.PendingResource = null;
            view.Release();
            _root.Children.Remove(view);
            _root.Children.Add(precise);
            precise.Open(service, new MoviePilotMedia { Title = "测试电影", Type = "电影", MediaSource = "themoviedb", MediaId = "42" }, fixture.Launcher);
            await LayoutAsync(precise);
            await precise.ViewModel.SearchSites.EnsureLoadedAsync();
            // 分类搜索会继承设置页那份默认站点（见 MoviePilotService.SearchSitePreference）：先清空继承来的勾，
            // 再只选一个，这条才量得到「精确资源仍按媒体类型读目录、按所选站点发请求」。
            precise.ViewModel.SearchSites.ClearSelectionCommand.Execute(null);
            precise.ViewModel.SearchSites.Sites[1].Selected = true;
            await precise.ViewModel.SearchAsync();
            Require(server.Paths.Any(path => path.EndsWith("site/media/movie", StringComparison.Ordinal))
                && server.Paths.Last().Contains("search/media/42?", StringComparison.Ordinal)
                && server.Paths.Last().Contains("sites=2", StringComparison.Ordinal), "精确资源搜索没有使用按媒体类型读取的站点");
            await SaveReturnFrameAsync(precise, "search-sites-precise.png");
            precise.Release();
            Require(fixture.Actions.Plays == 0, "站点选择不应启动播放");
        }
        finally
        {
            view.Release();
            precise.Release();
            _root.Children.Remove(view);
            _root.Children.Remove(precise);
            // 弹层关闭后移除其拥有者会丢失前台；把焦点交回探针窗口，供后续真实截图验证。
            _window?.Activate();
        }
    }

    /// <summary>
    /// 订阅详情「按标题搜索」→ 搜索页：站点表留在详情页，确认后保存本机选择并把身份交给外壳；搜索页
    /// 按来源 2 自动检索，并与字幕共用刚确认的站点（用户报 2026-11-23「点了搜索跳到搜索页也没触发搜索」）。
    /// </summary>
    private async Task MoviePilotSearchJumpAsync()
    {
        var server = new SubscriptionProbeServer();
        server.Poster = await FixtureImageAsync();
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        Require(await service.RefreshAvailabilityAsync(), "假 MoviePilot 连接失败");
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        fixture.Transport.Reply = (request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.Contains("/Episodes", StringComparison.Ordinal)
                ? Json(new { Items = Array.Empty<EmbyItem>() })
                : Json(new { Items = new[] { new EmbyItem { Id = "linked-show", Type = EmbyItemType.Series } } }));
        var searches = new List<MoviePilotDetailSearch>();
        var page = new MoviePilotSubscriptionPage();
        var frame = new Frame();
        Exception? navigationFailure = null;
        frame.NavigationFailed += (_, args) => navigationFailure = args.Exception;
        _root.Children.Add(page);
        var picker = Get<MoviePilotSitePicker>(page, "SearchSitePicker");
        // 一屏到底的读数：超时时说出搜索页此刻的来源、待办请求、面板与站点状态，不靠再猜一轮。
        string State()
        {
            var live = frame.Content as LibraryPage;
            var resources = live is null ? null : Get<MoviePilotVersionsView?>(live, "_moviePilotResources");
            var subtitles = live is null ? null : Get<MoviePilotSubtitlePanel?>(live, "_moviePilotSubtitles");
            return "searchText=" + (live?.ViewModel.SearchText ?? "-")
                + "，source=" + (live is null ? "-" : Get<SearchSourceTabs>(live, "SourceTabs").SelectedIndex.ToString())
                + "，pending=" + (live is null ? "-" : Get<object?>(live, "_pendingMoviePilotResourceSearch") + "/" + Get<object?>(live, "_pendingMoviePilotDetailSearch"))
                + "，资源面板=" + (resources is null ? "未建" : resources.ViewModel.Versions.Count + " 条/" + resources.ViewModel.EmptyNotice)
                + "，提示=" + (resources is null ? "-" : resources.ViewModel.NoticeTitle + "/" + resources.ViewModel.NoticeMessage)
                + "，站点=" + (resources is null ? "-" : resources.ViewModel.SearchSites.UseDefault
                    + "/" + resources.ViewModel.SearchSites.Sites.Count + "/" + resources.ViewModel.SearchSites.SelectedCount)
                + "，字幕面板=" + (subtitles is null ? "未建" : subtitles.ViewModel.Subtitles.Count + " 条")
                + "，请求=" + server.KeywordSearches.Count + "/" + server.SubtitleSearches.Count;
        }
        async Task ReadyAsync(Func<bool> ready, string context)
        {
            try { await UntilAsync(ready); }
            catch (TimeoutException) { throw new TimeoutException($"{context}未就绪（{State()}）"); }
        }
        // 排版等待带上下文：门外的 LayoutAsync 只报「未等到预期状态」，分不清是哪一页、也看不到实际尺寸。
        async Task LayoutProbeAsync(FrameworkElement element, string context)
        {
            try
            {
                // 跳页带过场，门里那 2 秒在满载的机器上会偏紧；这里放宽到 8 秒，只在这一条用例里用。
                await UntilAsync(() =>
                {
                    element.UpdateLayout();
                    return element.XamlRoot is not null && element.ActualWidth > 0 && element.ActualHeight > 0;
                }, attempts: 320);
                await Task.Delay(150);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"{context}未完成排版：{element.GetType().Name} {element.ActualWidth:0}×{element.ActualHeight:0}，"
                    + $"root={(element.XamlRoot is not null)}，frame={(frame.Content as FrameworkElement)?.GetType().Name ?? "null"}，"
                    + $"导航失败={(navigationFailure is null ? "无" : navigationFailure.GetType().Name + "：" + navigationFailure.Message)}");
            }
        }
        try
        {
            page.Attach(new(service, subscription,
                Settings: fixture.SettingsService,
                OpenDetailSearch: search => searches.Add(search)));
            await ReadyAsync(() => page.MediaViewModel.CanInteract, "订阅详情页");
            await LayoutProbeAsync(page, "订阅详情页");

            // 详情页点「按标题搜索」：初始沿用本机默认模式，勾一个站点后确认保存，并把身份交给外壳。
            var catalogueReads = server.SiteCatalogueReads.Count;
            var opening = (Task)Call(page, "SearchResourcesAsync", null, null, "title")!;
            await ReadyAsync(() => picker.CurrentDialog is not null, "资源站点表未弹出");
            var dialog = picker.CurrentDialog!;
            await ReadyAsync(() => server.SiteCatalogueReads.Count > catalogueReads && dialog.ViewModel.Sites.Count > 0,
                "资源站点目录未重新加载");
            Require(server.SiteCatalogueReads.Skip(catalogueReads).All(path => path == "/api/v1/site/"),
                "详情选站没有使用不限类型目录");
            Require(dialog.ViewModel.Sites.Count == 2, "站点目录没有返回两个启用站点");
            await LayoutProbeAsync((FrameworkElement)dialog.Content, "资源站点表正文");
            // 目录与 ItemsRepeater 实际生成当前草稿的行分别等待。
            await ReadyAsync(() => dialog.SiteGrid.TryGetElement(0) is ToggleButton first
                && ReferenceEquals(first.Tag, dialog.ViewModel.Sites[0]) && first.IsLoaded && first.ActualWidth > 0
                && dialog.SiteGrid.TryGetElement(1) is ToggleButton second
                && ReferenceEquals(second.Tag, dialog.ViewModel.Sites[1]) && second.IsLoaded && second.ActualWidth > 0,
                "资源站点行未完成排版");
            Require(dialog.ViewModel.UseDefault && dialog.ViewModel.Selection is null && dialog.ViewModel.SelectedCount == 0
                && Get<CheckBox>(dialog, "UseDefaultSites").IsChecked == true,
                "详情选站没有恢复本机默认站点模式");
            Require(!Descendants(dialog).OfType<Button>().Any(button => button.Name == "ReloadSites"),
                "站点表仍显示刷新按钮");
            var rows = Descendants(dialog).OfType<ToggleButton>().Where(box => box.Tag is MoviePilotSearchSiteRow).ToArray();
            Require(rows.Length == 2, "站点表没有列出两个启用站点：" + rows.Length);
            rows[1].IsChecked = true;
            await ReadyAsync(() => dialog.ViewModel.Selection?.Ids.Count == 1, "站点未勾上");
            await ReadyAsync(() => dialog.IsPrimaryButtonEnabled, "资源站点表确认键未亮");
            ClickDialogPrimary(dialog);
            await opening;
            Require(searches.Count == 1 && searches[0].Media.Title == "漫长的旅途" && searches[0].Options.Area == "title",
                "详情页没有把精确检索交给外壳");
            Require(searches[0].Sites?.Ids.SequenceEqual([2]) == true && !service.SearchSitePreference().UseDefault
                && service.SearchSitePreference().SiteIds.SequenceEqual([2]),
                "详情页没有保存并传递不限类型站点选择");

            // 「搜索字幕」也先选站点：重读目录，继承刚才确认的同一份本机选择。
            var media = page.MediaViewModel.Detail!.Media;
            catalogueReads = server.SiteCatalogueReads.Count;
            var subtitleOpening = (Task)Call(page, "SearchSubtitlesAsync")!;
            await ReadyAsync(() => picker.CurrentDialog is not null, "字幕站点表未弹出");
            var subtitleDialog = picker.CurrentDialog!;
            await ReadyAsync(() => server.SiteCatalogueReads.Count > catalogueReads && subtitleDialog.ViewModel.Sites.Count > 0,
                "字幕站点目录未重新加载");
            Require(server.SiteCatalogueReads.Skip(catalogueReads).All(path => path == "/api/v1/site/"),
                "字幕选站没有使用不限类型目录");
            await LayoutProbeAsync((FrameworkElement)subtitleDialog.Content, "字幕站点表正文");
            Require(!subtitleDialog.ViewModel.UseDefault && subtitleDialog.ViewModel.SelectedCount == 1
                && subtitleDialog.ViewModel.Selection?.Ids.SequenceEqual([2]) == true,
                "字幕选站没有继承刚保存的站点 2");
            await ReadyAsync(() => DialogDismiss.TemplatePart(subtitleDialog, "PrimaryButton") is Button
            { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 }, "字幕确认按钮未完成排版");
            await ReadyAsync(() => subtitleDialog.IsPrimaryButtonEnabled, "字幕站点表确认键未亮");
            ClickDialogPrimary(subtitleDialog);
            await subtitleOpening;
            Require(searches.Count == 2 && searches[1].Options.Subtitles && searches[1].Media.MediaId == media.MediaId
                && searches[1].Sites?.Ids.SequenceEqual([2]) == true, "字幕没有带上影片身份与共用的本机站点选择");

            // 搜索页收到来源 2 与「立刻搜」：按完整影片身份重新检索，并带上刚选的站点。
            // 详情页这一半已经走完，先让出根面板：两页叠在一起量排版会把「谁没铺开」混成同一件事。
            _root.Children.Remove(page);
            _root.Children.Add(frame);
            var request = LibraryRequest.Search(fixture.Services, media.Title) with
            { SearchSource = 2, MoviePilotDetailSearch = searches[0] };
            frame.Navigate(typeof(LibraryPage), request);
            var library = (LibraryPage)frame.Content;
            await LayoutProbeAsync(library, "搜索页（资源来源）");
            await ReadyAsync(() => server.MediaSearches.Count > 0, "媒体资源请求未发出");
            var search = Uri.UnescapeDataString(server.MediaSearches[0]);
            Require(search.Contains("area=title", StringComparison.Ordinal) && search.Contains("media_source=themoviedb", StringComparison.Ordinal),
                "跳来后没有按影片身份和标题方式检索");
            Require(search.Contains("sites=2", StringComparison.Ordinal), "搜索页没有用详情页刚选的站点：" + search);
            var resources = Get<MoviePilotVersionsView>(library, "_moviePilotResources");
            await ReadyAsync(() => resources.ViewModel.Versions.Count > 0, "资源结果未铺出");
            Require(resources.Visibility == Visibility.Visible, "搜索页没有切到 MoviePilot 资源来源");

            // 来源 3：带影片身份跳来就直接读字幕，同样用刚选的站点（面板上一轮已经建过也要读）。
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services, media.Title) with
            { SearchSource = 3, MoviePilotDetailSearch = searches[1] });
            library = (LibraryPage)frame.Content;
            await LayoutProbeAsync(library, "搜索页（字幕来源）");
            await ReadyAsync(() => server.SubtitleSearches.Count > 0, "字幕请求未发出");
            var subtitleSearch = Uri.UnescapeDataString(server.SubtitleSearches[0]);
            Require(subtitleSearch.Contains("sites=2", StringComparison.Ordinal), "字幕搜索没有带上详情页刚选的站点：" + subtitleSearch);
            var subtitles = Get<MoviePilotSubtitlePanel>(library, "_moviePilotSubtitles");
            await ReadyAsync(() => subtitles.ViewModel.Subtitles.Count > 0, "字幕结果未铺出");
            Require(subtitles.Visibility == Visibility.Visible, "搜索页没有切到 MoviePilot 字幕来源");
        }
        finally
        {
            _root.Children.Remove(frame);
            if (frame.Content is IShellContent content) content.Release();
            _root.Children.Remove(page);
            page.Release();
            // 弹层关闭后移除其拥有者会丢失前台；把焦点交回探针窗口，供后续用例的真实输入与弹层打开。
            _window?.Activate();
        }
    }
}
