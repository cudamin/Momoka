using System.Collections;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;

namespace Momoka.MoviePilotJourneyTests;

internal static class Program
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length != 2) return Usage();
        var shellPath = Path.GetFullPath(args[0]);
        var root = Path.GetFullPath(args[1]);
        var shellOutput = Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar;
        var testOutput = Path.Combine(root, "tests", "Momoka.MoviePilotJourneyTests", "bin") + Path.DirectorySeparatorChar;
        if (!File.Exists(shellPath) || Path.GetFileName(shellPath) != "Momoka.dll"
            || !shellPath.StartsWith(shellOutput, StringComparison.OrdinalIgnoreCase)
            || !typeof(Program).Assembly.Location.StartsWith(testOutput, StringComparison.OrdinalIgnoreCase)) return Usage();
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(shellPath);
            var type = assembly.GetType("Momoka.Shell.ShellNavigationProbe+MoviePilotJourneyServer", true)!;
            var tests = new List<(string Name, Func<Task> Run)>
            {
                ("搜索返回稳定的影视身份与图片路径", async () =>
                {
                    using var fixture = new Fixture(type);
                    using var json = await fixture.JsonAsync(HttpMethod.Get, "media/search?title=漫长的旅途");
                    Equal(2, json.RootElement.GetArrayLength());
                    Equal("4", json.RootElement[0].GetProperty("media_id").GetString());
                    Equal("电视剧", json.RootElement[0].GetProperty("type").GetString());
                    Equal(0, fixture.WriteCount);
                }),
                ("目录保存路径与下载接口路径保持可区分", async () =>
                {
                    using var fixture = new Fixture(type);
                    using var json = await fixture.JsonAsync(HttpMethod.Get, "download/paths");
                    Equal("/downloads/series", json.RootElement[0].GetProperty("download_path").GetString());
                    Equal("local:/downloads/series", json.RootElement[0].GetProperty("save_path").GetString());
                    Equal(3, json.RootElement.GetArrayLength());
                    using var rules = await fixture.JsonAsync(HttpMethod.Get, "rule/groups?include_usage=false");
                    Equal("剧集优先", rules.RootElement.GetProperty("rule_groups")[0].GetProperty("name").GetString());
                }),
                ("站点同时提供启用与停用样本", async () =>
                {
                    using var fixture = new Fixture(type);
                    using var json = await fixture.JsonAsync(HttpMethod.Get, "site/rss");
                    Equal(2, json.RootElement.EnumerateArray().Count(site => site.GetProperty("is_active").GetBoolean()));
                    Equal(3, json.RootElement.GetArrayLength());
                }),
                ("图片读取只返回注入的匿名字节", async () =>
                {
                    using var fixture = new Fixture(type);
                    fixture.Set("Poster", new byte[] { 2, 4, 6 });
                    using var response = await fixture.SendAsync(HttpMethod.Get, "system/cache/image?url=https%3A%2F%2Fposter.invalid%2F4.png");
                    Check((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 2, 4, 6 }));
                }),
                ("匿名登录与状态读取可以驱动正式连接服务", async () =>
                {
                    using var fixture = new Fixture(type);
                    using var login = await fixture.JsonAsync(HttpMethod.Post, "login/access-token", "username=fixture&password=fixture");
                    Equal("fixture-token", login.RootElement.GetProperty("access_token").GetString());
                    using var system = await fixture.JsonAsync(HttpMethod.Get, "dashboard/system");
                    Equal("fixture", system.RootElement.GetProperty("version").GetString());
                    Equal(0, fixture.WriteCount);
                }),
                ("第一季已订阅而第二季可新增，随后可更新和取消", async () =>
                {
                    using var fixture = new Fixture(type);
                    using var before = await fixture.JsonAsync(HttpMethod.Get, "subscribe/");
                    Equal(4, before.RootElement.GetProperty("data").GetArrayLength());
                    using var created = await fixture.JsonAsync(HttpMethod.Post, "subscribe/", Subscription);
                    var id = created.RootElement.GetProperty("data").GetProperty("id").GetInt32();
                    using var read = await fixture.JsonAsync(HttpMethod.Get, "subscribe/" + id);
                    Equal(2, read.RootElement.GetProperty("data").GetProperty("season").GetInt32());
                    using var updated = await fixture.JsonAsync(HttpMethod.Put, "subscribe/", $$"""{"id":{{id}},"keyword":"changed","search_interval":null}""");
                    using var saved = await fixture.JsonAsync(HttpMethod.Get, "subscribe/" + id);
                    Equal("changed", saved.RootElement.GetProperty("data").GetProperty("keyword").GetString());
                    Equal(JsonValueKind.Null, saved.RootElement.GetProperty("data").GetProperty("search_interval").ValueKind);
                    using var removed = await fixture.JsonAsync(HttpMethod.Delete, "subscribe/" + id);
                    using var missing = await fixture.SendAsync(HttpMethod.Get, "subscribe/" + id);
                    Equal(HttpStatusCode.NotFound, missing.StatusCode);
                    Equal(3, fixture.WriteCount);
                }),
                ("业务拒绝不修改订阅且允许下一次明确提交", async () =>
                {
                    using var fixture = new Fixture(type);
                    fixture.Set("FailNextCreate", true);
                    using var failed = await fixture.JsonAsync(HttpMethod.Post, "subscribe/", Subscription);
                    Check(!failed.RootElement.GetProperty("success").GetBoolean());
                    Equal(0, fixture.WriteCount);
                    using var before = await fixture.JsonAsync(HttpMethod.Get, "subscribe/");
                    Equal(4, before.RootElement.GetProperty("data").GetArrayLength());
                    using var retry = await fixture.JsonAsync(HttpMethod.Post, "subscribe/", Subscription);
                    Check(retry.RootElement.GetProperty("success").GetBoolean());
                    Equal(1, fixture.WriteCount);
                }),
                ("取消令牌在路由与写入前生效", async () =>
                {
                    using var fixture = new Fixture(type);
                    using var cancellation = new CancellationTokenSource();
                    cancellation.Cancel();
                    await ThrowsAsync<OperationCanceledException>(() => fixture.SendAsync(HttpMethod.Post, "subscribe/", Subscription, cancellation.Token));
                    Equal(0, fixture.WriteCount);
                    Equal(0, fixture.RequestCount);
                }),
                ("只读入库 POST 不记作订阅写入", async () =>
                {
                    using var fixture = new Fixture(type);
                    using var json = await fixture.JsonAsync(HttpMethod.Post, "mediaserver/exists_remote", "{}");
                    Equal(1, json.RootElement.GetProperty("data").GetProperty("1")[0].GetInt32());
                    Equal(0, fixture.WriteCount);
                }),
                ("正式服务完成登录搜索详情季选和第二季订阅回读", async () =>
                {
                    using var fixture = new Fixture(type);
                    var settings = new AppSettings();
                    settings.MoviePilot.Enabled = true;
                    settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
                    settings.MoviePilot.Username = "fixture";
                    var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
                    credentials.SetPassword(settings.MoviePilot, "fixture");
                    using var client = new MoviePilotClient(fixture.Handler);
                    using var service = new MoviePilotService(client, credentials, settings);
                    Check(await service.RefreshAvailabilityAsync());
                    var found = await service.SearchAsync("漫长的旅途", CancellationToken.None);
                    var media = found.First(item => item.Type == "电视剧");
                    var detail = await service.MediaDetailAsync(media, CancellationToken.None);
                    var seasons = await service.MediaSeasonsAsync(detail, "", CancellationToken.None);
                    Equal(2, seasons.Count);
                    var groups = await service.MediaGroupsAsync(detail, CancellationToken.None);
                    Equal(3, groups.Count); Check(groups[0].IsDefault);
                    Equal(MoviePilotSubscribeMode.Normal, await service.DefaultTvSubscribeModeAsync(media, CancellationToken.None));
                    Check((await service.MediaExistingEpisodesAsync(detail, "", CancellationToken.None)).ContainsKey(1));
                    await service.ApplySubscriptionChangeAsync(new MoviePilotSubscribeChange(MoviePilotSubscribeChangeKind.Add,
                        media with { Season = 2 }, "", MoviePilotSubscribeMode.Normal, null), CancellationToken.None);
                    var saved = await service.SubscriptionsAsync(CancellationToken.None);
                    Check(saved.Any(item => item.Media.MediaId == "4" && item.Media.Season == 2));
                    Equal(1, fixture.WriteCount);
                })
            };
            foreach (var target in new[] { "https://example.com/api/v1/subscribe/", "http://moviepilot.invalid:3001/api/v1/subscribe/",
                "https://moviepilot.invalid:3002/api/v1/subscribe/", "https://moviepilot.invalid:3001/unregistered" })
            {
                tests.Add(("拒绝其它目标 " + target, async () =>
                {
                    using var fixture = new Fixture(type);
                    await ThrowsAsync<InvalidOperationException>(() => fixture.SendAsync(HttpMethod.Post, target, Subscription));
                    Equal(0, fixture.WriteCount);
                    Equal(1, fixture.Get<int>("Rejected"));
                }
                ));
            }
            foreach (var path in new[] { "download/", "transfer/manual", "subscribe/search/4", "settings/", "unknown/" })
            {
                tests.Add(("拒绝未登记写入 " + path, async () =>
                {
                    using var fixture = new Fixture(type);
                    await ThrowsAsync<InvalidOperationException>(() => fixture.SendAsync(HttpMethod.Post, path, "{}"));
                    Equal(0, fixture.WriteCount);
                }
                ));
            }
            tests.Add(("拒绝未知读取而不落入宽松订阅兜底", async () =>
            {
                using var fixture = new Fixture(type);
                await ThrowsAsync<InvalidOperationException>(() => fixture.SendAsync(HttpMethod.Get, "download/private"));
                Equal(0, fixture.WriteCount);
            }
            ));
            tests.Add(("检查入口使用真路由代理并保持播放隔离", () =>
            {
                var source = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "ShellNavigationProbe.MoviePilotJourney.cs"));
                var inspect = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "ShellNavigationProbe.Inspect.cs"));
                Check(source.Contains("shell.Attach(new MoviePilotJourneyServices", StringComparison.Ordinal));
                Check(source.Contains("Navigation.OpenMoviePilotMedia(media, subscription)", StringComparison.Ordinal));
                Check(source.Contains("detail.ViewModel.AttachPlayback(null", StringComparison.Ordinal));
                Check(!source.Contains("AttachWindow(", StringComparison.Ordinal));
                Check(inspect.Contains("await InspectMoviePilotJourneyAsync(frame)", StringComparison.Ordinal));
                Check(inspect.Contains("moviePilotJourney?.Dispose()", StringComparison.Ordinal));
                return Task.CompletedTask;
            }
            ));
            var failedCount = 0;
            foreach (var test in tests)
            {
                try { await test.Run(); Console.WriteLine("通过 " + test.Name); }
                catch (Exception error) { failedCount++; Console.WriteLine("失败 " + test.Name + "\n" + error); }
            }
            Console.WriteLine($"共 {tests.Count} 项，通过 {tests.Count - failedCount}，失败 {failedCount}，跳过 0。仅验证真实检查台传输和源码接线，无窗口、网络或真实账户。");
            return failedCount == 0 ? 0 : 1;
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }

        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            var candidate = Path.Combine(Path.GetDirectoryName(shellPath)!, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        }
    }

    private const string Subscription = """{"name":"漫长的旅途","type":"电视剧","year":"2026","media_source":"themoviedb","media_id":"4","season":2,"best_version":0,"best_version_full":0}""";
    private static int Usage() { Console.Error.WriteLine("请提供当前本树 Release Momoka.dll 绝对路径与本树根目录。"); return 2; }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("断言未满足"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"期望 {expected}，实际 {actual}"); }
    private static async Task ThrowsAsync<T>(Func<Task<HttpResponseMessage>> action) where T : Exception
    {
        try { using var response = await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("未抛出 " + typeof(T).Name);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly object _server;
        private readonly HttpMessageInvoker _client;
        internal Fixture(Type type)
        {
            _server = Activator.CreateInstance(type, Members, null, [], null)!;
            _client = new HttpMessageInvoker((HttpMessageHandler)_server);
        }
        internal T Get<T>(string name) => (T)_server.GetType().GetProperty(name, Members)!.GetValue(_server)!;
        internal HttpMessageHandler Handler => (HttpMessageHandler)_server;
        internal void Set(string name, object value) => _server.GetType().GetProperty(name, Members)!.SetValue(_server, value);
        internal int RequestCount => Get<ICollection>("Requests").Count;
        internal int WriteCount
        {
            get
            {
                var subscriptions = Get<object>("Subscriptions");
                return ((ICollection)subscriptions.GetType().GetProperty("Writes", Members)!.GetValue(subscriptions)!).Count;
            }
        }
        internal async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null, CancellationToken token = default)
        {
            var uri = path.StartsWith("http", StringComparison.Ordinal) ? path : "https://moviepilot.invalid:3001/api/v1/" + path;
            using var request = new HttpRequestMessage(method, uri);
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await _client.SendAsync(request, token);
        }
        internal async Task<JsonDocument> JsonAsync(HttpMethod method, string path, string? body = null)
        {
            using var response = await SendAsync(method, path, body);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        public void Dispose() => _client.Dispose();
    }
}
