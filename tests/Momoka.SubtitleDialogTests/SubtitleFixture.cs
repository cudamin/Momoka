using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Tests;

internal sealed class SubtitleFixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly DeviceIdentity _device = new("r056-fixture", "r056-fixture", "r056-fixture", "1");
    private readonly List<CancellationTokenSource> _lifetimes = [];
    private readonly FixtureActions _actions = new();
    internal EmbySession Session { get; }
    internal EmbySessionScope Scope { get; }
    internal SubtitleTransport Transport { get; } = new();
    internal DetailViewModel Parent { get; } = new();
    internal FrameworkElement Owner { get; } = new() { XamlRoot = new() };
    internal EmbyItem Item { get; }
    internal SubtitleDialog Dialog { get; private set; } = null!;
    internal Task Caller { get; private set; } = Task.CompletedTask;
    internal int Changed { get; private set; }

    internal SubtitleFixture()
    {
        var secrets = new NoSecrets();
        var paths = new AppPaths(Path.Combine(AppContext.BaseDirectory, "no-settings-io"));
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Private, null,
            [new AppSettings(), new SettingsStore(paths, secrets), new CredentialVault(secrets), _device, Transport], null)!;
        Scope = AddSource("original");
        SetDefault(Scope);
        Item = Scope.Bind(Transport.Snapshot());
        Parent.FixtureAttach(Scope, Item);
    }

    private EmbySessionScope AddSource(string name)
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("http://r056-" + name + ".invalid/emby/"), "",
            name + "-user", name + "-user", "offline-" + name, _device);
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Private | BindingFlags.Public, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = name + "-server" },
                new AccountProfile { Id = name + "-account" }], null)!;
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Private, null, [Session, identity], null)!;
        _lifetimes.Add((CancellationTokenSource)identityType.GetProperty("Lifetime")!.GetValue(identity)!);
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(Session)!;
        sources.Add(scope.Key, scope);
        return scope;
    }

    private void SetDefault(EmbySessionScope scope) => typeof(EmbySession).GetField("_current", Private)!.SetValue(Session, scope);
    internal void SwitchDefault() => SetDefault(AddSource("replacement"));
    internal void Revoke() => _lifetimes[0].Cancel();

    internal async Task OpenAsync()
    {
        var shown = new TaskCompletionSource<SubtitleDialog>();
        ContentDialog.Shown = dialog => shown.SetResult((SubtitleDialog)dialog);
        Caller = ItemCommands.FixtureSubtitles(Scope, _actions, Owner, Item, () =>
        {
            Changed++;
            Parent.Reload();
        });
        Dialog = await shown.Task.WaitAsync(TimeSpan.FromSeconds(10));
        ContentDialog.Shown = null;
    }

    internal async Task CloseAsync()
    {
        Assert.True(Dialog.FixtureClose(), "A settled write must allow closing");
        await Caller.WaitAsync(TimeSpan.FromSeconds(10));
    }

    internal int DisplayedTracks => Parent.Applied!.DefaultMediaSource!.SubtitleStreams.Count();
    internal void AssertOriginalOnly() => Assert.True(Transport.Requests.All(request => request.Host == "r056-original.invalid"),
        "Never use a new default source for the old dialog or its parent refresh");

    public void Dispose()
    {
        Console.WriteLine($"    请求账：GET={Transport.Reads}，实际write={Transport.Writes}，服务器落实={Transport.AppliedWrites}，parent.changed={Changed}");
        ContentDialog.Shown = null;
        Parent.Dispose();
        Session.Dispose();
        foreach (var lifetime in _lifetimes) lifetime.Dispose();
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("No credential IO in R056 fixture");
        public string Unprotect(string cipherText) => throw new InvalidOperationException("No credential IO in R056 fixture");
    }
    private sealed class FixtureActions : IShellActions
    {
        public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) { }
    }
}

