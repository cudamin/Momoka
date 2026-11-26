using System.IO.Compression;

namespace Momoka.Scrobbling;

/// <summary>Offline reverse index generated from the pinned open-ani/bangumi-tmdb release.</summary>
internal static class BangumiTmdbMap
{
    private const string ResourceName = "Momoka.Scrobbling.BangumiTmdbMap.bin.gz";
    private const int FormatMagic = 0x314D5442; // BTM1, little-endian
    private const int MaximumRows = 250_000;
    private static readonly Lazy<Row[]> Rows = new(Load);

    internal static (long SubjectId, long EpisodeId)? Find(int tmdbId, int season, int episode)
    {
        if (tmdbId <= 0 || season < 0 || episode <= 0) return null;
        return Lookup(tmdbId, season, episode) is { } row ? (row.SubjectId, row.EpisodeId) : null;
    }

    internal static bool ContainsSeries(int tmdbId) => tmdbId > 0 && Lookup(tmdbId, 0, 0) is not null;

    private static Row? Lookup(int tmdbId, int season, int episode)
    {
        var rows = Rows.Value;
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var row = rows[middle];
            var comparison = row.CompareKey(tmdbId, season, episode);
            if (comparison == 0) return row;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return null;
    }

    private static Row[] Load()
    {
        using var resource = typeof(BangumiTmdbMap).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("缺少打格子映射资源");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        if (reader.ReadInt32() != FormatMagic) throw InvalidMap();
        var count = reader.ReadInt32();
        if (count is <= 0 or > MaximumRows) throw InvalidMap();

        var rows = new Row[count];
        var currentSeries = 0;
        for (var index = 0; index < count; index++)
        {
            var row = new Row(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            if (row.TmdbId <= 0 || row.Season < 0 || row.Episode < 0
                || index > 0 && rows[index - 1].CompareKey(row.TmdbId, row.Season, row.Episode) >= 0) throw InvalidMap();
            if (row.Episode == 0)
            {
                if (row.Season != 0 || row.SubjectId != 0 || row.EpisodeId != 0) throw InvalidMap();
                currentSeries = row.TmdbId;
            }
            else if (row.TmdbId != currentSeries || row.SubjectId <= 0 || row.EpisodeId <= 0) throw InvalidMap();
            rows[index] = row;
        }
        if (gzip.ReadByte() != -1) throw InvalidMap();
        return rows;
    }

    private static InvalidDataException InvalidMap() => new("打格子映射资源格式无效");

    private readonly record struct Row(int TmdbId, int Season, int Episode, int SubjectId, int EpisodeId)
    {
        internal int CompareKey(int tmdbId, int season, int episode)
        {
            var idOrder = TmdbId.CompareTo(tmdbId);
            if (idOrder != 0) return idOrder;
            var seasonOrder = Season.CompareTo(season);
            return seasonOrder != 0 ? seasonOrder : Episode.CompareTo(episode);
        }
    }
}
