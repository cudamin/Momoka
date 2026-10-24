using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;

namespace Momoka.Tests;

internal sealed class PlexAffordanceFixture : IDisposable
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly Dictionary<string, EmbySessionScope> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _lifetimes = new(StringComparer.Ordinal);
    private readonly DeviceIdentity _device = new("r029-r030-fixture", "fixture", "fixture", "1");
    public AffordanceTransport Transport { get; } = new();
    public AffordanceSettings Settings { get; } = new();
    public EmbySession Session { get; }
    public EmbyImageStore Images { get; }
    public PlexActionsProxy Actions { get; }
    public IShellActions SourceActions { get; }

    public PlexAffordanceFixture(string root)
    {
        var output = Path.Combine(root, "artifacts", "r029-r030", "fixture", "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var secrets = new NoSecrets();
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Fields, null,
            [Settings.Settings, new SettingsStore(new AppPaths(Path.Combine(output, "config|IO-forbidden")), secrets),
                new CredentialVault(secrets), _device, Transport], null)!;
        Images = new EmbyImageStore(Session, Path.Combine(output, "images"));
        SourceActions = DispatchProxy.Create<IShellActions, PlexActionsProxy>();
        Actions = (PlexActionsProxy)(object)SourceActions;
        Add("emby", MediaServerKind.Emby);
        Add("jellyfin", MediaServerKind.Jellyfin);
        Add("plex", MediaServerKind.Plex);
        Add("fnnas", MediaServerKind.FnNas);
        Default("emby");
    }

    public EmbySessionScope Scope(string name) => _sources[name];
    public void Default(string name) => typeof(EmbySession).GetField("_current", Fields)!.SetValue(Session, Scope(name));
    public void Revoke(string name) => _lifetimes[name].Cancel();

    private void Add(string name, MediaServerKind kind)
    {
        var api = new Uri("https://r029-" + name + ".invalid/" + (kind == MediaServerKind.Emby ? "emby/" : ""));
        var connection = new EmbyConnection(api, "placeholder-only", name + "-user", name + "-user", name, _device)
        { ServerKind = kind };
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Fields)!.GetValue(Session)!;
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Fields | BindingFlags.Public, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = name, Name = name, ServerKind = kind },
                new AccountProfile { Id = name + "-account", Username = name + "-user" }], null)!;
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Fields, null, [Session, identity], null)!;
        _sources.Add(name, scope);
        _lifetimes.Add(name, (CancellationTokenSource)identityType.GetProperty("Lifetime")!.GetValue(identity)!);
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Fields)!.GetValue(Session)!;
        sources.Add(scope.Key, scope);
        Transport.Products.Add(api.Host, kind);
    }

    public EmbyItem Item(string source, string type = EmbyItemType.Movie, bool favorite = false, bool played = false,
        string id = "item", bool genres = true)
    {
        var item = Scope(source).Bind(new EmbyItem
        {
            Id = id,
            Name = "离线条目",
            Type = type,
            Overview = "假数据，不播放",
            Genres = genres ? ["动画", "剧情"] : [],
            RunTimeTicks = TimeSpan.FromMinutes(20).Ticks,
            UserData = new EmbyUserData
            {
                IsFavorite = favorite,
                Played = played,
                PlayCount = 2,
                PlaybackPositionTicks = 40_000,
                PlayedPercentage = 20
            },
            SeriesId = type == EmbyItemType.Episode ? "show" : null,
            SeasonId = type == EmbyItemType.Episode ? "season" : null,
            SeriesName = type == EmbyItemType.Episode ? "离线剧集" : null,
            ParentIndexNumber = type == EmbyItemType.Episode ? 1 : null,
            IndexNumber = type == EmbyItemType.Episode ? 1 : null
        });
        Transport.Items[(Scope(source).Connection.ApiBase.Host, id)] = JsonSerializer.Deserialize<EmbyItem>(JsonSerializer.Serialize(item))!;
        return item;
    }

    public CardItem Card(EmbyItem item, bool wide = false, bool sourceLine = false) =>
        new(item, Images, wide ? 300 : 170, wide: wide, sourceLine: sourceLine);

    public static bool HasStateOrder(CardItem card) => Orders().TryGetValue(card.Item, out _);
    public static int Pending(CardItem card)
    {
        if (!Orders().TryGetValue(card.Item, out var order)) return 0;
        return new[] { "_watched", "_favorite" }.Sum(name =>
        {
            var state = typeof(ItemUserStateOrder).GetField(name, Fields)!.GetValue(order)!;
            return (int)state.GetType().GetField("Pending")!.GetValue(state)!;
        });
    }
    private static ConditionalWeakTable<EmbyItem, ItemUserStateOrder> Orders() =>
        (ConditionalWeakTable<EmbyItem, ItemUserStateOrder>)typeof(ItemCommands)
            .GetField("UserStateOrders", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    public void Dispose()
    {
        Session.Dispose();
        foreach (var lifetime in _lifetimes.Values) lifetime.Dispose();
        Assert.Equal(0, Transport.Unexpected, "只允许本项占位 HTTP，任何越界即失败");
        Assert.Equal(0, Settings.SaveCalls, "不能持久化用户设置");
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string value) => throw new AssertionException("禁止凭据写入");
        public string Unprotect(string value) => throw new AssertionException("禁止凭据读取");
    }
}

