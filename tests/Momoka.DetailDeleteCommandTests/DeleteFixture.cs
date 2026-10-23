using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Tests;

internal sealed class DeleteFixture : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly NoSecrets _secrets = new();
    private readonly FakeSettings _settings = new();
    public FixtureContext Context { get; } = new();
    public DialogPlan Dialogs { get; } = new();
    public DeleteTransport Transport { get; }
    public EmbySession Session { get; }
    public EmbySessionScope Scope { get; }
    public EmbyImageStore Images { get; }
    public object Vm { get; }
    public DetailVmBridge Bridge { get; }
    public DetailActionsProxy Actions { get; }
    public Button Owner { get; } = new();
    public CardItem Card { get; private set; } = null!;
    public int Changed { get; private set; }
    public int Removed { get; private set; }
    public DeleteFixture(MediaServerKind kind = MediaServerKind.FnNas, int sourceCount = 1)
    {
        DialogPlan.Current = Dialogs;
        Transport = new DeleteTransport(kind);
        var device = new DeviceIdentity("anonymous fixture", "fixture", "fixture", "0");
        var path = Path.Combine(Program.Runtime.Root, "artifacts", "r025-delete-command-fixture", "no-settings-or-images");
        var store = new SettingsStore(new AppPaths(path), _secrets);
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [_settings.Settings, store, new CredentialVault(_secrets), device, Transport], null)!;
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("https://delete-fixture.invalid/" +
            (kind == MediaServerKind.FnNas ? "v/" : kind == MediaServerKind.Emby ? "emby/" : "")),
            "placeholder-only", "fixture-user", "anonymous", "anonymous", device)
        { ServerKind = kind };
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = "fixture-server", Name = "anonymous", ServerKind = kind },
                new AccountProfile { Id = "fixture-account" }], null)!;
        Scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!).Add(Scope.Key, Scope);
        typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, Scope);
        Images = new EmbyImageStore(Session, path);
        Actions = (DetailActionsProxy)DispatchProxy.Create(Program.Runtime.Type("Momoka.Shell.Views.IShellActions"), typeof(DetailActionsProxy));
        Vm = Program.Runtime.New("Momoka.Shell.ViewModels.DetailViewModel");
        Bridge = new DetailVmBridge(Vm, Images);
        Attach("item", sourceCount);
        Assert.Equal(0, Transport.Requests.Count, "Attach/Apply不得登录或网络备援");
        Assert.Equal(0, _secrets.Calls);
    }
    public void Attach(string id, int sourceCount = 1)
    {
        var item = NewItem(id, sourceCount);
        var request = Program.Runtime.New("Momoka.Shell.Views.DetailRequest");
        request.GetType().GetProperty("Item")!.SetValue(request, item);
        ShellRuntime.Invoke(Vm, "Attach", request, Actions, _settings, Session, Images);
        ShellRuntime.Invoke(Vm, "Apply", item);
        Card = new CardItem(item, Images, 170);
        Assert.NotNull(Get("PlayTarget"));
        Context.Drain();
    }
    public EmbyItem NewItem(string id, int sources) => Scope.Bind(new EmbyItem
    {
        Id = id,
        Name = "anonymous movie",
        Type = EmbyItemType.Movie,
        RunTimeTicks = 600_000_000,
        MediaSources = Enumerable.Range(1, sources).Select(index => new MediaSource
        {
            Id = "version-" + index,
            ItemId = id,
            Path = "/anonymous/" + index + ".mkv",
            Container = "mkv"
        }).ToList()
    });
    public void ApplySame(int sources) => ShellRuntime.Invoke(Vm, "Apply", NewItem("item", sources));
    public Action ChangedCallback()
    {
        var callback = Bridge.CaptureReload();
        return () => { Changed++; callback(); };
    }
    public Action<CardItem> RemovedCallback()
    {
        var callback = Bridge.CaptureRemoval();
        return card => { Removed++; callback(card); };
    }
    public Task Command(string name, Action? changed, Action<CardItem>? removed)
    {
        var method = typeof(ItemCommands).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        var args = method.GetParameters().Length == 5
            ? new object?[] { Scope, new CommandActions(Actions), Owner, Card, changed }
            : new object?[] { Scope, new CommandActions(Actions), Owner, Card, changed, removed };
        try { return (Task)method.Invoke(null, args)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    public void Await(Task task) => Context.Await(task);
    public void Reload() => Await((Task)ShellRuntime.Invoke(Vm, "ReloadAsync")!);
    public void Start() => Await((Task)ShellRuntime.Invoke(Vm, "StartAsync", false)!);
    public void Cancel() => ShellRuntime.Invoke(Vm, "Cancel");
    public object? Get(string name) => ShellRuntime.Get(Vm, name);
    public bool CanPlay => ((System.Windows.Input.ICommand)Get("PlayCommand")!).CanExecute(null);
    public string? SelectedId => Get("SelectedSource") is { } row
        ? ((MediaSource)row.GetType().GetProperty("Source")!.GetValue(row)!).Id : null;
    public int SourceCount => ((ICollection)Get("Sources")!).Count;
    public void More(ItemCommand command)
    {
        ItemCommands.NextCommand = command;
        var page = new DetailPage(Session, new CommandActions(Actions), Images, Bridge);
        page.OpenMore(Owner);
    }
    // Assemble a real discovered version/source row without login or cross-server HTTP.
    // It deliberately reuses A's item/file IDs; only the captured source key distinguishes B.
    public object SelectForeignCopy()
    {
        var device = new DeviceIdentity("anonymous fixture", "fixture", "fixture", "0");
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("https://delete-fixture.invalid/foreign/emby/"),
            "placeholder-only", "foreign-user", "anonymous", "foreign", device);
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = "foreign-server" }, new AccountProfile { Id = "foreign-account" }], null)!;
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!).Add(scope.Key, scope);
        var item = scope.Bind(new EmbyItem
        {
            Id = "item",
            Name = "anonymous foreign copy",
            Type = EmbyItemType.Movie,
            RunTimeTicks = 600_000_000,
            MediaSources = [new MediaSource { Id = "version-1", ItemId = "item", Path = "/anonymous/foreign.mkv", Container = "mkv" }]
        });
        var versions = Vm.GetType().GetField("_versions", Members)!;
        var current = (IReadOnlyList<Momoka.Playback.MediaVersion>)versions.GetValue(Vm)!;
        versions.SetValue(Vm, current.Append(new Momoka.Playback.MediaVersion(item, item.MediaSources[0])).ToArray());
        var row = new SourceRow(item.MediaSources[0], "foreign copy");
        ((IList)Get("Sources")!).Add(row);
        Vm.GetType().GetProperty("SelectedSource", Members)!.SetValue(Vm, row);
        Assert.True(ReferenceEquals(item, Get("PlayTarget")));
        Assert.Equal(3, SourceCount);
        return row!;
    }
    public void Revoke()
    {
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!).Remove(Scope.Key);
    }
    public void Dispose()
    {
        ((IDisposable)Vm).Dispose();
        Session.Dispose();
        Context.Drain();
        Context.Dispose();
        Assert.Equal(0, _secrets.Calls);
        Assert.Equal(0, _settings.Writes);
        Assert.Equal(0, Transport.Forbidden);
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public int Calls { get; private set; }
        public string Protect(string text) { Calls++; throw new AssertionException("禁止凭据保护"); }
        public string Unprotect(string text) { Calls++; throw new AssertionException("禁止DPAPI/登录备援"); }
    }
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int PageSize => 20;
        public bool HasUnsavedChanges => false;
        public int Writes { get; private set; }
        private Exception NoWrite() { Writes++; return new AssertionException("禁止设置/账号文件操作"); }
        public bool TrySave() => throw NoWrite();
        public void Save() => throw NoWrite();
        public ServerProfile ResolveServer(string url) => throw NoWrite();
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw NoWrite();
        public void ExportPreferences(string path) => throw NoWrite();
        public AppSettings ReadBackup(string path) => throw NoWrite();
    }
}

