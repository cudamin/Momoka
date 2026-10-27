using System.Collections;

namespace Momoka.Tests;

/// <summary>执行本树实际 SettingsViewModel 的搜索，不构造窗口、不读账号、不触发网络或保存。</summary>
internal static class SettingsSearchTests
{
    internal static int Run(string shellAssembly, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var fixture = new SettingsPageStateTests.ShellFixture(shellAssembly, sourceRoot);
        Register(fixture);
        return TestHarness.Run();
    }

    private static void Register(SettingsPageStateTests.ShellFixture fixture)
    {
        TestHarness.Test("设置标题：每张卡都保留自己的标题", () =>
        {
            var home = fixture.NewRow("SettingSection", "主页", "主页", "说明", Rows(Fact("主页选项")));
            Assert.Equal("主页", fixture.Get<string>(home, "Title"));
            var player = fixture.NewRow("SettingSection", "播放器", "自定义 mpv.conf", "说明", Rows(Fact("路径")));
            Assert.Equal("自定义 mpv.conf", fixture.Get<string>(player, "Title"));
        });
        TestHarness.Test("设置搜索：未附加依赖可读目录，空白输入没有结果", () =>
        {
            var vm = fixture.NewViewModel();
            fixture.Set(vm, "SettingsSearchQuery", "  \t  ");
            Assert.Equal(0, fixture.List(vm, "SettingsSearchResults").Count);
            Assert.Equal("Collapsed", fixture.Get<object>(vm, "SettingsSearchEmptyVisibility").ToString());
        });
        TestHarness.Test("设置搜索：不增加、删除或过滤侧栏分类", () =>
        {
            var vm = fixture.NewViewModel();
            var categories = fixture.List(vm, "Categories");
            var before = categories.Cast<object>().ToArray();
            fixture.Set(vm, "SettingsSearchQuery", "字幕");
            Assert.True(ReferenceEquals(categories, fixture.List(vm, "Categories")));
            Assert.True(before.SequenceEqual(categories.Cast<object>()));
            Assert.True(Results(vm).Any(result => fixture.Get<string>(result, "Label") == "字幕"));
        });
        TestHarness.Test("设置搜索：精确名称优先于说明中的匹配", () =>
        {
            var (vm, _, exact) = About(Fact("其他选项", "包含 WebP 质量的说明"), Fact("WebP 质量"));
            fixture.Set(vm, "SettingsSearchQuery", "WebP 质量");
            Assert.Equal("WebP 质量", fixture.Get<string>(Results(vm).First(), "Label"));
            Assert.True(ReferenceEquals(exact, fixture.Get<object>(Results(vm).First(), "Row")));
        });
        TestHarness.Test("设置搜索：多词与忽略大小写匹配", () =>
        {
            var (vm, _, _) = About(Fact("WebP 压缩", "保存截图"), Fact("WebP 质量"));
            fixture.Set(vm, "SettingsSearchQuery", "webp   压缩");
            Assert.Equal(1, Results(vm).Count());
            Assert.Equal("WebP 压缩", fixture.Get<string>(Results(vm).Single(), "Label"));
        });
        TestHarness.Test("设置搜索：标签、分组和说明可搜，设置值不进入索引", () =>
        {
            var (vm, _, _) = About(Fact("目录", "导出配置", "private-token-9df7"));
            fixture.Set(vm, "SettingsSearchQuery", "导出");
            Assert.Equal(1, Results(vm).Count());
            fixture.Set(vm, "SettingsSearchQuery", "private-token-9df7");
            Assert.Equal(0, Results(vm).Count());
            Assert.Equal("Visible", fixture.Get<object>(vm, "SettingsSearchEmptyVisibility").ToString());
        });
        TestHarness.Test("设置搜索：无结果和清空即时恢复，结果集合保持稳定", () =>
        {
            var vm = fixture.NewViewModel();
            var results = fixture.List(vm, "SettingsSearchResults");
            fixture.Set(vm, "SettingsSearchQuery", "未存在的选项9df7");
            Assert.Equal("Visible", fixture.Get<object>(vm, "SettingsSearchEmptyVisibility").ToString());
            fixture.Set(vm, "SettingsSearchQuery", "");
            Assert.Equal("Collapsed", fixture.Get<object>(vm, "SettingsSearchEmptyVisibility").ToString());
            Assert.True(ReferenceEquals(results, fixture.List(vm, "SettingsSearchResults")));
        });
        TestHarness.Test("设置搜索：从属行隐藏和恢复当场更新，不改原值", () =>
        {
            var row = Fact("WebP 质量", value: "73");
            var (vm, section, _) = About(row);
            fixture.Set(vm, "SettingsSearchQuery", "WebP 质量");
            Assert.Equal(1, Results(vm).Count());
            fixture.List(section, "Rows").Remove(row);
            Assert.Equal(0, Results(vm).Count());
            fixture.List(section, "Rows").Add(row);
            Assert.Equal(1, Results(vm).Count());
            Assert.Equal("73", fixture.Get<string>(row, "Value"));
        });
        TestHarness.Test("设置搜索：旧行建议不导航到已经隐藏的选项", () =>
        {
            var row = Fact("WebP 质量");
            var (vm, section, _) = About(row);
            fixture.Set(vm, "SettingsSearchQuery", "WebP 质量");
            var result = Results(vm).Single();
            fixture.List(section, "Rows").Remove(row);
            Assert.False((bool)fixture.Invoke(vm, "SelectSettingsSearchResult", result)!);
            Assert.Equal("主页", fixture.Get<string>(vm, "SelectedCategory"));
        });
        TestHarness.Test("设置搜索：旧页面建议不越过当前目录权限", () =>
        {
            var vm = fixture.NewViewModel();
            fixture.Set(vm, "SettingsSearchQuery", "服务器");
            var result = Results(vm).Single(item => fixture.Get<string>(item, "Label") == "服务器");
            var categories = fixture.List(vm, "Categories");
            categories.Remove(categories.Cast<object>().Single(entry => fixture.Get<string>(entry, "Label") == "服务器"));
            Assert.False((bool)fixture.Invoke(vm, "SelectSettingsSearchResult", result)!);
        });
        TestHarness.Test("设置搜索：定位保留原分类、分组和输入行实例", () =>
        {
            var row = Fact("截图目录");
            var (vm, section, _) = About(row);
            var sections = fixture.List(vm, "Sections");
            fixture.Set(vm, "SettingsSearchQuery", "截图目录");
            Assert.True((bool)fixture.Invoke(vm, "SelectSettingsSearchResult", Results(vm).Single())!);
            Assert.Equal("关于", fixture.Get<string>(vm, "SelectedCategory"));
            Assert.True(ReferenceEquals(sections, fixture.List(vm, "Sections")));
            Assert.True(ReferenceEquals(row, fixture.List(section, "Rows")[0]));
            Assert.True(fixture.Get<bool>(section, "IsVisible"));
        });
        TestHarness.Test("设置搜索：刷新行表不会改变当前卡片可见性或选择", () =>
        {
            var (vm, section, _) = About(Fact("截图目录"));
            fixture.Set(vm, "SelectedCategory", "关于");
            fixture.Set(vm, "SettingsSearchQuery", "字幕");
            Assert.Equal("关于", fixture.Get<string>(vm, "SelectedCategory"));
            Assert.True(fixture.Get<bool>(section, "IsVisible"));
        });
        TestHarness.Test("设置搜索：限制建议数量避免超长列表", () =>
        {
            var (vm, _, _) = About(Enumerable.Range(0, 80).Select(index => Fact("项目" + index)).ToArray());
            fixture.Set(vm, "SettingsSearchQuery", "项目");
            Assert.Equal(40, Results(vm).Count());
        });
        TestHarness.Test("设置搜索：替换分组解除旧行索引，旧分组变化不会返回失效项", () =>
        {
            var (vm, section, _) = About(Fact("旧设置项"));
            fixture.Set(vm, "SettingsSearchQuery", "旧设置项");
            fixture.List(vm, "Sections").Clear();
            fixture.List(section, "Rows").Add(Fact("旧设置项2"));
            Assert.Equal(0, Results(vm).Count());
        });

        Array Rows(params object[] rows)
        {
            var array = Array.CreateInstance(rows[0].GetType().BaseType!, rows.Length);
            for (var index = 0; index < rows.Length; index++) array.SetValue(rows[index], index);
            return array;
        }
        IEnumerable<object> Results(object vm) => fixture.List(vm, "SettingsSearchResults").Cast<object>();
        object Fact(string label, string note = "", string value = "") => fixture.NewRow("SettingFactRow", label, note, value, null, null);
        (object Vm, object Section, object LastRow) About(params object[] rows)
        {
            var vm = fixture.NewViewModel();
            var section = fixture.NewSection(rows);
            fixture.List(vm, "Sections").Add(section);
            return (vm, section, rows.Last());
        }
    }
}
