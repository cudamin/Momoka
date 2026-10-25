using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotResourceOptionsTests
{
    public static void Register()
    {
        Test("MoviePilot 资源卡片：上游展示字段独立投影，原种子与下载正文保持一致", () =>
        {
            var resource = Parse("""
                [{"torrent_info":{"title":"Show.S01E02.1080p.WEB-DL.H264-GROUP","description":"站点副标题","seeders":18,"peers":"7","leechers":99},
                  "media_info":{"title":"识别作品","type":"电视剧","media_source":"themoviedb","media_id":"42"},
                  "meta_info":{"name":"解析片名","season_episode":"S01 E02","video_encode":" H264 ","edition":"WEB-DL",
                    "resource_type":"WEB","resource_effect":"HDR","resource_team":"GROUP","web_source":"平台"}}]
                """)[0];
            Assert.Equal("识别作品", resource.DisplayTitle);
            Assert.Equal("Show.S01E02.1080p.WEB-DL.H264-GROUP", resource.Title);
            Assert.Equal(18, resource.Seeders);
            Assert.Equal(7, resource.Peers);
            Assert.Equal("H264", resource.VideoCodec);
            Assert.Equal("WEB-DL", resource.Edition);
            Assert.Equal("WEB", resource.ResourceType);
            Assert.Equal("HDR", resource.ResourceEffect);
            Assert.Equal("GROUP", resource.ReleaseGroup);
            Assert.Equal("平台", resource.WebSource);
            Assert.Equal("S01 E02", resource.SeasonEpisode);
            var body = MoviePilotMediaParser.DownloadBody(resource);
            Assert.Equal(resource.Title, ((JsonElement)body["torrent_in"]!).GetProperty("title").GetString());
            Assert.Equal("42", ((JsonElement)body["media_in"]!).GetProperty("media_id").GetString());
        });
        Test("MoviePilot 资源卡片：候选与否决不借目标片名，缺元数据回退原种子", () =>
        {
            var resources = Parse("""
                [{"torrent_info":{"title":"原种子","peers":-3},"media_info":{"title":"目标片名"},"meta_info":{"name":"实际资源"},"match_status":"candidate"},
                 {"torrent_info":{"title":"原种子二"},"media_info":{"title":"目标片名"},"meta_info":{"name":"实际资源二"},"media_info_is_target":false},
                 {"torrent_info":{"title":"原种子三","peers":"坏数"},"meta_info":{"name":"  ","video_encode":[],"edition":"  "}}]
                """);
            Assert.Equal("实际资源", resources[0].DisplayTitle);
            Assert.Equal("实际资源二", resources[1].DisplayTitle);
            Assert.Equal("原种子三", resources[2].DisplayTitle);
            Assert.Equal(0, resources[0].Peers);
            Assert.Equal(0, resources[2].Peers);
            Assert.Equal<string?>(null, resources[2].VideoCodec);
            Assert.Equal<string?>(null, resources[2].Edition);
        });
        Test("MoviePilot 资源筛选：编码质量制作组精确匹配、大小写与缺字段", () =>
        {
            var rows = Parse("""
                [{"torrent_info":{"title":"甲"},"meta_info":{"name":"识别标题","video_encode":"H264","edition":"WEB-DL","resource_team":"GROUP"}},
                 {"torrent_info":{"title":"乙 H264 WEB-DL GROUP"},"meta_info":{"video_encode":"H265","edition":"WEB","resource_team":"GROUP-2"}},
                 {"torrent_info":{"title":"丙"}}]
                """);
            var now = DateTimeOffset.Now;
            Assert.Equal("甲", new MoviePilotResourceFilter { VideoCodec = "h264", Edition = "web-dl", ReleaseGroup = "group" }.Apply(rows, now).Single().Title);
            Assert.Equal(0, new MoviePilotResourceFilter { VideoCodec = "H26" }.Apply(rows, now).Count());
            Assert.Equal(0, new MoviePilotResourceFilter { ReleaseGroup = "GROUP", Edition = "WEB" }.Apply(rows, now).Count());
            Assert.Equal("甲", new MoviePilotResourceFilter { Text = "识别标题 H264 WEB-DL" }.Apply(rows, now).Single().Title);
            Assert.Equal(3, new MoviePilotResourceFilter().Apply(rows, now).Count());
        });
        Test("MoviePilot 资源多选：同维度或、跨维度且、清除恢复原对象", () =>
        {
            var rows = Parse("""
                [{"torrent_info":{"title":"甲 S01E02","site_name":"A","labels":["中字"],"downloadvolumefactor":0},"meta_info":{"resource_pix":"1080p","video_encode":"H264","edition":"WEB-DL","resource_team":"GROUP","begin_season":1,"begin_episode":2}},
                 {"torrent_info":{"title":"乙 S02E03","site_name":"B","labels":["双语"],"downloadvolumefactor":"0.5"},"meta_info":{"resource_pix":"2160p","video_encode":"H265","edition":"BluRay","resource_team":"OTHER","begin_season":2,"begin_episode":3}},
                 {"torrent_info":{"title":"丙 S02E03","site_name":"C","downloadvolumefactor":1},"meta_info":{"resource_pix":"2160p","video_encode":"H265","edition":"BluRay","resource_team":"OTHER","begin_season":2,"begin_episode":3}},
                 {"torrent_info":{"title":"字段缺失"}}]
                """);
            var now = DateTimeOffset.Now;
            var filter = new MoviePilotResourceFilter
            {
                Site = "旧单值被多选替代",
                Sites = ["a", "b"],
                Resolutions = ["1080P", "2160p"],
                Labels = ["中字", "双语"],
                VideoCodecs = ["h264", "h265"],
                Editions = ["web-dl", "bluray"],
                ReleaseGroups = ["group", "other"],
                Season = 99,
                Seasons = [1, 2],
                Episode = 99,
                Episodes = [2, 3],
                PromotionFactors = [0, 0.5]
            };
            var selected = filter.Apply(rows, now).ToArray();
            Assert.Equal(2, selected.Length);
            Assert.True(ReferenceEquals(rows[0], selected[0]));
            Assert.Equal("乙 S02E03", (filter with { Editions = ["BluRay"] }).Apply(rows, now).Single().Title);
            Assert.Equal(0, (filter with { ReleaseGroups = ["GRO"] }).Apply(rows, now).Count());
            Assert.Equal(4, new MoviePilotResourceFilter().Apply(rows, now).Count());
            Assert.Equal(1, new MoviePilotResourceFilter { PromotionFactors = [1] }.Apply(rows, now).Count());
            Assert.Equal(1, new MoviePilotResourceFilter { Episodes = [2] }.Apply(rows, now).Count());
        });
        Test("MoviePilot 资源角标：未知不冒充免费，数字倍率优先于旧优惠文本", () =>
        {
            var resource = Parse("""[{"torrent_info":{"title":"资源"}}]""")[0];
            Assert.Equal("", resource.PromotionBadge);
            Assert.Equal("", (resource with { DownloadFactor = 1, UploadFactor = 1, Promotion = "FREE" }).PromotionBadge);
            Assert.Equal("50%", (resource with { DownloadFactor = 0.5 }).PromotionBadge);
            Assert.Equal("2×FREE", (resource with { DownloadFactor = 0, UploadFactor = 2 }).PromotionBadge);
            Assert.Equal("2×", (resource with { DownloadFactor = 1, UploadFactor = 2 }).PromotionBadge);
            Assert.Equal("FREE", (resource with { Promotion = "FREE" }).PromotionBadge);
            Assert.Equal("", (resource with { Promotion = "未知" }).PromotionBadge);
        });
        Test("MoviePilot 资源折叠：同发布跨站点保留原对象、优惠与下载身份", () =>
        {
            var rows = Parse("""
                [{"torrent_info":{"title":"Show S01E03 1080p-GROUP","size":1024,"site_name":"甲","downloadvolumefactor":0}},
                 {"torrent_info":{"title":" show  S01E03 1080p-GROUP ","size":1024,"site_name":"乙","downloadvolumefactor":1}},
                 {"torrent_info":{"title":"Show S01E03 1080p-GROUP","size":2048,"site_name":"丙"}},
                 {"torrent_info":{"title":"Show S01E03 1080p-OTHER","size":1024,"site_name":"丁"}}]
                """);
            var groups = MoviePilotResourceGrouping.Group(rows);
            Assert.Equal(3, groups.Count);
            Assert.Equal(2, groups[0].Count);
            Assert.True(ReferenceEquals(rows[1], groups[0][1]));
            Assert.True(groups[0][0].IsFree);
            Assert.False(groups[0][1].IsFree);
            var filtered = new MoviePilotResourceFilter { Site = "乙" }.Apply(rows, DateTimeOffset.Now);
            Assert.Equal("乙", MoviePilotResourceGrouping.Group(filtered).Single().Single().SiteName);
        });
        Test("MoviePilot 资源折叠：未知体积和冲突媒体不能按同名合并", () =>
        {
            var resource = Parse("""[{"torrent_info":{"title":"Show"}}]""")[0];
            Assert.Equal(2, MoviePilotResourceGrouping.Group([resource, resource with { SiteName = "乙" }]).Count);
            Assert.Equal(2, MoviePilotResourceGrouping.Group([
                resource with { Size = 100, MediaSource = "douban", MediaId = "1" },
                resource with { Size = 100, MediaSource = "themoviedb", MediaId = "1" }]).Count);
        });
        Test("MoviePilot 季集筛选：特别篇、范围、离散集和未知编号", () =>
        {
            var rows = Parse("""
                [{"torrent_info":{"title":"特别篇"},"meta_info":{"begin_season":0,"begin_episode":1,"end_episode":3}},
                 {"torrent_info":{"title":"双季合集"},"meta_info":{"begin_season":"1","end_season":2,"episode_list":[1,3,"5"],"begin_episode":1,"end_episode":5}},
                 {"torrent_info":{"title":"第二季"},"meta_info":{"begin_season":2}},
                 {"torrent_info":{"title":"未知"}}]
                """);
            Assert.Equal(1, new MoviePilotResourceFilter { Season = 0, Episode = 2 }.Apply(rows, DateTimeOffset.Now).Count());
            Assert.Equal(1, new MoviePilotResourceFilter { Season = 2, Episode = 3 }.Apply(rows, DateTimeOffset.Now).Count());
            Assert.Equal(0, new MoviePilotResourceFilter { Season = 2, Episode = 2 }.Apply(rows, DateTimeOffset.Now).Count());
            Assert.Equal(2, new MoviePilotResourceFilter { Season = 2 }.Apply(rows, DateTimeOffset.Now).Count());
            Assert.Equal(4, new MoviePilotResourceFilter().Apply(rows, DateTimeOffset.Now).Count());
        });
        Test("MoviePilot 季集筛选：畸形和超大范围不展开", () =>
        {
            var rows = Parse("""
                [{"torrent_info":{"title":"资源"},"meta_info":{"begin_season":2,"end_season":1,"begin_episode":1,"end_episode":2147483647}}]
                """);
            Assert.Equal(0, rows[0].Seasons.Count);
            Assert.Equal(0, rows[0].Episodes.Count);
        });
        Test("MoviePilot 下载选项：目录只读取名称及保存路径，远程路径不丢存储前缀", () =>
        {
            using var transport = Transport()
                .Answer("download/clients", """[{"name":"下载器甲","type":"qbittorrent","password":"private"},{"name":"下载器甲"},{"name":""}]""")
                .Answer("download/paths", """[{"name":"动画","save_path":"rclone:/media/anime","download_path":"/media/anime"},{"save_path":"/downloads"}]""");
            var (service, _) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            var choices = service.DownloadChoicesAsync(resource, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, choices.Downloaders.Count);
            Assert.Equal("rclone:/media/anime", choices.Paths[0]);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(choices));
            Assert.Equal("GET", transport.Only("download/clients").Method);
            Assert.Equal("GET", transport.Only("download/paths").Method);
        });
        Test("MoviePilot 下载选项：默认省略字段，指定选项回传且不能换下载器重复提交", () =>
        {
            foreach (var complete in new[] { false, true })
            {
                using var transport = Transport().Answer("download/", """{"success":true}""");
                var (service, _) = Service(transport);
                var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
                if (complete) resource = resource with { MediaInfo = Json("""{"title":"测试","type":"电视剧","media_source":"themoviedb","media_id":"42"}""") };
                var defaults = MoviePilotMediaParser.DownloadBody(resource);
                Assert.False(defaults.ContainsKey("downloader"));
                Assert.False(defaults.ContainsKey("save_path"));
                service.DownloadAsync(resource, CancellationToken.None, new("下载器乙", "rclone:/media/动画")).GetAwaiter().GetResult();
                var sent = transport.Only("download/");
                Assert.True(sent.Url.EndsWith(complete ? "download/" : "download/add", StringComparison.Ordinal));
                using var body = JsonDocument.Parse(sent.Body);
                Assert.Equal("下载器乙", body.RootElement.GetProperty("downloader").GetString());
                Assert.Equal("rclone:/media/动画", body.RootElement.GetProperty("save_path").GetString());
                Assert.Equal("资源", body.RootElement.GetProperty("torrent_in").GetProperty("title").GetString());
                Assert.Equal(complete, body.RootElement.TryGetProperty("media_in", out _));
                Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource, CancellationToken.None, new("另一个", "/elsewhere")).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Count("download/"));
            }
        });
        Test("MoviePilot 下载选项：切换连接后旧资源不得读取目录或提交", () =>
        {
            using var transport = Transport();
            var (service, settings) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            settings.MoviePilot.Username = "另一个账号";
            var before = transport.Total;
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadChoicesAsync(resource, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource, CancellationToken.None, new("下载器", "/downloads")).GetAwaiter().GetResult());
            Assert.Equal(before, transport.Total);
        });
        Test("MoviePilot 下载媒体：核对身份后回传完整媒体和原种子，改选项仍不能重复提交", () =>
        {
            foreach (var complete in new[] { false, true })
            {
                using var transport = DownloadMediaTransport().Answer("download/", """{"success":true}""");
                var (service, _) = Service(transport);
                var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
                if (complete) resource = resource with
                {
                    MediaSource = "themoviedb",
                    MediaId = "42",
                    MediaType = "电影",
                    MediaInfo = Json("""{"title":"原识别","type":"电影","media_source":"themoviedb","media_id":"42"}""")
                };
                var media = service.ResolveDownloadMediaAsync(resource, " anilist ", " 73 ", " 电视剧 ", CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal("AniList 测试剧", media.Title);
                Assert.Equal("anilist", media.Source);
                Assert.Equal("73", media.Id);
                Assert.Equal("电视剧", media.Type);
                var lookup = transport.Only("media/73");
                Assert.Equal("GET", lookup.Method);
                Assert.True(lookup.Url.EndsWith("media/73?media_source=anilist&type_name=" + Uri.EscapeDataString("电视剧"), StringComparison.Ordinal));
                Assert.Equal(0, transport.Count("download/"));
                service.DownloadAsync(resource, CancellationToken.None, new("下载器", "/downloads", media)).GetAwaiter().GetResult();
                var sent = transport.Only("download/");
                Assert.Equal("POST", sent.Method);
                Assert.True(sent.Url.EndsWith("download/", StringComparison.Ordinal));
                using var body = JsonDocument.Parse(sent.Body);
                Assert.True(JsonElement.DeepEquals(resource.TorrentInfo, body.RootElement.GetProperty("torrent_in")));
                var mediaInfo = body.RootElement.GetProperty("media_in");
                Assert.Equal("anilist", mediaInfo.GetProperty("media_source").GetString());
                Assert.Equal(73, mediaInfo.GetProperty("media_id").GetInt32());
                Assert.Equal("服务器完整元数据", mediaInfo.GetProperty("overview").GetString());
                Assert.Equal("下载器", body.RootElement.GetProperty("downloader").GetString());
                Assert.Equal("/downloads", body.RootElement.GetProperty("save_path").GetString());
                Assert.False(body.RootElement.TryGetProperty("media_source", out _));
                Assert.False(body.RootElement.TryGetProperty("media_id", out _));
                Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource,
                    CancellationToken.None, new("另一个", "/elsewhere")).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Count("download/"));
            }
        });
        Test("MoviePilot 下载媒体：错误来源编号类型或空标题不得形成核对结果", () =>
        {
            foreach (var response in new[]
            {
                """{"media_source":"themoviedb","media_id":"73","type":"电视剧","title":"错来源"}""",
                """{"media_source":"anilist","media_id":"74","type":"电视剧","title":"错编号"}""",
                """{"media_source":"anilist","media_id":"73","type":"电影","title":"错类型"}""",
                """{"media_source":"anilist","media_id":"73","type":"电视剧","title":"  "}""",
                """{"media_source":"anilist","media_id":[],"type":"电视剧","title":"畸形编号"}""",
                "[]"
            })
            {
                using var transport = Transport().Answer("media/73", response);
                var (service, _) = Service(transport);
                var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
                Assert.Throws<MoviePilotException>(() => service.ResolveDownloadMediaAsync(resource, "anilist", "73",
                    "电视剧", CancellationToken.None).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Count("media/73"));
                Assert.Equal(0, transport.SentTo("download/").Count);
            }
        });
        Test("MoviePilot 下载媒体：缺来源编号或非法类型及取消不发查询", () =>
        {
            using var transport = DownloadMediaTransport();
            var (service, _) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            foreach (var (source, id, type) in new[] { ("", "73", "电视剧"), ("anilist", " ", "电视剧"), ("anilist", "73", "音乐") })
                Assert.Throws<MoviePilotException>(() => service.ResolveDownloadMediaAsync(resource, source, id,
                    type, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => service.ResolveDownloadMediaAsync(resource, "anilist", "73",
                "电视剧", new CancellationToken(true)).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Count("media/73"));
            Assert.Equal(0, transport.SentTo("download/").Count);
        });
        Test("MoviePilot 下载媒体：编号不能改变媒体查询路径且非法输入零请求", () =>
        {
            using var transport = DownloadMediaTransport();
            var (service, _) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            var before = transport.Total;
            foreach (var id in new[] { ".", "..", " .. ", "a/b", "a\\b", "%2e%2e", "a?b", "a#b", "a\0b", "73\n", "\t73" })
                Assert.Throws<MoviePilotException>(() => service.ResolveDownloadMediaAsync(resource, "anilist", id,
                    "电视剧", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(before, transport.Total);
            Assert.Equal(0, transport.SentTo("/media/").Count);
        });
        Test("MoviePilot 下载媒体：IMDb 与插件的原生编号仍以单一路径段查询", () =>
        {
            foreach (var (source, id) in new[] { ("imdb", "tt1234567"), ("plugin-source", "series:Abc-123") })
            {
                var endpoint = "media/" + Uri.EscapeDataString(id);
                using var transport = Transport().Answer(endpoint, JsonSerializer.Serialize(new
                {
                    media_source = source,
                    media_id = id,
                    type = "电视剧",
                    title = "编号查询结果"
                }));
                var (service, _) = Service(transport);
                var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
                var media = service.ResolveDownloadMediaAsync(resource, source, id, "电视剧", CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(id, media.Id);
                Assert.Equal(source, media.Source);
                var sent = transport.Only(endpoint);
                Assert.Equal("GET", sent.Method);
                Assert.True(sent.Url.EndsWith(endpoint + "?media_source=" + source + "&type_name=" + Uri.EscapeDataString("电视剧"), StringComparison.Ordinal));
                Assert.Equal(0, transport.SentTo("download/").Count);
            }
        });
        Test("MoviePilot 下载媒体：核对快照不得给另一条资源复用", () =>
        {
            using var transport = DownloadMediaTransport();
            var (service, _) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            var media = service.ResolveDownloadMediaAsync(resource, "anilist", "73", "电视剧", CancellationToken.None).GetAwaiter().GetResult();
            var other = resource with { Title = "另一条资源", TorrentInfo = Json("""{"title":"另一条资源","page_url":"https://site.invalid/details/2"}""") };
            Assert.Throws<MoviePilotOperationBlockedException>(() => MoviePilotMediaParser.DownloadBody(other, new(Media: media)));
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(other,
                CancellationToken.None, new(Media: media)).GetAwaiter().GetResult());
            Assert.Equal(MoviePilotOperationState.Idle, service.DownloadState(resource));
            Assert.Equal(MoviePilotOperationState.Idle, service.DownloadState(other));
            Assert.Equal(0, transport.SentTo("download/").Count);
        });
        Test("MoviePilot 下载媒体：切走再切回连接仍使核对快照过期", () =>
        {
            using var transport = DownloadMediaTransport();
            var (service, settings) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            var media = service.ResolveDownloadMediaAsync(resource, "anilist", "73", "电视剧", CancellationToken.None).GetAwaiter().GetResult();
            settings.MoviePilot.Username = "另一账号";
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.ResolveDownloadMediaAsync(resource, "anilist", "73",
                "电视剧", CancellationToken.None).GetAwaiter().GetResult());
            settings.MoviePilot.Username = "fixture";
            var before = transport.Total;
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource,
                CancellationToken.None, new(Media: media)).GetAwaiter().GetResult());
            Assert.Equal(before, transport.Total);
            Assert.Equal(MoviePilotOperationState.Idle, service.DownloadState(resource));
        });
        Test("MoviePilot 下载媒体：查询期间连接改变丢弃迟到结果", () =>
        {
            using var transport = DownloadMediaTransport();
            var (service, settings) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            transport.When("media/73", () =>
            {
                settings.MoviePilot.Username = "另一账号";
                settings.MoviePilot.Username = "fixture";
            });
            Assert.Throws<OperationCanceledException>(() => service.ResolveDownloadMediaAsync(resource, "anilist", "73",
                "电视剧", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Count("media/73"));
            Assert.Equal(0, transport.SentTo("download/").Count);
        });
        Test("MoviePilot 下载媒体：显式否决的资源不能靠重新识别绕过", () =>
        {
            using var transport = DownloadMediaTransport();
            var (service, _) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            var media = service.ResolveDownloadMediaAsync(resource, "anilist", "73", "电视剧", CancellationToken.None).GetAwaiter().GetResult();
            var rejected = resource with { DownloadProblem = "资源身份不匹配" };
            var before = transport.Total;
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.ResolveDownloadMediaAsync(rejected, "anilist", "73",
                "电视剧", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotOperationBlockedException>(() => MoviePilotMediaParser.DownloadBody(rejected, new(Media: media)));
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(rejected,
                CancellationToken.None, new(Media: media)).GetAwaiter().GetResult());
            Assert.Equal(before, transport.Total);
        });
        Test("MoviePilot 下载媒体：提交超时后切换识别或选项不能再次发送", () =>
        {
            using var transport = DownloadMediaTransport().Throw("download/", new HttpRequestException("fixture timeout"));
            var (service, _) = Service(transport);
            var resource = service.SearchByKeywordAsync("资源", CancellationToken.None).GetAwaiter().GetResult()[0];
            var media = service.ResolveDownloadMediaAsync(resource, "anilist", "73", "电视剧", CancellationToken.None).GetAwaiter().GetResult();
            Assert.Throws<MoviePilotOperationUncertainException>(() => service.DownloadAsync(resource,
                CancellationToken.None, new(Media: media)).GetAwaiter().GetResult());
            Assert.Equal(MoviePilotOperationState.Uncertain, service.DownloadState(resource));
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource,
                CancellationToken.None, new("另一个", "/elsewhere")).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Count("download/"));
        });
    }

    private static JsonElement Json(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static IReadOnlyList<MoviePilotResource> Parse(string json) => MoviePilotMediaParser.ParseResources(Json(json), null, null);
    private static StubTransport Transport() => new StubTransport()
        .Answer("login/access-token", """{"access_token":"fixture","super_user":true}""")
        .Answer("search/title", """[{"torrent_info":{"title":"资源","size":100,"site":1,"page_url":"https://site.invalid/details/1"}}]""");
    private static StubTransport DownloadMediaTransport() => Transport()
        .Answer("media/73", """{"media_source":"anilist","media_id":73,"type":"电视剧","title":"AniList 测试剧","overview":"服务器完整元数据"}""");
    private static (MoviePilotService Service, AppSettings Settings) Service(StubTransport transport)
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
        credentials.SetPassword(settings.MoviePilot, "fixture");
        return (new MoviePilotService(new MoviePilotClient(transport), credentials, settings), settings);
    }
}
