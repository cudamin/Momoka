using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Tests;

/// <summary>Real Shell presentation models with inert settings; no App, Attach, windows, or server requests.</summary>
internal static class UiLanguageDisplayTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Register(SettingsPageStateTests.ShellFixture fixture)
    {
        // The linked framework-free UiText in Momoka.Tests is a different static owner. Change the
        // loaded Shell's actual property, and restore it even when a presentation assertion fails.
        var sample = fixture.NewRow("SettingChoice", "", (Action)(() => { }));
        var language = sample.GetType().Assembly.GetType("Momoka.Shell.Localization.UiText", throwOnError: true)!
            .GetProperty("Language", BindingFlags.Static | BindingFlags.Public)!;

        TestHarness.Test("界面语言真实行：英文展示不调用选项回调，选择仍写原始值且只保存一次", () => English(() =>
        {
            var value = "initial";
            var writes = 0;
            var saves = 0;
            var after = 0;
            var follow = fixture.NewRow("SettingChoice", "跟随系统",
                (Action)(() => { value = "raw-system-value"; writes++; }));
            var other = fixture.NewRow("SettingChoice", "默认",
                (Action)(() => { value = "raw-default-value"; writes++; }));
            var choices = TypedArray(follow.GetType(), follow, other);
            var row = fixture.NewRow("SettingChoiceRow", "界面语言", null, choices, follow,
                (Action)(() => saves++), (Action)(() => after++));

            Assert.Equal("跟随系统", fixture.Get<string>(follow, "Label"));
            Assert.Equal("Follow system", fixture.Get<string>(follow, "DisplayLabel"));
            Assert.Equal("Follow system", follow.ToString());
            Assert.Equal("Default", fixture.Get<string>(other, "DisplayLabel"));
            Assert.Equal("initial", value);
            Assert.Equal(0, writes);
            Assert.Equal(0, saves);
            Assert.Equal(0, after);

            fixture.Set(row, "Selected", other);
            Assert.Equal("raw-default-value", value);
            Assert.Equal(1, writes);
            Assert.Equal(1, saves);
            Assert.Equal(1, after);
            Assert.Equal("默认", fixture.Get<string>(fixture.Get<object>(row, "Selected"), "Label"));
            fixture.Set(row, "Selected", other);
            Assert.Equal(1, writes);
            Assert.Equal(1, saves);
            Assert.Equal(1, after);
        }));

        TestHarness.Test("界面语言真实行：标签说明仅翻译展示，重述说明发显示通知且不写设置", () => English(() =>
        {
            var writes = 0;
            var saves = 0;
            var row = fixture.NewRow("SettingToggleRow", "UI 动画", "自动保存，下次播放生效。", false,
                (Action<bool>)(_ => writes++), (Action)(() => saves++), null);
            Assert.Equal("UI 动画", fixture.Get<string>(row, "Label"));
            Assert.Equal("UI animations", fixture.Get<string>(row, "DisplayLabel"));
            Assert.Equal("自动保存，下次播放生效。", fixture.Get<string>(row, "Note"));
            Assert.Equal("Saves automatically; applies to the next playback.", fixture.Get<string>(row, "DisplayNote"));
            var notices = new List<string>();
            ((INotifyPropertyChanged)row).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
            row.GetType().BaseType!.GetMethod("Restate", Members)!.Invoke(row, ["跟随系统"]);
            Assert.Equal("跟随系统", fixture.Get<string>(row, "Note"));
            Assert.Equal("Follow system", fixture.Get<string>(row, "DisplayNote"));
            Assert.Equal(1, notices.Count(name => name == "Note"));
            Assert.Equal(1, notices.Count(name => name == "DisplayNote"));
            row.GetType().BaseType!.GetMethod("Restate", Members)!.Invoke(row, ["跟随系统"]);
            Assert.Equal(1, notices.Count(name => name == "DisplayNote"));
            Assert.Equal(0, writes);
            Assert.Equal(0, saves);

            var external = fixture.NewRow("SettingToggleRow", "用户自定义电影名称甲", "用户服务器说明甲", false,
                (Action<bool>)(_ => writes++), (Action)(() => saves++), null);
            Assert.Equal("用户自定义电影名称甲", fixture.Get<string>(external, "DisplayLabel"));
            Assert.Equal("用户服务器说明甲", fixture.Get<string>(external, "DisplayNote"));
        }));

        TestHarness.Test("界面语言真实分类：导航键和路由不变，同名字典项不能翻译用户服务器名", () => English(() =>
        {
            var builtIn = fixture.NewRow("SettingsCategoryGroup", "界面", null, false, true);
            Assert.Equal("Interface", fixture.Get<string>(builtIn, "DisplayName"));
            // Deliberately collide with a known label: a server named 界面 is still user data.
            var server = fixture.NewRow("SettingsCategoryGroup", "界面", "opaque-source", true, true);
            var entry = fixture.Invoke(server, "Add", "界面", "opaque-source", "dashboard/settings", "界面", null)!;
            Assert.Equal("界面", fixture.Get<string>(server, "Name"));
            Assert.Equal("界面", fixture.Get<string>(server, "DisplayName"));
            Assert.Equal("opaque-source\u001f界面", fixture.Get<string>(entry, "Key"));
            Assert.Equal("opaque-source", fixture.Get<string>(entry, "SourceKey"));
            Assert.Equal("dashboard/settings", fixture.Get<string>(entry, "Route"));
            Assert.Equal("界面", fixture.Get<string>(entry, "CardCategory"));
            Assert.Equal("界面", fixture.Get<string>(entry, "Label"));
            Assert.Equal("Interface", fixture.Get<string>(entry, "DisplayLabel"));
            Assert.Equal("界面 · 界面", fixture.Get<string>(entry, "PickerLabel"));
            Assert.Equal("界面 · Interface", fixture.Get<string>(entry, "DisplayPickerLabel"));
        }));

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
            TestHarness.Test($"界面语言真实 VM：{kind} 共用语言行，三种选择保存稳定值且建行不保存", () => English(() =>
            {
                var settings = new SettingsPageStateTests.FakeSettings();
                var vm = NewViewModel(settings);
                try
                {
                    fixture.Invoke(vm, "ApplyServerAdministration", false, kind);
                    var category = fixture.List(vm, "Categories").Cast<object>()
                        .Single(entry => fixture.Get<string>(entry, "Key") == "界面");
                    Assert.Equal("Interface", fixture.Get<string>(category, "DisplayLabel"));
                    var row = fixture.Invoke(vm, "InterfaceLanguageRow", settings.Settings.Ui)!;
                    var choices = fixture.List(row, "Choices").Cast<object>().ToArray();
                    Assert.Equal("界面语言", fixture.Get<string>(row, "Label"));
                    Assert.Equal("Interface language", fixture.Get<string>(row, "DisplayLabel"));
                    Assert.Equal("跟随系统,简体中文,英语", string.Join(',', choices.Select(choice => fixture.Get<string>(choice, "Label"))));
                    Assert.Equal("Follow system,Simplified Chinese,English", string.Join(',', choices.Select(choice => fixture.Get<string>(choice, "DisplayLabel"))));
                    language.SetValue(null, UiLanguages.Chinese);
                    Assert.Equal("跟随系统,简体中文,英语", string.Join(',', choices.Select(choice => fixture.Get<string>(choice, "DisplayLabel"))));
                    language.SetValue(null, UiLanguages.English);
                    Assert.Equal("跟随系统", fixture.Get<string>(fixture.Get<object>(row, "Selected"), "Label"));
                    Assert.Contains("Restart Momoka", fixture.Get<string>(row, "DisplayNote"));
                    Assert.Equal(0, settings.TrySaveCalls);

                    foreach (var (label, value) in new[] { ("英语", UiLanguages.English), ("简体中文", UiLanguages.Chinese), ("跟随系统", UiLanguages.System) })
                    {
                        fixture.Set(row, "Selected", choices.Single(choice => fixture.Get<string>(choice, "Label") == label));
                        Assert.Equal(value, settings.Settings.Ui.Language);
                        Assert.Equal("Interface language", fixture.Get<string>(row, "DisplayLabel"), "保存偏好不在当前进程重建语言");
                        var reloaded = fixture.Invoke(vm, "InterfaceLanguageRow", settings.Settings.Ui)!;
                        Assert.Equal(label, fixture.Get<string>(fixture.Get<object>(reloaded, "Selected"), "Label"));
                    }
                    Assert.Equal(3, settings.TrySaveCalls);
                }
                finally { ((IDisposable)vm).Dispose(); }
            }));

        TestHarness.Test("界面语言真实搜索：中英文命中同一原始分类键，英文位置不翻译服务器名", () => English(() =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            var vm = NewViewModel(settings);
            try
            {
                var row = fixture.Invoke(vm, "InterfaceLanguageRow", settings.Settings.Ui)!;
                var section = fixture.NewRow("SettingSection", "界面", "界面", "自动保存，下次播放生效。",
                    TypedArray(row.GetType().BaseType!, row));
                fixture.List(vm, "Sections").Add(section);
                foreach (var query in new[] { "界面语言", "Interface language" })
                {
                    fixture.Set(vm, "SelectedCategory", "关于");
                    fixture.Set(vm, "SettingsSearchQuery", query);
                    var result = fixture.List(vm, "SettingsSearchResults").Cast<object>()
                        .Single(item => fixture.Get<string>(item, "Label") == "界面语言");
                    Assert.Equal("界面", fixture.Get<string>(result, "CategoryKey"));
                    Assert.Equal("Interface language", fixture.Get<string>(result, "DisplayLabel"));
                    Assert.Equal("Interface", fixture.Get<string>(result, "DisplayLocation"));
                    Assert.True(ReferenceEquals(row, fixture.Get<object>(result, "Row")));
                    Assert.True((bool)fixture.Invoke(vm, "SelectSettingsSearchResult", result)!);
                    Assert.Equal("界面", fixture.Get<string>(vm, "SelectedCategory"));
                    Assert.Equal("界面", fixture.Get<string>(fixture.Get<object>(vm, "SelectedEntry"), "Key"));
                }

                var server = fixture.NewRow("SettingsCategoryGroup", "界面", "source-a", true, true);
                var entry = fixture.Invoke(server, "Add", "控制台", "source-a", "dashboard", "控制台", null)!;
                fixture.List(vm, "Categories").Add(entry);
                foreach (var query in new[] { "控制台", "Dashboard" })
                {
                    fixture.Set(vm, "SettingsSearchQuery", query);
                    var result = fixture.List(vm, "SettingsSearchResults").Cast<object>()
                        .Single(item => fixture.Get<string>(item, "CategoryKey") == "source-a\u001f控制台");
                    Assert.Equal("界面", fixture.Get<string>(result, "DisplayLocation"));
                    Assert.Equal("Dashboard", fixture.Get<string>(result, "DisplayLabel"));
                    Assert.True((bool)fixture.Invoke(vm, "SelectSettingsSearchResult", result)!);
                    Assert.Equal("source-a\u001f控制台", fixture.Get<string>(vm, "SelectedCategory"));
                    Assert.Equal("dashboard", fixture.Get<string>(fixture.Get<object>(vm, "SelectedEntry"), "Route"));
                }
                Assert.Equal(0, settings.TrySaveCalls);
            }
            finally { ((IDisposable)vm).Dispose(); }
        }));

        TestHarness.Test("界面语言真实说明：下次播放前缀与 HDR、缓冲说明组合完整翻译", () => English(() =>
        {
            var vm = NewViewModel(new SettingsPageStateTests.FakeSettings());
            try
            {
                foreach (var method in new[] { "HdrCard", "BufferCard", "FrameInterpolationCard" })
                {
                    var section = fixture.Invoke(vm, method)!;
                    var original = fixture.Get<string>(section, "Description");
                    var display = fixture.Get<string>(section, "DisplayDescription");
                    Assert.True(original.StartsWith("自动保存，下次播放生效。", StringComparison.Ordinal), method);
                    Assert.True(display.StartsWith("Saves automatically; applies to the next playback.", StringComparison.Ordinal), method);
                    Assert.False(Regex.IsMatch(display, "[\u4e00-\u9fff]"), method + ": " + display);
                    Assert.Equal(original, fixture.Get<string>(section, "Description"));
                }
                Assert.Contains("InterfaceLanguageRow(ui)", fixture.ViewModelSource, "真实界面卡仍须接入同一个可测试建行入口");
            }
            finally { ((IDisposable)vm).Dispose(); }
        }));

        object NewViewModel(SettingsPageStateTests.FakeSettings settings)
        {
            var vm = fixture.NewViewModel();
            vm.GetType().GetField("_settings", Members)!.SetValue(vm, settings);
            return vm;
        }

        void English(Action verify)
        {
            var original = (string)language.GetValue(null)!;
            try
            {
                language.SetValue(null, UiLanguages.English);
                verify();
            }
            finally { language.SetValue(null, original); }
        }
    }

    private static Array TypedArray(Type type, params object[] items)
    {
        var array = Array.CreateInstance(type, items.Length);
        for (var index = 0; index < items.Length; index++) array.SetValue(items[index], index);
        return array;
    }
}