public class DetailActionsProxy : DispatchProxy
{
    public List<(string Message, string Severity)> Notices { get; } = [];
    public int Refreshes { get; set; }
    public int Plays { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method?.Name)
        {
            case "Notify": Notices.Add(((string)args![0]!, args[1]!.ToString()!)); return null;
            case "RefreshFavorites": Refreshes++; return null;
            case "PlayAsync": Plays++; return Task.CompletedTask;
            case "get_MoviePilotEnabled": return false;
            default: throw new AssertionException("禁止外部shell操作：" + method?.Name);
        }
    }
}

internal sealed class CommandActions(DetailActionsProxy proxy) : IShellActions
{
    public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) => proxy.Notices.Add((message, severity.ToString()));
    public void RefreshFavorites(EmbyItem? changed = null) => proxy.Refreshes++;
    public bool MoviePilotEnabled => false;
    public event EventHandler? MoviePilotAvailabilityChanged { add { } remove { } }
    private static Exception Reject() => new AssertionException("禁止无关shell动作");
    public void OpenItem(EmbyItem item) => throw Reject();
    public Task PlayAsync(EmbyItem item, EmbyItem? parent = null, Momoka.Playback.PlaybackChoice? choice = null,
        IReadOnlyList<EmbyItem>? episodes = null) => throw Reject();
    public void ReportMarkedWatched(EmbyItem item) => throw Reject();
    public bool TryOpenLibrary(string id, EmbySessionScope? source = null) => throw Reject();
    public void OpenGenre(string genre, EmbySessionScope? source = null) => throw Reject();
    public void OpenRow(HomeLayout.HomeRowTarget target, EmbySessionScope? source = null) => throw Reject();
    public void OpenSignIn(ServerProfile? server = null, bool newAccount = false) => throw Reject();
    public Task SwitchProfileAsync(ServerProfile server, AccountProfile? account) => throw Reject();
    public void SetTitleStrip(TitleStrip strip) => throw Reject();
    public void SearchMoviePilotVersions(EmbyItem item) => throw Reject();
    public void SearchAniBtVersions(EmbyItem item) => throw Reject();
    public void ShowMoviePilotReorganize(Momoka.MoviePilot.MoviePilotTransferContext context) => throw Reject();
    public void ShowAniBtScrape(EmbySessionScope scope, EmbyItem item) => throw Reject();
    public void ShowMissingEpisodes(EmbyItem item, IReadOnlyList<EmbyItem> missing) => throw Reject();
}

