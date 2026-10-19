using System.Net;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FnNasMediaTests
{
    public static void Register()
    {
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
            var record = FnNasMediaAdapter.PlaybackRecord(new PlaybackReport
            { ItemId = item.Id, PlaySessionId = "fixture", MediaSourceId = source.Id, PositionTicks = 23 * TimeSpan.TicksPerSecond, AudioStreamIndex = 1, FnNasSelection = request.FnNasSelection });
            Assert.Equal("video", record["video_guid"]!.GetValue<string>());
            Assert.Equal("audio", record["audio_guid"]!.GetValue<string>());
            Assert.Equal(23d, record["ts"]!.GetValue<double>());
            Assert.Equal(90d, record["duration"]!.GetValue<double>());
            source.DirectStreamUrl = new Uri("https://external.invalid/file");
            Assert.Throws<InvalidOperationException>(() => planner.Plan(new PlaybackTicket { Item = item, Source = source }, fixture.Session.Connection!));
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
            Assert.False(commands.Any(row => row.Command == ItemCommand.EditMetadata || row.Command == ItemCommand.Delete));
            var before = fixture.Transport.Paths.Count;
            Assert.Throws<NotSupportedException>(() => fixture.Session.Client.ScanLibraryAsync(default).GetAwaiter().GetResult());
            Assert.Equal(before, fixture.Transport.Paths.Count);
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
    }

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
                "/v/api/v1/stream/list/movie" => "{\"files\":[{\"guid\":\"file\",\"path\":\"/media/test.mkv\"}],\"video_streams\":[{\"guid\":\"video\",\"media_guid\":\"file\",\"index\":0,\"codec_name\":\"h264\"}],\"audio_streams\":[{\"guid\":\"audio\",\"media_guid\":\"file\",\"index\":1,\"codec_name\":\"aac\"}]}",
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
