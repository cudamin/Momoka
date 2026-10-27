using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.Loader;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>
/// 正式测试开关显式加载本树当前 Shell Release；普通 Core 运行不自动登记本套件。
/// 真 SettingsViewModel、真卡片/行属性回调配假内存偏好与 Task 委托，不构造 WinUI 或读取颜色画刷。
/// 导入/默认覆盖是 Core Apply 后重建这些真卡，非完整恢复对话框或整页 Reload/UI 验证。
/// </summary>
internal static class SettingsApplicabilityTests
{
    private const string ShaderTitle = "画质与着色器";
    private const string HdrTitle = "HDR 与杜比视界";
    private const string SubtitleTitle = "字幕";

    /// <summary>2026-10-27 起字幕分类拆成三张卡：字体、预览、底板那几行都在「字幕外观」这一张。</summary>
    private const string SubtitleAppearanceTitle = "字幕外观";
    private static readonly string[] ShaderDependents = ["显卡档位", "手动指定档位", "自动识别动画", "老片源修复", "8K 片源关闭着色器"];
    private static readonly string[] BackRows = ["底板颜色", "底板不透明度（%）"];

    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            Register(shellAssemblyPath, sourceRoot);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    public static void Register(string shellAssemblyPath, string sourceRoot)
    {
        var shell = new ShellFixture(shellAssemblyPath, sourceRoot);
        TestHarness.Test("适用性：初建与 cached 重入接线使用相同规则（源码检查，非 UI）", () =>
        {
            Assert.Contains("Sections.Add(ApplicableShaderCard());", shell.Source);
            Assert.Contains("Sections.Add(ApplicableHdrCard());", shell.Source);
            Assert.Contains("RefreshSettingsApplicability();", shell.Source);
        });
        foreach (var enabled in new[] { false, true })
        {
            TestHarness.Test($"R-049 初建 shader={enabled} 的真实行集合/整行说明", () =>
            {
                var state = shell.Create(settings => settings.Shaders.Enabled = enabled);
                ShaderVisibility(state, enabled);
                Assert.Equal(0, state.Settings.Saves);
            });
        }
        TestHarness.Test("R-049 同页关→开→关→开只更新从属行，偏好/预设/集合保留", () => ShaderTransitions(shell));
        TestHarness.Test("R-049 总关时预设仍可编辑且只保存一次", () =>
        {
            var state = shell.Create(settings => settings.Shaders.Enabled = false);
            state.Choose(ShaderTitle, "画质预设", "high-quality");
            Assert.Equal("high-quality", state.Settings.Settings.Video.QualityPreset);
            ShaderVisibility(state, false);
            Assert.Equal(1, state.Settings.Saves);
            Assert.Equal(0, state.Pushes);
        });
        // 「新增功能开关，播放动漫时自动启用 anime4k」（用户的话，2026-10-27）。这一行只在「自动识别动画」
        // 打开时才有片源可选，所以跟着它收放；收起时不改存着的偏好，恢复后原值回来。
        TestHarness.Test("R-049 动画自动使用 Anime4K 跟随自动识别动画显隐，隐藏不清空偏好", () =>
        {
            var state = shell.Create(settings => settings.Shaders.AutoAnimeProfile = true);
            Assert.True(state.Labels(ShaderTitle).Contains("动画自动使用 Anime4K"));
            state.Toggle(ShaderTitle, "动画自动使用 Anime4K", true);
            Assert.True(state.Settings.Settings.Shaders.AutoAnime4K);

            state.Toggle(ShaderTitle, "自动识别动画", false);
            Assert.False(state.Labels(ShaderTitle).Contains("动画自动使用 Anime4K"),
                "识别关掉后这一行选不中任何片源，应当收起");
            Assert.True(state.Settings.Settings.Shaders.AutoAnime4K, "隐藏不清空偏好");

            state.Toggle(ShaderTitle, "自动识别动画", true);
            Assert.True(state.Labels(ShaderTitle).Contains("动画自动使用 Anime4K"));
            Assert.True(state.Get<bool>(state.Row(ShaderTitle, "动画自动使用 Anime4K"), "IsOn"),
                "条件恢复后重新显示原值");
            Assert.Equal(3, state.Settings.Saves);
        });

        foreach (var mode in new[] { "tonemap", "", "passthrough" })
        {
            TestHarness.Test($"R-050 初建 HDR mode={ModeName(mode)} 的真实峰值行集合", () =>
            {
                var state = shell.Create(settings => settings.Video.HdrMode = mode);
                HdrVisibility(state, mode != "tonemap");
                Assert.Equal(0, state.Settings.Saves);
            });
        }
        TestHarness.Test("R-050 同页 SDR→HDR→自动→SDR，旧手动峰值恢复且不多保存", () => HdrTransitions(shell));

        foreach (var style in new[] { "", "opaque-box", "background-box" })
            foreach (var shadow in new[] { "0", "0.5", "" })
            {
                TestHarness.Test($"R-055 初建 style={ModeName(style)} shadow={ModeName(shadow)} 的真实底板两行", () =>
                {
                    var state = shell.Create(settings =>
                    {
                        settings.Playback.SubtitleBackStyle = style;
                        settings.Playback.SubtitleShadowOffset = shadow;
                    });
                    BackVisibility(state, style == "background-box" || shadow != "0");
                    Assert.Equal(0, state.Settings.Saves);
                });
            }
        foreach (var style in new[] { "", "opaque-box" })
        {
            TestHarness.Test($"R-055 {ModeName(style)} 同页阴影0→正→0→正，颜色/透明度与草稿保留", () =>
                SubtitleTransitions(shell, style));
        }
        TestHarness.Test("R-055 零留白时普通→整体盒→逐行盒→整体盒，恢复入口与原值保留", () =>
        {
            var state = shell.Create(settings => settings.Playback.SubtitleShadowOffset = "0");
            var font = state.Row(SubtitleAppearanceTitle, "字体");
            state.Set(font, "BoxText", "未完成字体检索");
            foreach (var style in new[] { "background-box", "opaque-box", "background-box", "" })
            {
                state.Choose(SubtitleAppearanceTitle, "字幕底板", style);
                BackVisibility(state, style == "background-box");
                Assert.Equal("#123456", state.Settings.Settings.Playback.SubtitleBackColor);
                Assert.Equal(37, state.Settings.Settings.Playback.SubtitleBackOpacity);
                Assert.True(ReferenceEquals(font, state.Row(SubtitleAppearanceTitle, "字体")));
                Assert.Equal("未完成字体检索", state.Get<string>(font, "BoxText"));
            }
            Assert.Equal(4, state.Settings.Saves);
            Assert.Equal(4, state.Pushes);
        });
        TestHarness.Test("R-055 底板色空不隐藏透明度；正阴影切盒型更新在场真颜色行说明", () =>
        {
            var state = shell.Create(settings => settings.Playback.SubtitleBackColor = "");
            var color = state.Row(SubtitleAppearanceTitle, "底板颜色");
            var opacity = state.Row(SubtitleAppearanceTitle, "底板不透明度（%）");
            state.Choose(SubtitleAppearanceTitle, "字幕底板", "opaque-box");
            Assert.True(ReferenceEquals(color, state.Row(SubtitleAppearanceTitle, "底板颜色")));
            Assert.Contains("逐行盒", state.Get<string>(color, "Note"));
            state.Choose(SubtitleAppearanceTitle, "字幕底板", "background-box");
            Assert.True(ReferenceEquals(color, state.Row(SubtitleAppearanceTitle, "底板颜色")));
            Assert.Contains("所有文字行", state.Get<string>(color, "Note"));
            Assert.True(ReferenceEquals(opacity, state.Row(SubtitleAppearanceTitle, "底板不透明度（%）")));
            state.Set(opacity, "Value", 61d);
            Assert.Equal("", state.Settings.Settings.Playback.SubtitleBackColor);
            Assert.Equal(61, state.Settings.Settings.Playback.SubtitleBackOpacity);
            Assert.Equal(3, state.Settings.Saves);
            Assert.Equal(3, state.Pushes);
        });

        TestHarness.Test("三号：共享偏好改变后 cached 真 VM 入口重算，草稿/分类/集合保持", () => CachedReentry(shell));
        TestHarness.Test("三号：Core 假导入 Apply 后真卡重建并可恢复，非完整导入 UI", () => PreferencesRebuild(shell, defaults: false));
        TestHarness.Test("三号：Core 默认 Apply 后真卡重建，非完整恢复默认 UI", () => PreferencesRebuild(shell, defaults: true));
        foreach (var issue in new[] { "R-049", "R-050", "R-055" })
        {
            TestHarness.Test($"{issue} 假保存失败仍更新适用性且仅尝试保存一次", () =>
            {
                var state = shell.Create();
                state.Settings.SaveSucceeds = false;
                if (issue == "R-049") { state.Toggle(ShaderTitle, "启用着色器", false); ShaderVisibility(state, false); }
                else if (issue == "R-050") { state.Choose(HdrTitle, "HDR 处理", "tonemap"); HdrVisibility(state, false); }
                else { state.Shadow(0); BackVisibility(state, false); }
                Assert.Equal(1, state.Settings.Saves);
                Assert.True(state.Get<bool>(state.Vm, "HasUnsavedChanges"));
            });
        }
        TestHarness.Test("适用性：未 Attach/未建卡的 cached 入口安全无效", () =>
        {
            var vm = shell.NewViewModel();
            ((Task)shell.Invoke(vm, "RefreshAudioDevicesAsync")!).GetAwaiter().GetResult();
            shell.Invoke(vm, "SyncSaveState");
            Assert.Equal(0, shell.List(vm, "Sections").Count);
        });
    }

