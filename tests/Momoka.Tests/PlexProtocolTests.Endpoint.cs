using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexProtocolTests
{
    private static void RegisterEndpoints()
    {
        foreach (var example in EndpointCases())
            Test("Plex R-006 地址表：" + example.Input, () =>
            {
                Assert.True(PlexEndpoint.TryNormalize(example.Input, out var address, out var error), error);
                Assert.Equal(example.Port, address.Port);
                Assert.Equal(example.AbsoluteUri, address.AbsoluteUri);
                Assert.Equal(example.AbsoluteUri, EmbyServerAddress.Normalize(example.Input, MediaServerKind.Plex).AbsoluteUri);
                Assert.Equal("", address.UserInfo);
                Assert.Equal("", address.Query);
                Assert.Equal("", address.Fragment);

                var display = PlexEndpoint.ToDisplayString(address);
                Assert.Equal(example.Display, display);
                var restored = PlexEndpoint.Normalize(display);
                Assert.Equal(example.Port, restored.Port);
                Assert.Equal(example.AbsoluteUri, restored.AbsoluteUri);
                Assert.Equal(display, PlexEndpoint.ToDisplayString(restored));
            });

        foreach (var example in EndpointCases())
            Test("Plex R-006 共享显示往返：" + example.Input, () =>
            {
                var address = EmbyServerAddress.Normalize(example.Input, MediaServerKind.Plex);
                var display = EmbyServerAddress.ToDisplayString(address, MediaServerKind.Plex);
                Assert.Equal(example.Display, display, "共用保存入口也必须保留显式 HTTP 80");
                var restored = EmbyServerAddress.Normalize(display, MediaServerKind.Plex);
                Assert.Equal(example.Port, restored.Port);
                Assert.Equal(example.AbsoluteUri, restored.AbsoluteUri);
                Assert.Equal(display, EmbyServerAddress.ToDisplayString(restored, MediaServerKind.Plex));
            });

        foreach (var host in new[] { "plex-address.invalid", "192.0.2.10", "[2001:db8::1]" })
            foreach (var scheme in new[] { "http", "https" })
                Test($"Plex R-006 地址拒绝：{scheme} userinfo {host}", () =>
                {
                    var input = $"{scheme}://fixture-user:placeholder@{host}:80/plex/web";
                    Assert.False(PlexEndpoint.TryNormalize(input, out var address, out var error));
                    Assert.Null(address);
                    Assert.True(error.Length > 0);
                    Assert.Throws<ArgumentException>(() => PlexEndpoint.Normalize(input));
                });
    }

    private static IEnumerable<EndpointCase> EndpointCases()
    {
        foreach (var host in new[] { "plex-address.invalid", "192.0.2.10", "[2001:db8::1]" })
        {
            yield return new(host, $"http://{host}:32400/", 32400, $"http://{host}:32400");
            yield return new($"http://{host}", $"http://{host}:32400/", 32400, $"http://{host}:32400");
            yield return new($"http://{host}:80", $"http://{host}/", 80, $"http://{host}:80");
            yield return new($"http://{host}:32400", $"http://{host}:32400/", 32400, $"http://{host}:32400");
            yield return new($"http://{host}:32401", $"http://{host}:32401/", 32401, $"http://{host}:32401");
            yield return new($"https://{host}", $"https://{host}/", 443, $"https://{host}");
            yield return new($"https://{host}:443", $"https://{host}/", 443, $"https://{host}");
            yield return new($"https://{host}:8443", $"https://{host}:8443/", 8443, $"https://{host}:8443");
        }

        yield return new("plex-address.invalid:80/plex/web/index.html?view=home#page",
            "http://plex-address.invalid/plex/", 80, "http://plex-address.invalid:80/plex");
        yield return new("192.0.2.10:80/proxy/plex/web", "http://192.0.2.10/proxy/plex/", 80,
            "http://192.0.2.10:80/proxy/plex");
        yield return new("[::1]", "http://[::1]:32400/", 32400, "http://[::1]:32400");
        yield return new("http://[::1]:80/plex/web/index.html", "http://[::1]/plex/", 80,
            "http://[::1]:80/plex");
        yield return new("http://[2001:db8::1]/proxy/plex/web/index.html", "http://[2001:db8::1]:32400/proxy/plex/", 32400,
            "http://[2001:db8::1]:32400/proxy/plex");
        yield return new("https://plex-address.invalid:443/plex/web/index.html?view=home#page",
            "https://plex-address.invalid/plex/", 443, "https://plex-address.invalid/plex");
        yield return new("https://[2001:db8::1]:8443/proxy/plex/web", "https://[2001:db8::1]:8443/proxy/plex/", 8443,
            "https://[2001:db8::1]:8443/proxy/plex");
        yield return new("http://plex-address.invalid/proxy:80/web/index.html", "http://plex-address.invalid:32400/proxy:80/", 32400,
            "http://plex-address.invalid:32400/proxy:80");
        yield return new("http://plex-address.invalid?example=:80#web", "http://plex-address.invalid:32400/", 32400,
            "http://plex-address.invalid:32400");
        yield return new("http://plex-address.invalid/plex/web/library", "http://plex-address.invalid:32400/plex/web/library/", 32400,
            "http://plex-address.invalid:32400/plex/web/library");
        yield return new("https://plex-address.invalid/my%20plex/web/index.html", "https://plex-address.invalid/my%20plex/", 443,
            "https://plex-address.invalid/my%20plex");
        yield return new("  HTTP://PLEX-ADDRESS.INVALID:80/Proxy/WEB/INDEX.HTML  ", "http://plex-address.invalid/Proxy/", 80,
            "http://plex-address.invalid:80/Proxy");
    }

    private sealed record EndpointCase(string Input, string AbsoluteUri, int Port, string Display);
}
