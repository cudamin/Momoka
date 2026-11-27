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

        TestHarness.Test("分类下拉与图标：非 Momoka 分组带前缀，Jellyfin 组名更新，已删分类回落默认图标", () => English(() =>
        {
            var momoka = fixture.NewRow("SettingsCategoryGroup", "Momoka", null, false, true);
            var home = fixture.Invoke(momoka, "Add", "主页", null, null, null, null)!;
            var notify = fixture.Invoke(momoka, "Add", "通知", "src-1", null, null, "主服")!;
            Assert.Equal("主页", fixture.Get<string>(home, "PickerLabel"));
            Assert.Equal("Home", fixture.Get<string>(home, "DisplayPickerLabel"));
            Assert.Equal("通知 · 主服", fixture.Get<string>(notify, "PickerLabel"));
            Assert.Equal("Notifications · 主服", fixture.Get<string>(notify, "DisplayPickerLabel"));

            var emby = fixture.NewRow("SettingsCategoryGroup", "Emby Server", null, false, true);
            var embyDash = fixture.Invoke(emby, "Add", "控制台", null, "dashboard", null, null)!;
            Assert.Equal("Emby Server · 控制台", fixture.Get<string>(embyDash, "PickerLabel"));
            Assert.Equal("Emby Server · Dashboard", fixture.Get<string>(embyDash, "DisplayPickerLabel"));

            var account = fixture.NewRow("SettingsCategoryGroup", "帐号", null, false, true);
            var fnAppear = fixture.Invoke(account, "Add", "外观", null, null, "界面", null)!;
            Assert.Equal("帐号 · 外观", fixture.Get<string>(fnAppear, "PickerLabel"));
            Assert.Equal("Account · Appearance", fixture.Get<string>(fnAppear, "DisplayPickerLabel"));

            var media = fixture.NewRow("SettingsCategoryGroup", "影视服务器", null, false, true);
            var fnLib = fixture.Invoke(media, "Add", "媒体库", null, null, null, null)!;
            Assert.Equal("影视服务器 · 媒体库", fixture.Get<string>(fnLib, "PickerLabel"));
            Assert.Equal("Media server · Libraries", fixture.Get<string>(fnLib, "DisplayPickerLabel"));

            var vm = NewViewModel(new SettingsPageStateTests.FakeSettings());
            try
            {
                fixture.Invoke(vm, "SetServerMenu", JellyfinServerSettingsMenu.Build(), MediaServerKind.Jellyfin);
                fixture.Invoke(vm, "ApplyServerAdministration", true, MediaServerKind.Jellyfin);
                var categories = fixture.List(vm, "Categories").Cast<object>().ToArray();
                var jfDash = categories.Single(entry => fixture.Get<string>(entry, "Label") == "控制台");
                var jfActivity = categories.Single(entry => fixture.Get<string>(entry, "Label") == "活动");
                Assert.Equal("Jellyfin", JellyfinServerSettingsMenu.ServerGroupLabel);
                Assert.Equal("Jellyfin", fixture.Get<string>(fixture.Get<object>(jfDash, "Group"), "Name"));
                Assert.Equal("Jellyfin · 控制台", fixture.Get<string>(jfDash, "PickerLabel"));
                Assert.Equal("Jellyfin · Dashboard", fixture.Get<string>(jfDash, "DisplayPickerLabel"));
                Assert.Equal("Devices · Activity", fixture.Get<string>(jfActivity, "DisplayPickerLabel"));
            }
            finally { ((IDisposable)vm).Dispose(); }

            var converter = new Momoka.Shell.Views.SettingsCategoryGlyphConverter();
            Assert.Equal("\uE7C3", (string)converter.Convert("恢复默认", typeof(string), null!, "")!);
            Assert.Equal("\uE80F", (string)converter.Convert("主页", typeof(string), null!, "")!);
            Assert.Equal("\uF246", (string)converter.Convert("控制台", typeof(string), null!, "")!);
        }));

        TestHarness.Test("界面语言完整审计：所有安全卡片、选项与分支在英文下无未翻译中文", () => English(() =>
        {
            var cjk = new Regex("[\u4e00-\u9fff]");
            var unlocalizedChoices = new HashSet<string>(StringComparer.Ordinal) { "手动指定档位" };
            var untranslated = new List<string>();
            void NoCjk(string context, string? value)
            {
                if (!string.IsNullOrEmpty(value) && cjk.IsMatch(value)) untranslated.Add($"{context}: {value}");
            }

            void AuditRow(string owner, object row)
            {
                var label = fixture.Get<string>(row, "Label");
                var where = $"{owner} -> {label}";
                NoCjk(where + " DisplayLabel", fixture.Get<string>(row, "DisplayLabel"));
                var note = fixture.Get<string>(row, "DisplayNote");
                if (label == "自动识别动画")
                {
                    Assert.True(note.StartsWith("Match the following keywords in Emby genres and tags:", StringComparison.Ordinal), note);
                    Assert.True(note.EndsWith("A manually selected profile takes priority.", StringComparison.Ordinal), note);
                }
                else NoCjk(where + " DisplayNote", note);
                switch (row.GetType().Name)
                {
                    case "SettingChoiceRow":
                        if (!unlocalizedChoices.Contains(label))
                            foreach (var choice in fixture.List(row, "Choices").Cast<object>())
                                NoCjk(where + " Choice", fixture.Get<string>(choice, "DisplayLabel"));
                        break;
                    case "SettingTextRow":
                        NoCjk(where + " DisplayPlaceholder", fixture.Get<string>(row, "DisplayPlaceholder"));
                        break;
                    case "SettingColorRow":
                        NoCjk(where + " DisplayHexLabel", fixture.Get<string>(row, "DisplayHexLabel"));
                        break;
                    case "SettingFactRow":
                        NoCjk(where + " DisplayActionLabel", fixture.Get<string>(row, "DisplayActionLabel"));
                        NoCjk(where + " ActionAutomationName", fixture.Get<string>(row, "ActionAutomationName"));
                        break;
                    case "SettingSecretRow":
                        NoCjk(where + " Status", fixture.Get<string>(row, "Status"));
                        NoCjk(where + " SaveAutomationName", fixture.Get<string>(row, "SaveAutomationName"));
                        NoCjk(where + " ClearAutomationName", fixture.Get<string>(row, "ClearAutomationName"));
                        break;
                    case "SettingMoviePilotRow":
                        NoCjk(where + " TestLabel", fixture.Get<string>(row, "TestLabel"));
                        break;
                    case "SettingFontRow":
                        NoCjk(where + " Status", fixture.Get<string>(row, "Status"));
                        break;
                    case "SettingLanguagesRow":
                        foreach (var choice in fixture.List(row, "Choices").Cast<object>())
                            NoCjk(where + " Language", fixture.Get<string>(choice, "DisplayName"));
                        NoCjk(where + " DisplaySummary", fixture.Get<string>(row, "DisplaySummary"));
                        break;
                    case "SettingTitleRulesRow":
                        NoCjk(where + " PriorityHint", fixture.Get<string>(row, "PriorityHint"));
                        NoCjk(where + " NewTermPlaceholder", fixture.Get<string>(row, "NewTermPlaceholder"));
                        NoCjk(where + " NewTermName", fixture.Get<string>(row, "NewTermName"));
                        NoCjk(where + " AddName", fixture.Get<string>(row, "AddName"));
                        foreach (var rule in fixture.List(row, "Rules").Cast<object>())
                        {
                            foreach (var state in fixture.Get<IReadOnlyList<string>>(rule, "DisplayStateLabels"))
                                NoCjk(where + " RuleState", state);
                            NoCjk(where + " SelectedState", fixture.Get<string?>(rule, "DisplaySelectedState"));
                            Assert.True(fixture.Get<string>(rule, "UpName").StartsWith("Move ", StringComparison.Ordinal));
                            Assert.True(fixture.Get<string>(rule, "DownName").StartsWith("Move ", StringComparison.Ordinal));
                            Assert.True(fixture.Get<string>(rule, "RemoveName").StartsWith("Delete ", StringComparison.Ordinal));
                        }
                        break;
                    case "SettingHomeLayoutRow":
                        if (row.GetType().GetProperty("LibraryEntries", Members)?.GetValue(row) is { } nested)
                            AuditRow(owner, nested);
                        break;
                }
            }

            void AuditSections(string scenario, System.Collections.IEnumerable sections)
            {
                foreach (var section in sections.Cast<object>())
                {
                    var title = fixture.Get<string>(section, "Title");
                    NoCjk($"{scenario}/{title} DisplayTitle", fixture.Get<string>(section, "DisplayTitle"));
                    NoCjk($"{scenario}/{title} DisplayDescription", fixture.Get<string>(section, "DisplayDescription"));
                    NoCjk($"{scenario}/{title} DisplayCount", fixture.Get<string>(section, "DisplayCount"));
                    foreach (var row in fixture.List(section, "Rows").Cast<object>())
                        AuditRow($"{scenario}/{title}", row);
                }
            }

            var scenarios = new Action<AppSettings>[]
            {
                _ => { },
                s =>
                {
                    s.Mpv.Backend = MpvBackendKind.ExternalMpv;
                    s.Mpv.Pipeline = VideoPipelineKind.Standalone;
                    s.Mpv.CustomConfigEnabled = true;
                    s.Mpv.ScreenshotFormat = "webp";
                    s.Mpv.ScreenshotWebpLossless = false;
                    s.Video.Renderer = "gpu";
                    s.Video.FrameInterpolationMode = Momoka.Mpv.FrameInterpolationMode.Smooth;
                    s.Playback.SubtitleBackStyle = "background-box";
                    s.SubtitleTranslation.Enabled = true;
                    s.SubtitleTranslation.Provider = SubtitleTranslationProvider.Google;
                    s.Danmaku.Enabled = true;
                    s.Danmaku.Source = DanmakuSourceMode.Both;
                    s.Danmaku.Show = true;
                    s.MoviePilot.Enabled = true;
                    s.Scrobbling.Bangumi.Enabled = true;
                    s.Scrobbling.Douban.Enabled = true;
                    s.Ui.EnableImmersive = true;
                    s.Ui.UseAcrylicControls = true;
                },
                s =>
                {
                    s.Mpv.Pipeline = VideoPipelineKind.Standalone;
                    s.Mpv.CustomConfigEnabled = true;
                    s.Mpv.SvpEnabled = true;
                    s.Mpv.ScreenshotFormat = "jpg";
                    s.Video.FrameInterpolationMode = Momoka.Mpv.FrameInterpolationMode.MotionCompensated;
                    s.Playback.SubtitleBackStyle = "opaque-box";
                    s.Playback.SubtitleLanguages = ["简体中文", Momoka.Playback.TrackLanguagePriority.Any];
                    s.SubtitleTranslation.Enabled = true;
                    s.SubtitleTranslation.Provider = SubtitleTranslationProvider.Microsoft;
                    s.Ui.EnableImmersive = true;
                    s.Ui.UseAcrylicControls = false;
                },
                s =>
                {
                    s.Mpv.ScreenshotFormat = "webp";
                    s.Mpv.ScreenshotWebpLossless = true;
                    s.SubtitleTranslation.Enabled = true;
                    s.SubtitleTranslation.Provider = SubtitleTranslationProvider.OpenAi;
                    s.Ui.EnableImmersive = false;
                    s.Ui.StoreCornerArt = true;
                    s.Ui.ShowHomeBanner = false;
                }
            };

            string[] singleCards =
            [
                "PlayerCard", "CustomMpvConfigCard", "ResourceFilterCard", "ScreenshotCard", "VideoCard",
                "ApplicableShaderCard", "ApplicableHdrCard", "BufferCard", "FrameInterpolationCard",
                "TrailerCard", "DetailAppearanceCard", "DetailContentCard", "MoviePilotCard"
            ];
            string[] multiSections =
            [
                "HomeSections", "DetailLayoutCards", "PlaybackSections", "AudioSections", "SubtitleSections",
                "SubtitleTranslationSections", "DanmakuSections", "ShortcutSections", "ScrobblingSections",
                "AboutSections", "NetworkSections", "StartupSections"
            ];

            foreach (var configure in scenarios)
            {
                var settings = new SettingsPageStateTests.FakeSettings();
                configure(settings.Settings);
                var vm = NewViewModel(settings);
                fixture.Attach(vm, settings, fixture.NewLauncher());
                vm.GetType().GetField("_shaders", Members)!.SetValue(vm, new Momoka.Services.ShaderStaging(settings.Settings));
                vm.GetType().GetField("_translationCredentials", Members)!.SetValue(vm,
                    new SubtitleTranslationCredentials(PassthroughSecretProtector.Instance));
                try
                {
                    AuditSections("single", singleCards.Select(name => fixture.Invoke(vm, name)!));
                    foreach (var name in multiSections)
                        AuditSections(name, (System.Collections.IEnumerable)fixture.Invoke(vm, name)!);
                    AuditRow("Interface", fixture.Invoke(vm, "AppearanceModeRow", settings.Settings.Ui)!);
                    AuditRow("Interface", fixture.Invoke(vm, "TransparencyRow", settings.Settings.Ui)!);
                    AuditRow("Interface", fixture.Invoke(vm, "InterfaceLanguageRow", settings.Settings.Ui)!);
                }
                finally { ((IDisposable)vm).Dispose(); }
            }

            foreach (var label in new[]
            {
                "字幕字号", "弹幕不透明度（%）", "主页栏目顺序", "详情页显示模式", "MoviePilot 连接",
                "长按快退倍率", "长按快进倍速", "直通 TrueHD", "其他语言", "其他字幕", "开机自启",
                "自动切换深浅", "深色", "浅色"
            })
                NoCjk("Static label: " + label, fixture.TranslateUiText(label));
            Assert.Equal("Subtitle font size", fixture.TranslateUiText("字幕字号"));
            Assert.Equal("TrueHD passthrough", fixture.TranslateUiText("直通 TrueHD"));
            Assert.Equal("Other languages", fixture.TranslateUiText("其他语言"));
            Assert.Equal("Other subtitles", fixture.TranslateUiText("其他字幕"));
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                NoCjk("Theme name: " + theme.Name, fixture.TranslateUiText(theme.Name));
                NoCjk("Theme note: " + theme.Note, fixture.TranslateUiText(theme.Note));
            }
            Assert.False(untranslated.Count > 0, "Untranslated UI strings:\n" + string.Join("\n", untranslated.Distinct().Take(80)));
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
