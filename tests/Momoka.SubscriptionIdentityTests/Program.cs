using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

var observations = new List<object>();
var failures = new List<string>();
foreach (var changeKind in new[] { "season", "group", "keyword-control" })
{
    using var handler = new AnonymousSubscriptions();
    using var client = new MoviePilotClient(handler);
    var settings = new AppSettings();
    settings.MoviePilot.Enabled = true;
    settings.MoviePilot.Url = "http://round6-subscription.invalid:3001";
    settings.MoviePilot.Username = "anonymous-review";
    var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
    credentials.SetPassword(settings.MoviePilot, "placeholder");
    using var service = new MoviePilotService(client, credentials, settings);
    var original = (await service.SubscriptionsAsync(default)).Single();
    using var files = new MoviePilotSubscriptionsViewModel();
    files.Attach(service, original);
    await files.ReloadAsync();
    var card = files.Selected ?? throw new InvalidOperationException("Missing actual card");
    var media = new MoviePilotMediaViewModel();
    media.Seed(original, service);
    media.Seasons.Add(new(new(1, "First", 1, "", "", null), original, _ => { }, _ => { }));
    media.Seasons.Add(new(new(2, "Second", 1, "", "", null), null, _ => { }, _ => { }));
    var view = new MoviePilotMediaSeasonsView();
    view.Attach(media, files);
    if (media.Seasons.Any(row => row.Expanded)) failures.Add("season headers must start collapsed");
    media.Seasons[0].Expanded = true;
    view.Attach(media, files);
    if (!media.Seasons[0].Expanded) failures.Add("reattach changed manual expansion");
    media.Seasons[0].Expanded = false;
    view.Attach(media, files);
    if (media.Seasons.Any(row => row.Expanded)) failures.Add("reattach reopened a manually collapsed season");
    var before = HostSeason(media);
    var notifications = new List<string?>();
    files.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
    await files.EditAsync(card, async (subscription, save, remove) =>
    {
        var draft = MoviePilotSubscriptionEdit.From(subscription);
        draft = changeKind switch
        {
            "season" => draft with { Season = 2 },
            "group" => draft with { EpisodeGroup = "new-group" },
            _ => draft with { Keyword = "new keyword" }
        };
        await save(draft);
    });
    var selected = files.Selected?.Subscription ?? throw new InvalidOperationException("Missing reloaded subscription");
    var hostAfterEdit = HostSeason(media);
    if (media.Seasons.Any(row => row.Expanded)) failures.Add("identity update expanded a season");
    var visibleEpisodeTitle = media.Seasons.FirstOrDefault(row => row.Files is not null)?.FilesEpisodes?.Single().RowTitle;
    var expectedHost = changeKind == "season" ? 2 : changeKind == "group" ? (int?)null : 1;
    if (before != 1 || !ReferenceEquals(card, files.Selected) || handler.Writes != 1)
        throw new InvalidOperationException("Actual edit path did not execute");
    if (hostAfterEdit != expectedHost) failures.Add($"{changeKind}: expected host {expectedHost}, actual {hostAfterEdit}");
    if (notifications.Contains("SelectedIdentity") != (changeKind != "keyword-control"))
        failures.Add(changeKind + ": incorrect identity change notification");
    if (changeKind == "season" && visibleEpisodeTitle?.Contains("Season 2", StringComparison.Ordinal) != true)
        failures.Add("new season does not own its actual files");
    if (changeKind == "group" && media.Seasons.Any(row => row.FilesEpisodes is not null))
        failures.Add("old group retained file collection");
    // Calling the production Attach path again is a positive control for the existing identity predicate.
    view.Attach(media, files);
    var hostAfterReattach = HostSeason(media);
    if (hostAfterReattach != expectedHost) throw new InvalidOperationException("Reattach control failed");
    observations.Add(new
    {
        changeKind,
        selectedSeason = selected.Media.Season,
        selectedGroup = selected.EpisodeGroup,
        mediaGroup = media.GroupId,
        hostBeforeEdit = before,
        hostAfterEdit,
        expectedHost,
        visibleEpisodeTitle,
        sameSelectedCard = ReferenceEquals(card, files.Selected),
        selectedNotification = notifications.Contains("Selected"),
        hostAfterReattach,
        handler.Writes
    });
    media.SyncSubscriptionIdentity(selected);
    if (media.OldGroupCancelled != (changeKind != "keyword-control")) failures.Add("old group request ownership was not preserved");
    if (media.GroupId != selected.EpisodeGroup || media.Reloads != (changeKind == "keyword-control" ? 0 : 1))
        failures.Add(changeKind + ": media group/season reload not synchronized");
    media.SyncSubscriptionIdentity(selected);
    if (media.Reloads > 1) failures.Add("unchanged identity redundantly reloads media");
    if (changeKind == "keyword-control")
    {
        var notices = new List<string?>();
        media.PropertyChanged += (_, args) => notices.Add(args.PropertyName);
        media.AvailabilityNotice(new object());
        if (notices.Count != 0) failures.Add("foreign availability sender notified current media");
        media.AvailabilityNotice(service);
        if (!notices.SequenceEqual(new[] { "AniBtVisibility", "CanSearchAniBt" }))
            failures.Add("actual availability handler did not notify both properties");
        notices.Clear();
        media.Live = false;
        media.AvailabilityNotice(service);
        if (notices.Count != 0) failures.Add("departed media accepted late availability notification");
        media.Live = true;
    }
    view.Attach(media, null);
    if (media.Seasons.Any(row => row.Files is not null || row.FilesEpisodes is not null))
        failures.Add("detach retained old file owner");
    files.Cancel();
}
Console.WriteLine(JsonSerializer.Serialize(new { production = "Actual subscription VM/card/episode row, actual MediaSeasonsView, verbatim media row source; inert WinUI and metadata owner", network = "none", observations }, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"3 identity edit cases; {failures.Count} failures");
Console.WriteLine("1 actual availability-handler ownership case completed (foreign/current/departed).");
if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));

