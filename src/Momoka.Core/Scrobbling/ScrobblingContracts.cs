using Momoka.MoviePilot;

namespace Momoka.Scrobbling;

public enum ScrobblingProvider { Bangumi, Douban }
public enum ScrobblingConnectionState { Unchecked, Checking, Connected, MissingCredential, SignedOut, Failed }
public enum ScrobblingRecordOutcome { Watching, Watched, Skipped, Failed }

public sealed record ScrobblingSubject(long Id, string Title, int? EpisodeCount);
public sealed record ScrobblingCandidate(long Id, string Title, int? Year, int? EpisodeCount, string? Detail = null);
public sealed record BangumiEpisode(long Id, int Number);
public sealed record ScrobblingRecord(DateTimeOffset At, ScrobblingProvider Provider, string Title,
    int? EpisodeNumber, ScrobblingRecordOutcome Outcome, string Detail);

internal interface IScrobblingIdentityLookup
{
    string ScrobblingConnectionStamp { get; }
    bool IsCurrentScrobblingConnection(string stamp);
    Task<MoviePilotScrobblingMatch?> ResolveScrobblingEpisodeAsync(string title, string? originalTitle,
        int? year, IReadOnlyDictionary<string, string>? providerIds, int season, int episode,
        bool includeBangumi, bool includeDouban, CancellationToken cancellationToken, string expectedConnectionStamp);
}

/// <summary>Douban requests remain inside its trusted WebView session; no cookies cross this boundary.</summary>
public interface IDoubanScrobblingClient
{
    Task<bool> CheckLoginAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string query, CancellationToken cancellationToken);
    Task<ScrobblingSubject> SubjectAsync(long id, CancellationToken cancellationToken);
    Task MarkAsync(long id, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken);

    async Task<bool> MarkEpisodeAsync(long id, int episodeNumber, bool completed, Func<bool> stillAllowed, CancellationToken token)
    {
        if (episodeNumber < 1) throw new ArgumentOutOfRangeException(nameof(episodeNumber));
        if (completed && (await SubjectAsync(id, token).ConfigureAwait(false)).EpisodeCount != episodeNumber) return false;
        await MarkAsync(id, completed, stillAllowed, token).ConfigureAwait(false);
        return completed;
    }
}
