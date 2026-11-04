using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Media;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task PerformanceWarmTimingAsync(Fixture fixture, Frame frame)
    {
        var library = new EmbyItem
        {
            Id = "timing-library",
            Name = "匿名媒体库",
            Type = EmbyItemType.CollectionFolder,
            CollectionType = "movies"
        };
        fixture.Session.Capture().Bind(library);
        fixture.Settings.Ui.ShowHomeBanner = false;
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, [library], false));
        foreach (var row in fixture.Settings.Ui.HomeRows) row.Visible = row.Key == HomeLayout.Resume;
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal)
                || path.EndsWith("/Items", StringComparison.Ordinal)) return Task.FromResult(Items(40, 40));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var path = Path.Combine(_options.Paths.LogDirectory, "navigation-warm-timing.ndjson");
        using var sink = new NavigationTimingFileSink(path);
        NavigationTiming.Start(new NavigationTimingRecorder(sink));
        var expected = new List<(string Run, string Name, int Count)>();
        var warmRuns = new List<string>();
        try
        {
            foreach (var home in new[] { false, true })
            {
                for (var visit = 0; visit < 2; visit++)
                {
                    var run = NavigationTiming.Click(home ? "probe-home" : "probe-library")!;
                    Require(frame.Navigate(home ? typeof(HomePage) : typeof(LibraryPage), home
                        ? new HomeRequest(fixture.Services, [library], null)
                        : LibraryRequest.For(fixture.Services, library)), "暖恢复导航失败");
                    if (home)
                    {
                        var page = (HomePage)frame.Content!;
                        await UntilAsync(() => page.IsReady && page.HomeRows.Count > 0);
                        await LayoutAsync(page);
                        if (visit == 1)
                        {
                            var count = page.ViewModel.Shelves.Sum(shelf => shelf.Cards.Count);
                            expected.Add((run.RunId, "home-restore", count));
                            expected.Add((run.RunId, "home-cards", count));
                            warmRuns.Add(run.RunId);
                            // A later ambient click must not take ownership of this page's queued layout.
                            NavigationTiming.Click("probe-other");
                            Call(page, "ForceLayout");
                        }
                    }
                    else
                    {
                        var page = (LibraryPage)frame.Content!;
                        await UntilAsync(() => page.ViewModel.IsReady && page.ViewModel.Cards.Count == 40);
                        await LayoutAsync(page);
                        if (visit == 1)
                        {
                            expected.Add((run.RunId, "library-restore", 40));
                            expected.Add((run.RunId, "library-cards", 40));
                            warmRuns.Add(run.RunId);
                        }
                    }
                    ((IShellContent)frame.Content!).Release();
                    frame.Content = null;
                    frame.BackStack.Clear();
                }
            }

            var bytes = await FixtureImageAsync(1280, 720);
            var hero = NavigationTiming.Click("probe-hero")!;
            var pixels = await HeroPictureLoader.DecodePixelsAsync(bytes, 1280, CancellationToken.None)
                ?? throw new InvalidOperationException("匿名背景图未解码");
            var picture = await HeroPictureLoader.RenderAsync(pixels, 6, CancellationToken.None);
            Require(picture is { PixelWidth: 1280, PixelHeight: 720 }, "背景处理改变尺寸");
            expected.Add((hero.RunId, "hero-pixels", 1280 * 720 * 4));
            expected.Add((hero.RunId, "hero-copy", 1280 * 720 * 4));
            expected.Add((hero.RunId, "hero-blur", 6));
            expected.Add((hero.RunId, "hero-xaml", 1280 * 720));

            sink.Flush();
            sink.Dispose();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var events = File.ReadAllLines(path).Select(line => JsonSerializer.Deserialize<NavigationTimingEvent>(line, options)).ToArray();
            foreach (var item in expected) PerformanceProbeRules.TimingSpan(events, item.Run, item.Name, item.Count);
            Require(warmRuns.All(id => events.Any(entry => entry.RunId == id && entry.Stage == "snapshot-hit")),
                "第二次导航没有命中快照");
            Require(events.All(entry => entry.Scenario != "probe-other" || !entry.Stage.StartsWith("home-", StringComparison.Ordinal)),
                "上一页布局借用了后续点击的运行");
            Require(events.Any(entry => entry.RunId == warmRuns[1] && entry.Stage == "home-force-layout-end"),
                "真实页面回调缺少原运行归属");
            await WritePerformanceEvidenceAsync("performance-warm-timing.json", new
            {
                spans = expected.Count,
                warmVisits = warmRuns.Count,
                oldPageKeptRun = true,
                heroWidth = picture!.PixelWidth,
                heroHeight = picture.PixelHeight,
                realNetwork = false,
                mediaLoaded = false
            });
            await PerformanceHeroCacheAsync(fixture, frame);
        }
        finally
        {
            NavigationTiming.Stop();
            if (frame.Content is IShellContent content) content.Release();
            frame.Content = null;
            frame.BackStack.Clear();
        }
    }
}
