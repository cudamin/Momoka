using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];
    private static void Test(string name, Func<Task> run) => Cases.Add((name, run));
    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("R-062 实际Shell目录读/导航控制流＋SourceLibraries；匿名HTTP/无窗口或真实登录");
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test("R-062 目录挂起先到主页，再交接合法目录快照 " + kind, async () =>
            {
                using var f = new Fixture(kind);
                var entering = f.Shell.Enter();
                await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                f.Shell.GoTo("home");
                Assert.Equal(0, f.Shell.HomeSnapshot.Count);
                var stale = f.Shell.ContentFrame.Content;
                f.Transport.Release();
                await entering.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(2, f.Shell.Directory.Count, "音乐库继续被真实Shell过滤");
                Assert.Equal(2, f.Shell.HomeSnapshot.Count);
                Assert.False(ReferenceEquals(stale, f.Shell.ContentFrame.Content));
                Assert.Equal(2, f.Shell.ContentFrame.Navigations);
                CheckSnapshot(f);
            });
            Test("R-062 未提前选页，目录完成照常进入主页 " + kind, async () =>
            {
                using var f = new Fixture(kind);
                var entering = f.Shell.Enter();
                await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                f.Transport.Release();
                await entering.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(1, f.Shell.ContentFrame.Navigations);
                CheckSnapshot(f);
            });
            Test("R-062 已转去其它页，合法目录只更新侧栏不抢导航 " + kind, async () =>
            {
                using var f = new Fixture(kind);
                var entering = f.Shell.Enter();
                await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                f.Shell.GoTo("home");
                f.Shell.LeaveForOtherPage();
                var other = f.Shell.ContentFrame.Content;
                f.Transport.Release();
                await entering.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(ReferenceEquals(other, f.Shell.ContentFrame.Content));
                Assert.Equal(1, f.Shell.ContentFrame.Navigations);
                Assert.Equal(2, f.Shell.Directory.Count);
            });
            Test("R-062 目录await期间撤销旧访问，不交接到现有主页 " + kind, async () =>
            {
                using var f = new Fixture(kind);
                var entering = f.Shell.Enter();
                await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                f.Shell.GoTo("home");
                var current = f.Shell.ContentFrame.Content;
                f.Shell.InvalidateVisit();
                f.Transport.Release();
                await entering.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(ReferenceEquals(current, f.Shell.ContentFrame.Content));
                Assert.Equal(0, f.Shell.Directory.Count);
                Assert.Equal(0, f.Shell.HomeSnapshot.Count);
            });
            Test("R-062 目录await期间来源撤销拒绝旧列表/导航 " + kind, async () =>
            {
                using var f = new Fixture(kind);
                var entering = f.Shell.Enter();
                await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                f.Shell.GoTo("home");
                var current = f.Shell.ContentFrame.Content;
                f.Revoke();
                f.Transport.Release();
                await entering.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(ReferenceEquals(current, f.Shell.ContentFrame.Content));
                Assert.Equal(0, f.Shell.Directory.Count);
            });
        }
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex })
        {
            Test("R-062 聚合" + kind + "默认来源保留恢复目录，另一来源等待后交当前主页", async () =>
            {
                using var f = new Fixture(kind, second: true, restored: true);
                f.Transport.FailSecond = true;
                var entering = f.Shell.Enter();
                await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                f.Shell.GoTo("home");
                Assert.Equal(0, f.Shell.HomeSnapshot.Count);
                f.Transport.Release();
                await entering.WaitAsync(TimeSpan.FromSeconds(10));
                CheckSnapshot(f);
                Assert.True(f.Shell.HomeSnapshot.All(item => item.Source!.Connection.ServerKind == kind));
                Assert.Equal(2, f.Shell.ContentFrame.Navigations);
            });
        }
        Test("R-062 空目录成功仍正确完成主页而不伪造库", async () =>
        {
            using var f = new Fixture(MediaServerKind.Emby);
            f.Transport.Empty = true;
            var entering = f.Shell.Enter();
            await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            f.Shell.GoTo("home");
            f.Transport.Release();
            await entering.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, f.Shell.HomeSnapshot.Count);
            Assert.Equal(2, f.Shell.ContentFrame.Navigations);
        });
        Test("R-062 部分来源失败，当前主页仍接可用目录并保留恢复提示", async () =>
        {
            using var f = new Fixture(MediaServerKind.Emby, second: true);
            f.Transport.FailSecond = true;
            var entering = f.Shell.Enter();
            await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            f.Shell.GoTo("home");
            f.Transport.Release();
            await entering.WaitAsync(TimeSpan.FromSeconds(10));
            CheckSnapshot(f);
            Assert.Equal(1, f.Shell.Notices.Count);
            Assert.Contains("部分来源", f.Shell.Notices.Single());
        });
        Test("R-062 先到主页再返回主页，最终目录只交接当前实例", async () =>
        {
            using var f = new Fixture(MediaServerKind.Emby);
            var entering = f.Shell.Enter();
            await f.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            f.Shell.GoTo("home");
            f.Shell.LeaveForOtherPage();
            f.Shell.GoTo("home");
            f.Transport.Release();
            await entering.WaitAsync(TimeSpan.FromSeconds(10));
            CheckSnapshot(f);
            Assert.Equal(3, f.Shell.ContentFrame.Navigations);
        });
        var failures = 0;
        foreach (var test in Cases)
        {
            try { await test.Run(); Console.WriteLine("  通过  " + test.Name); }
            catch (Exception error) { failures++; Console.WriteLine("  失败  " + test.Name + "\n" + error); }
        }
        Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failures}，失败 {failures}，跳过 0");
        return failures == 0 ? 0 : 1;
    }

    private static void CheckSnapshot(Fixture f)
    {
        Assert.Equal("movies,shows", string.Join(',', f.Shell.HomeSnapshot.Select(item => item.Id)));
        Assert.True(f.Shell.HomeSnapshot.All(item => item.Source is { IsAvailable: true }));
        var plan = HomeLayout.Plan(null, f.Shell.HomeSnapshot);
        Assert.True(plan.Any(row => row.Key == HomeLayout.LibraryKey(f.Shell.HomeSnapshot[0])));
        Assert.True(plan.Any(row => row.Key == HomeLayout.LibraryKey(f.Shell.HomeSnapshot[1])));
        Assert.Equal(0, f.Transport.Writes);
    }

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly EmbySession _session;
        private readonly List<object> _identities = [];
        private readonly List<CancellationTokenSource> _lifetimes = [];
        public DirectoryTransport Transport { get; } = new();
        public ShellPage Shell { get; }
        public Fixture(MediaServerKind kind, bool second = false, bool restored = false)
        {
            var settings = new AppSettings();
            var secrets = new NoSecrets();
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Fields, null,
                [settings, new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "r062|no-IO")), secrets), new CredentialVault(secrets), device, Transport], null)!;
            var scope = Add("directory", kind, device);
            if (restored)
            {
                _identities[0].GetType().GetProperty("RestoredViews")!.SetValue(_identities[0], new List<EmbyItem>
                {
                    scope.Bind(new() { Id = "movies", Name = "Movies", Type = "CollectionFolder", CollectionType = "movies" }),
                    scope.Bind(new() { Id = "shows", Name = "Shows", Type = "CollectionFolder", CollectionType = "tvshows" })
                });
            }
            typeof(EmbySession).GetField("_current", Fields)!.SetValue(_session, scope);
            if (second) Add("failed", MediaServerKind.Emby, device);
            Shell = new ShellPage(_session, settings);
        }
        private EmbySessionScope Add(string name, MediaServerKind kind, DeviceIdentity device)
        {
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Fields)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri("https://" + name + ".invalid/" + (kind == MediaServerKind.Emby ? "emby/" : "")),
                "placeholder-only", "fixture-user", "fixture-user", name, device)
            { ServerKind = kind };
            var type = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
            var identity = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [new EmbyClient(http, connection), new ServerProfile { Id = name, ServerKind = kind }, new AccountProfile { Id = name + "-account" }], null)!;
            var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Fields, null, [_session, identity], null)!;
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Fields)!.GetValue(_session)!).Add(scope.Key, scope);
            _identities.Add(identity); _lifetimes.Add((CancellationTokenSource)type.GetProperty("Lifetime")!.GetValue(identity)!);
            return scope;
        }
        public void Revoke() => _lifetimes[0].Cancel();
        public void Dispose()
        {
            _session.Dispose();
            foreach (var identity in _identities)
            {
                ((IDisposable)identity.GetType().GetProperty("Reauthentication")!.GetValue(identity)!).Dispose();
                ((IDisposable)identity.GetType().GetProperty("PasswordChanges")!.GetValue(identity)!).Dispose();
            }
            Transport.Dispose();
        }
        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string text) => throw new AssertionException("目录夹具不得保护凭据");
            public string Unprotect(string text) => throw new AssertionException("目录夹具不得解密或登录");
        }
    }

    private sealed class DirectoryTransport : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Writes { get; private set; }
        public bool Empty { get; set; }
        public bool FailSecond { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri is not { } url || !(url.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal) || url.AbsolutePath == "/UserViews"))
            {
                Writes++;
                throw new AssertionException("目录夹具拒绝其它HTTP／登录／播放");
            }
            Entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (FailSecond && url.Host == "failed.invalid") return new(HttpStatusCode.InternalServerError);
            var rows = Empty ? Array.Empty<object>() : new object[]
            {
                new { Id = "movies", Name = "Movies", Type = "CollectionFolder", CollectionType = "movies" },
                new { Id = "shows", Name = "Shows", Type = "CollectionFolder", CollectionType = "tvshows" },
                new { Id = "music", Name = "Music", Type = "CollectionFolder", CollectionType = "music" }
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { Items = rows }), Encoding.UTF8, "application/json") };
        }
        public void Release() => _release.TrySetResult();
    }
}
