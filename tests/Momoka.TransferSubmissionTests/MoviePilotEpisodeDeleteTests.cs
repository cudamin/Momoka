using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotEpisodeDeleteTests
{
    internal static void Register()
    {
        foreach (var multiple in new[] { "duplicate", "versions" })
            foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource,
                MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
                Test("单集删除：" + multiple + " 打开选择列表，取消不提交 " + action, () =>
                {
                    using var fixture = new EpisodeDeleteFixture();
                    fixture.History.Multiple = multiple;
                    var dialogs = MoviePilotMediaManagementDialog.Constructions;
                    Complete(fixture.Invoke(action));
                    Assert.Equal(dialogs + 1, MoviePilotMediaManagementDialog.Constructions);
                    Assert.Equal(0, fixture.History.Writes.Count);
                    Assert.Equal(0, fixture.Shell.Notices.Count);
                    AssertReleased(fixture);
                });
        foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource,
            MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
        {
            Test("单集直接删除：" + action + " 无确认窗、单次请求且重复选择不重发", () =>
            {
                using var fixture = new EpisodeDeleteFixture();
                var dialogs = MoviePilotMediaManagementDialog.Constructions;
                Complete(fixture.Invoke(action));
                Assert.Equal(dialogs, MoviePilotMediaManagementDialog.Constructions);
                Assert.Equal(1, fixture.History.Writes.Count);
                var request = fixture.History.Writes.Single();
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Equal("{\"id\":1}", request.Body);
                Assert.Equal("?deletesrc=" + (action is MoviePilotHistoryAction.DeleteSource or MoviePilotHistoryAction.DeleteAll ? "true" : "false") +
                    "&deletedest=" + (action is MoviePilotHistoryAction.DeleteDestination or MoviePilotHistoryAction.DeleteAll ? "true" : "false"), request.Query);
                Assert.Contains("第 1 集", fixture.Shell.Notices.Single().Message);
                Assert.Equal(InfoBarSeverity.Success, fixture.Shell.Notices.Single().Severity);
                Complete(fixture.Invoke(MoviePilotHistoryAction.DeleteAll));
                Assert.Equal(1, fixture.History.Writes.Count);
                AssertReleased(fixture);
            });
        }
        foreach (var fault in new[] { "lost", "write-json", "no-receipt", "partial" })
        {
            Test("单集直接删除：" + fault + " 提示待核对且换档不重发", () =>
            {
                using var fixture = new EpisodeDeleteFixture();
                fixture.History.Fault = fault;
                Complete(fixture.Invoke(MoviePilotHistoryAction.DeleteAll));
                Assert.Equal(1, fixture.History.Writes.Count);
                Assert.Equal(InfoBarSeverity.Warning, fixture.Shell.Notices.Single().Severity);
                Assert.Contains("核对", fixture.Shell.Notices.Single().Message);
                Assert.DoesNotContain("本次没有提交", fixture.Shell.Notices.Single().Message);
                fixture.History.Fault = "";
                Complete(fixture.Invoke(MoviePilotHistoryAction.DeleteRecord));
                Assert.Equal(1, fixture.History.Writes.Count);
                AssertReleased(fixture);
            });
        }
        foreach (var revoke in new[] { "navigate-return", "content-return", "connection", "source-return", "source-signout", "stop" })
            foreach (var read in new[] { 1, 3 })
            {
                Test("单集直接删除：" + revoke + " 在只读核对 " + read + " 撤销后零写", () =>
                {
                    using var fixture = new EpisodeDeleteFixture();
                    var page = fixture.Shell.ContentFrame.Content;
                    fixture.Transport.OnHistoryRead = current =>
                    {
                        if (current != read) return;
                        switch (revoke)
                        {
                            case "navigate-return":
                                fixture.Shell.ContentFrame.NavigateTo(new object());
                                fixture.Shell.ContentFrame.Content = page;
                                break;
                            case "content-return":
                                fixture.Shell.ContentFrame.Content = new object();
                                fixture.Shell.ContentFrame.Content = page;
                                break;
                            case "connection": fixture.Settings.MoviePilot.Username = "replacement"; break;
                            case "source-return":
                                var other = fixture.InstallScope("other");
                                fixture.Session.SelectSource(other);
                                fixture.Session.SelectSource(fixture.Scope);
                                break;
                            case "source-signout": fixture.Session.SignOut(); break;
                            default: fixture.Shell.StopForFixture(); break;
                        }
                    };
                    Complete(fixture.Invoke(MoviePilotHistoryAction.DeleteRecord));
                    Assert.Equal(0, fixture.History.Writes.Count);
                    if (revoke is "navigate-return" or "content-return" or "source-return" or "source-signout")
                        Assert.Equal(0, fixture.Shell.Notices.Count, "离开后的旧操作不能在返回页回填通知");
                    AssertReleased(fixture);
                });
            }
        Test("单集直接删除：在途重复点击不新增核对、关闭后迟到响应不发请求", () =>
        {
            using var fixture = new EpisodeDeleteFixture();
            fixture.Transport.PauseAt = 1;
            var pending = fixture.Invoke(MoviePilotHistoryAction.DeleteAll);
            Complete(fixture.Transport.Paused.Task);
            Assert.True(fixture.Shell.HistoryShowing && fixture.Shell.HoldsDelete);
            Complete(fixture.Invoke(MoviePilotHistoryAction.DeleteSource));
            Assert.Equal(1, fixture.Transport.HistoryReads);
            fixture.Shell.ContentFrame.Content = null;
            fixture.Transport.Release.TrySetResult(true);
            Complete(pending);
            Assert.Equal(0, fixture.History.Writes.Count);
            AssertReleased(fixture);
        });
        Test("单集直接删除：明确核对失败只提示未提交", () =>
        {
            using var fixture = new EpisodeDeleteFixture();
            fixture.History.Fault = "missing";
            Complete(fixture.Invoke(MoviePilotHistoryAction.DeleteRecord));
            Assert.Equal(0, fixture.History.Writes.Count);
            Assert.Contains("本次没有提交", fixture.Shell.Notices.Single().Message);
            AssertReleased(fixture);
        });
    }

    private static void AssertReleased(EpisodeDeleteFixture fixture)
    {
        Assert.False(fixture.Shell.HistoryShowing);
        Assert.False(fixture.Shell.HoldsDelete);
        Assert.Equal(0, fixture.Shell.ContentFrame.CallbackCount);
        Assert.Equal(0, fixture.Shell.ContentFrame.NavigationSubscribers);
        Assert.Equal(0, fixture.EventSubscribers(fixture.Service, "AvailabilityChanged"));
        Assert.Equal(0, fixture.EventSubscribers(fixture.Service, "HistoryCapabilitiesChanged"));
        Assert.Equal(0, fixture.EventSubscribers(fixture.Session, "SourcesChanged"));
    }
    private static void Complete(Task task)
    {
        using var finished = new ManualResetEventSlim();
        _ = task.ContinueWith(_ => finished.Set(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)), "匿名请求任务未结束");
        TransferSubmissionFixture.Complete(task);
    }
}

