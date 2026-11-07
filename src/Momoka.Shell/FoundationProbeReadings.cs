namespace Momoka.Shell;

// Anonymous probe readings only. These guards do not decide product behavior.
internal static class FoundationProbeReadings
{
    internal static readonly string[] CarouselLabels = ["轮播图用什么媒体", "轮播图条目来源", "轮播图张数", "封面轮换秒数"];

    internal static void Search(string expected, string input, string request, int source, bool current,
        IEnumerable<string> cards, IEnumerable<string> expectedCards)
    {
        Check(input == expected && request == expected && source == 0 && current,
            "当前搜索输入、导航参数、来源或查询代次不一致");
        Check(cards.SequenceEqual(expectedCards, StringComparer.Ordinal), "当前搜索分页混入旧项或漏项");
    }

    internal static void Home(IEnumerable<string> expected, IEnumerable<string> actual, bool canOpen, bool offer, bool buttonVisible)
    {
        Check(actual.SequenceEqual(expected, StringComparer.Ordinal), "返回主页未使用本次有效目录");
        Check(!canOpen && !offer && !buttonVisible, "固定媒体库标题仍保留导航入口");
    }

    internal static void Clock(string expected, string actual, bool multiple, bool running, double progress)
    {
        Check(expected == actual, "张数边界换掉了当前幻灯片");
        Check(running == multiple && double.IsFinite(progress) && (multiple ? progress > 0 : progress == 0),
            "张数边界未正确启停计时与进度");
    }

    internal static void Carousel(bool enabled, IEnumerable<string> actualLabels)
    {
        string[] expected = enabled ? ["显示主页轮播大图", .. CarouselLabels] : ["显示主页轮播大图"];
        Check(actualLabels.SequenceEqual(expected, StringComparer.Ordinal), "轮播开关未收起或恢复完整四个从属行");
    }

    internal static void Dwell(long started, long actualStarted, long now, bool samePicture, double progress, long dwellMilliseconds)
    {
        Check(started == actualStarted && now >= started && now - started < dwellMilliseconds,
            "轮播样本不属于当前停留周期或已超过其时长");
        Check(samePicture && double.IsFinite(progress) && progress is >= 0 and < 1,
            "停留时长没到就换片或进度失效");
    }

    internal static void Directory(int expectedShelves, int loadedShelves, double scrollableHeight) =>
        Check(expectedShelves > 1 && loadedShelves == expectedShelves && double.IsFinite(scrollableHeight) && scrollableHeight > 0,
            "主页滚动恢复夹具缺少可滚动的有效货架");

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
