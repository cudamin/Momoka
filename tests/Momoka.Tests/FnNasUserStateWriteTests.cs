using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>Confirmed native state writes and real detail controls; only synthetic in-memory HTTP.</summary>
internal static class FnNasUserStateWriteTests
{
    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("飞牛用户状态确认／真实详情 VM（假HTTP，无登录或真实写入）");
        try
        {
            Register();
            RegisterDetails(new SettingsPageStateTests.ShellFixture(shellAssemblyPath, sourceRoot));
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    public static void Register()
    {
        foreach (var field in new[] { "watched", "favorite" })
            foreach (var value in new[] { true, false })
                TestHarness.Test($"R-024 {field}={value}写入确认后不绑补读，503也不重发写", () =>
                {
                    using var state = new State();
                    var result = state.Write(field, value).GetAwaiter().GetResult();
                    Assert.Null(result, "原生已确认写没有UserData正文，不能伪造全字段快照");
                    Assert.Equal(1, state.Transport.Writes);
                    Assert.Equal(0, state.Transport.Reads, "写入任务不能夹带可失败的补读");
                });
        foreach (var failure in new[] { "http500", "permission", "bad-envelope" })
            TestHarness.Test($"R-024 写入{failure}仍严格失败且没有补读", () =>
            {
                using var state = new State();
                state.Transport.WriteFailure = failure;
                Assert.Throws<FnNasApiException>(() => state.Write("favorite", true).GetAwaiter().GetResult());
                Assert.Equal(1, state.Transport.Writes);
                Assert.Equal(0, state.Transport.Reads);
            });
        TestHarness.Test("R-024 写入预取消零请求，不宣称确认成功", () =>
        {
            using var state = new State();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => state.Write("watched", true, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(0, state.Transport.Writes);
            Assert.Equal(0, state.Transport.Reads);
        });
        foreach (var failure in new[] { "http503", "http403", "http401", "permission", "signature", "bad-envelope" })
            TestHarness.Test($"R-024 独立补读{failure}严格抛错，不重复已确认写入", () =>
            {
                using var state = new State();
                Assert.Null(state.Write("favorite", true).GetAwaiter().GetResult());
                state.Transport.ReadFailure = failure;
                Assert.Throws<FnNasApiException>(() => state.Client.GetFnNasUserDataAsync("item", default).GetAwaiter().GetResult());
                Assert.Equal(1, state.Transport.Writes);
                Assert.Equal(1, state.Transport.Reads);
            });
        TestHarness.Test("R-024 独立补读预取消零额外请求，确认写不重发", () =>
        {
            using var state = new State();
            Assert.Null(state.Write("watched", true).GetAwaiter().GetResult());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => state.Client.GetFnNasUserDataAsync("item", cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(1, state.Transport.Writes);
            Assert.Equal(0, state.Transport.Reads);
        });
        TestHarness.Test("R-024 独立补读成功可取服务器派生字段，不伪造未知统计", () =>
        {
            using var state = new State();
            Assert.Null(state.Write("watched", true).GetAwaiter().GetResult());
            state.Transport.ReadItem = new JsonObject
            {
                ["guid"] = "item",
                ["type"] = "Movie",
                ["watched"] = 1,
                ["is_favorite"] = 1,
                ["ts"] = 4,
                ["duration"] = 20
            };
            var data = state.Client.GetFnNasUserDataAsync("item", default).GetAwaiter().GetResult()!;
            Assert.True(data.Played);
            Assert.True(data.IsFavorite);
            Assert.Equal(TimeSpan.FromSeconds(4).Ticks, data.PlaybackPositionTicks);
            Assert.Null(data.UnplayedItemCount);
            Assert.Equal(1, state.Transport.Writes);
            Assert.Equal(1, state.Transport.Reads);
        });
        TestHarness.Test("R-024 迟到补读只丰富自己的确认组，不回写另一组或改响应对象", () =>
        {
            var order = new ItemUserStateOrder();
            var watched = order.BeginWatched(true);
            var current = order.Apply(watched, null, null)!;
            var favorite = order.BeginFavorite(true);
            current = order.Apply(favorite, current, null)!;
            var snapshot = new EmbyUserData { Played = false, IsFavorite = false, PlaybackPositionTicks = 30, PlayCount = 4 };
            var refreshed = order.RefreshConfirmed(watched, current, snapshot)!;
            Assert.True(refreshed.Played);
            Assert.True(refreshed.IsFavorite);
            Assert.Equal(4, refreshed.PlayCount);
            Assert.False(snapshot.Played);
            Assert.False(snapshot.IsFavorite);
            Assert.False(ReferenceEquals(snapshot, refreshed));
            order.Complete(watched);
            order.Complete(favorite);
        });
        TestHarness.Test("R-024 新未看确认后旧已看补读失效，新补读仍清进度", () =>
        {
            var order = new ItemUserStateOrder();
            var early = order.BeginWatched(true);
            var current = order.Apply(early, null, null)!;
            var latest = order.BeginWatched(false);
            current = order.Apply(latest, current, null)!;
            Assert.Null(order.RefreshConfirmed(early, current, new EmbyUserData { Played = true, PlaybackPositionTicks = 90 }));
            var refreshed = order.RefreshConfirmed(latest, current, new EmbyUserData { Played = true, PlaybackPositionTicks = 90, PlayedPercentage = 90 })!;
            Assert.False(refreshed.Played);
            Assert.Equal(0L, refreshed.PlaybackPositionTicks);
            Assert.Equal(0d, refreshed.PlayedPercentage);
            order.Complete(early);
            order.Complete(latest);
        });
        TestHarness.Test("R-024 非重叠收藏补读保留服务器进度价值，确认位不被旧快照否定", () =>
        {
            var order = new ItemUserStateOrder();
            var favorite = order.BeginFavorite(true);
            var current = order.Apply(favorite, new EmbyUserData(), null)!;
            var refreshed = order.RefreshConfirmed(favorite, current, new EmbyUserData
            {
                IsFavorite = false,
                Played = true,
                PlaybackPositionTicks = 3,
                PlayedPercentage = 4,
                UnplayedItemCount = 5
            })!;
            Assert.True(refreshed.IsFavorite);
            Assert.True(refreshed.Played);
            Assert.Equal(3L, refreshed.PlaybackPositionTicks);
            Assert.Equal(4d, refreshed.PlayedPercentage);
            Assert.Equal(5, refreshed.UnplayedItemCount);
            order.Complete(favorite);
        });
    }

    private static void RegisterDetails(SettingsPageStateTests.ShellFixture shell)
    {
        foreach (var field in new[] { "watched", "favorite" })
            foreach (var value in new[] { true, false })
                TestHarness.Test($"R-024 真详情{field}目标{value}写成后503，确认位更新并单列刷新失败", () =>
                {
                    using var state = new State();
                    var item = state.Scope.Bind(new EmbyItem
                    {
                        Id = "item",
                        Name = "fixture",
                        Type = EmbyItemType.Movie,
                        UserData = new EmbyUserData
                        {
                            Played = field == "watched" && !value,
                            IsFavorite = field == "favorite" && !value,
                            PlaybackPositionTicks = 20_000,
                            PlayedPercentage = 20,
                            PlayCount = 2
                        }
                    });
                    var vm = PrepareDetail(shell, state, item, out var actions);
                    ((Task)shell.Invoke(vm, field == "watched" ? "ToggleWatchedAsync" : "ToggleFavoriteAsync")!).GetAwaiter().GetResult();
                    Assert.Equal(value, field == "watched" ? item.IsWatched : item.UserData!.IsFavorite);
                    Assert.Equal(value, shell.Get<bool>(vm, field == "watched" ? "Watched" : "Favorite"));
                    Assert.Equal(1, state.Transport.Writes);
                    if (field == "watched")
                        Assert.True(actions.Messages.Any(message => message.Contains("已保存", StringComparison.Ordinal)
                            && message.Contains("刷新", StringComparison.Ordinal)), "需要区分确认写与补读失败");
                    else
                    {
                        Assert.Equal(0, state.Transport.Reads, "详情收藏只改一位，原本不消费状态快照，无需补读");
                        Assert.Equal(0, actions.Messages.Count);
                        Assert.Equal(1, actions.FavoriteRefreshes);
                    }
                    if (field == "watched" && !value) Assert.Equal(0L, item.ResumeTicks);
                    // The page is not attached to a real window or artwork store; no Load path may run on read failure.
                    ((IDisposable)vm).Dispose();
                });
        TestHarness.Test("R-024 真详情写失败不乐观翻位，仍报告操作错误", () =>
        {
            using var state = new State();
            state.Transport.WriteFailure = "http500";
            var item = state.Scope.Bind(new EmbyItem { Id = "item", Name = "fixture", Type = EmbyItemType.Movie, UserData = new EmbyUserData() });
            var vm = PrepareDetail(shell, state, item, out var actions);
            ((Task)shell.Invoke(vm, "ToggleFavoriteAsync")!).GetAwaiter().GetResult();
            Assert.False(item.UserData!.IsFavorite);
            Assert.Equal(0, state.Transport.Reads);
            Assert.True(actions.Messages.Any(message => message.Contains("失败", StringComparison.Ordinal)));
            ((IDisposable)vm).Dispose();
        });
        foreach (var type in new[] { EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
            TestHarness.Test($"R-024 真详情{type}已看确认后503，只保留目标位不猜子项统计", () =>
            {
                using var state = new State();
                var item = state.Scope.Bind(new EmbyItem
                {
                    Id = "item",
                    Name = "fixture",
                    Type = type,
                    UserData = new EmbyUserData { Played = false, UnplayedItemCount = 7, PlayCount = 2 }
                });
                var vm = PrepareDetail(shell, state, item, out var actions);
                ((Task)shell.Invoke(vm, "ToggleWatchedAsync")!).GetAwaiter().GetResult();
                Assert.True(item.IsWatched);
                Assert.True(shell.Get<bool>(vm, "Watched"));
                Assert.Equal(7, item.UserData!.UnplayedItemCount);
                Assert.Equal(2, item.UserData.PlayCount);
                Assert.Equal(1, state.Transport.Writes);
                Assert.Equal(1, state.Transport.Reads);
                Assert.True(actions.Messages.Any(message => message.Contains("已保存", StringComparison.Ordinal)));
                ((IDisposable)vm).Dispose();
            });
        foreach (var failure in new[] { "http403", "permission", "signature", "bad-envelope" })
            TestHarness.Test($"R-024 真详情补读{failure}保留确认位且展示错误，不宽泛吞掉", () =>
            {
                using var state = new State();
                state.Transport.ReadFailure = failure;
                var item = state.Scope.Bind(new EmbyItem { Id = "item", Name = "fixture", Type = EmbyItemType.Movie, UserData = new EmbyUserData() });
                var vm = PrepareDetail(shell, state, item, out var actions);
                ((Task)shell.Invoke(vm, "ToggleWatchedAsync")!).GetAwaiter().GetResult();
                Assert.True(item.IsWatched);
                Assert.Equal(1, state.Transport.Writes);
                Assert.Equal(1, state.Transport.Reads);
                Assert.Equal("Error", shell.Get<object>(vm, "NoticeSeverity").ToString());
                Assert.True(actions.Messages.Any(message => message.Contains("已保存", StringComparison.Ordinal)));
                ((IDisposable)vm).Dispose();
            });
        TestHarness.Test("R-024 真详情已撤销来源不写入、不借默认来源补发", () =>
        {
            using var state = new State();
            var item = state.Scope.Bind(new EmbyItem { Id = "item", Name = "fixture", Type = EmbyItemType.Movie, UserData = new EmbyUserData() });
            var vm = PrepareDetail(shell, state, item, out _);
            state.Scope.IdentityState.Lifetime.Cancel();
            ((Task)shell.Invoke(vm, "ToggleWatchedAsync")!).GetAwaiter().GetResult();
            Assert.False(item.IsWatched);
            Assert.Equal(0, state.Transport.Writes);
            Assert.Equal(0, state.Transport.Reads);
            ((IDisposable)vm).Dispose();
        });
    }

    private static object PrepareDetail(SettingsPageStateTests.ShellFixture shell, State state, EmbyItem item, out FnNasStateActionsProxy actions)
    {
        var vm = shell.NewRow("DetailViewModel");
        var contract = vm.GetType().Assembly.GetType("Momoka.Shell.Views.IShellActions", throwOnError: true)!;
        actions = (FnNasStateActionsProxy)DispatchProxy.Create(contract, typeof(FnNasStateActionsProxy));
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var (name, value) in new (string, object)[]
        {
            ("_active", true), ("_settings", new FakeSettings()), ("_scope", state.Scope), ("_session", state.Session),
            ("_detail", item), ("_seed", item), ("_actions", actions)
        }) vm.GetType().GetField(name, fields)!.SetValue(vm, value);
        shell.Set(vm, "Watched", item.IsWatched);
        shell.Set(vm, "Favorite", item.UserData?.IsFavorite == true);
        return vm;
    }

    internal sealed class State : IDisposable
    {
        public StateTransport Transport { get; } = new();
        public EmbyHttp Http { get; }
        public EmbyClient Client { get; }
        public EmbySession Session { get; }
        public EmbySessionScope Scope { get; }
        public State()
        {
            var settings = new AppSettings();
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var connection = new EmbyConnection(new Uri("https://state-write.invalid/v/"), "placeholder-only", "fixture", "fixture", "fixture", device)
            { ServerKind = MediaServerKind.FnNas };
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "state-write-config|IO-forbidden"));
            Session = new EmbySession(settings, new SettingsStore(paths, new NoSecrets()), new CredentialVault(new NoSecrets()), device, Transport);
            Http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
            Client = new EmbyClient(Http, connection);
            var identity = new EmbySessionScope.Identity(Client, new ServerProfile { Id = "fixture", Url = "https://state-write.invalid/v/", ServerKind = MediaServerKind.FnNas },
                new AccountProfile { Id = "fixture", Username = "fixture" });
            Scope = new EmbySessionScope(Session, identity);
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
            sources.Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Scope);
        }
        public Task<EmbyUserData?> Write(string field, bool value, CancellationToken cancellationToken = default) => field == "watched"
            ? value ? Client.MarkPlayedAsync("item", cancellationToken) : Client.MarkUnplayedAsync("item", cancellationToken)
            : Client.SetFavoriteAsync("item", value, cancellationToken);
        public void Dispose() => Session.Dispose();
    }

    internal sealed class StateTransport : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public string? WriteFailure { get; set; }
        public string ReadFailure { get; set; } = "http503";
        public JsonObject? ReadItem { get; set; }
        public Action? AfterWrite { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Assert.Equal("state-write.invalid", uri.Host);
            if ((request.Method == HttpMethod.Post && uri.AbsolutePath == "/v/api/v1/item/watched")
                || (request.Method == HttpMethod.Put && uri.AbsolutePath == "/v/api/v1/item/favorite")
                || (request.Method == HttpMethod.Delete && uri.AbsolutePath is "/v/api/v1/item/watched" or "/v/api/v1/item/favorite"))
            {
                Writes++;
                AfterWrite?.Invoke();
                return Task.FromResult(WriteFailure switch
                {
                    "http500" => Reply(HttpStatusCode.InternalServerError),
                    "permission" => Envelope("null", code: -5),
                    "bad-envelope" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("broken") },
                    _ => Envelope("null")
                });
            }
            if (request.Method == HttpMethod.Get && uri.AbsolutePath == "/v/api/v1/item/item")
            {
                Reads++;
                return Task.FromResult(ReadItem is not null ? Envelope(ReadItem.ToJsonString()) : ReadFailure switch
                {
                    "permission" => Envelope("null", code: -5),
                    "signature" => Envelope("null", code: -3),
                    "http403" => Reply(HttpStatusCode.Forbidden),
                    "http401" => Reply(HttpStatusCode.Unauthorized),
                    "bad-envelope" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("broken") },
                    _ => Reply(HttpStatusCode.ServiceUnavailable)
                });
            }
            throw new AssertionException("未授权的假HTTP路径：" + request.Method + " " + uri.AbsolutePath);
        }
        private static HttpResponseMessage Reply(HttpStatusCode status) => new(status) { Content = new StringContent("") };
        private static HttpResponseMessage Envelope(string data, int code = 0) => new(HttpStatusCode.OK)
        { Content = new StringContent("{\"code\":" + code + ",\"data\":" + data + "}", Encoding.UTF8, "application/json") };
    }
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int PageSize => 20;
        public event EventHandler? SaveStateChanged { add { } remove { } }
        public bool HasUnsavedChanges => false;
        public bool TrySave() => throw new AssertionException("不允许配置IO");
        public void Save() => throw new AssertionException("不允许配置IO");
        public ServerProfile ResolveServer(string url) => throw new AssertionException("不允许登录路径");
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new AssertionException("不允许登录路径");
        public void ExportPreferences(string path) => throw new AssertionException("不允许配置IO");
        public AppSettings ReadBackup(string path) => throw new AssertionException("不允许配置IO");
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("不允许配置/凭据IO");
        public string Unprotect(string cipherText) => throw new AssertionException("不允许配置/凭据IO");
    }
}

public class FnNasStateActionsProxy : DispatchProxy
{
    public List<string> Messages { get; } = [];
    public int FavoriteRefreshes { get; private set; }
    public int WatchedReports { get; private set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "Notify": Messages.Add((string)args![0]!); return null;
            case "RefreshFavorites": FavoriteRefreshes++; return null;
            case "ReportMarkedWatched": WatchedReports++; return null;
            default: throw new AssertionException("不允许窗口/播放动作：" + targetMethod?.Name);
        }
    }
}