internal sealed class EpisodeDeleteFixture : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly MoviePilotClient _client;
    private readonly RejectEpisodeSecrets _secrets = new();
    internal AppSettings Settings { get; } = new();
    internal EpisodeHistoryTransport History => Transport;
    internal EpisodeHistoryTransport Transport { get; }
    internal MoviePilotService Service { get; }
    internal EmbySession Session { get; }
    internal EmbySessionScope Scope { get; }
    internal ShellPage Shell { get; }
    internal EmbyItem Item { get; }

    internal EpisodeDeleteFixture()
    {
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "http://192.0.2.10:3001";
        Settings.MoviePilot.Username = "anonymous-fixture";
        Settings.MoviePilot.ProtectedPassword = "not-a-secret-fixture";
        Transport = new();
        _client = new(Transport);
        Service = new(_client, new MoviePilotCredentials(_secrets), Settings);
        var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", Members)!.Invoke(Service, null)!;
        var sessionType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var cached = Activator.CreateInstance(sessionType, Members, null,
            [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
        typeof(MoviePilotService).GetField("_session", Members)!.SetValue(Service, cached);
        typeof(MoviePilotService).GetField("_availableIdentity", Members)!.SetValue(Service, identity);
        TransferSubmissionFixture.Complete(Service.HistoryCapabilitiesAsync(CancellationToken.None));

        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "momoka-unused-episode-delete-" + Guid.NewGuid().ToString("N")));
        var store = new SettingsStore(paths, _secrets);
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [Settings, store, new CredentialVault(_secrets), new DeviceIdentity("anonymous", "fixture", "fixture", "0"), new EpisodeMediaTransport(Transport)], null)!;
        Scope = InstallScope("original");
        typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, Scope);
        Shell = new(Service, Session);
        Item = Scope.Bind(new EmbyItem { Id = "episode", Name = "第 1 集", Type = EmbyItemType.Episode });
    }

    internal Task Invoke(MoviePilotHistoryAction action) => Shell.InvokeHistory(Scope, Item, action);
    internal EmbySessionScope InstallScope(string name)
    {
        var connection = new EmbyConnection(new Uri("https://episode-fixture.invalid/emby/"), "anonymous-token", "anonymous-user",
            "anonymous", "anonymous", Session.Device);
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = name }, new AccountProfile { Id = name }], null)!;
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!).Add(scope.Key, scope);
        return scope;
    }
    internal int EventSubscribers(object owner, string name) =>
        ((Delegate?)owner.GetType().GetField(name, Members)!.GetValue(owner))?.GetInvocationList().Length ?? 0;
    public void Dispose()
    {
        Transport.Release.TrySetResult(true);
        Service.Dispose();
        Session.Dispose();
        _client.Dispose();
        Assert.Equal(0, _secrets.Calls);
    }
    private sealed class RejectEpisodeSecrets : ISecretProtector
    {
        internal int Calls { get; private set; }
        public string Protect(string value) { Calls++; throw new AssertionException("禁止实际凭据保护"); }
        public string Unprotect(string value) { Calls++; throw new AssertionException("禁止实际凭据读取／登录"); }
    }
}