internal sealed class AffordanceSettings : ISettingsService
{
    public AppSettings Settings { get; } = new() { AggregateMode = true };
    public int PageSize => 20;
    public int SaveCalls { get; private set; }
    public event EventHandler? SaveStateChanged { add { } remove { } }
    public bool HasUnsavedChanges => false;
    public bool TrySave() { SaveCalls++; throw new AssertionException("禁止配置保存"); }
    public void Save() { SaveCalls++; throw new AssertionException("禁止配置保存"); }
    public ServerProfile ResolveServer(string url) => throw new AssertionException("禁止发现真实来源");
    public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new AssertionException("禁止发现真实账号");
    public void ExportPreferences(string path) => throw new AssertionException("禁止配置IO");
    public AppSettings ReadBackup(string path) => throw new AssertionException("禁止配置IO");
}

internal sealed class AffordanceTransport : HttpMessageHandler
{
    public Dictionary<string, MediaServerKind> Products { get; } = new(StringComparer.Ordinal);
    public Dictionary<(string Host, string Id), EmbyItem> Items { get; } = new();
    public List<(string Host, string Path, string Method)> Requests { get; } = [];
    public int Writes { get; private set; }
    public int Unexpected { get; private set; }
    public bool FailWrites { get; set; }
    public bool FailDetailRead { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        if (!Products.TryGetValue(uri.Host, out var kind)) throw UnexpectedRequest(request);
        Requests.Add((uri.Host, uri.AbsolutePath, request.Method.Method));
        var route = uri.AbsolutePath.Trim('/');
        if (route.StartsWith("emby/", StringComparison.Ordinal)) route = route[5..];
        var parts = route.Split('/');
        // Match both legacy Emby and Jellyfin 12 routes actually emitted by this tree.
        var jellyfinState = kind == MediaServerKind.Jellyfin && parts.Length == 2
            && parts[0] is "UserFavoriteItems" or "UserPlayedItems"
            && (request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete);
        var state = parts.Length == 4 && parts[0] == "Users" && parts[2] is "FavoriteItems" or "PlayedItems"
            && request.Method is var method && (method == HttpMethod.Post || method == HttpMethod.Delete);
        var plexState = kind == MediaServerKind.Plex && route is ":/scrobble" or ":/unscrobble" && request.Method == HttpMethod.Get;
        if (state || jellyfinState || plexState)
        {
            Writes++;
            await Task.Yield();
            if (FailWrites) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (plexState)
            {
                var id = Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("key=", StringComparison.Ordinal))[4..]);
                Items[(uri.Host, id)].UserData!.Played = route == ":/scrobble";
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            var item = Items[(uri.Host, jellyfinState ? parts[1] : parts[3])];
            item.UserData ??= new EmbyUserData();
            if (jellyfinState ? parts[0] == "UserFavoriteItems" : parts[2] == "FavoriteItems") item.UserData.IsFavorite = request.Method == HttpMethod.Post;
            else item.UserData.Played = request.Method == HttpMethod.Post;
            return Json(item.UserData);
        }
        if (request.Method != HttpMethod.Get) throw UnexpectedRequest(request);
        // Read-only replies are immediate: background artwork/version work is outside this bounded suite.
        if (kind == MediaServerKind.Plex)
        {
            if (parts.Length == 3 && parts[0] == "library" && parts[1] == "metadata")
            {
                if (FailDetailRead && parts[2] == "item") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                var item = Resolve(uri.Host, parts[2]);
                return Json(new JsonObject { ["MediaContainer"] = new JsonObject { ["Metadata"] = new JsonArray(Plex(item)) } });
            }
            if (parts.Length == 4 && parts[0] == "library" && parts[1] == "metadata" && parts[3] is "similar" or "children")
                return Json(new JsonObject { ["MediaContainer"] = new JsonObject { ["Metadata"] = new JsonArray() } });
        }
        else if (kind is MediaServerKind.Emby or MediaServerKind.Jellyfin)
        {
            if (FailDetailRead && (parts.Length == 4 && parts[0] == "Users" && parts[2] == "Items" && parts[3] == "item"
                || kind == MediaServerKind.Jellyfin && route == "Items/item"))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (parts.Length == 4 && parts[0] == "Users" && parts[2] == "Items") return Json(Resolve(uri.Host, parts[3]));
            if (kind == MediaServerKind.Jellyfin && parts.Length == 2 && parts[0] == "Items") return Json(Resolve(uri.Host, parts[1]));
            if (kind == MediaServerKind.Jellyfin && route == "Items") return Json(new ItemsResult { Items = [], TotalRecordCount = 0 });
            if (parts.Length == 3 && parts[0] == "Users" && parts[2] == "Items"
                || parts.Length == 3 && parts[0] == "Items" && parts[2] == "Similar"
                || parts.Length == 3 && parts[0] == "Shows" && parts[2] == "Episodes")
                return Json(new ItemsResult { Items = [], TotalRecordCount = 0 });
        }
        throw UnexpectedRequest(request);
    }

    private EmbyItem Resolve(string host, string id) => Items.TryGetValue((host, id), out var item) ? item
        : id == "show" ? new EmbyItem { Id = "show", Name = "离线剧集", Type = EmbyItemType.Series, Genres = ["动画"] }
        : throw new AssertionException("未注册假条目：" + id);
    private static JsonObject Plex(EmbyItem item) => new()
    {
        ["ratingKey"] = item.Id,
        ["title"] = item.Name,
        ["summary"] = item.Overview,
        ["type"] = item.Type == EmbyItemType.Episode ? "episode" : item.Type == EmbyItemType.Series ? "show" : "movie",
        ["duration"] = 1_200_000,
        ["viewCount"] = item.IsWatched ? 1 : 0,
        ["viewOffset"] = 4,
        ["grandparentRatingKey"] = item.SeriesId,
        ["parentRatingKey"] = item.SeasonId,
        ["grandparentTitle"] = item.SeriesName,
        ["parentIndex"] = item.ParentIndexNumber,
        ["index"] = item.IndexNumber,
        ["Genre"] = new JsonArray(item.Genres.Select(genre => (JsonNode)new JsonObject { ["tag"] = genre }).ToArray())
    };
    private Exception UnexpectedRequest(HttpRequestMessage request)
    {
        Unexpected++;
        return new AssertionException("本项不允许的假HTTP：" + request.Method + " " + request.RequestUri!.AbsolutePath);
    }
    private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}

