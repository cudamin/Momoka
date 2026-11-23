using System.Net;
using System.Net.Sockets;
using System.Text;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Metadata;

namespace Momoka.Tests;

/// <summary>
/// 设置 → 关于 → 网络的地址解析、设置读写，以及 TMDB 和 AniBT 请求的代理出口。
/// </summary>
internal static class ProxySettingsTests
{
    public static void Register()
    {
        TestHarness.Test("代理：认得 http(s)、socks5、socks5h 与不写 scheme 的 host:port", () =>
        {
            foreach (var (input, canonical, browser) in new[]
            {
                ("http://172.17.0.1:8888", "http://172.17.0.1:8888", "--proxy-server=http://172.17.0.1:8888"),
                (" HTTP://Proxy.Example:8080/ ", "http://proxy.example:8080", "--proxy-server=http://proxy.example:8080"),
                ("https://proxy.example:443", "https://proxy.example:443", "--proxy-server=https://proxy.example:443"),
                ("socks5://127.0.0.1:1080", "socks5://127.0.0.1:1080", "--proxy-server=socks5://127.0.0.1:1080"),
                // socks5h 是 curl 的写法（域名也交给代理解析）；Chromium 只认 socks5，换算在 BrowserArgument 里。
                ("socks5h://127.0.0.1:1080", "socks5h://127.0.0.1:1080", "--proxy-server=socks5://127.0.0.1:1080"),
                ("127.0.0.1:8686", "http://127.0.0.1:8686", "--proxy-server=http://127.0.0.1:8686"),
                ("http://[::1]:8686", "http://[::1]:8686", "--proxy-server=http://[::1]:8686")
            })
            {
                Assert.True(ProxyEndpoint.TryParse(input, out _), input);
                Assert.Equal(canonical, ProxyEndpoint.Normalize(input));
                Assert.Equal(browser, ProxyEndpoint.BrowserArgument(input));
            }

            var socks = (WebProxy)ProxyEndpoint.CreateWebProxy("socks5h://127.0.0.1:1080")!;
            Assert.Equal("socks5", socks.Address!.Scheme);
            Assert.Equal("127.0.0.1", socks.Address.Host);
            Assert.Equal(1080, socks.Address.Port);

            var http = (WebProxy)ProxyEndpoint.CreateWebProxy("172.17.0.1:8888")!;
            Assert.Equal("http", http.Address!.Scheme);
            Assert.Equal(8888, http.Address.Port);

            var ipv6 = (WebProxy)ProxyEndpoint.CreateWebProxy("http://[::1]:8686")!;
            Assert.Equal("[::1]", ipv6.Address!.Host);
        });

        TestHarness.Test("代理：空、带凭据、带路径、少端口的 socks 与控制字符都不认", () =>
        {
            foreach (var input in new[]
            {
                "", "   ", "http://user:pass@host:8080", "http://user@host:8080", "http://host:8080/path",
                "ftp://host:21", "socks5://host", "http://host:8080?x=1", "http://host:8080#f",
                "http://host:0", "http://host:70000", "http://host:8080\r\n--evil"
            })
            {
                Assert.False(ProxyEndpoint.TryParse(input, out _), input);
                Assert.Null(ProxyEndpoint.BrowserArgument(input));
                Assert.Null(ProxyEndpoint.CreateWebProxy(input));
            }

            Assert.Equal("", ProxyEndpoint.Normalize(null));
            Assert.Equal("", ProxyEndpoint.Normalize("   "));
            // 认不出的值原样保留，用户可以继续编辑。
            Assert.Equal("还没写完", ProxyEndpoint.Normalize("  还没写完  "));
        });

        TestHarness.Test("代理：缺键为空、手改值读盘归一、null 段回装机默认", () =>
        {
            var missing = SettingsMigration.FromJson(
                "{\"SchemaVersion\":" + AppSettings.CurrentSchemaVersion + "}", PassthroughSecretProtector.Instance);
            Assert.Equal("", missing.Network.ProxyServer);

            var typed = SettingsMigration.FromJson(
                "{\"SchemaVersion\":" + AppSettings.CurrentSchemaVersion
                    + ",\"Network\":{\"ProxyServer\":\" 172.17.0.1:8888 \"}}", PassthroughSecretProtector.Instance);
            Assert.Equal("http://172.17.0.1:8888", typed.Network.ProxyServer);

            var damaged = SettingsMigration.FromJson(
                "{\"SchemaVersion\":" + AppSettings.CurrentSchemaVersion + ",\"Network\":null}",
                PassthroughSecretProtector.Instance);
            Assert.Equal("", damaged.Network.ProxyServer);

            var junk = SettingsMigration.FromJson(
                "{\"SchemaVersion\":" + AppSettings.CurrentSchemaVersion
                    + ",\"Network\":{\"ProxyServer\":\" 随便写的 \"}}", PassthroughSecretProtector.Instance);
            Assert.Equal("随便写的", junk.Network.ProxyServer);
        });

        TestHarness.Test("代理：备份带上、恢复配置盖回、恢复默认清空", () =>
        {
            var live = new AppSettings();
            live.Network.ProxyServer = "socks5://127.0.0.1:1080";

            var backup = SettingsPreferences.ToBackupDocument(live);
            Assert.Equal("socks5://127.0.0.1:1080", backup.Network.ProxyServer);

            live.Network.ProxyServer = "http://other:8080";
            SettingsPreferences.Apply(live, backup);
            Assert.Equal("socks5://127.0.0.1:1080", live.Network.ProxyServer);

            SettingsPreferences.Apply(live, new AppSettings());
            Assert.Equal("", live.Network.ProxyServer);
        });

        // 填了代理，请求就该先到代理那里。回环上搭一个只收一行的假代理：HTTPS 目标会让 HttpClient 发
        // CONNECT api.themoviedb.org:443，看到它就算这根线接上了；不回话，请求随后按超时收尾，不外连目标。
        TestHarness.Test("代理：TMDB 请求交给代理而不是直连目标", () =>
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string? head = null;
            var accepted = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[1024];
                var read = await stream.ReadAsync(buffer);
                head = Encoding.ASCII.GetString(buffer, 0, read);
            });

