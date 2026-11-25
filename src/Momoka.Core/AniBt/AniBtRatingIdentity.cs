using System.Globalization;
using Momoka.Emby;

namespace Momoka.AniBt;

internal static class AniBtRatingIdentity
{
    internal static long? For(EmbyItem item)
    {
        if (item.Type is not (EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season)) return null;

        // A season must carry its own subject ID. The parent and the display title cannot prove it.
        long? subject = null;
        foreach (var (key, value) in item.ProviderIds)
        {
            if (!key.Equals("Bangumi", StringComparison.OrdinalIgnoreCase)
                && !key.Equals("Bgm", StringComparison.OrdinalIgnoreCase)) continue;
            if (!long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || id is < 1 or > 10000000 || (subject is not null && subject != id)) return null;
            subject = id;
        }
        return subject;
    }
}
