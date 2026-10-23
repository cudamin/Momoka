using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal sealed class MissingEpisodesFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly MoviePilotClient _moviePilotClient;
    private readonly RejectMoviePilotHttp _moviePilotHttp = new();
    private readonly RejectSecrets _secrets = new();
    private readonly List<Task> _opens = [];
    private readonly List<(EmbySessionScope Scope, CancellationTokenSource Lifetime)> _sources = [];
    private readonly DialogPlan _previousDialogs;
    private readonly string _dataRoot;
    internal AppSettings Settings { get; } = new();
    internal DialogPlan Dialogs { get; } = new();
    internal AnonymousMediaHttp Transport { get; }
    internal EmbySession Session { get; }
    internal EmbySessionScope Scope { get; }
    internal MoviePilotService MoviePilot { get; }
    internal ShellPage Shell { get; }
    internal EmbyItem Owner { get; }
    internal IReadOnlyList<EmbyItem> Missing { get; }
    internal bool Disposed { get; private set; }
    internal static ConcurrentBag<FixtureObservation> Observations { get; } = [];

    internal MissingEpisodesFixture(bool season = false, bool anime = false, bool parentAnime = false)
    {
        _previousDialogs = DialogPlan.Current;
        DialogPlan.Current = Dialogs;
        _dataRoot = Path.Combine(ProductionProvenance.Root, "artifacts", "r041-missing-search-availability", "anonymous-data", Guid.NewGuid().ToString("N"));
        var device = new DeviceIdentity("anonymous fixture", "fixture", "r041-fixture", "0");
        var store = new SettingsStore(new AppPaths(_dataRoot), _secrets);
        Transport = new AnonymousMediaHttp(season, anime, parentAnime);
        Session = Reflect.New<EmbySession>(Settings, store, new CredentialVault(_secrets), device, Transport);
        Scope = InstallScope("original");
        Owner = Scope.Bind(new EmbyItem
        {
            Id = season ? "owner-season" : "owner-series",
            Name = season ? "Anonymous season" : "Anonymous series",
            Type = season ? EmbyItemType.Season : EmbyItemType.Series,
            SeriesId = season ? "parent-series" : null,
            IndexNumber = season ? 1 : null,
            Genres = anime ? ["动画"] : []
        });
        Missing = [Scope.Bind(new EmbyItem
        {
            Id = "virtual-missing-2",
            Name = "Anonymous missing episode",
            Type = EmbyItemType.Episode,
            ParentIndexNumber = 1,
            IndexNumber = 2,
            PremiereDate = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
        })];
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "https://r041-moviepilot.invalid/";
        Settings.MoviePilot.Username = "anonymous-mp";
        Settings.MoviePilot.ProtectedPassword = "placeholder-not-dpapi";
        _moviePilotClient = new MoviePilotClient(_moviePilotHttp);
        MoviePilot = new MoviePilotService(_moviePilotClient, new MoviePilotCredentials(_secrets), Settings);
        var images = new EmbyImageStore(Session, Path.Combine(_dataRoot, "images"));
        _services = new ServiceCollection().AddSingleton(MoviePilot).AddSingleton(images).BuildServiceProvider();
        Shell = new ShellPage(_services, Session);
        Check.Equal(0, Transport.Requests.Count, "anonymous installation must not login/probe");
        Check.Equal(0, _secrets.Calls, "construction must not use secrets");
    }

    internal EmbySessionScope InstallScope(string name)
    {
        var device = Session.Device;
        var connection = new EmbyConnection(new Uri("https://r041-media.invalid/emby/"), "placeholder-only",
            "fixture-user", "anonymous", "anonymous", device)
        { ServerKind = MediaServerKind.Emby };
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("missing actual source identity type");
        var identity = Reflect.New(identityType,
            new EmbyClient(Reflect.Field<EmbyHttp>(Session, "_http"), connection),
            new ServerProfile { Id = "fixture-server-" + name, Name = "anonymous", ServerKind = MediaServerKind.Emby },
            new AccountProfile { Id = "fixture-account-" + name, Username = "anonymous" });
        var scope = Reflect.New<EmbySessionScope>(Session, identity);
        Reflect.Field<Dictionary<string, EmbySessionScope>>(Session, "_sources").Add(scope.Key, scope);
        Reflect.SetField(Session, "_current", scope);
        _sources.Add((scope, Reflect.Property<CancellationTokenSource>(identity, "Lifetime")));
        return scope;
    }

    internal void RevokeOriginal() => _sources.Single(source => ReferenceEquals(source.Scope, Scope)).Lifetime.Cancel();

    // Installs only in-memory availability through the real service setter and event.
    // No TestConnection/RefreshAvailability, login, token recovery, or protector is called.
    internal void Available(bool value)
    {
        var identity = Reflect.Call(MoviePilot, "CaptureIdentity");
        Reflect.Call(MoviePilot, "SetAvailable", identity, Reflect.Field<long>(MoviePilot, "_availabilityRevision"), value);
        Check.Equal(value && MoviePilot.Enabled, MoviePilot.Available, "real availability setter");
    }
    internal void ReplaceMoviePilot()
    {
        Settings.MoviePilot.Url = "https://r041-moviepilot-replacement.invalid/";
        Available(true);
    }
    internal int AvailabilitySubscribers =>
        Reflect.Field<Delegate?>(MoviePilot, "AvailabilityChanged")?.GetInvocationList().Length ?? 0;
    internal Task Open(IReadOnlyList<EmbyItem>? missing = null)
    {
        var task = Shell.OpenMissing(Owner, missing ?? Missing);
        _opens.Add(task);
        return task;
    }
    internal Task<MissingEpisodesDialog> NextDialog() => Dialogs.Next();
    internal async Task Finish(Task opening, MissingEpisodesDialog dialog)
    {
        if (dialog.IsOpen) dialog.Hide();
        dialog.DeliverClosed();
        await opening.WaitAsync(TimeSpan.FromSeconds(10));
        Shell.DispatcherQueue.Drain();
        Check.False(Shell.MissingShowing, "prompt guard resets");
        Check.Equal(0, AvailabilitySubscribers, "operation unsubscribes actual service event");
    }
    internal int SearchHandoffs => Shell.MoviePilotQueries.Count + Shell.AniBtQueries.Count;
    internal void AssertNoSearch() => Check.Equal(0, SearchHandoffs, "no actual search handoff");

    public async ValueTask DisposeAsync()
    {
        if (Disposed) return;
        try
        {
            foreach (var source in _sources) source.Lifetime.Cancel();
            Transport.ReleaseAll();
            foreach (var dialog in Dialogs.Dialogs)
            {
                if (dialog.IsOpen) dialog.Hide();
                dialog.DeliverClosed();
            }
            Shell.DispatcherQueue.Drain();
            await Task.WhenAll(_opens).WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var dialog in Dialogs.Dialogs)
            {
                if (dialog.IsOpen) dialog.Hide();
                dialog.DeliverClosed();
            }
            Check.Equal(0, Transport.Forbidden, "fake HTTP rejects all non-whitelisted routes");
            Check.Equal(0, Transport.Writes, "zero write requests");
            Check.Equal(0, _moviePilotHttp.Attempts, "zero MoviePilot HTTP/search/login requests");
            Check.Equal(0, _secrets.Calls, "zero secret/DPAPI attempts");
            Check.False(File.Exists(Path.Combine(_dataRoot, "settings.json")), "no settings IO");
            Check.Equal(0, AvailabilitySubscribers, "no leaked availability subscription");
        }
        finally
        {
            Observations.Add(new FixtureObservation(Transport.Requests.ToArray(), Transport.Writes, Transport.Forbidden,
                _moviePilotHttp.Attempts, _secrets.Calls, Dialogs.Dialogs.Count, SearchHandoffs));
            _services.Dispose();
            MoviePilot.Dispose();
            _moviePilotClient.Dispose();
            _moviePilotHttp.Dispose();
            Session.Dispose();
            foreach (var source in _sources) source.Lifetime.Dispose();
            DialogPlan.Current = _previousDialogs;
            Disposed = true;
        }
    }
}

