using System.Collections.ObjectModel;
using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>An explicit play-all/shuffle order; unlike a season's navigation list it must not be refilled.</summary>
public sealed class UserPlaybackQueue(IEnumerable<EmbyItem> items) : ReadOnlyCollection<EmbyItem>(items.ToArray())
{
    /// <summary>The active slot disambiguates versions that also occur elsewhere in this queue.</summary>
    public int CurrentIndex { get; init; } = -1;

    public int IndexOfCurrent(string identityKey) => CurrentIndex >= 0 && CurrentIndex < Count
        && this[CurrentIndex].IdentityKey == identityKey ? CurrentIndex : EpisodeNavigation.IndexOf(this, identityKey);

    public UserPlaybackQueue Rebind(IEnumerable<EmbyItem> items) => new(items) { CurrentIndex = CurrentIndex };

    public UserPlaybackQueue ReplaceCurrent(string identityKey, EmbyItem replacement)
    {
        var index = IndexOfCurrent(identityKey);
        if (index < 0) return this;
        var items = this.ToArray();
        items[index] = replacement;
        return new(items) { CurrentIndex = index };
    }
}
