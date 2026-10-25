using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Plex;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// The Plex backend end to end through <see cref="EmbySession"/>: sign-in, restore, browse, detail,
/// playback planning, reporting and the menu — all against a fake transport.
/// <para>
/// Nothing here has been run against a real Plex server, and no live server was used to write it. These
/// cases say what this client sends and how it reads the answers it is given; they are not evidence that
/// a Plex server answers this way. See <c>docs/Plex支持.md</c> for that boundary.
/// </para>
/// </summary>
internal static class PlexMediaTests
{
    public static void Register()
    {
        Test("Plex 媒体：登录、媒体库命名和令牌失效后同身份恢复", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            Assert.True(scope.Connection.IsPlex);
            Assert.Equal("客厅 Plex", scope.Connection.ServerName);
            Assert.Equal("machine", scope.Connection.ServerId);
            Assert.Equal(1, fixture.Transport.Logins);

            // A 401 on the first browse is what a revoked token looks like; the session re-signs in with
            // the saved password and replays the same call on the same identity.
            fixture.Transport.Expire = true;
            var libraries = scope.ExecuteAsync((client, ct) => client.GetViewsAsync(ct), default).GetAwaiter().GetResult();
            Assert.Equal("电影,剧集", string.Join(',', libraries.Select(library => library.Name)));
            Assert.Equal(2, fixture.Transport.Logins);
            Assert.True(scope.IsCurrent);

            fixture.Session.SignOut();
            Assert.False(scope.IsCurrent);
        });
        Test("Plex 媒体：已保存账号恢复不重登账号并复用媒体库", () =>
        {
            using var fixture = new Fixture();
            fixture.Server.ServerId = "machine";
            fixture.Account.UserId = "7";
            fixture.Account.ProtectedAccessToken = PassthroughSecretProtector.Instance.Protect("fixture-token");
            Assert.True(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
            Assert.Equal("电影,剧集", string.Join(',', fixture.Session.TakeRestoredViews()!.Select(library => library.Name)));
            Assert.Equal(0, fixture.Transport.Logins);
        });
        Test("Plex 媒体：音乐库被挡在外面，浏览只认电影与剧集", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var libraries = fixture.Session.Client.GetViewsAsync(default).GetAwaiter().GetResult();
            Assert.Equal(2, libraries.Count);
            Assert.DoesNotContain("音乐", string.Join(',', libraries.Select(library => library.Name)));
            Assert.Equal("movies", libraries[0].CollectionType);
        });
        Test("Plex 媒体：搜索按电影剧集分类", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var result = fixture.Session.Client.GetItemsAsync(new ItemQuery
            { SearchTerm = "片名", IncludeItemTypes = [EmbyItemType.Movie], Limit = 20 }, default).GetAwaiter().GetResult();
            Assert.Equal(1, result.Items.Count);
            Assert.Equal("10", result.Items.Single().Id);
        });
        Test("Plex 媒体：详情把 Media/Part/Stream 映射成媒体源、音轨与外挂字幕", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var item = fixture.Session.Client.GetItemAsync("10", default).GetAwaiter().GetResult();
            Assert.Equal("电影", item.Name);
            Assert.Equal(EmbyItemType.Movie, item.Type);
            Assert.Equal(2020, item.ProductionYear);
            Assert.Equal(5400d, item.RunTimeTicks!.Value / (double)TimeSpan.TicksPerSecond);
            Assert.Equal(12d, item.ResumeTicks / (double)TimeSpan.TicksPerSecond);
            Assert.Equal("tt1", item.ProviderIds["Imdb"]);
            Assert.Equal("动画", item.Genres.Single());
            Assert.Equal("/library/metadata/10/thumb/1", item.ImageTags["Primary"]);
            Assert.Equal("/library/metadata/10/art/1", item.BackdropImageTags.Single());

            var source = item.MediaSources.Single();
            Assert.Equal("/media/test.mkv", source.Path);
            Assert.Equal("mkv", source.Container);
            Assert.Equal("/library/parts/20/1/file.mkv", source.DirectStreamUrl!.AbsolutePath);
            Assert.Equal("", source.DirectStreamUrl.Query);
            var video = source.PrimaryVideoStream!;
            Assert.Equal("hevc", video.Codec);
            Assert.Equal(1920, video.Width);
            // 动态范围：Plex 的 videoRange 是粗档位（HDR/SDR），DV 只有 DOVIPresent 这个布尔 —— 两个都带上
            // 才是「HDR/DV」，只认粗档位会把 DV 那半截丢掉（用户令 2026-10-25）。
            Assert.Equal("HDR", video.VideoRange);
            Assert.Equal("DOVI", video.VideoRangeType);
            Assert.Equal("HDR/DV", video.DynamicRangeLabel);
            Assert.Equal(2, source.DefaultAudioStreamIndex);
            var subtitle = source.SubtitleStreams.Single();
            Assert.True(subtitle.IsExternal);
            Assert.Equal("/library/streams/3", subtitle.DeliveryUrl);
        });
        Test("Plex 媒体：季、集与接下来播放都来自 children 和 onDeck", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var seasons = fixture.Session.Client.GetSeasonsAsync("30", default).GetAwaiter().GetResult();
            Assert.Equal("31", seasons.Single().Id);
            Assert.Equal(EmbyItemType.Season, seasons.Single().Type);

            var episodes = fixture.Session.Client.GetEpisodesAsync("30", null, default).GetAwaiter().GetResult();
            Assert.Equal("40,41", string.Join(',', episodes.Select(episode => episode.Id)));
            Assert.Equal("30", episodes[0].SeriesId);
            Assert.Equal("31", episodes[0].SeasonId);
            Assert.True(episodes[0].UserData!.Played);

            var next = fixture.Session.Client.GetNextUpAsync(1, default, "30").GetAwaiter().GetResult();
            Assert.Equal("41", next.Single().Id);
        });
        Test("Plex 媒体：详情进入播放规划，进度上报带 part key 与毫秒位置", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var item = fixture.Session.Client.GetItemAsync("10", default).GetAwaiter().GetResult();
            var source = item.MediaSources.Single();
            var planner = new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders));
            var request = planner.Plan(new PlaybackTicket
            { Item = item, Source = source, StartTicks = 12 * TimeSpan.TicksPerSecond }, fixture.Session.Connection!);

            Assert.Equal("/library/parts/20/1/file.mkv", request.MediaUrl.AbsolutePath);
            Assert.Equal("", request.MediaUrl.Query);
            Assert.Equal(12d, request.StartSeconds);
            Assert.Equal("X-Plex-Token", request.HttpHeaders[0].Key);
            Assert.Equal(fixture.Session.Connection!.AccessToken, request.HttpHeaders[0].Value);
            Assert.Equal("10", request.PlexSelection!.RatingKey);
            Assert.Equal("/library/parts/20/1/file.mkv", request.PlexSelection.PartKey);
            Assert.Equal("http://plex.invalid:32400/library/streams/3", request.ExternalSubtitles.Single().AbsoluteUri);

            var report = new PlaybackReport
            {
                ItemId = item.Id,
                PlaySessionId = "fixture",
                MediaSourceId = source.Id,
                PositionTicks = 23 * TimeSpan.TicksPerSecond,
                PlexSelection = request.PlexSelection
            };
            fixture.Session.Client.ReportPlaybackProgressAsync(report, default).GetAwaiter().GetResult();
            var timeline = fixture.Transport.Requests.Last();
            Assert.Equal("/:/timeline", timeline.AbsolutePath);
            Assert.Contains("ratingKey=10", timeline.Query);
            Assert.Contains("state=playing", timeline.Query);
            Assert.Contains("time=23000", timeline.Query);
            Assert.Contains("duration=5400000", timeline.Query);

            // A media address that leaves the server, or carries its own credentials, is refused rather
            // than handed to mpv.
            source.DirectStreamUrl = new Uri("https://external.invalid/file");
            Assert.Throws<InvalidOperationException>(() =>
                planner.Plan(new PlaybackTicket { Item = item, Source = source }, fixture.Session.Connection!));
        });
        Test("Plex 媒体：菜单只提供适用操作，收藏与删除不向服务器发请求", () =>
        {
            using var fixture = new Fixture(); fixture.Login();
            var commands = ItemMenu.For(new EmbyItem { Id = "10", Name = "电影", Type = EmbyItemType.Movie }, plex: true);
            Assert.True(commands.Where(row => row.Command is not null)
                .All(row => ItemMenu.Supports(row.Command!.Value, jellyfin: false, fnNas: false, plex: true)));
            Assert.False(commands.Any(row => row.Command is ItemCommand.Favorite
                || row.Command == ItemCommand.EditMetadata || row.Command == ItemCommand.Delete));

            var before = fixture.Transport.Requests.Count;
            Assert.Throws<NotSupportedException>(() =>
                fixture.Session.Client.SetFavoriteAsync("10", true, default).GetAwaiter().GetResult());
            Assert.Equal(before, fixture.Transport.Requests.Count);
        });
    }

    private sealed class Fixture : IDisposable
    {
        public AppSettings Settings { get; } = new();
        public Transport Transport { get; } = new();
        public EmbySession Session { get; }
        public ServerProfile Server { get; } = new() { ServerKind = MediaServerKind.Plex, Url = "http://plex.invalid:32400", Name = "Plex" };
        public AccountProfile Account { get; } = new() { Username = "fixture" };

        public Fixture()
        {
            Server.Accounts.Add(Account); Settings.Servers.Add(Server);
            Session = new EmbySession(Settings, new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "momoka-plex-" + Guid.NewGuid().ToString("N"))), PassthroughSecretProtector.Instance),
                new CredentialVault(PassthroughSecretProtector.Instance), DeviceIdentity.Create("fixture", "test"), Transport);
        }

        public void Login() => Session.SignInAsync(Server, Account, "fixture-password", "fixture", true, default).GetAwaiter().GetResult();
        public void Dispose() => Session.Dispose();
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public int Logins { get; private set; }
        public bool Expire { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host is not ("plex.invalid" or "plex.tv")) throw new InvalidOperationException("Unexpected host: " + uri.Host);
            Requests.Add(uri);

            if (uri.Host == "plex.tv")
            {
                Assert.Equal("/api/v2/users/signin", uri.AbsolutePath);
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.DoesNotContain("fixture-password", uri.AbsoluteUri);
                Assert.Contains("\"password\":\"fixture-password\"", body);
                return Raw("{\"user\":{\"id\":\"7\",\"username\":\"fixture\"},\"authToken\":\"fixture-token-" + ++Logins + "\"}");
            }

            // The other products' probes, and 飞牛's, all land here first and must miss.
            if (uri.AbsolutePath is "/System/Info/Public" or "/emby/System/Info/Public" or "/v/api/v1/sys/version")
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (Expire && uri.AbsolutePath == "/library/sections")
            {
                Expire = false;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            var container = uri.AbsolutePath switch
            {
                "/identity" => "{\"machineIdentifier\":\"machine\",\"version\":\"1.41.2\"}",
                "/" => "{\"friendlyName\":\"客厅 Plex\",\"version\":\"1.41.2\",\"machineIdentifier\":\"machine\"}",
                "/library/sections" => "{\"Directory\":["
                    + "{\"key\":\"1\",\"title\":\"电影\",\"type\":\"movie\"},"
                    + "{\"key\":\"2\",\"title\":\"剧集\",\"type\":\"show\"},"
                    + "{\"key\":\"3\",\"title\":\"音乐\",\"type\":\"artist\"}]}",
                "/library/sections/1/all" => "{\"size\":1,\"totalSize\":1,\"Metadata\":[" + Movie + "]}",
                "/search" => "{\"size\":2,\"totalSize\":2,\"Metadata\":[" + Movie
                    + ",{\"ratingKey\":\"30\",\"type\":\"show\",\"title\":\"剧\"}]}",
                "/library/metadata/10" => "{\"size\":1,\"Metadata\":[" + Movie + "]}",
                "/library/metadata/30/children" => "{\"size\":1,\"Metadata\":[{\"ratingKey\":\"31\",\"type\":\"season\",\"title\":\"第 1 季\",\"index\":1,\"parentRatingKey\":\"30\",\"leafCount\":2}]}",
                "/library/metadata/31/children" => "{\"size\":2,\"Metadata\":["
                    + "{\"ratingKey\":\"40\",\"type\":\"episode\",\"title\":\"第一集\",\"index\":1,\"parentIndex\":1,\"parentRatingKey\":\"31\",\"grandparentRatingKey\":\"30\",\"grandparentTitle\":\"剧\",\"viewCount\":1},"
                    + "{\"ratingKey\":\"41\",\"type\":\"episode\",\"title\":\"第二集\",\"index\":2,\"parentIndex\":1,\"parentRatingKey\":\"31\",\"grandparentRatingKey\":\"30\",\"grandparentTitle\":\"剧\"}]}",
                "/library/onDeck" => "{\"size\":1,\"totalSize\":1,\"Metadata\":[{\"ratingKey\":\"41\",\"type\":\"episode\",\"title\":\"第二集\",\"index\":2,\"parentIndex\":1,\"parentRatingKey\":\"31\",\"grandparentRatingKey\":\"30\",\"grandparentTitle\":\"剧\"}]}",
                "/:/timeline" => "{\"size\":0}",
                "/:/scrobble" => "{\"size\":0}",
                "/:/unscrobble" => "{\"size\":0}",
                _ => throw new InvalidOperationException("Unexpected fixture path: " + uri.AbsolutePath)
            };
            return Raw("{\"MediaContainer\":" + container + "}");
        }

        /// <summary>One film with one file, a video track, a default audio track and an external subtitle.</summary>
        private const string Movie = "{\"ratingKey\":\"10\",\"type\":\"movie\",\"title\":\"电影\",\"year\":2020,"
            + "\"duration\":5400000,\"viewOffset\":12000,\"viewCount\":0,"
            + "\"thumb\":\"/library/metadata/10/thumb/1\",\"art\":\"/library/metadata/10/art/1\","
            + "\"Guid\":[{\"id\":\"imdb://tt1\"}],\"Genre\":[{\"tag\":\"动画\"}],"
            + "\"Media\":[{\"bitrate\":8000,\"duration\":5400000,\"Part\":[{"
            + "\"id\":\"20\",\"key\":\"/library/parts/20/1/file.mkv\",\"file\":\"/media/test.mkv\",\"container\":\"mkv\",\"size\":1234,"
            + "\"Stream\":["
            + "{\"id\":\"1\",\"streamType\":\"1\",\"codec\":\"hevc\",\"width\":1920,\"height\":1080,\"videoRange\":\"HDR\",\"DOVIPresent\":\"1\"},"
            + "{\"id\":\"2\",\"streamType\":\"2\",\"codec\":\"aac\",\"channels\":2,\"default\":\"1\"},"
            + "{\"id\":\"3\",\"streamType\":\"3\",\"codec\":\"srt\",\"language\":\"zh\",\"key\":\"/library/streams/3\"}]}]}]}";

        private static HttpResponseMessage Raw(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
