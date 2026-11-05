using System.Globalization;

namespace Momoka.Emby;

/// <summary>Card-only cleanup of the Jellyfin names confirmed in the library screenshots.</summary>
public static class CardDisplayTitle
{
    public static string Clean(string title, int? productionYear)
    {
        var result = title;
        const string marker = "{tmdbid=";
        var start = result.LastIndexOf(marker, StringComparison.Ordinal);
        if (start > 0 && result.EndsWith('}'))
        {
            var digits = result.AsSpan(start + marker.Length, result.Length - start - marker.Length - 1);
            if (!digits.IsEmpty && digits.IndexOfAnyExceptInRange('0', '9') < 0
                && result[..start].TrimEnd() is { Length: > 0 } name)
                result = name;
        }

        if (productionYear is >= 1000 and <= 9999)
        {
            var suffix = "(" + productionYear.Value.ToString(CultureInfo.InvariantCulture) + ")";
            if (result.EndsWith(suffix, StringComparison.Ordinal)
                && result[..^suffix.Length].TrimEnd() is { Length: > 0 } name)
                result = name;
        }
        return result;
    }
}