    private static void ShaderTransitions(ShellFixture shell)
    {
        var state = shell.Create();
        var card = state.Card(ShaderTitle);
        var rows = state.Rows(card);
        var preset = state.Row(ShaderTitle, "画质预设");
        var toggle = state.Row(ShaderTitle, "启用着色器");
        var preferences = state.Settings.Settings.Shaders.Snapshot();
        for (var step = 0; step < 4; step++)
        {
            var enabled = step % 2 != 0;
            state.Toggle(ShaderTitle, "启用着色器", enabled);
            ShaderVisibility(state, enabled);
            Assert.True(ReferenceEquals(card, state.Card(ShaderTitle)));
            Assert.True(ReferenceEquals(rows, state.Rows(card)));
            Assert.True(ReferenceEquals(preset, state.Row(ShaderTitle, "画质预设")));
            Assert.True(ReferenceEquals(toggle, state.Row(ShaderTitle, "启用着色器")));
            var actual = state.Settings.Settings.Shaders;
            Assert.Equal(preferences.Gpu, actual.Gpu);
            Assert.Equal(preferences.ManualGroup, actual.ManualGroup);
            Assert.Equal(preferences.AutoAnimeProfile, actual.AutoAnimeProfile);
            Assert.Equal(preferences.RestoreVintageSources, actual.RestoreVintageSources);
            Assert.Equal(preferences.DisableForUltraHighRes, actual.DisableForUltraHighRes);
            if (enabled)
            {
                Assert.Contains("高档", state.SelectedLabel(ShaderTitle, "显卡档位"));
                Assert.False(state.Get<bool>(state.Row(ShaderTitle, "自动识别动画"), "IsOn"));
                Assert.False(state.Get<bool>(state.Row(ShaderTitle, "老片源修复"), "IsOn"));
                Assert.False(state.Get<bool>(state.Row(ShaderTitle, "8K 片源关闭着色器"), "IsOn"));
            }
        }
        Assert.Equal(4, state.Settings.Saves);
        Assert.Equal(0, state.Pushes);
    }

