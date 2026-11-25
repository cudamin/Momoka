using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Momoka.Metadata;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class TmdbLocalMediaTests
{
    public static void Register()
    {
        ParsingTests();
        MatchingTests();
        HttpTests();
        ImageTests();
        TimeoutTests();
    }

    private static void ParsingTests()
    {
        foreach (var (context, title, year, season, episode) in new (string, string, int?, int?, int?)[]
        {
            ("Movie.2024.1080p.WEB-DL.x265-GROUP.mkv", "Movie", 2024, null, null),
            ("The.1080p.Story.2024.2160p.mkv", "The 1080p Story", 2024, null, null),
            ("The.1080p.Story.mkv", "The 1080p Story", null, null, null),
            ("Movie.1080p.WEB-DL.x265.AAC2.0-GROUP.mkv", "Movie", null, null, null),
            ("2001.A.Space.Odyssey.1968.1080p.mkv", "2001 A Space Odyssey", 1968, null, null),
            ("1917.2019.1080p.BluRay.mkv", "1917", 2019, null, null),
            ("1984.mkv", "1984", null, null, null),
            ("Series.S01E02.1080p.mkv", "Series", null, 1, 2),
            ("Series.S00E01.mkv", "Series", null, 0, 1),
            ("C:\\Media\\Series (2020)\\Season01\\E02.mkv", "Series", 2020, 1, 2),
            ("C:\\Media\\Series\\Season 00\\E01.mkv", "Series", null, 0, 1),
            ("C:\\Media\\Series.2020\\S01E02.mkv", "Series", 2020, 1, 2),
            ("Animation - 01.mkv", "Animation - 01", null, null, null),
            ("Blade.Runner.2049.2017.1080p.mkv", "Blade Runner 2049", 2017, null, null)
        })
            Test($"本机文件名：{context}", () =>
            {
                var result = LocalMediaName.Parse(context);
                Assert.Equal(title, result.Title);
                Assert.Equal(year, result.Year);
                Assert.Equal(season, result.Season);
                Assert.Equal(episode, result.Episode);
                Assert.Equal(season.HasValue || episode.HasValue, result.IsSeries);
                Assert.Equal("", result.Problem);
            });

        foreach (var name in new[] { "Series.S01E01E02.mkv", "Series.S01E01-E03.mkv", "Series.S01-S03.mkv", "Series.S01-03.mkv",
            "Series.S01E01-S02E02.mkv", "Series.S01E01.1080p.E02.mkv", "C:\\Series\\Season 02\\S01E01.mkv" })
            Test($"本机文件名：范围或冲突须人工修正 {name}", () =>
            {
                var result = LocalMediaName.Parse(name);
                Assert.True(result.Problem.Length > 0);
            });
        Test("本机文件名：仅 E02 不猜第一季", () =>
        {
            var parsed = LocalMediaName.Parse("Series.E02.mkv");
            Assert.Null(parsed.Season);
            Assert.Equal<int?>(2, parsed.Episode);
            Assert.True(parsed.Problem.Length > 0);
        });
    }

    private static void MatchingTests()
    {
        Test("原生识别：只取唯一严格片名或原名并核对年份", () =>
        {
            var parsed = LocalMediaName.Parse("Original.Title.2024.1080p.mkv");
            var wrongYear = Candidate("10", "Original Title", "", 2023);
            var exact = Candidate("11", "译名", "Original Title", 2024);
            var partial = Candidate("12", "Original Title Part II", "", 2024);
            Assert.Equal(exact, LocalMediaName.UniqueExactMatch(parsed, [wrongYear, exact, partial]));
            Assert.Null(LocalMediaName.UniqueExactMatch(parsed, [exact, exact with { Id = "13" }]));
            Assert.Null(LocalMediaName.UniqueExactMatch(parsed, [partial]));
            Assert.Null(LocalMediaName.UniqueExactMatch(parsed with { Problem = "多集" }, [exact]));
            Assert.Null(LocalMediaName.UniqueExactMatch(parsed with { IsSeries = true }, [exact]));
        });
        Test("原生识别：保留片名标点区别，重复相同ID不造歧义", () =>
        {
            var parsed = LocalMediaName.Parse("A.B.2024.mkv");
            var exact = Candidate("11", "A B", "", 2024);
            Assert.Equal(exact, LocalMediaName.UniqueExactMatch(parsed, [exact, exact]));
            Assert.Null(LocalMediaName.UniqueExactMatch(parsed, [exact with { Title = "AB" }]));
        });
        Test("原生识别：双语片名整串搜不到时改用中文段命中 TMDB", () =>
        {
            var parsed = LocalMediaName.Parse("爱·回家之三代同糖.Like.Mother.Like.Daughter.S01E75.2026.1080p.MyTVSuper.WEB-DL.H265.AAC-ADWeb.mkv");
            Assert.Equal(1, parsed.Season);
            Assert.Equal(75, parsed.Episode);
            using var handler = new BilingualSearchHandler();
            using var client = new TmdbClient("anonymous-read-token", handler);
            var match = TmdbLocalMedia.FindUniqueAsync(client, parsed, CancellationToken.None).GetAwaiter().GetResult();
            Assert.NotNull(match);
            Assert.Equal("331026", match!.Id);
            Assert.True(handler.Queries.Count >= 2, string.Join(" | ", handler.Queries));
            Assert.Contains("爱·回家之三代同糖", handler.Queries[^1]);
        });
    }

    /// <summary>整串双语片名按 TMDB 的真实行为答空，只有中文段命中；用来验证关键词回退而不是假命中。</summary>
    private sealed class BilingualSearchHandler : HttpMessageHandler
    {
        public List<string> Queries { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            var text = query.Split("query=", StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault()?.Split('&')[0] ?? "";
            Queries.Add(text);
            var body = text == "爱·回家之三代同糖"
                ? """{"page":1,"results":[{"id":331026,"name":"爱·回家之三代同糖","original_name":"愛·回家之三代同糖","first_air_date":"2026-08-10","media_type":"tv","overview":""}],"total_pages":1,"total_results":1}"""
                : """{"page":1,"results":[],"total_pages":1,"total_results":0}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static void HttpTests()
    {
        Test("TMDB 搜索：固定GET路径、编码、中文语言与Bearer仅在请求头", () =>
        {
            using var stub = new StubTransport().Answer("/search/movie?", SearchJson());
            using var capture = new CaptureTransport(stub);
            using var client = new TmdbClient("anonymous-read-token", capture);
            var found = client.SearchAsync("Movie & ?", false, 2024, default).GetAwaiter().GetResult();
            var sent = stub.Only("/search/movie?");
            Assert.Equal("GET", sent.Method);
            Assert.Equal("https://api.themoviedb.org/3/search/movie?query=Movie%20%26%20%3F&language=zh-CN&include_adult=false&page=1&year=2024", sent.Url);
            Assert.Equal("", sent.Body);
            Assert.Equal("Bearer anonymous-read-token", capture.Headers.Single());
            Assert.DoesNotContain("anonymous-read-token", sent.Url);
            Assert.Equal(1, found.Count);
            Assert.Equal("Original Movie", found[0].OriginalTitle);
            Assert.Equal(2024, found[0].Year);
            Assert.False(found[0].IsSeries);
        });
        Test("TMDB 电视剧搜索：首播年份和name字段", () =>
        {
            using var stub = new StubTransport().Answer("/search/tv?", """{"results":[{"id":42,"name":"Series","original_name":"Original Series","first_air_date":"2020-01-02"}]}""");
            using var client = new TmdbClient("anonymous", stub);
            var found = client.SearchAsync("Series", true, 2020, default).GetAwaiter().GetResult().Single();
            Assert.Equal("https://api.themoviedb.org/3/search/tv?query=Series&language=zh-CN&include_adult=false&page=1&first_air_date_year=2020", stub.Only("/search/tv?").Url);
            Assert.Equal("Original Series", found.OriginalTitle);
            Assert.True(found.IsSeries);
        });
        Test("TMDB 电影详情：详情ID与电影字段必须一致", () =>
        {
            using var stub = new StubTransport().Answer("/movie/42?", MovieJson());
            using var client = new TmdbClient("anonymous", stub);
            var details = client.DetailsAsync("42", false, null, null, default).GetAwaiter().GetResult();
            Assert.Equal("https://api.themoviedb.org/3/movie/42?language=zh-CN&append_to_response=credits,aggregate_credits", stub.Only("/movie/42?").Url);
            Assert.Equal("42", details.Id);
            Assert.Equal("Movie", details.Title);
            Assert.Equal("Original Movie", details.OriginalTitle);
            Assert.Equal("2024-01-02", details.Premiered);
            Assert.Equal("https://image.tmdb.org/t/p/original/poster.jpg", details.PosterUrl);
            Assert.Equal("Movie overview", details.Overview);
        });
        Test("TMDB 详情：类型、评分、片长与演职人员随本机条目返回", () =>
        {
            const string body = """
                {"id":42,"title":"Movie","original_title":"Original Movie","release_date":"2024-01-02",
                 "overview":"Movie overview","poster_path":"/poster.jpg","backdrop_path":"/backdrop.jpg",
                 "tagline":"A tagline","vote_average":8.6,"runtime":128,
                 "genres":[{"id":18,"name":"剧情"},{"id":35,"name":"喜剧"}],
                 "production_companies":[{"id":1,"name":"示例影业"}],
                 "credits":{"cast":[{"id":101,"name":"演员甲","character":"角色甲","profile_path":"/person-a.jpg"},
                                    {"id":102,"name":"演员乙","character":"角色乙","profile_path":"/person-b.jpg"}],
                            "crew":[{"id":201,"name":"导演丙","job":"Director","profile_path":"/person-c.jpg"}]}}
                """;
            using var stub = new StubTransport().Answer("/movie/42?", body);
            using var client = new TmdbClient("anonymous", stub);
            var details = client.DetailsAsync("42", false, null, null, default).GetAwaiter().GetResult();

            Assert.Equal("A tagline", details.Tagline);
            Assert.Equal(8.6, details.Rating);
            Assert.Equal(128, details.Runtime);
            Assert.Equal("剧情,喜剧", string.Join(',', details.Genres));
            Assert.Equal("示例影业", string.Join(',', details.Studios));
            Assert.Equal("演员甲:角色甲|演员乙:角色乙|导演丙:Director",
                string.Join('|', details.People.Select(person => $"{person.Name}:{(person.Director ? "Director" : person.Role)}")));
            Assert.Equal("101,102,201", string.Join(',', details.People.Select(person => person.Id)));
            Assert.Equal("https://image.tmdb.org/t/p/original/person-a.jpg", details.People[0].ImageUrl);
        });
        Test("TMDB 特别篇：S00与单集信息保留，剧集与单集ID分开", () =>
        {
            using var stub = new StubTransport().Answer("/tv/42?", SeriesJson())
                .Answer("/tv/42/season/0/episode/1?", EpisodeJson(0, 1));
            using var client = new TmdbClient("anonymous", stub);
            var details = client.DetailsAsync("42", true, 0, 1, default).GetAwaiter().GetResult();
            Assert.Equal("https://api.themoviedb.org/3/tv/42/season/0/episode/1?language=zh-CN", stub.Only("/season/0/").Url);
            Assert.Equal("42", details.Id);
            Assert.Equal("84", details.EpisodeId);
            Assert.Equal<int?>(0, details.Season);
            Assert.Equal<int?>(1, details.Episode);
            Assert.Equal("Special", details.EpisodeTitle);
            Assert.Equal("Episode overview", details.EpisodeOverview);
            Assert.Equal("2020-01-02", details.Premiered);
            Assert.Equal("2020-05-06", details.EpisodeAired);
            Assert.Equal("https://image.tmdb.org/t/p/original/still.jpg", details.EpisodeImageUrl);
            Assert.Equal(43, details.Runtime);
        });
        foreach (var (series, body) in new[] { (false, MovieJson().Replace("\"id\":42", "\"id\":43", StringComparison.Ordinal)),
            (false, SeriesJson()), (true, MovieJson()) })
            Test($"TMDB 详情：拒绝ID或媒体类型冲突 {series} {body}", () =>
            {
                using var stub = new StubTransport().Answer(series ? "/tv/42?" : "/movie/42?", body);
                using var client = new TmdbClient("anonymous", stub);
                Assert.Throws<TmdbException>(() => client.DetailsAsync("42", series, null, null, default).GetAwaiter().GetResult());
                Assert.Equal(1, stub.Total);
            });
        Test("TMDB 单集：拒绝季集错位与show_id冲突", () =>
        {
            foreach (var body in new[] { EpisodeJson(1, 2), EpisodeJson(0, 1).Replace("\"show_id\":42", "\"show_id\":43", StringComparison.Ordinal) })
            {
                using var stub = new StubTransport().Answer("/tv/42?", SeriesJson()).Answer("/episode/1?", body);
                using var client = new TmdbClient("anonymous", stub);
                Assert.Throws<TmdbException>(() => client.DetailsAsync("42", true, 0, 1, default).GetAwaiter().GetResult());
            }
        });
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.Redirect })
            Test($"TMDB 失败：{status}不重试不切类型不泄漏响应内容", () =>
            {
                using var handler = new ResponseTransport(() => new HttpResponseMessage(status)
                {
                    Content = new StringContent("anonymous-read-token response secret"),
                    Headers = { Location = new Uri("https://example.invalid/redirect") }
                });
                using var client = new TmdbClient("anonymous-read-token", handler);
                var error = Assert.Catch<TmdbException>(() => client.DetailsAsync("42", false, null, null, default).GetAwaiter().GetResult());
                Assert.Equal(status, error.StatusCode);
                Assert.DoesNotContain("anonymous-read-token", error.ToString());
                Assert.DoesNotContain("response secret", error.ToString());
                Assert.Equal(1, handler.Count);
            });
        Test("TMDB 搜索：拒绝无效JSON与非数组候选", () =>
        {
            foreach (var body in new[] { "not JSON", "{}", "{\"results\":{}}", "{\"results\":[{\"id\":0,\"title\":\"Movie\"}]}" })
            {
                using var stub = new StubTransport().Answer("/search/movie?", body);
                using var client = new TmdbClient("anonymous", stub);
                Assert.Throws<TmdbException>(() => client.SearchAsync("Movie", false, null, default).GetAwaiter().GetResult());
            }
        });
        Test("TMDB 搜索：分页首屏不得宣称唯一匹配", () =>
        {
            using var stub = new StubTransport().Answer("/search/movie?", SearchJson().Replace("{\"results\":", "{\"total_pages\":2,\"results\":", StringComparison.Ordinal));
            using var client = new TmdbClient("anonymous", stub);
            var candidates = client.SearchAsync("Movie", false, 2024, default).GetAwaiter().GetResult();
            Assert.Equal(1, candidates.Count);
            Assert.False(candidates[0].AutoMatchEligible);
            Assert.Null(LocalMediaName.UniqueExactMatch(LocalMediaName.Parse("Movie.2024.mkv"), candidates));
        });
        Test("TMDB 搜索：相同ID不同内容不得当成同一身份", () =>
        {
            using var stub = new StubTransport().Answer("/search/movie?", """{"results":[{"id":42,"title":"Movie"},{"id":42,"title":"Other"}]}""");
            using var client = new TmdbClient("anonymous", stub);
            Assert.Throws<TmdbException>(() => client.SearchAsync("Movie", false, null, default).GetAwaiter().GetResult());
        });
        Test("TMDB 搜索：响应伪造类型和图片URL须拒绝", () =>
        {
            foreach (var body in new[] { SearchJson().Replace("\"id\":42", "\"id\":42,\"media_type\":\"tv\"", StringComparison.Ordinal),
                SearchJson().Replace("/poster.jpg", "https://example.invalid/poster.jpg", StringComparison.Ordinal) })
            {
                using var stub = new StubTransport().Answer("/search/movie?", body);
                using var client = new TmdbClient("anonymous", stub);
                Assert.Throws<TmdbException>(() => client.SearchAsync("Movie", false, null, default).GetAwaiter().GetResult());
            }
        });
        Test("TMDB 响应：断流、超大JSON与超时不泄漏传输异常", () =>
        {
            using var stub = new StubTransport().Understate("/search/movie?", SearchJson(), 2 * 1024 * 1024 + 1);
            using var client = new TmdbClient("anonymous", stub);
            Assert.Throws<TmdbException>(() => client.SearchAsync("Movie", false, null, default).GetAwaiter().GetResult());
            foreach (var error in new Exception[] { new HttpRequestException("anonymous-secret in transport"), new TaskCanceledException("anonymous-secret in timeout") })
            {
                using var failed = new StubTransport().Throw("/search/movie?", error);
                using var failedClient = new TmdbClient("anonymous", failed);
                var safe = Assert.Catch<TmdbException>(() => failedClient.SearchAsync("Movie", false, null, default).GetAwaiter().GetResult());
                Assert.DoesNotContain("anonymous-secret", safe.ToString());
            }
        });
        Test("TMDB 真实传输配置：注入HTTP handler也关闭redirect与cookie", () =>
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true, UseCookies = true };
            using var client = new TmdbClient("anonymous", handler);
            Assert.False(handler.AllowAutoRedirect);
            Assert.False(handler.UseCookies);
            using var sockets = new SocketsHttpHandler { AllowAutoRedirect = true, UseCookies = true };
            using var socketsClient = new TmdbClient("anonymous", sockets);
            Assert.False(sockets.AllowAutoRedirect);
            Assert.False(sockets.UseCookies);
        });
        Test("TMDB 参数：注入ID和无季集输入在联网前拒绝", () =>
        {
            using var stub = new StubTransport();
            using var client = new TmdbClient("anonymous", stub);
            foreach (var id in new[] { "../movie/1", "42?token=x", "0", "-1" })
                Assert.Throws<ArgumentException>(() => client.DetailsAsync(id, false, null, null, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.DetailsAsync("42", true, null, 1, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.DetailsAsync("42", false, 1, 1, default).GetAwaiter().GetResult());
            Assert.Equal(0, stub.Total);
            Assert.Throws<TmdbException>(() => new TmdbClient("\r\nsecret"));
        });
        Test("TMDB 取消：调用方取消保持取消异常", () =>
        {
            using var stub = new StubTransport().Answer("/search/movie?", SearchJson());
            using var client = new TmdbClient("anonymous", stub);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => client.SearchAsync("Movie", false, null, cancel.Token).GetAwaiter().GetResult());
            Assert.Equal(0, stub.Total);
        });
    }

    private static void ImageTests()
    {
        Test("TMDB 图片：固定图域请求不带Authorization", () =>
        {
            using var handler = new ResponseTransport(() => ImageResponse([0xff, 0xd8, 0xff, 0xd9], "image/jpeg"));
            using var client = new TmdbClient("anonymous-read-token", handler);
            var image = client.FetchImageAsync("https://image.tmdb.org/t/p/original/poster.jpg", default).GetAwaiter().GetResult();
            Assert.Equal(4, image.Length);
            Assert.Null(handler.LastAuthorization);
            Assert.Equal("GET", handler.LastMethod);
            Assert.Equal("https://image.tmdb.org/t/p/original/poster.jpg", handler.LastUrl);
            // TMDB 会按 Accept 协商格式；索取 webp 会让 .jpg 地址回 WebP 字节，与元数据目录的落盘后缀冲突。
            Assert.Equal("image/jpeg, image/png", handler.LastAccept);
        });
        foreach (var url in new[] { "http://image.tmdb.org/t/p/original/a.jpg", "https://example.invalid/a.jpg",
            "https://image.tmdb.org.example.invalid/a.jpg", "https://user@image.tmdb.org/a.jpg", "https://image.tmdb.org:444/a.jpg",
            "https://image.tmdb.org/a.jpg?token=secret", "https://image.tmdb.org/a.jpg#fragment" })
            Test($"TMDB 图片：无效图域联网前拒绝 {url}", () =>
            {
                using var handler = new ResponseTransport(() => ImageResponse([1], "image/jpeg"));
                using var client = new TmdbClient("anonymous", handler);
                Assert.Throws<ArgumentException>(() => client.FetchImageAsync(url, default).GetAwaiter().GetResult());
                Assert.Equal(0, handler.Count);
            });
        Test("TMDB 图片：拒绝HTML、空内容和超过大小上限", () =>
        {
            foreach (var (bytes, type, length) in new (byte[], string, long?)[] { ([1], "text/html", null),
                ([], "image/jpeg", null), ([1], "image/jpeg", 21 * 1024 * 1024),
                (new byte[21 * 1024 * 1024], "image/jpeg", null) })
            {
                using var handler = new ResponseTransport(() => ImageResponse(bytes, type, length));
                using var client = new TmdbClient("anonymous", handler);
                Assert.Throws<TmdbException>(() => client.FetchImageAsync("https://image.tmdb.org/t/p/original/a.jpg", default).GetAwaiter().GetResult());
            }
        });
    }

    private static void TimeoutTests()
    {
        foreach (var image in new[] { false, true })
        {
            Test($"TMDB 总超时：收到响应头后挂起的{(image ? "图片" : "JSON")}响应体也超时", () =>
            {
                using var stream = new HangingStream();
                using var handler = new ResponseTransport(() => HangingResponse(stream, image));
                using var client = new TmdbClient("anonymous", handler);
                SetTimeout(client, TimeSpan.FromMilliseconds(100));
                var error = Assert.Catch<TmdbException>(() => Request(client, image, default)
                    .WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult());
                Assert.Contains("超时", error.Message);
                Assert.True(stream.Disposed, "超时应释放响应和响应体");
                Assert.Null(error.InnerException);
            });
            Test($"TMDB 用户取消：读取{(image ? "图片" : "JSON")}响应体中取消仍是取消异常", () =>
            {
                using var stream = new HangingStream();
                using var handler = new ResponseTransport(() => HangingResponse(stream, image));
                using var client = new TmdbClient("anonymous", handler);
                using var cancel = new CancellationTokenSource();
                var task = Request(client, image, cancel.Token);
                stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                cancel.Cancel();
                Assert.Throws<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult());
                Assert.True(stream.Disposed, "用户取消也应释放响应体");
            });
        }
        Test("TMDB 无效令牌：空值、控制字符和非ASCII均是安全用户错误", () =>
        {
            foreach (var token in new[] { null, "", " ", "anonymous-secret\r\nInjected", "anonymous-secret\t", "anonymous-secret\u007f", "anonymous-secret中文" })
            {
                var error = Assert.Catch<TmdbException>(() => new TmdbClient(token!));
                Assert.DoesNotContain("anonymous-secret", error.ToString());
                Assert.Null(error.InnerException);
                Assert.Null(error.StatusCode);
            }
        });
    }

    private static Task Request(TmdbClient client, bool image, CancellationToken ct) => image
        ? client.FetchImageAsync("https://image.tmdb.org/t/p/original/a.jpg", ct)
        : client.SearchAsync("Movie", false, null, ct);

    private static void SetTimeout(TmdbClient client, TimeSpan timeout)
    {
        // 修改真实HttpClient配置以缩短时间；生产只保留统一的20秒预算，没有测试开关。
        var field = typeof(TmdbClient).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var http = (HttpClient)field!.GetValue(client)!;
        http.Timeout = timeout;
    }

    private static HttpResponseMessage HangingResponse(Stream stream, bool image)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(image ? "image/jpeg" : "application/json");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class HangingStream : Stream
    {
        private readonly CancellationTokenSource _stop = new();
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token).ConfigureAwait(false);
            return 0;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            {
                Disposed = true;
                _stop.Cancel();
                _stop.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static TmdbMediaCandidate Candidate(string id, string title, string original, int year) =>
        new() { Id = id, Title = title, OriginalTitle = original, Year = year };

    private static string SearchJson() => """{"results":[{"id":42,"title":"Movie","original_title":"Original Movie","release_date":"2024-01-02","overview":"Movie overview","poster_path":"/poster.jpg"}]}""";
    private static string MovieJson() => """{"id":42,"title":"Movie","original_title":"Original Movie","release_date":"2024-01-02","overview":"Movie overview","poster_path":"/poster.jpg","backdrop_path":"/backdrop.jpg"}""";
    private static string SeriesJson() => """{"id":42,"name":"Series","original_name":"Original Series","first_air_date":"2020-01-02","overview":"Series overview","poster_path":"/poster.jpg"}""";
    private static string EpisodeJson(int season, int episode) =>
        $$"""{"id":84,"show_id":42,"name":"Special","season_number":{{season}},"episode_number":{{episode}},"air_date":"2020-05-06","overview":"Episode overview","still_path":"/still.jpg","runtime":43}""";

    private static HttpResponseMessage ImageResponse(byte[] bytes, string type, long? length = null)
    {
        var content = new StreamContent(new MemoryStream(bytes));
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        if (length.HasValue) content.Headers.ContentLength = length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class CaptureTransport(StubTransport inner) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _invoker = new(inner, disposeHandler: false);
        public List<string?> Headers { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Headers.Add(request.Headers.Authorization?.ToString());
            return _invoker.SendAsync(request, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _invoker.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ResponseTransport(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastMethod { get; private set; }
        public string? LastUrl { get; private set; }
        public string? LastAccept { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastMethod = request.Method.Method;
            LastUrl = request.RequestUri?.AbsoluteUri;
            LastAccept = request.Headers.Accept.ToString();
            return Task.FromResult(answer());
        }
    }
}
