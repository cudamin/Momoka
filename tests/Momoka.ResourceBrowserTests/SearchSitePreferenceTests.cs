using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SearchSitePreferenceTests
{
    internal static void Register()
    {
        Test("搜索站点：新页面自动恢复选项并在首次搜索前读取目录", () =>
        {
            using var fixture = new SiteFixture();
            var picker = fixture.Picker();
            ResourceFixture.Complete(picker.EnsureLoadedAsync());
            picker.UseDefault = false;
            picker.Sites.Single(site => site.Id == 2).Selected = true;
            Assert.Equal(2, fixture.Saves);
            fixture.Transport.Requests.Clear();
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            Assert.False(vm.SearchSites.UseDefault);
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            Assert.Equal(2, fixture.Transport.Requests.Count);
            Assert.True(fixture.Transport.Requests[0].Contains("/site/"));
            Assert.True(fixture.Transport.Requests[1].Contains("sites=2"));
            Assert.False(vm.Busy);
            Assert.True(vm.IsReady);
            Assert.Equal(2, fixture.Saves, "恢复目录不应写盘或取消发起搜索");
        });

        Test("搜索站点：使用默认站点省略sites且不强制读取目录", () =>
        {
            using var fixture = new SiteFixture();
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            Assert.Equal(1, fixture.Transport.Requests.Count);
            Assert.False(fixture.Transport.Requests[0].Contains("sites="));
        });

        Test("搜索站点：未配置服务Attach仍保留默认且不读取网络", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Settings.MoviePilot.Url = "";
            var picker = fixture.Picker();
            Assert.True(picker.UseDefault);
            Assert.Equal(0, fixture.Transport.Requests.Count);
            Assert.Equal(0, fixture.Saves);
        });

        Test("搜索站点：更改站点不清空关键词结果，下一次搜索才应用", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Transport.SearchResults = "[" + Result("旧结果", "站点甲") + "," + Result("旧结果二", "站点乙") + "]";
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            Assert.Equal(2, vm.Versions.Count);

            vm.SearchSites.UseDefault = false;
            Assert.Equal(2, vm.Versions.Count);
            Assert.True(vm.NoticeOpen && vm.NoticeTitle == "搜索站点已更改" && vm.NoticeMessage?.Contains("重搜") == true,
                "取消勾选默认站点没有说明要重新搜索才生效");
            ResourceFixture.Complete(vm.SearchSites.EnsureLoadedAsync());
            vm.SearchSites.Sites[0].Selected = true;
            Assert.Equal(2, vm.Versions.Count);
            Assert.False(vm.ShowEmptyNotice);

            ResourceFixture.Complete(vm.SearchAsync("Target"));
            Assert.Equal(2, vm.Versions.Count);
            Assert.True(fixture.Transport.Requests.Last().Contains("sites=1", StringComparison.Ordinal));
            Assert.False(vm.Busy);
        });

        Test("搜索站点：离页取消在飞搜索后，重新进入仍可下载留下的卡片", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Transport.SearchResults = "[" + Result("留下的资源", "站点甲") + "]";
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            var row = vm.Versions.Single();
            var chosen = 0;
            vm.UseDownloadPicker((_, _) => { chosen++; return Task.FromResult<MoviePilotDownloadOptions?>(null); });

            vm.Cancel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(row.DownloadCommand.ExecuteAsync(null));
            Assert.Equal(1, chosen);
        });

        Test("搜索站点：默认与手动切换保留已勾选编号", () =>
        {
            using var fixture = new SiteFixture();
            var picker = fixture.Picker();
            ResourceFixture.Complete(picker.EnsureLoadedAsync());
            picker.UseDefault = false;
            picker.Sites[1].Selected = true;
            picker.UseDefault = true;
            var restored = fixture.Picker();
            Assert.True(restored.UseDefault);
            ResourceFixture.Complete(restored.EnsureLoadedAsync());
            Assert.True(restored.Sites[1].Selected);
            restored.UseDefault = false;
            Assert.Equal("2", restored.Selection!.QueryValue());
        });

        Test("搜索站点：清空持久化为显式空选择而非默认站点", () =>
        {
            using var fixture = new SiteFixture();
            var picker = fixture.Picker();
            ResourceFixture.Complete(picker.EnsureLoadedAsync());
            picker.SelectAllCommand.Execute(null);
            picker.ClearSelectionCommand.Execute(null);
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            fixture.Transport.Requests.Clear();
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            Assert.False(vm.SearchSites.UseDefault);
            Assert.False(vm.SearchSites.HasSelection);
            Assert.Equal(1, fixture.Transport.Requests.Count);
            Assert.True(vm.EmptyNotice.Contains("请先选择"));
            Assert.False(vm.Busy);
        });

        Test("搜索站点：目录暂缺所选站点不扩大搜索并在恢复后重选", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [2]);
            fixture.Transport.Catalogue = "[{\"id\":1,\"name\":\"站点甲\",\"is_active\":true}]";
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            Assert.False(vm.SearchSites.HasSelection);
            Assert.Equal(1, fixture.Transport.Requests.Count);
            fixture.Transport.Catalogue = SiteTransport.FullCatalogue;
            ResourceFixture.Complete(vm.SearchSites.ReloadAsync());
            Assert.Equal("2", vm.SearchSites.Selection!.QueryValue());
            Assert.Equal(1, fixture.Saves, "目录消失或恢复不覆盖用户偏好");
        });

        Test("搜索站点：目录读取失败不降级全站搜索", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [2]);
            fixture.Transport.FailCatalogue = true;
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            Assert.True(vm.SearchSites.NoticeOpen);
            Assert.Equal(1, fixture.Transport.Requests.Count);
            Assert.False(vm.Busy);
        });

        Test("搜索站点：精确资源在首次搜索前恢复对应媒体目录", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, "电视剧", false, [2]);
            var vm = new MoviePilotResourceViewModel();
            vm.Attach(fixture.Service, ResourceFixture.Media, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync());
            Assert.True(fixture.Transport.Requests[0].Contains("/site/media/tv"));
            Assert.True(fixture.Transport.Requests[1].Contains("sites=2"));
            Assert.False(vm.Busy);
            Assert.True(vm.IsReady);
        });

        Test("搜索站点：换账号与服务隔离，密码轮换与重连保留", () =>
        {
            using var fixture = new SiteFixture();
            var originalStamp = fixture.Service.ConnectionStamp;
            fixture.Service.SaveSearchSitePreference(originalStamp, null, false, [2, 2, -1, 0]);
            fixture.Settings.MoviePilot.ProtectedPassword = "rotated-placeholder";
            fixture.Settings.MoviePilot.Enabled = false;
            fixture.Settings.MoviePilot.Enabled = true;
            Assert.False(fixture.Service.SearchSitePreference().UseDefault);
            Assert.Equal("2", string.Join(',', fixture.Service.SearchSitePreference().SiteIds));
            Assert.Throws<MoviePilotOperationBlockedException>(() => fixture.Service.SaveSearchSitePreference(originalStamp, null, true, []));
            fixture.Settings.MoviePilot.Username = "another-user";
            Assert.True(fixture.Service.SearchSitePreference().UseDefault);
            fixture.Settings.MoviePilot.Username = "anonymous-fixture";
            fixture.Settings.MoviePilot.Url = "http://192.0.2.20:3001";
            Assert.True(fixture.Service.SearchSitePreference().UseDefault);
            fixture.Settings.MoviePilot.Url = "HTTP://192.0.2.10:3001/api/v1/";
            Assert.False(fixture.Service.SearchSitePreference().UseDefault);
            Assert.True(fixture.Service.SearchSitePreference("电影").UseDefault);
            var json = JsonSerializer.Serialize(fixture.Settings.MoviePilot.SearchSitePreferences);
            Assert.False(json.Contains("placeholder"));
            Assert.False(json.Contains(originalStamp));
            Assert.False(json.Contains("anonymous-fixture"));
        });

        Test("搜索站点：保存失败保留当前选择并显示失败原因", () =>
        {
            using var fixture = new SiteFixture();
            var picker = fixture.Picker();
            ResourceFixture.Complete(picker.EnsureLoadedAsync());
            fixture.FailSave = true;
            picker.UseDefault = false;
            picker.Sites[1].Selected = true;
            Assert.True(picker.NoticeOpen);
            Assert.True(picker.NoticeTitle!.Contains("未能保存"));
            Assert.Equal("2", picker.Selection!.QueryValue());
        });

        Test("搜索站点：真实设置文件重读后新服务恢复选择", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "Momoka-SitePreferences-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (var fixture = new SiteFixture(directory: directory))
                    fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [2]);
                using var restored = new SiteFixture(directory: directory);
                var vm = new MoviePilotVersionsViewModel();
                vm.Attach(restored.Service, new FixtureLauncher());
                ResourceFixture.Complete(vm.SearchAsync("Target"));
                Assert.True(restored.Transport.Requests.Last().Contains("sites=2"));
                Assert.Equal(0, restored.Saves);
            }
            finally { Directory.Delete(directory, true); }
        });

        Test("搜索站点：挂起目录共享给新搜索，旧关键词不再发出请求", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [2]);
            fixture.Transport.PendingCatalogue = new();
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            var first = vm.SearchAsync("Old");
            var second = vm.SearchAsync("New");
            Assert.Equal(1, fixture.Transport.Requests.Count);
            fixture.Transport.PendingCatalogue.SetResult(SiteTransport.CatalogueResponse());
            CompletePending(Task.WhenAll(first, second));
            Assert.Equal(2, fixture.Transport.Requests.Count);
            Assert.True(fixture.Transport.Requests[1].Contains("keyword=New"));
            Assert.False(fixture.Transport.Requests.Any(path => path.Contains("keyword=Old")));
        });

        Test("搜索站点：离页取消挂起目录后不搜索，再进入可重新读取", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [2]);
            fixture.Transport.PendingCatalogue = new();
            var oldResponse = fixture.Transport.PendingCatalogue;
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            var oldSearch = vm.SearchAsync("Old");
            vm.Cancel();
            fixture.Transport.PendingCatalogue = null;
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("New"));
            oldResponse.SetResult(SiteTransport.CatalogueResponse());
            CompletePending(oldSearch);
            Assert.Equal(3, fixture.Transport.Requests.Count);
            Assert.True(fixture.Transport.Requests.Last().Contains("keyword=New"));
            Assert.False(fixture.Transport.Requests.Any(path => path.Contains("keyword=Old")));
            Assert.Equal("2", vm.SearchSites.Selection!.QueryValue());
        });
    }

    private static void CompletePending(Task task)
    {
        using var completion = new ManualResetEventSlim();
        _ = task.ContinueWith(_ => completion.Set(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Assert.True(completion.Wait(TimeSpan.FromSeconds(5)), "挂起的匿名任务没有完成");
        ResourceFixture.Complete(task);
    }

    /// <summary>匿名资源行：只带标题、站点与体积，足够铺一张卡。</summary>
    internal static string Result(string title, string site) =>
        JsonSerializer.Serialize(new { torrent_info = new { title, site_name = site, size = 1024L } });
}

internal sealed class SiteFixture : IDisposable
{
    internal AppSettings Settings { get; }
    internal SiteTransport Transport { get; } = new();
    internal MoviePilotService Service { get; }
    internal int Saves { get; private set; }
    internal bool FailSave { get; set; }
    private readonly MoviePilotClient _client;

    internal SiteFixture(string? directory = null)
    {
        var protector = new SiteProtector();
        var store = directory is null ? null : new SettingsStore(new AppPaths(directory), protector);
        Settings = store is not null && File.Exists(store.Paths.SettingsFile) ? store.LoadStrict() : new();
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "http://192.0.2.10:3001";
        Settings.MoviePilot.Username = "anonymous-fixture";
        Settings.MoviePilot.ProtectedPassword = "placeholder-password";
        _client = new MoviePilotClient(Transport);
        Service = new(_client, new MoviePilotCredentials(protector), Settings, () =>
        {
            Saves++;
            if (FailSave) throw new IOException("fixture save failure");
            store?.Save(Settings);
        });
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(Service, null)!;
        var sessionType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var session = Activator.CreateInstance(sessionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
        typeof(MoviePilotService).GetField("_session", flags)!.SetValue(Service, session);
        typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(Service, identity);
    }

    internal MoviePilotSitePickerViewModel Picker()
    {
        var vm = new MoviePilotSitePickerViewModel();
        vm.Attach(Service);
        return vm;
    }

    public void Dispose() { Service.Dispose(); _client.Dispose(); }

    private sealed class SiteProtector : ISecretProtector
    {
        public string Protect(string text) => throw new AssertionException("站点夹具不保护凭据");
        public string Unprotect(string text) => throw new AssertionException("站点夹具不解密或登录");
    }
}

internal sealed class SiteTransport : HttpMessageHandler
{
    internal const string FullCatalogue = "[{\"id\":1,\"name\":\"站点甲\",\"is_active\":true},{\"id\":2,\"name\":\"站点乙\",\"is_active\":true}]";
    internal List<string> Requests { get; } = [];
    internal string Catalogue { get; set; } = FullCatalogue;
    internal string SearchResults { get; set; } = "[]";
    internal bool FailCatalogue { get; set; }
    internal TaskCompletionSource<HttpResponseMessage>? PendingCatalogue { get; set; }

    internal static HttpResponseMessage CatalogueResponse() => new(HttpStatusCode.OK)
    { Content = new StringContent(FullCatalogue, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Method != HttpMethod.Get) throw new AssertionException("站点夹具拒绝登录或任何写请求");
        var uri = request.RequestUri!;
        Requests.Add(uri.PathAndQuery);
        var sites = uri.AbsolutePath.StartsWith("/api/v1/site/", StringComparison.Ordinal);
        if (!sites && !uri.AbsolutePath.StartsWith("/api/v1/search/", StringComparison.Ordinal))
            throw new AssertionException("站点夹具拒绝未登记读取");
        if (sites && PendingCatalogue is { } pending) return pending.Task;
        return Task.FromResult(new HttpResponseMessage(sites && FailCatalogue ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        { Content = new StringContent(sites ? Catalogue : SearchResults, Encoding.UTF8, "application/json") });
    }
}