    private static void HdrTransitions(ShellFixture shell)
    {
        var state = shell.Create(settings => settings.Video.HdrMode = "tonemap");
        var card = state.Card(HdrTitle);
        var rows = state.Rows(card);
        var referenceWhite = state.Row(HdrTitle, "HDR 参考白亮度");
        var contrast = state.Row(HdrTitle, "HDR 对比度恢复");
        state.Set(contrast, "Value", 0.71d); // Automatic 下未提交输入，应保留而非整卡重建。
        foreach (var mode in new[] { "passthrough", "", "tonemap", "passthrough", "tonemap" })
        {
            state.Choose(HdrTitle, "HDR 处理", mode);
            HdrVisibility(state, mode != "tonemap");
            Assert.Equal<double?>(731, state.Settings.Settings.Video.HdrPeakNits);
            Assert.True(ReferenceEquals(card, state.Card(HdrTitle)));
            Assert.True(ReferenceEquals(rows, state.Rows(card)));
            Assert.True(ReferenceEquals(referenceWhite, state.Row(HdrTitle, "HDR 参考白亮度")));
            Assert.True(ReferenceEquals(contrast, state.Row(HdrTitle, "HDR 对比度恢复")));
            Assert.Equal(0.71d, state.Get<double>(contrast, "Value"));
            Assert.Null(state.Settings.Settings.Video.HdrContrastRecovery);
            if (mode != "tonemap")
            {
                Assert.False(state.Get<bool>(state.Row(HdrTitle, "HDR 输出峰值亮度"), "Automatic"));
                Assert.Equal(731d, state.Get<double>(state.Row(HdrTitle, "HDR 输出峰值亮度"), "Value"));
            }
        }
        Assert.Equal(5, state.Settings.Saves);
        Assert.Equal(0, state.Pushes);
    }

