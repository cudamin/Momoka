using System.Net;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Tests;

// Only anonymous identities, event endpoints and a gated GET. No account/configuration API is used.
internal sealed class EntryFixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<CancellationTokenSource> _lifetimes = [];
    internal EmbySession Session { get; }
    internal EmbySessionScope A { get; }
    internal EmbySessionScope B { get; }
    internal EntryTransport Transport { get; } = new();
    internal EntryActions Actions { get; } = new();
    internal FrameworkElement Owner { get; } = new();
    internal CardItem Card { get; }
    internal MenuFlyout Menu { get; private set; } = null!;

    internal EntryFixture(string root, bool knownAnime = true, MediaServerKind kind = MediaServerKind.Emby)
    {
        var secrets = new NoSecrets();
        var device = DeviceIdentity.Create("r040-offline", "1");
        var paths = new AppPaths(Path.Combine(root, "artifacts", "r040", "no-settings-io"));
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Private, null,
            [new AppSettings { AggregateMode = true }, new SettingsStore(paths, secrets), new CredentialVault(secrets), device, Transport], null)!;
        A = AddSource("a", device, kind);
        B = AddSource("b", device, MediaServerKind.Emby);
        Select(A, notify: false);
        var item = A.Bind(new EmbyItem
        {
            Id = "episode",
            Name = "匿名动画",
            Type = EmbyItemType.Episode,
            SeriesId = "series",
            Genres = knownAnime ? ["动画"] : []
        });
        var images = new EmbyImageStore(Session, Path.Combine(root, "artifacts", "r040", "empty-image-cache"));
        Card = new CardItem(item, images, 170);
    }

    private EmbySessionScope AddSource(string name, DeviceIdentity device, MediaServerKind kind)
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("https://r040-" + name + ".invalid/emby/"), "",
            name + "-user", name + "-user", "offline-" + name, device)
        { ServerKind = kind };
        var type = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(type, Private | BindingFlags.Public, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = name }, new AccountProfile { Id = name }], null)!;
        _lifetimes.Add((CancellationTokenSource)type.GetProperty("Lifetime")!.GetValue(identity)!);
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Private, null, [Session, identity], null)!;
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(Session)!).Add(scope.Key, scope);
        return scope;
    }

    // Simulate the final pointer/event boundary only; SelectSource would persist settings and is forbidden here.
    internal void Select(EmbySessionScope scope, bool notify = true)
    {
        typeof(EmbySession).GetField("_current", Private)!.SetValue(Session, scope);
        if (notify) NotifySources();
    }

    internal void RevokeA(bool notify = true)
    {
        _lifetimes[0].Cancel();
        if (notify) NotifySources();
    }

    private void NotifySources() => typeof(EmbySession).GetMethod("NotifySourcesChanged", Private)!.Invoke(Session, null);
    internal int SourceListeners => (typeof(EmbySession).GetField("SourcesChanged", Private)!.GetValue(Session) as Delegate)?.GetInvocationList().Length ?? 0;
    internal MenuFlyout Build() => ItemCommands.Build(Session, Actions, Owner, Card);
    internal MenuFlyout Show()
    {
        MenuFlyout.ShownForFixture = menu => Menu = menu;
        try { ItemCommands.Show(Session, Actions, Owner, Card, position: null); }
        finally { MenuFlyout.ShownForFixture = null; }
        return Menu;
    }
    internal void DirectScrape() => ItemCommands.Run(ItemCommand.AniBtScrape, Session, Actions, Owner, Card);
    internal bool HasScrape(MenuFlyout menu) => menu.Items.Any(row => row.Tag is ItemCommand.AniBtScrape);
    internal static MenuFlyoutSubItem Search(MenuFlyout menu) => menu.Items.OfType<MenuFlyoutSubItem>().Single(row => row.Tag is ItemCommand.MoviePilotVersions);
    internal static void ClickSearch(MenuFlyout menu, ItemCommand command) => Search(menu).Items.OfType<MenuFlyoutItem>()
        .Single(row => row.Tag is ItemCommand value && value == command).ClickForFixture();

    public void Dispose()
    {
        if (Menu is { OpenForFixture: true }) Menu.Hide();
        Transport.Release();
        Session.Dispose();
        foreach (var lifetime in _lifetimes) lifetime.Dispose();
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string value) => throw new InvalidOperationException("R040禁止保存凭据");
        public string Unprotect(string value) => throw new InvalidOperationException("R040禁止读取凭据");
    }
}

