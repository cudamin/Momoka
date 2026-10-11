using System.Net;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotAvailabilityTests
{
    private const string Session = """{"access_token":"fixture-token"}""";

    public static void Register()
    {
        Test("MoviePilot 可用性：不完整的旧配置不会使后台探测崩溃", () =>
        {
            using var transport = Transport();
            var settings = Settings();
            settings.MoviePilot.Username = null!;
            using var service = Service(transport, settings);
            Assert.False(Check(service));
            Assert.False(service.Available);
            Assert.Equal(0, transport.Total);
        });

        Test("MoviePilot 可用性：历史成功不算本次连接，关闭时不发请求", () =>
        {
            using var transport = Transport();
            var settings = Settings();
            settings.MoviePilot.LastConnected = DateTimeOffset.Now;
            using var service = Service(transport, settings);
            Assert.False(service.Available);
            settings.MoviePilot.Enabled = false;
            Assert.False(Check(service));
            Assert.Equal(0, transport.Total);
            service.TestConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(settings.MoviePilot.Enabled);
            Assert.False(service.Available);
            settings.MoviePilot.Enabled = true;
            Assert.False(service.Available);
            Assert.True(Check(service));
        });

        Test("MoviePilot 可用性：网关失联隐藏，恢复探测显示，业务拒绝不隐藏", () =>
        {
            using var transport = Transport();
            using var service = Service(transport, Settings());
            var changes = 0;
            service.AvailabilityChanged += (_, _) => changes++;
            Assert.True(Check(service));
            transport.Fail("dashboard/system", HttpStatusCode.BadGateway);
            Assert.False(Check(service));
            transport.Answer("dashboard/system", "{}");
            Assert.True(Check(service));
            transport.Fail("media/source", HttpStatusCode.NotFound);
            Assert.Throws<MoviePilotException>(() => service.MediaSourcesAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.True(service.Available);
            Assert.Equal(3, changes);
        });

        Test("MoviePilot 可用性：正常续令牌不闪退，最终认证失败隐藏", () =>
        {
            using var transport = Transport();
            using var service = Service(transport, Settings());
            Assert.True(Check(service));
            var changes = 0;
            service.AvailabilityChanged += (_, _) => changes++;
            transport.Sequence("dashboard/system", (HttpStatusCode.Unauthorized, ""), (HttpStatusCode.OK, "{}"));
            Assert.True(Check(service));
            Assert.Equal(0, changes);
            Assert.Equal(2, transport.Count("login/access-token"));
            transport.Sequence("dashboard/system", (HttpStatusCode.Unauthorized, ""), (HttpStatusCode.Unauthorized, ""));
            Assert.False(Check(service));
            Assert.Equal(1, changes);
            Assert.Equal(3, transport.Count("login/access-token"));
        });

        Test("MoviePilot 可用性：测试途中断线或认证失败不报成功，可选接口缺失仍兼容", () =>
        {
            foreach (var path in new[] { "mediaserver/clients", "download/clients" })
            {
                foreach (var failure in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadGateway })
                {
                    using var transport = Transport().Fail(path, failure);
                    using var service = Service(transport, Settings());
                    Assert.True(Check(service));
                    Assert.Throws<MoviePilotException>(() => service.TestConnectionAsync(CancellationToken.None).GetAwaiter().GetResult());
                    Assert.False(service.Available);
                }
                using var offline = Transport().Throw(path, new HttpRequestException("offline"));
                using var disconnected = Service(offline, Settings());
                Assert.Throws<MoviePilotUnreachableException>(() => disconnected.TestConnectionAsync(CancellationToken.None).GetAwaiter().GetResult());
                Assert.False(disconnected.Available);
                using var optional = Transport().Fail(path, HttpStatusCode.NotFound);
                using var compatible = Service(optional, Settings());
                compatible.TestConnectionAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(compatible.Available);
            }
        });

        Test("MoviePilot 可用性：断线前发出的迟到成功不重新显示，下一次探测可恢复", () =>
        {
            using var transport = Transport().Fail("media/source", HttpStatusCode.BadGateway);
            using var service = Service(transport, Settings());
            Assert.True(Check(service));
            transport.When("dashboard/system", () =>
                Assert.Throws<MoviePilotException>(() => service.MediaSourcesAsync(CancellationToken.None).GetAwaiter().GetResult()));
            Assert.False(Check(service));
            Assert.False(service.Available);
            transport.When("dashboard/system", () => { });
            Assert.True(Check(service));
        });

        Test("MoviePilot 可用性：关闭重开和配置往返均拒绝迟到响应", () =>
        {
            foreach (var change in new Action<MoviePilotSettings>[]
            {
                settings => { settings.Enabled = false; settings.Enabled = true; },
                settings => { var original = settings.Url; settings.Url = "other.invalid:3001"; settings.Url = original; },
                settings => { var original = settings.Username; settings.Username = "other"; settings.Username = original; },
                settings => { var original = settings.ProtectedPassword; settings.ProtectedPassword = "other"; settings.ProtectedPassword = original; }
            })
            {
                using var transport = Transport();
                var settings = Settings();
                using var service = Service(transport, settings);
                var stamp = service.ConnectionStamp;
                transport.When("dashboard/system", () => change(settings.MoviePilot));
                Assert.False(Check(service));
                Assert.False(service.Available);
                Assert.Equal(stamp, service.ConnectionStamp);
            }
        });

        Test("MoviePilot 可用性：配置变化立即收起，外部取消不抹掉有效连接", () =>
        {
            using var transport = Transport();
            var settings = Settings();
            using var service = Service(transport, settings);
            Assert.True(Check(service));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => service.RefreshAvailabilityAsync(canceled.Token).GetAwaiter().GetResult());
            Assert.True(service.Available);
            settings.MoviePilot.Enabled = false;
            Assert.False(service.Available);
            settings.MoviePilot.Enabled = true;
            Assert.False(service.Available);
            Assert.True(Check(service));
            Assert.Equal(2, transport.Count("login/access-token"));
        });

        Test("MoviePilot 可用性：写请求失联仍保留待核对，恢复后不能重发", () =>
        {
            using var transport = Transport().Answer("media/search", """[{"title":"测试","type":"电影","media_source":"themoviedb","media_id":"42"}]""")
                .Throw("subscribe/", new HttpRequestException("offline"));
            var settings = Settings();
            using var service = Service(transport, settings);
            var media = service.SearchAsync("测试", CancellationToken.None).GetAwaiter().GetResult().Single();
            Assert.True(service.Available);
            Assert.Throws<MoviePilotOperationUncertainException>(() => service.SubscribeAsync(media, CancellationToken.None).GetAwaiter().GetResult());
            Assert.False(service.Available);
            Assert.Equal(MoviePilotOperationState.Uncertain, service.SubscribeState(media));
            settings.MoviePilot.Enabled = false;
            settings.MoviePilot.Enabled = true;
            Assert.True(Check(service));
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.SubscribeAsync(media, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Count("subscribe/"));
        });
    }

    private static bool Check(MoviePilotService service) => service.RefreshAvailabilityAsync().GetAwaiter().GetResult();
    private static StubTransport Transport() => new StubTransport().Answer("login/access-token", Session)
        .Answer("dashboard/system", "{}").Answer("mediaserver/clients", "[]").Answer("download/clients", "[]");
    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "https://fixture.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(settings.MoviePilot, "fixture");
        return settings;
    }
    private static MoviePilotService Service(StubTransport transport, AppSettings settings) =>
        new(new MoviePilotClient(transport), new MoviePilotCredentials(PassthroughSecretProtector.Instance), settings);
}
