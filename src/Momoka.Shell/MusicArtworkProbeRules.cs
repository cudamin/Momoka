namespace Momoka.Shell;

/// <summary>音乐图片探针的可复用断言；缓存条目不算CardItem当前持图。</summary>
internal static class MusicArtworkProbeRules
{
    // 当前1100x760基线首尾最高26个layout项；保留8项波动用于平台缓存和第一次量测，
    // 总元数据放大到10000不得增加这个上限。池里不属于realized的图片需求容许值是0。
    internal const int MaxRealizedAtProbeSize = 34;

    internal static void Demands(int total, int realized, int images, int held, int wanted, int liveHolders, int maxRealized = MaxRealizedAtProbeSize)
    {
        if (total < 1 || realized < 1 || maxRealized < 1 || realized > Math.Min(total, maxRealized)
            || images < 0 || held < 0 || wanted < 0 || liveHolders < 0
            || images > realized || held > realized || wanted > realized || liveHolders > realized)
            throw new InvalidOperationException($"音乐图片需求超出当前layout：total={total}, realized={realized}, "
                + $"images={images}, held={held}, wanted={wanted}, holders={liveHolders}");
    }

    internal static int TrackRealizationBudget(double viewport, double rowHeight, double cacheLength)
    {
        if (!double.IsFinite(viewport) || !double.IsFinite(rowHeight) || !double.IsFinite(cacheLength)
            || viewport <= 0 || rowHeight <= 0 || cacheLength < 0)
            throw new InvalidOperationException("曲目缓存预算缺少有效viewport/rowHeight/cacheLength");
        // 原生ItemsStackPanel缓存总长cacheLength*viewport，两边各一半；再加viewport本身。
        // 两个边缘相交行与一个可聚焦pin是明确容许，不按失败时的38更新快照。
        return checked((int)Math.Ceiling(viewport * (1 + cacheLength) / rowHeight) + 3);
    }

    internal static void EndOffset(double requested, double currentMax, double actual, bool tail)
    {
        if (!double.IsFinite(requested) || !double.IsFinite(currentMax) || !double.IsFinite(actual) || currentMax < 0
            || Math.Abs(actual - Math.Clamp(requested, 0, currentMax)) >= 1
            || tail && Math.Abs(actual - currentMax) >= 1)
            throw new InvalidOperationException($"音乐端点滚动未完成：requested={requested}, currentMax={currentMax}, actual={actual}, tail={tail}");
    }

    internal static void HeadSample(bool correlationCompleted, double actualOffset, string expectedItem, string actualItem,
        double top, double height, double viewportHeight)
    {
        if (!correlationCompleted || !double.IsFinite(actualOffset) || Math.Abs(actualOffset) >= 1
            || string.IsNullOrWhiteSpace(expectedItem) || expectedItem != actualItem
            || !double.IsFinite(top) || !double.IsFinite(height) || height <= 0
            || !double.IsFinite(viewportHeight) || viewportHeight <= 0
            || top + height <= 0 || top >= viewportHeight)
            throw new InvalidOperationException("音乐首屏样本未完成回首项或沿用前组锚点");
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
