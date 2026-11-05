using System.Net;
using System.Text;
using Momoka.Emby;

internal static class PlexDiscovery
{
    public static async Task RunAsync()
    {
        foreach (var address in new[] { "plex-review.invalid", "http://plex-review.invalid", "http://plex-review.invalid:32400", "https://plex-review.invalid", "http://plex-review.invalid:80" })
        {
            using var handler = new Probe(address.StartsWith("https", StringComparison.Ordinal) ? 443 : address.EndsWith(":80", StringComparison.Ordinal) ? 80 : 32400);
            using var http = new EmbyHttp(handler);
            var info = await new EmbyServerGateway(http, DeviceIdentity.Create("anonymous", "1")).DetectAsync(address, default);
            if (info.ServerKind != MediaServerKind.Plex || info.ApiBase?.Port != handler.Port)
                throw new InvalidOperationException("Plex discovery did not respect the address port");
        }
        using (var handler = new Probe(32400) { RefuseAll = true })
        using (var http = new EmbyHttp(handler))
        {
            try
            {
                await new EmbyServerGateway(http, DeviceIdentity.Create("anonymous", "1")).DetectAsync("plex-review.invalid", default);
                throw new InvalidOperationException("Unreachable endpoints must fail");
            }
            catch (EmbyUnreachableException) { }
        }
        using (var handler = new Probe(32400))
        using (var http = new EmbyHttp(handler))
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try
            {
                await new EmbyServerGateway(http, DeviceIdentity.Create("anonymous", "1")).DetectAsync("plex-review.invalid", cancellation.Token);
                throw new InvalidOperationException("Canceled discovery must stop");
            }
            catch (OperationCanceledException) { }
            if (handler.Requests != 0) throw new InvalidOperationException("Canceled discovery sent requests");
        }
        Console.WriteLine("R29: 7 anonymous gateway discovery cases passed.");
    }

    private sealed class Probe(int port) : HttpMessageHandler
    {
        public int Port => port;
        public int Requests { get; private set; }
        public bool RefuseAll { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            if (uri.Host != "plex-review.invalid" || request.Headers.Contains("X-Plex-Token") || request.Headers.Contains("X-Emby-Token"))
                throw new InvalidOperationException("Unexpected identity in anonymous probe");
            Requests++;
            if (RefuseAll || uri.Port != port) throw new HttpRequestException(HttpRequestError.ConnectionError, "fixture refuses endpoint");
            var identity = uri.AbsolutePath == "/identity";
            return Task.FromResult(new HttpResponseMessage(identity ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(identity ? "{\"MediaContainer\":{\"machineIdentifier\":\"anonymous-machine\",\"version\":\"1.0\"}}" : "{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
