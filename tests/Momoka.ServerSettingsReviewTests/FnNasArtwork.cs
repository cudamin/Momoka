using System.Net;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;

internal static class FnNasArtwork
{
    public static async Task RunAsync()
    {
        foreach (var failure in new[] { "expired", "401", "500" })
        {
            var settings = new AppSettings();
            var server = new ServerProfile { ServerKind = MediaServerKind.FnNas, Url = "https://artwork.invalid/v", ServerId = "server" };
            var account = new AccountProfile { Username = "anonymous" };
            server.Accounts.Add(account); settings.Servers.Add(server);
            using var handler = new Transport(failure);
            var store = new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "isolated-settings", Guid.NewGuid().ToString("N"))), PassthroughSecretProtector.Instance);
            using var session = new EmbySession(settings, store, new CredentialVault(PassthroughSecretProtector.Instance), DeviceIdentity.Create("anonymous", "1"), handler);
            await session.SignInAsync(server, account, "anonymous-password", "anonymous", true, default);
            var scope = session.Capture();
            var result = await scope.ExecuteAsync(async (client, token) =>
            {
                var items = await client.GetItemsAsync(new ItemQuery { ParentId = "library", Limit = 20 }, token);
                return await client.WithCarouselArtworkAsync(items.Items, 1, token);
            }, default);
            if (failure == "500")
            {
                if (handler.Logins != 1 || result.Single().BackdropImageTags.Count != 0)
                    throw new InvalidOperationException("Transient artwork failure did not retain graceful fallback");
            }
            else if (handler.Logins != 2 || handler.ListReads != 2 || result.Single().BackdropImageTags.Count != 1)
                throw new InvalidOperationException("Artwork identity failure did not reauthenticate and retry the operation");
        }
        Console.WriteLine("R20: actual session reauthentication for artwork -2/401 and 500 fallback passed.");
    }

    private sealed class Transport(string failure) : HttpMessageHandler
    {
        public int Logins { get; private set; }
        public int ListReads { get; private set; }
        private bool _failed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri?.Host != "artwork.invalid") throw new InvalidOperationException("Unexpected fixture host");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("System/Info/Public", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var body = path switch
            {
                "/v/api/v1/sys/version" => "{\"version\":\"0.9.8\",\"mediasrvVersion\":\"0.8.43\"}",
                "/v/api/v1/sys/config" => "{\"server_guid\":\"server\",\"server_name\":\"anonymous\"}",
                "/v/api/v2/user/loginByPassword" => "{\"token\":\"anonymous-" + ++Logins + "\"}",
                "/v/api/v1/user/info" => "{\"guid\":\"user\",\"username\":\"anonymous\",\"is_admin\":1}",
                "/v/api/v1/item/list" => List(),
                "/v/api/v1/item/movie" => "{\"guid\":\"movie\",\"title\":\"anonymous\",\"type\":\"Movie\",\"backdrops\":\"/wide.webp\"}",
                _ => throw new InvalidOperationException("Unexpected artwork fixture route: " + path)
            };
            if (path.EndsWith("/item/movie", StringComparison.Ordinal) && !_failed)
            {
                _failed = true;
                if (failure != "expired") return Task.FromResult(new HttpResponseMessage(failure == "401" ? HttpStatusCode.Unauthorized : HttpStatusCode.InternalServerError));
                return Reply("null", -2);
            }
            return Reply(body);
        }
        private string List()
        {
            ListReads++;
            return "{\"total\":1,\"list\":[{\"guid\":\"movie\",\"title\":\"anonymous\",\"type\":\"Movie\",\"poster\":\"/poster.webp\"}]}";
        }
        private static Task<HttpResponseMessage> Reply(string body, int code = 0) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"code\":" + code + ",\"data\":" + body + "}", Encoding.UTF8, "application/json") });
    }
}
