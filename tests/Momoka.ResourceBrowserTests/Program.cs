using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is not [var sourceRoot] || !File.Exists(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/MoviePilotResourceBrowser.xaml")))
        {
            Console.Error.WriteLine("请提供当前本树根目录；本夹具不猜测其它树的模板或输出。");
            return 2;
        }
        sourceRoot = Path.GetFullPath(sourceRoot);
        var outputRoot = Path.Combine(sourceRoot, "tests", "Momoka.ResourceBrowserTests", "bin") + Path.DirectorySeparatorChar;
        if (!typeof(Program).Assembly.Location.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("模板根目录必须与本夹具实际编译输出属于同一工作树。");
            return 2;
        }
        Console.WriteLine("资源搜索真实源码/生成命令与模板契约；无窗口、配置、登录、网络或下载副作用");
        foreach (var meta in new[] { false, true })
        {
            Test("R-033 精确E05初筛、清除和重新选择 meta=" + meta, () =>
            {
                using var fixture = new ResourceFixture(meta: meta);
                var vm = fixture.Create(5);
                var rows = vm.Resources;
                var groups = vm.Browser.Groups;
                ResourceFixture.Complete(vm.SearchAsync());
                Assert.Equal("E05", vm.Browser.SelectedEpisode);
                Assert.Equal("Target.S01E05.1080p", vm.Resources.Single().Title);
                Assert.True(vm.Browser.Episodes.Contains("E05"));
                Assert.True(ReferenceEquals(rows, vm.Resources));
                Assert.True(ReferenceEquals(groups, vm.Browser.Groups));
                vm.Browser.ResetCommand.Execute(null);
                Assert.Equal(2, vm.Resources.Count);
                Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.Browser.SelectedEpisode);
                Assert.True(vm.Browser.Episodes.Contains("E05"), "清除后没有meta也要能选回目标集");
                vm.Browser.SelectedEpisode = "E05";
                Assert.Equal("Target.S01E05.1080p", vm.Resources.Single().Title);
                ResourceFixture.Complete(vm.SearchAsync());
                Assert.Equal("E05", vm.Browser.SelectedEpisode);
                Assert.Equal(1, vm.Resources.Count);
                Assert.Equal(0, fixture.Transport.Writes);
            });
        }
        foreach (var episode in new int?[] { null, 0, -1 })
        {
            Test("R-033 没有正目标集保留全集 episode=" + episode, () =>
            {
                using var fixture = new ResourceFixture();
                var vm = fixture.Create(episode);
                ResourceFixture.Complete(vm.SearchAsync());
                Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.Browser.SelectedEpisode);
                Assert.Equal(2, vm.Resources.Count);
                Assert.Equal(1, vm.Browser.Episodes.Count, "没有meta或正目标时不能伪造候选集号");
                Assert.Equal(0, fixture.Transport.Writes);
            });
        }
        Test("R-033 重新附加另一集替换seed但保留集合身份", () =>
        {
            using var fixture = new ResourceFixture();
            var vm = fixture.Create(5);
            ResourceFixture.Complete(vm.SearchAsync());
            var rows = vm.Resources;
            vm.Attach(fixture.Service, ResourceFixture.Media, fixture.Launcher, new(Episode: 3));
            ResourceFixture.Complete(vm.SearchAsync());
            Assert.Equal("E03", vm.Browser.SelectedEpisode);
            Assert.Equal("Target.S01E03.1080p", vm.Resources.Single().Title);
            Assert.True(ReferenceEquals(rows, vm.Resources));
            Assert.False(vm.Browser.Episodes.Contains("E05"));
        });
        foreach (var invalid in new[] { "other-source", "other-id", "other-type", "no-snapshot", "false-target", "non-exact" })
        {
            Test("R-034 匹配否决无下载入口且原原因可见 " + invalid, () =>
            {
                using var fixture = new ResourceFixture(invalid: invalid);
                var vm = fixture.Create(null);
                ResourceFixture.Complete(vm.SearchAsync());
                var row = vm.Resources.First();
                Assert.NotNull(row.Resource.DownloadProblem);
                Assert.Equal("Collapsed", Read(row, "DownloadVisibility")?.ToString());
                Assert.Equal("Visible", Read(row, "DownloadProblemVisibility")?.ToString());
                Assert.Equal(row.Resource.DownloadProblem, Read(row, "DownloadProblem") as string);
                Assert.False(row.CanDownload);
                Assert.False(row.DownloadCommand.CanExecute(null));
                var choose = 0;
                vm.UseDownloadPicker((_, _) => { choose++; return Task.FromResult<MoviePilotDownloadOptions?>(new("fixture", "/fixture")); });
                ResourceFixture.Complete(row.DownloadCommand.ExecuteAsync(null));
                Assert.Equal(0, choose, "直接旧命令也在真实VM入口拒绝，不启动确认");
                Assert.Equal(0, fixture.Transport.Writes);
                Assert.True(row.CanOpenDetails, "仍保留有效站点网页入口");
                row.OpenDetailsCommand.Execute(null);
                Assert.Equal(1, fixture.Launcher.Urls.Count);
                Assert.Throws<MoviePilotOperationBlockedException>(() => MoviePilotMediaParser.DownloadBody(row.Resource));
            });
        }
        foreach (var status in new[] { "missing", "empty", "exact" })
        {
            Test("R-034 缺标志或空status的可信资源仍可下载 " + status, () =>
            {
                using var fixture = new ResourceFixture(status: status);
                var vm = fixture.Create(null);
                ResourceFixture.Complete(vm.SearchAsync());
                var row = vm.Resources.First();
                Assert.Null(row.Resource.DownloadProblem);
                Assert.Equal("Visible", Read(row, "DownloadVisibility")?.ToString());
                Assert.Equal("Collapsed", Read(row, "DownloadProblemVisibility")?.ToString());
                Assert.True(row.CanDownload);
                Assert.True(row.DownloadCommand.CanExecute(null));
                Assert.True(MoviePilotMediaParser.DownloadBody(row.Resource).ContainsKey("media_in"));
                Assert.Equal(0, fixture.Transport.Writes, "观察可用性不是真的下载");
            });
        }
        foreach (var state in Enum.GetValues<MoviePilotDownloadState>())
        {
            Test("R-034 真实State变更通知且过程状态不隐藏 " + state, () =>
            {
                using var fixture = new ResourceFixture(invalid: "false-target");
                var vm = fixture.Create(null);
                ResourceFixture.Complete(vm.SearchAsync());
                var row = vm.Resources.First();
                var changed = new List<string>();
                ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");
                row.State = state;
                Assert.Equal(state == MoviePilotDownloadState.Idle ? "Collapsed" : "Visible", Read(row, "DownloadVisibility")?.ToString());
                if (state != MoviePilotDownloadState.Idle) Assert.True(changed.Contains("DownloadVisibility"));
                Assert.Equal("Visible", Read(row, "DownloadProblemVisibility")?.ToString());
                Assert.Equal(state switch
                {
                    MoviePilotDownloadState.Working => "添加中…",
                    MoviePilotDownloadState.Done => "已加入下载",
                    MoviePilotDownloadState.Uncertain => "请先核对",
                    MoviePilotDownloadState.Failed => "下载未完成",
                    _ => "身份待核对"
                }, row.DownloadLabel, "身份拒绝原因不能覆盖已经存在的操作过程状态");
                Assert.False(row.DownloadCommand.CanExecute(null));
                Assert.Equal(0, fixture.Transport.Writes);
            });
        }
        Test("R-034 模板将下载显隐和独立拒绝原因接到真实row", () =>
        {
            var xml = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/MoviePilotResourceBrowser.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var button = xml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "DownloadResource");
            Assert.Equal("{x:Bind DownloadVisibility, Mode=OneWay}", button.Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind DownloadCommand}", button.Attribute("Command")?.Value);
            var reason = xml.Descendants().Single(node => node.Attribute("Text")?.Value == "{x:Bind DownloadProblem, Mode=OneWay}");
            Assert.Equal("TextBlock", reason.Name.LocalName);
            Assert.Equal("{x:Bind DownloadProblemVisibility, Mode=OneWay}", reason.Attribute("Visibility")?.Value);
            Assert.Equal("Wrap", reason.Attribute("TextWrapping")?.Value);
            Assert.Equal("{StaticResource EgCaptionStyle}", reason.Attribute("Style")?.Value);
            Assert.NotNull(xml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == "OpenTorrentPage"));
        });
        return Run();
    }

    private static object? Read(object row, string property) => row.GetType().GetProperty(property)?.GetValue(row);
}

