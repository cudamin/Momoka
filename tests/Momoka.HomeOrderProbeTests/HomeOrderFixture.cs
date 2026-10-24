using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Services;
using Momoka.Shell;
using Momoka.Shell.Views;

namespace Momoka.Tests;

/// <summary>Real HomeCard/RefreshHomeRows/row objects, real MP availability; no Login/DPAPI/native/UI.</summary>
internal sealed class HomeOrderFixture : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly MoviePilotClient _client;
    private readonly HttpMessageHandler _rejectMedia = new RejectReads();
    public MemorySettings Settings { get; } = new();
    public MoviePilotReads Reads { get; } = new();
    public MoviePilotService Service { get; }
    public EmbySession Session { get; }
    public object Vm { get; }
    public object Table { get; }
    public IList Rows { get; }
    public object Sections { get; }
    public SettingsPage Page { get; }
    public IReadOnlyList<HomeRowSetting> Saved { get; }
    public static ShellRuntime Runtime => ShellRuntime.Current;

    public HomeOrderFixture()
    {
        var library = new EmbyItem { Id = "library", Name = "离线电影库", Type = EmbyItemType.CollectionFolder };
        var plan = HomeLayout.Plan(null, [library]).ToList();
        var series = plan.Single(row => row.Key == HomeLayout.MoviePilotSeries);
        plan.Remove(series);
        plan.Insert(0, series with { Visible = false });
        var movies = plan.Single(row => row.Key == HomeLayout.MoviePilotMovies);
        plan[plan.IndexOf(movies)] = movies with { Visible = false };
        Settings.Settings.Ui.HomeRows = HomeLayout.Save(plan);
        Saved = HomeLayout.Save(plan);
        Settings.Settings.MoviePilot.Url = "https://homeorder-fixture.invalid:3001";
        Settings.Settings.MoviePilot.Username = "anonymous-fixture";
        // No password is supplied or decoded: the anonymous cached session below is the endpoint boundary.
        var protector = new DenySecrets();
        _client = new MoviePilotClient(Reads);
        Service = new MoviePilotService(_client, new MoviePilotCredentials(protector), Settings.Settings);
        var paths = new AppPaths(Path.Combine(Runtime.SourceRoot, "work", "homeorder-probe", "禁止IO|fixture"));
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [Settings.Settings, new SettingsStore(paths, protector), new CredentialVault(protector),
                new DeviceIdentity("homeorder-fixture", "offline", "offline", "1"), _rejectMedia], null)!;
        var capabilities = new ServerCapabilities(Session);
        Vm = Runtime.New("SettingsViewModel");
        var args = Vm.GetType().GetMethod("Attach", Members)!.GetParameters().Select(parameter => parameter.Name switch
        {
            "settings" => (object)Settings,
            "paths" => paths,
            "moviePilot" => Service,
            "capabilities" => capabilities,
            _ => parameter.HasDefaultValue ? parameter.DefaultValue : null
        }).ToArray();
        Runtime.Invoke(Vm, "Attach", args);
        // Only the actual home card is built. Whole-page Reload would construct native subtitle previews.
        Runtime.List(Vm, "Sections").Add(Runtime.Invoke(Vm, "HomeCard"));
        Runtime.Set(Vm, "SelectedCategory", "主页");
        Runtime.Set(Vm, "IsReady", true);
        Table = Runtime.Get<object>(Vm, "HomeRows");
        Rows = Runtime.List(Table, "Rows");
        Sections = Runtime.List(Vm, "Sections");
        Runtime.NextPageViewModel = Vm;
        Page = new SettingsPage();
    }

    public Task Open() => ShellNavigationProbe.BeginHomeOrderForFixture(Page, Service);
    public Task Reopen() => ShellNavigationProbe.ReopenHomeOrderForFixture(Page, Service, Rows);
    public Task WaitRows(int count) => ShellNavigationProbe.UntilForFixture(() => Rows.Count == count);
    public void Drain() => Page.DispatcherQueue.Drain();
    public string[] Keys => Rows.Cast<object>().Select(row => Runtime.Get<string>(row, "Key")).ToArray();
    public int MoviePilotCount => Keys.Count(HomeLayout.IsMoviePilot);

    public Task<bool> Connect()
    {
        Settings.Settings.MoviePilot.Enabled = true;
        // Seed only an anonymous transport session. Available still changes exclusively through the
        // actual GET and SetAvailable/AvailabilityChanged chain, never through a fake availability model.
        var identity = Runtime.Invoke(Service, "CaptureIdentity")!;
        var apiBase = (Uri)identity.GetType().GetProperty("ApiBase")!.GetValue(identity)!;
        var cachedType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var cached = Activator.CreateInstance(cachedType, Members, null,
            [identity, new MoviePilotSession(apiBase, "anonymous-session", "anonymous-fixture", false)], null)!;
        typeof(MoviePilotService).GetField("_session", Members)!.SetValue(Service, cached);
        return Service.RefreshAvailabilityAsync();
    }

    public void Stable()
    {
        Assert.True(ReferenceEquals(Table, Runtime.Get<object>(Vm, "HomeRows")), "真实 HomeRows 表对象必须稳定");
        Assert.True(ReferenceEquals(Rows, Runtime.List(Table, "Rows")), "真实 ItemsSource Rows 必须稳定");
        Assert.True(ReferenceEquals(Sections, Runtime.List(Vm, "Sections")), "缓存分类集合不能重建");
        Assert.True(HomeLayout.Same(Saved, HomeLayout.Plan(Settings.Settings.Ui.HomeRows, null)), "显隐不能删除或改写原次序/勾选");
        Assert.Equal(0, Settings.Writes, "装表/显隐/重入均不得保存设置");
    }

    public void Dispose()
    {
        Page.Release();
        Runtime.Invoke(Vm, "Dispose");
        Service.Dispose();
        _client.Dispose();
        Session.Dispose();
        _rejectMedia.Dispose();
    }
}

internal sealed class MoviePilotReads : HttpMessageHandler
{
    public bool Offline { get; set; }
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri?.Host != "homeorder-fixture.invalid"
            || !request.RequestUri.AbsolutePath.EndsWith("/dashboard/system", StringComparison.Ordinal))
            throw new InvalidOperationException("仅允许匿名夹具的 dashboard/system GET，拒绝登录、写入或网络");
        Requests++;
        if (Offline) throw new HttpRequestException("匿名夹具断线");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"version\":\"offline-fixture\"}", Encoding.UTF8, "application/json")
        });
    }
}

internal sealed class RejectReads : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("主页排序回归禁止媒体服务器HTTP");
}
