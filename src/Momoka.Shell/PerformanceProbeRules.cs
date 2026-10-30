namespace Momoka.Shell;

/// <summary>Pure failure predicates shared by the actual WinUI performance probe and offline controls.</summary>
internal static class PerformanceProbeRules
{
    internal static void ReleasedSearch(int remainingSubscriptions, int results)
    {
        if (remainingSubscriptions != 0 || results != 0)
            throw new InvalidOperationException($"离页仍保留订阅或搜索结果：subscriptions={remainingSubscriptions}, results={results}");
    }

    internal static void MatchingReadCanceled(bool canceled, bool successorBusy, int successorRows)
    {
        if (!canceled || !successorBusy || successorRows != 0)
            throw new InvalidOperationException("关闭没有取消只读搜索，或旧回调覆盖了新访问的读取中状态");
    }

    internal static void OneLogWake(int posts, int callbacks, int rows, int capacity, bool hasLatest)
    {
        if (posts != 1 || callbacks != 1 || rows != capacity || !hasLatest)
            throw new InvalidOperationException($"日志洪峰没有合并/追到最新尾部：posts={posts}, callbacks={callbacks}, rows={rows}, capacity={capacity}, latest={hasLatest}");
    }
}
