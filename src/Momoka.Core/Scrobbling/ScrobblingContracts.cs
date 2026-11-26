namespace Momoka.Scrobbling;

public enum ScrobblingProvider { Bangumi, Douban }
public enum ScrobblingConnectionState { Unchecked, Checking, Connected, MissingCredential, SignedOut, Failed }
public enum ScrobblingRecordOutcome { Watching, Watched, Skipped, Failed }

public sealed record ScrobblingSubject(long Id, string Title, int? EpisodeCount);
public sealed record ScrobblingCandidate(long Id, string Title, int? Year, int? EpisodeCount, string? Detail = null);
public sealed record BangumiEpisode(long Id, int Number);
public sealed record ScrobblingRecord(DateTimeOffset At, ScrobblingProvider Provider, string Title,
    int? EpisodeNumber, ScrobblingRecordOutcome Outcome, string Detail);

/// <summary>Douban requests remain inside its trusted WebView session; no cookies cross this boundary.</summary>
public interface IDoubanScrobblingClient
{
    Task<bool> CheckLoginAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string query, CancellationToken cancellationToken);
    Task<ScrobblingSubject> SubjectAsync(long id, CancellationToken cancellationToken);
    Task MarkAsync(long id, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken);

    async Task<bool> MarkEpisodeAsync(long id, int episodeNumber, bool completed, Func<bool> stillAllowed, CancellationToken token)
    {
        var subject = await SubjectAsync(id, token).ConfigureAwait(false);
        if (episodeNumber < 1 || subject.EpisodeCount is { } count && episodeNumber > count)
            throw new InvalidOperationException("豆瓣条目集号不匹配");
        var finished = completed && subject.EpisodeCount is > 0 && episodeNumber == subject.EpisodeCount;
        await MarkAsync(id, finished, stillAllowed, token).ConfigureAwait(false);
        return finished;
    }
}
