using System.Net;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FnNasMediaTests
{
    public static void Register()
    {
        RegisterRecovery();
        Test("飞牛媒体：登录、媒体库命名和 token 失效后同身份恢复", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            Assert.True(scope.Connection.IsFnNas);
            Assert.True(scope.Connection.IsAdministrator == true);
            fixture.Transport.Expire = true;
            var libraries = scope.ExecuteAsync((client, ct) => client.GetViewsAsync(ct), default).GetAwaiter().GetResult();
            Assert.Equal("电影库", libraries.Single().Name);
            Assert.Equal(2, fixture.Transport.Logins);
            Assert.True(scope.IsCurrent);
            Assert.True(fixture.Transport.Paths.All(path => path.StartsWith("/v/api/", StringComparison.Ordinal)));
            fixture.Session.SignOut();
            Assert.False(scope.IsCurrent);
        });
        Test("飞牛帐号：改密码后保存新密码与新令牌，后续请求使用新令牌", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            fixture.Session.ApplyPasswordChange(scope, "fixture-new-password", "fixture-token-2");
            Assert.Equal("fixture-new-password", fixture.Account.ProtectedPassword);
            Assert.Equal("fixture-token-2", fixture.Account.ProtectedAccessToken);
            _ = fixture.Session.ExecuteAsync((client, ct) => client.FnNas.GetUserAsync(client.FnNasConnection, ct), default)
                .GetAwaiter().GetResult();
            Assert.Equal("fixture-token-2", fixture.Transport.Authorizations[^1]);
        });
        Test("飞牛媒体：已保存账号恢复验证真实用户并复用目录", () =>
        {
            using var fixture = new Fixture();
            fixture.Account.UserId = "user";
            fixture.Account.ProtectedAccessToken = PassthroughSecretProtector.Instance.Protect("fixture-token");
            Assert.True(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
            Assert.Equal("电影库", fixture.Session.TakeRestoredViews()!.Single().Name);
            Assert.Equal(0, fixture.Transport.Logins);
        });
        Test("飞牛媒体：搜索按电影剧集分类且不会重复填充各分类", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var result = fixture.Session.Client.GetItemsAsync(new ItemQuery
            { SearchTerm = "片名", IncludeItemTypes = [EmbyItemType.Movie], Limit = 20 }, default).GetAwaiter().GetResult();
            Assert.Equal(1, result.TotalRecordCount);
            Assert.Equal("movie", result.Items.Single().Id);
        });
        Test("飞牛媒体：非整页偏移拼接下一页且保留总数", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var page = fixture.Session.Client.GetItemsAsync(new ItemQuery { StartIndex = 3, Limit = 2 }, default).GetAwaiter().GetResult();
            Assert.Equal(6, page.TotalRecordCount);
            Assert.Equal("item3,item4", string.Join(',', page.Items.Select(item => item.Id)));
        });
        Test("飞牛媒体：列表带上评分与清晰度供横幅卡显示", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var page = fixture.Session.Client.GetItemsAsync(new ItemQuery { Limit = 1 }, default).GetAwaiter().GetResult();
            var item = page.Items.Single();
            Assert.Equal(8.4f, item.CommunityRating);
            Assert.Equal("1080p,4k", string.Join(',', item.Resolutions));
        });
        // 卡片右上角那颗集数角标（2026-10-25 用户令「参考 Emby 库」）：飞牛列表里 local_number_of_episodes 是
        // 这部剧在库里的合计，local_number_of_seasons 是季数，两者各走各的字段。
        Test("飞牛媒体：剧集带本地集数，季与集不带", () =>
        {
            var series = FnNasMediaAdapter.Item(new JsonObject
            {
                ["guid"] = "series",
                ["title"] = "牧神记 年番1",
                ["type"] = "TV",
                ["local_number_of_seasons"] = 1,
                ["local_number_of_episodes"] = 14
            });
            Assert.Equal(1, series.ChildCount);
            Assert.Equal(14, series.LocalEpisodeCount);

            // 跨季的剧（真实服务器上 JOJO 本地两季共 3 集）：角标写整部剧的合计。
            var multi = FnNasMediaAdapter.Item(new JsonObject
            {
                ["guid"] = "jojo",
                ["title"] = "JOJO的奇妙冒险",
                ["type"] = "TV",
                ["local_number_of_seasons"] = 2,
                ["local_number_of_episodes"] = 3
            });
            Assert.Equal(2, multi.ChildCount);
            Assert.Equal(3, multi.LocalEpisodeCount);

            // 季自己的 local_number_of_episodes 是「这一季几集」，写在季卡上就是另一句话，所以不进这颗角标。
            var season = FnNasMediaAdapter.Item(new JsonObject
            {
                ["guid"] = "season",
                ["title"] = "第一季",
                ["type"] = "Season",
                ["season_number"] = 1,
                ["local_number_of_episodes"] = 13
            });
            Assert.Equal(13, season.ChildCount);
            Assert.Null(season.LocalEpisodeCount);
        });
        // 轮播底图（2026-10-25「只有飞牛登录时，首页没有轮播图」）：列表只发一张 2:3 海报，宽图在条目详情里。
        Test("飞牛轮播：缺宽图的条目去详情补背景图，已有宽图的不问", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var asked = new List<string>();
            fixture.Transport.Override = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/item/list", StringComparison.Ordinal)) return null;
                var id = path[(path.LastIndexOf('/') + 1)..];
                asked.Add(id);
                return Task.FromResult(FnNasItem(id, backdrop: id == "movie-b" ? "" : "/b/" + id + ".webp"));
            };

            var items = new List<EmbyItem>
            {
                new() { Id = "movie-a", Type = EmbyItemType.Movie, ImageTags = new() { ["Primary"] = "poster" } },
                new() { Id = "episode", Type = EmbyItemType.Episode, ImageTags = new() { ["Primary"] = "still", ["Thumb"] = "still" } },
                new() { Id = "movie-b", Type = EmbyItemType.Movie, ImageTags = new() { ["Primary"] = "poster" } }
            };
            var result = fixture.Session.Client.WithCarouselArtworkAsync(items, 3, default).GetAwaiter().GetResult();

            Assert.Equal("movie-a,movie-b", string.Join(',', asked), "只该给缺宽图的两条问详情（集已有 16:9 剧照）");
            Assert.Equal("/b/movie-a.webp", result[0].BackdropImageTags.Single());
            Assert.Equal(0, result[2].BackdropImageTags.Count, "详情没有 backdrops 时不能凭空造一张");
            Assert.True(result[1].ImageTags.ContainsKey("Thumb"), "已有宽图的那条不该被详情结果顶掉");

            // 单集已经够一张时不再问详情：一轮主页不该为已经站得住的幻灯片多花往返。
            asked.Clear();
            var fresh = new List<EmbyItem>
            {
                new() { Id = "movie-c", Type = EmbyItemType.Movie, ImageTags = new() { ["Primary"] = "poster" } },
                new() { Id = "episode-2", Type = EmbyItemType.Episode, ImageTags = new() { ["Primary"] = "still", ["Thumb"] = "still" } }
            };
            _ = fixture.Session.Client.WithCarouselArtworkAsync(fresh, 1, default).GetAwaiter().GetResult();
            Assert.Equal(0, asked.Count, "已经凑够张数时还去问详情");
        });
        Test("飞牛轮播：详情读不到时保留列表里的条目，不拖垮整页", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            fixture.Transport.Override = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                return path.EndsWith("/item/list", StringComparison.Ordinal)
                    ? null
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            };

            var items = new List<EmbyItem> { new() { Id = "movie", Type = EmbyItemType.Movie, ImageTags = new() { ["Primary"] = "poster" } } };
            var result = fixture.Session.Client.WithCarouselArtworkAsync(items, 3, default).GetAwaiter().GetResult();

            Assert.Equal("movie", result.Single().Id);
            Assert.Equal(0, result.Single().BackdropImageTags.Count);
        });
        Test("飞牛媒体：集的 16:9 海报记成缩略图，竖版海报不记", () =>
        {
            var episode = FnNasMediaAdapter.Item(new JsonObject
            {
                ["guid"] = "ep",
                ["title"] = "第一集",
                ["type"] = "Episode",
                ["poster"] = "/still.webp",
                ["poster_width"] = 1920,
                ["poster_height"] = 1080
            });
            Assert.Equal("/still.webp", episode.ImageTags["Thumb"], "集的 poster 就是这一集的 16:9 剧照");
            Assert.Equal("/still.webp", episode.ImageTags["Primary"]);

            var series = FnNasMediaAdapter.Item(new JsonObject
            {
                ["guid"] = "tv",
                ["title"] = "剧",
                ["type"] = "TV",
                ["poster"] = "/poster.webp",
                ["poster_width"] = 1920,
                ["poster_height"] = 2560
            });
            Assert.False(series.ImageTags.ContainsKey("Thumb"), "竖版海报不是宽图，不能进轮播");

            var still = FnNasMediaAdapter.Item(new JsonObject
            {
                ["guid"] = "ep2",
                ["title"] = "第二集",
                ["type"] = "Episode",
                ["poster"] = "/poster.webp",
                ["still_path"] = "/still2.webp",
                ["poster_width"] = 1920,
                ["poster_height"] = 1080
            });
            Assert.Equal("/still2.webp", still.ImageTags["Thumb"], "服务器给了 still_path 就用它");
        });
        Test("飞牛媒体：标记已看后使用季集列表选择下一集", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var next = fixture.Session.Client.GetNextUpAsync(1, default, "series").GetAwaiter().GetResult();
            Assert.Equal("episode2", next.Single().Id);
            Assert.Equal("series", next.Single().SeriesId);
        });
        Test("飞牛媒体：详情原画、音轨和续播进入播放规划及进度请求", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var item = fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult();
            var source = item.MediaSources.Single();
            var planner = new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders));
            var request = planner.Plan(new PlaybackTicket { Item = item, Source = source, StartTicks = 12 * TimeSpan.TicksPerSecond }, fixture.Session.Connection!);
            Assert.Equal("/v/api/v1/media/range/file", request.MediaUrl.AbsolutePath);
            Assert.Equal("", request.MediaUrl.Query);
            Assert.Equal(12d, request.StartSeconds);
            Assert.Equal("Authorization", request.HttpHeaders.Single().Key);
            Assert.Equal("audio", request.FnNasSelection!.AudioGuid(1));
            Assert.Equal("1080p", request.FnNasSelection.Resolution, "resolution 取视频流的 resolution_type");
            var record = FnNasMediaAdapter.PlaybackRecord(new PlaybackReport
            { ItemId = item.Id, PlaySessionId = "fixture", MediaSourceId = source.Id, PositionTicks = 23 * TimeSpan.TicksPerSecond, AudioStreamIndex = 1, FnNasSelection = request.FnNasSelection });
            Assert.Equal("video", record["video_guid"]!.GetValue<string>());
            Assert.Equal("audio", record["audio_guid"]!.GetValue<string>());
            Assert.Equal("_no_display_", record["subtitle_guid"]!.GetValue<string>(), "没选字幕要发官方那个占位符");
            Assert.Equal("1080p", record["resolution"]!.GetValue<string>());
            Assert.Equal(8_000_000, record["bitrate"]!.GetValue<int>());
            Assert.Equal(23L, record["ts"]!.GetValue<long>());
            Assert.Equal(90L, record["duration"]!.GetValue<long>());
            source.DirectStreamUrl = new Uri("https://external.invalid/file");
            Assert.Throws<InvalidOperationException>(() => planner.Plan(new PlaybackTicket { Item = item, Source = source }, fixture.Session.Connection!));
        });
        Test("飞牛进度：ts 与 duration 取整数秒，发的是整数字面量", () =>
        {
            var selection = Selection(90 * TimeSpan.TicksPerSecond + 7_000_000);
            var record = Record(selection, 5 * TimeSpan.TicksPerSecond + 9_000_000);
            Assert.Equal(5L, Whole(record, "ts"), "5.9 秒要发 5");
            Assert.Equal(90L, Whole(record, "duration"), "90.7 秒要发 90");
            Assert.Equal("5", record["ts"]!.ToJsonString());
            Assert.Equal(0L, Whole(Record(selection, 4_000_000), "ts"), "0.4 秒要发 0");
            Assert.Equal(0L, Whole(Record(selection, -3 * TimeSpan.TicksPerSecond), "ts"), "负值钳到 0");
        });
        Test("飞牛进度：没有字幕或索引失效发 _no_display_，命中索引发该轨 GUID", () =>
        {
            var selection = Selection();
            Assert.Equal("subtitle", selection.SubtitleGuid(3));
            Assert.Equal("_no_display_", selection.SubtitleGuid(null));
            Assert.Equal("_no_display_", selection.SubtitleGuid(9), "索引在映射里找不到时不能发空串");
            Assert.Equal("subtitle", Record(selection, 0, subtitle: 3)["subtitle_guid"]!.GetValue<string>());
            Assert.Equal("_no_display_", Record(selection, 0)["subtitle_guid"]!.GetValue<string>());

            var noTracks = FnNasPlaybackSelection.FromSource(Source(Video()), 0);
            Assert.Equal("_no_display_", Record(noTracks, 0)["subtitle_guid"]!.GetValue<string>());
        });
        Test("飞牛进度：音轨索引缺失或失效时回退默认音轨", () =>
        {
            var selection = Selection();
            Assert.Equal("audio-default", selection.DefaultAudioGuid);
            Assert.Equal("audio-default", selection.AudioGuid(null), "索引为空要回退默认音轨");
            Assert.Equal("audio-default", selection.AudioGuid(9), "索引在映射里找不到也要回退");
            Assert.Equal("audio-second", selection.AudioGuid(2));
            Assert.Equal("audio-default", Record(selection, 0)["audio_guid"]!.GetValue<string>());

            var unnamed = Source(Video(), Audio(1, "audio-first"), Audio(2, "audio-second"));
            Assert.Equal("audio-first", FnNasPlaybackSelection.FromSource(unnamed, 0).AudioGuid(null),
                "服务器没点默认音轨时退到第一条带 GUID 的音轨");

            var blankDefault = Source(Video(),
                new MediaStream { Index = 1, Type = "Audio", IsDefault = true }, Audio(2, "audio-second"));
            blankDefault.DefaultAudioStreamIndex = 1;
            Assert.Equal("audio-second", FnNasPlaybackSelection.FromSource(blankDefault, 0).AudioGuid(null),
                "点名的默认音轨没有 GUID 时也要退到第一条带 GUID 的");

            var noAudio = FnNasPlaybackSelection.FromSource(Source(Video()), 0);
            Assert.Equal("", noAudio.AudioGuid(null), "一条音轨都没有时播放器也没音轨可报");
            Assert.Equal("", Record(noAudio, 0)["audio_guid"]!.GetValue<string>());
        });
        Test("飞牛进度：resolution 与 bitrate 取实际视频流，取不到时是空串与 0", () =>
        {
            var selection = Selection();
            Assert.Equal("1080p", selection.Resolution);
            Assert.Equal(8_000_000, selection.Bitrate);
            var record = Record(selection, 0);
            Assert.Equal("1080p", record["resolution"]!.GetValue<string>());
            Assert.Equal(8_000_000, record["bitrate"]!.GetValue<int>());

            var unknown = FnNasPlaybackSelection.FromSource(Source(Video(resolutionType: null, bitrate: null)), 0);
            Assert.Equal("", unknown.Resolution);
            Assert.Equal(0, unknown.Bitrate);
            Assert.Equal("", Record(unknown, 0)["resolution"]!.GetValue<string>());
            Assert.Equal(0, Record(unknown, 0)["bitrate"]!.GetValue<int>());

            var audioOnly = FnNasPlaybackSelection.FromSource(Source(Audio(1, "audio")), 0);
            Assert.Equal("", audioOnly.Resolution, "没有视频流时不能发 original");
            Assert.Equal(0, audioOnly.Bitrate);
        });
        Test("飞牛查询：空的类型与题材不发空数组（真实服务器对空数组回 -4）", () =>
        {
            var empty = FnNasMediaAdapter.Query(new ItemQuery { Limit = 24, SortBy = EmbySortBy.DateAdded, Descending = true });
            var tags = empty["tags"]!.AsObject();
            Assert.Equal(0, tags.Count, "没有筛选时 tags 里不该出现空数组");
            Assert.Equal("create_time", empty["sort_column"]!.GetValue<string>());

            var filtered = FnNasMediaAdapter.Query(new ItemQuery
            {
                IncludeItemTypes = [EmbyItemType.Movie, EmbyItemType.Series],
                IsPlayed = true,
                Limit = 24
            });
            var filteredTags = filtered["tags"]!.AsObject();
            Assert.Equal("Movie,TV", string.Join(',', filteredTags["type"]!.AsArray().Select(node => node!.GetValue<string>())));
            Assert.Equal("1", filteredTags["watched"]!.GetValue<string>());
            Assert.Throws<NotSupportedException>(() => FnNasMediaAdapter.Query(new ItemQuery { Genre = "动画" }),
                "题材取值形状未核对，不能把猜的数组发给服务器换 -4");
        });
        Test("飞牛媒体：菜单只提供适用操作且不向 Emby 路径发送请求", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var commands = ItemMenu.For(new EmbyItem { Id = "m", Name = "电影", Type = EmbyItemType.Movie }, fnNas: true);
            Assert.True(commands.Where(row => row.Command is not null).All(row => ItemMenu.Supports(row.Command!.Value, false, true)));
            Assert.Equal("管理版本,手动匹配影片,解除匹配影片,刷新元数据,编辑元数据,删除",
                string.Join(',', commands.Where(row => row.Command is ItemCommand.ManageVersions or ItemCommand.FnNasMatch
                    or ItemCommand.FnNasUnmatch or ItemCommand.RefreshMetadata or ItemCommand.EditMetadata
                    or ItemCommand.Delete).Select(row => row.Label)));

            var series = ItemMenu.For(new EmbyItem { Id = "s", Name = "剧", Type = EmbyItemType.Series }, fnNas: true);
            Assert.False(series.Any(row => row.Command == ItemCommand.ManageVersions));
            Assert.True(series.Any(row => row.Command == ItemCommand.FnNasMatch));
            Assert.True(series.Any(row => row.Command == ItemCommand.Delete));

            var before = fixture.Transport.Paths.Count;
            Assert.Throws<NotSupportedException>(() => fixture.Session.Client.ScanLibraryAsync(default).GetAwaiter().GetResult());
            Assert.Equal(before, fixture.Transport.Paths.Count);
        });
        Test("飞牛筛选：工具栏下方不铺生效标签", () =>
        {
            Assert.False(LibraryFilterChips.ShowsActiveChips(fnNas: true));
            Assert.True(LibraryFilterChips.ShowsActiveChips(fnNas: false));
        });
        Test("飞牛筛选：选择写成 item/list 的 tags，空值不发", () =>
        {
            var filters = new FnNasFilterSelection { Type = "Movie", Watched = "1" };
            filters.Genres.Add("2");
            filters.Genres.Add("7");
            filters.Resolutions.Add("4k");
            filters.ColorRanges.Add("HDR10");
            filters.AudioTypes.Add("Stereo");
            filters.Locations.Add("CN");
            filters.Decades.Add("2020s");
            filters.RecognitionStatuses.Add("1");
            filters.RecognitionStatuses.Add("2");

            var query = FnNasMediaAdapter.Query(new ItemQuery
            {
                IncludeItemTypes = [EmbyItemType.Series],
                Limit = 30,
                SortBy = EmbySortBy.Name,
                FnNasFilters = filters
            });
            var tags = query["tags"]!.AsObject();
            Assert.Equal("Movie", tags["type"]!.AsArray().Single()!.GetValue<string>());
            // 官方前端的筛选每一类只留一个值：题材是单数整数，其余是单数字段；发数组会被服务器当坏请求。
            Assert.Equal(2, tags["genres"]!.GetValue<int>());
            Assert.Equal("4k", tags["resolution"]!.GetValue<string>());
            Assert.Equal("HDR10", tags["color_range"]!.GetValue<string>());
            Assert.Equal("Stereo", tags["audio_type"]!.GetValue<string>());
            Assert.Equal("CN", tags["locate"]!.GetValue<string>());
            Assert.Equal("2020s", tags["decade"]!.GetValue<string>());
            Assert.Equal("1", tags["recognition_status"]!.GetValue<string>());
            Assert.Equal("1", tags["watched"]!.GetValue<string>());
            Assert.False(tags.ContainsKey("resolutions") || tags.ContainsKey("decades"), "请求键必须是单数");

            var plain = FnNasMediaAdapter.Query(new ItemQuery { Limit = 30, SortBy = EmbySortBy.Name });
            Assert.Equal(0, plain["tags"]!.AsObject().Count);
        });
        Test("飞牛筛选：标签选项换成中文且映射缺失时保留原值", () =>
        {
            var tags = JsonNode.Parse("""
                {"genres":[2,7],"resolutions":["4k","1080P"],"color_range":["HDR10","SDR"],
                 "audio_type":["Stereo","Dolby Atmos","DolbySurround","DolbyAtmos"],"locate":["CN","XX"],
                 "decades":["Recent","2020s"],"recognition_status":[1,2,3]}
                """)!.AsObject();
            var genres = JsonNode.Parse("""[{"id":2,"value":"动画"},{"id":7,"value":"动作"}]""")!.AsArray()
                .Select(node => node!.AsObject()).ToArray();
            var countries = JsonNode.Parse("""[{"key":"CN","value":"中国大陆"},{"key":"CN","value":"重复"}]""")!.AsArray()
                .Select(node => node!.AsObject()).ToArray();

            var options = FnNasMediaAdapter.FilterOptions(tags, genres, countries);
            Assert.Equal("动画,动作", string.Join(',', options.Genres.Select(option => option.Label)));
            Assert.Equal("4k,1080P", string.Join(',', options.Resolutions.Select(option => option.Label)));
            Assert.Equal("HDR10,SDR", string.Join(',', options.ColorRanges.Select(option => option.Label)));
            Assert.Equal("立体声,杜比全景声,杜比环绕,杜比全景声",
                string.Join(',', options.AudioTypes.Select(option => option.Label)));
            Assert.Equal("中国大陆,XX", string.Join(',', options.Locations.Select(option => option.Label)));
            Assert.Equal("今年,2020年代", string.Join(',', options.Decades.Select(option => option.Label)));
            Assert.Equal("未匹配,已匹配,读取 NFO",
                string.Join(',', options.RecognitionStatuses.Select(option => option.Label)));
        });
        Test("飞牛筛选：经客户端读取 tag/list、题材与地区映射", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var options = fixture.Session.Client.GetFnNasFilterOptionsAsync("library", false, "zh-CN", default)
                .GetAwaiter().GetResult();
            Assert.Equal("动画,动作", string.Join(',', options.Genres.Select(option => option.Label)));
            Assert.Equal("中国大陆", options.Locations.Single().Label);
            Assert.True(fixture.Transport.RequestUris.Contains("/v/api/v1/tag/list?ancestor_guid=library"));
            Assert.True(fixture.Transport.RequestUris.Contains("/v/api/v1/tag/genres?lan=zh-CN"));
            Assert.True(fixture.Transport.RequestUris.Contains("/v/api/v1/tag/iso3166?lan=zh-CN"));
        });
        Test("飞牛筛选：计数、清空与克隆互不影响", () =>
        {
            var selection = new FnNasFilterSelection { Type = "Movie", Watched = "1" };
            selection.Genres.Add("2");
            selection.Resolutions.Add("4k");
            Assert.False(selection.IsBlank);
            Assert.Equal(4, selection.Count);

            var clone = selection.Clone();
            clone.Genres.Add("7");
            Assert.Equal(1, selection.Genres.Count);
            Assert.Equal(2, clone.Genres.Count);

            selection.Clear();
            Assert.True(selection.IsBlank);
            Assert.Equal(0, selection.Count);
        });
        Test("飞牛排序：官方菜单四项与顺序", () =>
        {
            Assert.Equal("添加日期,发行日期,标题,评分",
                string.Join(',', FnNasSortCatalog.Options.Select(option => option.Label)));
            Assert.Equal("DateCreated,SortName,ProductionYear,PremiereDate,SortName,SortName,CommunityRating,SortName",
                string.Join(',', FnNasSortCatalog.Options.Select(option => option.Value)));
        });
        Test("飞牛媒体库：保存排序按屏幕顺序发 setSort", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var scope = fixture.Session.Capture();
            scope.ExecuteAsync((client, ct) =>
                    client.FnNas.SetLibraryOrderAsync(client.FnNasConnection, ["second", "first"], ct), default)
                .GetAwaiter().GetResult();
            Assert.True(fixture.Transport.RequestUris.Contains("/v/api/v1/mdb/setSort"));
            var body = fixture.Transport.Bodies.Last();
            var list = body["list_sort"]!.AsArray();
            Assert.Equal("second,first", string.Join(',', list.Select(node => node!["guid"]!.GetValue<string>())));
            Assert.Equal("0,1", string.Join(',', list.Select(node => node!["sort_num"]!.GetValue<int>())));
        });
        Test("飞牛菜单请求：刷新模式、删除正文与手动匹配的季集形状", () =>
        {
            Assert.Equal(0, FnNasItemActions.Refresh("m", FnNasItemActions.RefreshMode("all"))["refresh_mode"]!.GetValue<int>());
            Assert.Equal(1, FnNasItemActions.Refresh("m", FnNasItemActions.RefreshMode("missing"))["refresh_mode"]!.GetValue<int>());

            var delete = FnNasItemActions.Delete(deleteFile: true, ["file", "file", ""]);
            Assert.Equal(1, delete["delete_file"]!.GetValue<int>());
            Assert.Equal("file", delete["media_guids"]!.AsArray().Single()!.GetValue<string>());

            var search = FnNasItemActions.SearchScrap("m", FnNasItemActions.SearchByImdbId, "  tt1  ", "zh-CN");
            Assert.Equal("imdb_id", search["source"]!.GetValue<string>());
            Assert.Equal("tt1", search["source_keyword"]!.GetValue<string>());
            Assert.Equal("m", search["item_guid"]!.GetValue<string>());
            Assert.Equal("zh-CN", search["lan"]!.GetValue<string>());

            var movie = new EmbyItem { Id = "m", Name = "电影", Type = EmbyItemType.Movie };
            var result = new FnNasScrapResult("电影", "tmdb", "42", "Movie", "2024-01-01");
            var files = new[] { new FnNasVersion("file1", "/a.mkv", 1, "1080p"), new FnNasVersion("file1", "/a.mkv", 1, "1080p") };
            var body = FnNasItemActions.Rescrap(movie, result, files);
            Assert.Equal("Movie", body["type"]!.GetValue<string>());
            Assert.Equal("file1", body["media_guids"]!.AsArray().Single()!.GetValue<string>());
            Assert.False(body.ContainsKey("season_number") || body.ContainsKey("scrap_tv_type"));

            var series = new EmbyItem { Id = "s", Name = "剧", Type = EmbyItemType.Series };
            var seriesBody = FnNasItemActions.Rescrap(series, result with { Type = "" }, files);
            Assert.Equal("TV", seriesBody["type"]!.GetValue<string>());
            Assert.Equal(0, seriesBody["media_guids"]!.AsArray().Count);
            Assert.Equal("MultiSeasonMultiEpisode", seriesBody["scrap_tv_type"]!.GetValue<string>());

            var episode = new EmbyItem
            {
                Id = "e",
                Name = "第一集",
                Type = EmbyItemType.Episode,
                ParentIndexNumber = 2,
                IndexNumber = 3
            };
            var episodeBody = FnNasItemActions.Rescrap(episode, result with { Type = "TV" }, files);
            Assert.Equal(2, episodeBody["season_number"]!.GetValue<int>());
            Assert.Equal(3, episodeBody["episode_number"]!.GetValue<int>());
            Assert.Equal("SingleEpisode", episodeBody["scrap_tv_type"]!.GetValue<string>());
        });
        Test("飞牛编辑元数据：发行日期是纯日期，带偏移的服务端日期也不换日", () =>
        {
            foreach (var value in new[] { "2024-05-01", "2024-05-01T00:00:00Z", "2024-05-01T23:00:00-07:00", "2024-05-01T00:00:00+08:00" })
            {
                var detail = new JsonObject { ["title"] = "电影", ["release_date"] = value };
                var edit = FnNasMetadataEdit.Read(detail);
                Assert.Equal("2024-05-01", edit.ReleaseDate);
                Assert.Null(edit.Problem);
                edit.ApplyTo(detail);
                Assert.Equal("2024-05-01", detail["release_date"]!.GetValue<string>());
            }
        });
        Test("飞牛编辑元数据：日期格式明确且不存在的日期、时刻与 NaN 都在表单内拒绝", () =>
        {
            var edit = new FnNasMetadataEdit("电影", "", "8.1", "2024-02-29", "release_date");
            Assert.Null(edit.Problem);
            foreach (var date in new[] { "2023-02-29", "2024-13-01", "05/01/2024", "2024-05-01T00:00:00Z" })
                Assert.NotNull((edit with { ReleaseDate = date }).Problem);
            foreach (var rating in new[] { "NaN", "Infinity", "-Infinity", "1e500" })
                Assert.NotNull((edit with { Rating = rating }).Problem);
        });
        Test("飞牛编辑元数据：只改四格，其余字段原样带回", () =>
        {
            var detail = JsonNode.Parse("""
                {"title":"旧名","overview":"旧简介","rating":8.4,"release_date":"2024-01-01",
                 "genres":[2,7],"posters":["p"],"title_locked":true}
                """)!.AsObject();
            var edit = FnNasMetadataEdit.Read(detail);
            Assert.Equal("旧名", edit.Name);
            Assert.Equal("8.4", edit.Rating);
            Assert.Equal("2024-01-01", edit.ReleaseDate);
            Assert.Equal("release_date", edit.ReleaseKey);
            Assert.Null(edit.Problem);

            var changed = edit with { Name = "新名", Overview = "新简介", Rating = "9.1", ReleaseDate = "2024-02-03" };
            Assert.True(changed.ApplyTo(detail));
            Assert.Equal("新名", detail["title"]!.GetValue<string>());
            Assert.Equal("新简介", detail["overview"]!.GetValue<string>());
            Assert.Equal(9.1, detail["rating"]!.GetValue<double>());
            Assert.Equal("2024-02-03", detail["release_date"]!.GetValue<string>());
            Assert.Equal("[2,7]", detail["genres"]!.ToJsonString());
            Assert.True(detail["title_locked"]!.GetValue<bool>());
            Assert.False(FnNasMetadataEdit.Read(detail).ApplyTo(detail));
            Assert.Equal("名称不能为空", (edit with { Name = " " }).Problem);
            Assert.NotNull((edit with { Rating = "11" }).Problem);
            Assert.NotNull((edit with { ReleaseDate = "不是日期" }).Problem);

            var episode = JsonNode.Parse("""{"title":"集","first_air_date":"2024-01-01"}""")!.AsObject();
            Assert.Equal("first_air_date", FnNasMetadataEdit.Read(episode).ReleaseKey);
            var emptyRelease = JsonNode.Parse("""{"title":"集","release_date":null,"first_air_date":"2024-01-01"}""")!.AsObject();
            Assert.Equal("first_air_date", FnNasMetadataEdit.Read(emptyRelease).ReleaseKey);
        });
        Test("飞牛版本与匹配候选：解析文件字段和来源字段", () =>
        {
            var version = FnNasVersion.From(JsonNode.Parse("""
                {"media_guid":"file1","file_path":"/media/a.mkv","file_size":2147483648,"height":1080}
                """)!.AsObject());
            Assert.Equal("file1", version.MediaGuid);
            Assert.Equal("/media/a.mkv", version.Path);
            Assert.Equal("1080p", version.Resolution);
            Assert.Contains("GB", version.Detail);

            var result = FnNasScrapResult.From(JsonNode.Parse("""
                {"title":"片名","source":"tmdb","source_id":42,"type":"Movie","release_date":"2024-01-01"}
                """)!.AsObject());
            Assert.Equal("片名", result.Title);
            Assert.Equal("42", result.SourceId);
            Assert.Equal("Movie", result.Type);
            Assert.Equal("2024-01-01", result.ReleaseDate);
        });
    }

    /// <summary>飞牛 play/record 的素材：一路视频、两路音轨（1 是默认）、一路字幕（3）。</summary>
    private static FnNasPlaybackSelection Selection(long durationTicks = 90 * TimeSpan.TicksPerSecond)
    {
        var source = Source(Video(), Audio(1, "audio-default", isDefault: true), Audio(2, "audio-second"),
            Subtitle(3, "subtitle"));
        source.DefaultAudioStreamIndex = 1;
        return FnNasPlaybackSelection.FromSource(source, durationTicks);
    }

    private static MediaSource Source(params MediaStream[] streams) => new() { Id = "file", MediaStreams = [.. streams] };

    private static MediaStream Video(string? resolutionType = "1080p", int? bitrate = 8_000_000) =>
        new() { Index = 0, Type = "Video", ProviderId = "video", ResolutionType = resolutionType, BitRate = bitrate };

    private static MediaStream Audio(int index, string guid, bool isDefault = false) =>
        new() { Index = index, Type = "Audio", ProviderId = guid, IsDefault = isDefault };

    private static MediaStream Subtitle(int index, string guid) =>
        new() { Index = index, Type = "Subtitle", ProviderId = guid };

    private static JsonObject Record(FnNasPlaybackSelection selection, long positionTicks, int? audio = null, int? subtitle = null) =>
        FnNasMediaAdapter.PlaybackRecord(new PlaybackReport
        {
            ItemId = "movie",
            PlaySessionId = "fixture",
            MediaSourceId = "file",
            PositionTicks = positionTicks,
            AudioStreamIndex = audio,
            SubtitleStreamIndex = subtitle,
            FnNasSelection = selection
        });

    /// <summary>整数秒断言：double 支撑的节点 <c>TryGetValue&lt;long&gt;</c> 会失败，正好证明发的是整数字面量。</summary>
    private static long Whole(JsonObject record, string key)
    {
        Assert.True(record[key]!.AsValue().TryGetValue<long>(out var value), $"{key} 必须是整数字面量，不能是小数");
        return value;
    }

    /// <summary>一条详情响应：<c>backdrops</c> 为空串时不带这个键，模拟服务器没有宽图。</summary>
    private static HttpResponseMessage FnNasItem(string id, string backdrop) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"code\":0,\"data\":{\"guid\":\"" + id + "\",\"title\":\"" + id + "\",\"type\":\"Movie\""
            + (backdrop.Length > 0 ? ",\"backdrops\":\"" + backdrop + "\"" : "") + "}}",
            System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class Fixture : IDisposable
    {
        public AppSettings Settings { get; } = new();
        public Transport Transport { get; } = new();
        public EmbySession Session { get; }
        public ServerProfile Server { get; } = new() { ServerKind = MediaServerKind.FnNas, Url = "https://fnnas.invalid/v", Name = "飞牛" };
        public AccountProfile Account { get; } = new() { Username = "fixture" };
        public Fixture()
        {
            Server.Accounts.Add(Account); Settings.Servers.Add(Server);
            Session = new EmbySession(Settings, new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "momoka-fnnas-" + Guid.NewGuid().ToString("N"))), PassthroughSecretProtector.Instance),
                new CredentialVault(PassthroughSecretProtector.Instance), DeviceIdentity.Create("fixture", "test"), Transport);
        }
        public void Login() => Session.SignInAsync(Server, Account, "fixture-password", "fixture", true, default).GetAwaiter().GetResult();
        public void Dispose() => Session.Dispose();
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> RequestUris { get; } = [];
        public List<JsonObject> Bodies { get; } = [];
        public List<string> Authorizations { get; } = [];
        public int Logins { get; private set; }
        public bool Expire { get; set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>?>? Override { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("fnnas.invalid", request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath; Paths.Add(path);
            RequestUris.Add(request.RequestUri.PathAndQuery);
            JsonObject? body = null;
            if (request.Content is { } content)
            {
                var text = await content.ReadAsStringAsync(cancellationToken);
                if (text.TrimStart().StartsWith('{')) body = JsonNode.Parse(text) as JsonObject;
                if (body is not null) Bodies.Add(body);
            }
            if (request.Headers.TryGetValues("Authorization", out var authorization))
                Authorizations.Add(authorization.Single());
            if (Override?.Invoke(request, cancellationToken) is { } pending) return await pending;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            if (Expire && path.EndsWith("/mediadb/list", StringComparison.Ordinal))
            { Expire = false; return Reply("null", -2); }
            var data = path switch
            {
                "/v/api/v2/user/loginByPassword" => "{\"token\":\"fixture-token-" + ++Logins + "\"}",
                "/v/api/v1/user/info" => "{\"guid\":\"user\",\"username\":\"fixture\",\"is_admin\":1}",
                "/v/api/v1/sys/config" => "{\"server_guid\":\"server\",\"server_name\":\"飞牛\"}",
                "/v/api/v1/sys/version" => "{\"version\":\"0.9.8\",\"mediasrvVersion\":\"0.8.43\"}",
                "/v/api/v1/mediadb/list" => "[{\"guid\":\"library\",\"name\":\"电影库\",\"category\":\"Movie\"}]",
                "/v/api/v1/tag/list" => "{\"genres\":[2,7],\"resolutions\":[\"4k\",\"1080P\"],\"color_range\":[\"HDR10\",\"SDR\"],\"audio_type\":[\"Stereo\"],\"locate\":[\"CN\"],\"decades\":[\"Recent\",\"2020s\"],\"recognition_status\":[1,2,3]}",
                "/v/api/v1/tag/genres" => "[{\"id\":2,\"value\":\"动画\"},{\"id\":7,\"value\":\"动作\"}]",
                "/v/api/v1/tag/iso3166" => "[{\"key\":\"CN\",\"value\":\"中国大陆\"}]",
                "/v/api/v1/mdb/setSort" => "null",
                "/v/api/v1/search/list" => "[{\"guid\":\"movie\",\"title\":\"片名\",\"type\":\"Movie\"},{\"guid\":\"series\",\"title\":\"剧\",\"type\":\"TV\"}]",
                "/v/api/v1/season/list/series" => "[{\"guid\":\"season\",\"type\":\"Season\",\"season_number\":1}]",
                "/v/api/v1/episode/list/season" => "[{\"guid\":\"episode1\",\"title\":\"第一集\",\"episode_number\":1,\"season_number\":1,\"watched\":1},{\"guid\":\"episode2\",\"title\":\"第二集\",\"episode_number\":2,\"season_number\":1,\"watched\":0}]",
                "/v/api/v1/item/movie" => "{\"guid\":\"movie\",\"title\":\"电影\",\"type\":\"Movie\",\"duration\":90}",
                "/v/api/v1/play/info" => "{\"ts\":12,\"duration\":90}",
                "/v/api/v1/stream/list/movie" => "{\"files\":[{\"guid\":\"file\",\"path\":\"/media/test.mkv\"}],\"video_streams\":[{\"guid\":\"video\",\"media_guid\":\"file\",\"index\":0,\"codec_name\":\"h264\",\"resolution_type\":\"1080p\",\"bps\":8000000}],\"audio_streams\":[{\"guid\":\"audio\",\"media_guid\":\"file\",\"index\":1,\"codec_name\":\"aac\"}]}",
                "/v/api/v1/item/list" => "",
                _ => throw new InvalidOperationException("Unexpected fixture path: " + path)
            };
            if (data.Length == 0)
            {
                if (body is null) throw new InvalidOperationException("item/list 缺少 JSON 正文");
                var sent = body["tags"]!.AsObject();
                if (sent["type"] is JsonArray types) Assert.True(types.Count > 0, "item/list 的 tags.type 不能是空数组");
                foreach (var key in new[] { "genres", "resolution", "color_range", "audio_type",
                    "locate", "decade", "recognition_status", "watched" })
                    if (sent[key] is { } value)
                        Assert.False(string.IsNullOrWhiteSpace(value.ToString()), $"item/list 的 tags.{key} 不能为空");
                Assert.False(sent.ContainsKey("resolutions") || sent.ContainsKey("decades"),
                    "item/list 的筛选键是单数 resolution / decade");
                var size = body["page_size"]!.GetValue<int>(); var page = body["page"]!.GetValue<int>();
                var list = new JsonArray(Enumerable.Range((page - 1) * size, Math.Min(size, 6 - (page - 1) * size))
                    .Select(index => (JsonNode?)new JsonObject
                    {
                        ["guid"] = "item" + index,
                        ["title"] = "影片",
                        ["type"] = "Movie",
                        ["vote_average"] = 8.4,
                        ["media_stream"] = new JsonObject { ["resolutions"] = new JsonArray("1080p", "4k") }
                    }).ToArray());
                data = new JsonObject { ["total"] = 6, ["list"] = list }.ToJsonString();
            }
            return Reply(data);
        }
        private static HttpResponseMessage Reply(string data, int code = 0) => new(HttpStatusCode.OK)
        { Content = new StringContent("{\"code\":" + code + ",\"data\":" + data + "}") };
    }
}
