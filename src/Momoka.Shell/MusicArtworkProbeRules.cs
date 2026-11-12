namespace Momoka.Shell;

/// <summary>音乐图片探针的可复用断言；缓存条目不算CardItem当前持图。</summary>
internal static class MusicArtworkProbeRules
{
    // 当前1100x760基线首尾最高26个layout项；保留8项波动用于平台缓存和第一次量测，
    // 总元数据放大到10000不得增加这个上限。池里不属于realized的图片需求容许值是0。
    internal const int MaxRealizedAtProbeSize = 34;

    internal static void Demands(int total, int realized, int images, int held, int wanted, int liveHolders)
    {
        if (total < 1 || realized < 1 || realized > Math.Min(total, MaxRealizedAtProbeSize)
            || images < 0 || held < 0 || wanted < 0 || liveHolders < 0
            || images > realized || held > realized || wanted > realized || liveHolders > realized)
            throw new InvalidOperationException($"音乐图片需求超出当前layout：total={total}, realized={realized}, "
                + $"images={images}, held={held}, wanted={wanted}, holders={liveHolders}");
    }

    internal static void Viewport(double hostHeight, double viewportHeight)
    {
        if (!double.IsFinite(hostHeight) || !double.IsFinite(viewportHeight) || hostHeight <= 0
            || viewportHeight <= 0 || Math.Abs(hostHeight - viewportHeight) > 1)
            throw new InvalidOperationException($"音乐墙视口不是有限宿主：host={hostHeight}, viewport={viewportHeight}");
    }

    internal static void Item(string expected, string actual, bool focused, double top, double height, double viewportHeight)
    {
        if (expected != actual || !focused || !double.IsFinite(top) || !double.IsFinite(height) || height <= 0
            || top + height <= 0 || top >= viewportHeight)
            throw new InvalidOperationException($"音乐墙首/末项未可见聚焦：expected={expected}, actual={actual}, "
                + $"focused={focused}, bounds={top}/{height}, viewport={viewportHeight}");
    }
}
