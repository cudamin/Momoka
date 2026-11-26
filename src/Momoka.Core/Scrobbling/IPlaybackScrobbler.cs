using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Scrobbling;

/// <summary>Captures the account and settings belonging to one accepted playback.</summary>
public interface IPlaybackScrobbler
{
    PlaybackScrobblingAttempt? CreateAttempt(PlaybackTicket ticket, EmbySessionScope scope, PlaybackRequest request);
}
