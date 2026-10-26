using System.Reflection;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Shell.ViewModels;

namespace Momoka.NaturalContinuationCallerTests;

internal sealed class Fixture : IAsyncDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Dictionary<string, EmbySessionScope> _sources;
    private readonly List<CancellationTokenSource> _identities = [];
    private readonly List<Task> _runs = [];
    private readonly List<TaskCompletionSource> _gates = [];
    private readonly List<FakePlayback.Plan> _plans = [];
    private readonly List<LeaseProbe> _leases = [];
    private readonly NoSecrets _secrets = new();
    private readonly string _output;
    private readonly string _name;
    internal Tape Tape { get; } = new();
    internal MemorySettings MemorySettings { get; } = new();
    internal AppSettings Settings => MemorySettings.Settings;
    internal AnonymousTransport Transport { get; }
    internal EmbySession Session { get; }
    internal FakePlayback Playback { get; }
    internal PlayerViewModel Vm { get; }
    internal EmbySessionScope A { get; }
    internal EmbySessionScope B { get; }
    internal EmbySessionScope C { get; }
    internal List<string> Notices { get; } = [];
    internal int Refreshes { get; private set; }
    internal int Leaves { get; private set; }
    internal Action? RefreshInput { get; set; }
    internal Action<string>? NoticeInput { get; set; }
    internal Action? LeaveInput { get; set; }

    internal Fixture(string output, string name)
    {
        _output = output;
        _name = name;
        Settings.Playback.AutoPlayNextEpisode = true;
        Settings.Playback.ReportProgressToServer = false;
        Settings.Playback.StopReportEnabled = false;
        Transport = new AnonymousTransport(Tape);
        var device = new DeviceIdentity("anonymous", "fixture", "fixture", "0");
        // Actual constructors and cached anonymous scopes, no login/restore/persistence. A null store
        // refuses unintended persistence; a throwing protector refuses any secret access.
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [Settings, null, new CredentialVault(_secrets), device, Transport], null)!;
        _sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
        A = Install("a");
        B = Install("b");
        C = Install("c");
        Default(A);
        Playback = new FakePlayback(Tape);
        Vm = new PlayerViewModel(Playback, MemorySettings, Session, Tape);
        Tape.State = () => (Vm.Hold, Vm.Idle.IsCompleted, Playback.IsPlaying, Vm.NowPlaying?.IdentityKey);
        Vm.Noticed += (text, _) =>
        {
            Notices.Add(text);
            Tape.Add("notice:" + text);
            NoticeInput?.Invoke(text);
        };
        Vm.RefreshRequested += () =>
        {
            Refreshes++;
            Tape.Add("refresh");
            RefreshInput?.Invoke();
        };
        Vm.PlayerShown += () => Tape.Add("player-shown");
        Vm.PlayerHidden += () =>
        {
            Leaves++;
            Tape.Add("leave");
            LeaveInput?.Invoke();
        };
    }

    private EmbySessionScope Install(string name)
    {
        var account = new AccountProfile { Id = "anonymous-" + name, Username = "anonymous" };
        var server = new ServerProfile { Id = "fixture-" + name, Name = "anonymous", Url = "https://" + name + ".invalid", Accounts = [account] };
        var connection = new EmbyConnection(new Uri("https://" + name + ".invalid/emby/"),
            "placeholder-only", "anonymous", "anonymous", "anonymous", Session.Device);
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null, [new EmbyClient(http, connection), server, account], null)!;
        _identities.Add((CancellationTokenSource)identityType.GetProperty("Lifetime", Members)!.GetValue(identity)!);
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
        _sources.Add(scope.Key, scope);
        return scope;
    }

    internal void Default(EmbySessionScope scope) => typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, scope);
    internal static void Revoke(EmbySessionScope scope)
    {
        var identity = typeof(EmbySessionScope).GetProperty("IdentityState", Members)!.GetValue(scope)!;
        ((CancellationTokenSource)identity.GetType().GetProperty("Lifetime", Members)!.GetValue(identity)!).Cancel();
    }
    internal EmbyItem Episode(string id, EmbySessionScope? source = null, bool media = true, string season = "season") =>
        (source ?? A).Bind(new EmbyItem
        {
            Id = id,
            Name = "anonymous-" + id,
            Type = EmbyItemType.Episode,
            SeriesId = "series",
            SeasonId = season,
            ParentIndexNumber = 1,
            MediaSources = media ? [new MediaSource { Id = "memory-" + id, Name = "inert" }] : []
        });
    internal EmbyItem Series(EmbySessionScope? source = null) => (source ?? A).Bind(new EmbyItem
    {
        Id = "series",
        Name = "anonymous series",
        Type = EmbyItemType.Series,
        MediaSources = [new MediaSource { Id = "inert" }]
    });
    internal EmbyItem ParentFor(EmbySessionScope? scope = null) => Series(scope);
    internal LeaseProbe Lease(string name, bool closed = false)
    {
        var probe = new LeaseProbe(name, Tape, closed);
        _leases.Add(probe);
        return probe;
    }
    internal TaskCompletionSource Gate()
    {
        var gate = Signals.New();
        _gates.Add(gate);
        return gate;
    }
    internal FakePlayback.Plan Plan(PlaybackResult? result = null)
    {
        var plan = result is null ? new FakePlayback.Plan() : FakePlayback.Plan.Complete(result);
        _plans.Add(plan);
        Playback.Plans.Enqueue(plan);
        return plan;
    }
    internal Task Start(EmbyItem item, IReadOnlyList<EmbyItem>? episodes = null, EmbyItem? parent = null,
        bool replace = false, long? intent = null, PlaybackChoice? choice = null)
    {
        var run = Vm.StartForFixture(item, parent ?? ParentFor(item.Source), episodes, replace, intent, choice: choice);
        _runs.Add(run);
        return run;
    }
    internal Task ViaPublicPlay(EmbyItem item, IReadOnlyList<EmbyItem>? episodes = null)
    {
        var run = Vm.PlayAsync(item, ParentFor(item.Source), episodes: episodes);
        _runs.Add(run);
        return run;
    }
    internal static PlaybackResult Eof(LeaseProbe? lease = null) =>
        new(new PlaybackExit(PlaybackEndReason.EndOfFile, 1, 0, null), 1, false, false) { HasStarted = true, Continuation = lease?.Lease };
    internal static PlaybackResult Stopped(LeaseProbe? lease = null) =>
        new(new PlaybackExit(PlaybackEndReason.Stopped, 1, 0, null), 1, false, false) { HasStarted = true, Continuation = lease?.Lease };

    internal void Settled(LeaseProbe probe, int started = 1, int leaves = 1)
    {
        Tape.Add("idle-observed");
        Check.Equal(1, probe.ReleaseCalls, "real lease release callback exactly once");
        Check.Equal(started, Playback.Starts.Count, "number of accepted fake starts");
        Check.Equal(0, Vm.Hold, "all real StartPlayback finally holds drained");
        Check.True(Vm.Idle.IsCompletedSuccessfully, "real playback idle completed");
        Check.Equal(leaves, Leaves, "only final caller leaves");
        Check.False(Vm.HasPreparation, "no surviving preparation owner");
        var end = Tape.One("lease-release-end:" + probe.Name);
        var idle = Tape.One("idle-observed");
        Check.True(end.Sequence < idle.Sequence, "lease released before idle can be observed");
        Check.True(end.Hold > 0, "release ends before its original StartPlayback hold finally");
        Check.False(end.Idle, "idle is never complete at lease release");

        if (leaves > 0)
        {
            Check.True(Vm.NowPlaying is null && Vm.PlayingScope is null, "real Leave clears item and captured scope");
            Check.Equal(0, Vm.Episodes.Count, "real Leave clears episode list");
            var leave = Tape.Snapshot().Last(e => e.Event == "leave");
            Check.True(end.Sequence < leave.Sequence, "actual lease disposal precedes LeavePlayer");
            Check.Equal(0, leave.Hold, "LeavePlayer observes zero player holds");
            Check.False(leave.Idle, "LeavePlayer runs before the idle completion finally");
            Check.False(leave.Playing, "no LeavePlayer while replacement plays");
        }
    }

    public async ValueTask DisposeAsync()
    {
        var errors = new List<Exception>();
        foreach (var gate in _gates) gate.TrySetResult();
        Vm.CancelLifetimeForFixture();
        foreach (var plan in _plans) plan.Result.TrySetResult(Stopped());
        foreach (var run in _runs)
        {
            try { await Signals.Done(run).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            catch (EndpointFailure) { }
            catch (Exception error) { errors.Add(error); }
        }
        foreach (var lease in _leases)
        {
            try { await lease.Lease.DisposeAsync().ConfigureAwait(true); }
            catch (EndpointFailure) { }
            catch (Exception error) { errors.Add(error); }
        }
        try
        {
            Check.Equal(0, _secrets.Calls, "no secret/DPAPI/login access");
            Check.Equal(0, Transport.Rejected, "only anonymous read-only fake metadata endpoints");
        }
        catch (Exception error) { errors.Add(error); }
        try
        {
            var events = Tape.Snapshot();
            await File.WriteAllTextAsync(Path.Combine(_output, _name + ".json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    events,
                    reads = Transport.Reads.Select(r => new { r.Host, r.Path, cancelled = r.Token.IsCancellationRequested }),
                    leases = _leases.Select(l => new { l.Name, l.ReleaseCalls, l.CleanupCalls, l.Consumed }),
                    secretCalls = _secrets.Calls,
                    rejectedEndpoints = Transport.Rejected,
                    remainingHold = Vm.Hold,
                    cleanupErrors = errors.Select(e => e.ToString())
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n").ConfigureAwait(true);
        }
        finally
        {
            Session.Dispose();
            foreach (var identity in _identities) identity.Dispose();
            _ = Vm.InertState;
        }
        if (errors.Count > 0) throw new AggregateException("Fixture cleanup retained failures", errors);
    }
    private sealed class NoSecrets : ISecretProtector
    {
        internal int Calls { get; private set; }
        public string Protect(string value) { Calls++; throw new TestFailure("secret protection is forbidden"); }
        public string Unprotect(string value) { Calls++; throw new TestFailure("secret/config/login restore is forbidden"); }
    }
}
