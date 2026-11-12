using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Emby;

namespace Momoka.Shell;

/// <summary>Only the two anonymous hosts, artificial login and read-only movie catalogue used by the keyboard journey.</summary>
internal sealed class KeyboardJourneyProbeServer
{
    internal const string Address = "https://keyboard-a.invalid";
    internal const string Username = "keyboard-user";
    internal const string Password = "fixture-password";
    internal const string Keyword = "keyboard-query";
    internal const string MovieId = "keyboard-movie";
    internal byte[] Poster { get; set; } = [];
    internal int AuthenticationPosts { get; private set; }
    internal int RejectedWrites { get; private set; }
    internal List<(string Host, string Path, string Method, string? Search)> Requests { get; } = [];

    internal async Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = request.RequestUri ?? throw new InvalidOperationException("键盘夹具缺少URI");
        if (uri.Host is not ("keyboard-a.invalid" or "keyboard-b.invalid"))
            throw new InvalidOperationException("键盘流程禁止真实网络地址");
        var path = uri.AbsolutePath;
        if (request.Method == HttpMethod.Post && path.EndsWith("/Users/AuthenticateByName", StringComparison.Ordinal))
        {
            AuthenticationPosts++;
            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = input.RootElement;
            var user = root.TryGetProperty("UserName", out var emby) ? emby.GetString()
                : root.TryGetProperty("Username", out var jellyfin) ? jellyfin.GetString() : null;
            if (user != Username || !root.TryGetProperty("Pw", out var password) || password.GetString() != Password)
                return new(HttpStatusCode.Unauthorized);
            return Json(new AuthenticationResult { AccessToken = "anonymous-keyboard-token", ServerId = uri.Host, User = User() });
        }
        if (request.Method != HttpMethod.Get)
        {
            RejectedWrites++;
            throw new InvalidOperationException("键盘流程只允许人工登录与只读目录");
        }
        var search = Parameter(uri, "SearchTerm");
        Requests.Add((uri.Host, path, request.Method.Method, search));
        if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
            return Json(new PublicSystemInfo { Id = uri.Host, ProductName = "Emby", ServerName = uri.Host, Version = "4.10.1.0" });
        if (path.EndsWith("/Users/Public", StringComparison.Ordinal)) return Json(new[] { User() });
        if (path.EndsWith("/Users/" + Username, StringComparison.Ordinal)) return Json(User());
        if (path.Contains("/Images/", StringComparison.Ordinal))
            return Poster.Length == 0 ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(Poster) };
        if (path.EndsWith("/Views", StringComparison.Ordinal))
            return Json(new ItemsResult { Items = [new EmbyItem { Id = "keyboard-library", Name = "匿名电影库", Type = EmbyItemType.CollectionFolder, CollectionType = "movies" }], TotalRecordCount = 1 });
        if (path.EndsWith("/Items/" + MovieId, StringComparison.Ordinal)) return Json(Movie());
        if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Json(new[] { Movie() });
        if (path.EndsWith("/Movies/Recommendations", StringComparison.Ordinal)) return Json(Array.Empty<MovieRecommendation>());
        if (path.EndsWith("/Items", StringComparison.Ordinal))
        {
            var found = search is null || search == Keyword && uri.Host == "keyboard-b.invalid";
            return Json(new ItemsResult { Items = found ? [Movie()] : [], TotalRecordCount = found ? 1 : 0 });
        }
        if (path.EndsWith("/Items/Resume", StringComparison.Ordinal) || path.EndsWith("/Similar", StringComparison.Ordinal))
            return Json(new ItemsResult());
        if (path.EndsWith("/Items/Counts", StringComparison.Ordinal)) return Json(new { MovieCount = 1, SeriesCount = 0, EpisodeCount = 0 });
        throw new InvalidOperationException("键盘夹具未安排匿名路由：" + path);
    }

    private static EmbyUser User() => new()
    {
        Id = Username, Name = Username, HasPassword = true,
        Policy = new EmbyUserPolicy { IsAdministrator = false, EnableContentDeletion = false, EnableContentDownloading = false }
    };
    private static EmbyItem Movie() => new()
    {
        Id = MovieId, Name = "匿名键盘电影", Type = EmbyItemType.Movie, RunTimeTicks = TimeSpan.FromMinutes(90).Ticks,
        Overview = "此条目仅用于原生键盘导航，网络、媒体和写入均由匿名夹具隔离。",
        ImageTags = new() { ["Primary"] = "keyboard" }, BackdropImageTags = ["keyboard"],
        MediaSources = [new MediaSource { Id = "keyboard-source", Name = "匿名版本", Container = "mkv",
            MediaStreams = [new() { Type = "Video", Index = 0, Codec = "h264", Width = 1920, Height = 1080 },
                new() { Type = "Audio", Index = 1, Codec = "aac", IsDefault = true }] }]
    };
    private static string? Parameter(Uri uri, string name) => uri.Query.TrimStart('?').Split('&').Select(value => value.Split('=', 2))
        .Where(pair => pair.Length == 2 && pair[0] == name).Select(pair => Uri.UnescapeDataString(pair[1])).FirstOrDefault();
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}

internal static class KeyboardJourneyProbeRules
{
    // x:Bind fills PosterCard.Card; ItemsView's outer container does not need a DataContext.
    internal static T? FindResult<T>(IEnumerable<T> containers, Func<T, string?> cardIdentity, string wanted) where T : class =>
        containers.FirstOrDefault(container => cardIdentity(container) == wanted);
    internal static void RequireName(string actual, string expected)
    {
        if (actual != expected) throw new InvalidOperationException("键盘流程自动化名称不匹配实际字段");
    }
    internal static void RequireFocus(string name, string role, bool inTarget)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(role) || !inTarget)
            throw new InvalidOperationException("键盘流程焦点或自动化名称/角色不正确");
    }
    internal static void RequireKeyProgress(int before, int after)
    {
        if (after <= before) throw new InvalidOperationException("阶段完成但没有收到真实XAML键盘事件");
    }
    internal static void RequireFinal(string search, bool expectedSource, int plays, int authentications, int rejectedWrites)
    {
        if (search != KeyboardJourneyProbeServer.Keyword || !expectedSource || plays != 0 || authentications != 1 || rejectedWrites != 0)
            throw new InvalidOperationException("键盘流程返回、身份、登录或零播放/写入回执不一致");
    }
}
