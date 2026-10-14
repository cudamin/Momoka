using Momoka.Configuration;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 「开机自启」那一格写进 Windows 启动项的那一行，以及读回来的那一行算不算「会自启」。真正的注册表读写在外壳
/// （单元测试进不到那个程序集），所以这里钉的是外壳拿去做判断的那几个纯函数 —— 而它们每一个都有一个「写错了屏上
/// 什么也看不出来」的坏法：路径里带空格时少了引号，Windows 会从空格处截断去找一个不存在的程序，而设置页上那个
/// 开关照样是开的；程序换了目录之后还认那一格，报的就是「会自启」，起来的却是旧的那一份。
/// </summary>
internal static class StartupEntryTests
{
    public static void Register()
    {
        Test("开机自启：写进启动项的那一行带引号", () =>
        {
            Assert.Equal(@"""C:\Momoka\Momoka.exe""", StartupEntry.Command(@"C:\Momoka\Momoka.exe"));

            // 路径里有空格才是引号存在的理由：没有它 Windows 会从空格处截断。
            Assert.Equal(@"""C:\Program Files\Momoka\Momoka.exe""",
                StartupEntry.Command(@"C:\Program Files\Momoka\Momoka.exe"));

            // 手抄一遍进来的路径可能已经带着引号或者两头有空白，不要叠成两层引号。
            Assert.Equal(@"""C:\Momoka\Momoka.exe""", StartupEntry.Command(@"  ""C:\Momoka\Momoka.exe""  "));

            // 空的路径是编程错误，不是「静默写一条没用的值」—— 那一格写进去一个空串，登录时什么都不发生，
            // 而屏上那一行是开着的。
            Assert.Throws<ArgumentException>(() => StartupEntry.Command(""));
            Assert.Throws<ArgumentException>(() => StartupEntry.Command("   "));
            Assert.Throws<ArgumentException>(() => StartupEntry.Command("\"\""));
        });

        Test("开机自启：认得出那一行是不是本程序", () =>
        {
            const string exe = @"C:\Momoka\Momoka.exe";

            Assert.True(StartupEntry.Names(@"""C:\Momoka\Momoka.exe""", exe), "自己写进去的那一行");
            Assert.True(StartupEntry.Names(@"C:\Momoka\Momoka.exe", exe), "手改过、没带引号的也认");
            Assert.True(StartupEntry.Names(@"  ""C:\MOMOKA\momoka.EXE""  ", exe), "路径大小写不分、两头空白不算");

            // 后面拖着参数的照认：头一个词才是程序。手改过的那一格、别的程序留下的那一格都可能长这样。
            Assert.True(StartupEntry.Names(@"""C:\Momoka\Momoka.exe"" --maximized", exe), "带引号后面拖参数");
            Assert.True(StartupEntry.Names(@"C:\Momoka\Momoka.exe --maximized", exe), "不带引号后面拖参数");

            // 换过地方的程序不算 —— 这一格指着的仍是旧的那一份，这一次登录起来的不会是当前这个程序。
            Assert.False(StartupEntry.Names(@"""C:\Old\Momoka.exe""", exe), "旧目录里的那一份");
            Assert.False(StartupEntry.Names(@"""C:\Momoka\Momoka.exe.bak""", exe), "名字多一截不算");
            Assert.False(StartupEntry.Names(@"""C:\Momoka\Momo""", exe), "前缀不算");

            Assert.False(StartupEntry.Names(null, exe), "那一格不在");
            Assert.False(StartupEntry.Names("", exe), "那一格是空的");
            Assert.False(StartupEntry.Names(@"C:\Momoka\Momoka.exe", ""), "自己这一头没有路径时不认任何东西");
        });

        Test("开机自启：任务管理器关掉的要看另一份标记", () =>
        {
            const string exe = @"C:\Momoka\Momoka.exe";
            var mine = @"""C:\Momoka\Momoka.exe""";

            // 头一个字节 2 是开着、3 是关着，后面那 8 个字节是关掉它的时间。
            byte[] enabled = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            byte[] disabled = [3, 0, 0, 0, 0x40, 0x5A, 0xC0, 0x01, 0, 0, 0, 0];

            Assert.False(StartupEntry.TurnedOff(null), "整格不在就是没被关过");
            Assert.False(StartupEntry.TurnedOff([]), "空的那一份当作没被关过");
            Assert.False(StartupEntry.TurnedOff(enabled), "2 是开着");
            Assert.True(StartupEntry.TurnedOff(disabled), "3 是关着");

            Assert.True(StartupEntry.IsEnabled(mine, null, exe), "那一格在、没人关过：会自启");
            Assert.True(StartupEntry.IsEnabled(mine, enabled, exe), "任务管理器里显示开着：会自启");

            // 这一条是「拨开关」那一段最容易漏的一格：Run 那一格还在，关掉它的是任务管理器留在另一处的标记 ——
            // 只看 Run 就会报「会自启」，而登录时 Windows 什么都不启动。
            Assert.False(StartupEntry.IsEnabled(mine, disabled, exe), "被任务管理器关掉了：不自启");
            Assert.False(StartupEntry.IsEnabled(null, null, exe), "那一格根本不在：不自启");
            Assert.False(StartupEntry.IsEnabled(@"""C:\Old\Momoka.exe""", null, exe), "那一格指着别的程序：不自启");
        });

        Test("开机自启：值名就是程序名", () =>
        {
            // 任务管理器「启动」页上显示的就是这个值名，所以它得是程序自己的名字。
            Assert.Equal(AppIdentity.Title, StartupEntry.ValueName);
            Assert.Equal("Momoka", StartupEntry.ValueName);
        });
    }
}