    private static void SubtitleTransitions(ShellFixture shell, string style)
    {
        var state = shell.Create(settings => settings.Playback.SubtitleBackStyle = style);
        var card = state.Card(SubtitleAppearanceTitle);
        var rows = state.Rows(card);
        var font = state.Row(SubtitleAppearanceTitle, "字体");
        var preview = state.Row(SubtitleAppearanceTitle, "字幕示例");
        state.Set(font, "BoxText", "字体草稿未选定");
        var matches = state.List(font, "Matches");
        foreach (var shadow in new[] { 0d, 1d, 0d, 0.5d })
        {
            state.Shadow(shadow);
            BackVisibility(state, shadow > 0);
            Assert.True(ReferenceEquals(card, state.Card(SubtitleAppearanceTitle)));
            Assert.True(ReferenceEquals(rows, state.Rows(card)));
            Assert.True(ReferenceEquals(font, state.Row(SubtitleAppearanceTitle, "字体")));
            Assert.True(ReferenceEquals(preview, state.Row(SubtitleAppearanceTitle, "字幕示例")));
            Assert.True(ReferenceEquals(matches, state.List(font, "Matches")));
            Assert.Equal("字体草稿未选定", state.Get<string>(font, "BoxText"));
            Assert.Equal("#123456", state.Settings.Settings.Playback.SubtitleBackColor);
            Assert.Equal(37, state.Settings.Settings.Playback.SubtitleBackOpacity);
            if (shadow > 0)
            {
                Assert.Equal("#123456", state.Get<string>(state.Row(SubtitleAppearanceTitle, "底板颜色"), "Color"));
                Assert.Equal(37d, state.Get<double>(state.Row(SubtitleAppearanceTitle, "底板不透明度（%）"), "Value"));
            }
        }
        Assert.Equal(4, state.Settings.Saves);
        Assert.Equal(4, state.Pushes);
    }

