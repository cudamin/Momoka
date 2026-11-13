using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Diagnoses production ScrollToInfo with a fixed small table, without real media/configuration.</summary>
    private async Task InfoTableAsync(bool wrongOffset = false)
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var poster = await FixtureImageAsync(400, 600);
        var item = InfoTableProbeFixture.Movie();
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "信息表探针拒绝写入");
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) }
                : path.EndsWith("/Items/info-movie", StringComparison.Ordinal) ? Json(item)
                : path.EndsWith("/Similar", StringComparison.Ordinal) ? Json(new ItemsResult())
                : FakeTransport.Standard(request));
        };
        var frame = new Frame();
        var host = new Grid { Margin = new Thickness(0, 40, 0, 0) };
        host.Children.Add(frame);
        _root.Children.Add(host);
        var evidence = new List<object>();
        try
        {
            foreach (var size in new[] { (Width: 1280, Height: 900), (Width: 760, Height: 760), (Width: 760, Height: 560) })
            {
                ResizeProbeClient(size.Width, size.Height);
                frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
                var page = (DetailPage)frame.Content;
                page.AttachWindow(_window!);
                await UntilAsync(() => page.IsReady && page.ViewModel.InfoRows.Count == 10 && page.ViewModel.HeroImage is not null);
                await LayoutAsync(page);
                var body = Get<ScrollView>(page, "Body");
                var content = (FrameworkElement)body.Content;
                var panel = Get<StackPanel>(page, "InfoPanel");
                var table = Get<ItemsRepeater>(page, "InfoTable");
                Capture("before");
                for (var look = 0; look < 8; look++)
                {
                    page.ScrollToInfo();
                    if (wrongOffset) body.ScrollTo(0, body.ScrollableHeight, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                    await Task.Delay(250);
                    await LayoutAsync(page);
                    Capture("look-" + look);
                }
                if (wrongOffset)
                {
                    // Deliberate real-viewport corruption: move the table outside the viewport, rather than falsifying a count.
                    table.RenderTransform = new TranslateTransform { Y = body.ViewportHeight * 2 };
                    await LayoutAsync(page);
                    Capture("wrong-viewport");
                }
                var (drawn, first) = page.InfoRealised;
                var realized = Enumerable.Range(0, 10).Where(index => table.TryGetElement(index) is not null).ToArray();
                InfoTableProbeFixture.RequireRendered(10, realized, first.Contains('：'));
                Require(drawn == 10, "实际InfoRealised没有统计所有已实现行");
                var tableTop = table.TransformToVisual(body).TransformPoint(new()).Y;
                Require(tableTop + table.ActualHeight > 0 && tableTop < body.ViewportHeight, "媒体表不在可见视口");
                Require(fixture.Actions.Plays == 0, "信息表定位意外起播");
                await SaveProbeScreenAsync($"info-table-{size.Width}-{size.Height}-screen.png");
                page.Release();

                void Capture(string phase)
                {
                    var rows = Enumerable.Range(0, 10).Select(index =>
                    {
                        var row = table.TryGetElement(index) as FrameworkElement;
                        var inBody = row?.TransformToVisual(body).TransformPoint(new Windows.Foundation.Point());
                        var inContent = row?.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point());
                        return new
                        {
                            index,
                            realized = row is not null,
                            topInViewport = inBody?.Y,
                            topInContent = inContent?.Y,
                            width = row?.ActualWidth,
                            height = row?.ActualHeight,
                            label = page.ViewModel.InfoRows[index].Label
                        };
                    }).ToArray();
                    evidence.Add(new
                    {
                        phase,
                        size.Width,
                        size.Height,
                        body.ViewportHeight,
                        body.ExtentHeight,
                        body.ScrollableHeight,
                        body.VerticalOffset,
                        panelContentY = panel.TransformToVisual(content).TransformPoint(new()).Y,
                        panelViewportY = panel.TransformToVisual(body).TransformPoint(new()).Y,
                        panelHeight = panel.ActualHeight,
                        tableContentY = table.TransformToVisual(content).TransformPoint(new()).Y,
                        tableViewportY = table.TransformToVisual(body).TransformPoint(new()).Y,
                        tableHeight = table.ActualHeight,
                        contentHeight = content.ActualHeight,
                        viewHeight = page.ViewModel.Viewport,
                        realized = rows.Where(row => row.realized).Select(row => row.index).ToArray(),
                        rows
                    });
                    File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "info-table-geometry.json"),
                        JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(host);
        }
    }
}
