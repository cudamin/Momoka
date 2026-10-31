using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotManualOrganizeAsync()
    {
        var server = new ManualOrganizeServer();
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var originalTheme = ThemeHost.Current.Id;
        MoviePilotReorganizeDialog? dialog = null;
        Task<ContentDialogResult>? showing = null;
        try
        {
            await ShowAsync();
            var type = Get<ComboBox>(dialog!, "TypeBox");
            var method = Get<ComboBox>(dialog!, "TransferTypeBox");
            var storage = Get<ComboBox>(dialog!, "TargetStorageBox");
            var path = Get<ComboBox>(dialog!, "TargetPathBox");
            var mediaId = Get<AutoSuggestBox>(dialog!, "MediaIdBox");
            // 打开时全部自动：不预选当前条目的类型、编号、季集，也不显示识别区。
            Require(type.SelectedIndex == 0, "打开时类型不是自动");
            Require(mediaId.Text.Length == 0, "媒体编号带了当前条目的值");
            Require(Get<TextBox>(dialog!, "SeasonBox").Text.Length == 0, "季号带了当前条目的值");
            Require(Get<TextBox>(dialog!, "EpisodeBox").Text.Length == 0, "集号带了当前条目的值");
            Require(Get<Grid>(dialog!, "IdentityGrid").Visibility == Visibility.Collapsed, "自动类型仍显示识别区");
            Require(path.Text.Length == 0, "自动匹配覆盖了未确认的目的路径");
            Require(method.SelectedIndex == 0, "自动方式变成了显式目录方式");
            Require(Get<TextBox>(dialog!, "OriginalSourceBox").Text == ManualOrganizeServer.Source, "原源文件未回填：" + Get<TextBox>(dialog!, "OriginalSourceBox").Text);
            Require(Get<ToggleSwitch>(dialog!, "TypeFolderToggle").IsOn, "自动分类没有展示匹配目录的有效值");
            Require(Get<Border>(dialog!, "MatchedPathPanel").Visibility == Visibility.Visible, "缺少使用匹配路径入口");
            // 放大后的窗口一次放下全部选项：默认视图没有纵向滚动。
            ResizeInspect(1040, 920);
            await LayoutAsync(dialog!);
            await Task.Delay(120);
            var formScroll = Get<ScrollViewer>(dialog!, "FormScroll");
            Require(dialog!.ActualWidth >= 900, $"整理窗口没有放大：{dialog.ActualWidth:0}");
            Require(formScroll.ScrollableHeight < 1, $"默认视图仍需滚动：{formScroll.ScrollableHeight:0}；窗口 {dialog!.ActualWidth:0}×{dialog.ActualHeight:0}，正文 {Get<Grid>(dialog, "DialogBody").ActualWidth:0}×{Get<Grid>(dialog, "DialogBody").ActualHeight:0}，视口 {formScroll.ViewportHeight:0}，表单 {Get<ContentControl>(dialog, "FormPanel").ActualHeight:0}，目的 {Get<Grid>(dialog, "DestinationGrid").ActualWidth:0}，折叠 {Get<Expander>(dialog, "AdvancedOptions").IsExpanded}");
            await SaveProbeScreenAsync("mp-manual-default-screen.png");
            // 选媒体库目录：存储、整理方式、刮削与两个分类开关跟着目录配置走。
            path.Text = "/library";
            await SettleAsync();
            Require(storage.SelectedItem is MoviePilotStorage { Type: "local" }, "选目录没有带出目的存储");
            Require(method.SelectedIndex == 4, "选目录没有带出整理方式");
            Require(Get<ToggleSwitch>(dialog!, "TypeFolderToggle").IsOn, "选目录没有打开按类型分类");
            Require(!Get<ToggleSwitch>(dialog!, "CategoryFolderToggle").IsOn, "选目录没有按目录关闭按类别分类");
            Require(Get<ToggleSwitch>(dialog!, "ScrapeToggle").IsOn, "选目录没有带出刮削");
            Require(Get<Border>(dialog!, "MatchedPathPanel").Visibility == Visibility.Collapsed, "显式路径仍显示自动匹配");
            // 与上游一致：已手选的整理方式不被后来的路径覆盖；陌生路径退到本地复制。
            path.Text = "/custom/library";
            await SettleAsync();
            Require(method.SelectedIndex == 4, "后来的路径覆盖了已手选的整理方式");
            // 回到自动：显式值全部清空，交回后端匹配。
            path.Text = "";
            await UntilAsync(() => Get<Border>(dialog!, "MatchedPathPanel").Visibility == Visibility.Visible &&
                method.SelectedIndex == 0 && storage.SelectedIndex == 0);
            // 手选电视剧、TMDB 来源与编号后才有剧集组，取回两组供下拉。
            type.SelectedIndex = 2;
            await LayoutAsync(dialog!);
            RequireSearchRadius(mediaId);
            Get<Expander>(dialog!, "AdvancedOptions").IsExpanded = true;
            await LayoutAsync(dialog!);
            RequireSearchRadius(Get<AutoSuggestBox>(dialog!, "HistoryQuery"));
            Get<Expander>(dialog!, "AdvancedOptions").IsExpanded = false;
            Get<ComboBox>(dialog!, "SourceBox").SelectedIndex = 1;
            mediaId.Text = "123";
            Call(dialog!, "QueueGroupLookup");
            await UntilAsync(() => Get<ComboBox>(dialog!, "EpisodeGroupBox").Items.Count == 2);
            await LayoutAsync(dialog!);
            await Task.Delay(120);
            Require(formScroll.ScrollableHeight < 1,
                $"识别区展开后仍需滚动：{formScroll.ScrollableHeight:0}；视口 {formScroll.ViewportHeight:0}，表单 {Get<ContentControl>(dialog!, "FormPanel").ActualHeight:0}");
            foreach (var width in new[] { 1040, 720, 520 })
            {
                ResizeInspect(width, 920);
                await LayoutAsync(dialog!);
                await Task.Delay(100);
                var body = Get<Grid>(dialog!, "DialogBody");
                var scroll = Get<ScrollViewer>(dialog!, "FormScroll");
                scroll.ChangeView(null, 0, null, disableAnimation: true);
                await Task.Delay(100);
                foreach (var action in new[] { "PreviewButton", "QueueButton", "ReorganizeButton", "DismissReorganize" })
                {
                    var button = Get<Button>(dialog!, action);
                    Require(button.ActualWidth >= 40 && button.ActualHeight >= 32, "操作按钮被压缩：" + action);
                    Require(AutomationProperties.GetAutomationId(button).Length > 0 || button.Name.Length > 0, "操作按钮无法定位：" + action);
                    var position = button.TransformToVisual(dialog!).TransformPoint(default);
                    Require(position.X >= 0 && position.Y >= 0 && position.X + button.ActualWidth <= dialog!.ActualWidth + 1 &&
                        position.Y + button.ActualHeight <= dialog.ActualHeight + 1, "操作按钮被裁切：" + action);
                }
                var episode = Get<TextBox>(dialog!, "EpisodeBox");
                Require(episode.ActualWidth >= 200, "窄窗季集输入被压扁");
                Require(body.ActualWidth <= dialog!.ActualWidth + 1 && scroll.HorizontalScrollMode == ScrollMode.Disabled,
                    "表单没有限制横向滚动");
                var footer = Get<Grid>(dialog!, "ActionsGrid");
                var before = footer.TransformToVisual(dialog!).TransformPoint(default).Y;
                scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
                await Task.Delay(130);
                Require(Math.Abs(before - footer.TransformToVisual(dialog!).TransformPoint(default).Y) < 1, "底部操作跟随正文滚动");
                await SaveProbeScreenAsync($"mp-manual-{width}-bottom-screen.png");
                scroll.ChangeView(null, 0, null, disableAnimation: true);
                await Task.Delay(130);
                await SaveProbeScreenAsync($"mp-manual-{width}-screen.png");
            }
            ResizeInspect(1040, 920);
            await LayoutAsync(dialog!);
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                dialog!.RequestedTheme = ElementTheme.Dark;
                await LayoutAsync(dialog);
                await SaveProbeScreenAsync($"mp-manual-{theme.Id}-screen.png");
            }
            ThemeHost.Apply(originalTheme);
            Invoke("UseMatchedPathButton");
            await LayoutAsync(dialog!);
            Require(Get<ComboBox>(dialog!, "TargetPathBox").Text == "/library", "匹配路径按钮未应用路径");
            Require(Get<ComboBox>(dialog!, "TargetStorageBox").SelectedItem is MoviePilotStorage { Type: "local" }, "匹配路径遗漏存储");
            Require(Get<ComboBox>(dialog!, "TransferTypeBox").SelectedIndex == 4, "匹配路径遗漏软链接方式");
            Require(Get<ToggleSwitch>(dialog!, "ScrapeToggle").IsOn, "匹配路径遗漏刮削设置");
            Get<TextBox>(dialog!, "EpisodeFormatBox").Text = "第{ep}集";
            Get<TextBox>(dialog!, "EpisodeOffsetBox").Text = "-10";
            Get<ToggleSwitch>(dialog!, "FromHistoryToggle").IsOn = true;
            Call(dialog!, "OnFromHistoryChanged", Get<ToggleSwitch>(dialog!, "FromHistoryToggle"), new RoutedEventArgs());
            Require(Get<Grid>(dialog!, "IdentityGrid").Visibility == Visibility.Collapsed, "复用历史没有收起手填识别区");
            await SaveProbeScreenAsync("mp-manual-history-screen.png");
            Get<ToggleSwitch>(dialog!, "FromHistoryToggle").IsOn = false;
            Call(dialog!, "OnFromHistoryChanged", Get<ToggleSwitch>(dialog!, "FromHistoryToggle"), new RoutedEventArgs());
            Require(Get<TextBox>(dialog!, "EpisodeFormatBox").Text == "第{ep}集" &&
                Get<TextBox>(dialog!, "EpisodeOffsetBox").Text == "-10", "关闭复用历史丢失手填内容");
            Get<ComboBox>(dialog!, "EpisodeGroupBox").Text = "group-1";
            Invoke("PreviewButton");
            await UntilAsync(() => server.Previews == 1 && !Get<ProgressRing>(dialog!, "PreviewRing").IsActive);
            Require(!Get<Button>(dialog!, "ReorganizeButton").IsEnabled && !Get<Button>(dialog!, "QueueButton").IsEnabled,
                "未经明确确认即可提交");
            Require(Get<Border>(dialog!, "ConfirmationPanel").Visibility == Visibility.Visible, "完整预览没有同窗确认");
            Get<CheckBox>(dialog!, "ConfirmChanges").IsChecked = true;
            Require(Get<Button>(dialog!, "ReorganizeButton").IsEnabled, "核对确认后仍不能提交");
            await SaveProbeScreenAsync("mp-manual-preview-screen.png");
            Get<TextBox>(dialog!, "PartBox").Text = "part1";
            await UntilAsync(() => !Get<Button>(dialog!, "ReorganizeButton").IsEnabled && !Get<Button>(dialog!, "QueueButton").IsEnabled);
            Require(!Get<Button>(dialog!, "ReorganizeButton").IsEnabled && !Get<Button>(dialog!, "QueueButton").IsEnabled,
                "编辑表单未撤销旧预览确认");
            dialog!.Hide();
            await showing!;
            Require(server.Writes == 0, "取消窗口发送了写请求");
            await ShowAsync();
            Invoke("PreviewButton");
            await UntilAsync(() => server.Previews == 2 && !Get<ProgressRing>(dialog!, "PreviewRing").IsActive);
            Get<CheckBox>(dialog!, "ConfirmChanges").IsChecked = true;
            Invoke("QueueButton");
            await UntilAsync(() => server.Writes == 1 && !Get<ProgressRing>(dialog!, "PreviewRing").IsActive);
            Require(server.Background && Get<InfoBar>(dialog!, "Problem").Severity == InfoBarSeverity.Success,
                "入队没有提交到假服务器或缺回执");
            Require(!Get<Button>(dialog!, "QueueButton").IsEnabled && !Get<Button>(dialog!, "PreviewButton").IsEnabled,
                "已提交窗口重新开放了重复请求");
            Require(fixture.Actions.Plays == 0, "手动整理探针不应播放");
        }
        finally
        {
            dialog?.Hide();
            ThemeHost.Apply(originalTheme);
            ResizeInspect(1420, 980);
        }

        async Task ShowAsync()
        {
            dialog = new MoviePilotReorganizeDialog(service, new MoviePilotTransferContext("离线测试剧集", "电视剧", "123", 1, "01",
                [new("第一集", ManualOrganizeServer.Target)]))
            { XamlRoot = _root.XamlRoot };
            showing = dialog.ShowAsync().AsTask();
            await UntilAsync(() => showing.IsCompleted || !Get<bool>(dialog, "_busy") &&
                (Get<MoviePilotTransferOptions?>(dialog, "_options") is not null || Get<InfoBar>(dialog, "Problem").IsOpen));
            if (showing.IsCompleted) await showing;
            Require(!Get<InfoBar>(dialog, "Problem").IsOpen, Get<InfoBar>(dialog, "Problem").Message);
            await LayoutAsync(dialog);
        }
        void Invoke(string name)
        {
            var button = Get<Button>(dialog!, name);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        }
    }

    private sealed class ManualOrganizeServer : HttpMessageHandler
    {
        internal const string Source = "/downloads/Fixture.Series.S01E01.1080p.mkv";
        internal const string Target = "/library/Fixture/Season 01/Fixture.S01E01.mkv";
        internal int Previews { get; private set; }
        internal int Writes { get; private set; }
        internal bool Background { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "手动整理探针拒绝真实网络");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture" }, envelope: false);
            if (path.EndsWith("storage/options", StringComparison.Ordinal)) return Json(new[] { new { name = "本地", type = "local" } });
            if (path.EndsWith("storage/directories", StringComparison.Ordinal)) return Json(new[] { new
            {
                library_storage = "local", library_path = "/library", transfer_type = "softlink", scraping = true,
                library_type_folder = true, library_category_folder = false
            } });
            if (path.EndsWith("media/source", StringComparison.Ordinal)) return Json(new[]
            {
                new { name = "TheMovieDb", media_source = "themoviedb", media_types = new[] { "电影", "电视剧" } },
                new { name = "豆瓣", media_source = "douban", media_types = new[] { "电影", "电视剧" } }
            });
            if (path.EndsWith("system/global/user", StringComparison.Ordinal)) return Json(new { RECOGNIZE_SOURCE = "douban" });
            if (path.EndsWith("history/transfer", StringComparison.Ordinal)) return Json(new
            {
                total = 1,
                list = new[] { new
            {
                id = 1859, title = "离线测试剧集", type = "电视剧", status = true, mode = "softlink", media_source = "themoviedb",
                media_id = "123", seasons = "S01", episodes = "E01", date = "2026-10-28 10:00:00", src = Source, dest = Target,
                src_fileitem = new { storage = "local", type = "file", path = Source },
                dest_fileitem = new { storage = "local", type = "file", path = Target }
            } }
            });
            if (path.EndsWith("transfer/manual/target-path", StringComparison.Ordinal)) return Json(new
            { target_storage = "local", target_path = "/library", transfer_type = "softlink", scrape = true, library_type_folder = true, library_category_folder = false });
            if (path.Contains("media/groups/", StringComparison.Ordinal)) return Json(new[]
            { new { id = "group-1", name = "播出顺序", group_count = 1, episode_count = 12 } });
            if (path.EndsWith("storage/list", StringComparison.Ordinal)) return Json(new[] { new { storage = "local", type = "file", path = Source } });
            if (path.EndsWith("transfer/manual", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Require(body.RootElement.GetProperty("logid").GetInt64() == 1859, "手动整理未提交原记录编号");
                var preview = body.RootElement.GetProperty("preview").GetBoolean();
                if (preview) Previews++; else { Writes++; Background = request.RequestUri.Query == "?background=true"; }
                return Json(new { items = new[] { new { source = Source, target = "/library/Fixture/Season 01/Fixture.S01E02.mkv", success = true, state = preview ? "preview" : "accepted" } } });
            }
            throw new InvalidOperationException("未登记的手动整理请求：" + path);
        }
        private static HttpResponseMessage Json(object data, bool envelope = true) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(envelope ? new { success = true, data } : data), Encoding.UTF8, "application/json") };
    }
}
