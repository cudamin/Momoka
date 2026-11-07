using Momoka.Infrastructure;
using System.Reflection;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FontFillRecoveryTests
{
    internal static int Run(string assembly, string root)
    {
        var fixture = new SettingsPageStateTests.ShellFixture(assembly, root);
        foreach (var query in new[] { "Con", "" })
            Test("R15-005 字体目录到达保留正在搜索的文字 " + query, () =>
            {
                var writes = 0;
                var row = fixture.NewRow("SettingFontRow", "字体", null, "Original", (Action<string>)(_ => writes++), (Action)(() => writes++));
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                row.GetType().GetMethod("OpenList", flags)!.Invoke(row, null);
                fixture.Set(row, "BoxText", query);
                var catalogue = (FontCatalogue)Activator.CreateInstance(typeof(FontCatalogue), flags, null,
                    [new List<FontEntry> { new("Consolas", []) }, 1], null)!;
                row.GetType().GetMethod("Fill", flags)!.Invoke(row, [catalogue]);
                Assert.Equal(query, fixture.Get<string>(row, "Query"));
                Assert.Equal(query, fixture.Get<string>(row, "BoxText"));
                Assert.Equal("Visible", fixture.Get<object>(row, "ListVisibility").ToString());
                Assert.Equal(0, writes);
            });
        return TestHarness.Run();
    }
}