internal sealed class EpisodeHistoryTransport : HttpMessageHandler
{
    internal string Multiple { get; set; } = "";
    internal string Fault { get; set; } = "";
    internal List<(HttpMethod Method, string Path, string Query, string Body)> Writes { get; } = [];
    internal int HistoryReads { get; private set; }
    internal int PauseAt { get; set; }
    internal Action<int>? OnHistoryRead { get; set; }
    internal TaskCompletionSource<bool> Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Assert.Equal("192.0.2.10", request.RequestUri!.Host);
        token.ThrowIfCancellationRequested();
        var path = request.RequestUri.AbsolutePath;
        if (path.EndsWith("user/current", StringComparison.Ordinal)) return Json(new { is_active = true, is_superuser = true });
        if (path.EndsWith("system/global/user", StringComparison.Ordinal)) return Json(new { AI_AGENT_ENABLE = false });
        if (request.Method == HttpMethod.Get && path == "/api/v1/history/transfer")
        {
            var read = ++HistoryReads;
            OnHistoryRead?.Invoke(read);
            if (read == PauseAt)
            {
                Paused.TrySetResult(true);
                await Release.Task; // Deliberately late response: the actual lifetime must still refuse submission.
            }
            token.ThrowIfCancellationRequested();
            object Record(int id, string target) => new
            {
                id,
                title = "Fixture",
                type = "电视剧",
                media_source = "themoviedb",
                media_id = "1",
                status = true,
                mode = "softlink",
                seasons = "S01",
                episodes = "E01",
                src = "/downloads/one.mkv",
                dest = target,
                src_fileitem = new { storage = "local", type = "file", path = "/downloads/one.mkv" },
                dest_fileitem = new { storage = "local", type = "file", path = target }
            };
            object[] records = Fault == "missing" ? [] : Multiple.Length == 0 ? [Record(1, "/library/one.mkv")]
                : [Record(1, "/library/one.mkv"), Record(2, Multiple == "duplicate" ? "/library/one.mkv" : "/library/two.mkv")];
            return Json(new { total = records.Length, list = records });
        }
        if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Delete)
        {
            Writes.Add((request.Method, path, request.RequestUri.Query, await request.Content!.ReadAsStringAsync(token)));
            if (Fault == "lost") throw new HttpRequestException("匿名回执丢失");
            if (Fault == "write-json") return new(HttpStatusCode.OK) { Content = new StringContent("{") };
            if (Fault == "no-receipt") return Json(new { });
            return Json(new
            {
                source = new { status = request.RequestUri.Query.Contains("deletesrc=true", StringComparison.Ordinal) ? "deleted" : "not_requested" },
                destination = new { status = Fault == "partial" ? "failed" : request.RequestUri.Query.Contains("deletedest=true", StringComparison.Ordinal) ? "deleted" : "not_requested" },
                history = Fault == "partial" ? "retained" : "deleted",
                message = Fault == "partial" ? "匿名目标删除失败" : ""
            });
        }
        throw new AssertionException("匿名集删除夹具拒绝未登记MP请求：" + request.Method + " " + path);
    }
    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { success = true, data }), System.Text.Encoding.UTF8, "application/json") };
}

internal sealed class EpisodeMediaTransport(EpisodeHistoryTransport history) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Assert.Equal("episode-fixture.invalid", request.RequestUri!.Host);
        Assert.Equal(HttpMethod.Get, request.Method);
        token.ThrowIfCancellationRequested();
        object reply = request.RequestUri.AbsolutePath switch
        {
            "/emby/Users/anonymous-user/Items/episode" => new EmbyItem
            {
                Id = "episode",
                Name = "第 1 集",
                Type = EmbyItemType.Episode,
                Path = "/library/one.mkv",
                MediaSources = history.Multiple == "versions" ?
                    [new() { Id = "one", Path = "/library/one.mkv" }, new() { Id = "two", Path = "/library/two.mkv" }] : [],
                SeriesId = "series",
                IndexNumber = 1,
                ParentIndexNumber = 1
            },
            "/emby/Users/anonymous-user/Items/series" => new EmbyItem
            {
                Id = "series",
                Name = "Fixture",
                Type = EmbyItemType.Series,
                ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1" }
            },
            _ => throw new AssertionException("匿名集删除夹具拒绝未登记媒体请求：" + request.RequestUri.AbsolutePath)
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(reply), System.Text.Encoding.UTF8, "application/json") });
    }
}