            using var tmdb = new TmdbClient("anonymous",
                proxy: ProxyEndpoint.CreateWebProxy($"http://127.0.0.1:{port}"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { tmdb.SearchAsync("Movie", false, null, timeout.Token).GetAwaiter().GetResult(); }
            catch (Exception) { /* 假代理不回话，这一趟必然失败；要看的是它先收到了连接。 */ }

            Assert.True(accepted.Wait(TimeSpan.FromSeconds(5)), "代理端口没有收到任何连接");
            Assert.True(head?.StartsWith("CONNECT api.themoviedb.org:443", StringComparison.Ordinal) == true,
                head ?? "代理没有收到 CONNECT");
        });

        TestHarness.Test("代理：AniBT 目录搜索使用当前代理，切换地址保留正在进行的请求", async () =>
        {
            using var firstProxy = new TcpListener(IPAddress.Loopback, 0);
            using var secondProxy = new TcpListener(IPAddress.Loopback, 0);
            firstProxy.Start();
            secondProxy.Start();
            var firstPort = ((IPEndPoint)firstProxy.LocalEndpoint).Port;
            var secondPort = ((IPEndPoint)secondProxy.LocalEndpoint).Port;
            var settings = new AppSettings();
            settings.Network.ProxyServer = $"127.0.0.1:{firstPort}";
            using var client = new AniBtClient(readProxyServer: () => settings.Network.ProxyServer);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var firstSearch = ExpectProxyRejectionAsync(client, timeout.Token);
            using var firstTunnel = await firstProxy.AcceptTcpClientAsync(timeout.Token);
            await AssertAniBtConnectAsync(firstTunnel, timeout.Token);

            settings.Network.ProxyServer = $"http://127.0.0.1:{secondPort}";
            var secondSearch = ExpectProxyRejectionAsync(client, timeout.Token);
            using var secondTunnel = await secondProxy.AcceptTcpClientAsync(timeout.Token);
            await AssertAniBtConnectAsync(secondTunnel, timeout.Token);
            await RejectTunnelAsync(secondTunnel, timeout.Token);
            await secondSearch;

            Assert.False(firstSearch.IsCompleted, "切换代理提前中断了旧请求");
            await RejectTunnelAsync(firstTunnel, timeout.Token);
            await firstSearch;
        });

        TestHarness.Test("代理：AniBT 清空地址后恢复系统代理", async () =>
        {
            using var systemProxy = new TcpListener(IPAddress.Loopback, 0);
            using var explicitProxy = new TcpListener(IPAddress.Loopback, 0);
            systemProxy.Start();
            explicitProxy.Start();
            var systemPort = ((IPEndPoint)systemProxy.LocalEndpoint).Port;
            var explicitPort = ((IPEndPoint)explicitProxy.LocalEndpoint).Port;
            var previousProxy = HttpClient.DefaultProxy;
            try
            {
                HttpClient.DefaultProxy = new WebProxy($"http://127.0.0.1:{systemPort}");
                var settings = new AppSettings();
                settings.Network.ProxyServer = $"http://127.0.0.1:{explicitPort}";
                using var client = new AniBtClient(readProxyServer: () => settings.Network.ProxyServer);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                var explicitSearch = ExpectProxyRejectionAsync(client, timeout.Token);
                using var explicitTunnel = await explicitProxy.AcceptTcpClientAsync(timeout.Token);
                await AssertAniBtConnectAsync(explicitTunnel, timeout.Token);
                await RejectTunnelAsync(explicitTunnel, timeout.Token);
                await explicitSearch;

                SettingsPreferences.Apply(settings, new AppSettings());
                var systemSearch = ExpectProxyRejectionAsync(client, timeout.Token);
                using var systemTunnel = await systemProxy.AcceptTcpClientAsync(timeout.Token);
                await AssertAniBtConnectAsync(systemTunnel, timeout.Token);
                await RejectTunnelAsync(systemTunnel, timeout.Token);
                await systemSearch;
            }
            finally
            {
                HttpClient.DefaultProxy = previousProxy;
            }
        });
    }

    private static async Task ExpectProxyRejectionAsync(AniBtClient client, CancellationToken cancellationToken)
    {
        try { await client.SearchCatalogAsync("Anime", null, cancellationToken); }
        catch (AniBtException error) when (error.InnerException is HttpRequestException) { return; }
        throw new InvalidOperationException("AniBT 没有报告代理拒绝连接");
    }

    private static async Task AssertAniBtConnectAsync(TcpClient tunnel, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(tunnel.GetStream(), Encoding.ASCII, leaveOpen: true);
        var request = await reader.ReadLineAsync(cancellationToken);
        Assert.Equal("CONNECT anibt.net:443 HTTP/1.1", request);
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 }) { }
    }

    private static ValueTask RejectTunnelAsync(TcpClient tunnel, CancellationToken cancellationToken) =>
        tunnel.GetStream().WriteAsync("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(),
            cancellationToken);
}