internal sealed class ReadPlan
{
    internal TaskCompletionSource Entered { get; } = new();
    internal TaskCompletionSource Gate { get; } = new();
    internal HttpStatusCode? Failure { get; set; }
}

// The virtual server applies a POST/DELETE before losing its response. It never contacts any host.
internal sealed class SubtitleTransport : HttpMessageHandler
{
    private readonly Queue<ReadPlan> _reads = new();
    internal List<MediaStream> Tracks { get; } = [Track(1, "original")];
    internal List<(string Method, string Host, string Path)> Requests { get; } = [];
    internal int Reads { get; private set; }
    internal int Writes { get; private set; }
    internal int AppliedWrites { get; private set; }
    internal bool LoseResponse { get; set; } = true;
    internal HttpStatusCode? WriteFailure { get; set; }
    internal Exception ResponseLoss { get; set; } = new IOException("Virtual server applied write; response interrupted");
    internal TaskCompletionSource? WriteGate { get; set; }
    internal TaskCompletionSource WriteEntered { get; } = new();

    internal ReadPlan PlanRead(bool hold = false, HttpStatusCode? failure = null)
    {
        var plan = new ReadPlan { Failure = failure };
        if (!hold) plan.Gate.SetResult();
        _reads.Enqueue(plan);
        return plan;
    }

    internal EmbyItem Snapshot() => new()
    {
        Id = "item",
        Name = "virtual-movie",
        Type = EmbyItemType.Movie,
        MediaSources = [new MediaSource
        {
            Id = "file", Name = "virtual-file", Path = "/virtual/not-opened/movie.mkv",
            MediaStreams = [.. Tracks.Select(track => Track(track.Index, track.Title!))]
        }]
    };

    private static MediaStream Track(int index, string title) => new()
    {
        Index = index,
        Type = "Subtitle",
        Codec = "srt",
        IsExternal = true,
        Title = title
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing virtual URI");
        Requests.Add((request.Method.Method, uri.Host, uri.AbsolutePath));
        if (uri.Host is not ("r056-original.invalid" or "r056-replacement.invalid"))
            throw new InvalidOperationException("Unexpected host in source fixture");

        if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/Items/item", StringComparison.Ordinal))
        {
            Reads++;
            if (_reads.TryDequeue(out var plan))
            {
                plan.Entered.SetResult();
                await plan.Gate.Task.WaitAsync(token).ConfigureAwait(false);
                if (plan.Failure is { } failure) return new(failure);
            }
            return Json(Snapshot());
        }
        if (request.Method == HttpMethod.Get && uri.AbsolutePath.Contains("/RemoteSearch/Subtitles/", StringComparison.Ordinal))
            return Json(Array.Empty<RemoteSubtitleInfo>());

        var downloading = request.Method == HttpMethod.Post && uri.AbsolutePath.Contains("/RemoteSearch/Subtitles/", StringComparison.Ordinal);
        var deleting = request.Method == HttpMethod.Delete && uri.AbsolutePath.Contains("/Subtitles/", StringComparison.Ordinal);
        if (!downloading && !deleting) throw new InvalidOperationException("Forbidden endpoint in R056 fixture: " + request.Method + " " + uri.AbsolutePath);
        Writes++;
        if (WriteFailure is { } rejected) return new(rejected);
        if (downloading) Tracks.Add(Track(Tracks.Max(track => track.Index) + 1, "downloaded-" + Writes));
        else Tracks.RemoveAll(track => track.Index == int.Parse(uri.Segments[^1], System.Globalization.CultureInfo.InvariantCulture));
        AppliedWrites++;
        WriteEntered.TrySetResult();
        if (WriteGate is { } gate) await gate.Task.WaitAsync(token).ConfigureAwait(false);
        if (LoseResponse) throw ResponseLoss;
        return downloading ? Json(new SubtitleDownloadResult { NewIndex = Tracks[^1].Index }) : new(HttpStatusCode.NoContent);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };
}