    private static void CachedReentry(ShellFixture shell)
    {
        var state = shell.Create();
        var sections = state.List(state.Vm, "Sections");
        var cards = sections.Cast<object>().ToArray();
        var rows = cards.Select(state.Rows).ToArray();
        var categories = state.List(state.Vm, "Categories");
        state.Set(state.Vm, "SelectedCategory", SubtitleTitle);
        state.Set(state.Vm, "IsReady", true);
        var selected = state.Get<object>(state.Vm, "SelectedEntry");
        var font = state.Row(SubtitleAppearanceTitle, "字体");
        state.Set(font, "BoxText", "跨页仍未选定");
        state.Settings.Settings.Shaders.Enabled = false;
        state.Settings.Settings.Video.HdrMode = "tonemap";
        state.Settings.Settings.Playback.SubtitleShadowOffset = "0";
        state.Reenter();
        ShaderVisibility(state, false);
        HdrVisibility(state, false);
        BackVisibility(state, false);
        Assert.False(state.Get<Momoka.Playback.SubtitlePreviewPlan>(state.Row(SubtitleAppearanceTitle, "字幕示例"), "Model")
            .Layers.Any(layer => layer.Color == "#123456"), "共享阴影归零后，示意模型也不能保留旧阴影");
        Assert.True(ReferenceEquals(sections, state.List(state.Vm, "Sections")));
        Assert.True(sections.Cast<object>().SequenceEqual(cards, ReferenceEqualityComparer.Instance));
        Assert.True(cards.Select(state.Rows).SequenceEqual(rows, ReferenceEqualityComparer.Instance));
        Assert.True(ReferenceEquals(categories, state.List(state.Vm, "Categories")));
        Assert.True(ReferenceEquals(selected, state.Get<object>(state.Vm, "SelectedEntry")));
        Assert.Equal(SubtitleTitle, state.Get<string>(state.Vm, "SelectedCategory"));
        Assert.Equal("跨页仍未选定", state.Get<string>(font, "BoxText"));
        Assert.Equal(0d, state.Get<double>(state.Row(SubtitleAppearanceTitle, "阴影偏移 / 背景盒留白"), "Value"),
            "共享条件已变，重入控制入口不能仍显示旧阴影");
        var changes = 0;
        foreach (var list in rows) ((INotifyCollectionChanged)list).CollectionChanged += (_, _) => changes++;
        state.Reenter();
        Assert.Equal(0, changes, "条件没变，不制造重排");
        state.Settings.Settings.Shaders.Enabled = true;
        state.Settings.Settings.Video.HdrMode = "passthrough";
        state.Settings.Settings.Playback.SubtitleBackStyle = "background-box";
        state.Reenter();
        ShaderVisibility(state, true);
        HdrVisibility(state, true);
        BackVisibility(state, true);
        Assert.True(state.Get<Momoka.Playback.SubtitlePreviewPlan>(state.Row(SubtitleAppearanceTitle, "字幕示例"), "Model").Plate,
            "重入采用整体背景盒时，示意模型也须同步；不是实际 libass 渲染验收");
        Assert.Equal(MpvOutputOptions.SubtitleBackStyles.Single(option => option.Value == "background-box").Label,
            state.SelectedLabel(SubtitleAppearanceTitle, "字幕底板"), "恢复入口须显示共享偏好的当前模式");
        Assert.Equal(0, state.Settings.Saves);
        Assert.Equal(0, state.Pushes);
        Assert.Equal("跨页仍未选定", state.Get<string>(font, "BoxText"));
        // 重入同步不能把回调接到未采用的临时行：随后真实控制入口仍可恢复/关闭两行。
        state.Choose(SubtitleAppearanceTitle, "字幕底板", "");
        BackVisibility(state, false);
        state.Shadow(1);
        BackVisibility(state, true);
        Assert.Equal(2, state.Settings.Saves);
        Assert.Equal(2, state.Pushes);
        Assert.Equal(1d, state.Get<double>(state.Row(SubtitleAppearanceTitle, "阴影偏移 / 背景盒留白"), "Value"),
            "重入后真实滑块编辑应显示新值，不被旧重入值覆盖");
    }

    private static void PreferencesRebuild(ShellFixture shell, bool defaults)
    {
        var state = shell.Create();
        var live = state.Settings.Settings;
        var shaders = live.Shaders;
        var video = live.Video;
        var playback = live.Playback;
        var source = new AppSettings();
        if (!defaults)
        {
            source.Playback.SubtitleShadowOffset = "0";
            source.Playback.SubtitleBackStyle = "opaque-box";
            source.Playback.SubtitleBackColor = "#AC5D5D";
            source.Playback.SubtitleBackOpacity = 61;
            source.Video.HdrPeakNits = 823;
        }
        SettingsPreferences.Apply(live, source); // 只在内存应用假偏好文档，无读取/写入配置。
        state.Rebuild();
        ShaderVisibility(state, false);
        HdrVisibility(state, false);
        BackVisibility(state, defaults);
        Assert.True(ReferenceEquals(shaders, live.Shaders));
        Assert.True(ReferenceEquals(video, live.Video));
        Assert.True(ReferenceEquals(playback, live.Playback));
        Assert.Equal(0, state.Settings.Saves);
        if (!defaults)
        {
            state.Toggle(ShaderTitle, "启用着色器", true);
            state.Choose(HdrTitle, "HDR 处理", "passthrough");
            state.Shadow(1);
            Assert.Equal(823d, state.Get<double>(state.Row(HdrTitle, "HDR 输出峰值亮度"), "Value"));
            Assert.Equal("#AC5D5D", state.Get<string>(state.Row(SubtitleAppearanceTitle, "底板颜色"), "Color"));
            Assert.Equal(61d, state.Get<double>(state.Row(SubtitleAppearanceTitle, "底板不透明度（%）"), "Value"));
            Assert.Equal(3, state.Settings.Saves);
        }
    }

