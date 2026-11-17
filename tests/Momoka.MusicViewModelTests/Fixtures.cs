using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.MusicViewModelTests;

internal sealed class Fixture : IAsyncDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly string _directory;
    public Fixture()
    {
        _directory = Path.Combine(FindRoot(), "artifacts", "music-vm-tests", Guid.NewGuid().ToString("N"));
        var noSecrets = new NoSecrets();
        Settings = new MemorySettings();
        Settings.Settings.Playback.ReportProgressToServer = false;
        var device = DeviceIdentity.Create("music-vm-fixture", "test");
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [Settings.Settings, new SettingsStore(new AppPaths(_directory), noSecrets),
                new CredentialVault(noSecrets), device, Transport], CultureInfo.InvariantCulture)!;
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("https://music-vm.invalid/emby/"), "anonymous-fixture", "user", "fixture", "fixture", device);
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, [new EmbyClient(http, connection),
            new ServerProfile { Id = "server", Name = "fixture" }, new AccountProfile { Id = "account", Username = "fixture" }])!;
        Scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null,
            [Session, identity], CultureInfo.InvariantCulture)!;
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
        sources.Add(Scope.Key, Scope);
        typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, Scope);
        Images = new EmbyImageStore(Session, Path.Combine(_directory, "images"));
        Service = new PlaybackService(Session, Settings.Settings, () => Backend,
            new PlaybackPlanner(Settings.Settings, new ShaderGroupResolver(Settings.Settings.Shaders)),
            audioBackendFactory: () => Backend);
        Music = new MusicPlaybackViewModel(Service, Session, Images, Ui, Settings);
        Music.Noticed += (message, _) => Notices.Enqueue(message);
    }

    public Transport Transport { get; } = new();
    public Backend Backend { get; } = new();
    public QueueDispatcher Ui { get; } = new();
    public MemorySettings Settings { get; }
    public EmbySession Session { get; }
    public EmbySessionScope Scope { get; }
    public EmbyImageStore Images { get; }
    public PlaybackService Service { get; }
    public MusicPlaybackViewModel Music { get; }
    public ConcurrentQueue<string> Notices { get; } = new();
    public EmbyItem Track(string id) => Scope.Bind(Transport.Track(id));
    public MusicLibraryViewModel Library(string? containerType = null, string? containerId = null)
    {
        var vm = new MusicLibraryViewModel();
        vm.Attach(new LibraryRequest
        {
            Services = EmptyServices.Instance,
            Title = "fixture",
            Source = Scope,
            ParentId = containerId ?? "music",
            MusicRootId = "music",
            MusicContainerType = containerType
        }, Session, Images);
        return vm;
    }
    public async ValueTask DisposeAsync()
    {
        await Music.StopAsync();
        Music.Dispose();
        await Service.ShutdownAsync();
        Session.Dispose();
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("当前测试不在 Momoka 工作树内");
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string value) => throw new InvalidOperationException("测试禁止凭据写入");
        public string Unprotect(string value) => throw new InvalidOperationException("测试禁止凭据读取");
    }
    private sealed class EmptyServices : IServiceProvider
    {
        public static EmptyServices Instance { get; } = new();
        public object? GetService(Type serviceType) => throw new InvalidOperationException("测试导航数据不能解析服务");
    }
}

internal sealed class QueueDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _pending = new();
    public int Pending => _pending.Count;
    public bool Post(Action work) { _pending.Enqueue(work); return true; }
    public void Run(Action work) => Post(work);
    public void Drain() { while (_pending.TryDequeue(out var work)) work(); }
}

internal sealed class MemorySettings : ISettingsService
{
    public AppSettings Settings { get; } = new();
    public int PageSize => 80;
    public bool HasUnsavedChanges => false;
    public int Saves { get; private set; }
    public event EventHandler? SaveStateChanged { add { } remove { } }
    public void Save() => Saves++;
    public void SaveDeferred() => Saves++;
    public bool TrySave() { Save(); return true; }
    public ServerProfile ResolveServer(string url) => throw new InvalidOperationException("不允许账号配置");
    public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new InvalidOperationException("不允许账号配置");
    public void ExportPreferences(string path) => throw new InvalidOperationException("不允许设置导出");
    public AppSettings ReadBackup(string path) => throw new InvalidOperationException("不允许读取设置");
}

internal sealed record Sent(string Path, string Query, HttpMethod Method);