internal sealed record FixtureObservation(AnonymousRequest[] Requests, int WriteRequests, int ForbiddenRequests,
    int MoviePilotHttpAttempts, int SecretAttempts, int Dialogs, int SearchHandoffs);
internal sealed record AnonymousRequest(string Method, string Path);

internal sealed class DialogPlan
{
    private readonly Channel<MissingEpisodesDialog> _shown = Channel.CreateUnbounded<MissingEpisodesDialog>();
    internal static DialogPlan Current { get; set; } = new();
    internal List<MissingEpisodesDialog> Dialogs { get; } = [];
    internal bool DeferCloseCompletion { get; set; }
    internal void Shown(ContentDialog dialog)
    {
        if (dialog is not MissingEpisodesDialog missing) throw new InvalidOperationException("unrelated dialog rejected");
        Dialogs.Add(missing);
        if (!_shown.Writer.TryWrite(missing)) throw new InvalidOperationException("dialog notification failed");
    }
    internal Task<MissingEpisodesDialog> Next() => _shown.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
}

internal sealed class ReadGate
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Hold { get; set; }
    internal Task Entered => _entered.Task;
    internal void Release() => _release.TrySetResult(true);
    internal async Task Wait(CancellationToken token)
    {
        _entered.TrySetResult(true);
        if (Hold) await _release.Task.WaitAsync(token);
    }
}

