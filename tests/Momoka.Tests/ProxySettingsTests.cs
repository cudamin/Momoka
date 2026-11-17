using System.Net;
using System.Net.Sockets;
using System.Text;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Metadata;

namespace Momoka.Tests;

/// <summary>
/// 设置 → 关于 → 网络 的代理服务器：地址解析、设置读写与它真正被交给 TMDB 请求的那根线。
/// <para>
/// 纯函数那几条钉住写法（http/socks5/socks5h、不写 scheme、认不出的值）；最后一条用回环上的假代理收一次
/// CONNECT，证明「填了代理」不是只写进设置文件，而是真的换了 <see cref="HttpClient"/> 的出口。
/// </para>
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
                ("127.0.0.1:8686", "http://127.0.0.1:8686", "--proxy-server=http://127.0.0.1:8686")
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
            // 认不出的值原样留着（用户可能正打到一半），只是不生效 —— 两个消费者都先过 TryParse。
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
    }
}