    private static void ShaderVisibility(State state, bool enabled)
    {
        var labels = state.Labels(ShaderTitle);
        Assert.True(labels.Take(2).SequenceEqual(new[] { "画质预设", "启用着色器" }));
        foreach (var dependent in ShaderDependents) Assert.Equal(enabled, labels.Contains(dependent), dependent);
        Assert.Equal(enabled ? 7 : 2, labels.Length, "从属整行连同说明被移出 ItemsSource");
        Assert.Equal($"{labels.Length} 项", state.Get<string>(state.Card(ShaderTitle), "Count"));
    }

    private static void HdrVisibility(State state, bool visible)
    {
        Assert.Equal(visible, state.Labels(HdrTitle).Contains("HDR 输出峰值亮度"));
        Assert.NotNull(state.Row(HdrTitle, "HDR 处理"));
        Assert.NotNull(state.Row(HdrTitle, "HDR 参考白亮度"));
        Assert.Equal(visible ? 12 : 11, state.Labels(HdrTitle).Length);
    }

    private static void BackVisibility(State state, bool visible)
    {
        foreach (var row in BackRows) Assert.Equal(visible, state.Labels(SubtitleAppearanceTitle).Contains(row), row);
        Assert.NotNull(state.Row(SubtitleAppearanceTitle, "阴影偏移 / 背景盒留白"));
        Assert.NotNull(state.Row(SubtitleAppearanceTitle, "字幕底板"));
        // 字幕编码 2026-10-27 起在「编码与画面」那张卡上，不在外观卡里。
        Assert.NotNull(state.Row("编码与画面", "字幕编码"));
        // 外观卡：示例、外观应用范围、字体、字号、缩放、加粗、文字颜色、描边大小、描边颜色、阴影、字幕底板
        // = 11 行；底板打开时多出「底板颜色」「底板不透明度」两行。
        Assert.Equal(visible ? 13 : 11, state.Labels(SubtitleAppearanceTitle).Length);
    }

