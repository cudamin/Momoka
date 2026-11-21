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

/// <summary>Real HomeSections/RefreshHomeRows/row objects, real MP availability; no Login/DPAPI/native/UI.</summary>
internal sealed class HomeOrderFixture : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly MoviePilotClient _client;
    private readonly HttpMessageHandler _rejectMedia = new RejectReads();
    public MemorySettings Settings { get; } = new();
    public MoviePilotReads Reads { get; } = new();
    public MoviePilotService Service { get; }
    public EmbySession Session { get; }
    public EmbySessionScope Source { get; }
    /// <summary>聚合模式那一档多出来的一条在线来源；默认夹具只有一条。</summary>
    public EmbySessionScope? SecondSource { get; }
    public ServerCapabilities Capabilities { get; }
    public object Vm { get; }
    public object Table { get; }
    public IList Rows { get; }
    public object Sections { get; }
    public List<EmbyItem> LibraryDirectory { get; } = [];
    public SettingsPage Page { get; }
    public IReadOnlyList<HomeRowSetting> Saved { get; }
    public static ShellRuntime Runtime => ShellRuntime.Current;

    public HomeOrderFixture(bool secondSource = false, bool liveDirectory = false)
    {
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
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        Source = CreateScope(http, "homeorder", "anonymous", "anonymous-user");
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
        sources.Add(Source.Key, Source);
        if (secondSource)
        {
            SecondSource = CreateScope(http, "homeorder2", "second", "second-user");
            sources.Add(SecondSource.Key, SecondSource);
        }
        typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, Source);
        Capabilities = new ServerCapabilities(Session);
        LibraryDirectory.Add(Source.Bind(new EmbyItem
        {
            Id = "library",
            Name = "离线电影库",
            Type = EmbyItemType.CollectionFolder,
            CollectionType = "movies"
        }));
        if (liveDirectory)
            LibraryDirectory.Add(Source.Bind(new EmbyItem
            {
                Id = "music",
                Name = "离线音乐库",
                Type = EmbyItemType.CollectionFolder,
                CollectionType = "music"
            }));
        if (SecondSource is not null)
            LibraryDirectory.Add(SecondSource.Bind(new EmbyItem
            {
                Id = "library",
                Name = "离线电影库",
                Type = EmbyItemType.CollectionFolder,
                CollectionType = "movies"
            }));
        var plan = HomeLayout.Plan(null, LibraryDirectory).ToList();
        var series = plan.Single(row => row.Key == HomeLayout.MoviePilotSeries);
        plan.Remove(series);
        plan.Insert(0, series with { Visible = false });
        var movies = plan.Single(row => row.Key == HomeLayout.MoviePilotMovies);
        plan[plan.IndexOf(movies)] = movies with { Visible = false };
        Settings.Settings.Ui.HomeRows = HomeLayout.Save(plan);
        Saved = HomeLayout.Save(plan);
        Vm = Runtime.New("SettingsViewModel");
        var args = Vm.GetType().GetMethod("Attach", Members)!.GetParameters().Select(parameter => parameter.Name switch
        {
            "settings" => (object)Settings,
            "paths" => paths,
            "moviePilot" => Service,
            "capabilities" => Capabilities,
            _ => parameter.HasDefaultValue ? parameter.DefaultValue : null
        }).ToArray();
        Runtime.Invoke(Vm, "Attach", args);
        if (liveDirectory)
            Runtime.Invoke(Vm, "UseHomeLibraryDirectory", (Func<IReadOnlyList<EmbyItem>>)(() => LibraryDirectory.ToArray()));
        // Only the actual home cards are built. Whole-page Reload would construct native subtitle previews.
        foreach (var section in (IEnumerable)Runtime.Invoke(Vm, "HomeSections")!)
            Runtime.List(Vm, "Sections").Add(section);
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
    public Task WaitLibraryEntries(int count) => ShellNavigationProbe.UntilForFixture(() => LibraryKeys().Length == count);
    public void Drain() => Page.DispatcherQueue.Drain();
    public string[] Keys => Rows.Cast<object>().Select(row => Runtime.Get<string>(row, "Key")).ToArray();
    public string[] LibraryKeys()
    {
        var table = Runtime.Get<object>(Table, "LibraryEntries");
        return table is null ? [] : Runtime.List(table, "Rows").Cast<object>()
            .Select(row => Runtime.Get<string>(row, "Key")).ToArray();
    }
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

    /// <summary>一条匿名在线来源：连接、身份与来源键都齐，HTTP 仍由 <see cref="RejectReads"/> 拒绝。</summary>
    private EmbySessionScope CreateScope(EmbyHttp http, string serverId, string accountId, string userName)
    {
        var connection = new EmbyConnection(new Uri($"https://{serverId}-media.invalid/emby/"), "",
            userName, userName, $"{serverId}-server", DeviceIdentity.Create(serverId, "1"));
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = serverId }, new AccountProfile { Id = accountId }], null)!;
        return (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
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
