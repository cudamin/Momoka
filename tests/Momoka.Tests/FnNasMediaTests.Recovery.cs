using System.Net;
using Momoka.Emby;
using Momoka.FnNas;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FnNasMediaTests
{
    private static void RegisterRecovery()
    {
        Test("飞牛登录：登录及用户身份成功后，配置 503 只降级名称并保留探测的服务器身份", () =>
        {
            using var fixture = new Fixture();
            fixture.Server.ServerId = "server";
            fixture.Transport.Override = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/sys/config", StringComparison.Ordinal)
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) : null;
            fixture.Login();
            var connection = fixture.Session.Capture().CurrentConnection;
            Assert.True(connection.IsFnNas && connection.IsAdministrator == true);
            Assert.Equal("user", connection.UserId);
            Assert.Equal("server", connection.ServerId);
            Assert.Equal("fnnas.invalid", connection.ServerName);
        });
        Test("飞牛登录：配置确认的服务器 ID 与探测 ID 不同，不能静默采用新身份", () =>
        {
            using var fixture = new Fixture();
            fixture.Server.ServerId = "different-server";
            Assert.Throws<EmbyApiException>(fixture.Login);
            Assert.False(fixture.Session.IsSignedIn);
        });
        Test("飞牛恢复：配置读取失败仍不信任保存的令牌，不因名称降级跳过恢复探测", () =>
        {
            using var fixture = new Fixture();
            fixture.Account.UserId = "user";
            fixture.Account.ProtectedAccessToken = "synthetic-saved-token";
            fixture.Transport.Override = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/sys/config", StringComparison.Ordinal)
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
                : request.RequestUri.AbsolutePath.EndsWith("/identity", StringComparison.Ordinal)
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)) : null;
            var detection = Assert.Catch<EmbyApiException>(() => fixture.Session.Gateway.DetectAsync(fixture.Server.Url, default).GetAwaiter().GetResult());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, detection.StatusCode);
            Assert.False(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
            Assert.False(fixture.Session.IsSignedIn);
            Assert.Equal("synthetic-saved-token", fixture.Account.ProtectedAccessToken);
        });

        foreach (var endpoint in new[] { "/stream/list/movie", "/play/info" })
            Test($"飞牛详情：{endpoint} 503 保留元数据及另一项已成功的信息", () =>
            {
                using var fixture = new Fixture();
                fixture.Login();
                fixture.Transport.Override = (request, _) => request.RequestUri!.AbsolutePath.EndsWith(endpoint, StringComparison.Ordinal)
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) : null;
                var item = fixture.Session.ExecuteAsync((client, ct) => client.GetItemAsync("movie", ct), default).GetAwaiter().GetResult();
                Assert.Equal("电影", item.Name);
                Assert.Equal(90 * TimeSpan.TicksPerSecond, item.RunTimeTicks);
                Assert.Equal(endpoint == "/stream/list/movie" ? 0 : 1, item.MediaSources.Count);
                if (endpoint == "/stream/list/movie") Assert.Equal(12 * TimeSpan.TicksPerSecond, item.UserData!.PlaybackPositionTicks);
                else Assert.Equal(0L, item.UserData!.PlaybackPositionTicks);
            });

        foreach (var endpoint in new[] { "/sys/config", "/stream/list/movie", "/play/info", "/tag/genres", "/tag/iso3166" })
            foreach (var code in new[] { -2, -3, 5000, -5, 12345 })
                Test($"飞牛补充读取：{endpoint} 错误 {code} 不被降级吞掉", () =>
                {
                    using var fixture = new Fixture();
                    if (endpoint != "/sys/config") fixture.Login();
                    fixture.Transport.Override = (request, _) => request.RequestUri!.AbsolutePath.EndsWith(endpoint, StringComparison.Ordinal)
                        ? Task.FromResult(RecoveryReply("null", code)) : null;
                    if (endpoint == "/sys/config") Assert.Throws<EmbyApiException>(fixture.Login);
                    else
                    {
                        var client = fixture.Session.Client;
                        var operation = endpoint.StartsWith("/tag/", StringComparison.Ordinal)
                            ? (Task)client.GetFnNasFilterOptionsAsync("library", false, "zh", default)
                            : client.GetItemAsync("movie", default);
                        var error = Assert.Catch<FnNasApiException>(() => operation.GetAwaiter().GetResult());
                        Assert.Equal(code, error.Code);
                    }
                });

        Test("飞牛筛选：映射暂时不可用显示原始标签；网络失败可以降级但格式错误不能", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            fixture.Transport.Override = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/tag/genres", StringComparison.Ordinal)
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic network"))
                : request.RequestUri.AbsolutePath.EndsWith("/tag/iso3166", StringComparison.Ordinal)
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)) : null;
            var options = fixture.Session.Client.GetFnNasFilterOptionsAsync("library", false, "zh", default).GetAwaiter().GetResult();
            Assert.Equal("2", options.Genres[0].Label);
            Assert.Equal("CN", options.Locations[0].Label);
            fixture.Transport.Override = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/tag/genres", StringComparison.Ordinal)
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") }) : null;
            Assert.Throws<FnNasApiException>(() => fixture.Session.Client.GetFnNasFilterOptionsAsync("library", false, "zh", default).GetAwaiter().GetResult());
        });
        Test("飞牛补充读取：调用方取消不能被网络降级掩盖", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            using var cancelled = new CancellationTokenSource();
            fixture.Transport.Override = (request, _) =>
            {
                if (!request.RequestUri!.AbsolutePath.EndsWith("/stream/list/movie", StringComparison.Ordinal)) return null;
                cancelled.Cancel();
                return Task.FromException<HttpResponseMessage>(new OperationCanceledException(cancelled.Token));
            };
            Assert.Throws<OperationCanceledException>(() => fixture.Session.Client.GetItemAsync("movie", cancelled.Token).GetAwaiter().GetResult());
        });
        foreach (var value in new[] { "1", "\"1\"", "true", "0", "\"0\"", "false" })
            Test($"飞牛管理：管理员值 {value} 与媒体身份判断一致", () =>
            {
                using var fixture = new Fixture();
                fixture.Login();
                fixture.Transport.Override = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal)
                    ? Task.FromResult(RecoveryReply("{\"guid\":\"user\",\"username\":\"fixture\",\"is_admin\":" + value + "}"))
                    : request.RequestUri.AbsolutePath.EndsWith("/search/rebuild", StringComparison.Ordinal)
                        ? Task.FromResult(RecoveryReply("null")) : null;
                var client = fixture.Session.Client;
                if (value is "1" or "\"1\"" or "true") client.FnNas.RebuildSearchIndexAsync(client.FnNasConnection).GetAwaiter().GetResult();
                else
                {
                    Assert.Throws<FnNasApiException>(() => client.FnNas.RebuildSearchIndexAsync(client.FnNasConnection).GetAwaiter().GetResult());
                    Assert.False(fixture.Transport.Paths.Any(path => path.EndsWith("/search/rebuild", StringComparison.Ordinal)));
                }
            });
    }

    private static HttpResponseMessage RecoveryReply(string data, int code = 0) => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"code\":" + code + ",\"data\":" + data + "}") };
}
