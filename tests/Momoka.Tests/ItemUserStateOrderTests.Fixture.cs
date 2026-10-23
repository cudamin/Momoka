#if ITEM_USER_STATE_ORDER_FIXTURE
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Tests;

internal sealed class ItemUserStateFixture : IAsyncDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "fixtures", "r016", Guid.NewGuid().ToString("N"));
    private readonly List<CancellationTokenSource> _sourceLifetimes = [];
    private readonly EmbyImageStore _images;
    public EmbySession Session { get; }
    public EmbySessionScope Scope { get; }
    public ItemUserStateHandler Handler { get; } = new();
    public ItemUserStateShell Shell { get; } = new();
    public CardItem Card { get; }
    public FrameworkElement Owner { get; } = new();
    public PosterCard Poster { get; }
    public EpisodeRow Episode { get; }
    public List<string> Changes { get; } = [];
    public int WatchedCallbacks { get; private set; }
    private ItemUserStateContext Context => (ItemUserStateContext)SynchronizationContext.Current!;

    public ItemUserStateFixture(EmbyUserData? initial = null, string type = EmbyItemType.Movie)
    {
        Directory.CreateDirectory(_root); // Test output only; retain it, never delete a computed tree.
        var device = new DeviceIdentity("r016-fixture", "r016-fixture", "r016-fixture", "1");
        var protector = new NoSecrets();
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [new AppSettings(), new SettingsStore(new AppPaths(_root), protector), new CredentialVault(protector), device, Handler], null)!;
        Scope = AddSource("original", device);
        SetDefault(Scope);
        _images = new EmbyImageStore(Session, Path.Combine(_root, "images"));
        Card = NewCard(Scope.Bind(new EmbyItem
        {
            Id = "state-item",
            Name = "offline-state-fixture",
            Type = type,
            RunTimeTicks = 100_000,
            UserData = initial ?? State()
        }));
        Poster = new PosterCard { Card = Card };
        Episode = new EpisodeRow { Card = Card };
        Owner.DataContext = Card;
    }

    // Install synthetic available identities directly. No SignIn, Restore, Adopt, credential exchange,
    // profile persistence or server/product detection is called. All fields belong to this fake session.
    private EmbySessionScope AddSource(string name, DeviceIdentity device)
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("http://item-state-" + name + ".invalid/emby/"), "",
            name + "-user", name + "-user", "offline-" + name, device);
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [new EmbyClient(http, connection), new ServerProfile { Id = name + "-server" }, new AccountProfile { Id = name + "-account" }], null)!;
        var source = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [Session, identity], null)!;
        _sourceLifetimes.Add((CancellationTokenSource)identityType.GetProperty("Lifetime")!.GetValue(identity)!);
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession)
            .GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
        sources.Add(source.Key, source);
        return source;
    }

    private void SetDefault(EmbySessionScope source) =>
        typeof(EmbySession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, source);

    public void SwitchDefault()
    {
        var other = AddSource("default", new DeviceIdentity("r016-default", "r016-default", "r016-default", "1"));
        SetDefault(other); // Only the fake default pointer changes; no saved profile is read or written.
        Assert.False(Scope.IsCurrent);
        Assert.True(Scope.IsAvailable);
    }

    public void RevokeSource() => _sourceLifetimes[0].Cancel();
    public CardItem NewCard(EmbyItem item)
    {
        var card = new CardItem(item, _images, 170);
        card.PropertyChanged += (_, args) => Changes.Add(args.PropertyName ?? "");
        return card;
    }

    public void Hover(CardAction action, bool episode = false) =>
        ItemCommands.Handle(Session, Shell, episode ? Episode : Poster, new CardActionEventArgs(action, Owner),
            watched: _ => WatchedCallbacks++);

    public void Menu(ItemCommand command)
    {
        // Build and click the actual menu row, not just its lower-level method.
        var menu = ItemCommands.Build(Session, Shell, Owner, Card, watched: _ => WatchedCallbacks++);
        menu.Items.OfType<MenuFlyoutItem>().Single(row => row.Tag is ItemCommand value && value == command).ClickForFixture();
    }

    public async Task ReplyAsync(ItemUserStateRequest request, EmbyUserData? data, bool noContent = false)
    {
        var next = Context.CompletedCommands + 1;
        request.Complete(data, noContent);
        await ItemUserStateContext.UntilAsync(() => Context.CompletedCommands >= next, "real ItemCommands continuation did not finish");
    }

    public async Task FailAsync(ItemUserStateRequest request)
    {
        var next = Context.CompletedCommands + 1;
        request.Fail();
        await ItemUserStateContext.UntilAsync(() => Context.CompletedCommands >= next, "real failed command continuation did not finish");
    }

    public async ValueTask DisposeAsync()
    {
        Handler.ReleaseAll();
        await ItemUserStateContext.UntilAsync(() => Context.CompletedCommands >= Handler.Count, "fixture left real user-state commands running");
        Assert.Equal(0, Handler.Unexpected, "only synthetic user-state POST/DELETE may execute");
        Session.Dispose();
        foreach (var lifetime in _sourceLifetimes) lifetime.Dispose();
    }

    public static EmbyUserData State(bool played = false, bool favorite = false, long position = 40_000,
        double? percentage = 40, int playCount = 2, int? unplayed = 3, DateTimeOffset? lastPlayed = null) => new()
        {
            Played = played,
            IsFavorite = favorite,
            PlaybackPositionTicks = position,
            PlayedPercentage = percentage,
            PlayCount = playCount,
            UnplayedItemCount = unplayed,
            LastPlayedDate = lastPlayed
        };

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string value) => throw new InvalidOperationException("R-016 fixture must not save credentials");
        public string Unprotect(string value) => throw new InvalidOperationException("R-016 fixture must not read credentials");
    }
}

