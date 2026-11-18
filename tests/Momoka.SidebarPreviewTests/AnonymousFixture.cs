using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;

namespace Momoka.Tests;

/// <summary>No authentication or network: the real session adopts synthetic identities and all HTTP is intercepted in memory.</summary>
internal sealed class AnonymousFixture : IDisposable
{
    internal AppSettings Settings { get; } = new() { AggregateMode = true };
    internal EmbySession Session { get; }
    internal EmbySessionScope Source { get; }
    internal ShellPage Shell { get; }
    internal NavigationViewItemPair Anchors { get; }
    internal AnonymousHttp Http { get; } = new();
    internal string Root { get; }

    internal AnonymousFixture()
    {
        Settings.Servers.Clear();
        Root = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(new AppPaths(Root), PassthroughSecretProtector.Instance);
        var constructor = typeof(EmbySession).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(AppSettings), typeof(SettingsStore), typeof(CredentialVault), typeof(DeviceIdentity), typeof(HttpMessageHandler)], null)
            ?? throw new InvalidOperationException("Missing anonymous HTTP session boundary");
        Session = (EmbySession)constructor.Invoke([Settings, store, new CredentialVault(PassthroughSecretProtector.Instance),
            DeviceIdentity.Create("sidebar-preview-fixture", "test"), Http]);
        Source = AddSource("source-a");
        Shell = new ShellPage(Settings, Session);
        Anchors = new(
            Shell.AddLibrary(Source.Bind(new EmbyItem { Id = "movies", Name = "匿名电影库", CollectionType = "movies" }), 80),
            Shell.AddLibrary(Source.Bind(new EmbyItem { Id = "series", Name = "匿名剧集库", CollectionType = "tvshows" }), 124));
        Http.Items["movies"] = [Media("movie-a", EmbyItemType.Movie), Media("movie-b", EmbyItemType.Movie), Media("movie-c", EmbyItemType.Movie)];
        Http.Items["series"] = [Media("series-a", EmbyItemType.Series), Media("season-a", EmbyItemType.Season), Media("episode-a", EmbyItemType.Episode)];
    }

    internal EmbySessionScope AddSource(string id)
    {
        var server = new ServerProfile { Id = id, Name = "匿名服务器", Url = "https://preview.invalid" };
        var account = new AccountProfile { Id = "anonymous", Username = "fixture", UserId = "fixture" };
        server.Accounts.Add(account);
        Settings.Servers.Add(server);
        var connection = new EmbyConnection(new Uri("https://preview.invalid/emby/"), "placeholder-token", "fixture", "fixture",
            server.Name, Session.Device) { ServerId = id };
        var adopt = typeof(EmbySession).GetMethod("Adopt", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing source-adoption boundary");
        return (EmbySessionScope)adopt.Invoke(Session, [server, account, connection, 0L])!;
    }

    internal static EmbyItem Media(string id, string type) => new()
    {
        Id = id,
        Name = "匿名媒体 " + id,
        Type = type,
        ImageTags = new Dictionary<string, string> { [EmbyImageStore.Primary] = "fixture-image-tag" }
    };

    internal EmbyImageStore Images() => new(Session, Path.Combine(Root, "images"));
    public void Dispose()
    {
        Shell.Hide();
        Session.Dispose();
        Momoka.Shell.Media.PosterLoader.Decode = (_, _) => Task.FromResult<Momoka.Shell.Media.Bitmap?>(new());
    }

    internal sealed record NavigationViewItemPair(Microsoft.UI.Xaml.Controls.NavigationViewItem Movies, Microsoft.UI.Xaml.Controls.NavigationViewItem Series);
}

internal sealed class AnonymousHttp : HttpMessageHandler
{
    internal Dictionary<string, EmbyItem[]> Items { get; } = [];
    internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Override { get; set; }
    internal int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri?.Host != "preview.invalid" || request.Method != HttpMethod.Get)
            throw new InvalidOperationException("Sidebar preview fixture refuses non-anonymous or mutating requests");
        Requests++;
        if (Override is { } callback) return callback(request, token);
        return Task.FromResult(Reply(request));
    }

    internal HttpResponseMessage Reply(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("/Images/", StringComparison.Ordinal))
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        var query = request.RequestUri.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "");
        if (!query.TryGetValue("ParentId", out var parent))
            throw new InvalidOperationException("Unexpected anonymous fixture route");
        if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(Items.GetValueOrDefault(parent) ?? []), Encoding.UTF8, "application/json")
            };
        // 音乐库预览走 HomeLatest.MusicQuery（Users/{id}/Items?IncludeItemTypes=MusicAlbum）：按请求里的
        // 条目类型与上限筛，和服务器一样只交回专辑；音频、电影不混进音乐预览。
        if (path.EndsWith("/Items", StringComparison.Ordinal))
        {
            IEnumerable<EmbyItem> items = Items.GetValueOrDefault(parent) ?? [];
            if (query.TryGetValue("IncludeItemTypes", out var types) && types.Length > 0)
            {
                var allowed = types.Split(',', StringSplitOptions.RemoveEmptyEntries);
                items = items.Where(item => allowed.Contains(item.Type, StringComparer.OrdinalIgnoreCase));
            }
            if (query.TryGetValue("Limit", out var limitText) && int.TryParse(limitText, out var limit) && limit > 0)
                items = items.Take(limit);
            var page = items.ToArray();
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { Items = page, TotalRecordCount = page.Length }),
                    Encoding.UTF8, "application/json")
            };
        }
        throw new InvalidOperationException("Unexpected anonymous fixture route");
    }
}
