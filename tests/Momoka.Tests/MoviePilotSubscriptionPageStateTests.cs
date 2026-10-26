using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual current Shell subscription VM with anonymous in-memory reads only, not WinUI UI.</summary>
internal static class MoviePilotSubscriptionPageStateTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(string shellPath, string sourceRoot)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var root = Path.GetFullPath(sourceRoot);
            var shell = Path.GetFullPath(shellPath);
            if (!File.Exists(Path.Combine(root, "Momoka.sln")) || !File.Exists(shell)
                || !shell.StartsWith(Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(shell) != "Momoka.dll") throw new AssertionException("必须提供当前本树 x64 Release Shell DLL");
            var directory = Path.GetDirectoryName(shell)!;
            AssemblyLoadContext.Default.Resolving += Resolve;
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(shell);
                var type = assembly.GetType("Momoka.Shell.ViewModels.MoviePilotSubscriptionsViewModel", true)!;
                Console.WriteLine("订阅分类空态：真实Shell VM，禁止配置／凭据／网络／写入／窗口");
                Console.WriteLine("Shell=" + shell);
                foreach (var series in new[] { false, true })
                {
                    var key = series ? Momoka.Emby.HomeLayout.MoviePilotSeries : Momoka.Emby.HomeLayout.MoviePilotMovies;
                    Test("R-036 当前分类空而另一分类有数据 " + key, () =>
                    {
                        using var f = new Fixture(type, key, series ? "movie" : "series");
                        f.Read();
                        Assert.Equal("Visible", f.Get("Visible").ToString(), "共享主页Visible仍是任一分类有数据");
                        Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                        Assert.Equal("Collapsed", f.Get("LoadingVisibility").ToString());
                        Assert.False((bool)f.Get("Loading"));
                        Assert.Equal(0, f.Transport.Writes);
                    });
                    Test("R-036 本类空且另一类旧数据，实际刷新挂起显示读取中 " + key, () =>
                    {
                        using var f = new Fixture(type, key, series ? "movie" : "series");
                        f.Read();
                        f.Transport.Hold = true;
                        var read = f.StartRead();
                        Assert.False(read.IsCompleted);
                        Assert.True((bool)f.Get("Busy"));
                        Assert.True((bool)f.Get("Loading"));
                        Assert.Equal("Visible", f.Get("LoadingVisibility").ToString());
                        Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString());
                        f.Transport.Release();
                        read.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                        Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                        Assert.Equal("Collapsed", f.Get("LoadingVisibility").ToString());
                    });
                    Test("R-036 本类最后条目读取后移除会更新空态通知 " + key, () =>
                    {
                        using var f = new Fixture(type, key, "both");
                        f.Read();
                        Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString());
                        var notifications = new List<string>();
                        ((System.ComponentModel.INotifyPropertyChanged)f.Vm).PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? "");
                        f.Transport.Kind = series ? "movie" : "series";
                        f.Read();
                        Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                        Assert.True(notifications.Contains("EmptyStateVisibility"));
                        Assert.True(notifications.Contains("Loading"));
                        Assert.Equal(0, f.Transport.Writes, "仅模拟确认写后服务器读取列表，不执行真实删除");
                    });
                    Test("R-036 本类错误保留提示且可刷新恢复 " + key, () =>
                    {
                        using var f = new Fixture(type, key, series ? "movie" : "series");
                        f.Read();
                        f.Transport.Fail = true;
                        f.Read();
                        Assert.True((bool)f.Get("NoticeOpen"));
                        Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString(), "业务错误由既有提示/刷新排显示，不与成功空态混同");
                        Assert.Equal("Visible", f.Get("Visible").ToString());
                        f.Transport.Fail = false;
                        f.Read();
                        Assert.False((bool)f.Get("NoticeOpen"));
                        Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                    });
                }
                Test("R-036 同VM分类切换仅改页面投影且立即通知", () =>
                {
                    using var f = new Fixture(type, Momoka.Emby.HomeLayout.MoviePilotMovies, "series");
                    f.Read();
                    var movies = f.Get("Movies");
                    var seriesRows = f.Get("Series");
                    var changes = new List<string>();
                    ((System.ComponentModel.INotifyPropertyChanged)f.Vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? "");
                    f.SetCategory(Momoka.Emby.HomeLayout.MoviePilotSeries);
                    Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString());
                    Assert.True(changes.Contains("EmptyStateVisibility"));
                    Assert.True(ReferenceEquals(movies, f.Get("Movies")));
                    Assert.True(ReferenceEquals(seriesRows, f.Get("Series")));
                    f.SetCategory(Momoka.Emby.HomeLayout.MoviePilotMovies);
                    Assert.Equal("Visible", f.Get("EmptyStateVisibility").ToString());
                    f.SetCategory("unknown-page");
                    Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString(), "未知分类退回主页语义，不误删数据");
                    Assert.Equal(0, f.Transport.Writes);
                });
                Test("R-036 主页共享VM保留任一分类语义", () =>
                {
                    using var f = new Fixture(type, null, "series");
                    f.Read();
                    Assert.Equal("Visible", f.Get("Visible").ToString());
                    Assert.Equal("Collapsed", f.Get("EmptyStateVisibility").ToString());
                    f.Transport.Hold = true;
                    var read = f.StartRead();
                    Assert.False((bool)f.Get("Loading"), "共享主页有任一内容时由行顶部细进度显示刷新");
                    f.Transport.Release();
                    read.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                });
                Test("R-036 当前源码页在AttachRow之前设置分类且保留原刷新接线", () =>
                {
                    var page = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionsPage.xaml.cs"));
                    var scope = page.IndexOf("UsePageCategory(request.Key)", StringComparison.Ordinal);
                    Assert.True(scope > page.IndexOf("_owner = new MoviePilotHomeSubscriptions()", StringComparison.Ordinal));
                    Assert.True(scope < page.IndexOf("Subscriptions.AttachRow", StringComparison.Ordinal));
                    Assert.Contains("private void OnRefresh(object sender, RoutedEventArgs e) => _ = ViewModel.ReloadAsync();", page);
                    var xaml = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionsPage.xaml"));
                    Assert.Contains("Visibility=\"{x:Bind ViewModel.EmptyStateVisibility, Mode=OneWay}\"", xaml);
                    Assert.Contains("x:Name=\"RefreshSubscriptions\"", xaml);
                    Assert.Contains("Click=\"OnRefresh\"", xaml);
                });
                return TestHarness.Run();
            }
            finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
            Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Type _type;
        private readonly MoviePilotClient _client;
        private readonly MoviePilotService _service;
        public SubscriptionTransport Transport { get; } = new();
        public object Vm { get; }
        public Fixture(Type type, string? key, string kind)
        {
            _type = type;
            Transport.Kind = kind;
            var settings = new AppSettings();
            settings.MoviePilot.Enabled = true;
            settings.MoviePilot.Url = "http://192.0.2.10:3001";
            settings.MoviePilot.Username = "anonymous-fixture";
            settings.MoviePilot.ProtectedPassword = "not-a-secret-fixture";
            _client = new MoviePilotClient(Transport);
            _service = new MoviePilotService(_client, new MoviePilotCredentials(new NoSecrets()), settings);
            var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", Members)!.Invoke(_service, null)!;
            var cache = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
            var session = Activator.CreateInstance(cache, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
            typeof(MoviePilotService).GetField("_session", Members)!.SetValue(_service, session);
            typeof(MoviePilotService).GetField("_availableIdentity", Members)!.SetValue(_service, identity);
            Vm = Activator.CreateInstance(type)!;
            if (key is not null) _type.GetMethod("UsePageCategory", Members)?.Invoke(Vm, [key]); // Old code lacks the page-only rule.
            _type.GetMethod("Attach", Members)!.Invoke(Vm, [_service, null]);
        }
        public void SetCategory(string key) => _type.GetMethod("UsePageCategory", Members)!.Invoke(Vm, [key]);
        public object Get(string property) => _type.GetProperty(property, Members)!.GetValue(Vm)!;
        public Task StartRead() => (Task)_type.GetMethod("ReloadAsync", Members)!.Invoke(Vm, null)!;
        public void Read() => StartRead().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        public void Dispose()
        {
            _type.GetMethod("Cancel", Members)!.Invoke(Vm, null);
            ((IDisposable)Vm).Dispose();
            _service.Dispose(); _client.Dispose(); Transport.Dispose();
        }
        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string text) => throw new AssertionException("夹具不得保护凭据");
            public string Unprotect(string text) => throw new AssertionException("夹具不得解开凭据或登录");
        }
    }

    private sealed class SubscriptionTransport : HttpMessageHandler
    {
        public string Kind { get; set; } = "series";
        public bool Fail { get; set; }
        public bool Hold { get; set; }
        public int Writes { get; private set; }
        private readonly TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri?.AbsolutePath != "/api/v1/subscribe/")
            {
                Writes++;
                throw new AssertionException("订阅夹具仅允许匿名列表读取，不登录／写入／取图");
            }
            if (Hold) await _pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var body = Fail ? "{\"success\":false,\"message\":\"anonymous fixture error\"}" : JsonSerializer.Serialize(
                (Kind == "both" ? new[] { "movie", "series" } : new[] { Kind }).Select((kind, index) => new
                { id = index + 1, name = "Fixture", type = kind == "series" ? "电视剧" : "电影", media_source = "themoviedb", media_id = "42", state = "N" }));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
        public void Release() => _pending.TrySetResult();
    }
}
