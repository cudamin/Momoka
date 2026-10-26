using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;

namespace Momoka.Tests;

internal sealed class SubscriptionFixture : IDisposable
{
    internal readonly record struct Request(string Method, string Path, string Query, JsonElement Body);
    internal AppSettings Settings { get; } = new();
    internal FakeMoviePilot Http { get; } = new();
    internal MoviePilotClient Client { get; }
    internal MoviePilotService Service { get; }
    internal SubscriptionFixture()
    {
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "https://moviepilot-subscribe.invalid";
        Settings.MoviePilot.Username = "anonymous-fixture";
        var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
        credentials.SetPassword(Settings.MoviePilot, "placeholder-not-an-account");
        Client = new(Http);
        Service = new(Client, credentials, Settings);
    }
    internal Task ConnectAsync() => Service.RefreshAvailabilityAsync();
    internal MoviePilotMedia Media(string type = "电视剧", string source = "themoviedb", string id = "42", int? season = null) => new()
    {
        Title = "匿名示例",
        Type = type,
        MediaSource = source,
        MediaId = id,
        Season = season,
        ConnectionStamp = Service.ConnectionStamp
    };
    internal static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    internal static JsonElement Subscription(int id, string type = "电视剧", string source = "themoviedb", string mediaId = "42",
        int? season = 1, string group = "", MoviePilotSubscribeMode mode = MoviePilotSubscribeMode.Normal) => JsonSerializer.SerializeToElement(new
        {
            id,
            name = "匿名示例",
            type,
            media_source = source,
            media_id = mediaId,
            season,
            episode_group = group,
            best_version = mode == MoviePilotSubscribeMode.Normal ? 0 : 1,
            best_version_full = mode == MoviePilotSubscribeMode.FullSeason ? 1 : 0,
            state = "R"
        });
    internal void Seed(params JsonElement[] subscriptions) => Http.Subscriptions.AddRange(subscriptions);
    internal Task<MoviePilotSubscription> ExistingAsync(int id) => ReadAsync(id);
    private async Task<MoviePilotSubscription> ReadAsync(int id) => (await Service.SubscriptionsAsync(default)).Single(item => item.Id == id);
    public void Dispose() { Service.Dispose(); Client.Dispose(); Http.Dispose(); }

