using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>可控匿名传输验证真实 PrepareAsync；不使用计时推测并发，不登录，不创建媒体文件。</summary>
internal static class AniBtPrepareConcurrencyTests
{
    public static void Register()
    {
        Test("AniBT 四路匹配：峰值为四，乱序完成仍按文件及原历史配对", () =>
        {
            using var fixture = new Fixture();
            var pending = fixture.Prepare();
            var first = Enumerable.Range(0, 4).Select(_ => fixture.Matches.Next()).ToArray();
            Assert.Equal(4, fixture.Matches.Peak);
            Assert.Equal(4, fixture.Matches.Requests);
            // 每释放一条，等待下一条确实进入；始终有四条在途，不依赖 Sleep 或调度速度。
            foreach (var index in first.Reverse())
            {
                fixture.Matches.Complete(index);
                var next = fixture.Matches.Next();
                Assert.True(next >= 4);
            }
            foreach (var index in Enumerable.Range(4, 4).Reverse()) fixture.Matches.Complete(index);
            var plan = pending.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.Equal(4, fixture.Matches.Peak);
            Assert.Equal(8, fixture.Matches.Requests);
            Assert.Equal(0, fixture.Matches.Active);
            Assert.Equal(8, plan.Rows.Count);
            for (var index = 0; index < plan.Rows.Count; index++)
            {
                Assert.Equal(Path(index), plan.Rows[index].TargetPath);
                Assert.Equal($"Source {index}.mkv", plan.Rows[index].SourceName);
                Assert.Equal(100L + index, plan.Rows[index].Match.Candidates.Single().BgmId);
                Assert.Equal(1L + index, plan.Rows[index].History!.Id);
            }
        });

        Test("AniBT 四路匹配：单路失败取消其余在途请求，不启动剩余文件或返回部分计划", () =>
        {
            using var fixture = new Fixture();
            var pending = fixture.Prepare();
            var first = Enumerable.Range(0, 4).Select(_ => fixture.Matches.Next()).ToArray();
            fixture.Matches.Fail(first[0]);
            Assert.Throws<AniBtException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(4, fixture.Matches.Requests);
            Assert.Equal(3, fixture.Matches.Canceled);
            Assert.Equal(0, fixture.Matches.Active);
        });

        Test("AniBT 四路匹配：外部取消传到全部请求，不返回部分计划", () =>
        {
            using var fixture = new Fixture();
            var pending = fixture.Prepare();
            for (var index = 0; index < 4; index++) fixture.Matches.Next();
            fixture.Stop.Cancel();
            Assert.Throws<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(4, fixture.Matches.Requests);
            Assert.Equal(4, fixture.Matches.Canceled);
            Assert.Equal(0, fixture.Matches.Active);
        });

        Test("AniBT 四路匹配：连接 A 到 B 再回 A，迟到结果拒绝且其余请求取消", () =>
        {
            using var fixture = new Fixture();
            var pending = fixture.Prepare();
            var first = Enumerable.Range(0, 4).Select(_ => fixture.Matches.Next()).ToArray();
            fixture.Settings.MoviePilot.Url = "http://192.0.2.11:3001";
            fixture.Settings.MoviePilot.Url = "http://192.0.2.10:3001";
            fixture.InstallSession();
            fixture.Matches.Complete(first[0]);
            Assert.Throws<MoviePilotOperationBlockedException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(4, fixture.Matches.Requests);
            Assert.Equal(3, fixture.Matches.Canceled);
            Assert.Equal(0, fixture.Matches.Active);
        });
    }

    private static string Path(int index) => $"/library/Anonymous S01E{index + 1:00}.mkv";

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClientTransport _transport = new();
        private readonly MoviePilotClient _client;
        private readonly MoviePilotService _moviePilot;
        private readonly EmbyHttp _embyHttp;
        private readonly EmbyClient _emby;
        private readonly AniBtClient _aniBt;
        private readonly AniBtService _service;

        internal AppSettings Settings { get; } = new();
        internal MatchTransport Matches { get; } = new();
        internal CancellationTokenSource Stop { get; } = new();