internal sealed class EntryTransport : HttpMessageHandler
{
    private readonly TaskCompletionSource<HttpResponseMessage> _reply = new();
    internal TaskCompletionSource Started { get; } = new();
    internal int Reads { get; private set; }
    internal int Writes { get; private set; }
    internal int Unexpected { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.Method != HttpMethod.Get) Writes++;
        if (request.Method != HttpMethod.Get || request.RequestUri is not { Host: "r040-a.invalid", AbsolutePath: "/emby/Users/a-user/Items/episode" })
        {
            Unexpected++;
            throw new InvalidOperationException("R040仅允许匿名单集Genres GET，禁止登录、搜索或写入");
        }
        Reads++;
        Started.TrySetResult();
        // Intentionally ignore cancellation here: the actual scope/continuation must reject a late reply.
        return _reply.Task;
    }

    internal void Release(bool anime = true) => _reply.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(anime
            ? """{"Id":"episode","Name":"匿名动画","Type":"Episode","Genres":["动画"],"SeriesId":"series"}"""
            : """{"Id":"episode","Name":"非动画","Type":"Episode","Genres":["剧情"],"SeriesId":"series"}""")
    });
}

// Only the shell action endpoints are substituted. Rejected commands must not call even this boundary.
internal sealed class EntryActions : IShellActions
{
    internal int Scrapes { get; private set; }
    internal int MoviePilotSearches { get; private set; }
    internal int AniBtSearches { get; private set; }
    internal List<string> Notices { get; } = [];
    public bool MoviePilotEnabled { get; private set; } = true;
    public event EventHandler? MoviePilotAvailabilityChanged;
    internal void SetMoviePilot(bool available)
    {
        MoviePilotEnabled = available;
        MoviePilotAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ShowAniBtScrape(EmbySessionScope scope, EmbyItem item) => Scrapes++;
    public void SearchMoviePilotVersions(EmbyItem item) => MoviePilotSearches++;
    public void SearchAniBtVersions(EmbyItem item) => AniBtSearches++;
    public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) => Notices.Add(message);
    private static Exception Unrelated() => new InvalidOperationException("R040 reached unrelated shell operation");
    public void OpenItem(EmbyItem item) => throw Unrelated();
    public Task PlayAsync(EmbyItem item, EmbyItem? parent = null, PlaybackChoice? choice = null, IReadOnlyList<EmbyItem>? episodes = null) => throw Unrelated();
    public bool TryOpenLibrary(string id, EmbySessionScope? source = null) => throw Unrelated();
    public void OpenGenre(string genre, EmbySessionScope? source = null) => throw Unrelated();
    public void OpenRow(HomeLayout.HomeRowTarget target, EmbySessionScope? source = null) => throw Unrelated();
    public void OpenSignIn(ServerProfile? server = null, bool newAccount = false) => throw Unrelated();
    public Task SwitchProfileAsync(ServerProfile server, AccountProfile? account) => throw Unrelated();
    public void SetTitleStrip(TitleStrip strip) => throw Unrelated();
    public void ShowMoviePilotReorganize(Momoka.MoviePilot.MoviePilotTransferContext context) => throw Unrelated();
    public void ShowMissingEpisodes(EmbyItem item, IReadOnlyList<EmbyItem> missing) => throw Unrelated();
    public void ReportMarkedWatched(EmbyItem item) => throw Unrelated();
    public void RefreshFavorites(EmbyItem? changed = null) => throw Unrelated();
}
