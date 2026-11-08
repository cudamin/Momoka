using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>匿名飞牛 HTTP 经过实际详情页；电影和缺 tv_guid 的单集均核对人员、推荐、空态与恢复。</summary>
    private async Task FnNasDetailAsync()
    {
        var evidence = new List<object>();
        foreach (var episode in new[] { false, true })
        {
            using var fixture = CreateFixture();
            var server = new FnNasDetailProbeServer(await FixtureImageAsync(400, 600), await FixtureImageAsync(1280, 720));
            fixture.Transport.Reply = server.ReplyAsync;
            await fixture.SelectAsync("fnnas-detail.invalid", kind: MediaServerKind.FnNas);
            fixture.Settings.AggregateMode = false;
            fixture.Settings.Ui.EnableImmersive = false;

            // 只挂正式 Shell 的内容与通知面；不接播放器、真实启动入口或账号监听。
            var shell = new ShellPage();
            Get<FrameworkElement>(shell, "SignIn").Visibility = Visibility.Collapsed;
            Get<FrameworkElement>(shell, "ContentHost").Visibility = Visibility.Visible;
            Get<FrameworkElement>(shell, "SidebarHost").Visibility = Visibility.Collapsed;
            var frame = Get<Frame>(shell, "ContentFrame");
            _root.Children.Add(shell);
            DetailPage? page = null;
            try
            {
                ResizeProbeClient(1280, 900);
                var id = episode ? FnNasDetailProbeServer.EpisodeId : FnNasDetailProbeServer.MovieId;
                var seed = fixture.Session.Capture().Bind(FnNasMediaAdapter.Item(server.Item(id)));
                Require(!episode || string.IsNullOrEmpty(seed.SeriesId), "单集种子意外携带 tv_guid，父季恢复链未被覆盖");
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, seed)), "匿名飞牛详情导航失败");
                page = (DetailPage)frame.Content;
                page.AttachWindow(_window!);
                await UntilAsync(() => page.IsReady && !page.ViewModel.Busy);
                await LayoutAsync(page);
                Require(page.CurrentItem?.Source?.Connection.IsFnNas == true, "实际详情丢失飞牛来源身份");
                Require(page.CurrentItem?.Id == id && page.ViewModel.PlayTarget?.Id == id, "详情或播放目标变成其他条目");
                Require(!page.ViewModel.NoticeOpen && !Descendants(page).OfType<InfoBar>().Any(bar => bar.IsOpen),
                    "匿名飞牛详情出现错误横幅");
                if (episode)
                {
                    Require(page.CurrentItem?.SeriesId == FnNasDetailProbeServer.SeriesId,
                        "缺 tv_guid 的单集没有经父季补回所属剧");
                    Require(server.ItemReads.Contains(FnNasDetailProbeServer.SeasonId)
                        && server.ItemReads.Contains(FnNasDetailProbeServer.SeriesId), "单集没有读取父季和所属剧");
                    Require(page.ViewModel.SelectedSeason?.Id == FnNasDetailProbeServer.SeasonId
                        && page.ViewModel.ShownEpisodes.Select(item => item.Id).SequenceEqual(
                            [FnNasDetailProbeServer.EpisodeId, FnNasDetailProbeServer.SiblingId]),
                        "单集详情没有只显示当前季的两集");
                    Require(page.ViewModel.SeasonShelf is { Cards.Count: 0 }
                        && Get<StackPanel>(page, "SeasonPanel").Visibility == Visibility.Collapsed,
                        "单集详情多出全部剧季区域");

                    // 左上角那一格封面用的是**剧集那一层**的海报（2026-11-09 用户令「Emby、飞牛、Jellyfin 的
                    // 集页面左上角统一使用剧封面，不要用集封面」）。这条夹具的单集故意没有 tv_guid（列表上的飞牛
                    // 单集就是这样），剧是详情那一趟才补回来的 —— 所以它同时钉住「剧问出来之后封面要重取」
                    // （ItemArtwork.SameCoverSource）：本集自己那张 1280×720 的剧照装进 260×375 会是 260×146，
                    // 只有 400×600 的剧海报才量得到 250×375。
                    await UntilAsync(() => page.ViewModel.StillImage is not null);
                    var poster = DetailHero.StillBox(400, 600, 260, 375) ?? default;
                    Require(Math.Abs(page.ViewModel.StillWidth - poster.Width) < 0.6
                        && Math.Abs(page.ViewModel.StillHeight - poster.Height) < 0.6,
                        $"飞牛集页的封面不是剧海报：量到 {page.ViewModel.StillWidth:0.#}×{page.ViewModel.StillHeight:0.#}，"
                            + $"该是 {poster.Width:0.#}×{poster.Height:0.#}");
                }

                foreach (var width in new[] { 1280, 760 })
                {
                    await ResizeResponsiveAsync(page, width, 900);
                    await LayoutAsync(shell);
                    // 简洁模式电影保留双栏；单集才沿现有窄窗紧凑断点。
                    Require(page.ViewModel.IsCompact == (episode && width == 760), "飞牛详情没有进入现有类型的宽窄布局");
                    await RequireFnNasDetailRowsAsync(page, shell, server, episode, width, "populated");
                    if (episode)
                    {
                        var panel = Get<StackPanel>(page, "EpisodePanel");
                        Require(panel.Visibility == Visibility.Visible && panel.ActualHeight > 0,
                            "当前季单集列表整块没有布局");
                        var row = Get<ItemsRepeater>(page, "EpisodeList");
                        var strip = Get<ShelfStrip>(page, "EpisodeStrip");
                        Require(row.Visibility == page.ViewModel.EpisodeListVisibility
                            && strip.Visibility == page.ViewModel.EpisodeStripVisibility
                            && (row.Visibility == Visibility.Visible || strip.Visibility == Visibility.Visible),
                            "当前季单集列表两种形状均被隐藏或绑定错误");
                        await ScrollFnNasDetailPanelAsync(page, shell, panel);
                        await SaveProbeScreenAsync($"fnnas-detail-episode-{width}-season-screen.png");
                    }
                    evidence.Add(new
                    {
                        Surface = episode ? "episode" : "movie",
                        Width = width,
                        Subject = page.CurrentItem?.Id,
                        Series = page.CurrentItem?.SeriesId,
                        SelectedSeason = page.ViewModel.SelectedSeason?.Id,
                        People = page.ViewModel.CastShelf!.Cards.Select(card => card.Item.Id).ToArray(),
                        Similar = page.ViewModel.SimilarShelf!.Cards.Select(card => card.Item.Id).ToArray(),
                        Notice = page.ViewModel.NoticeOpen
                    });
                }

                // HTTP 真回空表，而非直接改视图模型；重新读详情后完整容器连标题和间距一起收起。
                server.EmptyRows = true;
                await page.ViewModel.ReloadAsync();
                await LayoutAsync(page);
                Require(page.ViewModel.CastShelf is { Cards.Count: 0 } && page.ViewModel.SimilarShelf is { Cards.Count: 0 },
                    "空响应没有清除旧名单或旧推荐");
                Require(Get<StackPanel>(page, "CastPanel").Visibility == Visibility.Collapsed
                    && Get<StackPanel>(page, "SimilarPanel").Visibility == Visibility.Collapsed,
                    "飞牛空名单或空推荐残留标题与容器");
                Require(!page.ViewModel.NoticeOpen, "正常空列表被显示为详情错误");
                await page.ScrollToEndAsync();
                await LayoutAsync(shell);
                await SaveProbeScreenAsync($"fnnas-detail-{(episode ? "episode" : "movie")}-760-empty-screen.png");

                server.EmptyRows = false;
                await page.ViewModel.ReloadAsync();
                await RequireFnNasDetailRowsAsync(page, shell, server, episode, 760, "restored");
                Require(page.ViewModel.PlayTarget?.Id == id && !page.ViewModel.NoticeOpen, "恢复名单推荐改变播放落点或留下错误");
                Require(fixture.Actions.Plays == 0 && server.ForbiddenOperations == 0, "飞牛详情探针触发播放或服务器写入");
                evidence.Add(new
                {
                    Surface = episode ? "episode" : "movie",
                    EmptyRowsHidden = true,
                    RestoredPeople = page.ViewModel.CastShelf!.Cards.Count,
                    RestoredSimilar = page.ViewModel.SimilarShelf!.Cards.Count,
                    fixture.Actions.Plays,
                    server.ForbiddenOperations,
                    PeopleSubjects = server.PeopleReads.ToArray(),
                    ItemReads = server.ItemReads.ToArray(),
                    ImageReads = server.ImageReads.ToArray()
                });
                // 人物卡进入已有作品库；不适用的飞牛筛选整组收起，调用入口也不能发标签请求。
                var person = page.ViewModel.CastShelf!.Cards[1].Item;
                page.Release();
                frame.Navigate(typeof(LibraryPage), LibraryRequest.ForPerson(fixture.Services, person));
                var works = (LibraryPage)frame.Content;
                await UntilAsync(() => works.IsReady && !works.ViewModel.Busy);
                Require(!works.ViewModel.NoticeOpen && works.ViewModel.Cards.Count == 2, "人物作品卡没有载入");
                Require(works.ViewModel.FilterVisibility == Visibility.Collapsed
                    && works.ViewModel.FnNasFilterVisibility == Visibility.Collapsed
                    && works.ViewModel.FnNasFilterTextVisibility == Visibility.Collapsed,
                    $"人物作品页仍显示无效筛选：Filter={works.ViewModel.FilterVisibility}, FnPanel={works.ViewModel.FnNasFilterVisibility}, FnText={works.ViewModel.FnNasFilterTextVisibility}, FnNas={works.ViewModel.IsFnNas}, Person={person.Id}");
                await works.ViewModel.ToggleFnNasFilterAsync();
                Require(!works.ViewModel.FnNasFilterOpen, "人物作品页命令打开无效筛选");
                await LayoutAsync(shell);
                await SaveProbeScreenAsync($"fnnas-detail-{(episode ? "episode" : "movie")}-person-760-screen.png");
                works.Release();
            }
            finally
            {
                page?.Release();
                frame.Content = null;
                Get<DispatcherTimer>(shell, "_toast").Stop();
                _root.Children.Remove(shell);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "fnnas-detail-receipt.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RequireFnNasDetailRowsAsync(DetailPage page, ShellPage shell, FnNasDetailProbeServer server,
        bool episode, int width, string state)
    {
        var view = page.ViewModel;
        // 集页自 2026-11-09 起不摆「更多类似」（用户令「Emby、飞牛、Jellyfin 的集页面统一不显示更多类似」）：
        // 那一排既没有卡片，也没有请求（DetailViewModel.LoadSimilarAsync 对集页直接返回），容器整块收起。
        var similar = view.SimilarShelf!;
        Require(view.CastShelf is { Cards.Count: 2 }, "飞牛演职人员仍为空");
        Require(episode ? similar.Cards.Count == 0 : similar.Cards.Count == 2,
            episode ? "飞牛集页仍摆着更多类似" : "飞牛更多类似为空，或推荐没有排除主体");
        var cast = view.CastShelf!;
        // 2026-11-08：题材与字幕的原始读数不能再上屏。飞牛的 item.genres 是数字 ID、字幕流是 zho + subrip，
        // 照抄出来就是「1 · 10 · 2 · 9」和「zho · SUBRIP」—— 这里核对屏上换成了名字。
        if (!episode)
            Require(view.SublineGenres.SequenceEqual(["动画", "动作"]),
                "题材没有换成名字：" + string.Join(" · ", view.SublineGenres));
        var subtitleRow = view.SubtitleTracks.FirstOrDefault(row => row.Stream is not null);
        Require(subtitleRow?.Text == "中文 · SRT", "字幕轨道标签仍是机器读数：" + (subtitleRow?.Text ?? "（没有字幕行）"));
        Require(cast.Cards.Select(card => card.Item.Name).SequenceEqual(["匿名导演", "匿名演员"])
            && cast.Cards.Select(card => card.Subtitle).SequenceEqual(["导演", "主角"]), "演职人员姓名、职业或角色映射错误");
        Require(cast.Cards.All(card => card.Item.Type == EmbyItemType.Person
            && card.Item.Source?.Connection.IsFnNas == true), "人物卡类型或来源不正确");
        var subject = episode ? FnNasDetailProbeServer.SeriesId : FnNasDetailProbeServer.MovieId;
        Require(server.PeopleReads.Contains(episode ? FnNasDetailProbeServer.SeasonId : subject), "没有为当前电影或所属季读取人物");
        if (!episode)
            Require(similar.Cards.All(card => card.Item.Id != subject && card.Item.Type == EmbyItemType.Movie),
                "推荐包含自己或类型错误");
        Require(cast.Cards.All(card => card.Item.ImageTags.GetValueOrDefault("Primary")?.StartsWith("/person/", StringComparison.Ordinal) == true),
            "人物头像路径没有进入现有图片管道");
        var castPanel = Get<StackPanel>(page, "CastPanel");
        var similarPanel = Get<StackPanel>(page, "SimilarPanel");
        Require(castPanel.Visibility == Visibility.Visible, "有内容的人员容器仍被隐藏");
        Require(similarPanel.Visibility == (episode ? Visibility.Collapsed : Visibility.Visible),
            episode ? "集页的更多类似容器没有收起" : "有内容的推荐容器仍被隐藏");
        await LayoutAsync(page);
        Require(castPanel.ActualHeight > 0
            && (episode || (similarPanel.ActualHeight > 0
                && Bounds(castPanel, page).Bottom <= Bounds(similarPanel, page).Top + 1)),
            "人员和推荐没有实际布局或顺序倒置");
        await ScrollFnNasDetailPanelAsync(page, shell, castPanel);
        await UntilAsync(() => cast.Cards.All(card => card.Poster is not null));
        var portraitCards = Descendants(Get<ShelfStrip>(page, "CastRow")).OfType<PosterCard>().ToArray();
        Require(portraitCards.Length == 2 && portraitCards.All(card => card.ActualWidth > 0 && card.ActualHeight > 0
            && card.Card?.Poster is not null && ReferenceEquals(Get<Image>(card, "Poster").Source, card.Card.Poster)),
            "人员头像没有进入实际人物模板");
        await SaveProbeScreenAsync($"fnnas-detail-{(episode ? "episode" : "movie")}-{width}-{state}-cast-screen.png");
        if (!episode)
        {
            await ScrollFnNasDetailPanelAsync(page, shell, similarPanel);
            await UntilAsync(() => similar.Cards.All(card => card.Poster is not null));
            var recommendationCards = Descendants(Get<ShelfStrip>(page, "SimilarRow")).OfType<PosterCard>().ToArray();
            Require(recommendationCards.Length == 2 && recommendationCards.All(card => card.Card?.Poster is not null
                && ReferenceEquals(Get<Image>(card, "Poster").Source, card.Card.Poster)),
                "推荐没有进入实际海报模板");
            await SaveProbeScreenAsync($"fnnas-detail-movie-{width}-{state}-similar-screen.png");
        }
        Require(!view.NoticeOpen && !Descendants(page).OfType<InfoBar>().Any(bar => bar.IsOpen), "名单或推荐加载后出现错误横幅");
    }

    private static async Task ScrollFnNasDetailPanelAsync(DetailPage page, ShellPage shell, FrameworkElement panel)
    {
        var scroll = Get<ScrollView>(page, "Body");
        await ScrollProbeAsync(scroll, Bounds(panel, (UIElement)scroll.Content).Top);
        await LayoutAsync(shell);
        var box = Bounds(panel, scroll);
        Require(box.Bottom > 0 && box.Top < scroll.ViewportHeight, "详情截图目标没有进入真实滚动视口");
    }

    private sealed class FnNasDetailProbeServer(byte[] poster, byte[] wide)
    {
        internal const string MovieId = "fn-movie";
        internal const string SeriesId = "fn-series";
        internal const string SeasonId = "fn-season";
        internal const string EpisodeId = "fn-episode";
        internal const string SiblingId = "fn-sibling";
        public bool EmptyRows { get; set; }
        public int ForbiddenOperations { get; private set; }
        public ConcurrentQueue<string> ItemReads { get; } = new();
        public ConcurrentQueue<string> PeopleReads { get; } = new();
        public ConcurrentQueue<string> ImageReads { get; } = new();

        public JsonObject Item(string id)
        {
            var episode = id is EpisodeId or SiblingId or "fn-other-episode";
            var season = id is SeasonId or "fn-other-season";
            var series = id is SeriesId or "fn-related-series-1" or "fn-related-series-2";
            var type = episode ? "Episode" : season ? "Season" : series ? "TV" : "Movie";
            return new JsonObject
            {
                ["guid"] = id,
                ["title"] = id == MovieId ? "匿名飞牛电影" : id == SeriesId ? "匿名飞牛剧集" : id == SeasonId ? "第 1 季" : "匿名条目 " + id,
                ["type"] = type,
                ["overview"] = "只使用匿名内存 HTTP；核对演职人员、更多类似及当前季列表，不读取真实媒体库。",
                ["parent_guid"] = episode ? id == "fn-other-episode" ? "fn-other-season" : SeasonId : season ? SeriesId : "fn-library",
                // 单集有 parent_guid 和 tv_title，故意没有 tv_guid，不能靠种子绕过父季恢复。
                ["tv_title"] = episode || season ? "匿名飞牛剧集" : "",
                ["season_number"] = episode || season ? id.StartsWith("fn-other-", StringComparison.Ordinal) ? 2 : 1 : 0,
                ["episode_number"] = id == SiblingId ? 2 : episode ? 1 : 0,
                ["local_number_of_seasons"] = series ? 2 : 0,
                ["local_number_of_episodes"] = series ? 3 : season ? 2 : 0,
                ["release_date"] = id == SiblingId ? "2026-01-02" : id == "fn-other-episode" ? "2026-02-01" : "2026-01-01",
                ["genres"] = new JsonArray(2, 7),
                ["duration"] = episode || type == "Movie" ? 1800 : 0,
                ["poster"] = "/poster/" + id + ".webp",
                ["poster_width"] = episode ? 1280 : 400,
                ["poster_height"] = episode ? 720 : 600,
                ["still_path"] = episode ? "/wide/" + id + ".webp" : "",
                ["backdrops"] = "/wide/" + id + ".webp",
                ["watched"] = 0,
                ["is_favorite"] = 0,
                ["vote_average"] = 8.5
            };
        }

        public async Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            var postRead = path.EndsWith("/api/v1/item/list", StringComparison.Ordinal)
                || path.EndsWith("/api/v1/person/item/list", StringComparison.Ordinal)
                || path.EndsWith("/api/v1/play/info", StringComparison.Ordinal)
                || path.Contains("/api/v1/person/list/", StringComparison.Ordinal);
            if (request.Method != HttpMethod.Get && !(request.Method == HttpMethod.Post && postRead))
            {
                ForbiddenOperations++;
                throw new InvalidOperationException("飞牛匿名详情探针拒绝服务器写入或播放端点");
            }
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal))
                return FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.8.43" });
            if (path.EndsWith("/api/v1/sys/config", StringComparison.Ordinal))
                return FnNasEnvelope(new { server_guid = "anonymous-fnnas", server_name = "匿名飞牛" });
            if (path.EndsWith("/api/v1/user/info", StringComparison.Ordinal))
                return FnNasEnvelope(new { guid = "user", username = "user", is_admin = 0 });
            if (path.EndsWith("/api/v1/mediadb/list", StringComparison.Ordinal))
                return FnNasEnvelope(Array.Empty<object>());
            if (path.EndsWith("/api/v1/mediadb/sum", StringComparison.Ordinal)) return FnNasEnvelope(new { total = 0 });
            if (path.EndsWith("/api/v1/play/list", StringComparison.Ordinal)) return FnNasEnvelope(Array.Empty<object>());
            // 条目映射会先问题材映射表：2／7 就是 Item() 里那个 genres 数组的两个 ID。
            if (path.EndsWith("/api/v1/tag/genres", StringComparison.Ordinal))
                return FnNasEnvelope(new[] { new { id = 2, value = "动画" }, new { id = 7, value = "动作" } });
            if (path.Contains("/api/v1/sys/img/", StringComparison.Ordinal))
            {
                ImageReads.Enqueue(path);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(path.Contains("/wide/", StringComparison.Ordinal) ? wide : poster) };
            }
            var id = path[(path.LastIndexOf('/') + 1)..];
            if (path.Contains("/api/v1/person/list/", StringComparison.Ordinal))
            {
                Require(id is MovieId or SeriesId or SeasonId, "人员请求没有使用电影、季或父剧 GUID");
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
                Require(body.Count == 2 && body["page"]?.GetValue<int>() == 1
                    && body["page_size"]?.GetValue<int>() is > 0, "人员读取没有发送只有 page/page_size 的官方正文");
                PeopleReads.Enqueue(id);
                var people = new[]
                {
                    new { person_guid = "fn-director", name = "匿名导演", job = "Director", role = "", order = 1, profile_path = "/person/director.webp" },
                    new { person_guid = "fn-actor", name = "匿名演员", job = "Actor", role = "主角", order = 2, profile_path = "/person/actor.webp" }
                };
                return FnNasEnvelope(new { total = EmptyRows ? 0 : people.Length, list = EmptyRows ? [] : people });
            }
            if (path.EndsWith("/api/v1/item/list", StringComparison.Ordinal))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
                var types = body["tags"]?["type"]?.AsArray();
                Require(types is { Count: 1 } && types[0]?.GetValue<string>() is "Movie" or "TV", "推荐请求没有收窄电影或剧集类型");
                Require(body["tags"]?["genres"]?.GetValue<int>() == 2, "推荐请求没有取首个整数题材");
                Require(body["sort_column"]?.GetValue<string>() == "vote_average"
                    && string.Equals(body["sort_type"]?.GetValue<string>(), "DESC", StringComparison.OrdinalIgnoreCase),
                    "推荐没有按评分倒序读取");
                Require(body["exclude_grouped_video"]?.GetValue<int>() == 1, "推荐混入已分组视频");
                var series = types![0]!.GetValue<string>() == "TV";
                var items = new[] { Item(series ? SeriesId : MovieId), Item(series ? "fn-related-series-1" : "fn-related-movie-1"),
                    Item(series ? "fn-related-series-2" : "fn-related-movie-2") };
                return FnNasEnvelope(new { total = EmptyRows ? 0 : items.Length, list = EmptyRows ? [] : items });
            }
            if (path.EndsWith("/api/v1/person/item/list", StringComparison.Ordinal))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
                Require(body["person_guid"]?.GetValue<string>() == "fn-actor", "人物卡丢失人物标识");
                return FnNasEnvelope(new { total = 2, list = new[] { Item("fn-related-movie-1"), Item("fn-related-movie-2") } });
            }
            if (path.Contains("/api/v1/item/", StringComparison.Ordinal))
            {
                ItemReads.Enqueue(id);
                return FnNasEnvelope(Item(id));
            }
            if (path.EndsWith("/api/v1/season/list/" + SeriesId, StringComparison.Ordinal))
                return FnNasEnvelope(new[] { Item(SeasonId), Item("fn-other-season") });
            if (path.Contains("/api/v1/episode/list/", StringComparison.Ordinal))
                return FnNasEnvelope(id == SeasonId ? new[] { Item(EpisodeId), Item(SiblingId) } : new[] { Item("fn-other-episode") });
            if (path.Contains("/api/v1/stream/list/", StringComparison.Ordinal))
            {
                var media = "media-" + id;
                return FnNasEnvelope(new
                {
                    files = new[] { new { guid = media, path = "/anonymous/" + id + ".mkv", duration = 1800, size = 1_000_000 } },
                    video_streams = new[] { new { guid = "video-" + id, media_guid = media, index = 0, codec_name = "h264", width = 1920, height = 1080, duration = 1800 } },
                    audio_streams = new[] { new { guid = "audio-" + id, media_guid = media, index = 1, codec_name = "aac", language = "zho", channels = 2, is_default = 1 } },
                    subtitle_streams = new[] { new { guid = "subtitle-" + id, media_guid = media, index = 2, codec_name = "subrip", language = "zho", is_default = 1 } }
                });
            }
            if (path.EndsWith("/api/v1/play/info", StringComparison.Ordinal)) return FnNasEnvelope(new { ts = 0, duration = 1800 });
            throw new InvalidOperationException("飞牛匿名详情未登记只读端点：" + path);
        }
    }
}
