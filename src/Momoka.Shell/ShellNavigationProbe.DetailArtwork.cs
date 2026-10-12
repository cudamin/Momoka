using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task DetailArtworkActionsAsync(string type)
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var item = type switch
        {
            EmbyItemType.Series => Series(),
            EmbyItemType.Season => Season("a"),
            _ => new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, Name = "离线测试电影" }
        };
        var episode = Episode("a");
        var target = item.IsPlayable ? item : episode;
        target.RunTimeTicks = TimeSpan.FromMinutes(45).Ticks;
        target.UserData = new() { PlaybackPositionTicks = TimeSpan.FromMinutes(20).Ticks };
        target.MediaSources = [new() { Id = "offline", Name = "离线测试版本", Container = "mkv" }];
        var image = await FixtureImageAsync();
        fixture.Transport.Reply = async (request, token) =>
        {
            // 先让无图的页面落地，再交回播放目标，不能靠图片到达顺带触发布局。
            await Task.Delay(100, token);
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) };
            if (path.EndsWith("/Items/" + item.Id, StringComparison.Ordinal))
                return Json(item);
            if (path.EndsWith("/Episodes", StringComparison.Ordinal)) return Json(new { Items = new[] { episode } });
            return FakeTransport.Standard(request);
        };
        var frame = new Frame();
        var readings = new List<string>();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
            var page = (DetailPage)frame.Content;
            await UntilAsync(() => page.IsReady);
            Require(page.ViewModel.PlayTarget is not null && page.ViewModel.StillImage is null,
                "无图夹具没有取得播放目标，或意外加载了封面");
            foreach (var state in new[] { "none", "backdrop", "poster", "removed" })
            {
                if (state != "none")
                {
                    item.BackdropImageTags.Clear();
                    item.ImageTags.Clear();
                    if (state is "backdrop" or "poster") item.BackdropImageTags.Add("fixture");
                    if (state == "poster") item.ImageTags["Primary"] = "fixture";
                    await page.ViewModel.ReloadAsync();
                    if (state == "poster") await UntilAsync(() => page.ViewModel.StillImage is not null);
                    if (state == "backdrop") await UntilAsync(() => page.ViewModel.HeroImage is not null);
                }
                foreach (var width in new[] { 0, 1280, 1023, 1024 })
                {
                    if (width > 0)
                    {
                        ResizeProbeClient(width, 900);
                        await UntilAsync(() =>
                        {
                            page.UpdateLayout();
                            return Math.Abs(page.ViewModel.PageWidth - width) < 0.6;
                        });
                    }
                    await LayoutAsync(page);
                    Require(width == 0 || Math.Abs(page.ViewModel.PageWidth - width) < 0.6,
                        $"没有到达目标页面宽度 {width}，实际 {page.ViewModel.PageWidth:0.0}");
                    await SaveProbeScreenAsync($"detail-no-art-{type}-{state}-{width}.png");
                    foreach (var suffix in new[] { "PlayButton", "RestartButton" })
                    {
                        var id = (page.ViewModel.IsCompact ? "Compact" : "Column") + suffix;
                        var button = Descendants(page).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == id);
                        var point = button.TransformToVisual(page).TransformPoint(default);
                        var row = (FrameworkElement)VisualTreeHelper.GetParent(button);
                        var read = $"{type} {state} {width} {id}: button={button.Visibility} {button.ActualWidth:0.0}×{button.ActualHeight:0.0}"
                            + $" at {point.X:0.0},{point.Y:0.0}; row={row.ActualWidth:0.0}×{row.ActualHeight:0.0};"
                            + $" stack={Get<StackPanel>(page, "HeroStack").ActualHeight:0.0}; hero={page.ViewModel.HeroHeight:0.0}";
                        readings.Add(read);
                        Require(button.Visibility == Visibility.Visible && button.IsEnabled
                            && button.ActualWidth > 20 && button.ActualHeight > 20
                            && point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= page.ActualWidth + 1
                            && point.Y + button.ActualHeight <= page.ActualHeight + 1
                            && row.ActualHeight >= button.ActualHeight, "播放按钮不可见或被裁切：" + read);
                        var center = button.TransformToVisual(_root).TransformPoint(new(button.ActualWidth / 2, button.ActualHeight / 2));
                        Require(VisualTreeHelper.FindElementsInHostCoordinates(center, _root).Contains(button),
                            "播放按钮位于裁切区域或被遮住：" + read);
                    }
                }
            }
            Require(fixture.Actions.Plays == 0, "布局检查意外请求播放");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, $"detail-no-art-{type}.txt"), readings);
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