internal sealed class Transport : HttpMessageHandler
{
    public ConcurrentQueue<Sent> Requests { get; } = new();
    public Func<Sent, CancellationToken, Task<HttpResponseMessage>>? Reply { get; set; }
    public EmbyItem Track(string id) => new()
    {
        Id = id,
        Name = id,
        Type = EmbyItemType.Audio,
        RunTimeTicks = TimeSpan.FromSeconds(120).Ticks,
        MediaSources = [new MediaSource { Id = "source-" + id, Container = "flac", SupportsDirectPlay = true,
            RunTimeTicks = TimeSpan.FromSeconds(120).Ticks,
            MediaStreams = [new MediaStream { Type = "Audio", Index = 1, Codec = "flac", IsDefault = true }] }],
        UserData = new EmbyUserData()
    };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Host != "music-vm.invalid") throw new InvalidOperationException("测试请求不能离开匿名域名");
        var sent = new Sent(request.RequestUri.AbsolutePath, Uri.UnescapeDataString(request.RequestUri.Query), request.Method);
        Requests.Enqueue(sent);
        if (Reply is { } reply) return await reply(sent, cancellationToken);
        return Default(sent);
    }
    public HttpResponseMessage Default(Sent sent)
    {
        if (sent.Path.Contains("/FavoriteItems/", StringComparison.Ordinal)) return Json(new EmbyUserData { IsFavorite = true });
        if (sent.Path.StartsWith("/emby/Users/user/Items/", StringComparison.Ordinal)) return Json(Track(sent.Path.Split('/').Last()));
        if (sent.Path.EndsWith("/Items", StringComparison.Ordinal)) return Json(new ItemsResult());
        throw new InvalidOperationException("未安排的匿名 HTTP: " + sent.Path);
    }
    public static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
}

internal sealed class Backend : IPlaybackBackend
{
    private readonly ConcurrentQueue<Handle> _handles = new();
    public string DisplayName => "anonymous controlled audio";
    public ConcurrentQueue<PlaybackRequest> Starts { get; } = new();
    public Handle[] Handles => _handles.ToArray();
    public string? Validate() => null;
    public Task<IPlaybackHandle> StartAsync(PlaybackRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MediaUrl.Host != "music-vm.invalid") throw new InvalidOperationException("不允许真实媒体 URL");
        Starts.Enqueue(request);
        var handle = new Handle(request);
        _handles.Enqueue(handle);
        return Task.FromResult<IPlaybackHandle>(handle);
    }
}

internal sealed class Handle : IPlaybackHandle, IPlayerControl
{
    private readonly TaskCompletionSource<PlaybackExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Handle(PlaybackRequest request)
    {
        var volume = request.PlayerOptions.FirstOrDefault(pair => pair.Key == "volume").Value;
        Status = new PlayerStatus
        {
            Loaded = true,
            Duration = 120,
            Position = 0,
            VolumeKnown = true,
            Volume = double.TryParse(volume, CultureInfo.InvariantCulture, out var level) ? level : 100
        };
    }
    public TaskCompletionSource Listening { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool HasControlChannel => true;
    public bool? PictureInHostWindow => null;
    public bool IsPaused => Status.Paused;
    public PlayerStatus Status { get; private set; }
    public int Stops { get; private set; }
    public ConcurrentQueue<string[]> Commands { get; } = new();
    public event Action<bool>? PauseChanged;
    public event Action<PlayerStatus>? StatusChanged;
    public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
    public void Publish(double position = 0) { Status = Status with { Position = position }; StatusChanged?.Invoke(Status); }
    public void End() => _exit.TrySetResult(new PlaybackExit(PlaybackEndReason.EndOfFile, 120, 0, null));
    public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken)
    { Listening.TrySetResult(); return _exit.Task.WaitAsync(cancellationToken); }
    public Task StopAsync() { Stops++; _exit.TrySetResult(new PlaybackExit(PlaybackEndReason.Stopped, Status.Position, 0, null)); return Task.CompletedTask; }
    public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => Task.FromResult<double?>(Status.Position);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task ShowMessageAsync(string text) => Task.CompletedTask;
    public Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
    public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => Task.FromResult<double?>(name == "volume" ? Status.Volume : null);
    public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(name.StartsWith("metadata/by-key/", StringComparison.Ordinal)
            && Metadata.TryGetValue(name["metadata/by-key/".Length..], out var value) ? value : null);
    public Task<IReadOnlyList<string>?> GetStringListAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>?>(name == "display-tags" ? DisplayTags : null);

    /// <summary>匿名夹具里的文件标签：测试自己填，不读真实文件。</summary>
    public List<string> DisplayTags { get; } = [];
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Enqueue(arguments.ToArray());
        if (arguments is ["set", "volume", var value])
            Status = Status with { Volume = double.Parse(value, CultureInfo.InvariantCulture), VolumeKnown = true };
        if (arguments is ["cycle", "pause"])
        { Status = Status with { Paused = !Status.Paused }; PauseChanged?.Invoke(Status.Paused); }
        StatusChanged?.Invoke(Status);
        return Task.FromResult(true);
    }
}
