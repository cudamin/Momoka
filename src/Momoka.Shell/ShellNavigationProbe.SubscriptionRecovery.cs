using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotSubscriptionPosterRefreshAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync(), FailedImageUrl = "https://poster.invalid/1.png" };
        var (_, service) = SubscriptionService(server);
        var view = new MoviePilotSubscriptionsView();
        _root.Children.Add(view);
        view.Attach(service, null);
        try
        {
            await UntilAsync(() => view.ViewModel.IsReady);
            await LayoutAsync(view);
            var card = view.ViewModel.Movies[0];
            await UntilAsync(() => server.ImageRequests.Contains(server.FailedImageUrl));
            await SettleAsync();
            var requests = server.ImageRequests.Count;
            var failedRequests = server.ImageRequests.Count(url => url == "https://poster.invalid/1.png");
            Require(card.Poster is null, "失败图片没有保留占位");
            await SettleAsync();
            Require(server.ImageRequests.Count == requests, "失败图片自动循环重试");
            server.FailedImageUrl = null;
            await view.ViewModel.ReloadAsync();
            await UntilAsync(() => card.Poster is not null);
            Require(ReferenceEquals(card, view.ViewModel.Movies[0]), "刷新重建了订阅卡片");
            var retried = server.ImageRequests.Count(url => url == "https://poster.invalid/1.png");
            Require(retried == failedRequests + 1, $"失败图片刷新请求次数错误：刷新前 {failedRequests}，刷新后 {retried}");
            var oldPoster = card.Poster;
            server.Items[1]["poster"] = "https://poster.invalid/replaced.png";
            await view.ViewModel.ReloadAsync();
            await UntilAsync(() => card.Poster is not null && !ReferenceEquals(card.Poster, oldPoster));
            Require(server.ImageRequests.Count(url => url == "https://poster.invalid/replaced.png") == 1, "换图没有使用新的 URL");
            requests = server.ImageRequests.Count;
            await view.ViewModel.ReloadAsync();
            await SettleAsync();
            Require(server.ImageRequests.Count == requests, "成功的图片被刷新反复请求");
        }
        finally { view.Release(); _root.Children.Remove(view); }
    }

    private async Task MoviePilotSubscriptionFilesRecoveryAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var pending = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.Contains("/Episodes", StringComparison.Ordinal)
            ? pending.Task : Task.FromResult(Json(new { Items = new[] { new EmbyItem { Id = "linked-show", Type = EmbyItemType.Series, ProviderIds = new() { ["Tmdb"] = "4" } } } }));
        var scope = fixture.Session.Capture();
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        page.Attach(new(service, subscription, scope, _ => Task.CompletedTask));
        try
        {
            var vm = page.ViewModel;
            await UntilAsync(() => vm.Episodes.Count > 0 && page.MediaViewModel.IsReady);
            var seasons = Get<MoviePilotMediaSeasonsView>(page, "MediaSeasons");
            seasons.StartBringIntoView();
            await LayoutAsync(page);
            var play = Descendants(seasons).OfType<Button>().Single(button => button.Name == "PlaySubscriptionEpisode"
                && ReferenceEquals(button.Command, vm.Episodes[0].PlayCommand));
            var headPlay = Get<Button>(page, "PlayMediaLibraryItem");
            Require(play.Visibility == Visibility.Collapsed && headPlay.Visibility == Visibility.Collapsed, "匹配完成前出现播放入口");
            pending.SetResult(Json(new
            {
                Items = new[] { new EmbyItem { Id = "play-1", Type = EmbyItemType.Episode, ParentIndexNumber = 1,
                IndexNumber = 1, SeriesId = "linked-show", Path = "/library/01.mkv" } }
            }));
            await UntilAsync(() => vm.IsReady);
            Require(vm.Episodes[0].HasPlayableTarget, "没有匹配到假播放条目：" + vm.PlaybackStatus + "；" + vm.NoticeMessage);
            Require(play.Visibility == Visibility.Visible, "匹配完成后原行按钮没有显示");
            Require(headPlay.Visibility == Visibility.Visible && play.IsEnabled, "匹配完成没有更新原播放按钮");
            vm.Working = true;
            Require(play.Visibility == Visibility.Visible && !play.IsEnabled, "忙碌时播放按钮消失或仍允许重复操作");
            vm.Working = false;
            vm.AttachPlayback(null, null);
            Require(play.Visibility == Visibility.Collapsed && headPlay.Visibility == Visibility.Collapsed, "播放能力撤销后入口未隐藏");
            vm.AttachPlayback(scope, _ => Task.CompletedTask);
            Require(play.Visibility == Visibility.Visible, "播放能力恢复后入口未恢复");

            foreach (var width in new[] { 600, 760, 600 })
            {
                ResizeProbeClient(width, 1000);
                await UntilAsync(() => Math.Abs(page.ActualWidth - width) < 2);
                seasons.StartBringIntoView();
                await LayoutAsync(page);
                var picture = Descendants(seasons).OfType<Border>().First(border => border.Name == "EpisodeArtwork");
                picture.StartBringIntoView();
                await LayoutAsync(page);
                var row = (Grid)picture.Parent;
                var content = (StackPanel)row.Children[1];
                var actions = (StackPanel)row.Children[2];
                Require(content.ActualWidth >= (width == 600 ? 250 : 150), "集名、日期和状态被固定海报挤窄");
                Require(Grid.GetRow(content) == (width == 600 ? 1 : 0), "紧凑布局没有随窗口恢复");
                Require(actions.TransformToVisual(row).TransformPoint(default).X + actions.ActualWidth <= row.ActualWidth + 1, "播放或搜索按钮超出行宽");
                await SaveUsersFrameAsync(page, $"mp-subscription-files-recovery-{width}.png");
            }
            vm.FilterIndex = 1;
            var files = vm.Episodes;
            server.PendingGroup = Pending<HttpResponseMessage>();
            server.PendingGroup.SetResult(Json(new[] { new { season_number = 1, episode_count = 4, name = "另一种分季" } }));
            await page.MediaViewModel.SelectGroupAsync(page.MediaViewModel.Groups.Single(group => group.Group.Id == "grp-9"));
            var other = page.MediaViewModel.Seasons.Single();
            Require(other.Files is null && other.MediaEpisodesVisibility == Visibility.Visible && !seasons.HasSubscriptionFiles,
                "另一剧集组的同号季仍装载原订阅统计");
            Require(!page.BuildMoreMenu().Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "文件统计"), "无统计落点仍显示文件统计菜单");
            other.Expanded = true;
            await UntilAsync(() => other.Episodes.Count > 0);
            await page.MediaViewModel.SelectGroupAsync(page.MediaViewModel.Groups.Single(group => group.Group.Id.Length == 0));
            var restored = page.MediaViewModel.Seasons.Single(row => row.Files is not null);
            Require(restored.Expanded && ReferenceEquals(restored.FilesEpisodes, files) && restored.FilesFilterIndex == 1, "切回原组没有恢复统计及筛选");
            Require(server.Writes.Count == 0, "浏览、匹配或切组意外写入订阅");
        }
        finally
        {
            pending.TrySetResult(Json(new { Items = Array.Empty<EmbyItem>() }));
            page.Release(); _root.Children.Remove(page); ResizeInspect(1420, 980);
        }
    }

    private async Task MoviePilotSubscriptionRecreateAsync()
    {
        foreach (var series in new[] { false, true })
        {
            using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
            var (_, service) = SubscriptionService(server);
            var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries == series);
            var page = new MoviePilotSubscriptionPage();
            _root.Children.Add(page);
            page.Attach(new(service, subscription));
            try
            {
                await UntilAsync(() => page.ViewModel.IsReady && page.MediaViewModel.IsReady);
                page.ViewModel.UseConfirm((_, _, _) => Task.FromResult(true));
                await page.ManageSubscriptionAsync(MoviePilotSubscriptionAction.Delete);
                Require(server.Writes.Count == 1 && !server.Items.ContainsKey(subscription.Id), "取消没有仅提交一次");
                Require(page.ViewModel.Deleted && page.ViewModel.DetailStatus == "已取消订阅" && page.SubscriptionVisibility == Visibility.Collapsed, "取消后仍显示旧订阅状态");
                Require(page.MediaViewModel.SubscribeLabel == (series ? "选择订阅季" : "订阅"), "主订阅按钮未同步取消结果");
                Require(page.MediaViewModel.Seasons.All(row => row.Subscription is null), "取消后季仍标记为已订阅");
                Require(((MoviePilotSubscriptionRequest)page.NavigationRequest!).Subscription is null, "返回历史仍保存已取消编号");

                var picker = new MoviePilotMediaViewModel();
                try
                {
                    picker.Attach(service, subscription.Media, picker: true);
                    await picker.ReloadAsync();
                    picker.Seasons[0].Selected = true;
                    Require(await picker.SubmitAsync(), "重新订阅未成功：" + picker.NoticeMessage);
                }
                finally { picker.Cancel(); }
                await page.RefreshSubscriptionStateAsync();
                var current = page.ViewModel.Selected!.Subscription;
                Require(current.Id != subscription.Id && page.ViewModel.CanWriteSelected && !page.ViewModel.Deleted, "重新订阅后仍被旧编号或 Deleted 锁住");
                Require(((MoviePilotSubscriptionRequest)page.NavigationRequest!).Subscription?.Id == current.Id, "返回历史没有更新订阅编号");
                await page.ManageSubscriptionAsync(MoviePilotSubscriptionAction.Pause);
                Require(server.Writes.Count == 3 && server.Writes[^1].EndsWith("/status/" + current.Id, StringComparison.Ordinal), "后续操作没有使用新订阅编号");
            }
            finally { page.Release(); _root.Children.Remove(page); }
        }
    }

    private async Task MoviePilotSubscriptionReviewAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First();
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        page.Attach(new(service, subscription));
        try
        {
            var vm = page.ViewModel;
            await UntilAsync(() => vm.IsReady && page.MediaViewModel.IsReady);
            vm.UseConfirm((_, _, _) => Task.FromResult(true));
            server.UncertainNextWrite = true;
            await page.ManageSubscriptionAsync(MoviePilotSubscriptionAction.Pause);
            Require(vm.NeedsReview(vm.Selected!) && !vm.CanWriteSelected, "不明回执没有锁定写操作");
            await page.RefreshSubscriptionStateAsync();
            var menu = page.BuildMoreMenu().Items.OfType<MenuFlyoutItem>().ToArray();
            Require(menu.Single(item => item.Text == "核对后继续").IsEnabled, "详情菜单缺少可用的核对入口");
            Require(menu.Where(item => item.Text is "恢复订阅" or "重置订阅" or "编辑" or "搜索" or "取消订阅").All(item => !item.IsEnabled), "待核对状态仍开放写操作");
            Require(!Get<Button>(page, "EditSubscription").IsEnabled, "头部编辑没有跟随待核对状态");
            await page.ManageSubscriptionAsync(MoviePilotSubscriptionAction.Reset);
            Require(server.Writes.Count == 1, "结果不明后再次发出了写请求");
            vm.UseConfirm((_, _, _) => Task.FromResult(false));
            await vm.ReviewAsync(vm.Selected!);
            Require(vm.NeedsReview(vm.Selected!), "取消核对却解除了限制");
            vm.UseConfirm((_, _, _) => Task.FromResult(true));
            await vm.ReviewAsync(vm.Selected!);
            Require(vm.CanWriteSelected && server.Writes.Count == 1, "核对没有恢复编辑或擅自重放了操作");
            await page.ManageSubscriptionAsync(MoviePilotSubscriptionAction.Resume);
            Require(server.Writes.Count == 2 && vm.DetailStatus == "订阅中", "核对后的操作未恢复");

            var oldItems = server.Items.Values.Select(item => item.DeepClone()).ToArray();
            var lateList = Pending<HttpResponseMessage>();
            server.PendingList = lateList;
            var oldRefresh = vm.RefreshAfterMediaChangesAsync();
            Require(vm.Busy && !vm.CanWriteSelected, "重定位读取期间仍可写订阅");
            server.PendingList = null;
            server.Items.Remove(subscription.Id);
            await vm.RefreshAfterMediaChangesAsync();
            Require(vm.Deleted, "最新列表未呈现订阅已移除");
            lateList.SetResult(Json(new { success = true, data = oldItems }));
            await oldRefresh;
            Require(vm.Deleted && !vm.CanWriteSelected && server.Writes.Count == 2, "迟到列表恢复了旧订阅或补发操作");
        }
        finally { page.Release(); _root.Children.Remove(page); }
    }
}