internal sealed class DeleteTransport(MediaServerKind kind) : HttpMessageHandler
{
    public List<(string Method, string Path, string Body)> Requests { get; } = [];
    public int Forbidden { get; private set; }
    public string FilesJson { get; set; } = """{"code":0,"msg":"","data":[{"media_guid":"version-1","file_path":"/anonymous/1.mkv","file_size":1048576,"height":1080}]}""";
    public HttpStatusCode DeleteStatus { get; set; } = HttpStatusCode.OK;
    public string DeleteJson { get; set; } = """{"code":0,"msg":"","data":{}}""";
    public HttpStatusCode FilesStatus { get; set; } = HttpStatusCode.OK;
    public bool HoldDelete { get; set; }
    public TaskCompletionSource DeleteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void ReleaseDelete() => _release.TrySetResult();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.RequestUri is not { Host: "delete-fixture.invalid" } uri)
        {
            Forbidden++;
            throw new AssertionException("禁止真实主机/网络备援");
        }
        Assert.False(uri.Query.Contains("placeholder-only", StringComparison.Ordinal), "凭据不得进URL");
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
        Requests.Add((request.Method.Method, uri.AbsolutePath, body));
        var fnnas = kind == MediaServerKind.FnNas;
        var prefix = kind == MediaServerKind.Emby ? "/emby/" : "/";
        if (request.Method == HttpMethod.Delete && uri.AbsolutePath == (fnnas ? "/v/api/v1/item/item" : prefix + "Items/item"))
        {
            if (fnnas)
            {
                using var json = JsonDocument.Parse(body);
                Assert.Equal(1, json.RootElement.GetProperty("delete_file").GetInt32());
                Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("media_guids").ValueKind);
            }
            else Assert.Equal("", body);
            DeleteEntered.TrySetResult();
            if (HoldDelete) await _release.Task.ConfigureAwait(false); // Deliberately permit a late server reply.
            return Response(DeleteStatus, fnnas ? DeleteJson : "");
        }
        if (fnnas && request.Method == HttpMethod.Get && uri.AbsolutePath == "/v/api/v1/media/itemfile/item")
            return Response(FilesStatus, FilesJson);
        if (request.Method == HttpMethod.Get && uri.AbsolutePath == (fnnas ? "/v/api/v1/item/item" : prefix + "Users/fixture-user/Items/item"))
            return Response(HttpStatusCode.ServiceUnavailable, "");
        Forbidden++;
        throw new AssertionException("禁止未登记端点：" + request.Method + " " + uri.AbsolutePath);
    }
    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
    public int Deletes => Requests.Count(request => request.Method == "DELETE");
    public int Gets => Requests.Count(request => request.Method == "GET");
    public string[] DeletedIds
    {
        get
        {
            var body = Requests.Single(request => request.Method == "DELETE").Body;
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("media_guids").EnumerateArray().Select(value => value.GetString()!).ToArray();
        }
    }
}