    internal sealed class FakeMoviePilot : HttpMessageHandler
    {
        private readonly object _gate = new();
        internal List<JsonElement> Subscriptions { get; } = [];
        internal List<Request> Requests { get; } = [];
        internal int NextId { get; set; } = 100;
        internal bool RetainDeleted { get; set; }
        internal bool NoId { get; set; }
        internal bool KeepWrongMode { get; set; }
        internal JsonElement Seasons { get; set; } = Json("""[{"season_number":1,"name":"第一季","episode_count":8,"air_date":"2026-07-31","poster_path":""},{"season_number":3,"name":"第三季","episode_count":12,"air_date":"2027-01-01","poster_path":""},{"season_number":0,"name":"特别篇","episode_count":2,"poster_path":""}]""");
        internal JsonElement GroupSeasons { get; set; } = Json("""[{"season_number":7,"name":"组第七季","episode_count":10,"air_date":"2026-10-01","poster_path":""}]""");
        internal JsonElement Groups { get; set; } = Json("""[{"id":"group-a","name":"A 组","group_count":1,"episode_count":10},{"id":"group-b","name":"B 组","group_count":1,"episode_count":10}]""");
        internal JsonElement SearchResults { get; set; } = Json("""[{"title":"匿名示例","type":"电影","media_source":"themoviedb","media_id":"42"}]""");
        internal Func<Request, CancellationToken, Task<HttpResponseMessage?>>? Before { get; set; }
        internal int Writes => Requests.Count(item => IsWrite(item));
        internal static bool IsWrite(Request request) => request.Path.StartsWith("subscribe", StringComparison.Ordinal) && request.Method != "GET";
        internal int Count(string method, string path) => Requests.Count(item => item.Method == method && item.Path == path);
        internal static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(body is JsonElement json ? json.GetRawText() : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "moviepilot-subscribe.invalid") throw new InvalidOperationException("夹具禁止任何非匿名来源");
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath.Replace("/api/v1/", "", StringComparison.Ordinal);
            var body = request.Content is null ? default : request.Content.Headers.ContentType?.MediaType == "application/json"
                ? Json(await request.Content.ReadAsStringAsync(cancellationToken)) : default;
            var sent = new Request(request.Method.Method, path, uri.Query, body);
            lock (_gate) Requests.Add(sent);
            if (Before is { } before && await before(sent, cancellationToken) is { } intercept) return intercept;
            lock (_gate)
            {
                if (path == "login/access-token") return Reply(new { access_token = "placeholder-token" });
                if (path == "dashboard/system") return Reply(new { version = "fixed-source-fixture" });
                if (path == "media/search") return Reply(SearchResults);
                if (path == "media/source") return Reply(Json("""[{"id":"themoviedb","name":"TMDB","media_types":["电影","电视剧"]}]"""));
                if (path.StartsWith("media/groups/", StringComparison.Ordinal)) return Reply(Groups);
                if (path.StartsWith("media/group/seasons/", StringComparison.Ordinal)) return Reply(GroupSeasons);
                if (path == "media/seasons") return Reply(Seasons);
                if (path == "mediaserver/exists_remote") return Reply(Json("""{"1":[],"3":[1,2],"0":[],"7":[]}"""));
                if (path.StartsWith("media/", StringComparison.Ordinal))
                {
                    var source = uri.Query.Contains("media_source=douban", StringComparison.Ordinal) ? "douban" : "themoviedb";
                    var type = uri.Query.Contains(Uri.EscapeDataString("电影"), StringComparison.Ordinal) ? "电影" : "电视剧";
                    return Reply(JsonSerializer.SerializeToElement(new { title = "匿名示例", type, media_source = source, media_id = "42", season_info = Seasons }));
                }
                if (path == "subscribe/" && request.Method == HttpMethod.Get) return Reply(JsonSerializer.SerializeToElement(Subscriptions.ToArray()));
                if (path == "subscribe/" && request.Method == HttpMethod.Post)
                {
                    var values = body.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());
                    var id = NextId++;
                    values["id"] = JsonSerializer.SerializeToElement(id);
                    if (KeepWrongMode) { values["best_version"] = Json("0"); values["best_version_full"] = Json("0"); }
                    Subscriptions.Add(JsonSerializer.SerializeToElement(values));
                    return Reply(NoId ? Json("""{"success":true,"data":null}""") : JsonSerializer.SerializeToElement(new { success = true, data = new { id } }));
                }
                if (path == "subscribe/" && request.Method == HttpMethod.Put)
                {
                    var id = body.GetProperty("id").GetInt32();
                    var index = Subscriptions.FindIndex(item => item.GetProperty("id").GetInt32() == id);
                    if (index < 0) return Reply(new { detail = "not found" }, HttpStatusCode.NotFound);
                    if (!KeepWrongMode)
                    {
                        var values = Subscriptions[index].EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());
                        foreach (var property in body.EnumerateObject()) values[property.Name] = property.Value.Clone();
                        Subscriptions[index] = JsonSerializer.SerializeToElement(values);
                    }
                    return Reply(new { success = true });
                }
                if (path.StartsWith("subscribe/", StringComparison.Ordinal) && int.TryParse(path[10..], out var subscriptionId))
                {
                    if (request.Method == HttpMethod.Delete)
                    {
                        if (!RetainDeleted) Subscriptions.RemoveAll(item => item.GetProperty("id").GetInt32() == subscriptionId);
                        return Reply(new { success = true });
                    }
                    return Reply(Subscriptions.FirstOrDefault(item => item.GetProperty("id").GetInt32() == subscriptionId) is { ValueKind: JsonValueKind.Object } item ? item : Json("{}"));
                }
                throw new InvalidOperationException("没有匿名回话：" + request.Method + " " + path);
            }
        }
    }
}
