using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotMediaBrowsingAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        using var fixture = CreateFixture();
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        page.Attach(new(service, subscription, Launcher: fixture.Launcher));
        try
        {
            var vm = page.MediaViewModel;
            await UntilAsync(() => vm.IsReady && page.ViewModel.IsReady);
            Require(vm.Detail is not null && vm.Groups.Count == 3 && vm.Seasons.Count == 2, "媒体详情、剧集组或默认季没有载入：" + vm.NoticeMessage);
            Require(vm.Facts.Any(fact => fact.Value == "The Long Journey"), "没有展示真实媒体元数据");
            var groups = vm.Groups;
            var seasons = vm.Seasons;
            await vm.SelectGroupAsync(vm.Groups.Single(row => row.Group.Id == "grp-9"));
            Require(vm.Seasons.Single().Season.Number == 3 && ReferenceEquals(groups, vm.Groups) && ReferenceEquals(seasons, vm.Seasons), "切组没有保留实际季号或集合实例");
            vm.Seasons[0].Expanded = true;
            await UntilAsync(() => vm.Seasons[0].Episodes.Count == 4);
            Require(server.Writes.Count == 0, "浏览剧集组或展开季修改了订阅");
            foreach (var width in new[] { 1240, 760, 360 })
            {
                ResizeInspect(width, 1000);
                await LayoutAsync(page);
                Get<MoviePilotMediaSeasonsView>(page, "MediaSeasons").StartBringIntoView();
                await LayoutAsync(page);
                await SaveUsersFrameAsync(page, $"mp-media-seasons-{width}.png");
            }
            var resources = Get<MoviePilotResourcePanel>(page, "MediaResources");
            var search = Descendants(page).OfType<Button>().First(button => button.Name == "SearchMediaEpisode");
            InvokeSubscriptionButton(search);
            Require(resources.Visibility == Visibility.Visible && Get<ScrollViewer>(page, "DetailScroll").Visibility == Visibility.Collapsed,
                "集搜索没有打开真实资源面板或底层页面仍可交互");
            Require(server.Writes.Count == 0, "打开资源面板触发订阅或下载");
        }
        finally { page.Release(); _root.Children.Remove(page); ResizeInspect(1420, 980); }
    }

    private async Task MoviePilotSeasonSelectionAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var dialog = new MoviePilotSeasonPicker { XamlRoot = _root.XamlRoot };
        dialog.ViewModel.Attach(service, subscription.Media, picker: true);
        ResizeInspect(760, 1000);
        try
        {
            await dialog.ViewModel.ReloadAsync();
            var showing = dialog.ShowAsync();
            await LayoutAsync(dialog);
            var list = Get<ItemsRepeater>(dialog, "SubscribeSeasons");
            var first = (FrameworkElement)list.GetOrCreateElement(0);
            var second = (FrameworkElement)list.GetOrCreateElement(1);
            Descendants(first).OfType<RadioButton>().Single(button => button.Name == "FullSeasonUpgradeMode").IsChecked = true;
            Descendants(second).OfType<ToggleSwitch>().Single().IsOn = true;
            Descendants(second).OfType<RadioButton>().Single(button => button.Name == "EpisodeUpgradeMode").IsChecked = true;
            await LayoutAsync(dialog);
            Require(dialog.ViewModel.Plan().Count == 2 && server.Writes.Count == 0, "季选择或模式草稿提前写入服务器");
            await SaveProbeScreenAsync("mp-subscribe-season-picker-760.png");
            InvokeSubscriptionButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            await showing;
            Require(server.Writes.Count == 2 && server.Items[4]["best_version_full"]!.GetValue<int>() == 1, "已有季没有改为全集洗版");
            var added = server.Items.Values.Single(item => item["id"]!.GetValue<int>() > 5);
            Require(added["season"]!.GetValue<int>() == 2 && added["best_version"]!.GetValue<int>() == 1 && added["best_version_full"]!.GetValue<int>() == 0, "新增季没有使用分集洗版");
            Require(server.WriteBodies[0].Count == 3, "模式变更覆盖了无关订阅字段");
            dialog.ViewModel.Cancel();
            var cancelled = new MoviePilotSeasonPicker { XamlRoot = _root.XamlRoot };
            cancelled.ViewModel.Attach(service, subscription.Media, picker: true);
            await cancelled.ViewModel.ReloadAsync();
            showing = cancelled.ShowAsync();
            await LayoutAsync(cancelled);
            cancelled.ViewModel.Seasons[0].Selected = false;
            ResizeInspect(360, 900); await LayoutAsync(cancelled);
            await SaveProbeScreenAsync("mp-subscribe-season-picker-360.png");
            InvokeSubscriptionButton(Descendants(cancelled).OfType<Button>().Single(button => button.Name == "CloseButton" && button.Content as string == "关闭"));
            await showing;
            cancelled.ViewModel.Cancel();
            Require(server.Writes.Count == 2 && server.Items.ContainsKey(4), "关闭弹窗仍取消了订阅");
        }
        finally { dialog.Hide(); dialog.ViewModel.Cancel(); ResizeInspect(1420, 980); }
    }

    private async Task MoviePilotMediaReadRacesAsync()
    {
        using var server = new SubscriptionProbeServer();
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var vm = new MoviePilotMediaViewModel();
        vm.Attach(service, subscription.Media);
        try
        {
            var exists = Pending<System.Net.Http.HttpResponseMessage>();
            server.PendingExists = exists;
            var loading = vm.ReloadAsync();
            await UntilAsync(() => vm.Seasons.Count == 2);
            var row = vm.Seasons[0];
            await vm.LoadEpisodesAsync(row);
            Require(row.Episodes[0].Exists is null, "入库统计未返回就显示了确定状态");
            exists.SetResult(Json(new { success = true, data = new Dictionary<string, int[]> { ["1"] = [1] } }));
            await loading;
            Require(row.Episodes[0].Exists == true && row.Episodes[1].Exists == false, "入库统计迟到没有更新已展开集");
            server.PendingExists = null;
            var group = Pending<System.Net.Http.HttpResponseMessage>();
            server.PendingGroup = group;
            var old = vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "grp-9"));
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "grp-10"));
            group.SetResult(Json(new[] { new { season_number = 99, episode_count = 4 } }));
            await old;
            Require(vm.GroupId == "grp-10" && vm.Seasons.Single().Season.Number == 7, "旧剧集组迟到覆盖当前组");
            vm.Cancel();
            Require(vm.Seasons.Count == 0 && !vm.CanInteract && server.Writes.Count == 0, "离页后仍保留可操作的旧数据");
        }
        finally { vm.Cancel(); }
    }

    private async Task MoviePilotMediaBackgroundAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var page = new MoviePilotSubscriptionPage();
        var originalTheme = ThemeHost.Current.Id;
        _root.Children.Add(page);
        page.Attach(new(service, subscription));
        try
        {
            ResizeInspect(1240, 1000);
            await UntilAsync(() => page.ViewModel.IsReady && page.MediaViewModel.IsReady && page.MediaViewModel.Backdrop is not null);
            await LayoutAsync(page);
            var backdrop = Get<Border>(page, "DetailBackdropBand");
            Require(double.IsNaN(backdrop.Height) && backdrop.CornerRadius.TopLeft == 0 && backdrop.BorderThickness.Left == 0
                && Math.Abs(backdrop.ActualWidth - page.ActualWidth) < 2 && backdrop.ActualHeight >= page.ActualHeight - 2,
                "背景仍是定高圆角横幅，没有覆盖整个页面");
            var before = backdrop.TransformToVisual(page).TransformPoint(default);
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id); await LayoutAsync(page);
                await SaveUsersFrameAsync(page, $"mp-media-background-{theme.Id}.png");
            }
            var seasons = Get<MoviePilotMediaSeasonsView>(page, "MediaSeasons");
            seasons.StartBringIntoView();
            await LayoutAsync(page);
            var season = page.MediaViewModel.Seasons.Single(row => row.Files is not null);
            var expander = Descendants(seasons).OfType<Expander>().Single(control => control.IsExpanded);
            var header = Descendants(expander).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>()
                .Single(control => control.Name == "ExpanderHeader");
            var summary = Descendants((Grid)expander.Header).OfType<TextBlock>().Single(text => text.Text == page.ViewModel.FileSummary);
            var originalSummary = page.ViewModel.FileSummary;
            foreach (var status in new[] { "正在读取文件统计…", "文件统计读取失败", originalSummary })
            {
                page.ViewModel.FileSummary = status;
                Require(summary.Text == status, "季标题没有同步统计的加载、失败或恢复状态");
            }
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                foreach (var expanded in new[] { false, true })
                {
                    // 操作真实头部控件，验证模板到视图模型的双向绑定；不移动用户的鼠标。
                    header.IsChecked = expanded;
                    await LayoutAsync(page);
                    Require(expander.IsExpanded == expanded && season.Expanded == expanded, "季折叠状态未同步");
                    Require(summary.Text == originalSummary, "折叠切换丢失了入库摘要");
                    Require(header.Background is Microsoft.UI.Xaml.Media.SolidColorBrush headerBrush
                        && headerBrush.Color == ThemeHost.ToColor(theme.Colors.SurfaceAlt)
                        && expander.Background is Microsoft.UI.Xaml.Media.SolidColorBrush contentBrush
                        && contentBrush.Color == headerBrush.Color, "季标题与展开区域没有跟随当前主题");
                    var scroll = Get<ScrollViewer>(page, "DetailScroll");
                    var offset = seasons.TransformToVisual((UIElement)scroll.Content).TransformPoint(default).Y;
                    scroll.ChangeView(null, offset - 16, null, true);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"mp-season-{theme.Id}-{(expanded ? "expanded" : "collapsed")}-screen.png");
                }
            }
            Get<ScrollViewer>(page, "DetailScroll").ChangeView(null, 300, null, true);
            await LayoutAsync(page);
            var after = backdrop.TransformToVisual(page).TransformPoint(default);
            Require(before.Y == after.Y && before.X == after.X, "背景跟着正文滚走");
        }
        finally { page.Release(); _root.Children.Remove(page); ThemeHost.Apply(originalTheme); ResizeInspect(1420, 980); }
    }

    private async Task MoviePilotSeasonPartialSubmitAsync()
    {
        using var server = new SubscriptionProbeServer { FailSubscriptionCreate = true };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var vm = new MoviePilotMediaViewModel();
        vm.Attach(service, subscription.Media, picker: true);
        try
        {
            await vm.ReloadAsync();
            vm.Seasons[0].ModeIndex = 2;
            vm.Seasons[1].Selected = true;
            Require(!await vm.SubmitAsync(), "部分失败被当作整批成功");
            Require(server.Writes.Count == 2 && server.Items[4]["best_version_full"]!.GetValue<int>() == 1
                && vm.SubmitSummary.Contains("已提交", StringComparison.Ordinal) && vm.Submitted, "没有保留已经成功的季或锁住结果不明的季");
            Require(!await vm.SubmitAsync() && server.Writes.Count == 2, "部分失败后重放了整批");
            vm.ReviewConfirmed = true;
            await vm.ReviewAsync();
            Require(vm.Seasons[0].ModeIndex == 2 && !vm.Seasons[1].Selected && !vm.Submitted, "核对后没有按服务器现状重新读取");
            using var owner = new CancellationTokenSource();
            vm.Attach(service, subscription.Media, picker: true, lifetimeToken: owner.Token);
            await vm.ReloadAsync();
            vm.Seasons[1].Selected = true;
            owner.Cancel();
            Require(!await vm.SubmitAsync() && server.Writes.Count == 2, "所属页面已离开，旧季选择仍提交订阅");
        }
        finally { vm.Cancel(); }
    }

    private async Task MoviePilotMediaSubtitlesAsync()
    {
        using var server = new ResourceOptionsServer();
        using var fixture = CreateFixture(server);
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new Momoka.Configuration.MoviePilotCredentials(Momoka.Configuration.PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var panel = new MoviePilotResourcePanel();
        _root.Children.Add(panel);
        try
        {
            panel.Open(service, new MoviePilotMedia { Title = "示例剧", MediaSource = "themoviedb", MediaId = "42", Type = "电视剧", Season = 1, ConnectionStamp = service.ConnectionStamp }, fixture.Launcher, new(Episode: 1, Subtitles: true));
            await panel.ViewModel.SearchAsync();
            Require(panel.ViewModel.Subtitles.Count == 1 && panel.ViewModel.Resources.Count == 0, "字幕没有进入独立的真实结果列表");
            panel.ViewModel.ChooseSubtitlePath = (_, _, _) => Task.FromResult<string?>(null);
            await panel.ViewModel.Subtitles[0].DownloadCommand.ExecuteAsync(null);
            Require(server.SubtitleDownloads.Count == 0, "取消字幕保存仍提交下载");
            panel.ViewModel.ChooseSubtitlePath = (_, _, _) => Task.FromResult<string?>("/subtitles");
            await panel.ViewModel.Subtitles[0].DownloadCommand.ExecuteAsync(null);
            Require(server.SubtitleDownloads.Count == 1 && server.SubtitleDownloads[0].GetProperty("save_path").GetString() == "/subtitles", "字幕下载没有提交实际保存路径");
            await LayoutAsync(panel);
            await SaveUsersFrameAsync(panel, "mp-media-subtitles.png");
            panel.ViewModel.SearchSites.UseDefault = false;
            Require(panel.ViewModel.Subtitles.Count == 0, "更换站点后仍显示已失效字幕");
        }
        finally { panel.Release(); _root.Children.Remove(panel); }
    }
}