internal sealed class ItemUserStateHandler : HttpMessageHandler
{
    private readonly List<ItemUserStateRequest> _requests = [];
    public int Count => _requests.Count;
    public int Unexpected { get; private set; }
    public ItemUserStateRequest At(int index) => _requests.Count > index ? _requests[index]
        : throw new AssertionException($"Expected real user-state request #{index + 1}, got {_requests.Count}");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if ((request.Method != HttpMethod.Post && request.Method != HttpMethod.Delete)
            || uri.Host != "item-state-original.invalid" || segments.Length != 5
            || segments[0] != "emby" || segments[1] != "Users" || segments[2] != "original-user"
            || segments[3] is not ("PlayedItems" or "FavoriteItems") || !segments[4].StartsWith("state-", StringComparison.Ordinal))
        {
            Unexpected++;
            throw new InvalidOperationException("R-016 fixture permits only in-memory original-source user-state requests");
        }
        var pending = new ItemUserStateRequest(request.Method, segments[3], segments[4], cancellationToken);
        _requests.Add(pending);
        // Deliberately ignore cancellation and return late successes: real scope/apply guards must reject them.
        return pending.Response;
    }

    public void ReleaseAll()
    {
        foreach (var request in _requests) request.Complete(null, noContent: true);
    }
}

internal sealed class ItemUserStateRequest(HttpMethod method, string field, string itemId, CancellationToken token)
{
    private readonly TaskCompletionSource<HttpResponseMessage> _response = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public HttpMethod Method { get; } = method;
    public string Field { get; } = field;
    public string ItemId { get; } = itemId;
    public CancellationToken Token { get; } = token;
    public Task<HttpResponseMessage> Response => _response.Task;

    public void Complete(EmbyUserData? data, bool noContent)
    {
        var response = new HttpResponseMessage(noContent ? HttpStatusCode.NoContent : HttpStatusCode.OK)
        {
            Content = new StringContent(noContent ? "" : JsonSerializer.Serialize(data), Encoding.UTF8, "application/json")
        };
        if (!_response.TrySetResult(response)) response.Dispose();
    }
    public void Fail()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("synthetic user-state failure", Encoding.UTF8, "text/plain")
        };
        if (!_response.TrySetResult(response)) response.Dispose();
    }
}

