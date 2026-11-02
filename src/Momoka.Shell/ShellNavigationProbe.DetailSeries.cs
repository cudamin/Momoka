using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 剧页上那两件事（2026-11-02 用户令）：
    /// <para>
    /// ①「当剧集只有一季时，在剧页面不显示『全部剧集』卡片」—— 只有一季时那一格只是一张通往本页已经列着的那
    /// 一季的卡，收起的是标题、卡带和它自己的间距，别的卡片照旧。
    /// </para>
    /// <para>
    /// ②「在剧页面的集列表下方复刻一个与集页面『其他信息』卡片一致的卡片」—— 同一块板、同一块牌子、同一行
    /// 代码搭出来的值（类型／工作室／数据库链接）；剧没有文件，所以那张媒体信息表与它下面那条分割线都不出现，
    /// 只剩牌子与那一行。位置在集列表正下方、全部剧季之上。
    /// </para>
    /// <para>
    /// 两种剧各开一页：单季那份同时验「季卡收起」与「信息卡接在集列表下面」，多季那份验「季卡还在、顺序是
    /// 单集→其他信息→全部剧季」。截图 `detail-series-*-{1280,760}.png`，几何读数落 `detail-series-info.txt`。
    /// 全部走假传输，不联网、不播放。
    /// </para>
    /// </summary>
    private async Task DetailSeriesInfoAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();

        var single = SeriesWithInfo("show-one", "离线单季剧集", ["动画", "奇幻"], "Wit Studio");
        var multi = SeriesWithInfo("show-two", "离线多季剧集", ["剧情"], "TBS");
        var singleSeasons = new[] { SeasonOf(single, "one-s1", 1) };
        var multiSeasons = new[] { SeasonOf(multi, "two-s1", 1), SeasonOf(multi, "two-s2", 2) };

        fixture.Transport.Reply = (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            if (path.EndsWith("/Items/" + single.Id, StringComparison.Ordinal)) return Task.FromResult(Json(single));
            if (path.EndsWith("/Items/" + multi.Id, StringComparison.Ordinal)) return Task.FromResult(Json(multi));

            if (path.Contains("/Shows/" + single.Id + "/", StringComparison.Ordinal))
                return Task.FromResult(SeriesPart(path, single, singleSeasons));
            if (path.Contains("/Shows/" + multi.Id + "/", StringComparison.Ordinal))
                return Task.FromResult(SeriesPart(path, multi, multiSeasons));

            return Task.FromResult(FakeTransport.Standard(request));
        };

        var readings = new List<string>();
        try
        {
            // ① 只有一季：季卡整块收起，其他信息卡接在集列表正下方。
            var frame = new Frame();
            _root.Children.Add(frame);
            try
            {
                frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, single));
                var page = (DetailPage)frame.Content;
                await UntilAsync(() => page.IsReady);
                await LayoutAsync(page);

                Require(page.ViewModel.SeasonShelf is { Cards.Count: 1 },
                    $"单季夹具的季卡带不是一张：{page.ViewModel.SeasonShelf?.Cards.Count ?? -1}");
                Require(page.ViewModel.EpisodeShelf is { Cards.Count: > 0 }, "单季夹具没有列出这一季的集");

                var seasonPanel = Get<StackPanel>(page, "SeasonPanel");
                var infoPanel = Get<StackPanel>(page, "InfoPanel");
                var episodePanel = Get<StackPanel>(page, "EpisodePanel");

                Require(seasonPanel.Visibility == Visibility.Collapsed && seasonPanel.ActualHeight == 0,
                    $"只有一季，全部剧季卡还占着位置：{seasonPanel.Visibility}、高 {seasonPanel.ActualHeight:0.#}");
                Require(infoPanel.Visibility == Visibility.Visible && infoPanel.ActualHeight > 0,
                    $"剧页没有其他信息卡：{infoPanel.Visibility}、高 {infoPanel.ActualHeight:0.#}");

                var head = Get<ShelfHead>(page, "InfoHead");
                Require(head.Title == "其他信息" && head.Visibility == Visibility.Visible, $"信息卡的牌子写的是「{head.Title}」");

                // 剧没有文件：那张媒体信息表与它下面那条分割线都不该出现，只剩牌子与那一行。
                Require(page.ViewModel.InfoRows.Count == 0, $"剧页凭空有了 {page.ViewModel.InfoRows.Count} 行媒体信息");
                Require(Get<Border>(page, "OtherInfoDivider").Visibility == Visibility.Collapsed,
                    "剧页没有媒体信息表，却画了那条分割线");

                var line = Get<TextBlock>(page, "OtherInfoLine");
                var text = string.Concat(line.Inlines.Select(InlineText));
                var links = line.Inlines.OfType<Hyperlink>().Count();
                Require(line.Visibility == Visibility.Visible && text.Contains('：'), "其他信息那一行没画出来");
                Require(text.Contains("类型：", StringComparison.Ordinal)
                        && text.Contains("工作室：", StringComparison.Ordinal)
                        && text.Contains("数据库链接：", StringComparison.Ordinal),
                    "剧页那一行没有类型、工作室或数据库链接：" + text);
                Require(!text.Contains("上次播放日期", StringComparison.Ordinal), "剧页多出了只在集页才有的上次播放日期：" + text);
                Require(links == 3, $"屏上 {links} 段数据库链接，模型给的是 TMDB／IMDb／TVDB 三段：{text}");

                var episodeTop = Top(episodePanel, page);
                var infoTop = Top(infoPanel, page);
                Require(infoTop >= episodeTop + episodePanel.ActualHeight - 1,
                    $"其他信息卡没有接在集列表下方：集 {episodeTop:0}–{episodeTop + episodePanel.ActualHeight:0}、"
                        + $"信息卡从 {infoTop:0} 起");

                readings.Add($"单季 1280：集带 {episodePanel.ActualHeight:0}、信息卡 {infoTop:0}–{infoTop + infoPanel.ActualHeight:0}、"
                    + $"季卡 {seasonPanel.Visibility}、可点 {links} 段");

                foreach (var width in new[] { 1280, 760 })
                {
                    ResizeProbeClient(width, 900);
                    await UntilAsync(() =>
                    {
                        page.UpdateLayout();
                        return Math.Abs(page.ViewModel.PageWidth - width) < 0.6;
                    });
                    await LayoutAsync(page);

                    // 停在集列表的顶上：这一帧要看得见「单集 → 其他信息」是上下相接的，只把信息卡顶到视口
                    // 顶端就断了那半边证据。
                    ScrollToPanel(page, episodePanel);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"detail-series-single-season-{width}.png");
                    readings.Add($"单季 {width}：季卡 {seasonPanel.Visibility}（高 {seasonPanel.ActualHeight:0.#}）、"
                        + $"信息卡 {infoPanel.Visibility}（高 {infoPanel.ActualHeight:0.#}）");
                }
            }
            finally
            {
                (frame.Content as IShellContent)?.Release();
                _root.Children.Remove(frame);
            }

            // ② 多季：季卡带照旧，顺序是 单集 → 其他信息 → 全部剧季。
            ResizeProbeClient(1280, 900);
            var second = new Frame();
            _root.Children.Add(second);
            try
            {
                second.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, multi));
                var page = (DetailPage)second.Content;
                await UntilAsync(() => page.IsReady);
                await LayoutAsync(page);

                var seasonPanel = Get<StackPanel>(page, "SeasonPanel");
                var infoPanel = Get<StackPanel>(page, "InfoPanel");
                var episodePanel = Get<StackPanel>(page, "EpisodePanel");

                Require(page.ViewModel.SeasonShelf is { Cards.Count: 2 }, "多季夹具的季卡带不是两张");
                Require(seasonPanel.Visibility == Visibility.Visible && seasonPanel.ActualHeight > 0,
                    "两季的剧页没有了全部剧季卡");
                Require(infoPanel.Visibility == Visibility.Visible && infoPanel.ActualHeight > 0, "多季剧页没有其他信息卡");

                var infoTop = Top(infoPanel, page);
                var seasonTop = Top(seasonPanel, page);
                Require(infoTop >= Top(episodePanel, page) + episodePanel.ActualHeight - 1,
                    "其他信息卡没有接在集列表下方");
                Require(seasonTop >= infoTop + infoPanel.ActualHeight - 1,
                    $"其他信息卡跑到全部剧季后面去了：信息卡 {infoTop:0}–{infoTop + infoPanel.ActualHeight:0}、"
                        + $"季卡从 {seasonTop:0} 起");

                readings.Add($"多季 1280：集带 {episodePanel.ActualHeight:0}、信息卡 {infoTop:0}–{infoTop + infoPanel.ActualHeight:0}、"
                    + $"季卡 {seasonTop:0}–{seasonTop + seasonPanel.ActualHeight:0}");

                foreach (var width in new[] { 1280, 760 })
                {
                    ResizeProbeClient(width, 900);
                    await UntilAsync(() =>
                    {
                        page.UpdateLayout();
                        return Math.Abs(page.ViewModel.PageWidth - width) < 0.6;
                    });
                    await LayoutAsync(page);
                    ScrollToPanel(page, episodePanel);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"detail-series-two-seasons-{width}.png");
                }
            }
            finally
            {
                (second.Content as IShellContent)?.Release();
                _root.Children.Remove(second);
            }

            Require(fixture.Actions.Plays == 0, "布局检查意外请求播放");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-series-info.txt"), readings);
            ResizeInspect(1420, 980);
        }

        static double Top(UIElement element, UIElement root) =>
            element.TransformToVisual(root).TransformPoint(default).Y;

        // 把某一块的顶对到视口顶 —— 和 DetailPage.ScrollToInfo 同一条算法，只是目标由调用方给。
        // 偏移在内容自己的坐标里量，量出来的数直接就是要滚到的地方。
        static void ScrollToPanel(DetailPage page, UIElement target)
        {
            var body = Get<ScrollView>(page, "Body");
            body.UpdateLayout();
            if (body.Content is not UIElement content) return;
            body.ScrollTo(0, target.TransformToVisual(content).TransformPoint(default).Y,
                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
        }

        static string InlineText(Inline inline) => inline switch
        {
            Run run => run.Text,
            Hyperlink link => string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text)),
            _ => ""
        };
    }

    /// <summary>一页剧：类型、工作室、三种刮削 id。图一律没有，这一条只看正文那两块。</summary>
    private static EmbyItem SeriesWithInfo(string id, string name, string[] genres, string studio) => new()
    {
        Id = id,
        Name = name,
        Type = EmbyItemType.Series,
        Genres = [.. genres],
        Studios = [new EmbyStudio { Name = studio }],
        ProviderIds = new()
        {
            ["Tmdb"] = "286171",
            ["Imdb"] = "tt35324004",
            ["Tvdb"] = "465020"
        },
        ChildCount = 2
    };

    private static EmbyItem SeasonOf(EmbyItem series, string id, int number) => new()
    {
        Id = id,
        Name = $"第 {number} 季",
        Type = EmbyItemType.Season,
        SeriesId = series.Id,
        SeriesName = series.Name,
        IndexNumber = number,
        ChildCount = 2
    };

    /// <summary>季清单还是这一季的集 —— 两条接口共用一个回包函数，按路径分。</summary>
    private static HttpResponseMessage SeriesPart(string path, EmbyItem series, IReadOnlyList<EmbyItem> seasons)
    {
        if (path.EndsWith("/Seasons", StringComparison.Ordinal))
            return Json(new { Items = seasons, TotalRecordCount = seasons.Count });

        // 集那一趟带着季 id；按季号发两集，剧页那条横带就有东西可画。
        var season = seasons.FirstOrDefault(item => path.Contains("SeasonId=" + item.Id, StringComparison.Ordinal))
            ?? seasons[0];
        return Json(new
        {
            Items = new[]
            {
                EpisodeOf(series, season, 1, "从右边数第三颗星"),
                EpisodeOf(series, season, 2, "夜里的那班车")
            }
        });
    }

    private static EmbyItem EpisodeOf(EmbyItem series, EmbyItem season, int number, string name) => new()
    {
        Id = $"{season.Id}-e{number}",
        Name = name,
        Type = EmbyItemType.Episode,
        SeriesId = series.Id,
        SeriesName = series.Name,
        SeasonId = season.Id,
        ParentIndexNumber = season.IndexNumber,
        IndexNumber = number,
        RunTimeTicks = TimeSpan.FromMinutes(24).Ticks,
        // 剧页的播放落点要挑这一季第一集未看的；带上媒体源，那一趟就不必再按 id 把这一集读一遍。
        MediaSources = [new MediaSource { Id = "offline", Name = "离线测试版本", Container = "mkv" }]
    };
}