internal sealed class ResourceFixture : IDisposable
{
    public ResourceTransport Transport { get; }
    public MoviePilotService Service { get; }
    private readonly MoviePilotClient _client;
    public FixtureLauncher Launcher { get; } = new();
    public static MoviePilotMedia Media => new() { Title = "Target", Type = "电视剧", MediaSource = "themoviedb", MediaId = "42", Season = 1 };

    public ResourceFixture(bool meta = false, string invalid = "", string status = "exact")
    {
        Transport = new ResourceTransport(meta, invalid, status);
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "http://192.0.2.10:3001";
        settings.MoviePilot.Username = "anonymous-fixture";
        settings.MoviePilot.ProtectedPassword = "not-a-secret-fixture";
        _client = new MoviePilotClient(Transport);
        Service = new MoviePilotService(_client, new MoviePilotCredentials(new RejectSecretAccess()), settings);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(Service, null)!;
        var sessionType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var session = Activator.CreateInstance(sessionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
        typeof(MoviePilotService).GetField("_session", flags)!.SetValue(Service, session);
        typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(Service, identity);
    }

    public MoviePilotResourceViewModel Create(int? episode)
    {
        var vm = new MoviePilotResourceViewModel();
        vm.Attach(Service, Media, Launcher, new(Episode: episode));
        return vm;
    }

    public static void Complete(Task task)
    {
        if (!task.IsCompleted) throw new AssertionException("匿名内存任务没有同步完成，不能阻塞或重复运行洗绿");
        if (task.IsCanceled) throw new TaskCanceledException(task);
        if (task.Exception is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw();
    }

    public void Dispose() { Service.Dispose(); _client.Dispose(); Transport.Dispose(); }

    private sealed class RejectSecretAccess : ISecretProtector
    {
        public string Protect(string text) => throw new AssertionException("资源夹具不得保护凭据");
        public string Unprotect(string text) => throw new AssertionException("资源夹具不得解开凭据或登录");
    }
}

internal sealed class FixtureLauncher : ISystemLauncher
{
    public List<string> Urls { get; } = [];
    public void OpenUrl(string url) => Urls.Add(url);
    public void OpenFolder(string path) => throw new AssertionException("资源夹具不得启动文件夹");
}

internal sealed class ResourceTransport(bool meta, string invalid, string status) : HttpMessageHandler
{
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Method != HttpMethod.Get)
        {
            Writes++;
            throw new AssertionException("匿名资源夹具拒绝登录／订阅／下载写请求");
        }
        if (request.RequestUri?.AbsolutePath != "/api/v1/search/media/42")
        {
            Writes++;
            throw new AssertionException("匿名资源夹具拒绝未登记请求");
        }
        Reads++;
        var body = JsonSerializer.Serialize(new[] { Context(5), Context(3) });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private Dictionary<string, object?> Context(int episode)
    {
        var result = new Dictionary<string, object?>
        {
            ["torrent_info"] = new { title = $"Target.S01E{episode:00}.1080p", page_url = "https://example.invalid/torrent", site_name = "Fixture" }
        };
        if (invalid != "no-snapshot") result["media_info"] = new
        {
            title = "Target",
            type = invalid == "other-type" ? "电影" : "电视剧",
            media_source = invalid == "other-source" ? "douban" : "themoviedb",
            media_id = invalid == "other-id" ? "99" : "42"
        };
        if (meta) result["meta_info"] = new { begin_episode = episode, end_episode = episode, begin_season = 1, end_season = 1 };
        if (status != "missing") result["match_status"] = invalid == "non-exact" ? "fuzzy" : status == "empty" ? "" : "exact";
        if (status != "missing") result["media_info_is_target"] = invalid != "false-target";
        return result;
    }
}
