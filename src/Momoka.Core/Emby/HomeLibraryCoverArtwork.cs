using Momoka.Configuration;

namespace Momoka.Emby;

/// <summary>从主页已有的该库条目选图；只生成显示引用，不修改库 DTO 或增加目录查询。</summary>
public static class HomeLibraryCoverArtwork
{
    public static IReadOnlyList<ArtworkRef> Select(EmbyItem library, IEnumerable<EmbyItem> samples, string preset)
    {
        preset = HomeLibraryCoverPresets.Normalize(preset);
        if (preset == HomeLibraryCoverPresets.Gradient) return [];
        var limit = preset switch
        {
            HomeLibraryCoverPresets.Wall => 9,
            HomeLibraryCoverPresets.Stack => 3,
            _ => 1
        };
        var result = new List<ArtworkRef>(limit);
        var seen = new HashSet<ArtworkRef>();
        var order = new[] { EmbyImageStore.Primary, EmbyImageStore.Thumb, EmbyImageStore.Backdrop };

        foreach (var item in samples.Where(item => SameSource(library, item)))
        {
            var picture = First(item);
            if (picture is { } artwork && seen.Add(artwork)) result.Add(artwork);
            if (result.Count == limit) return result;
        }
        // 飞牛库已有海报路径；其他服务已给出的库图片也能在近期排被隐藏时兜底。
        foreach (var path in library.PosterPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var artwork = new ArtworkRef(library.Id, EmbyImageStore.Primary, path);
            if (seen.Add(artwork)) result.Add(artwork);
            if (result.Count == limit) return result;
        }
        if (First(library) is { } fallback && seen.Add(fallback)) result.Add(fallback);
        return result;

        ArtworkRef? First(EmbyItem item)
        {
            foreach (var type in order)
                if (EmbyImageStore.TagFor(item, type) is { } tag && !string.IsNullOrWhiteSpace(tag)
                    && EmbyImageStore.ImageItemId(item, type) is { } id && !string.IsNullOrWhiteSpace(id))
                    return new(id, type, tag);
            return null;
        }
    }

    private static bool SameSource(EmbyItem library, EmbyItem item) => library.Source is { } source
        ? item.Source is { } other && source.Key == other.Key
            && ReferenceEquals(source.UserStateOwner, other.UserStateOwner)
        : item.Source is null;

    /// <summary>不使用随机数或运行时字符串哈希，重启后同一库仍用同一组渐变。</summary>
    public static int PaletteIndex(string identity)
    {
        uint hash = 2166136261;
        foreach (var character in identity) hash = unchecked((hash ^ character) * 16777619);
        return (int)(hash % 5);
    }
}
