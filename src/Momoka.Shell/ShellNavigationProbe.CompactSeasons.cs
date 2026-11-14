using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotCompactSeasonsAsync()
    {
        void Trace(string stage) => File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "mp-compact-progress.txt"),
            $"{DateTimeOffset.Now:O} {stage}\n");
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync(), CompactEpisodes = true };
        server.Items[4]["total_episode"] = 8;
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        page.Attach(new(service, subscription));
        try
        {
            Trace("attached");
            var vm = page.ViewModel;
            await UntilAsync(() => vm.IsReady && page.MediaViewModel.IsReady && vm.Episodes.Count == 8);
            Trace("ready");
            var seasons = Get<MoviePilotMediaSeasonsView>(page, "MediaSeasons");
            // 骨架收起后的第一拍先让页面排定，再请求把季列表滚进视野；加载占位与正文同拍时
            // StartBringIntoView 会在布局回路里反复改视口，实测能把 UI 线程打成 LayoutCycle。
            await LayoutAsync(page);
            Trace("first layout");
            seasons.StartBringIntoView();
            await LayoutAsync(page);
            Trace("first bring");
            seasons.ShowSubscriptionFiles();
            Trace("show files");
            var rows = vm.Episodes;
            Require(vm.Episodes[0].RowTitle == "第 1 集", "默认集名重复显示集号");
            Require(vm.Episodes.All(row => row.Artwork is null && row.DescriptionVisibility == Visibility.Collapsed), "无图无简介夹具不正确");
            foreach (var width in new[] { 1240, 760, 600 })
            {
                Trace($"width {width} start");
                ResizeProbeClient(width, 1000);
                await UntilAsync(() => Math.Abs(page.ActualWidth - width) < 2);
                Trace($"width {width} resized");
                await LayoutAsync(page);
                Trace($"width {width} layout");
                seasons.StartBringIntoView();
                await LayoutAsync(page);
                Trace($"width {width} bring");
                var list = Descendants(seasons).OfType<ItemsRepeater>().Single(repeater => repeater.Name == "EpisodeStatistics" && ReferenceEquals(repeater.ItemsSource, rows));
                var first = (FrameworkElement)list.GetOrCreateElement(0);
                var last = (FrameworkElement)list.GetOrCreateElement(7);
                await LayoutAsync(page);
                Trace($"width {width} elements");
                ValidateCompactEpisode(first);
                Require(last.TransformToVisual(first).TransformPoint(default).Y + last.ActualHeight <= 600,
                    "八集无图列表仍占用超过 600 DIP 高度");
                Require(!Descendants(first).OfType<Button>().Any(button => button.Visibility == Visibility.Visible), "未播出且未入库仍显示播放/搜索");
                await SaveProbeScreenAsync($"mp-compact-seasons-{width}-screen.png");

                // 同一几何检查必须能抓住固定高度回归，不能只验证新代码恰好写了什么值。
                if (width == 600)
                {
                    Trace("anti-test start");
                    var minimum = first.MinHeight;
                    first.MinHeight = 200;
                    await LayoutAsync(page);
                    Trace("anti-test layout");
                    var rejected = false;
                    try { ValidateCompactEpisode(first); }
                    catch (InvalidOperationException) { rejected = true; }
                    finally { first.MinHeight = minimum; }
                    Require(rejected, "紧凑行检查没有拒绝固定大高度反例");
                    await LayoutAsync(page);
                }
            }
            vm.FilterIndex = 1;
            Trace("filter set");
            await LayoutAsync(page);
            Trace("filter layout");
            var season = page.MediaViewModel.Seasons.Single(row => row.Files is not null);
            Require(ReferenceEquals(rows, vm.Episodes) && rows.Count == 0 && season.FilesEmptyNotice == "当前范围内暂无已入库的集。",
                "筛选零结果没有保留集合并显示空态");
            Require(Descendants(seasons).OfType<TextBlock>().Any(text => text.Text == season.FilesEmptyNotice && text.Visibility == Visibility.Visible), "筛选空态没有渲染");
            var other = page.MediaViewModel.Seasons.First(row => row.Files is null);
            other.Expanded = true;
            Trace("other expanded");
            await UntilAsync(() => other.Episodes.Count == 8);
            await LayoutAsync(page);
            Trace("other layout");
            var hiddenFilter = Descendants(seasons).OfType<ComboBox>().First(box => box.Name == "EpisodeFilter"
                && box.Tag is ViewModels.MoviePilotMediaSeasonRow row && row.Files is null);
            Trace("hidden filter found");
            hiddenFilter.SelectedIndex = 2;
            Trace("hidden filter 2");
            hiddenFilter.SelectedIndex = 0;
            Trace("hidden filter 0");
            Require(vm.FilterIndex == 1, "其他季的隐藏筛选器覆盖了当前订阅的显示范围");
            await SaveProbeScreenAsync("mp-compact-seasons-empty-screen.png");
            Trace("empty screenshot");
            vm.FilterIndex = 0;
            await LayoutAsync(page);
            Trace("filter restored");
            Require(rows.Count == 8 && season.FilesEmptyNoticeVisibility == Visibility.Collapsed, "恢复全部后空态未收起");

            await LayoutAsync(page);
            var generic = Descendants(seasons).OfType<ItemsRepeater>().Single(repeater => repeater.Name == "MediaEpisodeList" && ReferenceEquals(repeater.ItemsSource, other.Episodes));
            Trace("generic found");
            generic.StartBringIntoView();
            await LayoutAsync(page);
            Trace("generic bring");
            var genericFirst = (FrameworkElement)generic.GetOrCreateElement(0);
            Require(genericFirst.ActualHeight <= 100 && other.Episodes[0].ArtworkVisibility == Visibility.Collapsed, "普通季的无图行仍保留大图片空位");
            await SaveProbeScreenAsync("mp-compact-media-episodes-screen.png");
            Trace("generic screenshot");
            other.Expanded = false;
            Trace("other collapsed");

            server.CompactEpisodes = false;
            server.Items[4]["total_episode"] = 4;
            server.FailedImageUrl = "https://poster.invalid/episode-1.png";
            await vm.ReloadAsync();
            Trace("reload images");
            await PrepareSubscriptionImagesAsync(seasons, rows, 0, 1);
            Trace("prepare images");
            void TraceImages(string stage)
            {
                var list = Descendants(seasons).OfType<ItemsRepeater>().FirstOrDefault(repeater => repeater.Name == "EpisodeStatistics" && ReferenceEquals(repeater.ItemsSource, rows));
                File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "mp-compact-image-trace.txt"),
                    $"{DateTimeOffset.Now:O} {stage} rows={rows.Count} filter={vm.FilterIndex} expanded={season.Expanded} row0={list?.TryGetElement(0) is not null} row1={list?.TryGetElement(1) is not null} image0={rows.ElementAtOrDefault(0)?.Artwork is not null} image1={rows.ElementAtOrDefault(1)?.Artwork is not null} requests={string.Join('|', server.ImageRequests.Where(url => url.Contains("episode-", StringComparison.Ordinal)))}\n");
            }
            TraceImages("before-wait");
            try { await UntilAsync(() => vm.Episodes[1].Artwork is not null && server.ImageRequests.Contains(server.FailedImageUrl)); }
            finally { TraceImages("after-wait"); }
            await SettleAsync();
            Require(vm.Episodes[0].ArtworkVisibility == Visibility.Collapsed && vm.Episodes[1].ArtworkVisibility == Visibility.Visible,
                "图片失败没有保持文字行，或相邻有图行未恢复");
            await SaveProbeScreenAsync("mp-compact-seasons-image-failed-screen.png");
            server.FailedImageUrl = null;
            await vm.ReloadAsync();
            await PrepareSubscriptionImagesAsync(seasons, rows, 0);
            await UntilAsync(() => vm.Episodes[0].Artwork is not null);
            Require(vm.Episodes[0].ArtworkVisibility == Visibility.Visible, "刷新成功后图片没有恢复");
            Require(server.Writes.Count == 0, "查看或筛选季列表触发了写入");
        }
        finally { page.Release(); _root.Children.Remove(page); ResizeInspect(1420, 980); }
    }

    private static void ValidateCompactEpisode(FrameworkElement element)
    {
        var picture = Descendants(element).OfType<Border>().Single(border => border.Name == "EpisodeArtwork");
        var grid = (Grid)picture.Parent;
        var content = (StackPanel)grid.Children[1];
        Require(picture.Visibility == Visibility.Collapsed && element.ActualHeight <= 100, "无图行没有收紧到文字高度");
        Require(content.TransformToVisual(grid).TransformPoint(default).X < 1 && Grid.GetRow(content) == 0,
            "图片隐藏后仍残留左侧列或上方空行");
    }

    private static async Task PrepareSubscriptionImagesAsync(MoviePilotMediaSeasonsView seasons,
        System.Collections.IEnumerable rows, params int[] indices)
    {
        seasons.StartBringIntoView();
        await LayoutAsync(seasons);
        var list = Descendants(seasons).OfType<ItemsRepeater>().Single(repeater =>
            repeater.Name == "EpisodeStatistics" && ReferenceEquals(repeater.ItemsSource, rows));
        foreach (var index in indices)
        {
            var element = (FrameworkElement)list.GetOrCreateElement(index);
            element.StartBringIntoView();
            await LayoutAsync(element);
            ScrollViewer? viewport = null;
            for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not ScrollViewer scroll) continue;
                viewport = scroll;
                break;
            }
            Require(viewport is not null, "订阅图片目标行没有滚动视口");
            var targetViewport = viewport!;
            await UntilAsync(() =>
            {
                var point = element.TransformToVisual(targetViewport).TransformPoint(default);
                return element.ActualWidth > 0 && element.ActualHeight > 0 && point.X >= -1 && point.Y >= -1
                    && point.X + element.ActualWidth <= targetViewport.ActualWidth + 1
                    && point.Y + element.ActualHeight <= targetViewport.ActualHeight + 1;
            });
        }
    }
}