internal sealed class AnonymousMediaHttp(bool season, bool anime, bool parentAnime) : HttpMessageHandler
{
    internal ConcurrentQueue<AnonymousRequest> Requests { get; } = new();
    internal ReadGate Detail { get; } = new();
    internal ReadGate Parent { get; } = new();
    internal HttpStatusCode DetailStatus { get; set; } = HttpStatusCode.OK;
    internal int Writes { get; private set; }
    internal int Forbidden { get; private set; }
    internal void ReleaseAll() { Detail.Release(); Parent.Release(); }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("request URI required");
        Requests.Enqueue(new AnonymousRequest(request.Method.Method, uri.AbsolutePath));
        if (request.Method != HttpMethod.Get) Writes++;
        var detailPath = "/emby/Users/fixture-user/Items/" + (season ? "owner-season" : "owner-series");
        var parentPath = "/emby/Users/fixture-user/Items/parent-series";
        if (request.Method != HttpMethod.Get || uri.Host != "r041-media.invalid"
            || (uri.AbsolutePath != detailPath && (!season || uri.AbsolutePath != parentPath)))
        {
            Forbidden++;
            throw new InvalidOperationException("R041 fake HTTP forbids login, recovery, discovery, images, searches and writes");
        }
        var parent = uri.AbsolutePath == parentPath;
        await (parent ? Parent : Detail).Wait(token);
        var json = parent
            ? JsonSerializer.Serialize(new { Id = "parent-series", Name = "Anonymous parent", Type = EmbyItemType.Series, Genres = parentAnime ? new[] { "动画" } : [] })
            : JsonSerializer.Serialize(new
            {
                Id = season ? "owner-season" : "owner-series",
                Name = season ? "Anonymous detail season" : "Anonymous series",
                Type = season ? EmbyItemType.Season : EmbyItemType.Series,
                SeriesId = season ? "parent-series" : null,
                IndexNumber = season ? (int?)1 : null,
                Genres = anime ? new[] { "动画" } : []
            });
        return new HttpResponseMessage(parent ? HttpStatusCode.OK : DetailStatus)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class RejectMoviePilotHttp : HttpMessageHandler
{
    internal int Attempts { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Attempts++;
        throw new InvalidOperationException("R041 fixture forbids all MoviePilot HTTP including login/search/write");
    }
}
internal sealed class RejectSecrets : ISecretProtector
{
    internal int Calls { get; private set; }
    public string Protect(string plainText) { Calls++; throw new InvalidOperationException("secret protection forbidden"); }
    public string Unprotect(string cipherText) { Calls++; throw new InvalidOperationException("secret/DPAPI read forbidden"); }
}

internal static class Reflect
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static T New<T>(params object?[] args) => (T)New(typeof(T), args);
    internal static object New(Type type, params object?[] args) => Activator.CreateInstance(type, Members, null, args, null)
        ?? throw new InvalidOperationException("missing actual constructor: " + type.FullName);
    internal static T Field<T>(object owner, string name)
    {
        var field = owner.GetType().GetField(name, Members) ?? throw new InvalidOperationException("missing actual field: " + name);
        return (T)field.GetValue(owner)!;
    }
    internal static T Property<T>(object owner, string name) =>
        (T)(owner.GetType().GetProperty(name, Members)?.GetValue(owner)
            ?? throw new InvalidOperationException("missing actual property: " + name));
    internal static void SetField(object owner, string name, object value) =>
        (owner.GetType().GetField(name, Members) ?? throw new InvalidOperationException("missing actual field: " + name)).SetValue(owner, value);
    internal static object? Call(object owner, string name, params object?[] args)
    {
        var candidates = owner.GetType().GetMethods(Members).Where(method => method.Name == name && method.GetParameters().Length == args.Length).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("missing/ambiguous actual method: " + name);
        try { return candidates[0].Invoke(owner, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}

internal sealed class TestFailure(string message) : Exception(message);
internal static class Check
{
    internal static void True(bool actual, string message) { if (!actual) throw new TestFailure(message); }
    internal static void False(bool actual, string message) => True(!actual, message);
    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new TestFailure($"{message}: expected={expected}, actual={actual}");
    }
}
