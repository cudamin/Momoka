using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Momoka.Configuration;
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
            Get<CheckBox>(dialog, "UseDefaultSites").IsChecked = false;
            Require(view.ViewModel.Versions.Count == 1, "打开站点弹窗清空了已有结果");
            var rows = Descendants(dialog).OfType<ToggleButton>().Where(box => box.Tag is MoviePilotSearchSiteRow).ToArray();
            Require(rows.Length == 3, "站点网格未加载或包含已停用站点");
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
}
