using System.Collections.Concurrent;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    /// 代码搭出来的值（类型／工作室／数据库链接）。剧页自 2026-11-02 起也有媒体信息表（用户令「参考集页面的
    /// 更多信息卡片给剧页面的更多信息卡片也加上媒体信息，没有播放记录就用第一集的，有播放记录就用播放记录
    /// 那一集」）：单季那一份的第二集中途看了，表该说第二集（播放落点仍是第一集未看的）；多季那一份没有
    /// 播放记录，表说第一季第一集。同日另一条令把「全部剧季」挪到了「其他信息」上面，顺序是
    /// 单集 → 全部剧季 → 其他信息；集列表牌子右端那颗季选择栏也在这天删掉了，两种页面上都不该再找得到它。
    /// </para>
    /// <para>
    /// 两种剧各开一页：单季那份同时验「季卡收起」「续播集优先」与「信息卡接在集列表下面」，多季那份验
    /// 「季卡还在、顺序是单集→全部剧季→其他信息」和「没有记录取当季第一集」。
    /// </para>
    /// <para>
    /// ③ 再开一页季页，验同日的另外两条：「把季页面的剧名和季度的字体大小还有位置互换」＋「给季页面
    /// 补上图三哪些」—— 大标题是剧名（点得回剧页面）、第二行是季名、第三行是这部剧的类型（一个一个点得动），
    /// 评分和事实那一行（年份区间、工作室、时长、分级、共几季）也说这部剧；以及「季页面不要媒体信息」——
    /// 媒体信息表零行，整块「其他信息」卡收起。剧的季数按夹具自己的季数给（单季 1、多季 2），
    /// 事实那一行的「共 N 季」才对得上。
    /// </para>
    /// <para>
    /// ④ 最后开一页集页（多季夹具第一季的第一集），验「集页面也加上全部剧季卡片」：两季的剧集页上那张卡在
    /// 「其他信息」上面、高不为零，而且这一页也没有季选择栏。
    /// </para>
    /// <para>
    /// ⑤ 每页顺手验同一对下拉（2026-11-02 用户报的那一条）：同一集在集页面的字幕「自动」写着具体语言，剧页、
    /// 季页却写着「自动（由 mpv 决定）」。夹具因此按 Emby 的两种回包各做一份集 —— 集列表那一趟是**裸媒体源**
    /// （文件名、容器、时长都在，轨道表空着），按 id 取详情那一趟才带视频／音频／字幕三条轨道。真正的判据是
    /// 缺轨道表时页面要补取（<see cref="ItemDetail.CarriesTracks"/>）：三页的「自动」都该说「国语」「简体中文」，
    /// 一个字都不该落到「由 mpv 决定」。
    /// </para>
    /// <para>
    /// 截图 `detail-series-single-season-{1280,760}.png`、`detail-series-two-seasons-{1280,760}.png`、
    /// `detail-series-season-before-info-1280.png`、`detail-episode-seasons-1280.png` 与
    /// `detail-season-header-1280.png`，读数落 `detail-series-info.txt`。全部走假传输，不联网、不播放。
    /// </para>
    /// </summary>
    private async Task DetailSeriesInfoAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();

        var single = SeriesWithInfo("show-one", "离线单季剧集", ["动画", "奇幻"], "Wit Studio", seasons: 1);
        var multi = SeriesWithInfo("show-two", "离线多季剧集", ["剧情"], "TBS", seasons: 2);
        var special = SeriesWithInfo("show-special", "离线特别篇剧集", ["动画"], "Shaft", seasons: 2);
        var singleSeasons = new[] { SeasonOf(single, "one-s1", 1) };
        var multiSeasons = new[] { SeasonOf(multi, "two-s1", 1, 4), SeasonOf(multi, "two-s2", 2, 4) };
        // 服务器把第 0 季排在最前；播出日期却让 OAD 落在第一季两集之间 —— 全季列表该按日期把它插进去。
        var specialSeasons = new[] { SeasonOf(special, "sp-s0", 0), SeasonOf(special, "sp-s1", 1) };

        // 集列表按季各生成一次，当成稳定的夹具：导航到集页和运输层回包用的是同一批 id、同一批字段。
        // **两种形状**同 id、同路径 —— 列表那一趟是裸媒体源（Emby 的 /Shows/{id}/Episodes 常见的样子），
        // 按 id 取详情那一趟才带完整轨道。页面缺轨道表就会补取，补回来的正是详情那一份。
        var singleEpisodes = singleSeasons.ToDictionary(season => season.Id, season => EpisodesOf(single, season));
        var multiEpisodes = multiSeasons.ToDictionary(season => season.Id, season => EpisodesOf(multi, season, 4));
        var specialEpisodes = SpecialEpisodesOf(special, specialSeasons);
        var details = singleEpisodes.Values.Concat(multiEpisodes.Values).Concat(specialEpisodes.Values)
            .SelectMany(episodes => episodes)
            .ToDictionary(episode => episode.Id, EpisodeDetail, StringComparer.Ordinal);
        var known = new[] { single, multi, special, singleSeasons[0], multiSeasons[0], multiSeasons[1],
                specialSeasons[0], specialSeasons[1] }
            .Concat(details.Values)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

        // 按 id 取条目的次数：缺轨道表时才该补取，而且同一集不该被问两遍（媒体信息那一集按 id 复用播放
        // 落点已经取回的那一份）。补取本身屏上看不出来，多跑一趟只在网络上看得到 —— 这里替它记账。
        var itemReads = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        fixture.Transport.Reply = (request, token) =>
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;

            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            // 按 id 取条目：剧、季、集都走这一张表。集页那一趟读的是集自己（见 DetailViewModel.LoadAsync），
            // 回一个空对象那一页连类型都不认得，季卡和兄弟集就都没得验。
            var items = path.IndexOf("/Items/", StringComparison.Ordinal);
            if (items >= 0)
            {
                var itemId = path[(items + "/Items/".Length)..];
                if (known.TryGetValue(itemId, out var item))
                {
                    itemReads.AddOrUpdate(itemId, 1, (_, count) => count + 1);
                    return Task.FromResult(Json(item));
                }
            }

            if (path.Contains("/Shows/" + single.Id + "/", StringComparison.Ordinal))
                return Task.FromResult(SeriesPart(path, uri.Query, singleSeasons, singleEpisodes));
            if (path.Contains("/Shows/" + multi.Id + "/", StringComparison.Ordinal))
                return Task.FromResult(SeriesPart(path, uri.Query, multiSeasons, multiEpisodes));
            if (path.Contains("/Shows/" + special.Id + "/", StringComparison.Ordinal))
                return Task.FromResult(SeriesPart(path, uri.Query, specialSeasons, specialEpisodes));

            // 一张「更多类似」的卡：演职人员与更多类似两块的位置互换要拿两块的上下沿来量。
            if (path.EndsWith("/Similar", StringComparison.Ordinal))
                return Task.FromResult(Json(new
                {
                    Items = new[] { new EmbyItem { Id = "similar-1", Name = "相似条目", Type = EmbyItemType.Movie } },
                    TotalRecordCount = 1
                }));

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

                // ⑤ 剧页的下拉：列表那一份只有空壳媒体源，页面该按 id 补取那一集的详情，自动那一行才说得出
                // 具体轨道（2026-11-02 用户报的那一条：「自动（由 mpv 决定）」只在服务器真没给轨道表时出现）。
                RequireAutoTracks(page, "单季剧页");
                readings.Add($"单季 1280 下拉：音频「{page.ViewModel.AudioTracks[0].Text}」、"
                    + $"字幕「{page.ViewModel.SubtitleTracks[0].Text}」");

                // 补取只该为缺轨道表的那一份发生，一集一趟：播放落点一次，媒体信息说第二集再一次。
                Require(Reads("one-s1-e1") == 1, $"单季剧页按 id 取了 {Reads("one-s1-e1")} 次播放落点");
                Require(Reads("one-s1-e2") == 1, $"单季剧页按 id 取了 {Reads("one-s1-e2")} 次媒体信息那一集");

                var seasonPanel = Get<StackPanel>(page, "SeasonPanel");
                var specialPanel = Get<StackPanel>(page, "SpecialPanel");
                var infoPanel = Get<StackPanel>(page, "InfoPanel");
                var episodePanel = Get<StackPanel>(page, "EpisodePanel");

                Require(seasonPanel.Visibility == Visibility.Collapsed && seasonPanel.ActualHeight == 0,
                    $"只有一季，全部剧季卡还占着位置：{seasonPanel.Visibility}、高 {seasonPanel.ActualHeight:0.#}");
                Require(specialPanel.Visibility == Visibility.Collapsed && specialPanel.ActualHeight == 0,
                    $"没有第 0 季，特别篇卡还占着位置：{specialPanel.Visibility}、高 {specialPanel.ActualHeight:0.#}");
                Require(infoPanel.Visibility == Visibility.Visible && infoPanel.ActualHeight > 0,
                    $"剧页没有其他信息卡：{infoPanel.Visibility}、高 {infoPanel.ActualHeight:0.#}");

                var head = Get<ShelfHead>(page, "InfoHead");
                Require(head.Title == "其他信息" && head.Visibility == Visibility.Visible, $"信息卡的牌子写的是「{head.Title}」");

                // 第二集中途看了：表说第二集（播放落点仍是第一集未看的），两条并排才证明挑的不是播放落点。
                Require(page.ViewModel.PlayTarget is { IndexNumber: 1 },
                    $"单季夹具的播放落点该是第一集未看的，实际 {page.ViewModel.PlayTarget?.IndexNumber}");
                Require(page.ViewModel.InfoRows.Count > 0,
                    "剧页有第二集的续播记录，媒体信息表却是空的");
                Require(Get<Border>(page, "OtherInfoDivider").Visibility == Visibility.Visible,
                    "剧页有媒体信息表，却没有那条分割线");
                Require(page.ViewModel.InfoRows[0].Value.Contains("one-s1-e2", StringComparison.Ordinal),
                    $"剧页媒体信息没取续播的那一集：{page.ViewModel.InfoRows[0].Value}");

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

            // ② 多季：季卡带照旧，顺序是 单集 → 全部剧季 → 其他信息；集列表牌子右端那颗季选择栏已经删了。
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
                Require(Get<StackPanel>(page, "SpecialPanel").Visibility == Visibility.Collapsed,
                    "没有第 0 季，多季剧页却画出了特别篇卡");
                Require(infoPanel.Visibility == Visibility.Visible && infoPanel.ActualHeight > 0, "多季剧页没有其他信息卡");

                // 季选择栏 2026-11-02 随用户令一起删了：这一页按名字找不到那颗 EgPicker。
                Require(!HasSeasonPicker(page), "剧页上还有季选择栏");

                // 2026-11-03：剧页集列表是全季一条（两季各两集），按播出日期排；牌子上的「第x季」能点出
                // 季列表，选一季只移动集带起点、不重读、不改播放落点。
                var view = page.ViewModel;
                Require(view.EpisodeShelf is { Cards.Count: 8 },
                    $"剧页集列表不是全季八条：{view.EpisodeShelf?.Cards.Count ?? -1}");
                Require(view.EpisodeShelf!.Cards.Select(card => card.Item.Id)
                        .SequenceEqual(["two-s1-e1", "two-s1-e2", "two-s1-e3", "two-s1-e4",
                            "two-s2-e1", "two-s2-e2", "two-s2-e3", "two-s2-e4"]),
                    "剧页集列表没有按播出日期排：" + string.Join("、", view.EpisodeShelf.Cards.Select(card => card.Item.Id)));
                Require(view.SelectedSeason?.Id == "two-s1", $"剧页锚点不是第一季：{view.SelectedSeason?.Name}");
                Require(view.CanPickEpisodeSeason, "两季的剧页集列表牌子不可点");
                Require(view.PlayTarget?.Id == "two-s1-e1",
                    $"剧页播放落点不是播出最早的集：{view.PlayTarget?.Id}");

                var seasonMenu = page.OpenEpisodeSeasonMenu();
                Require(seasonMenu is { Rows.Count: 2 },
                    $"季列表不是两行：{seasonMenu?.Rows.Count ?? -1}");
                Require(seasonMenu!.Rows.Count(row => row.Selected) == 1
                        && seasonMenu.Rows.Single(row => row.Selected).Season.Id == "two-s1",
                    "季列表没有标出当前季");
                await UntilAsync(() => seasonMenu.Rows.All(row => row.Row.ActualHeight > 0));
                await SaveProbeScreenAsync("detail-season-menu-1280.png");
                seasonMenu.Flyout.Hide();

                var strip = Get<ShelfStrip>(page, "EpisodeStrip");
                page.SelectEpisodeSeason(multiSeasons[1]);
                await LayoutAsync(page);
                Require(view.SelectedSeason?.Id == "two-s2" && strip.Requested > 0,
                    $"选第二季后集带没有对到它：锚点 {view.SelectedSeason?.Name}、请求 {strip.Requested:0}");
                Require(view.EpisodeShelf.Title.Contains("第 2 季", StringComparison.Ordinal)
                        && !view.EpisodeShelf.Title.Contains("集，已看", StringComparison.Ordinal),
                    $"选第二季后标题没跟着走（或还带着集数）：{view.EpisodeShelf.Title}");
                Require(view.PlayTarget?.Id == "two-s1-e1", "选季改掉了播放落点");
                readings.Add($"多季换季：锚点「{view.SelectedSeason?.Name ?? "无"}」、集带请求 {strip.Requested:0}");

                // 集带自己翻过季界也要换标题（2026-11-03 用户令「翻到第x季第1集或者最后一集时上方要自动切换」）。
                // 第 3 张是第一季最后一集，往下翻到它时标题该回到第一季；再翻到第 4 张（第二季第一集）该换到第二季。
                strip.JumpTo(3);
                await UntilAsync(() => view.SelectedSeason?.Id == "two-s1");
                Require(view.EpisodeShelf.Title.Contains("第 1 季", StringComparison.Ordinal),
                    $"集带停在第一季最后一集时标题没回到第一季：{view.EpisodeShelf.Title}");
                strip.JumpTo(4);
                await UntilAsync(() => view.SelectedSeason?.Id == "two-s2");
                Require(view.EpisodeShelf.Title.Contains("第 2 季", StringComparison.Ordinal),
                    $"集带停在第二季第一集时标题没换到第二季：{view.EpisodeShelf.Title}");
                readings.Add($"集带自动换季：第 5 张「{view.SelectedSeason?.Name}」、标题「{view.EpisodeShelf.Title}」");

                page.SelectEpisodeSeason(multiSeasons[0]);
                await LayoutAsync(page);
                Require(strip.Requested == 0 && strip.Offset == 0 && view.SelectedSeason?.Id == "two-s1",
                    $"回到第一季没有真的回到开头：锚点 {view.SelectedSeason?.Name}、请求 {strip.Requested:0}、"
                        + $"实际 {strip.Offset:0}");

                // ⑤ 多季剧页同一对下拉；这一页的续播集与播放落点是同一集，补取只该跑一趟（按 id 复用）。
                RequireAutoTracks(page, "多季剧页");
                readings.Add($"多季 1280 下拉：音频「{page.ViewModel.AudioTracks[0].Text}」、"
                    + $"字幕「{page.ViewModel.SubtitleTracks[0].Text}」");

                // 这一页的媒体信息说的就是播放落点那一集：补取那一趟被复用，没有为信息表再问一遍。
                Require(Reads("two-s1-e1") == 1, $"多季剧页按 id 取了 {Reads("two-s1-e1")} 次第一集");

                // 没有播放记录：表说这一页第一季的第一集。
                Require(page.ViewModel.InfoRows.Count > 0,
                    "多季剧页没有播放记录，媒体信息表却是空的");
                Require(Get<Border>(page, "OtherInfoDivider").Visibility == Visibility.Visible,
                    "多季剧页有媒体信息表，却没有那条分割线");
                Require(page.ViewModel.InfoRows[0].Value.Contains("two-s1-e1", StringComparison.Ordinal),
                    $"没有播放记录时，媒体信息没取当季第一集：{page.ViewModel.InfoRows[0].Value}");

                var infoTop = Top(infoPanel, page);
                var seasonTop = Top(seasonPanel, page);
                Require(infoTop >= Top(episodePanel, page) + episodePanel.ActualHeight - 1,
                    "其他信息卡没有接在集列表下方");
                Require(seasonTop + seasonPanel.ActualHeight <= infoTop + 1,
                    $"全部剧季卡没在其他信息上面：季卡 {seasonTop:0}–{seasonTop + seasonPanel.ActualHeight:0}、"
                        + $"信息卡从 {infoTop:0} 起");

                // 2026-11-03 用户令「所有页面，演职人员卡片和更多推荐卡片位置互换」：演职人员在上，更多类似在下。
                var castPanel = Get<StackPanel>(page, "CastPanel");
                var similarPanel = Get<StackPanel>(page, "SimilarPanel");
                var castTop = Top(castPanel, page);
                var similarTop = Top(similarPanel, page);
                Require(castPanel.Visibility == Visibility.Visible && castPanel.ActualHeight > 0,
                    "多季剧页没有演职人员卡");
                Require(similarPanel.Visibility == Visibility.Visible && similarPanel.ActualHeight > 0,
                    "多季剧页没有更多类似卡");
                Require(castTop + castPanel.ActualHeight <= similarTop + 1,
                    $"演职人员没有排在更多类似上面：演职人员 {castTop:0}–{castTop + castPanel.ActualHeight:0}、"
                        + $"更多类似从 {similarTop:0} 起");
                readings.Add($"卡片次序：演职人员 {castTop:0}–{castTop + castPanel.ActualHeight:0}、"
                    + $"更多类似 {similarTop:0}–{similarTop + similarPanel.ActualHeight:0}");

                readings.Add($"多季 1280：集带 {episodePanel.ActualHeight:0}、季卡 {seasonTop:0}–{seasonTop + seasonPanel.ActualHeight:0}、"
                    + $"信息卡 {infoTop:0}–{infoTop + infoPanel.ActualHeight:0}");

                // 同日那条令的实物证据：季卡顶到视口顶，它和底下那块信息卡的牌子同框，先后一眼看得出。
                ScrollToPanel(page, seasonPanel);
                await LayoutAsync(page);
                await SaveProbeScreenAsync("detail-series-season-before-info-1280.png");

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

            // ③ 特别篇：第 0 季的卡片在集列表下面；没有第 0 季时整块收起（单季、双季两页都没有 S0）。
            ResizeProbeClient(1280, 900);
            var specialsFrame = new Frame();
            _root.Children.Add(specialsFrame);
            try
            {
                specialsFrame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, special));
                var page = (DetailPage)specialsFrame.Content;
                await UntilAsync(() => page.IsReady);
                await LayoutAsync(page);

                var view = page.ViewModel;
                var specialPanel = Get<StackPanel>(page, "SpecialPanel");
                var specialHead = Get<ShelfHead>(page, "SpecialHead");
                var specialRow = Get<ShelfStrip>(page, "SpecialRow");
                var episodePanel = Get<StackPanel>(page, "EpisodePanel");
                var episodeStrip = Get<ShelfStrip>(page, "EpisodeStrip");

                // 2026-11-03 用户令：特别篇是集列表（第 0 季的集各自一张卡），不是季列表；它的封面是每一集
                // 自己的封面。整块从独立面板挪进了「全部剧集」卡片，排在集带下面。
                Require(view.SpecialShelf is { Cards.Count: 1 } && view.SpecialShelf.Cards[0].Item.Id == "sp-s0-e1",
                    $"特别篇卡带不是第 0 季那一集：{view.SpecialShelf?.Cards.Count ?? -1}、"
                        + $"{view.SpecialShelf?.Cards.FirstOrDefault()?.Item.Id ?? "无"}");
                Require(specialPanel.Visibility == Visibility.Visible && specialPanel.ActualHeight > 0
                        && specialHead.Visibility == Visibility.Visible && specialHead.Title == "特别篇"
                        && specialRow.ActualHeight > 0,
                    $"特别篇卡没有画出来：{specialPanel.Visibility}、高 {specialPanel.ActualHeight:0.#}、"
                        + $"牌子 {specialHead.Visibility}「{specialHead.Title}」");
                Require(Descendants(specialRow).OfType<PosterCard>()
                        .Any(card => card.Card?.Item.Id == "sp-s0-e1" && card.ActualWidth > 0 && card.ActualHeight > 0),
                    "特别篇集没有在带子里实现出来");
                Require(Top(specialPanel, page) >= Top(episodeStrip, page) + episodeStrip.ActualHeight - 1
                        && Top(specialPanel, page) + specialPanel.ActualHeight
                            <= Top(episodePanel, page) + episodePanel.ActualHeight + 1,
                    "特别篇没有接在集带下面、仍留在全部剧集卡片里");

                // 第 0 季只在特别篇那一排出现；「全部剧季」只列正片季（这张夹具只剩一季，整块因此收起）。
                Require(view.SeasonShelf is { Cards.Count: 1 }
                        && view.SeasonShelf.Cards[0].Item.Id == "sp-s1"
                        && Get<StackPanel>(page, "SeasonPanel").Visibility == Visibility.Collapsed,
                    "第 0 季在全部剧季里又出现了一次");

                Require(view.EpisodeShelf is { Cards.Count: 3 }
                        && view.EpisodeShelf.Cards.Select(card => card.Item.Id)
                            .SequenceEqual(["sp-s1-e1", "sp-s0-e1", "sp-s1-e2"]),
                    "全季集列表没有把 OAD 按播出日期插进第一季两集之间："
                        + string.Join("、", view.EpisodeShelf?.Cards.Select(card => card.Item.Id) ?? []));
                Require(view.SelectedSeason?.Id == "sp-s1",
                    $"特别篇夹具的锚点不该是第 0 季：{view.SelectedSeason?.Name}");
                Require(view.PlayTarget?.Id == "sp-s1-e1",
                    $"特别篇夹具的播放落点不是最早播出的正片：{view.PlayTarget?.Id}");

                var opened = fixture.Actions.Opened;
                view.OpenCard(view.SpecialShelf!.Cards[0]);
                Require(fixture.Actions.Opened == opened + 1, "特别篇集点不开集页面");

                ScrollToPanel(page, specialPanel);
                await LayoutAsync(page);
                await SaveProbeScreenAsync("detail-series-specials-1280.png");
                readings.Add($"特别篇 1280：卡 {specialRow.ActualHeight:0}、面板 {Top(specialPanel, page):0}–"
                    + $"{Top(specialPanel, page) + specialPanel.ActualHeight:0}、"
                    + $"集列表 {string.Join("→", view.EpisodeShelf?.Cards.Select(card => card.Item.Id) ?? [])}、"
                    + $"锚点「{view.SelectedSeason?.Name ?? "无"}」");
            }
            finally
            {
                (specialsFrame.Content as IShellContent)?.Release();
                _root.Children.Remove(specialsFrame);
            }

            // ③b 集页也要那块特别篇（同日第二条令）。开特别篇那部剧第一季的第一集：主列表仍是全季一条，
            // 特别篇那一排却只列第 0 季的那一集。
            ResizeProbeClient(1280, 900);
            var specialEpisodeFrame = new Frame();
            _root.Children.Add(specialEpisodeFrame);
            try
            {
                var specialEpisode = specialEpisodes["sp-s1"][0];
                specialEpisodeFrame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, specialEpisode));
                var page = (DetailPage)specialEpisodeFrame.Content;
                await UntilAsync(() => page.IsReady);
                await LayoutAsync(page);

                var view = page.ViewModel;
                Require(view.SpecialShelf is { Cards.Count: 1 }
                        && view.SpecialShelf.Cards[0].Item.Id == "sp-s0-e1",
                    $"集页的特别篇不是第 0 季的集：{view.SpecialShelf?.Cards.Count ?? -1}");
                Require(Get<StackPanel>(page, "SpecialPanel").Visibility == Visibility.Visible
                        && Get<ShelfStrip>(page, "SpecialRow").ActualHeight > 0,
                    "集页没有画出特别篇那一排");
                Require(view.EpisodeShelf is { Cards.Count: 3 }
                        && view.SelectedSeason?.Id == "sp-s1",
                    "集页的全季集列表或锚点被特别篇带偏了");

                readings.Add($"集页特别篇：{view.SpecialShelf!.Cards.Count} 集、锚点「{view.SelectedSeason?.Name}」");
            }
            finally
            {
                (specialEpisodeFrame.Content as IShellContent)?.Release();
                _root.Children.Remove(specialEpisodeFrame);
            }

            // ④ 季页：2026-11-02 用户令「把季页面的剧名和季度的字体大小还有位置互换」「给季页面补上图三哪些」
            // 「季页面不要媒体信息」。头一行是剧名（点得回剧页面）、第二行是季名、第三行是这部剧的类型；评分和
            // 事实那一行也说这部剧；媒体信息表和整块「其他信息」卡都不出现。
            ResizeProbeClient(1280, 900);
            var third = new Frame();
            _root.Children.Add(third);
            try
            {
                // 季页的播放落点也要补取一趟；下面的计数只看这一页自己多出来的那一次。
                var playReadsBefore = Reads("one-s1-e1");
                third.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, singleSeasons[0]));
                var page = (DetailPage)third.Content;
                await UntilAsync(() => page.IsReady);
                await LayoutAsync(page);

                var view = page.ViewModel;

                // 头行的三件事：大标题是剧名，第二行是季名，剧名点得回剧页面。标题那颗按钮的 CanExecute 是私有
                // （CanOpenTitle），可「点得动」在这里就是「TitleLink 指着那部剧」—— 命令只认这一个数。
                Require(view.Title == single.Name,
                    $"季页大标题该是剧名「{single.Name}」，实际「{view.Title}」");
                Require(view.Subline == singleSeasons[0].Name,
                    $"季页第二行该是季名「{singleSeasons[0].Name}」，实际「{view.Subline}」");
                var link = view.TitleLink;
                Require(link is { Type: EmbyItemType.Series } && link.Id == single.Id,
                    $"季页的剧名没指向剧页面：{link?.Id}（{link?.Type}）");

                // 类型那一行也取这部剧 —— 季自己身上没有类型。
                Require(view.SublineGenres.Contains("动画", StringComparer.Ordinal)
                        && view.SublineGenres.Contains("奇幻", StringComparer.Ordinal),
                    $"季页的类型那一行不是这部剧的：{string.Join('、', view.SublineGenres)}");

                // 事实那一行：年份区间、工作室、时长、分级、共几季，五个都说这部剧。
                var facts = view.Facts ?? "";
                Require(facts.Contains("2014", StringComparison.Ordinal)
                        && facts.Contains("Wit Studio", StringComparison.Ordinal)
                        && facts.Contains("24 分钟", StringComparison.Ordinal)
                        && facts.Contains("TV-14", StringComparison.Ordinal)
                        && facts.Contains("共 1 季", StringComparison.Ordinal),
                    "季页的事实那一行不是这部剧的：" + facts);
                Require(view.Score == "7.2",
                    $"季页的评分该取这部剧的 7.2，实际「{view.Score}」（{view.ScoreLabel}）");

                // ⑤ 季页的下拉也来自那条补取的路（LoadEpisodesAsync 挑播放落点）—— 用户就是在这一页看到
                // 「自动（由 mpv 决定）」的，而同一集在集页面上写着具体语言。
                RequireAutoTracks(page, "季页");
                readings.Add($"季 1280 下拉：音频「{view.AudioTracks[0].Text}」、字幕「{view.SubtitleTracks[0].Text}」");

                // 季页的播放落点补取一趟，且只一趟。
                Require(Reads("one-s1-e1") - playReadsBefore == 1,
                    $"季页按 id 取了 {Reads("one-s1-e1") - playReadsBefore} 次第一集（该只有播放落点那一趟）");

                // 屏上那一份：类型一个一个点得动、墨和副标题同一支，评分那一格画的就是这个数 —— 和自检读同一支。
                var genres = page.GenreRead();
                Require(genres.Ok, "季页的类型那一行没画对：" + genres.Detail);
                var score = page.ScoreRead();
                Require(score.Ok, "季页的评分那一格没画对：" + score.Detail);

                // 季页不要媒体信息：表零行，整块「其他信息」卡（牌子、那一行、分割线）一起收起。
                var infoPanel = Get<StackPanel>(page, "InfoPanel");
                Require(view.InfoRows.Count == 0,
                    $"季页不该有媒体信息表，实际 {view.InfoRows.Count} 行");
                Require(infoPanel.Visibility == Visibility.Collapsed && infoPanel.ActualHeight == 0,
                    $"季页的其他信息卡还占着位置：{infoPanel.Visibility}、高 {infoPanel.ActualHeight:0.#}");

                readings.Add($"季 1280：标题「{view.Title}」／第二行「{view.Subline}」／可点 {link?.Id}，"
                    + $"类型 {string.Join('、', view.SublineGenres)}（{genres.Detail}），"
                    + $"评分 {view.Score} {view.ScoreLabel}，事实 {facts}，"
                    + $"信息卡 {infoPanel.Visibility}（媒体信息 {view.InfoRows.Count} 行）");
                var body = Get<ScrollView>(page, "Body");
                var at = page.TransformToVisual(_root).TransformPoint(default);
                readings.Add($"季页诊断：page {page.ActualWidth:0}x{page.ActualHeight:0} vis={page.Visibility} op={page.Opacity:0.##} "
                    + $"at {at.X:0},{at.Y:0}；body {body.ActualWidth:0}x{body.ActualHeight:0} vis={body.Visibility} "
                    + $"op={body.Opacity:0.##} offset={body.VerticalOffset:0.##} "
                    + $"content={(body.Content as UIElement)?.Visibility.ToString() ?? "null"}");
                await SaveProbeScreenAsync("detail-season-header-1280.png");
            }
            finally
            {
                (third.Content as IShellContent)?.Release();
                _root.Children.Remove(third);
            }

            // ④ 集页：2026-11-02 用户令「集页面也加上全部剧季卡片」。两季那部剧的一集（第一季第一集，和运输层
            // 回包是同一份夹具）该有那张卡：两张季卡、看得见、在「其他信息」上面；同一天删掉的那格季选择栏在
            // 这一页上也不该找得到。
            ResizeProbeClient(1280, 900);
            var fourth = new Frame();
            _root.Children.Add(fourth);
            try
            {
                var episode = multiEpisodes[multiSeasons[0].Id][0];
                // 集页自己那一集只读一趟（页面本来就要按 id 取详情）；下面的计数只看这一页自己那一次。
                var playReadsBefore = Reads(episode.Id);
                fourth.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, episode));
                var page = (DetailPage)fourth.Content;
                await UntilAsync(() => page.IsReady);
                await LayoutAsync(page);

                var seasonPanel = Get<StackPanel>(page, "SeasonPanel");
                var infoPanel = Get<StackPanel>(page, "InfoPanel");

                Require(page.ViewModel.SeasonShelf is { Cards.Count: 2 },
                    $"两季那部剧的集页，季卡带不是两张：{page.ViewModel.SeasonShelf?.Cards.Count ?? -1}");
                Require(seasonPanel.Visibility == Visibility.Visible && seasonPanel.ActualHeight > 0,
                    $"集页没有画出全部剧季卡：{seasonPanel.Visibility}、高 {seasonPanel.ActualHeight:0.#}");
                Require(!HasSeasonPicker(page), "集页上还有季选择栏");

                // 集页的集列表也是全季一条，并且锚点落在这一集所属的季（真季清单里的那一份，不是拼出来的）。
                Require(page.ViewModel.EpisodeShelf is { Cards.Count: 8 },
                    $"集页集列表不是全季八条：{page.ViewModel.EpisodeShelf?.Cards.Count ?? -1}");
                Require(page.ViewModel.SelectedSeason?.Id == "two-s1"
                        && page.ViewModel.Seasons.Contains(page.ViewModel.SelectedSeason),
                    $"集页锚点不是清单里的第一季：{page.ViewModel.SelectedSeason?.Name}");
                Require(page.ViewModel.CanPickEpisodeSeason && page.ViewModel.EpisodeFocus == 0,
                    "集页的季列表不可点，或集带没有开在正在看的那一集上");

                // ⑤ 集页的下拉：这一页读的是它自己那一集（按 id 取详情，回包就是带轨道的那一份），同一对
                // 下拉在三页上说的是同一件事。
                RequireAutoTracks(page, "集页");
                readings.Add($"集页 1280 下拉：音频「{page.ViewModel.AudioTracks[0].Text}」、"
                    + $"字幕「{page.ViewModel.SubtitleTracks[0].Text}」");

                // 集页自己那一集只读一趟：它按 id 取详情是页面本来那一趟，不是额外补取。
                Require(Reads(episode.Id) - playReadsBefore == 1,
                    $"集页按 id 取了 {Reads(episode.Id) - playReadsBefore} 次这一集（该只有页面自己那一趟）");

                // 信息卡在集页上照旧是有的（这一集自己的媒体信息表 + 那一行），季卡站在它上面。
                Require(infoPanel.Visibility == Visibility.Visible && infoPanel.ActualHeight > 0,
                    $"集页没有其他信息卡：{infoPanel.Visibility}、高 {infoPanel.ActualHeight:0.#}");

                var seasonTop = Top(seasonPanel, page);
                var infoTop = Top(infoPanel, page);
                Require(seasonTop + seasonPanel.ActualHeight <= infoTop + 1,
                    $"集页的季卡没在其他信息上面：季卡 {seasonTop:0}–{seasonTop + seasonPanel.ActualHeight:0}、"
                        + $"信息卡从 {infoTop:0} 起");

                readings.Add($"集页 1280：季卡 {seasonTop:0}–{seasonTop + seasonPanel.ActualHeight:0}"
                    + $"（{page.ViewModel.SeasonShelf!.Cards.Count} 张）、信息卡 {infoTop:0}–{infoTop + infoPanel.ActualHeight:0}、"
                    + $"季选择栏 {(HasSeasonPicker(page) ? "还在" : "没有")}");

                ScrollToPanel(page, seasonPanel);
                await LayoutAsync(page);
                await SaveProbeScreenAsync("detail-episode-seasons-1280.png");
            }
            finally
            {
                (fourth.Content as IShellContent)?.Release();
                _root.Children.Remove(fourth);
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

        // 某一条目被按 id 取过几次 —— 只数 /Items/{id} 那一趟（图片那条路在上面先走了 404）。屏上看不出来的
        // 「多跑一趟」靠这几个数说话。
        int Reads(string id) => itemReads.TryGetValue(id, out var count) ? count : 0;

        // 季选择栏那颗 2026-11-02 已按用户令删掉：在视觉树里按名字/自动化 id 找 EgPicker，而不是 Get 一个不存在的
        // 控件 —— Get 读的是页面上的私有字段，字段没了只会空引用，那和「控件被藏起来」分不出来。
        static bool HasSeasonPicker(DetailPage page) =>
            Descendants(page).OfType<EgPicker>().Any(picker =>
                picker.Name == "DetailSeasonPicker"
                || AutomationProperties.GetAutomationId(picker) == "DetailSeasonPicker");

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

        // 三页共用的一句：自动那一行要说得出具体轨道，而不是「自动（由 mpv 决定）」—— 那一行只在服务器
        // 真没给轨道表、按 id 补取也没补到时才该出现，而夹具两者都给了。
        static void RequireAutoTracks(DetailPage page, string where)
        {
            var view = page.ViewModel;
            Require(view.AudioTracks.Count > 0, $"{where}一行音轨都没有");
            Require(view.AudioTracks[0].Text.Contains("国语", StringComparison.Ordinal)
                    && !view.AudioTracks[0].Text.Contains("由 mpv 决定", StringComparison.Ordinal),
                $"{where}的音频「自动」选不出具体轨道：{view.AudioTracks[0].Text}");

            Require(view.SubtitleTracks.Count > 0, $"{where}一行字幕都没有");
            Require(view.SubtitleTracks[0].Text.Contains("简体中文", StringComparison.Ordinal)
                    && !view.SubtitleTracks[0].Text.Contains("由 mpv 决定", StringComparison.Ordinal),
                $"{where}的字幕「自动」选不出具体轨道：{view.SubtitleTracks[0].Text}");
        }
    }

    /// <summary>
    /// 一页剧：类型、工作室、三种刮削 id，加上事实那一行要的那几个数（年份区间、时长、分级、季数）。
    /// 图一律没有，这一条只看正文那两块和季页的头。
    /// </summary>
    private static EmbyItem SeriesWithInfo(string id, string name, string[] genres, string studio, int seasons) => new()
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
        // 事实那一行：2014 – 2015 · 工作室 · 24 分钟 · TV-14 · 共 N 季。日期取年中，公历换当地时时区差不会跨年。
        ProductionYear = 2014,
        EndDate = new DateTimeOffset(2015, 6, 30, 0, 0, 0, TimeSpan.FromHours(8)),
        RunTimeTicks = TimeSpan.FromMinutes(24).Ticks,
        OfficialRating = "TV-14",
        CommunityRating = 7.2f,
        ChildCount = seasons,
        // 演职人员那一块要有东西，才能和更多类似那块量上下沿。Emby 只在按 id 取详情时带 People。
        People = [new EmbyPerson { Id = "person-1", Name = "离线演员", Role = "主演", Type = "Actor" }]
    };

    private static EmbyItem SeasonOf(EmbyItem series, string id, int number, int episodes = 2) => new()
    {
        Id = id,
        Name = $"第 {number} 季",
        Type = EmbyItemType.Season,
        SeriesId = series.Id,
        SeriesName = series.Name,
        IndexNumber = number,
        ChildCount = episodes
    };

    /// <summary>
    /// 季清单还是这一季的集 —— 两条接口共用一个回包函数，按路径分。集按季从夹具里取（<see cref="EpisodesOf"/>
    /// 各生成一次）：剧页那条横带和集页的兄弟集是同一批对象，而它们都是**裸媒体源**那一份；按 id 取详情
    /// （集页自己读的那一趟、页面补取播放落点那一趟）走的是 <see cref="EpisodeDetail"/> 那份带轨道的。
    /// </summary>
    private static HttpResponseMessage SeriesPart(string path, string query, IReadOnlyList<EmbyItem> seasons,
        IReadOnlyDictionary<string, EmbyItem[]> episodes)
    {
        if (path.EndsWith("/Seasons", StringComparison.Ordinal))
            return Json(new { Items = seasons, TotalRecordCount = seasons.Count });

        // 集那一趟的季 id 在查询串里（AbsolutePath 里没有，别在 path 上找）。剧页、集页现在不带 SeasonId：
        // 那就是「全剧一条」，按服务器给的季序拼起来；季页仍按 SeasonId 收窄。
        var season = seasons.FirstOrDefault(item => query.Contains("SeasonId=" + item.Id, StringComparison.Ordinal));
        return season is not null
            ? Json(new { Items = episodes[season.Id] })
            : Json(new { Items = seasons.SelectMany(item => episodes[item.Id]).ToArray() });
    }

    /// <summary>一季的那几集 —— 调用方按季各调一次，生成一份当夹具。多季那份要够长，集带才翻得动。</summary>
    private static EmbyItem[] EpisodesOf(EmbyItem series, EmbyItem season, int count = 2) =>
    [
        .. Enumerable.Range(1, count).Select(number => EpisodeOf(series, season, number,
            number switch { 1 => "从右边数第三颗星", 2 => "夜里的那班车", _ => $"第 {number} 集" }))
    ];

    /// <summary>
    /// 特别篇那份：第 0 季一条 OAD，播在第一季两集之间。服务器把第 0 季排在最前，全季列表要按日期把它
    /// 插到 E1 和 E2 中间 —— 正是用户截图里 S1:E20 → OAD → S2:E1 的形状。
    /// </summary>
    private static Dictionary<string, EmbyItem[]> SpecialEpisodesOf(EmbyItem series, IReadOnlyList<EmbyItem> seasons)
    {
        var specialSeason = seasons.Single(season => season.IndexNumber == 0);
        var firstSeason = seasons.Single(season => season.IndexNumber == 1);

        var oad = EpisodeOf(series, specialSeason, 1, "OAD 1");
        oad.PremiereDate = new DateTimeOffset(2014, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var first = EpisodeOf(series, firstSeason, 1, "第一集");
        first.PremiereDate = new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var second = EpisodeOf(series, firstSeason, 2, "第二集");
        second.PremiereDate = new DateTimeOffset(2014, 4, 1, 0, 0, 0, TimeSpan.Zero);

        return new Dictionary<string, EmbyItem[]>(StringComparer.Ordinal)
        {
            [specialSeason.Id] = [oad],
            [firstSeason.Id] = [first, second]
        };
    }

    private static EmbyItem EpisodeOf(EmbyItem series, EmbyItem season, int number, string name)
    {
        var episode = new EmbyItem
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
            // 播出日期按季往后排，让「全季一条」的排序在夹具里真正起作用；特别篇另有自己的日期。
            PremiereDate = new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddMonths((season.IndexNumber ?? 1) * 3)
                .AddDays(number)
        };

        // 集列表那一趟的媒体源是**裸的**：文件名、容器、时长都在，轨道表空着 —— Emby 的 /Shows/{id}/Episodes
        // 就是这样下发的（2026-11-02 用户报的那一条的来由）。路径按集区分，媒体信息表的「文 件」一行才认得出
        // 说的是哪一集；带轨道的那一份见 EpisodeDetail。
        episode.MediaSources.Add(new MediaSource
        {
            Id = $"{season.Id}-e{number}",
            Name = "离线测试版本",
            Container = "mkv",
            Path = $@"\\fixture\Shows\{series.Id}\{season.Id}-e{number}.mkv",
            RunTimeTicks = TimeSpan.FromMinutes(24).Ticks
        });

        // 单季那份的第二集中途看了：媒体信息表该说第二集，而播放落点仍是第一集未看的。
        // 多季那份不带播放记录，表说第一季第一集。
        if (series.Id == "show-one" && number == 2)
        {
            episode.UserData = new EmbyUserData
            {
                PlaybackPositionTicks = TimeSpan.FromMinutes(10).Ticks,
                PlayedPercentage = 40,
                LastPlayedDate = new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.FromHours(8))
            };
        }

        return episode;
    }

    /// <summary>
    /// 同一集的详情那一份 —— <c>/Users/{uid}/Items/{集 id}</c> 的回包：id、路径、容器、时长和播放记录照抄，
    /// 媒体源上补视频／音频／字幕三条轨道。Emby 的集列表只给「有媒体源、没有轨道表」，按 id 取详情才带得回来，
    /// 探针要的正是这两份落在同一集上：页面缺轨道表就补取，三页的下拉因此都该说得出具体轨道。
    /// </summary>
    private static EmbyItem EpisodeDetail(EmbyItem list) => new()
    {
        Id = list.Id,
        Name = list.Name,
        Type = list.Type,
        SeriesId = list.SeriesId,
        SeriesName = list.SeriesName,
        SeasonId = list.SeasonId,
        ParentIndexNumber = list.ParentIndexNumber,
        IndexNumber = list.IndexNumber,
        RunTimeTicks = list.RunTimeTicks,
        PremiereDate = list.PremiereDate,
        UserData = list.UserData,
        MediaSources = [.. list.MediaSources.Select(source => new MediaSource
        {
            Id = source.Id,
            Name = source.Name,
            Container = source.Container,
            Path = source.Path,
            RunTimeTicks = source.RunTimeTicks,
            MediaStreams =
            [
                new() { Index = 0, Type = "Video", Codec = "h264", Width = 1920, Height = 1080 },
                new() { Index = 1, Type = "Audio", DisplayTitle = "国语 · AAC 2.0", IsDefault = true },
                new() { Index = 2, Type = "Subtitle", DisplayTitle = "简体中文", IsDefault = true }
            ]
        })]
    };
}
