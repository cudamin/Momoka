using Momoka.Emby;

namespace Momoka.Shell;

// Probe workload only: each type keeps the original deadline; the common acceptance tail runs once.
internal static class DetailCustomizationProbeCases
{
    internal static readonly string[] Types = [EmbyItemType.Series, EmbyItemType.Episode, EmbyItemType.Movie];

    internal static EmbyItem[] Select(IEnumerable<EmbyItem> items, string type)
    {
        if (!Types.Contains(type, StringComparer.Ordinal)) throw new InvalidOperationException("未知详情自定义探针类型");
        var selected = items.Where(item => item.Type == type).ToArray();
        if (selected.Length != 1) throw new InvalidOperationException("详情自定义探针缺少唯一类型样本");
        return selected;
    }

    internal static bool IncludesShared(string type) => type == EmbyItemType.Movie;
}
