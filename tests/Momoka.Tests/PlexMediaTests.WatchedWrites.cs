using System.Net;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexMediaTests
{
    private static void RegisterWatchedWrites()
    {
        foreach (var played in new[] { true, false })
        {
            Test($"R05-002 Plex 确认{played}不夹带失败补读", () =>
            {
                using var f = new Fixture();
                f.Login();
                f.Transport.MetadataFailure = HttpStatusCode.ServiceUnavailable;
                var scope = f.Session.Capture();
                var data = scope.ExecuteAsync((client, token) => played
                    ? client.MarkPlayedAsync("10", token) : client.MarkUnplayedAsync("10", token), default).GetAwaiter().GetResult();
                Assert.Null(data, "Plex 写成功没有完整用户状态，必须独立补读");
                Assert.Equal(1, f.Transport.Requests.Count(uri => uri.AbsolutePath == (played ? "/:/scrobble" : "/:/unscrobble")));
                Assert.Equal(0, f.Transport.Requests.Count(uri => uri.AbsolutePath == "/library/metadata/10"));
            });
            Test($"R05-002 Plex 确认{played}后401不重发写入", () =>
            {
                using var f = new Fixture();
                f.Login();
                f.Transport.ExpireMetadata = true;
                var scope = f.Session.Capture();
                var data = scope.ExecuteAsync((client, token) => played
                    ? client.MarkPlayedAsync("10", token) : client.MarkUnplayedAsync("10", token), default).GetAwaiter().GetResult();
                Assert.Null(data);
                Assert.Equal(1, f.Transport.Logins, "独立补读前不得因为metadata失效重跑写委托");
                Assert.NotNull(scope.ExecuteAsync((client, token) => client.GetPlexUserDataAsync("10", token), default).GetAwaiter().GetResult());
                Assert.Equal(2, f.Transport.Logins);
                Assert.Equal(2, f.Transport.Requests.Count(uri => uri.AbsolutePath == "/library/metadata/10"));
                Assert.Equal(1, f.Transport.Requests.Count(uri => uri.AbsolutePath == (played ? "/:/scrobble" : "/:/unscrobble")));
            });
            foreach (var status in new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.Forbidden })
                Test($"R05-002 Plex 确认{played}后独立补读{(int)status}失败仍只写一次", () =>
                {
                    using var f = new Fixture();
                    f.Login();
                    var scope = f.Session.Capture();
                    Assert.Null(scope.ExecuteAsync((client, token) => played
                        ? client.MarkPlayedAsync("10", token) : client.MarkUnplayedAsync("10", token), default).GetAwaiter().GetResult());
                    f.Transport.MetadataFailure = status;
                    Assert.Throws<EmbyApiException>(() => scope.ExecuteAsync((client, token) =>
                        client.GetPlexUserDataAsync("10", token), default).GetAwaiter().GetResult());
                    Assert.Equal(1, f.Transport.Requests.Count(uri => uri.AbsolutePath == (played ? "/:/scrobble" : "/:/unscrobble")));
                    Assert.Equal(1, f.Transport.Requests.Count(uri => uri.AbsolutePath == "/library/metadata/10"));
                });
            Test($"R05-002 Plex {played}写失败不确认、不补读", () =>
            {
                using var f = new Fixture();
                f.Login();
                f.Transport.WriteFailure = HttpStatusCode.InternalServerError;
                var scope = f.Session.Capture();
                Assert.Throws<EmbyApiException>(() => scope.ExecuteAsync((client, token) => played
                    ? client.MarkPlayedAsync("10", token) : client.MarkUnplayedAsync("10", token), default).GetAwaiter().GetResult());
                Assert.Equal(0, f.Transport.Requests.Count(uri => uri.AbsolutePath == "/library/metadata/10"));
            });
            Test($"R05-002 Plex {played}写本身401仍保留同身份认证重试", () =>
            {
                using var f = new Fixture();
                f.Login();
                f.Transport.ExpireWrite = true;
                var scope = f.Session.Capture();
                Assert.Null(scope.ExecuteAsync((client, token) => played
                    ? client.MarkPlayedAsync("10", token) : client.MarkUnplayedAsync("10", token), default).GetAwaiter().GetResult());
                Assert.Equal(2, f.Transport.Logins);
                Assert.Equal(2, f.Transport.Requests.Count(uri => uri.AbsolutePath == (played ? "/:/scrobble" : "/:/unscrobble")));
                Assert.Equal(0, f.Transport.Requests.Count(uri => uri.AbsolutePath == "/library/metadata/10"));
            });
        }
        Test("R05-002 Plex 确认后坏metadata仍失败且不重发写", () =>
        {
            using var f = new Fixture();
            f.Login();
            var scope = f.Session.Capture();
            Assert.Null(scope.ExecuteAsync((client, token) => client.MarkPlayedAsync("10", token), default).GetAwaiter().GetResult());
            f.Transport.BadMetadata = true;
            Assert.Throws<EmbyApiException>(() => scope.ExecuteAsync((client, token) => client.GetPlexUserDataAsync("10", token), default).GetAwaiter().GetResult());
            Assert.Equal(1, f.Transport.Requests.Count(uri => uri.AbsolutePath == "/:/scrobble"));
        });
        Test("R05-002 Plex 预取消写与独立补读零额外请求", () =>
        {
            using var f = new Fixture();
            f.Login();
            var scope = f.Session.Capture();
            var before = f.Transport.Requests.Count;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => scope.ExecuteAsync((client, token) =>
                client.MarkPlayedAsync("10", token), cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(before, f.Transport.Requests.Count);
            Assert.Null(scope.ExecuteAsync((client, token) => client.MarkPlayedAsync("10", token), default).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => scope.ExecuteAsync((client, token) =>
                client.GetPlexUserDataAsync("10", token), cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(before + 1, f.Transport.Requests.Count);
        });
    }
}
