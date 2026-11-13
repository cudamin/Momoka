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
    internal const int ManualStageMilliseconds = 300_000;
    internal const int AutomaticStageMilliseconds = 60_000;

    internal static void RequireResumeMode(bool manual, bool resumeFromSearch)
    {
        if (resumeFromSearch && !manual) throw new InvalidOperationException("从搜索继续只供手动匿名验收");
    }

    internal static string ManualInstruction(string stage, bool resumeFromSearch) => resumeFromSearch
        ? "前六步上轮已验证，本轮从搜索继续；本轮账号为匿名播种。" + ManualInstruction(stage)
        : ManualInstruction(stage);

    internal static string ManualInstruction(string stage) => stage switch
    {
        "address-ready" => "第1步：焦点已放到服务器地址。输入 https://keyboard-a.invalid 后按 Enter。",
        "username-ready" => "第2步：焦点已放到用户名。按 Ctrl+A，输入 keyboard-user，再按 Enter。",
        "password-ready" => "第3步：焦点已放到密码。输入 fixture-password 后按 Enter 登录匿名测试账号。",
        "account-ready" => "第4步：焦点已放到右上角账号按钮。按 Enter 打开账号菜单。",
        "source-ready" => "第5步：焦点已放到 keyboard-b.invalid 来源。按 Enter 切换来源。",
        "search-entry-ready" => "第6步：焦点已放到标题栏搜索入口。按 Enter 进入搜索页。",
        "search-ready" => "第7步：焦点已放到搜索框。输入 keyboard-query 后按 Enter 搜索。",
        "result-ready" => "第8步：焦点已放到匿名影片的导航容器。按 Enter 打开详情。",
        "settings-entry-ready" => "第9步：焦点已放到设置按钮。按 Enter 打开设置窗口。",
        "settings-ready" => "第10步：焦点已放到设置搜索框。按 Tab，再按 Shift+Tab 返回该框。",
        "settings-close-ready" => "第11步：按 Alt+F4 关闭设置窗口。这里只隐藏测试设置窗口，继续回到详情页。",
        "back-ready" => "第12步：焦点已放到返回按钮。按 Alt+← 返回搜索页。",
        "complete" => "键盘流程已完成。测试已记录输入与返回结果，可回到检查台。",
        "cancelled" => "已取消本次键盘验收。取消不是通过，已保留执行到的步骤。",
        _ => "正在准备匿名键盘验收。每步最多等待5分钟；焦点由测试定位，按屏幕提示操作。"
    };
    internal static bool SourceReady(bool selectedSource, bool homeReady, bool accountMenuOpen, bool accountPopupPresent) =>
        selectedSource && homeReady && !accountMenuOpen && !accountPopupPresent;
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
