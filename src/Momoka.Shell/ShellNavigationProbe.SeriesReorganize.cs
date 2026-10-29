using System.Net;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotSeriesReorganizeAsync()
    {
        var server = new SeriesReorganizeServer(await FixtureImageAsync());
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var context = new MoviePilotTransferContext("学生会也有洞！", MoviePilotTransferRequest.TypeSeries, "283938", null, "", [])
        { BrowseSeriesHistory = true };
        var originalTheme = ThemeHost.Current.Id;
        var picker = new MoviePilotSeriesReorganizeDialog(service, context) { XamlRoot = _root.XamlRoot };
        var showing = MoviePilotDialog.ShowAsync(picker, service);
        try
        {
            await UntilAsync(() => picker.ViewModel.IsReady && !picker.ViewModel.Busy);
            Require(picker.ViewModel.Rows.Count == 6 && server.Queries.All(query => query.Contains("title=tmdbid=283938", StringComparison.Ordinal)),
                "剧整理清单未按当前剧 TMDB 编号读取资源");
            await LayoutAsync(picker);
            Require(Get<TextBlock>(picker, "SeriesQuery").Text == "tmdbid=283938", "当前剧筛选条件未固定显示");
            var list = Get<ListView>(picker, "SeriesResources");
            Require(list.SelectionMode == ListViewSelectionMode.None && list.IsItemClickEnabled && !list.IsMultiSelectCheckBoxEnabled,
                "清单仍有会在重排时丢失的原生选择，或关闭了整行选择入口");
            var selectionBox = Descendants(list).OfType<CheckBox>().First(box => box.Name == "SelectSeriesResource");
            Require(selectionBox.VerticalAlignment == VerticalAlignment.Top &&
                AutomationProperties.GetName(selectionBox).Contains("学生会也有洞", StringComparison.Ordinal), "资源复选框没有顶部对齐或无法定位");
            var toggle = (IToggleProvider)new CheckBoxAutomationPeer(selectionBox).GetPattern(PatternInterface.Toggle);
            toggle.Toggle();
            Require(picker.ViewModel.SelectedCount == 1 && picker.ViewModel.Rows[0].Selected && picker.ViewModel.SelectedCount == 1,
                "复选框勾选未同步列表选择或重复切换");
            toggle.Toggle();
            Require(picker.ViewModel.SelectedCount == 0 && !picker.ViewModel.Rows[0].Selected, "取消勾选未同步列表选择");
            Require(picker.ViewModel.SelectedCount == 0 && !picker.IsPrimaryButtonEnabled, "清单不能默认授权整理所有文件");
            picker.ViewModel.SetSelected([picker.ViewModel.Rows[0]], true);
            picker.ViewModel.SetSelected([picker.ViewModel.Rows[1]], true);
            Require(picker.ViewModel.SelectedCount == 2 && picker.IsPrimaryButtonEnabled, "多选与批量按钮没有同步");
            var retained = picker.ViewModel.Rows[0];
            InvokeSeriesButton(picker, "ClearResourceSelection");
            Require(picker.ViewModel.SelectedCount == 0 && !picker.IsPrimaryButtonEnabled && ReferenceEquals(retained, picker.ViewModel.Rows[0]),
                "清空选择丢失行对象或没有关闭继续入口");
            await MoviePilotSeriesResourceBrowserAsync(picker, server);
            InvokeSeriesButton(picker, "SelectAllResources");
            Require(picker.ViewModel.SelectedCount == 6 && picker.ViewModel.Rows.All(row => row.Selected),
                "全选未选齐当前剧资源或没有同步复选框");
            foreach (var width in new[] { 1120, 760, 420 })
            {
                ResizeInspect(width, 960);
                await LayoutAsync(picker);
                await Task.Delay(200);
                Require(list.ActualWidth > 180 && list.ActualHeight > 100, "窄窗资源清单被压没");
                Require(Get<Button>(picker, "SelectAllResources").ActualWidth > 30 &&
                    Get<TextBlock>(picker, "SelectedResourcesCount").ActualWidth > 30, "窄窗的选择工具不可见");
                await SaveProbeScreenAsync($"mp-series-reorganize-{width}-screen.png");
            }
            ResizeInspect(760, 960);
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                await LayoutAsync(picker);
                Require(list.SelectionMode == ListViewSelectionMode.None && !list.IsMultiSelectCheckBoxEnabled,
                    "资源选择区仍残留原生方形灰色竖带");
                Require(Descendants(list).OfType<CheckBox>().Where(box => box.Name == "SelectSeriesResource")
                    .All(box => box.IsChecked == true && box.VerticalAlignment == VerticalAlignment.Top), "主题切换丢失复选框选中或顶部对齐");
                var primary = Descendants(picker).OfType<Button>().Single(button => button.Name == "PrimaryButton");
                var close = Descendants(picker).OfType<Button>().Single(button => button.Name == "CloseButton");
                Require(primary.Background is SolidColorBrush actionBrush && close.Background is SolidColorBrush closeBrush &&
                    actionBrush.Color != closeBrush.Color, "批量整理与取消使用相同背景，主要操作不清晰");
                await SaveProbeScreenAsync($"mp-series-reorganize-{theme.Id}-screen.png");
            }
            picker.ViewModel.ClearSelection();
            picker.ViewModel.SetSelected([picker.ViewModel.Rows[0]], true);
            picker.ViewModel.SetSelected([picker.ViewModel.Rows[1]], true);
            InvokeSeriesButton(picker, "PrimaryButton");
            Require(await showing == ContentDialogResult.Primary && picker.SelectedHistories.Count == 2,
                "批量入口没有把实际选择交回下一张表单");
            var chosen = picker.SelectedHistories;
            var optionsBeforeCancel = server.OptionReads;
            var canceled = new MoviePilotSeriesReorganizeDialog(service, context) { XamlRoot = _root.XamlRoot };
            var canceling = MoviePilotDialog.ShowAsync(canceled, service);
            await UntilAsync(() => !canceled.ViewModel.Busy && canceled.ViewModel.Rows.Count == 6);
            InvokeSeriesButton(canceled, "CloseButton");
            Require(await canceling == ContentDialogResult.None && server.OptionReads == optionsBeforeCancel && server.PreviewIds.Length == 0,
                "取消清单仍读取整理选项或自动发预览");
            var episodeContext = new MoviePilotTransferContext(context.Title, MoviePilotTransferRequest.TypeSeries, context.MediaId,
                1, "1", [new MoviePilotTransferFile("单集", chosen[0].TargetPath)]);
            var episodeForm = new MoviePilotReorganizeDialog(service, episodeContext);
            Require(Get<StackPanel>(episodeForm, "HistoryLookupPanel").Visibility == Visibility.Visible &&
                Get<TextBlock>(episodeForm, "BatchSelectionSummary").Visibility == Visibility.Collapsed &&
                Get<TextBox>(episodeForm, "SeasonBox").Text == "1" && Get<TextBox>(episodeForm, "EpisodeBox").Text == "1",
                "单集的历史查找区域或原有季集默认值被剧多选改动改变");
            var selectedContext = MoviePilotSeriesTransfer.CreateContext(context, chosen);
            var form = new MoviePilotReorganizeDialog(service, selectedContext, chosen) { XamlRoot = _root.XamlRoot };
            var editing = MoviePilotDialog.ShowAsync(form, service);
            await UntilAsync(() => !Get<ProgressRing>(form, "PreviewRing").IsActive &&
                Get<ContentControl>(form, "FormPanel").IsEnabled && Get<Button>(form, "PreviewButton").IsEnabled);
            await LayoutAsync(form);
            Require(Get<StackPanel>(form, "HistoryLookupPanel").Visibility == Visibility.Collapsed &&
                Get<TextBlock>(form, "BatchSelectionSummary").Text.Contains("2", StringComparison.Ordinal),
                "预选批量表单仍要求再次匹配媒体库文件");
            Require(Get<TextBox>(form, "SeasonBox").Text == "" && Get<TextBox>(form, "EpisodeBox").Text == "",
                "多选不同集时默认把它们改成同一集");
            InvokeSeriesButton(form, "PreviewButton");
            await UntilAsync(() => !Get<ProgressRing>(form, "PreviewRing").IsActive &&
                Get<Border>(form, "ConfirmationPanel").Visibility == Visibility.Visible);
            Require(server.PreviewIds.SequenceEqual(chosen.Select(history => history.Id)), "预览没有仅带选中的 logids");
            Require(form.Lines.Count == 2 && !form.IsPrimaryButtonEnabled, "批量预览不完整或未经确认直接可提交");
            await SaveProbeScreenAsync("mp-series-reorganize-preview-screen.png");
            form.Hide();
            await editing;
            Require(server.Writes == 0 && fixture.Actions.Plays == 0, "选择、预览和取消不应写文件或播放");

            server.FailHistory = true;
            var retry = new MoviePilotSeriesReorganizeDialog(service, context) { XamlRoot = _root.XamlRoot };
            var retrying = MoviePilotDialog.ShowAsync(retry, service);
            await UntilAsync(() => retry.ViewModel.IsReady && !retry.ViewModel.Busy);
            Require(retry.ViewModel.NoticeOpen && retry.ViewModel.Rows.Count == 0 && !retry.IsPrimaryButtonEnabled,
                "读取失败仍留可执行旧资源");
            await LayoutAsync(retry);
            await SaveProbeScreenAsync("mp-series-reorganize-failed-screen.png");
            server.FailHistory = false;
            InvokeSeriesButton(retry, "ReloadResources");
            await UntilAsync(() => !retry.ViewModel.Busy && retry.ViewModel.Rows.Count == 6);
            Require(!retry.ViewModel.NoticeOpen && retry.ViewModel.SelectedCount == 0, "重试未恢复资源或保留了旧授权");
            server.EmptyHistory = true;
            InvokeSeriesButton(retry, "ReloadResources");
            await UntilAsync(() => !retry.ViewModel.Busy && retry.ViewModel.Rows.Count == 0);
            Require(retry.ViewModel.EmptyVisibility == Visibility.Visible && !retry.IsPrimaryButtonEnabled,
                "空清单没有明确空态或仍允许整理");
            await SaveProbeScreenAsync("mp-series-reorganize-empty-screen.png");
            retry.Hide();
            await retrying;
            server.EmptyHistory = false;
            server.PendingHistory = Pending<bool>();
            var late = new MoviePilotSeriesReorganizeDialog(service, context) { XamlRoot = _root.XamlRoot };
            var cancelLate = MoviePilotDialog.ShowAsync(late, service);
            await UntilAsync(() => late.ViewModel.Busy);
            late.Hide();
            await cancelLate;
            server.PendingHistory.TrySetResult(true);
            server.PendingHistory = null;
            await UntilAsync(() => server.LateHistoryReturned);
            await Task.Delay(150);
            Require(late.ViewModel.Rows.Count == 0 && !late.ViewModel.CanContinue, "取消后迟到的资源重新回填清单");
            var unavailable = new MoviePilotSeriesReorganizeDialog(service, context) { XamlRoot = _root.XamlRoot };
            var closing = MoviePilotDialog.ShowAsync(unavailable, service);
            await UntilAsync(() => !unavailable.ViewModel.Busy && unavailable.ViewModel.Rows.Count == 6);
            fixture.Settings.MoviePilot.Enabled = false;
            Require(await closing == ContentDialogResult.None, "连接失效没有关闭剧整理清单");
            Require(server.Writes == 0 && fixture.Actions.Plays == 0, "恢复与显隐不应发生真实副作用");
        }
        finally
        {
            picker.Hide();
            ThemeHost.Apply(originalTheme);
            ResizeInspect(1420, 980);
        }
    }

    private static void InvokeSeriesButton(ContentDialog dialog, string name)
    {
        var button = Descendants(dialog).OfType<Button>().Single(button => button.Name == name);
        Require(AutomationProperties.GetAutomationId(button).Length > 0 || button.Name.Length > 0,
            $"剧整理按钮 {name} 没有自动化句柄");
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private sealed class SeriesReorganizeServer(byte[] image) : HttpMessageHandler
    {
        internal List<string> Queries { get; } = [];
        internal bool FailHistory { get; set; }
        internal bool EmptyHistory { get; set; }
        internal long[] PreviewIds { get; private set; } = [];
        internal int Writes { get; private set; }
        internal int OptionReads { get; private set; }
        internal TaskCompletionSource<bool>? PendingHistory { get; set; }
        internal bool LateHistoryReturned { get; private set; }
        private static string Source(long id) => $"/Downloads/BT/[Nekomoe kissaten] Seitokai ni mo Ana wa Aru! [0{id}][1080p].mkv";
        private static string Target(long id) => $"/Downloads_link/电视剧/学生会也有洞！ (2026) {{tmdbid=283938}}/Season 01/学生会也有洞！ S01E0{id} 1080p-Nekomoe kissaten.mkv";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "剧整理探针拒绝真实网络");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture" });
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Json(new { success = true, data = new { version = "v3.0.1" } });
            if (path.EndsWith("system/cache/image", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) };
            if (path.EndsWith("history/transfer", StringComparison.Ordinal))
            {
                Queries.Add(Uri.UnescapeDataString(request.RequestUri.Query));
                if (PendingHistory is { } pending)
                {
                    await pending.Task;
                    LateHistoryReturned = true;
                }
                if (FailHistory) return new HttpResponseMessage(HttpStatusCode.BadRequest);
                var list = EmptyHistory ? Array.Empty<object>() : Enumerable.Range(1, 6).Select(index => (object)new
                {
                    id = (long)index,
                    title = "学生会也有洞！",
                    type = "电视剧",
                    media_source = "themoviedb",
                    media_id = "283938",
                    tmdbid = 283938,
                    year = "2026",
                    seasons = "S01",
                    episodes = $"E0{index}",
                    mode = "softlink",
                    status = true,
                    date = "2026-10-28 10:00:00",
                    src = Source(index),
                    dest = Target(index),
                    image = "https://images.invalid/poster.png",
                    size = 375_000_000L,
                    src_fileitem = new { storage = "local", type = "file", path = Source(index), size = 375_000_000L + index },
                    dest_fileitem = new { storage = "local", type = "file", path = Target(index) }
                }).ToArray();
                return Json(new { success = true, data = new { total = list.Length, list } });
            }
            if (path.EndsWith("storage/options", StringComparison.Ordinal)) OptionReads++;
            if (path.EndsWith("storage/options", StringComparison.Ordinal)) return Json(new[] { new { name = "本地", type = "local" } });
            if (path.EndsWith("media/source", StringComparison.Ordinal)) return Json(new[] { new { media_source = "themoviedb", name = "TheMovieDb", media_types = new[] { "电影", "电视剧" } } });
            if (path.EndsWith("storage/directories", StringComparison.Ordinal)) return Json(new[] { new { library_storage = "local", library_path = "/Downloads_link", transfer_type = "softlink", scraping = true, library_type_folder = true } });
            if (path.EndsWith("transfer/manual/target-path", StringComparison.Ordinal)) return Json(new
            { success = true, data = new { target_storage = "local", target_path = "/Downloads_link", transfer_type = "softlink", scrape = true, library_type_folder = true, library_category_folder = false } });
            if (path.EndsWith("storage/list", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var source = body.RootElement.GetProperty("path").GetString();
                return Json(new { success = true, data = new[] { new { storage = "local", type = "file", path = source } } });
            }
            if (path.EndsWith("transfer/manual", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var data = body.RootElement;
                if (!data.GetProperty("preview").GetBoolean())
                {
                    Writes++;
                    throw new InvalidOperationException("本探针不允许实际整理写入");
                }
                PreviewIds = data.TryGetProperty("logids", out var ids)
                    ? ids.EnumerateArray().Select(id => id.GetInt64()).ToArray() : [data.GetProperty("logid").GetInt64()];
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        items = PreviewIds.Select(id => new
                        { source = Source(id), target = Target(id), title = "学生会也有洞！", state = "preview", success = true }).ToArray()
                    }
                });
            }
            throw new InvalidOperationException("剧整理探针拒绝未登记的请求：" + path);
        }
    }
}
