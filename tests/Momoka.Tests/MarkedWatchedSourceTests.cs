using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual manual-report service with in-memory source identities; no login, playback or secret IO.</summary>
internal static class MarkedWatchedSourceTests
{
    private static readonly MediaServerKind[] Kinds = [MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex];
    private const long Duration = 600_000_000;

    internal static int Run()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Register();
        return TestHarness.Run();
    }

    internal static void Register()
    {
        foreach (var selected in Kinds)
            foreach (var target in Kinds)
                foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode, EmbyItemType.Season, EmbyItemType.Series })
                {
                    Test($"R-047 默认{selected}／目标{target}／{type}只按条目来源补报", () =>
                    {
                        using var f = new Fixture(selected);
                        var item = f.Item(target, type);
                        var reported = f.Service.ReportMarkedWatchedAsync(item).GetAwaiter().GetResult();
                        Assert.Equal(target == MediaServerKind.Emby, reported);
                        Assert.Equal(target == MediaServerKind.Emby ? 2 : 0, f.Transport.Reports.Count);
                        Assert.Equal(target == MediaServerKind.Emby && type is EmbyItemType.Season or EmbyItemType.Series ? 1 : 0, f.Transport.EpisodeReads);
                        if (target == MediaServerKind.Emby) CheckReports(f.Transport, target, type is EmbyItemType.Season or EmbyItemType.Series ? "episode-2" : item.Id);
                    });
                }
        foreach (var selected in Kinds)
        {
            Test($"R-047 无来源旧条目仍按捕获时默认{selected}执行", () =>
            {
                using var f = new Fixture(selected);
                var item = new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, RunTimeTicks = Duration };
                Assert.Equal(selected == MediaServerKind.Emby, f.Service.ReportMarkedWatchedAsync(item).GetAwaiter().GetResult());
                Assert.Equal(selected == MediaServerKind.Emby ? 2 : 0, f.Transport.Reports.Count);
            });
        }
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Season, EmbyItemType.Series })
        {
            Test($"R-047 条目来源先撤销{type}不借另一在线默认来源", () =>
            {
                using var f = new Fixture(MediaServerKind.Jellyfin);
                var item = f.Item(MediaServerKind.Emby, type);
                f.Revoke(MediaServerKind.Emby);
                Assert.False(f.Service.ReportMarkedWatchedAsync(item).GetAwaiter().GetResult());
                Assert.Equal(0, f.Transport.Total);
            });
        }
        Test("R-047 列表await时切默认，旧条目和后代始终报原Emby", () =>
        {
            using var f = new Fixture(MediaServerKind.Emby);
            var item = f.Item(MediaServerKind.Emby, EmbyItemType.Season);
            item.Source = null;
            f.Transport.OnEpisodeRead = () => f.Default(MediaServerKind.Plex);
            Assert.True(f.Service.ReportMarkedWatchedAsync(item).GetAwaiter().GetResult());
            CheckReports(f.Transport, MediaServerKind.Emby, "episode-2");
        });
        Test("R-047 开始补报后换默认，停止仍发原条目来源", () =>
        {
            using var f = new Fixture(MediaServerKind.Jellyfin);
            f.Transport.OnStart = () => f.Default(MediaServerKind.Plex);
            Assert.True(f.Service.ReportMarkedWatchedAsync(f.Item(MediaServerKind.Emby, EmbyItemType.Movie)).GetAwaiter().GetResult());
            CheckReports(f.Transport, MediaServerKind.Emby, "movie");
        });
        Test("R-047 查询后代时来源撤销不发开始或停止", () =>
        {
            using var f = new Fixture(MediaServerKind.Plex);
            f.Transport.OnEpisodeRead = () => f.Revoke(MediaServerKind.Emby);
            Assert.False(f.Service.ReportMarkedWatchedAsync(f.Item(MediaServerKind.Emby, EmbyItemType.Season)).GetAwaiter().GetResult());
            Assert.Equal(0, f.Transport.Reports.Count);
            Assert.Equal(1, f.Transport.EpisodeReads);
        });
        Test("R-047 开始请求前来源撤销不补停止或改用其它来源", () =>
        {
            using var f = new Fixture(MediaServerKind.FnNas);
            f.Transport.OnStart = () => f.Revoke(MediaServerKind.Emby);
            Assert.False(f.Service.ReportMarkedWatchedAsync(f.Item(MediaServerKind.Emby, EmbyItemType.Movie)).GetAwaiter().GetResult());
            Assert.Equal(1, f.Transport.Reports.Count);
            Assert.Equal("/emby/Sessions/Playing", f.Transport.Reports.Single().Path);
        });
        foreach (var flag in new[] { "off-stop", "off-progress", "no-duration", "no-series", "canceled" })
        {
            Test("R-047 保留零报告前提 " + flag, () =>
            {
                using var f = new Fixture(MediaServerKind.Plex);
                var item = f.Item(MediaServerKind.Emby, flag == "no-series" ? EmbyItemType.Season : EmbyItemType.Movie);
                if (flag == "off-stop") f.Settings.Playback.StopReportEnabled = false;
                if (flag == "off-progress") f.Settings.Playback.ReportProgressToServer = false;
                if (flag == "no-duration") item.RunTimeTicks = null;
                if (flag == "no-series") item.SeriesId = null;
                using var cancel = new CancellationTokenSource();
                if (flag == "canceled") cancel.Cancel();
                Assert.False(f.Service.ReportMarkedWatchedAsync(item, cancel.Token).GetAwaiter().GetResult());
                Assert.Equal(0, f.Transport.Total);
            });
        }
        Test("R-047 开始失败不发送停止", () =>
        {
            using var f = new Fixture(MediaServerKind.Plex);
            f.Transport.RejectStart = true;
            Assert.False(f.Service.ReportMarkedWatchedAsync(f.Item(MediaServerKind.Emby, EmbyItemType.Movie)).GetAwaiter().GetResult());
            Assert.Equal(1, f.Transport.Reports.Count);
        });
        Test("R-047 没有在线来源不补报且不启动后端", () =>
        {
            using var f = new Fixture(MediaServerKind.Plex);
            f.DisposeSession();
            Assert.False(f.Service.ReportMarkedWatchedAsync(new EmbyItem { Id = "old", Type = EmbyItemType.Movie, RunTimeTicks = Duration }).GetAwaiter().GetResult());
            Assert.Equal(0, f.Transport.Total);
        });
    }

    private static void CheckReports(ReportTransport transport, MediaServerKind source, string itemId)
    {
        var reports = transport.Reports.ToArray();
        Assert.Equal(2, reports.Length);
        Assert.Equal(source.ToString().ToLowerInvariant() + ".invalid", reports[0].Host);
        Assert.Equal(reports[0].Host, reports[1].Host);
        Assert.Equal("/emby/Sessions/Playing", reports[0].Path);
        Assert.Equal("/emby/Sessions/Playing/Stopped", reports[1].Path);
        using var start = JsonDocument.Parse(reports[0].Body);
        using var stop = JsonDocument.Parse(reports[1].Body);
        Assert.Equal(itemId, start.RootElement.GetProperty("ItemId").GetString());
        Assert.Equal(itemId, stop.RootElement.GetProperty("ItemId").GetString());
        Assert.Equal(0L, start.RootElement.GetProperty("PositionTicks").GetInt64());
        Assert.Equal(Duration, stop.RootElement.GetProperty("PositionTicks").GetInt64());
        Assert.Equal(start.RootElement.GetProperty("PlaySessionId").GetString(), stop.RootElement.GetProperty("PlaySessionId").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Dictionary<MediaServerKind, EmbySessionScope> _scopes = [];
        private readonly Dictionary<MediaServerKind, EmbySessionScope.Identity> _identities = [];
        private readonly EmbySession _session;
        public AppSettings Settings { get; } = new();
        public ReportTransport Transport { get; } = new();
        public PlaybackService Service { get; }
        public Fixture(MediaServerKind selected)
        {
            Settings.Playback.StopReportEnabled = true;
            Settings.Playback.ReportProgressToServer = true;
            var secrets = new NoSecrets();
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "r047-no-config|no-secrets"));
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = new EmbySession(Settings, new SettingsStore(paths, secrets), new CredentialVault(secrets), device, Transport);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Fields)!.GetValue(_session)!;
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Fields)!.GetValue(_session)!;
            foreach (var kind in Kinds)
            {
                var name = kind.ToString().ToLowerInvariant();
                var api = new Uri("https://" + name + ".invalid/" + (kind == MediaServerKind.Emby ? "emby/" : ""));
                var connection = new EmbyConnection(api, "placeholder-only", name + "-user", name + "-user", name, device) { ServerKind = kind };
                var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = name, ServerKind = kind }, new AccountProfile { Id = name + "-account" });
                var scope = new EmbySessionScope(_session, identity);
                _identities.Add(kind, identity); _scopes.Add(kind, scope); sources.Add(scope.Key, scope);
            }
            Default(selected);
            Service = new PlaybackService(_session, Settings, () => throw new AssertionException("手动已看夹具禁止启动后端"),
                new PlaybackPlanner(Settings, new ShaderGroupResolver(Settings.Shaders)));
        }
        public EmbyItem Item(MediaServerKind kind, string type) => _scopes[kind].Bind(new EmbyItem
        {
            Id = type == EmbyItemType.Movie ? "movie" : type == EmbyItemType.Episode ? "episode" : type == EmbyItemType.Season ? "season" : "series",
            Type = type,
            Name = "anonymous fixture",
            SeriesId = type == EmbyItemType.Season ? "series" : null,
            RunTimeTicks = Duration
        });
        public void Default(MediaServerKind kind) => typeof(EmbySession).GetField("_current", Fields)!.SetValue(_session, _scopes[kind]);
        public void Revoke(MediaServerKind kind) => _identities[kind].Lifetime.Cancel();
        public void DisposeSession() => _session.Dispose();
        public void Dispose()
        {
            _session.Dispose();
            foreach (var identity in _identities.Values) { identity.Reauthentication.Dispose(); identity.PasswordChanges.Dispose(); }
            Transport.Dispose();
        }
        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string text) => throw new AssertionException("不能保护凭据");
            public string Unprotect(string text) => throw new AssertionException("不能解开凭据或登录");
        }
    }

    private sealed class ReportTransport : HttpMessageHandler
    {
        internal sealed record Sent(string Host, string Path, string Body);
        public ConcurrentQueue<Sent> Reports { get; } = new();
        public int EpisodeReads { get; private set; }
        public int Total => EpisodeReads + Reports.Count;
        public Action? OnEpisodeRead { get; set; }
        public Action? OnStart { get; set; }
        public bool RejectStart { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = request.RequestUri!;
            if (request.Method == HttpMethod.Get && url.AbsolutePath == "/emby/Shows/series/Episodes")
            {
                EpisodeReads++;
                OnEpisodeRead?.Invoke();
                var body = JsonSerializer.Serialize(new
                {
                    Items = new[]
                {
                    new { Id = "episode-1", Type = "Episode", IndexNumber = 1, ParentIndexNumber = 1, RunTimeTicks = Duration, PremiereDate = "2000-01-01T00:00:00Z" },
                    new { Id = "episode-2", Type = "Episode", IndexNumber = 2, ParentIndexNumber = 1, RunTimeTicks = Duration, PremiereDate = "2000-01-01T00:00:00Z" },
                    new { Id = "unaired", Type = "Episode", IndexNumber = 3, ParentIndexNumber = 2, RunTimeTicks = Duration, PremiereDate = "2100-01-01T00:00:00Z" }
                }
                });
                return Reply(body);
            }
            if (request.Method == HttpMethod.Post && url.Host == "emby.invalid" && url.AbsolutePath is "/emby/Sessions/Playing" or "/emby/Sessions/Playing/Stopped")
            {
                Reports.Enqueue(new(url.Host, url.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
                if (url.AbsolutePath == "/emby/Sessions/Playing") OnStart?.Invoke();
                return Reply("", RejectStart && url.AbsolutePath == "/emby/Sessions/Playing" ? HttpStatusCode.InternalServerError : HttpStatusCode.NoContent);
            }
            throw new AssertionException("已看补报匿名夹具拒绝未登记接口／产品／登录：" + url.AbsolutePath);
        }
        private static HttpResponseMessage Reply(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
