using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Real scope/client/HTTP operations against two anonymous version DTOs; no server or secret IO.</summary>
internal static class SubtitleTargetTests
{
    private const string ItemA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ItemB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public static void Register()
    {
        foreach (var version in new[] { "10.10.7", "12.4.0" })
        {
            foreach (var explicitAlternate in new[] { false, true })
                Test($"R04-001 Jellyfin {version} {(explicitAlternate ? "指定备选" : "默认源")}搜索下载删除和复读使用同一版本", () => Run(async () =>
                {
                    using var fixture = new Fixture(MediaServerKind.Jellyfin, version);
                    var item = await fixture.ReadAsync(ItemA);
                    var source = explicitAlternate ? item.MediaSources.Single(source => source.Id == ItemB) : item.DefaultMediaSource!;
                    var expected = explicitAlternate || version == "10.10.7" ? ItemB : ItemA;
                    Assert.Equal(expected, source.Id, "The DTO models the fixed upstream source ordering");
                    Assert.Equal("", source.ItemId, "Jellyfin identifies the version through source.Id, not Emby's ItemId");
                    var operation = new ServerSubtitles(fixture.Scope, item, source);
                    await operation.SearchAsync("chi");
                    await operation.DownloadAsync(new RemoteSubtitleInfo { Id = "provider/candidate" });
                    await operation.DeleteAsync(source.SubtitleStreams.Single());
                    var current = await operation.ReloadAsync();

                    var search = fixture.Transport.Requests.Single(request => request.Method == "GET" && request.Path.Contains("RemoteSearch", StringComparison.Ordinal));
                    var download = fixture.Transport.Requests.Single(request => request.Method == "POST");
                    var delete = fixture.Transport.Requests.Single(request => request.Method == "DELETE");
                    Assert.Equal($"/media/Items/{expected}/RemoteSearch/Subtitles/chi", search.Path);
                    Assert.Equal($"/media/Items/{expected}/RemoteSearch/Subtitles/provider%2Fcandidate", download.Path);
                    Assert.Equal($"/media/Videos/{expected}/Subtitles/3", delete.Path);
                    Assert.True(new[] { search, download, delete }.All(request => !request.Query.Contains("MediaSourceId", StringComparison.Ordinal)),
                        "Jellyfin routes select the version; its controllers do not consume MediaSourceId");
                    var reads = fixture.Transport.Requests.Where(request => request.Method == "GET" && !request.Path.Contains("RemoteSearch", StringComparison.Ordinal)).ToArray();
                    Assert.Equal(3, reads.Length);
                    Assert.True(reads.Skip(1).All(request => request.Path.EndsWith("/Items/" + expected, StringComparison.Ordinal)),
                        "Both verification and readback must read the selected version");
                    Assert.Equal(0, current.SubtitleStreams.Count());
                    Assert.Equal(1, fixture.Transport.Tracks[expected == ItemA ? ItemB : ItemA].Count,
                        "The other version's identical subtitle index must remain intact");
                }));
        }

        Test("R04-001 Emby备选源的删除与搜索下载均携带必需MediaSourceId", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby, "4.10.2.0");
            var item = await fixture.ReadAsync(ItemA);
            var source = item.MediaSources.Single(source => source.Id == "source-b");
            var operation = new ServerSubtitles(fixture.Scope, item, source);
            await operation.SearchAsync("chi");
            await operation.DownloadAsync(new RemoteSubtitleInfo { Id = "candidate" });
            await operation.DeleteAsync(source.SubtitleStreams.Single());
            var current = await operation.ReloadAsync();
            var writesAndSearch = fixture.Transport.Requests.Where(request => request.Path.Contains("RemoteSearch", StringComparison.Ordinal)
                || request.Method == "DELETE").ToArray();
            Assert.Equal(3, writesAndSearch.Length);
            Assert.True(writesAndSearch.All(request => request.Path.StartsWith($"/media/emby/Items/{ItemA}/", StringComparison.Ordinal)));
            Assert.True(writesAndSearch.All(request => request.Query.Contains("MediaSourceId=source-b", StringComparison.Ordinal)));
            Assert.Equal(0, current.SubtitleStreams.Count());
            Assert.Equal(1, fixture.Transport.Tracks[ItemA].Count);
        }));

        foreach (var product in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            foreach (var change in new[] { "missing", "duplicate", "path", "track", "other-track", "item" })
                Test($"R04-001 {product} 删除前{change}变化拒绝写入", () => Run(async () =>
                {
                    using var fixture = new Fixture(product, "10.10.7");
                    var item = await fixture.ReadAsync(ItemA);
                    var source = item.MediaSources.Single(source => source.Id == (product == MediaServerKind.Jellyfin ? ItemB : "source-b"));
                    var operation = new ServerSubtitles(fixture.Scope, item, source);
                    fixture.Transport.Change = change;
                    await RejectAsync(() => operation.DeleteAsync(source.SubtitleStreams.Single()));
                    Assert.False(fixture.Transport.Requests.Any(request => request.Method != "GET"), "Changed identity or snapshot cannot write");
                }));

        foreach (var sourceId in new[] { "", " ", ".", "..", "../other", "x/y", "x\\y", "x?y", "x#y", "dynamic-source", "00000000000000000000000000000000" })
            Test($"R04-001 Jellyfin无有效版本目标[{sourceId}]不读取或写入", () =>
            {
                using var fixture = new Fixture(MediaServerKind.Jellyfin, "10.10.7");
                var source = new MediaSource { Id = sourceId, Path = "/virtual/b.mkv", MediaStreams = [Track("B字幕")] };
                var item = new EmbyItem { Id = ItemA, MediaSources = [source] };
                Assert.Throws<InvalidOperationException>(() => new ServerSubtitles(fixture.Scope, item, source));
                Assert.Equal(0, fixture.Transport.Requests.Count);
            });
    }

    private static void Run(Func<Task> work) => work().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    private static async Task RejectAsync(Func<Task> work)
    {
        try { await work(); }
        catch (InvalidOperationException) { return; }
        throw new AssertionException("Changed target was not rejected");
    }

    private static MediaStream Track(string title) => new()
    { Index = 3, Type = "Subtitle", IsExternal = true, Codec = "srt", Language = "chi", Title = title };

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly EmbySession _session;
        internal EmbySessionScope Scope { get; }
        internal Transport Transport { get; }

        internal Fixture(MediaServerKind product, string version)
        {
            Transport = new(product, version);
            var secrets = new NoSecrets();
            var device = new DeviceIdentity("subtitle-target", "subtitle-target", "subtitle-target", "1");
            var paths = new AppPaths(Path.Combine(AppContext.BaseDirectory, "no-subtitle-target-settings-io"));
            _session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Private, null,
                [new AppSettings(), new SettingsStore(paths, secrets), new CredentialVault(secrets), device, Transport], null)!;
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri(product == MediaServerKind.Jellyfin ? "https://subtitle-target.invalid/media/" : "https://subtitle-target.invalid/media/emby/"),
                "", "user", "user", "offline", device)
            { ServerKind = product, ServerVersion = version };
            var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
            var identity = Activator.CreateInstance(identityType, Private | BindingFlags.Public, null,
                [new EmbyClient(http, connection), new ServerProfile { Id = "server" }, new AccountProfile { Id = "account" }], null)!;
            Scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Private, null, [_session, identity], null)!;
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(_session)!).Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", Private)!.SetValue(_session, Scope);
        }

        internal Task<EmbyItem> ReadAsync(string id) => Scope.ExecuteAsync((client, token) => client.GetItemAsync(id, token, EmbyFields.Files), CancellationToken.None);
        public void Dispose() => _session.Dispose();
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("No credential IO in subtitle target fixture");
        public string Unprotect(string cipherText) => throw new InvalidOperationException("No credential IO in subtitle target fixture");
    }

    private sealed record Request(string Method, string Path, string Query);

    private sealed class Transport(MediaServerKind product, string version) : HttpMessageHandler
    {
        internal List<Request> Requests { get; } = [];
        internal Dictionary<string, List<MediaStream>> Tracks { get; } = new()
        { [ItemA] = [Track("A字幕")], [ItemB] = [Track("B字幕")] };
        internal string? Change { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            if (uri.Host != "subtitle-target.invalid") throw new InvalidOperationException("Unexpected subtitle target fixture host");
            Requests.Add(new(request.Method.Method, uri.AbsolutePath, uri.Query));
            if (uri.AbsolutePath.Contains("/RemoteSearch/", StringComparison.Ordinal))
                return Task.FromResult(request.Method == HttpMethod.Get ? Json(Array.Empty<RemoteSubtitleInfo>()) : new HttpResponseMessage(HttpStatusCode.NoContent));
            if (request.Method == HttpMethod.Delete)
            {
                var itemId = uri.Segments[^3].TrimEnd('/');
                var target = product == MediaServerKind.Jellyfin ? itemId : uri.Query.Contains("MediaSourceId=source-b", StringComparison.Ordinal) ? ItemB : ItemA;
                Tracks[target].RemoveAll(track => track.Index == 3);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (request.Method == HttpMethod.Get && uri.AbsolutePath.Contains("/Items/", StringComparison.Ordinal))
                return Task.FromResult(Json(Snapshot(uri.Segments[^1])));
            throw new InvalidOperationException("Unexpected subtitle target fixture route");
        }

        private EmbyItem Snapshot(string requestedId)
        {
            var sourceA = Source(ItemA, "source-a", "A字幕");
            var sourceB = Source(ItemB, "source-b", "B字幕");
            if (Change == "path") sourceB.Path = "/virtual/replaced.mkv";
            if (Change == "track") sourceB.MediaStreams[0].Title = "changed";
            if (Change == "other-track") sourceB.MediaStreams.Add(new MediaStream { Index = 4, Type = "Subtitle", IsExternal = true, Title = "新增轨" });
            List<MediaSource> sources = product == MediaServerKind.Jellyfin && (version == "10.10.7" || requestedId == ItemB)
                ? [sourceB, sourceA] : [sourceA, sourceB];
            if (Change == "missing") sources.Remove(sourceB);
            if (Change == "duplicate") sources.Add(sourceB);
            return new EmbyItem { Id = Change == "item" ? requestedId == ItemA ? ItemB : ItemA : requestedId, Type = EmbyItemType.Movie, MediaSources = sources };
        }

        private MediaSource Source(string id, string embyId, string title) => new()
        {
            Id = product == MediaServerKind.Jellyfin ? id : embyId,
            ItemId = product == MediaServerKind.Emby ? id : "",
            Path = id == ItemA ? "/virtual/a.mkv" : "/virtual/b.mkv",
            Name = title,
            Size = 1,
            MediaStreams = [.. Tracks[id].Select(track => Track(track.Title!))]
        };

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
