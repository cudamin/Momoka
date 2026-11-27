using System.Collections;
using Momoka.Playback;

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
        SettingsScrobblingTests.RegisterSearch(fixture);
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
        TestHarness.Test("快捷键页：分区卡覆盖全部动作，六行附注挂在对应快捷键下面", () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            var vm = fixture.NewViewModel();
            fixture.Attach(vm, settings, fixture.NewLauncher());
            var sections = ((IEnumerable)fixture.Invoke(vm, "ShortcutSections")!).Cast<object>().ToList();
            fixture.List(vm, "Sections").Clear();
            foreach (var section in sections) fixture.List(vm, "Sections").Add(section);
            var rows = sections.SelectMany(section => fixture.List(section, "Rows").Cast<object>()).ToList();
            var expectedActions = fixture.Get<bool>(vm, "DanmakuReady")
                ? ShortcutCatalog.Actions.Count
                : ShortcutCatalog.Actions.Count - 4;

            string Label(object row) => fixture.Get<string>(row, "Label");

            Assert.Equal(13, sections.Count, "快捷键页应当拆成十三张分区卡（含退出、跳过与恢复默认）");
            var titles = sections.Select(section => fixture.Get<string>(section, "Title")).ToArray();
            Assert.True(titles.SequenceEqual(new[]
                {
                    "播放与进度", "播放控制", "集数与章节", "速度与循环", "字幕", "次字幕与兼容性", "音轨与音频",
                    "画面与缩放", "画质与色彩", "视频与滤镜", "截图", "弹幕", "退出、跳过与恢复默认"
                }),
                "分区卡的次序就是阅读次序：" + string.Join('、', titles));
            var shortcuts = rows.Where(row => row.GetType().Name == "SettingShortcutRow").ToList();
            Assert.Equal(expectedActions, shortcuts.Count, "每个可改动作恰好出现一次");
            Assert.Equal(expectedActions, shortcuts.Select(row => fixture.Get<string>(row, "Id")).Distinct().Count(),
                "同一个动作不能落在两张分区卡里");
            foreach (var (actionId, label) in new[]
            {
                ("seek-backward", "快退跨度（秒）"), ("seek-forward", "快进跨度（秒）"),
                ("seek-backward-long", "大步快退跨度（秒）"), ("seek-forward-long", "大步快进跨度（秒）")
            })
            {
                var index = shortcuts.FindIndex(row => fixture.Get<string>(row, "Id") == actionId);
                Assert.True(index >= 0, $"{label} 上面那颗快捷键行缺失");
                var spanIndex = rows.FindIndex(row => Label(row) == label);
                Assert.Equal(rows.IndexOf(shortcuts[index]) + 1, spanIndex, $"{label} 没有紧跟在 {actionId} 下面");
                Assert.True(fixture.Get<bool>(rows[spanIndex], "IsSubordinate"), $"{label} 不是缩进的从属行");
            }

            // 2026-11-24 用户令「快退也要」：快退、快进各有一行长按倍率，分别紧跟在同向的「跨度（秒）」后面。
            foreach (var (actionId, label, note) in new[]
            {
                ("seek-backward", "快退跨度（倍）", "长按箭头以这个倍速快退"),
                ("seek-forward", "快进跨度（倍）", "长按箭头以这个倍速播放")
            })
            {
                var multiplierIndex = rows.FindIndex(row => Label(row) == label);
                Assert.True(multiplierIndex >= 0, $"{label} 没有排出来");
                Assert.Equal(rows.IndexOf(shortcuts[shortcuts.FindIndex(row => fixture.Get<string>(row, "Id") == actionId)]) + 2,
                    multiplierIndex, $"{label} 没有紧跟在同向步长下面");
                Assert.True(fixture.Get<bool>(rows[multiplierIndex], "IsSubordinate"), $"{label} 不是缩进的从属行");
                Assert.Equal(2d, fixture.Get<double>(rows[multiplierIndex], "Minimum"));
                Assert.Equal(10d, fixture.Get<double>(rows[multiplierIndex], "Maximum"));
                Assert.Equal(note, fixture.Get<string>(rows[multiplierIndex], "Note"));
            }
        });
        TestHarness.Test("快捷键页：退出与两条跳过提示键可改、可搜并计入数量", () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            var vm = fixture.NewViewModel();
            fixture.Attach(vm, settings, fixture.NewLauncher());
            var sections = ((IEnumerable)fixture.Invoke(vm, "ShortcutSections")!).Cast<object>().ToList();
            fixture.List(vm, "Sections").Clear();
            foreach (var section in sections) fixture.List(vm, "Sections").Add(section);
            var expectedActions = fixture.Get<bool>(vm, "DanmakuReady")
                ? ShortcutCatalog.Actions.Count
                : ShortcutCatalog.Actions.Count - 4;
            List<object> Rows() => sections.SelectMany(section => fixture.List(section, "Rows").Cast<object>()).ToList();

            object Row(string label) => Rows().Single(row => Label(row) == label);
            string Label(object row) => fixture.Get<string>(row, "Label");
            string Value(object row) => fixture.Get<string>(row, "Value");

            Assert.Equal(expectedActions + 7, Rows().Count, "可改动作之外只有六行附注与恢复入口");
            foreach (var (label, id, combo) in new[]
            {
                ("退出全屏 / 退出播放", ShortcutCatalog.ExitPlayback, "Esc"),
                ("关闭跳过片头 / 片尾提示", ShortcutCatalog.DismissSkip, "Del"),
                ("确认跳过片头 / 片尾", ShortcutCatalog.TakeSkip, "回车")
            })
            {
                var row = Row(label);
                Assert.Equal("SettingShortcutRow", row.GetType().Name, "必须使用可录入按键的现有控件");
                Assert.Equal(id, fixture.Get<string>(row, "Id"));
                Assert.Equal(combo, fixture.Get<string>(row, "ComboText"));
            }
            Assert.Equal($"{expectedActions} 个可改快捷键", Value(Row("恢复默认快捷键")),
                "三条新动作必须计入可改数量");

            fixture.Set(vm, "ShortcutSearchQuery", "Esc");
            Assert.Equal(2, Rows().Count, "Esc 搜索应只剩退出快捷键与恢复入口");
            Assert.Equal("退出全屏 / 退出播放", Label(Rows()[0]!));
            Assert.Equal("SettingShortcutRow", Rows()[0].GetType().Name);

            fixture.Set(vm, "ShortcutSearchQuery", "Del");
            Assert.Equal(2, Rows().Count);
            Assert.Equal("关闭跳过片头 / 片尾提示", Label(Rows()[0]!));

            fixture.Set(vm, "ShortcutSearchQuery", "回车");
            Assert.Equal(2, Rows().Count);
            Assert.Equal("确认跳过片头 / 片尾", Label(Rows()[0]!));

            // 「→ 键跳」只出现在快进跨度那行的说明里：命中步长应带出它上面那颗快捷键和同组的两行附注，
            // 末尾恢复入口照旧在。
            fixture.Set(vm, "ShortcutSearchQuery", "→ 键跳");
            Assert.Equal(4, Rows().Count, "搜索步长说明应带出上面那颗快捷键与同组附注");
            Assert.Equal("快进", Label(Rows()[0]!));
            Assert.Equal("快进跨度（秒）", Label(Rows()[1]!));
            Assert.Equal("快进跨度（倍）", Label(Rows()[2]!));
            Assert.Equal("恢复默认快捷键", Label(Rows()[3]!));

            // 「长按箭头」同时出现在快退、快进两行倍率说明里：两组的快捷键与全部附注都要带出来，而不是只剩附注。
            fixture.Set(vm, "ShortcutSearchQuery", "长按箭头");
            Assert.Equal(7, Rows().Count, "搜索长按倍率说明应带出两组快捷键与附注");
            Assert.Equal("快退", Label(Rows()[0]!));
            Assert.Equal("快退跨度（秒）", Label(Rows()[1]!));
            Assert.Equal("快退跨度（倍）", Label(Rows()[2]!));
            Assert.Equal("快进", Label(Rows()[3]!));
            Assert.Equal("快进跨度（秒）", Label(Rows()[4]!));
            Assert.Equal("快进跨度（倍）", Label(Rows()[5]!));
            Assert.Equal("恢复默认快捷键", Label(Rows()[6]!));

            fixture.Set(vm, "ShortcutSearchQuery", "");
            Assert.Equal(expectedActions + 7, Rows().Count, "清空搜索后应恢复全部可改动作与附注");

            var dismiss = Row("关闭跳过片头 / 片尾提示");
            var take = Row("确认跳过片头 / 片尾");
            var exit = Row("退出全屏 / 退出播放");
            fixture.Invoke(dismiss, "OnCommitted", dismiss, "Space");
            fixture.Invoke(take, "OnCommitted", take, "F");
            fixture.Invoke(exit, "OnCommitted", exit, "Ctrl+F10");
            Assert.Equal("空格", fixture.Get<string>(dismiss, "ComboText"));
            Assert.Equal("F", fixture.Get<string>(take, "ComboText"));
            Assert.Equal("Ctrl + F10", fixture.Get<string>(exit, "ComboText"));
            Assert.Equal("空格", fixture.Get<string>(Row("播放 / 暂停"), "ComboText"), "重叠不应改掉普通动作");
            Assert.Equal("F", fixture.Get<string>(Row("全屏切换"), "ComboText"));
            fixture.Set(vm, "ShortcutSearchQuery", "Ctrl+F10");
            Assert.Equal(2, Rows().Count);
            Assert.True(ReferenceEquals(exit, Rows()[0]), "改绑后仍应保留原行与回调");
            fixture.Set(vm, "ShortcutSearchQuery", "Esc");
            Assert.Equal("没有找到匹配的快捷键", Label(Rows()[0]));
            fixture.Invoke(vm, "ClearAllShortcuts");
            Assert.Equal("Esc", fixture.Get<string>(exit, "ComboText"));
            Assert.Equal("Del", fixture.Get<string>(dismiss, "ComboText"));
            Assert.Equal("回车", fixture.Get<string>(take, "ComboText"));
            Assert.True(ReferenceEquals(exit, Rows()[0]), "过滤时恢复默认也应恢复隐藏的动作");
        });
        TestHarness.Test("设置搜索：替换分组解除旧行索引，旧分组变化不会返回失效项", () =>
        {
            var (vm, section, _) = About(Fact("旧设置项"));
            fixture.Set(vm, "SettingsSearchQuery", "旧设置项");
            fixture.List(vm, "Sections").Clear();
            fixture.List(section, "Rows").Add(Fact("旧设置项2"));
            Assert.Equal(0, Results(vm).Count());
        });
        TestHarness.Test("日历设置：MoviePilot 关闭时仍可搜索、修改和保存日历及 AniBT 偏好", () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            var vm = fixture.NewViewModel();
            fixture.Attach(vm, settings, fixture.NewLauncher());
            var section = fixture.Invoke(vm, "MoviePilotCard")!;
            fixture.List(vm, "Sections").Add(section);
            var rows = fixture.List(section, "Rows");
            var connection = rows[0]!;
            fixture.Set(vm, "SettingsSearchQuery", "侧边栏显示日历");
            Assert.Equal(2, rows.Count);
            var calendar = rows[1]!;
            Assert.Equal("侧边栏显示日历", fixture.Get<string>(calendar, "Label"));
            Assert.True(fixture.Get<bool>(calendar, "IsOn"));
            Assert.False(settings.Settings.MoviePilot.Enabled);
            Assert.Equal(0, settings.TrySaveCalls);
            var suggestion = Results(vm).Single();
            Assert.True(ReferenceEquals(calendar, fixture.Get<object>(suggestion, "Row")));
            Assert.True((bool)fixture.Invoke(vm, "SelectSettingsSearchResult", suggestion)!);

            fixture.Set(calendar, "IsOn", false);
            Assert.False(settings.Settings.MoviePilot.CalendarEnabled);
            Assert.Equal(1, settings.TrySaveCalls);
            fixture.Set(connection, "AniBtEnabled", false);
            Assert.False(settings.Settings.MoviePilot.AniBtEnabled);
            Assert.Equal(2, settings.TrySaveCalls);
            fixture.Set(connection, "Enabled", true);
            fixture.Set(connection, "Enabled", false);
            Assert.True(ReferenceEquals(calendar, rows[1]));
            Assert.False(fixture.Get<bool>(calendar, "IsOn"));
            Assert.Equal(1, Results(vm).Count());
        });
        TestHarness.Test("日历启动刷新：仅 MoviePilot 可用时显示，关闭日历或服务后保留偏好并拒绝迟到修改", () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            var vm = fixture.NewViewModel();
            fixture.Attach(vm, settings, fixture.NewLauncher());
            var section = fixture.Invoke(vm, "MoviePilotCard")!;
            fixture.List(vm, "Sections").Add(section);
            var rows = fixture.List(section, "Rows");
            var connection = rows[0]!;
            var calendar = rows[1]!;
            fixture.Set(vm, "SettingsSearchQuery", "启动时自动刷新日历");
            Assert.Equal(0, Results(vm).Count());

            fixture.Set(connection, "Enabled", true);
            Assert.Equal(3, rows.Count);
            var refresh = rows[2]!;
            Assert.False(fixture.Get<bool>(refresh, "IsOn"));
            Assert.Equal(1, Results(vm).Count());
            fixture.Set(refresh, "IsOn", true);
            Assert.True(settings.Settings.MoviePilot.CalendarRefreshOnStartup);

            fixture.Set(calendar, "IsOn", false);
            Assert.Equal(2, rows.Count);
            Assert.Equal(0, Results(vm).Count());
            fixture.Set(refresh, "IsOn", false);
            Assert.True(settings.Settings.MoviePilot.CalendarRefreshOnStartup);
            fixture.Set(calendar, "IsOn", true);
            Assert.True(ReferenceEquals(refresh, rows[2]));
            Assert.True(fixture.Get<bool>(refresh, "IsOn"));

            fixture.Set(connection, "Enabled", false);
            Assert.Equal(2, rows.Count);
            Assert.Equal(0, Results(vm).Count());
            fixture.Set(refresh, "IsOn", false);
            Assert.True(settings.Settings.MoviePilot.CalendarRefreshOnStartup);
            fixture.Set(calendar, "IsOn", false);
            fixture.Set(calendar, "IsOn", true);
            Assert.True(settings.Settings.MoviePilot.CalendarEnabled, "独立 AniBT 的日历开关仍可修改");
            fixture.Set(connection, "Enabled", true);
            Assert.True(ReferenceEquals(refresh, rows[2]));
            Assert.True(fixture.Get<bool>(refresh, "IsOn"));
        });
        TestHarness.Test("MP 日历设置：重建卡片读取导入或恢复后的偏好", () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            settings.Settings.MoviePilot.Enabled = true;
            var vm = fixture.NewViewModel();
            fixture.Attach(vm, settings, fixture.NewLauncher());
            var imported = new Configuration.AppSettings();
            imported.MoviePilot.CalendarEnabled = false;
            Configuration.SettingsPreferences.Apply(settings.Settings, imported);
            var section = fixture.Invoke(vm, "MoviePilotCard")!;
            Assert.False(fixture.Get<bool>(fixture.List(section, "Rows")[1]!, "IsOn"));
            Assert.True(settings.Settings.MoviePilot.Enabled);

            Configuration.SettingsReset.Restore(settings.Settings);
            section = fixture.Invoke(vm, "MoviePilotCard")!;
            Assert.True(fixture.Get<bool>(fixture.List(section, "Rows")[1]!, "IsOn"));
            Assert.True(settings.Settings.MoviePilot.Enabled);
            Assert.Equal(0, settings.TrySaveCalls, "重建只读，不补写配置");
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
