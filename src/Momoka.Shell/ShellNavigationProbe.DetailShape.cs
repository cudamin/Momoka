using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task PerformanceDetailShapeAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var frame = new Frame();
        _root.Children.Add(frame);
        var readings = new List<object>();
        try
        {
            foreach (var scenario in new[] { "preview", "stub", "corrected" })
            {
                ResizeProbeClient(1280, 900);
                var seed = new EmbyItem
                {
                    Id = "shape-" + scenario,
                    Name = "详情版式回归",
                    Type = EmbyItemType.Movie,
                    IsStub = scenario == "stub",
                    Overview = "主体类型相同时保留版式，完整详情与刷新仍更新可见内容。"
                };
                var detailRequested = Pending<bool>();
                var release = Pending<bool>();
                fixture.Transport.Reply = async (request, token) =>
                {
                    if (!request.RequestUri!.AbsolutePath.EndsWith("/Items/" + seed.Id, StringComparison.Ordinal))
                        return FakeTransport.Standard(request);
                    detailRequested.TrySetResult(true);
                    await release.Task.WaitAsync(token);
                    return Json(seed);
                };
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, seed)), "详情 Frame 导航失败");
                var page = (DetailPage)frame.Content!;
                var shapeNotices = 0;
                void Changed(object? sender, PropertyChangedEventArgs args)
                {
                    if (args.PropertyName == "ContinueVisibility") shapeNotices++;
                }
                page.ViewModel.PropertyChanged += Changed;
                try
                {
                    await detailRequested.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    if (scenario == "corrected") seed.Type = EmbyItemType.Episode;
                    release.TrySetResult(true);
                    await UntilAsync(() => page.IsReady);
                    await LayoutAsync(page);
                    var expected = scenario == "preview" ? 0 : 1;
                    Require(shapeNotices == expected,
                        $"{scenario} 完整主体版式通知 {shapeNotices}，应为 {expected}");

                    var beforeRefresh = shapeNotices;
                    seed.Name = "刷新后的详情标题";
                    await page.ViewModel.ReloadAsync();
                    await LayoutAsync(page);
                    Require(shapeNotices == beforeRefresh, "同类型刷新重复发布版式通知");
                    var title = Get<TextBlock>(page, "TitleText");
                    Require(title.Text == seed.Name, "去重后真实标题绑定没有刷新");
                    Require(title.Text != "详情版式回归", "标题更新检查未能区分旧值");

                    foreach (var width in new[] { 1280, 760 })
                    {
                        ResizeProbeClient(width, 900);
                        await LayoutAsync(page);
                        var hero = Get<Border>(page, "HeroCardTop");
                        Require(hero.Visibility == page.ViewModel.HeroCardVisibility, "宽窄切换后集页面板绑定失效");
                        Require((hero.Visibility == Visibility.Visible)
                                == (scenario == "corrected" && width >= DetailHero.CompactFloor),
                            "存根、类型更正或窗口断点后的面板形态错误");
                        await SaveProbeScreenAsync($"performance-detail-shape-{scenario}-{width}-screen.png");
                    }
                    readings.Add(new { scenario, completeShapeNotices = expected, refreshShapeNotices = shapeNotices - beforeRefresh, titleUpdated = true });
                }
                finally
                {
                    release.TrySetResult(true);
                    page.ViewModel.PropertyChanged -= Changed;
                    page.Release();
                    frame.Content = null;
                    frame.BackStack.Clear();
                }
            }
            Require(fixture.Actions.Plays == 0, "详情检查误触发播放");
            await WritePerformanceEvidenceAsync("performance-detail-shape.json", readings);
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            frame.BackStack.Clear();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
