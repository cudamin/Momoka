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
    }

    private static void Reject(Action<string, Func<Task>> test, string name, Action reading) => test(name, () =>
    {
        try { reading(); }
        catch (InvalidOperationException) { return Task.CompletedTask; }
        throw new InvalidOperationException("错误 UI 读数被接受");
    });
}
