using System.Globalization;
using System.Text.Json;

namespace Momoka.AniBt;

public sealed partial class AniBtClient
{
    internal async Task<decimal?> ReferenceRatingAsync(long bgmId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bgmId is < 1 or > 10000000) throw new ArgumentOutOfRangeException(nameof(bgmId));

        const string path = "api/seasons/anime?bgmId=";
        using var document = ParseJson(await RequestAsync(path + bgmId.ToString(CultureInfo.InvariantCulture),
            null, false, cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        var data = Field(root, "data");
        var scope = Field(data, "scope");
        var groups = Field(data, "byWeekday");
        if (Field(root, "ok").ValueKind != JsonValueKind.True || Text(scope, "kind") != "bgmId"
            || Number(scope, "bgmId") != bgmId || Number(data, "total") != 1
            || Field(data, "truncated").ValueKind != JsonValueKind.False || groups.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement found = default;
        foreach (var group in groups.EnumerateArray())
        {
            var groupItems = Field(group, "animes");
            if (groupItems.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in groupItems.EnumerateArray())
            {
                // The exact scope echoes the requested subject. An upstream merge may give the single result a new ID.
                if (found.ValueKind != JsonValueKind.Undefined || Number(item, "bgmId") is not (>= 1 and <= 10000000)
                    || Text(item, "animeId").Length == 0) return null;
                found = item;
            }
        }

        // AniBT uses 5.0 both for missing scores and for a genuine 5.0. Zero is a valid score.
        var rating = Field(found, "rating");
        return rating.ValueKind == JsonValueKind.Number && rating.TryGetDecimal(out var score)
            && score is >= 0 and <= 10 and not 5 ? score : null;
    }
}
