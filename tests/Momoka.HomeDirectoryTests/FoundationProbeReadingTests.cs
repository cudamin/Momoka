using Momoka.Shell;

namespace Momoka.Tests;

internal static class FoundationProbeReadingTests
{
    internal static void Register(Action<string, Func<Task>> test)
    {
        test("匿名 UI 读数接受当前来源、目录、时钟与设置", () =>
        {
            FoundationProbeReadings.Search("fresh", "fresh", "fresh", 0, true, ["fresh0", "fresh1"], ["fresh0", "fresh1"]);
            FoundationProbeReadings.Home(["library"], ["library"], false, false, false);
            FoundationProbeReadings.Clock("first", "first", true, true, 0.2);
            FoundationProbeReadings.Clock("first", "first", false, false, 0);
            FoundationProbeReadings.Carousel(false, ["显示主页轮播大图"]);
            FoundationProbeReadings.Carousel(true, ["显示主页轮播大图", .. FoundationProbeReadings.CarouselLabels]);
            return Task.CompletedTask;
        });
        Reject(test, "旧搜索回填不能算当前输入通过", () => FoundationProbeReadings.Search("fresh", "fresh", "old", 0, true, ["old0"], ["fresh0"]));
        Reject(test, "当前搜索来源丢失不能算通过", () => FoundationProbeReadings.Search("fresh", "fresh", "fresh", 2, true, ["fresh0"], ["fresh0"]));
        Reject(test, "被取消的搜索代次不能算通过", () => FoundationProbeReadings.Search("fresh", "fresh", "fresh", 0, false, ["fresh0"], ["fresh0"]));
        Reject(test, "搜索分页漏项不能算通过", () => FoundationProbeReadings.Search("fresh", "fresh", "fresh", 0, true, ["fresh1"], ["fresh0", "fresh1"]));
        Reject(test, "历史主页空目录不能算通过", () => FoundationProbeReadings.Home(["library"], [], false, false, false));
        Reject(test, "固定媒体库标题保留入口不能算通过", () => FoundationProbeReadings.Home(["library"], ["library"], true, true, true));
        Reject(test, "增加第二张仍停钟不能算通过", () => FoundationProbeReadings.Clock("first", "first", true, false, 0));
        Reject(test, "只剩单张仍计时不能算通过", () => FoundationProbeReadings.Clock("first", "first", false, true, 0.2));
        Reject(test, "边界换掉当前幻灯片不能算通过", () => FoundationProbeReadings.Clock("first", "second", true, true, 0.2));
        Reject(test, "轮播关闭仍留从属行不能算通过", () => FoundationProbeReadings.Carousel(false, ["显示主页轮播大图", "轮播图张数"]));
        Reject(test, "轮播恢复缺从属行不能算通过", () => FoundationProbeReadings.Carousel(true, ["显示主页轮播大图"]));
        test("实际轮播周期与足够纵向货架接受检查", () =>
        {
            FoundationProbeReadings.Dwell(1000, 1000, 2200, true, 0.4, 3000);
            FoundationProbeReadings.Directory(4, 4, 180);
            return Task.CompletedTask;
        });
        Reject(test, "像素采样后的旧计时起点不能算三秒通过", () => FoundationProbeReadings.Dwell(1000, 800, 2200, true, 0.4, 3000));
        Reject(test, "超过三秒的样本不能冒充换片提前", () => FoundationProbeReadings.Dwell(1000, 1000, 4000, true, 0.9, 3000));
        Reject(test, "三秒前换片必须拒绝", () => FoundationProbeReadings.Dwell(1000, 1000, 2200, false, 0.4, 3000));
        test("轮播采样窗口2999毫秒属于周期内，3000毫秒只结束采样", () =>
        {
            Assert.True(DwellSample(1000, 1000, 3999, true, 0.9, 3000));
            Assert.False(DwellSample(1000, 4000, 4000, false, 0, 3000));
            return Task.CompletedTask;
        });
        Reject(test, "周期内改变起点不得被采样窗口接受", () => DwellSample(1000, 1100, 2200, true, 0.4, 3000));
        Reject(test, "周期内提前换图不得被采样窗口接受", () => DwellSample(1000, 1000, 2200, false, 0.4, 3000));
        Reject(test, "迟到观察仍不得掩盖三秒前重启的周期", () => DwellSample(1000, 1100, 4100, false, 0.4, 3000));
        test("足量连续自然停留样本可结束检查", () =>
        {
            DwellProgress([0.02, 0.04, 0.06, 0.08, 0.1, 0.12, 0.14, 0.16], 8);
            return Task.CompletedTask;
        });
        Reject(test, "采样跨周期且有效样本不足必须失败", () => DwellProgress([0.1, 0.2], 8));
        Reject(test, "没有有效轮播样本必须失败", () => DwellProgress([], 8));
        Reject(test, "足量冻结样本不能冒充读秒前进", () => DwellProgress([0, 0, 0, 0, 0, 0, 0, 0], 8));
        Reject(test, "足量倒退样本不能冒充连续读秒", () => DwellProgress([0.02, 0.04, 0.06, 0.08, 0.07, 0.12, 0.14, 0.16], 8));
        Reject(test, "满格或非有限样本不能算停留期间进度", () => DwellProgress([0.1, double.NaN, 0.3, 0.4, 0.5, 0.6, 0.7, 1], 8));
        test("三秒检查接受本次第一个目标周期", () =>
        {
            DwellAdvance("first", "second", "second", 1000, 4000, 3000);
            return Task.CompletedTask;
        });
        Reject(test, "采样错过第二张不得等后轮回来冒充本次翻页", () => DwellAdvance("first", "second", "third", 1000, 7000, 3000));
        Reject(test, "转了一圈回首张不得算本次三秒翻页", () => DwellAdvance("first", "second", "first", 1000, 10000, 3000));
        Reject(test, "第一个目标周期提前开始仍必须失败", () => DwellAdvance("first", "second", "second", 1000, 3999, 3000));
        Reject(test, "目标仍是第二张也不能接受已过首轮窗口", () => DwellAdvance("first", "second", "second", 1000, 7000, 3000));
        Reject(test, "下一轮第二张不能冒充首个目标周期", () => DwellAdvance("first", "second", "second", 1000, 13000, 3000));
        Reject(test, "一排水平二十四卡不能充当可滚动主页", () => FoundationProbeReadings.Directory(1, 1, 0));
        Reject(test, "缺失库货架不能靠空白垫高通过", () => FoundationProbeReadings.Directory(4, 1, 180));
    }

    private static bool DwellSample(long started, long actualStarted, long now, bool samePicture, double progress, long milliseconds) =>
        FoundationProbeReadings.DwellSample(started, actualStarted, now, samePicture, progress, milliseconds);

    private static void DwellProgress(double[] samples, int required) => FoundationProbeReadings.DwellProgress(samples, required);

    private static void DwellAdvance(string first, string expected, string actual, long started, long actualStarted, long milliseconds)
        => FoundationProbeReadings.DwellAdvance(first, expected, actual, started, actualStarted, milliseconds);

    private static void Reject(Action<string, Func<Task>> test, string name, Action reading) => test(name, () =>
    {
        try { reading(); }
        catch (InvalidOperationException) { return Task.CompletedTask; }
        throw new InvalidOperationException("错误 UI 读数被接受");
    });
}