public class PlexActionsProxy : DispatchProxy
{
    public List<string> Messages { get; } = [];
    public List<EmbyItem> FavoriteRefreshes { get; } = [];
    public List<EmbyItem> WatchedReports { get; } = [];
    public List<(string Genre, EmbySessionScope? Scope)> Navigation { get; } = [];
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "Notify": Messages.Add((string)args![0]!); return null;
            case "RefreshFavorites": FavoriteRefreshes.Add((EmbyItem)args![0]!); return null;
            case "ReportMarkedWatched": WatchedReports.Add((EmbyItem)args![0]!); return null;
            case "OpenGenre": Navigation.Add(((string)args![0]!, (EmbySessionScope?)args[1])); return null;
            case "get_MoviePilotEnabled": return false;
            case "add_MoviePilotAvailabilityChanged":
            case "remove_MoviePilotAvailabilityChanged": return null;
            default: throw new AssertionException("不允许窗口/native/播放动作：" + targetMethod?.Name);
        }
    }
}

internal sealed class ShellAssembly
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Assembly _assembly;
    public string Root { get; }
    public ShellAssembly(string shellPath, string root)
    {
        Root = Path.GetFullPath(root);
        var path = Path.GetFullPath(shellPath);
        var release = Path.Combine(Root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar;
        if (!File.Exists(path) || !File.Exists(Path.Combine(Root, "Momoka.sln"))
            || !path.StartsWith(release, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) != "Momoka.dll")
            throw new AssertionException("只允许本树刚编译的 x64 Release Shell DLL");
        var directory = Path.GetDirectoryName(path)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        _assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        Console.WriteLine("真实 Shell DLL：" + path);
    }
    public Type Type(string name) => _assembly.GetType("Momoka.Shell." + name, throwOnError: true)!;
    public object NewViewModel() => Activator.CreateInstance(Type("ViewModels.DetailViewModel"))!;
    public PlexActionsProxy Actions() => (PlexActionsProxy)DispatchProxy.Create(Type("Views.IShellActions"), typeof(PlexActionsProxy));
    public static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, Members) ?? throw new AssertionException("缺少真实入口：" + name);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
    public static T Get<T>(object target, string name) => (T)(target.GetType().GetProperty(name, Members)
        ?? throw new AssertionException("缺少真实属性：" + name)).GetValue(target)!;
    public static void Set(object target, string name, object? value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
    public object Request(EmbyItem item)
    {
        var request = Activator.CreateInstance(Type("Views.DetailRequest"), nonPublic: true)!;
        Set(request, "Services", new NoServices());
        Set(request, "Item", item);
        return request;
    }
    public void Attach(object vm, PlexAffordanceFixture fixture, EmbyItem item, PlexActionsProxy actions) =>
        Invoke(vm, "Attach", Request(item), actions, fixture.Settings, fixture.Session, fixture.Images);
    public object GenreRequest(string genre, EmbySessionScope? scope) => Type("Views.LibraryRequest")
        .GetMethod("ForGenre", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [new NoServices(), genre, scope])!;
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new AssertionException("不启动容器/App");
    }
}

