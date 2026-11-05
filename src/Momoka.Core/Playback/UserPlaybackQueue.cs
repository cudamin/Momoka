using System.Collections.ObjectModel;
using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>An explicit play-all/shuffle order; unlike a season's navigation list it must not be refilled.</summary>
public sealed class UserPlaybackQueue(IEnumerable<EmbyItem> items) : ReadOnlyCollection<EmbyItem>(items.ToArray());
