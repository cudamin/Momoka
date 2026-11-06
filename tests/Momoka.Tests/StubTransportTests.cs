using System.Net;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class StubTransportTests
{
    public static void Register()
    {
        Test("假HTTP：未知路径也计入实际请求，不伪造零请求", () =>
        {
            using var handler = new StubTransport();
            using var http = new HttpClient(handler);
            Assert.Equal(0, handler.Total);
            using var response = http.GetAsync("http://fixture.invalid/not-configured").GetAwaiter().GetResult();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(1, handler.Total);
            Assert.Equal(1, handler.SentTo("").Count);
        });
        Test("假HTTP：匹配响应和传输异常都计数，片段计数仍独立", () =>
        {
            using var handler = new StubTransport().Answer("/known", "{}").Throw("/broken", new HttpRequestException("fixture"));
            using var http = new HttpClient(handler);
            using var response = http.GetAsync("http://fixture.invalid/known").GetAwaiter().GetResult();
            Assert.Throws<HttpRequestException>(() => http.GetAsync("http://fixture.invalid/broken").GetAwaiter().GetResult());
            using var missing = http.GetAsync("http://fixture.invalid/missing").GetAwaiter().GetResult();
            Assert.Equal(3, handler.Total);
            Assert.Equal(1, handler.Count("/known"));
            Assert.Equal(1, handler.Count("/broken"));
            Assert.Equal(3, handler.SentTo("").Count);
        });
    }
}
