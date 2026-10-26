using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal sealed class AnonymousFixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static readonly AniBtAnime Anime = new("ani-fixture", 42, "匿名动画", "Anonymous Anime", Year: 2026);
    internal static readonly AniBtAnime Other = new("ani-other", 43, "另一部动画", "Another Anime", Year: 2025);
    private readonly NoSecrets _secrets = new();
    private readonly MoviePilotClient _mpClient;
    private readonly AniBtClient _aniClient;
    internal AppSettings Settings { get; } = new();
    internal AnonymousHandler Handler { get; } = new();
    internal MoviePilotService MoviePilot { get; }
    internal AniBtService Service { get; }
    internal EmbySession Session { get; }
    internal EmbySessionScope Scope { get; }
    internal EmbySessionScope OtherScope { get; }
    internal EmbyClient Emby => Scope.Client;
    internal EmbyItem Item { get; }
    internal AniBtScrapePlan? LoadedPlan { get; private set; }
    internal IReadOnlyList<AniBtAnime> Choices => Enumerable.Repeat(Anime, Handler.FileCount).ToArray();

    internal AnonymousFixture(int historyCount = 0, bool mixed = false, bool movie = false)
    {
        Handler.HistoryCount = historyCount;
        Handler.FileCount = mixed ? historyCount + 1 : Math.Max(1, historyCount);
        Handler.Movie = movie;
        Settings.AggregateMode = true;
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "https://mp-fixture.invalid/";
        Settings.MoviePilot.Username = "anonymous-fixture";
        Settings.MoviePilot.ProtectedPassword = "non-secret-fixture-marker";
        _mpClient = new MoviePilotClient(Handler);
        MoviePilot = new MoviePilotService(_mpClient, new MoviePilotCredentials(_secrets), Settings);
        InstallCachedIdentity();
        _aniClient = new AniBtClient(Handler);
        Service = new AniBtService(_aniClient, MoviePilot);
        var device = DeviceIdentity.Create("r037-r039-anonymous", "1");
        // Construction only; no SettingsStore/Vault operation may be called. Paths are never opened.
        var paths = new AppPaths(Path.Combine(Environment.CurrentDirectory, "artifacts", "r037-r039", "never-open-settings"));
        Session = new EmbySession(Settings, new SettingsStore(paths, _secrets), new CredentialVault(_secrets), device, Handler);
        Scope = AddScope("source-a", device);
        OtherScope = AddScope("source-b", device);
        SetCurrent(Scope);
        Item = Scope.Bind(new EmbyItem
        {
            Id = movie ? "movie-1" : "episode-1",
            Name = "库内原动画",
            Type = movie ? EmbyItemType.Movie : EmbyItemType.Episode,
            SeriesId = movie ? "" : "series-1",
            IndexNumber = 1,
            ParentIndexNumber = 1,
            Genres = ["动画"]
        });
    }

    internal void InjectCachedAvailability() => InstallCachedIdentity();

    private void InstallCachedIdentity()
    {
        var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", Private)!.Invoke(MoviePilot, null)!;
        var cachedType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var cached = Activator.CreateInstance(cachedType, Private | BindingFlags.Public, null,
            [identity, new MoviePilotSession(new Uri("https://mp-fixture.invalid/api/v1/"), "", "anonymous-fixture", true)], null);
        typeof(MoviePilotService).GetField("_session", Private)!.SetValue(MoviePilot, cached);
        typeof(MoviePilotService).GetField("_availableIdentity", Private)!.SetValue(MoviePilot, identity);
        Assert.True(MoviePilot.Available, "只能装入匿名内存缓存身份；禁止测试连接、登录和凭据恢复");
    }

    private EmbySessionScope AddScope(string id, DeviceIdentity device)
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("https://emby-fixture.invalid/emby/"), "", id, id, "匿名服务器", device);
        var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = id }, new AccountProfile { Id = id });
        var scope = new EmbySessionScope(Session, identity);
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(Session)!).Add(scope.Key, scope);
        return scope;
    }

    internal void SetCurrent(EmbySessionScope scope) => typeof(EmbySession).GetField("_current", Private)!.SetValue(Session, scope);
    internal void ChangeEmbyConnection()
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        Scope.IdentityState.Client = new EmbyClient(http, Emby.Connection with { ApiBase = new Uri("https://emby-replacement.invalid/emby/") });
    }

    internal AniBtScrapeDialog Dialog() => new(Service, Scope, Item);
    internal async Task<AniBtScrapePlan> PrepareInitialAsync()
    {
        LoadedPlan = await Service.PrepareAsync(Emby, Item, CancellationToken.None);
        return LoadedPlan;
    }

    // Version bridge only for red evidence. Each branch invokes an actual production method.
    // The legacy branch intentionally writes on the base revision; assertions must catch it.
    internal async Task<object?> PrepareActualAsync(IReadOnlyList<AniBtAnime>? choices = null, CancellationToken cancellation = default,
        Func<bool>? canContinue = null)
    {
        var plan = LoadedPlan ?? await PrepareInitialAsync();
        var prepare = typeof(AniBtService).GetMethod("PrepareForConfirmationAsync");
        if (prepare is not null) return await InvokeAsync(prepare, [Emby, plan, choices ?? Choices, cancellation, canContinue]);
        var legacy = typeof(AniBtService).GetMethods().Single(method => method.Name == "SubmitAsync");
        await InvokeAsync(legacy, [Emby, plan, choices ?? Choices, cancellation, canContinue]);
        return null;
    }

    internal async Task<IReadOnlyList<AniBtScrapeResult>> SubmitActualAsync(object prepared, CancellationToken cancellation = default,
        Func<bool>? canContinue = null)
    {
        var submit = typeof(AniBtService).GetMethods().Single(method => method.Name == "SubmitAsync" && method.GetParameters()[1].ParameterType == prepared.GetType());
        return (IReadOnlyList<AniBtScrapeResult>)(await InvokeAsync(submit, [Emby, prepared, cancellation, canContinue]))!;
    }

    private async Task<object?> InvokeAsync(MethodInfo method, object?[] arguments)
    {
        Task task;
        try { task = (Task)method.Invoke(Service, arguments)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    internal void AssertZeroWrites()
    {
        Assert.Equal(0, Handler.Writes, "确认前/取消/读取失败必须没有真正写入");
        Assert.Equal(0, Handler.Unexpected, "白名单外端点必须为零");
        Assert.Equal(0, _secrets.Accesses, "不能读取或保存凭据");
    }

    public void Dispose()
    {
        foreach (var scope in Session.Sources) scope.IdentityState.Lifetime.Cancel();
        Session.Dispose();
        MoviePilot.Dispose();
        _mpClient.Dispose();
        _aniClient.Dispose();
    }

    private sealed class NoSecrets : ISecretProtector
    {
        internal int Accesses { get; private set; }
        public string Protect(string value) { Accesses++; throw new InvalidOperationException("禁止写凭据"); }
        public string Unprotect(string value) { Accesses++; throw new InvalidOperationException("禁止读凭据"); }
    }
}

internal sealed record AnonymousRequest(string Method, string Path, string Body, bool Write);

internal sealed class AnonymousHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<AnonymousRequest> _requests = [];
    internal IReadOnlyList<AnonymousRequest> Requests { get { lock (_gate) return _requests.ToArray(); } }
    internal int Writes => Requests.Count(request => request.Write);
    internal int ApplyCount => Requests.Count(request => request.Path.Contains("RemoteSearch/Apply", StringComparison.Ordinal));
    internal int RefreshCount => Requests.Count(request => request.Path.Contains("/Refresh", StringComparison.Ordinal) && !request.Path.Contains("Library", StringComparison.Ordinal));
    internal int TransferWrites => Requests.Count(request => request.Write && request.Path.Contains("transfer/manual?", StringComparison.Ordinal));
    internal int Previews => Requests.Count(request => !request.Write && request.Path.Contains("transfer/manual?", StringComparison.Ordinal));
    internal int Unexpected { get; private set; }
    internal int HistoryCount { get; set; }
    internal int FileCount { get; set; } = 1;
    internal bool Movie { get; set; }
    internal string SearchReply { get; set; } = "[{\"Name\":\"匿名动画\",\"ProductionYear\":2026,\"ProviderIds\":{\"Tmdb\":\"verified-42\"},\"SearchProviderName\":\"TheMovieDb\"}]";
    internal bool BadPreview { get; set; }
    internal bool UnknownReceipt { get; set; }
    internal bool FailedReceipt { get; set; }
    internal bool LostReceipt { get; set; }
    internal bool ReadFailure { get; set; }
    internal bool ChangedHistory { get; set; }
    internal int ChangedHistoryIndex { get; set; } = -1;
    internal bool HistoryReadFailure { get; set; }
    internal bool RefreshFailure { get; set; }
    internal bool ScanFailure { get; set; }
    internal string TransferType { get; set; } = "move";
    internal string PreviewSuffix { get; set; } = "confirmed";
    internal Action<AnonymousRequest>? OnRequest { get; set; }
    internal TaskCompletionSource? PauseStarted { get; private set; }
    private TaskCompletionSource? _pause;
    internal void PauseNextSearch() { PauseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); _pause = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    internal void ResumeSearch() => _pause?.TrySetResult();
    internal static string OldPath(int index) => $"/fixture/library/old-{index + 1}.mkv";
    internal static string NewPath(int index, string suffix = "confirmed") => $"/fixture/library/{suffix}/new-{index + 1}.mkv";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = uri.PathAndQuery;
        var allowedHost = uri.Host is "mp-fixture.invalid" or "emby-fixture.invalid" or "emby-replacement.invalid" or "anibt.net";
        var transfer = path.Contains("transfer/manual?", StringComparison.Ordinal);
        var preview = transfer && JsonDocument.Parse(body).RootElement.GetProperty("preview").GetBoolean();
        var write = transfer && !preview || path.Contains("RemoteSearch/Apply", StringComparison.Ordinal) || uri.AbsolutePath.EndsWith("/Refresh", StringComparison.Ordinal);
        var entry = new AnonymousRequest(request.Method.Method, path, body, write);
        lock (_gate) _requests.Add(entry);
        OnRequest?.Invoke(entry);
        if (!allowedHost) return UnexpectedRequest(path);
        if (path.Contains("torrent-match", StringComparison.Ordinal)) return Json(MatchReply());
        if (path.Contains("Items/RemoteSearch/Series", StringComparison.Ordinal) || path.Contains("Items/RemoteSearch/Movie", StringComparison.Ordinal))
        {
            if (_pause is not null && !PauseStarted!.Task.IsCompleted)
            {
                PauseStarted.TrySetResult();
                await _pause.Task.WaitAsync(cancellationToken);
            }
            if (ReadFailure) return Json("{}", HttpStatusCode.ServiceUnavailable);
            return Json(SearchReply);
        }
        if (path.Contains("RemoteSearch/Apply", StringComparison.Ordinal) || path.Contains("Items/series-1/Refresh", StringComparison.Ordinal) || path.Contains("Items/movie-1/Refresh", StringComparison.Ordinal)) return Json("", RefreshFailure && path.Contains("/Refresh", StringComparison.Ordinal) ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        if (path.Contains("Library/Refresh", StringComparison.Ordinal)) return Json("", ScanFailure ? HttpStatusCode.Forbidden : HttpStatusCode.OK);
        if (path.Contains("/Users/source-a/Items/", StringComparison.Ordinal))
        {
            var isSeries = path.Contains("/series-1", StringComparison.Ordinal);
            var isMovie = path.Contains("/movie-1", StringComparison.Ordinal);
            var item = new
            {
                Id = isSeries ? "series-1" : isMovie ? "movie-1" : "episode-1",
                Name = isSeries ? "实际库内整剧" : isMovie ? "实际库内电影" : "库内单集",
                Type = isSeries ? "Series" : isMovie ? "Movie" : "Episode",
                SeriesId = "series-1",
                ParentId = "season-1",
                IndexNumber = 1,
                ParentIndexNumber = 1,
                ProductionYear = 2024,
                Genres = new[] { "动画" },
                Path = OldPath(0),
                MediaSources = Enumerable.Range(0, FileCount).Select(index => new { Id = "file-" + index, Path = OldPath(index) }).ToArray()
            };
            return Json(JsonSerializer.Serialize(item));
        }
        if (path.Contains("history/transfer", StringComparison.Ordinal)) return HistoryReadFailure ? Json("{}", HttpStatusCode.ServiceUnavailable) : Json(HistoryReply());
        if (path.Contains("media/source", StringComparison.Ordinal)) return Json("[{\"name\":\"Bangumi\",\"media_source\":\"bangumi\",\"media_types\":[\"电视剧\",\"电影\"]}]");
        if (path.Contains("media/42?", StringComparison.Ordinal) || path.Contains("media/43?", StringComparison.Ordinal)) return Json(JsonSerializer.Serialize(new { title = path.Contains("media/43?", StringComparison.Ordinal) ? "另一部动画" : "匿名动画", media_source = "bangumi", media_id = path.Contains("media/43?", StringComparison.Ordinal) ? "43" : "42", type = Movie ? "电影" : "电视剧" }));
        if (path.Contains("storage/list?", StringComparison.Ordinal))
        {
            var input = JsonDocument.Parse(body).RootElement.GetProperty("path").GetString();
            return Json(JsonSerializer.Serialize(new { success = true, data = new[] { new { storage = "local", type = "file", path = input } } }));
        }
        if (path.Contains("transfer/manual/target-path", StringComparison.Ordinal)) return Json(JsonSerializer.Serialize(new
        {
            success = true,
            data = new { target_storage = "local", target_path = "/fixture/library/new", transfer_type = TransferType, scrape = true, library_type_folder = true, library_category_folder = true }
        }));
        if (transfer)
        {
            if (!preview && LostReceipt) throw new HttpRequestException("匿名回执丢失");
            var input = JsonDocument.Parse(body).RootElement;
            var logId = input.GetProperty("logid").GetInt32();
            var source = OldPath(logId - 1);
            var target = NewPath(logId - 1, PreviewSuffix);
            var state = preview ? "preview" : UnknownReceipt ? "manual_review" : FailedReceipt ? "failed" : "completed";
            var success = preview ? !BadPreview : !UnknownReceipt && !FailedReceipt;
            return Json(JsonSerializer.Serialize(new { success, message = "", data = new { items = new[] { new { source, target, title = "匿名动画", state, success, message = "" } } } }));
        }
        return UnexpectedRequest(path);
    }

    private string HistoryReply() => JsonSerializer.Serialize(new
    {
        list = Enumerable.Range(0, HistoryCount).Select(index => new
        {
            id = index + 1,
            src = $"/fixture/download/original-{index + 1}.mkv",
            dest = OldPath(index),
            title = "实际库内整剧",
            mode = "move",
            status = true,
            type = "电视剧",
            media_source = "bangumi",
            media_id = "42",
            date = ChangedHistory || index == ChangedHistoryIndex ? "2026-10-24 12:00:00" : "2026-10-24 01:00:00",
            src_fileitem = new { storage = "local", type = "file", path = $"/fixture/download/original-{index + 1}.mkv" },
            dest_fileitem = new { storage = "local", type = "file", path = OldPath(index) }
        }).ToArray(),
        total = HistoryCount
    });

    private static string MatchReply() => JsonSerializer.Serialize(new
    {
        version = 3,
        content = new { kind = "anime_episode", confidence = "high" },
        structure = new { season = 1 },
        decision = new { kind = "auto", confidence = "high", reason = "fixture", animeId = "ani-fixture", bgmId = 42, vetoes = Array.Empty<string>() },
        candidates = new[] { new { animeId = "ani-fixture", bgmId = 42, anime = new { _id = "ani-fixture", bgmId = 42, title = new { chinese = "匿名动画", native = "Anonymous Anime" }, seasonYear = 2026 } },
            new { animeId = "ani-other", bgmId = 43, anime = new { _id = "ani-other", bgmId = 43, title = new { chinese = "另一部动画", native = "Another Anime" }, seasonYear = 2025 } } }
    });

    private HttpResponseMessage UnexpectedRequest(string path)
    {
        Unexpected++;
        throw new InvalidOperationException("匿名夹具禁止白名单外端点：" + path);
    }
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