internal sealed class ItemUserStateShell : IShellActions
{
    public List<(string Message, InfoBarSeverity Severity)> Notices { get; } = [];
    public List<EmbyItem> WatchedReports { get; } = [];
    public List<EmbyItem?> FavoriteRefreshes { get; } = [];
    public bool MoviePilotEnabled => false;
    public event EventHandler? MoviePilotAvailabilityChanged { add { } remove { } }
    public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) => Notices.Add((message, severity));
    public void ReportMarkedWatched(EmbyItem item) => WatchedReports.Add(item); // No PlaybackService or actual report.
    public void RefreshFavorites(EmbyItem? changed = null) => FavoriteRefreshes.Add(changed);
    private static Exception Unrelated() => new InvalidOperationException("R-016 fixture must not navigate, play or open integrations");
    public void OpenItem(EmbyItem item) => throw Unrelated();
    public Task PlayAsync(EmbyItem item, EmbyItem? parent = null, PlaybackChoice? choice = null, IReadOnlyList<EmbyItem>? episodes = null) => throw Unrelated();
    public bool TryOpenLibrary(string id, EmbySessionScope? source = null) => throw Unrelated();
    public void OpenGenre(string genre, EmbySessionScope? source = null) => throw Unrelated();
    public void OpenRow(HomeLayout.HomeRowTarget target, EmbySessionScope? source = null) => throw Unrelated();
    public void OpenSignIn(ServerProfile? server = null, bool newAccount = false) => throw Unrelated();
    public Task SwitchProfileAsync(ServerProfile server, AccountProfile? account) => throw Unrelated();
    public void SetTitleStrip(TitleStrip strip) => throw Unrelated();
    public void SearchMoviePilotVersions(EmbyItem item) => throw Unrelated();
    public void SearchAniBtVersions(EmbyItem item) => throw Unrelated();
    public void ShowMoviePilotReorganize(Momoka.MoviePilot.MoviePilotTransferContext context) => throw Unrelated();
    public void ShowAniBtScrape(EmbySessionScope scope, EmbyItem item) => throw Unrelated();
    public void ShowMissingEpisodes(EmbyItem item, IReadOnlyList<EmbyItem> missing) => throw Unrelated();
}

// Drive actual ConfigureAwait(true) application callbacks on one controlled thread. Detect the
// compiler-generated RunAsync machine via its attribute, not a copied implementation or fixed delay.
internal sealed class ItemUserStateContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Call, object? State, bool Command)> _queue = new();
    private readonly List<(SendOrPostCallback Call, object? State, bool Command)> _held = [];
    private static readonly Type CommandMachine = typeof(ItemCommands).GetMethod("RunAsync", BindingFlags.Static | BindingFlags.NonPublic)!
        .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
    private int _postedCommands;
    private int _completedCommands;
    public int PostedCommands => Volatile.Read(ref _postedCommands);
    public int CompletedCommands => Volatile.Read(ref _completedCommands);
    public bool PauseCommands { get; set; }

    public override void Post(SendOrPostCallback d, object? state)
    {
        var target = state is Delegate action ? action.Target : state;
        var command = target?.GetType().GenericTypeArguments.Contains(CommandMachine) == true;
        if (command) Interlocked.Increment(ref _postedCommands);
        try { _queue.Add((d, state, command)); }
        catch (InvalidOperationException) { }
    }

    public void ResumeCommands()
    {
        PauseCommands = false;
        foreach (var work in _held) _queue.Add(work);
        _held.Clear();
    }

    public static void Run(Func<Task> run)
    {
        var previous = Current;
        using var context = new ItemUserStateContext();
        SetSynchronizationContext(context);
        try
        {
            var task = run().WaitAsync(TimeSpan.FromSeconds(12));
            while (!task.IsCompleted)
            {
                if (!context._queue.TryTake(out var work, 20)) continue;
                if (work.Command && context.PauseCommands) { context._held.Add(work); continue; }
                context.Invoke(work);
            }
            context.ResumeCommands();
            while (context._queue.TryTake(out var work)) context.Invoke(work);
            if (task.IsCanceled) throw new TaskCanceledException(task);
            if (task.Exception is { } failure)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException ?? failure).Throw();
        }
        finally { SetSynchronizationContext(previous); }
    }

    private void Invoke((SendOrPostCallback Call, object? State, bool Command) work)
    {
        work.Call(work.State);
        if (work.Command) Interlocked.Increment(ref _completedCommands);
    }

    public static async Task UntilAsync(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(3)) throw new AssertionException(message);
            await Task.Delay(5);
        }
    }

    public void Dispose() => _queue.CompleteAdding();
}
#endif
