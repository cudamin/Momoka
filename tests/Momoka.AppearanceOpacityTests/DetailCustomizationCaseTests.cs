using Momoka.Emby;
using Momoka.Shell;

internal static class DetailCustomizationCaseTests
{
    internal static void Run(Action<string, Action> check)
    {
        check("详情自定义分区完整保留三类型并仅电影执行公共尾部", () =>
        {
            var expected = new[] { EmbyItemType.Series, EmbyItemType.Episode, EmbyItemType.Movie };
            if (!DetailCustomizationProbeCases.Types.SequenceEqual(expected)) throw new InvalidOperationException("类型集合丢失或重排");
            var items = expected.Select(type => new EmbyItem { Type = type }).ToArray();
            foreach (var type in expected)
            {
                var selected = DetailCustomizationProbeCases.Select(items, type).ToArray();
                if (selected.Length != 1 || selected[0].Type != type) throw new InvalidOperationException("跨类型检查或漏类型");
                if (DetailCustomizationProbeCases.IncludesShared(type) != (type == EmbyItemType.Movie)) throw new InvalidOperationException("公共尾部不只运行一次");
            }
        });
        check("详情自定义分区拒绝未知空类型及缺失样本", () =>
        {
            foreach (var type in new[] { null!, "", "unknown", EmbyItemType.Season })
                Reject(() => DetailCustomizationProbeCases.Select([], type));
            Reject(() => DetailCustomizationProbeCases.Select([], EmbyItemType.Movie));
            Reject(() => DetailCustomizationProbeCases.Select(
                [new EmbyItem { Type = EmbyItemType.Movie }, new EmbyItem { Type = EmbyItemType.Movie }], EmbyItemType.Movie));
        });
        check("阅读前景就绪需要队列排空、实际颜色及至少一个可见读数", () =>
        {
            if (!DetailCustomizationProbeCases.Ready(false, true, true, [true, true])) throw new InvalidOperationException("真实就绪被拒绝");
            foreach (var state in new[]
            {
                DetailCustomizationProbeCases.Ready(true, true, true, [true]),
                DetailCustomizationProbeCases.Ready(false, false, true, [true]),
                DetailCustomizationProbeCases.Ready(false, true, false, [true]),
                DetailCustomizationProbeCases.Ready(false, true, true, [false]),
                DetailCustomizationProbeCases.Ready(false, true, true, [])
            })
                if (state) throw new InvalidOperationException("旧队列、旧图、空控件或错色被当作就绪");
        });
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("缺失/未知类型被当成成功");
    }
}
