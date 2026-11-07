using System.Net;
using System.Net.Sockets;
using System.Text;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DeviceHeaderTransmissionTests
{
    public static void Register()
    {
        foreach (var (number, input, expected) in new[]
        {
            (1, "我的电脑", "unknown"), (2, "PC-我的电脑", "PC-"),
            (3, "\";\\,\r\n\t\u007f\u0085😀", "unknown"), (4, "é-PC", "-PC"),
            (5, "PC one", "PC one")
        })
        {
            Test($"R04-002：设备头清理用例{number}保留ASCII名称或安全回退", () =>
            {
                Assert.Equal(expected, DeviceIdentity.Sanitize(input));
                var device = new DeviceIdentity(input, input, "stable-device-1", input);
                Assert.True(device.ToAuthorizationHeader().All(c => c is >= ' ' and <= '~'));
                Assert.Contains("DeviceId=\"stable-device-1\"", device.ToAuthorizationHeader());
            });
        }
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            foreach (var name in new[] { "ASCII-PC", "我的电脑", "PC-我的电脑" })
            {
                Test($"R04-002：{kind}设备名{name}能发送真实回环HTTP头", () =>
                    VerifyTransmissionAsync(kind, name).GetAwaiter().GetResult());
            }
        }
    }

    private static async Task VerifyTransmissionAsync(MediaServerKind kind, string name)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var url = new Uri($"http://127.0.0.1:{endpoint.Port}/fixture");
        var device = new DeviceIdentity(DeviceIdentity.ClientName, name, "stable-device-1", "0");
        using var http = new EmbyHttp(new SocketsHttpHandler { UseProxy = false });
        var receive = ReceiveAsync();
        try
        {
            var value = await http.GetJsonAsync<Reply>(url, new(device, "fixture-token", ServerKind: kind), deadline.Token);
            Assert.True(value.Ok);
            var wire = await receive;
            var expected = kind == MediaServerKind.Jellyfin ? "Authorization" : "X-Emby-Authorization";
            var expectedValue = MediaServerHeaders.Create(device, "fixture-token", kind)
                .Single(header => header.Key == expected).Value;
            Assert.Contains($"{expected}: {expectedValue}\r\n", wire);
            Assert.True(wire.All(c => c <= 127), "检查的是真实传输字节，没有UTF8头部编码例外");
            Assert.Contains("DeviceId=\"stable-device-1\"", wire);
            Assert.False(wire.Split("\r\n")[0].Contains("fixture-token", StringComparison.Ordinal));
        }
        finally
        {
            await deadline.CancelAsync();
            listener.Stop();
            try { await receive; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }

        async Task<string> ReceiveAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = client.GetStream();
            using var request = new MemoryStream();
            var buffer = new byte[1];
            while (request.Length < 8192)
            {
                if (await stream.ReadAsync(buffer, deadline.Token) == 0)
                    throw new IOException("HTTP头部尚未完成便关闭连接");
                request.WriteByte(buffer[0]);
                var bytes = request.GetBuffer();
                var length = (int)request.Length;
                if (length >= 4 && bytes[length - 4] == 13 && bytes[length - 3] == 10
                    && bytes[length - 2] == 13 && bytes[length - 1] == 10)
                {
                    var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 11\r\nConnection: close\r\n\r\n{\"Ok\":true}");
                    await stream.WriteAsync(response, deadline.Token);
                    return Encoding.Latin1.GetString(bytes, 0, length);
                }
            }
            throw new IOException("HTTP头部超过夹具上限");
        }
    }

    private sealed record Reply(bool Ok);
}
