using System.Net;
using System.Text;
using Momoka.FnNas;
using Momoka.Playback;
using Momoka.Tests;
using static Momoka.Tests.TestHarness;

namespace Momoka.FnNasSubtitleTests;

internal static class SubtitleTransportTests
{
    internal static void Register()
    {
        Test("真实播放transport：字幕DL逐URI即时authx，媒体与原生不带签名", () => Run(async () =>
        {
            var api = new Uri("https://signed.invalid/v/");
            var authorization = new FnNasSubtitleAuthorization(api);
            var source = FnNasClient.SubtitleUrl(api, "subtitle-a");
            var request = new PlaybackRequest
            {
                MediaUrl = new("https://signed.invalid/v/api/v1/media/range/video"),
                Title = "anonymous",
                HttpHeaders = [new("Authorization", "placeholder")],
                FnNasSubtitleAuthorization = authorization,
                ExternalSubtitles = [source]
            };
            using var endpoint = new Endpoint();
            await using var transport = new PlaybackHttpTransport(request, endpoint);
            var native = transport.Prepare(request);
            Assert.Null(native.FnNasSubtitleAuthorization);
            Assert.Equal(0, native.HttpHeaders.Count);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            await client.GetByteArrayAsync(native.MediaUrl);
            Assert.False(endpoint.Sent[0].Headers.ContainsKey("authx"));
            await client.GetByteArrayAsync(native.ExternalSubtitles[0]);
            var first = endpoint.Sent[1];
            Verify(first);
            await client.GetByteArrayAsync(transport.MapCommand(["sub-add", source.AbsoluteUri, "select"])[1]);
            Verify(endpoint.Sent[2]);
            Assert.False(first.Headers["authx"] == endpoint.Sent[2].Headers["authx"]);
            using var head = new HttpRequestMessage(HttpMethod.Head, native.ExternalSubtitles[0]);
            using var headReply = await client.SendAsync(head);
            Assert.Equal(HttpMethod.Head, endpoint.Sent[3].Method);
            Verify(endpoint.Sent[3]);
            foreach (var uri in new[] { new Uri("https://signed.invalid/v/api/v1/subtitle/dl/subtitle-a?api_key=bad"),
                         new Uri("https://signed.invalid/v/api/v1/subtitle/dl/../../other"), new Uri("https://signed.invalid/foreign/subtitle/dl/subtitle-a"),
                         new Uri("https://foreign.invalid/v/api/v1/subtitle/dl/subtitle-a") })
            {
                using var candidate = new HttpRequestMessage(HttpMethod.Get, uri);
                authorization.Apply(candidate);
                Assert.False(candidate.Headers.Contains("authx"));
            }
        }));
        Test("真实播放transport：字幕同源跳转重签，离源及回跳永不恢复Auth或签名", () => Run(async () =>
        {
            var api = new Uri("https://signed.invalid/v/");
            using var endpoint = new Endpoint();
            endpoint.Reply = index => index switch
            {
                0 => Redirect("/v/api/v1/subtitle/dl/subtitle-b"),
                1 => Redirect("https://foreign.invalid/v/api/v1/subtitle/dl/subtitle-c"),
                2 => Redirect("https://signed.invalid/v/api/v1/subtitle/dl/subtitle-d"),
                _ => new(HttpStatusCode.OK) { Content = new StringContent("subtitle") }
            };
            var request = new PlaybackRequest
            {
                MediaUrl = new("https://signed.invalid/v/api/v1/media/range/video"),
                Title = "anonymous",
                HttpHeaders = [new("Authorization", "placeholder")],
                FnNasSubtitleAuthorization = new(api)
            };
            await using var transport = new PlaybackHttpTransport(request, endpoint);
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            await client.GetByteArrayAsync(transport.Map(FnNasClient.SubtitleUrl(api, "subtitle-a")));
            Assert.Equal(4, endpoint.Sent.Count);
            Verify(endpoint.Sent[0]);
            Verify(endpoint.Sent[1]);
            Assert.False(endpoint.Sent[0].Headers["authx"] == endpoint.Sent[1].Headers["authx"]);
            foreach (var sent in endpoint.Sent.Skip(2))
            {
                Assert.False(sent.Headers.ContainsKey("authx"));
                Assert.False(sent.Headers.ContainsKey("Authorization"));
            }
        }));
        Test("真实字幕本地导出：每跳受限签名，跨源不携Auth或authx", () => Run(async () =>
        {
            var api = new Uri("https://signed.invalid/v/");
            using var endpoint = new Endpoint();
            endpoint.Reply = index => index switch
            {
                0 => Redirect("/v/api/v1/subtitle/dl/subtitle-b"),
                1 => Redirect("https://foreign.invalid/subtitle.srt"),
                _ => new(HttpStatusCode.OK) { Content = new StringContent("subtitle") }
            };
            using var client = new HttpClient(endpoint);
            var output = Path.Combine(Path.GetTempPath(), "momoka-fnnas-subtitle-" + Guid.NewGuid().ToString("N") + ".srt");
            try
            {
                await PlayerToolDownload.CopyAsync(client, FnNasClient.SubtitleUrl(api, "subtitle-a"),
                    [new("Authorization", "placeholder")], output, null, CancellationToken.None, new(api));
                Verify(endpoint.Sent[0]);
                Verify(endpoint.Sent[1]);
                Assert.False(endpoint.Sent[2].Headers.ContainsKey("authx"));
                Assert.False(endpoint.Sent[2].Headers.ContainsKey("Authorization"));
                Assert.Equal("subtitle", await File.ReadAllTextAsync(output));
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }));
    }

    private static void Verify(Sent sent)
    {
        Assert.Equal("placeholder", sent.Headers["Authorization"]);
        var fields = sent.Headers["authx"].Split('&').Select(value => value.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);
        Assert.Equal(FnNasSigner.Create(sent.Method, sent.Uri, null, fields["nonce"], long.Parse(fields["timestamp"], System.Globalization.CultureInfo.InvariantCulture)), sent.Headers["authx"]);
        Assert.True(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - long.Parse(fields["timestamp"], System.Globalization.CultureInfo.InvariantCulture)) < 10_000);
    }
    private static HttpResponseMessage Redirect(string destination) => new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new(destination, UriKind.RelativeOrAbsolute) } };
    private static void Run(Func<Task> scenario) { using var queue = new UiQueue(); queue.Run(scenario); }
    private sealed record Sent(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers);
    private sealed class Endpoint : HttpMessageHandler
    {
        internal List<Sent> Sent { get; } = [];
        internal Func<int, HttpResponseMessage> Reply { get; set; } = _ => new(HttpStatusCode.OK) { Content = new StringContent("subtitle", Encoding.UTF8) };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Sent.Add(new(request.Method, request.RequestUri!, request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(',', pair.Value), StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult(Reply(Sent.Count - 1));
        }
    }
}