        internal Fixture()
        {
            Settings.MoviePilot.Enabled = true;
            Settings.MoviePilot.Url = "http://192.0.2.10:3001";
            Settings.MoviePilot.Username = "anonymous-fixture";
            Settings.MoviePilot.ProtectedPassword = "anonymous-not-a-secret";
            _client = new MoviePilotClient(_transport);
            _moviePilot = new MoviePilotService(_client, new MoviePilotCredentials(new NoSecrets()), Settings);
            InstallSession();
            _embyHttp = new EmbyHttp(_transport);
            _emby = new EmbyClient(_embyHttp, new EmbyConnection(new Uri("http://192.0.2.10:8096/"),
                "anonymous-token", "user", "anonymous-fixture", "Fixture", DeviceIdentity.Create("fixture", "1.0")));
            _aniBt = new AniBtClient(Matches);
            _service = new AniBtService(_aniBt, _moviePilot);
        }

        internal Task<AniBtScrapePlan> Prepare() => _service.PrepareAsync(_emby,
            new EmbyItem { Id = "series-1", Type = EmbyItemType.Series }, Stop.Token);

        internal void InstallSession()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(_moviePilot, null)!;
            var type = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
            var cached = Activator.CreateInstance(type, flags | BindingFlags.Public, null,
                [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
            typeof(MoviePilotService).GetField("_session", flags)!.SetValue(_moviePilot, cached);
            typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(_moviePilot, identity);
        }

        public void Dispose()
        {
            Stop.Cancel();
            _aniBt.Dispose();
            _embyHttp.Dispose();
            _moviePilot.Dispose();
            _client.Dispose();
            Matches.Dispose();
            _transport.Dispose();
            Stop.Dispose();
        }
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string value) => throw new AssertionException("并发夹具禁止保存凭据");
        public string Unprotect(string value) => throw new AssertionException("并发夹具禁止解密或真实登录");
    }

    private sealed class HttpClientTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            object reply;
            if (path == "/Users/user/Items/series-1")
                reply = new { Id = "series-1", Name = "匿名动画", Type = "Series" };
            else if (path == "/Users/user/Items")
                reply = new
                {
                    Items = Enumerable.Range(0, 8).Select(index => new
                    { Id = $"episode-{index}", Name = "匿名动画", Type = "Episode", Path = Path(index) }),
                    TotalRecordCount = 8
                };
            else if (path == "/api/v1/history/transfer")
                reply = new
                {
                    list = Enumerable.Range(0, 8).Select(index => new
                    {
                        id = index + 1,
                        title = "匿名动画",
                        type = "电视剧",
                        media_source = "bangumi",
                        media_id = "42",
                        mode = "move",
                        status = true,
                        src_fileitem = new { storage = "local", type = "file", path = $"/downloads/Source {index}.mkv" },
                        dest_fileitem = new { storage = "local", type = "file", path = Path(index) }
                    }),
                    total = 8
                };
            else throw new AssertionException($"并发夹具拒绝未登记读取或写入：{path}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(reply), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class MatchTransport : HttpMessageHandler
    {
        private readonly Channel<int> _started = Channel.CreateUnbounded<int>();
        private readonly TaskCompletionSource<bool>[] _replies = [.. Enumerable.Range(0, 8)
            .Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))];
        private int _requests;
        private int _active;
        private int _peak;
        private int _canceled;

        internal int Requests => Volatile.Read(ref _requests);
        internal int Active => Volatile.Read(ref _active);
        internal int Peak => Volatile.Read(ref _peak);
        internal int Canceled => Volatile.Read(ref _canceled);
        internal int Next() => _started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        internal void Complete(int index) => _replies[index].SetResult(true);
        internal void Fail(int index) => _replies[index].SetResult(false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://anibt.net/api/animes/torrent-match", request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var name = body.RootElement.GetProperty("rawTitle").GetString()!;
            var index = int.Parse(name[7..^4], System.Globalization.CultureInfo.InvariantCulture);
            Interlocked.Increment(ref _requests);
            var active = Interlocked.Increment(ref _active);
            int previous;
            do { previous = Volatile.Read(ref _peak); }
            while (previous < active && Interlocked.CompareExchange(ref _peak, active, previous) != previous);
            Assert.True(_started.Writer.TryWrite(index));
            try
            {
                var success = await _replies[index].Task.WaitAsync(cancellationToken);
                var reply = new
                {
                    version = 3,
                    candidates = new[] { new { animeId = $"ani-{index}", bgmId = 100 + index,
                        anime = new { _id = $"ani-{index}", bgmId = 100 + index, title = new { chinese = $"匿名动画{index}" }, format = "TV" } } },
                    decision = new { kind = "manual", confidence = "low", reason = "fixture" }
                };
                return new HttpResponseMessage(success ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent(JsonSerializer.Serialize(reply), Encoding.UTF8, "application/json") };
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceled);
                throw;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