internal sealed class AffordanceContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Call, object? State)> _queue = new();
    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
    public static void Run(Func<Task> action)
    {
        var previous = Current;
        using var context = new AffordanceContext();
        SetSynchronizationContext(context);
        try
        {
            var task = action().WaitAsync(TimeSpan.FromSeconds(20));
            while (!task.IsCompleted)
                if (context._queue.TryTake(out var work, 20)) work.Call(work.State);
            while (context._queue.TryTake(out var tail)) tail.Call(tail.State);
            if (task.IsCanceled) throw new TaskCanceledException(task);
            if (task.Exception is { } failure)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException ?? failure).Throw();
        }
        finally { SetSynchronizationContext(previous); }
    }
    public static async Task Until(Func<bool> condition)
    {
        var time = Stopwatch.StartNew();
        while (!condition())
        {
            if (time.Elapsed > TimeSpan.FromSeconds(5)) throw new AssertionException("真实命令未收尾");
            await Task.Delay(1);
        }
    }
    public void Dispose() => _queue.Dispose();
}

internal sealed class VisibilityContract : IDisposable
{
    private readonly object _source;
    private readonly string? _property;
    private readonly System.ComponentModel.INotifyPropertyChanged? _notifier;
    public string Value { get; private set; } = "Visible";
    public VisibilityContract(XElement element, object source)
    {
        _source = source;
        var binding = (string?)element.Attribute("Visibility");
        if (binding is null) return;
        if (!binding.StartsWith("{x:Bind ", StringComparison.Ordinal)) { Value = binding; return; }
        Assert.Contains("Mode=OneWay", binding, "动态显隐必须接通知");
        var path = binding[8..].Split(',')[0];
        Assert.True(path.StartsWith("Card.", StringComparison.Ordinal) || path.StartsWith("ViewModel.", StringComparison.Ordinal));
        _property = path[(path.IndexOf('.') + 1)..];
        _notifier = source as System.ComponentModel.INotifyPropertyChanged;
        if (_notifier is not null) _notifier.PropertyChanged += Changed;
        Read();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == _property || string.IsNullOrEmpty(args.PropertyName)) Read();
    }
    private void Read() => Value = ShellAssembly.Get<object>(_source, _property!).ToString()!;
    public void Dispose() { if (_notifier is not null) _notifier.PropertyChanged -= Changed; }
    public static XElement Element(string root, string file, string handle)
    {
        var document = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", file));
        return document.Descendants().Single(element => element.Attributes().Any(attribute =>
            attribute.Name.LocalName is "Name" or "AutomationProperties.AutomationId" && attribute.Value == handle));
    }
}