    private static string ModeName(string mode) => mode.Length == 0 ? "未指定" : mode;

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int PageSize => 50;
        public int Saves { get; private set; }
        public bool SaveSucceeds { get; set; } = true;
        public event EventHandler? SaveStateChanged { add { } remove { } }
        public bool HasUnsavedChanges { get; private set; }
        public bool TrySave() { Saves++; HasUnsavedChanges = !SaveSucceeds; return SaveSucceeds; }
        public void Save() => throw new InvalidOperationException("只允许假 TrySave");
        public ServerProfile ResolveServer(string url) => throw new InvalidOperationException("禁止登录路径");
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new InvalidOperationException("禁止账号路径");
        public void ExportPreferences(string path) => throw new InvalidOperationException("禁止配置 IO");
        public AppSettings ReadBackup(string path) => throw new InvalidOperationException("禁止配置 IO");
    }

    private sealed class ShellFixture
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly Type _vm;
        public string Source { get; }
        public ShellFixture(string shellAssemblyPath, string sourceRoot)
        {
            var root = Path.GetFullPath(sourceRoot);
            var path = Path.GetFullPath(shellAssemblyPath);
            var release = Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar;
            if (!File.Exists(Path.Combine(root, "Momoka.sln")) || !path.StartsWith(release, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(path), "Momoka.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("只加载本树当前 x64 Release Shell 程序集");
            var directory = Path.GetDirectoryName(path)!;
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            };
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            _vm = assembly.GetType("Momoka.Shell.ViewModels.SettingsViewModel", throwOnError: true)!;
            Source = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "ViewModels", "SettingsViewModel.cs"));
        }
        public object NewViewModel() => Activator.CreateInstance(_vm)!;
        public object? Invoke(object target, string name, params object?[] args) =>
            (target.GetType().GetMethod(name, Members) ?? throw new AssertionException("缺入口 " + name)).Invoke(target, args);
        public T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;
        public void Set(object target, string name, object value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
        public IList List(object target, string name) => Get<IList>(target, name);
        public State Create(Action<AppSettings>? configure = null) => new(this, configure);
        public string Builder(string applicable, string original) => _vm.GetMethod(applicable, Members) is not null ? applicable : original;
    }

    private sealed class State
    {
        private readonly ShellFixture _shell;
        public object Vm { get; }
        public FakeSettings Settings { get; } = new();
        public int Pushes { get; private set; }
        public State(ShellFixture shell, Action<AppSettings>? configure)
        {
            _shell = shell;
            var settings = Settings.Settings;
            settings.Shaders.Enabled = true;
            settings.Shaders.Gpu = GpuTier.High;
            settings.Shaders.ManualGroup = new ShaderStaging(settings).Catalog[1].Id;
            settings.Shaders.AutoAnimeProfile = false;
            settings.Shaders.RestoreVintageSources = false;
            settings.Shaders.DisableForUltraHighRes = false;
            settings.Video.HdrMode = "";
            settings.Video.HdrPeakNits = 731;
            settings.Playback.SubtitleBackColor = "#123456";
            settings.Playback.SubtitleBackOpacity = 37;
            settings.Mpv.Backend = MpvBackendKind.ExternalMpv; // 后端仅是假偏好，不起进程；避免构建无关自定义配置卡。
            configure?.Invoke(settings);
            Vm = shell.NewViewModel();
            var attach = Vm.GetType().GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var args = attach.GetParameters().Select(parameter => parameter.Name switch
            {
                "settings" => (object)Settings,
                "shaders" => new ShaderStaging(settings),
                "pushSubtitleStyle" => (Func<Task>)(() => { Pushes++; return Task.CompletedTask; }),
                _ => null
            }).ToArray();
            shell.Invoke(Vm, "Attach", args);
            Rebuild();
        }
        public void Rebuild()
        {
            var sections = _shell.List(Vm, "Sections");
            sections.Clear();
            // 修前用原真实卡，修后使用 Reload 已接入的适用性卡；静态接线测试另外核对正式调用。
            foreach (var method in new[] { "VideoCard", _shell.Builder("ApplicableShaderCard", "ShaderCard"),
                _shell.Builder("ApplicableHdrCard", "HdrCard") })
                sections.Add(_shell.Invoke(Vm, method)!);
            // 字幕 2026-10-27 拆成三张卡，建卡入口返回一份列表，逐张加。
            foreach (var section in (IEnumerable)_shell.Invoke(Vm, "SubtitleSections")!) sections.Add(section);
        }
        public object Card(string title) => _shell.List(Vm, "Sections").Cast<object>().Single(card => Get<string>(card, "Title") == title);
        public IList Rows(object card) => _shell.List(card, "Rows");
        public object Row(string title, string label) => Rows(Card(title)).Cast<object>().Single(row => Get<string>(row, "Label") == label);
        public string[] Labels(string title) => Rows(Card(title)).Cast<object>().Select(row => Get<string>(row, "Label")).ToArray();
        public T Get<T>(object target, string name) => _shell.Get<T>(target, name);
        public void Set(object target, string name, object value) => _shell.Set(target, name, value);
        public IList List(object target, string name) => _shell.List(target, name);
        public void Toggle(string title, string label, bool value) => Set(Row(title, label), "IsOn", value);
        public void Shadow(double value) => Set(Row(SubtitleAppearanceTitle, "阴影偏移 / 背景盒留白"), "Value", value);
        public string SelectedLabel(string title, string label) => Get<string>(Get<object>(Row(title, label), "Selected"), "Label");
        public void Choose(string title, string label, string value)
        {
            var row = Row(title, label);
            var options = label switch
            {
                "画质预设" => MpvOutputOptions.QualityPresets,
                "HDR 处理" => MpvOutputOptions.HdrModes,
                "字幕底板" => MpvOutputOptions.SubtitleBackStyles,
                _ => throw new AssertionException("未知选项组")
            };
            var choiceLabel = options.Single(option => option.Value == value).Label;
            var choice = Get<IEnumerable>(row, "Choices").Cast<object>().Single(choice => Get<string>(choice, "Label") == choiceLabel);
            Set(row, "Selected", choice);
        }
        public void Reenter() => ((Task)_shell.Invoke(Vm, "RefreshAudioDevicesAsync")!).GetAwaiter().GetResult();
    }
}