static int? HostSeason(MoviePilotMediaViewModel media) => media.Seasons.SingleOrDefault(row => row.Files is not null)?.Season.Number;

internal sealed class AnonymousSubscriptions : HttpMessageHandler
{
    private readonly Dictionary<string, object?> _subscription = new()
    {
        ["id"] = 12,
        ["name"] = "Anonymous series",
        ["type"] = "电视剧",
        ["media_source"] = "themoviedb",
        ["media_id"] = "42",
        ["season"] = 1,
        ["episode_group"] = "",
        ["total_episode"] = 1,
        ["start_episode"] = 1,
        ["state"] = "R"
    };
    internal int Writes { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing URI");
        if (uri.Host != "round6-subscription.invalid") throw new InvalidOperationException("Only anonymous fixture URI is allowed");
        var path = uri.AbsolutePath.Replace("/api/v1/", "", StringComparison.Ordinal);
        if (path == "login/access-token") return Reply(new { access_token = "placeholder-token" });
        if (path == "subscribe/" && request.Method == HttpMethod.Put)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            foreach (var property in body.RootElement.EnumerateObject()) _subscription[property.Name] = property.Value.Clone();
            Writes++;
            return Reply(new { success = true });
        }
        if (path == "subscribe/") return Reply(new[] { _subscription });
        if (path == "subscribe/12") return Reply(_subscription);
        if (path == "subscribe/files/12") return Reply(new
        {
            subscribe = _subscription,
            episodes = new Dictionary<string, object> { ["1"] = new { title = $"Season {_subscription["season"]} group {_subscription["episode_group"]}", library = Array.Empty<object>(), download = Array.Empty<object>() } }
        });
        if (path.StartsWith("tmdb/", StringComparison.Ordinal)) return Reply(Array.Empty<object>());
        throw new InvalidOperationException("Unexpected anonymous route: " + request.Method + " " + path);
    }
    private static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
