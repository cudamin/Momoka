using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>连续改变真实窗口尺寸，跨过紧凑断点和背景裁切线；只使用匿名假传输。</summary>
    private async Task DetailResizeAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        if (_window!.IsMaximized) _window.ToggleMaximize();
        await SettleAsync();
        var series = Series();
        series.Overview = ResponsiveOverview;
        var episode = Episode("a");
        episode.MediaSources =
        [
            new()
            {
                Id = "offline",
                Container = "mkv",
                MediaStreams =
                [
                    new() { Index = 0, Type = "Video", Codec = "h264", Width = 1920, Height = 1080 },
                    new() { Index = 1, Type = "Audio", DisplayTitle = "国语 · AAC 2.0", IsDefault = true },
                    new() { Index = 2, Type = "Subtitle", DisplayTitle = "简体中文", IsDefault = true }
                ]
            }
        ];
        var movie = new EmbyItem
        {
            Id = "movie",
            Name = "离线电影 · 连续缩放",
            Type = EmbyItemType.Movie,
            Overview = ResponsiveOverview,
            MediaSources = episode.MediaSources
        };
        var season = Season("a");
        season.Overview = ResponsiveOverview;
        var poster = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync(1600, 900);
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
            {
                var bytes = path.Contains("/Images/Primary", StringComparison.Ordinal) ? poster : backdrop;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
            if (path.EndsWith("/Items/show", StringComparison.Ordinal)) return Task.FromResult(Json(series));
            if (path.EndsWith("/Items/movie", StringComparison.Ordinal)) return Task.FromResult(Json(movie));
            if (path.EndsWith("/Items/a", StringComparison.Ordinal)) return Task.FromResult(Json(season));
            if (path.EndsWith("/Episodes", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = new[] { episode } }));
            return Task.FromResult(FakeTransport.Standard(request));
        };

        var readings = new List<string>();
        foreach (var subject in new[] { series, movie, season })
            foreach (var artwork in new[] { "none", "backdrop", "poster" })
            {
                var item = subject;
                foreach (var withArt in new[] { series, movie, season })
                {
                    withArt.BackdropImageTags.Clear();
                    withArt.ImageTags.Clear();
                    if (artwork != "none") withArt.BackdropImageTags.Add("fixture");
                    if (artwork == "poster") withArt.ImageTags["Primary"] = "fixture";
                }
                ResizeProbeClient(1280, 900);
                var host = new Grid { Margin = new Thickness(0, 68, 0, 0) };
                var frame = new Frame();
                host.Children.Add(frame);
                _root.Children.Add(host);
                try
                {
                    frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
                    var page = (DetailPage)frame.Content;
                    page.AttachWindow(_window!);
                    var tail = Get<StackPanel>(page, "TailContent");
                    var overview = Get<StackPanel>(page, "OverviewPanel");
                    var pickers = Get<StackPanel>(page, "PickerPanel");
                    var changes = 0;
                    page.ViewModel.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(ViewModels.DetailViewModel.TailIntrinsic)) return;
                        changes++;
                        if (changes < 180)
                            Log.Debug("详情缩放探针", $"{artwork} #{changes}: 宽 {page.ViewModel.PageWidth:0.###}，"
                                + $"内容 {page.ViewModel.TailIntrinsic:0.#####}，收 {page.ViewModel.Squeeze:0.##}，"
                                + $"尾 {tail.ActualHeight:0.#####}，垫 {tail.Padding.Top:0.#####}，"
                                + $"距 {tail.Spacing:0.#####}，面板 {pickers.Padding.Top:0.#####}/{overview.Padding.Top:0.#####}");
                    };
                    await UntilAsync(() => page.IsReady);
                    if (artwork != "none") await UntilAsync(() => page.ViewModel.HeroImage is not null);
                    if (artwork == "poster") await UntilAsync(() => page.ViewModel.StillImage is not null);
                    await LayoutAsync(page);
                    var compactSeen = false;
                    var wideSeen = false;
                    for (var round = 0; round < 2; round++)
                    {
                        for (var step = 0; step <= 64; step++)
                        {
                            var width = round == 0 ? 760 + step * 9 : 1336 - step * 9;
                            var height = round == 0 ? 760 + step * 2 : 888 - step * 2;
                            ResizeProbeClient(width, height);
                            await Task.Delay(16);
                            page.UpdateLayout();
                            compactSeen |= page.ViewModel.IsCompact;
                            wideSeen |= !page.ViewModel.IsCompact;
                        }
                    }
                    Require(compactSeen && wideSeen, $"{item.Type} {artwork} 连续缩放没有真正跨过宽窄断点");
                    await ResizeResponsiveAsync(page, 1280, 900);
                    await LayoutAsync(page);
                    var settledChanges = changes;
                    for (var settle = 0; settle < 12; settle++)
                    {
                        await Task.Delay(16);
                        page.UpdateLayout();
                    }
                    Require(changes == settledChanges, $"{item.Type} {artwork} 窗口停止后内容高仍在反复更新");
                    // 固定窗口下，内容高不能混入响应式父级留白的取整误差。
                    var intrinsic = new[] { pickers, Get<StackPanel>(page, "CompactBlock"), overview }
                        .Where(panel => panel.Visibility == Visibility.Visible)
                        .Sum(panel =>
                        {
                            var children = panel.Children.Where(child => child.Visibility == Visibility.Visible).ToList();
                            return children.Sum(child => child.DesiredSize.Height)
                                + Math.Max(0, children.Count - 1) * panel.Spacing;
                        });
                    Require(Math.Abs(page.ViewModel.TailIntrinsic - intrinsic) < 0.01,
                        $"{item.Type} {artwork} 尾部内容混入留白取整误差：{page.ViewModel.TailIntrinsic:0.#####} 对 {intrinsic:0.#####}");
                    Require(double.IsFinite(page.ViewModel.HeroHeight) && page.ViewModel.HeroHeight > 0,
                        $"{artwork} 缩放后头部尺寸无效");
                    Require(Math.Abs(tail.ActualWidth - page.ViewModel.PageWidth) < 1,
                        $"{artwork} 缩放后尾部宽度没有跟上视口");
                    readings.Add($"{item.Type} {artwork}: 130 次窗口缩放后布局完成，尾部内容更新 {changes} 次");
                    File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-resize.txt"), readings);
                    if (artwork == "poster") await SaveProbeScreenAsync($"detail-resize-{item.Type}-1280.png");
                }
                finally
                {
                    if (frame.Content is DetailPage page) page.ViewModel.Cancel();
                    _root.Children.Remove(host);
                }
            }
        File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-resize.txt"), readings);
    }
}
