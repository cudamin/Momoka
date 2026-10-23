using System.Net;
using System.Text.Json;
using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>R-035：真实 Core FindAsync 配匿名 HTTP 回话；不登录、不访问文件或启动播放。</summary>
internal static class MoviePilotEpisodeGroupPlaybackTests
{
    private const string ProviderRoute = "Users/fixture-user/Items";
    private const string LibraryPath = "/library/fixture-show/Season 02/01.mkv";
    private static int _requests;

    internal static int Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Register();
        var code = TestHarness.Run();
        Console.WriteLine($"R-035 实际假 HTTP 请求：{_requests} 次；无真实网络、登录、凭据 IO 或播放。");
        return code;
    }

    internal static void Register()
    {
        GroupTests();
        PreservedTests();
        JellyfinTests();
        BoundaryTests();
    }

    private static void GroupTests()
    {
        Test("R-035 非默认组：唯一同数字但路径不符，不给目标也不信 itemid", () =>
        {
            var show = Series();
            var wrong = Episode("wrong-01", 1, 1, "/library/wrong/01.mkv");
            using var fixture = new Fixture(show).Episodes(show, wrong).Item(wrong);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath, wrong.Id)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 非默认组：组 S01E01 找到原始 S02E01，并保留原始季上下文", () =>
        {
            var show = Series();
            var wrong = Episode("wrong-01", 1, 1, "/library/wrong/01.mkv");
            var correct = Episode("original-02-01", 2, 1, LibraryPath);
            var next = Episode("original-02-02", 2, 2, "/library/02.mkv");
            var folder = new EmbyItem { Id = "not-episode", Type = EmbyItemType.Folder, ParentIndexNumber = 2, Path = LibraryPath };
            using var fixture = new Fixture(show).Episodes(show, wrong, correct, folder, next);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Target(targets, 1, correct, show, correct, next);
        });
        Test("R-035 非默认组：原始集号也不相同，组内 E01 不限制候选", () =>
        {
            var show = Series();
            var correct = Episode("original-02-07", 2, 7, LibraryPath);
            using var fixture = new Fixture(show).Episodes(show, correct);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Target(targets, 1, correct, show, correct);
        });
        Test("R-035 非默认组：无组季号时仍只能凭已核主身份与唯一路径定位", () =>
        {
            var show = Series();
            var correct = Episode("original-02-01", 2, 1, LibraryPath);
            using var fixture = new Fixture(show).Episodes(show, correct);
            var targets = fixture.Find(Subscription(season: null), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Target(targets, 1, correct, show, correct);
        });
        Test("R-035 非默认组：多个同主身份系列，唯一路径选对 Parent，不查询错身份", () =>
        {
            var first = Series();
            var second = Series("show-b");
            var foreign = Series("foreign-show", "Douban");
            var wrongType = Series("movie-not-show");
            wrongType.Type = EmbyItemType.Movie;
            var wrongId = Series("wrong-id-show", mediaId: "99");
            var wrong = Episode("wrong-01", 1, 1, "/other/01.mkv");
            var correct = Episode("correct-b-01", 2, 1, LibraryPath, second.Id);
            var next = Episode("correct-b-02", 2, 2, "/library/02.mkv", second.Id);
            using var fixture = new Fixture(first, second, foreign, wrongType, wrongId)
                .Episodes(first, wrong).Episodes(second, correct, next);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(first), EpisodesRoute(second));
            Target(targets, 1, correct, second, correct, next);
        });
        Test("R-035 非默认组：两部同主身份系列同路径，不借 itemid 消除歧义", () =>
        {
            var first = Series();
            var second = Series("show-b");
            var one = Episode("duplicate-a", 1, 1, LibraryPath);
            var two = Episode("duplicate-b", 1, 1, LibraryPath, second.Id);
            using var fixture = new Fixture(first, second).Episodes(first, one).Episodes(second, two).Item(one);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath, one.Id)));
            Requests(fixture, ProviderRoute, EpisodesRoute(first), EpisodesRoute(second));
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 非默认组：同剧跨季同路径歧义，数字唯一也不得接受", () =>
        {
            var show = Series();
            var one = Episode("duplicate-01", 1, 1, LibraryPath);
            var two = Episode("duplicate-02", 2, 1, LibraryPath);
            using var fixture = new Fixture(show).Episodes(show, one, two).Item(one);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath, one.Id)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 非默认组：多个入库路径命中不同目标，不取第一条", () =>
        {
            var show = Series();
            var one = Episode("candidate-01", 1, 1, "/library/one.mkv");
            var two = Episode("candidate-02", 2, 1, LibraryPath);
            using var fixture = new Fixture(show).Episodes(show, one, two).Item(one);
            var targets = fixture.Find(Subscription(), Files(Entry(one.Path!, one.Id), Entry(LibraryPath, two.Id)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 非默认组：重复入库记录仍为一个目标，按候选而非路径记录计歧义", () =>
        {
            var show = Series();
            var correct = Episode("original-02-01", 2, 1, LibraryPath);
            using var fixture = new Fixture(show).Episodes(show, correct);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath), Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Target(targets, 1, correct, show, correct);
        });
        Test("R-035 非默认组：LibraryFiles 有显示内容但无 LibraryEntries，不数字兜底", () =>
        {
            var show = Series();
            var item = Episode("numeric-01", 1, 1, LibraryPath);
            using var fixture = new Fixture(show).Episodes(show, item);
            var targets = fixture.Find(Subscription(), new([new(1, "", "", [LibraryPath], [])]));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Assert.Equal(0, targets.Count);
        });
        foreach (var path in new[] { "", "   " })
        {
            Test($"R-035 非默认组：{(path.Length == 0 ? "空" : "空白")}入库路径加 itemid 不可信", () =>
            {
                var show = Series();
                var item = Episode("numeric-01", 1, 1, LibraryPath);
                using var fixture = new Fixture(show).Episodes(show, item).Item(item);
                var targets = fixture.Find(Subscription(), Files(Entry(path, item.Id)));
                Requests(fixture, ProviderRoute, EpisodesRoute(show));
                Assert.Equal(0, targets.Count);
            });
        }
        Test("R-035 非默认组：仅 MediaSources.Path 可跨原始季集定位", () =>
        {
            var show = Series();
            var item = Episode("source-path-07", 2, 7, null);
            item.MediaSources = [new MediaSource { Id = "fixture-source", Path = LibraryPath }];
            using var fixture = new Fixture(show).Episodes(show, item);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Target(targets, 1, item, show, item);
        });
        Test("R-035 非默认组：有媒体源却没有任何媒体路径，不数字兜底", () =>
        {
            var show = Series();
            var item = Episode("source-without-path", 1, 1, null);
            item.MediaSources = [new MediaSource { Id = "fixture-source" }];
            using var fixture = new Fixture(show).Episodes(show, item);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Assert.Equal(0, targets.Count);
        });
        foreach (var (name, library, candidate, accepted) in new[]
        {
            ("Windows 驱动器大小写与斜线", @"D:\Library\Fixture Show\S02E01.mkv", "d:/library/fixture show/s02e01.mkv", true),
            ("Windows UNC 大小写与斜线", @"\\fixture-nas\Library\S02E01.mkv", "//FIXTURE-NAS/library/s02e01.mkv", true),
            ("Unix 精确大小写", "/library/Fixture Show/E01.mkv", "/library/Fixture Show/E01.mkv", true),
            ("Unix 大小写不符", "/library/Fixture Show/E01.mkv", "/library/fixture show/e01.mkv", false),
            ("Unix 同名不同目录", "/library/first/E01.mkv", "/library/second/E01.mkv", false),
            ("Windows 同名不同驱动器", @"D:\Library\E01.mkv", @"E:\Library\E01.mkv", false)
        })
        {
            Test($"R-035 非默认组路径：{name}，只比较字符串不访问路径", () =>
            {
                var show = Series();
                var item = Episode("path-candidate", accepted ? 2 : 1, 1, candidate);
                using var fixture = new Fixture(show).Episodes(show, item);
                var targets = fixture.Find(Subscription(), Files(Entry(library)));
                Requests(fixture, ProviderRoute, EpisodesRoute(show));
                if (accepted) Target(targets, 1, item, show, item);
                else Assert.Equal(0, targets.Count);
            });
        }
        Test("R-035 非默认组：旧字符串 library 经真实解析保留路径并跨季定位", () =>
        {
            var subscription = Subscription();
            var data = JsonSerializer.SerializeToElement(new
            {
                subscribe = subscription.Data,
                episodes = new Dictionary<string, object> { ["1"] = new { library = LibraryPath } }
            });
            var files = MoviePilotSubscriptionFiles.Parse(data, subscription);
            var show = Series();
            var item = Episode("parsed-original", 2, 1, LibraryPath);
            using var fixture = new Fixture(show).Episodes(show, item);
            var targets = fixture.Find(subscription, files);
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Target(targets, 1, item, show, item);
        });
        foreach (var (source, provider, id) in new[]
        {
            ("douban", "Douban", "42"), ("imdb", "Imdb", "tt42"), ("tvdb", "Tvdb", "42")
        })
        {
            Test($"R-035 非默认组：{provider} 主身份与精确查询保留", () =>
            {
                var show = Series(provider: provider.ToLowerInvariant(), mediaId: id);
                var item = Episode("provider-original", 2, 1, LibraryPath);
                using var fixture = new Fixture(show).Episodes(show, item);
                var targets = fixture.Find(Subscription(source: source, mediaId: id), Files(Entry(LibraryPath)));
                Requests(fixture, ProviderRoute, EpisodesRoute(show));
                Assert.Contains($"AnyProviderIdEquals={provider}.{id}", fixture.Transport.Only(ProviderRoute + "?").Url);
                Target(targets, 1, item, show, item);
            });
        }
    }

    private static void PreservedTests()
    {
        foreach (var group in new string?[] { null, "", "   " })
        {
            Test($"R-035 默认组：{(group is null ? "未设置" : group.Length == 0 ? "空组" : "空白组")}保留无路径数字唯一语义", () =>
            {
                var show = Series();
                var item = Episode("default-01", 1, 1, "/library/default/01.mkv");
                var other = Episode("original-02", 2, 1, LibraryPath);
                using var fixture = new Fixture(show).Episodes(show, item, other);
                var targets = fixture.Find(Subscription(group: group), new([new(1, "", "", ["fixture library item"], [])]));
                Requests(fixture, ProviderRoute, EpisodesRoute(show));
                Target(targets, 1, item, show, item);
            });
        }
        Test("R-035 默认组：按默认季号选数字唯一条目，不改成跨季路径规则", () =>
        {
            var show = Series();
            var item = Episode("default-01", 1, 1, "/library/default/01.mkv");
            var other = Episode("original-02", 2, 1, LibraryPath);
            using var fixture = new Fixture(show).Episodes(show, item, other);
            var targets = fixture.Find(Subscription(group: ""), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Target(targets, 1, item, show, item);
        });
        Test("R-035 默认组：IndexNumberEnd 合集范围语义保留", () =>
        {
            var show = Series();
            var item = Episode("default-02-03", 1, 2, LibraryPath);
            item.IndexNumberEnd = 3;
            using var fixture = new Fixture(show).Episodes(show, item);
            var targets = fixture.Find(Subscription(group: ""), Files(3, Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Target(targets, 3, item, show, item);
        });
        Test("R-035 默认组：多数字候选按唯一路径选对系列", () =>
        {
            var first = Series();
            var second = Series("show-b");
            var wrong = Episode("default-a", 1, 1, "/other/01.mkv");
            var item = Episode("default-b", 1, 1, LibraryPath, second.Id);
            using var fixture = new Fixture(first, second).Episodes(first, wrong).Episodes(second, item);
            var targets = fixture.Find(Subscription(group: ""), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute, EpisodesRoute(first), EpisodesRoute(second));
            Target(targets, 1, item, second, item);
        });
        Test("R-035 默认组：未知 provider 的旧 itemid 兜底仍须数字及路径同时匹配", () =>
        {
            var item = Episode("legacy-item", 1, 1, LibraryPath);
            using var fixture = new Fixture().Item(item);
            var targets = fixture.Find(Subscription(group: "", source: "unknown-fixture"), Files(Entry(LibraryPath, item.Id)));
            Requests(fixture, ItemRoute(item.Id));
            Target(targets, 1, item, null);
        });
        Test("R-035 默认组：外服同 itemid 不同路径不得接受", () =>
        {
            var item = Episode("legacy-item", 1, 1, "/other/01.mkv");
            using var fixture = new Fixture().Item(item);
            var targets = fixture.Find(Subscription(group: ""), Files(Entry(LibraryPath, item.Id)));
            Requests(fixture, ProviderRoute, ItemRoute(item.Id));
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 电影：零号电影及主 provider 唯一候选不受 episode_group 限制", () =>
        {
            var movie = Series("movie");
            movie.Type = EmbyItemType.Movie;
            movie.Path = LibraryPath;
            using var fixture = new Fixture(movie);
            var targets = fixture.Find(Subscription(series: false), Files(0, Entry(LibraryPath)));
            Requests(fixture, ProviderRoute);
            Assert.Contains("IncludeItemTypes=Movie", fixture.Transport.Only(ProviderRoute + "?").Url);
            Target(targets, 0, movie, null);
        });
        Test("R-035 电影：多同身份候选仍按唯一路径匹配", () =>
        {
            var wrong = Series("movie-a");
            wrong.Type = EmbyItemType.Movie;
            wrong.Path = "/other/movie.mkv";
            var movie = Series("movie-b");
            movie.Type = EmbyItemType.Movie;
            movie.Path = LibraryPath;
            using var fixture = new Fixture(wrong, movie);
            var targets = fixture.Find(Subscription(series: false), Files(0, Entry(LibraryPath)));
            Requests(fixture, ProviderRoute);
            Target(targets, 0, movie, null);
        });
        Test("R-035 电影：非零号条目不给电影目标", () =>
        {
            var movie = Series("movie");
            movie.Type = EmbyItemType.Movie;
            movie.Path = LibraryPath;
            using var fixture = new Fixture(movie);
            var targets = fixture.Find(Subscription(series: false), Files(Entry(LibraryPath)));
            Requests(fixture, ProviderRoute);
            Assert.Equal(0, targets.Count);
        });
    }

    private static void JellyfinTests()
    {
        Test("R-035 Jellyfin 补修：两页本地 provider 匹配→两系列 Episodes→跨季唯一路径", () =>
        {
            var first = Series("jf-show-a");
            var second = Series("jf-show-b");
            var page = UnrelatedJellyfinPage();
            page[197] = Series("jf-wrong-type");
            page[197].Type = EmbyItemType.Movie;
            page[198] = Series("jf-wrong-id", mediaId: "99");
            page[199] = first;
            var wrong = Episode("jf-wrong-01", 1, 1, "/other/01.mkv", first.Id);
            var correct = Episode("jf-original-02-01", 2, 1, LibraryPath, second.Id);
            var next = Episode("jf-original-02-02", 2, 2, "/library/02.mkv", second.Id);
            using var fixture = new Fixture(MediaServerKind.Jellyfin)
                .JellyfinPage(0, 201, page).JellyfinPage(200, 201, second)
                .Episodes(first, wrong).Episodes(second, correct, next);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath)));
            JellyfinRequests(fixture, true, "Items", "Items", EpisodesRoute(first), EpisodesRoute(second));
            Target(targets, 1, correct, second, correct, next);
        });
        Test("R-035 Jellyfin 补修：默认组保留数字唯一及原始季上下文", () =>
        {
            var show = Series("jf-show");
            var item = Episode("jf-default-01", 1, 1, "/library/default/01.mkv", show.Id);
            var other = Episode("jf-original-02", 2, 1, LibraryPath, show.Id);
            using var fixture = new Fixture(MediaServerKind.Jellyfin, show).Episodes(show, item, other);
            var targets = fixture.Find(Subscription(group: ""), new([new(1, "", "", ["fixture library item"], [])]));
            JellyfinRequests(fixture, true, "Items", EpisodesRoute(show));
            Target(targets, 1, item, show, item);
        });
        Test("R-035 Jellyfin 补修：电影 provider 分页及零号定位正常，无 Episodes 请求", () =>
        {
            var wrong = Series("jf-movie-a");
            wrong.Type = EmbyItemType.Movie;
            wrong.Path = "/other/movie.mkv";
            var correct = Series("jf-movie-b");
            correct.Type = EmbyItemType.Movie;
            correct.Path = LibraryPath;
            var page = UnrelatedJellyfinPage();
            foreach (var media in page) media.Type = EmbyItemType.Movie;
            page[199] = wrong;
            using var fixture = new Fixture(MediaServerKind.Jellyfin)
                .JellyfinPage(0, 201, page).JellyfinPage(200, 201, correct);
            var targets = fixture.Find(Subscription(series: false), Files(0, Entry(LibraryPath)));
            JellyfinRequests(fixture, false, "Items", "Items");
            Target(targets, 0, correct, null);
        });
        Test("R-035 Jellyfin 补修：非默认组数字唯一但路径不符仍无目标，不查询 itemid", () =>
        {
            var show = Series("jf-show");
            var wrong = Episode("jf-wrong-01", 1, 1, "/other/01.mkv", show.Id);
            using var fixture = new Fixture(MediaServerKind.Jellyfin, show).Episodes(show, wrong).Item(wrong);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath, wrong.Id)));
            JellyfinRequests(fixture, true, "Items", EpisodesRoute(show));
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 Jellyfin 补修：同主身份多系列同路径仍拒绝，不借 itemid 解歧义", () =>
        {
            var first = Series("jf-show-a");
            var second = Series("jf-show-b");
            var one = Episode("jf-duplicate-a", 1, 1, LibraryPath, first.Id);
            var two = Episode("jf-duplicate-b", 2, 1, LibraryPath, second.Id);
            using var fixture = new Fixture(MediaServerKind.Jellyfin, first, second)
                .Episodes(first, one).Episodes(second, two).Item(one);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath, one.Id)));
            JellyfinRequests(fixture, true, "Items", EpisodesRoute(first), EpisodesRoute(second));
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 Jellyfin 补修：无 LibraryEntries 仍不接受数字唯一候选", () =>
        {
            var show = Series("jf-show");
            var item = Episode("jf-numeric-01", 1, 1, LibraryPath, show.Id);
            using var fixture = new Fixture(MediaServerKind.Jellyfin, show).Episodes(show, item);
            var targets = fixture.Find(Subscription(), new([new(1, "", "", [LibraryPath], [])]));
            JellyfinRequests(fixture, true, "Items", EpisodesRoute(show));
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 Jellyfin 补修：两页错误主 provider 不读后代，不走 itemid 兜底", () =>
        {
            var foreign = Series("jf-foreign", "Douban");
            var item = Episode("jf-unverified-item", 1, 1, LibraryPath, foreign.Id);
            using var fixture = new Fixture(MediaServerKind.Jellyfin)
                .JellyfinPage(0, 201, UnrelatedJellyfinPage()).JellyfinPage(200, 201, foreign).Item(item);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath, item.Id)));
            JellyfinRequests(fixture, true, "Items", "Items");
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 Jellyfin 补修：第一页读取取消，不发第二页或 Episodes", () =>
        {
            using var cancellation = new CancellationTokenSource();
            using var fixture = new Fixture(MediaServerKind.Jellyfin)
                .JellyfinPage(0, 201, UnrelatedJellyfinPage()).JellyfinPage(200, 201, Series("jf-show"));
            fixture.Transport.When("StartIndex=0&", cancellation.Cancel);
            Assert.Throws<OperationCanceledException>(() => fixture.Find(Subscription(), Files(Entry(LibraryPath)), cancellation.Token));
            JellyfinRequests(fixture, true, "Items");
        });
        Test("R-035 Jellyfin 补修：第一系列 Episodes 取消，不读第二系列或 itemid", () =>
        {
            var first = Series("jf-show-a");
            var second = Series("jf-show-b");
            var item = Episode("jf-original", 2, 1, LibraryPath, first.Id);
            using var cancellation = new CancellationTokenSource();
            using var fixture = new Fixture(MediaServerKind.Jellyfin, first, second)
                .Episodes(first, item).Episodes(second, item).Item(item);
            fixture.Transport.When(EpisodesRoute(first), cancellation.Cancel);
            Assert.Throws<OperationCanceledException>(() => fixture.Find(Subscription(), Files(Entry(LibraryPath, item.Id)), cancellation.Token));
            JellyfinRequests(fixture, true, "Items", EpisodesRoute(first));
        });
    }

    private static EmbyItem[] UnrelatedJellyfinPage() =>
        Enumerable.Range(0, 200).Select(index => Series($"jf-unrelated-{index}", "Douban")).ToArray();

    private static void JellyfinRequests(Fixture fixture, bool series, params string[] paths)
    {
        var sent = fixture.Transport.SentTo("");
        Assert.Equal(paths.Length, sent.Count, "Jellyfin 精确实际请求数，含未脚本化请求");
        Assert.Equal(paths.Length, fixture.Transport.Total, "所有 Jellyfin 请求须命中本地匿名夹具");
        var offset = 0;
        for (var index = 0; index < paths.Length; index++)
        {
            var request = sent[index];
            var uri = new Uri(request.Url);
            Assert.Equal("GET", request.Method);
            Assert.Equal("", request.Body);
            Assert.Equal("jellyfin.invalid", uri.Host);
            Assert.Equal("/" + paths[index], uri.AbsolutePath, "现有 Jellyfin 12 本地 route，不套 /emby 前缀");
            var query = uri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
            Assert.Equal("fixture-user", query["UserId"]);
            Assert.Equal(JellyfinQuery.FilterFields(EmbyFields.Detail), query["Fields"]);
            Assert.False(query.ContainsKey("AnyProviderIdEquals"), "Jellyfin 用实际本地 provider 过滤，不能靠服务器忽略的参数");
            if (paths[index] == "Items")
            {
                Assert.Equal(offset.ToString(), query["StartIndex"], "分页偏移必须实际前进");
                offset += 200;
                Assert.Equal("200", query["Limit"]);
                Assert.Equal("true", query["Recursive"]);
                Assert.Equal(series ? "Series" : "Movie", query["IncludeItemTypes"]);
            }
            else
            {
                Assert.False(query.ContainsKey("SeasonId"), "取全剧后代，由路径唯一定位原始季");
                Assert.Equal("Ascending", query["SortOrder"]);
            }
        }
    }

    private static void BoundaryTests()
    {
        foreach (var (name, source, mediaId) in new[]
        {
            ("未知 provider", "unknown-fixture", "42"), ("非法 TMDB 编号", "themoviedb", "42x"),
            ("非法 IMDb 编号", "imdb", "42")
        })
        {
            Test($"R-035 非默认组边界：{name}不得凭 itemid 绕过主媒体核对，零请求", () =>
            {
                var item = Episode("unverified-item", 1, 1, LibraryPath);
                using var fixture = new Fixture().Item(item);
                var targets = fixture.Find(Subscription(source: source, mediaId: mediaId), Files(Entry(LibraryPath, item.Id)));
                Requests(fixture);
                Assert.Equal(0, targets.Count);
            });
        }
        Test("R-035 非默认组边界：无主媒体候选，不单独接受路径相同的 itemid", () =>
        {
            var item = Episode("unverified-item", 1, 1, LibraryPath);
            using var fixture = new Fixture().Item(item);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath, item.Id)));
            Requests(fixture, ProviderRoute);
            Assert.Equal(0, targets.Count);
        });
        Test("R-035 非默认组边界：错 provider 或错类型不取后代，不走文件 ID 兜底", () =>
        {
            var foreign = Series("foreign-show", "Douban");
            var wrongType = Series("wrong-type");
            wrongType.Type = EmbyItemType.Movie;
            var item = Episode("unverified-item", 1, 1, LibraryPath, foreign.Id);
            using var fixture = new Fixture(foreign, wrongType).Item(item);
            var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath, item.Id)));
            Requests(fixture, ProviderRoute);
            Assert.Equal(0, targets.Count);
        });
        foreach (var files in new[]
        {
            new MoviePilotSubscriptionFiles([]),
            new MoviePilotSubscriptionFiles([new(1, "", "", [], [LibraryPath]) { LibraryEntries = [Entry(LibraryPath, "item")] }])
        })
        {
            Test($"R-035 未入库：{(files.Episodes.Count == 0 ? "无条目" : "仅下载")}零请求且无目标", () =>
            {
                using var fixture = new Fixture(Series());
                var targets = fixture.Find(Subscription(), files);
                Requests(fixture);
                Assert.Equal(0, targets.Count);
            });
        }
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex })
        {
            Test($"R-035 产品边界：{kind} 不走当前 Emby 入库定位，零请求", () =>
            {
                using var fixture = new Fixture(kind);
                var targets = fixture.Find(Subscription(), Files(Entry(LibraryPath)));
                Requests(fixture);
                Assert.Equal(0, targets.Count);
            });
        }
        Test("R-035 取消：进入 FindAsync 前取消，零请求", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var fixture = new Fixture(Series());
            Assert.Throws<OperationCanceledException>(() => fixture.Find(Subscription(), Files(Entry(LibraryPath)), cancellation.Token));
            Requests(fixture);
        });
        Test("R-035 取消：provider 回话取消，不取任何后代", () =>
        {
            var show = Series();
            using var cancellation = new CancellationTokenSource();
            using var fixture = new Fixture(show).Episodes(show, Episode("original", 2, 1, LibraryPath));
            fixture.Transport.When(ProviderRoute + "?", cancellation.Cancel);
            Assert.Throws<OperationCanceledException>(() => fixture.Find(Subscription(), Files(Entry(LibraryPath)), cancellation.Token));
            Requests(fixture, ProviderRoute);
        });
        Test("R-035 取消：第一系列后代回话取消，不读第二系列或 itemid", () =>
        {
            var first = Series();
            var second = Series("show-b");
            var item = Episode("original", 2, 1, LibraryPath);
            using var cancellation = new CancellationTokenSource();
            using var fixture = new Fixture(first, second).Episodes(first, item).Episodes(second, item).Item(item);
            fixture.Transport.When(EpisodesRoute(first), cancellation.Cancel);
            Assert.Throws<OperationCanceledException>(() => fixture.Find(Subscription(), Files(Entry(LibraryPath, item.Id)), cancellation.Token));
            Requests(fixture, ProviderRoute, EpisodesRoute(first));
        });
        Test("R-035 取消：默认组 itemid 读取取消照实传播", () =>
        {
            var item = Episode("legacy-item", 1, 1, LibraryPath);
            using var cancellation = new CancellationTokenSource();
            using var fixture = new Fixture().Item(item);
            fixture.Transport.When(ItemRoute(item.Id) + "?", cancellation.Cancel);
            Assert.Throws<OperationCanceledException>(() => fixture.Find(Subscription(group: "", source: "unknown-fixture"),
                Files(Entry(LibraryPath, item.Id)), cancellation.Token));
            Requests(fixture, ItemRoute(item.Id));
        });
        Test("R-035 后代权限拒绝不降级为 itemid 目标", () =>
        {
            var show = Series();
            var item = Episode("unverified-item", 1, 1, LibraryPath);
            using var fixture = new Fixture(show).Item(item);
            fixture.Transport.Fail(EpisodesRoute(show), HttpStatusCode.Forbidden);
            var error = Assert.Catch<EmbyApiException>(() => fixture.Find(Subscription(), Files(Entry(LibraryPath, item.Id))));
            Requests(fixture, ProviderRoute, EpisodesRoute(show));
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        });
    }

    private static MoviePilotSubscription Subscription(string? group = "fixture-group", string source = "themoviedb",
        string mediaId = "42", int? season = 1, bool series = true) =>
        MoviePilotSubscription.Parse(JsonSerializer.SerializeToElement(new
        {
            id = 35,
            name = "匿名夹具作品",
            type = series ? "电视剧" : "电影",
            media_source = source,
            media_id = mediaId,
            season,
            episode_group = group,
            total_episode = 3,
            start_episode = 1
        }))!;

    private static EmbyItem Series(string id = "show-a", string provider = "Tmdb", string mediaId = "42") =>
        new() { Id = id, Type = EmbyItemType.Series, ProviderIds = new() { [provider] = mediaId } };
    private static EmbyItem Episode(string id, int season, int number, string? path, string series = "show-a") =>
        new() { Id = id, Type = EmbyItemType.Episode, ParentIndexNumber = season, IndexNumber = number, Path = path, SeriesId = series };
    private static MoviePilotSubscriptionLibraryFile Entry(string path, string itemId = "") => new(path, "emby", itemId);
    private static MoviePilotSubscriptionFiles Files(params MoviePilotSubscriptionLibraryFile[] entries) => Files(1, entries);
    private static MoviePilotSubscriptionFiles Files(int number, params MoviePilotSubscriptionLibraryFile[] entries) =>
        new([new(number, "", "", entries.Select(entry => entry.Path.Length > 0 ? entry.Path : $"fixture item {entry.ItemId}").ToArray(), [])
        { LibraryEntries = entries }]);
    private static string EpisodesRoute(EmbyItem series) => $"Shows/{series.Id}/Episodes";
    private static string ItemRoute(string id) => $"{ProviderRoute}/{id}";

    private static void Requests(Fixture fixture, params string[] paths)
    {
        var sent = fixture.Transport.SentTo("");
        Assert.Equal(paths.Length, sent.Count, "含未脚本化请求的实际请求数");
        Assert.Equal(paths.Length, fixture.Transport.Total, "所有请求必须命中匿名脚本");
        for (var index = 0; index < paths.Length; index++)
        {
            var request = sent[index];
            var uri = new Uri(request.Url);
            Assert.Equal("GET", request.Method);
            Assert.Equal("", request.Body);
            Assert.Equal("/emby/" + paths[index], uri.AbsolutePath, "精确端点及顺序");
            var query = Uri.UnescapeDataString(uri.Query);
            Assert.Contains("Fields=" + EmbyFields.Detail, query);
            if (paths[index] == ProviderRoute)
            {
                Assert.Contains("Recursive=true", query);
                Assert.Contains("AnyProviderIdEquals=", query);
            }
            if (paths[index].StartsWith("Shows/", StringComparison.Ordinal))
            {
                Assert.Contains("UserId=fixture-user", query);
                Assert.DoesNotContain("SeasonId=", query);
            }
        }
    }

    private static void Target(IReadOnlyDictionary<int, MoviePilotPlaybackTarget> targets, int number,
        EmbyItem item, EmbyItem? parent, params EmbyItem[] episodes)
    {
        Assert.Equal(1, targets.Count);
        Assert.True(targets.TryGetValue(number, out var target), "字典键仍是所选 MoviePilot 组内集号");
        Assert.NotNull(target);
        SameItem(item, target!.Item);
        if (parent is null) Assert.Null(target.Parent);
        else { Assert.NotNull(target.Parent); SameItem(parent, target.Parent!); }
        Assert.Equal(episodes.Length, target.Episodes.Count, "原始季的完整上下文");
        for (var index = 0; index < episodes.Length; index++) SameItem(episodes[index], target.Episodes[index]);
    }

    private static void SameItem(EmbyItem expected, EmbyItem actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.ParentIndexNumber, actual.ParentIndexNumber);
        Assert.Equal(expected.IndexNumber, actual.IndexNumber);
        Assert.Equal(expected.IndexNumberEnd, actual.IndexNumberEnd);
        Assert.Equal(expected.SeriesId, actual.SeriesId);
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(JsonSerializer.Serialize(expected.ProviderIds), JsonSerializer.Serialize(actual.ProviderIds));
        Assert.Equal(string.Join('|', expected.MediaSources.Select(source => source.Path)),
            string.Join('|', actual.MediaSources.Select(source => source.Path)));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly EmbyHttp _http;
        internal StubTransport Transport { get; } = new();
        internal EmbyClient Client { get; }

        internal Fixture(params EmbyItem[] media) : this(MediaServerKind.Emby, media) { }
        internal Fixture(MediaServerKind kind, params EmbyItem[] media)
        {
            if (kind == MediaServerKind.Jellyfin) JellyfinPage(0, media.Length, media);
            else Transport.Answer(ProviderRoute + "?", Items(media));
            _http = new EmbyHttp(Transport);
            Client = new EmbyClient(_http, new EmbyConnection(new Uri(kind == MediaServerKind.Jellyfin
                ? "https://jellyfin.invalid/" : "https://emby.invalid/emby/"), "fixture-token",
                "fixture-user", "fixture", "fixture", DeviceIdentity.Create("fixture", "test"))
            { ServerKind = kind });
        }
        internal Fixture JellyfinPage(int offset, int total, params EmbyItem[] media)
        {
            Transport.Answer($"StartIndex={offset}&", JsonSerializer.Serialize(new ItemsResult { Items = [.. media], TotalRecordCount = total }));
            return this;
        }
        internal Fixture Episodes(EmbyItem series, params EmbyItem[] episodes)
        {
            Transport.Answer(EpisodesRoute(series), Items(episodes));
            return this;
        }
        internal Fixture Item(EmbyItem item)
        {
            Transport.Answer(ItemRoute(item.Id) + "?", JsonSerializer.Serialize(item));
            return this;
        }
        internal IReadOnlyDictionary<int, MoviePilotPlaybackTarget> Find(MoviePilotSubscription subscription,
            MoviePilotSubscriptionFiles files, CancellationToken cancellationToken = default) =>
            MoviePilotLibraryPlayback.FindAsync(Client, subscription, files, cancellationToken).GetAwaiter().GetResult();
        public void Dispose()
        {
            _requests += Transport.SentTo("").Count;
            _http.Dispose();
        }
        private static string Items(EmbyItem[] items) => JsonSerializer.Serialize(new ItemsResult { Items = [.. items], TotalRecordCount = items.Length });
    }
}
