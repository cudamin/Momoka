using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.FnNas;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly PropertyInfo Optional = typeof(FnNasApiException).GetProperty("CanSkipOptionalRead", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo Business = typeof(FnNasApiException).GetMethod("Business", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        foreach (var code in new[] { -16, -5 })
            Test("R004 文件或账户权限 " + code + " 不是凭据拒绝且不得补读降级", () =>
            {
                var error = (FnNasApiException)Business.Invoke(null, [code])!;
                Assert.False(error.IsAuthenticationFailure);
                Assert.False(error.IsTokenExpired);
                Assert.False(CanSkip(error));
                Assert.Equal(typeof(EmbyApiException), FnNasMediaAdapter.Translate(error).GetType());
                Assert.Contains("权限", error.Message);
            });
        foreach (var code in new[] { -16, -5 })
            foreach (var transport in new[] { false, true })
                Test("R004 明确权限 " + code + " 不被传输／服务故障标记降级 " + transport, () =>
                {
                    var error = new FnNasApiException("anonymous", code, HttpStatusCode.ServiceUnavailable, transport);
                    Assert.False(error.IsAuthenticationFailure);
                    Assert.False(CanSkip(error));
                });
        foreach (var code in new[] { -2, -15 })
            Test("R004 真正密码或token拒绝 " + code + " 保留身份语义", () =>
            {
                var error = (FnNasApiException)Business.Invoke(null, [code])!;
                Assert.True(error.IsAuthenticationFailure);
                Assert.False(CanSkip(error));
                Assert.Equal(code == -2, error.IsTokenExpired);
            });
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
            Test("R004 HTTP " + (int)status + " 保原身份/权限分类", () =>
            {
                var error = new FnNasApiException("anonymous", statusCode: status);
                Assert.Equal(status == HttpStatusCode.Unauthorized, error.IsAuthenticationFailure);
                Assert.False(CanSkip(error));
            });
        foreach (var code in new[] { -3, 5000, -19 })
            Test("R004 签名或未知业务 " + code + " 不补读降级", () =>
            {
                var error = (FnNasApiException)Business.Invoke(null, [code])!;
                Assert.False(error.IsAuthenticationFailure);
                Assert.False(CanSkip(error));
            });
        foreach (var code in new[] { -4, -6, -8, -13, -60 })
            Test("R004 明确可选故障 " + code + " 保可降级边界", () =>
                Assert.True(CanSkip((FnNasApiException)Business.Invoke(null, [code])!)));
        foreach (var endpoint in new[] { "api/v1/user/info", "api/v1/sys/config" })
            Case("R004 实际非登录只读 " + endpoint + " 的-16保权限且无凭据重试", async () =>
            {
                using var transport = new PermissionHandler(endpoint);
                using var http = new FnNasHttp(transport, disposeHandler: false);
                var client = new FnNasClient(http);
                var connection = new FnNasConnection(new Uri("https://r004.invalid/v/"), "");
                FnNasApiException? error = null;
                try
                {
                    if (endpoint.EndsWith("user/info", StringComparison.Ordinal)) await client.GetUserAsync(connection);
                    else await client.GetConfigurationAsync(connection.ApiBase);
                }
                catch (FnNasApiException observed) { error = observed; }
                Assert.NotNull(error);
                Assert.Equal(-16, error!.Code);
                Assert.False(error.IsAuthenticationFailure);
                Assert.False(CanSkip(error));
                Assert.Equal(1, transport.Reads);
                Assert.Equal(0, transport.Writes);
                Assert.Equal(0, transport.Forbidden);
            });
        Test("R004 传输暂不可用与503保可选读取，不误当凭据", () =>
        {
            foreach (var error in new[] { new FnNasApiException("offline", isTransportFailure: true),
                new FnNasApiException("offline", statusCode: HttpStatusCode.ServiceUnavailable) })
            {
                Assert.True(CanSkip(error));
                Assert.False(error.IsAuthenticationFailure);
            }
        });
        return Run();
    }
    private static void Case(string name, Func<Task> work) => Test(name, () =>
    {
        using var queue = new UiQueue();
        queue.Run(work);
    });
    private static bool CanSkip(FnNasApiException error) => (bool)Optional.GetValue(error)!;
    private sealed class PermissionHandler(string endpoint) : HttpMessageHandler
    {
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }
        internal int Forbidden { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method != HttpMethod.Get) Writes++;
            if (request.Method != HttpMethod.Get || request.RequestUri is not { Host: "r004.invalid" } uri || uri.AbsolutePath != "/v/" + endpoint)
            { Forbidden++; throw new InvalidOperationException("R004 only allows anonymous readonly permission endpoints; no login/credential transport"); }
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"code":-16,"msg":"not logged","data":null}""") });
        }
    }
}
