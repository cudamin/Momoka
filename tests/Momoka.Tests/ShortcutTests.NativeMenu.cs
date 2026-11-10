using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class ShortcutTests
{
    private static void RegisterNativeMenu()
    {
        Test("扩展播放菜单：调色、滤镜与主次字幕都有可执行快捷键", () =>
        {
            foreach (var id in new[] { "video-aspect-cycle", "video-rotate-left", "video-rotate-right",
                "video-crop", "video-reset", "video-icc", "video-nonlinear", "contrast-decrease",
                "hue-increase", "color-reset", "deband-toggle", "hdr-visualize", "video-next",
                "secondary-subtitle-next", "secondary-subtitle-toggle", "secondary-subtitle-delay-decrease",
                "subtitle-blend", "subtitle-color-compat", "subtitle-border-scale", "screenshot-subtitles",
                "screenshot-each-frame-video", "osd-timeline", "vf-flip-horizontal" })
            {
                Assert.True(ShortcutCatalog.Actions.Any(action => action.Id == id), id);
                var plan = AdditionalShortcuts.Plan(id);
                Assert.NotNull(plan, id);
                Assert.True(plan!.Commands.Count > 0, id);
                Assert.True(plan.Notice.Length > 0, id);
            }
        });
        Test("扩展播放菜单：左右旋转不抢走已有字幕跳句快捷键", () =>
        {
            var resolved = ShortcutCatalog.Resolve(Empty());
            Assert.Equal("subtitle-seek-previous", ShortcutCatalog.Lookup(Empty(), Key("Left", ctrl: true)));
            Assert.Equal("subtitle-seek-next", ShortcutCatalog.Lookup(Empty(), Key("Right", ctrl: true)));
            Assert.True(resolved.ContainsKey("video-rotate-left"));
            Assert.True(ShortcutCatalog.DefaultsAreUnique());
        });
        Test("扩展播放菜单：菜单与快捷键共用完整命令和通知", () =>
        {
            foreach (var node in PlayerMenuCatalog.Commands.Where(node => node.Shortcut is not null))
            {
                if (AdditionalShortcuts.Plan(node.Shortcut!) is not { } plan) continue;
                Assert.Equal(string.Join(';', node.Commands.Select(command => string.Join('|', command))),
                    string.Join(';', plan.Commands.Select(command => string.Join('|', command))), node.Shortcut!);
                Assert.Equal(node.Notice, plan.Notice, node.Shortcut!);
            }
        });
        Test("扩展播放菜单：新增属性跟随换片复位而轨道选择不混入默认属性", () =>
        {
            foreach (var name in new[] { "sigmoid-upscaling", "d3d11-flip",
                "tone-mapping-visualize", "blend-subtitles", "sub-vsfilter-bidi-compat", "sub-ass-style-overrides",
                "sub-ass-vsfilter-color-compat", "sub-ass-use-video-data", "hr-seek-framedrop" })
                Assert.True(InlineSwitch.PerFilmNames.Contains(name), name);
            foreach (var name in new[] { "chapter", "vid", "sid", "aid" })
                Assert.False(InlineSwitch.PerFilmNames.Contains(name), name);
        });
        Test("扩展播放菜单：每个新增动作都执行真实目录，要求属性完整且恢复入口可达", () =>
        {
            foreach (var action in ShortcutCatalog.Actions.Skip(70).Where(action => !PlayerToolShortcuts.Contains(action.Id)))
            {
                var node = AdditionalShortcuts.Plan(action.Id);
                Assert.NotNull(node, action.Id);
                Assert.True(node!.Commands.Count > 0, action.Id);
                foreach (var command in node.Commands.Where(command => command.Count > 1
                    && command[0] is "set" or "cycle" or "cycle-values" or "add"))
                    if (command[1] != "chapter") Assert.True(node.RequireProperties.Contains(command[1]), action.Id);
            }
            var reset = AdditionalShortcuts.Plan("subtitle-reset")!;
            Assert.Equal(PlayerMenuRequirement.Subtitle, reset.Requirement);
            Assert.True(reset.Commands.Any(command => command[1] == "secondary-sub-delay"));
            Assert.True(AdditionalShortcuts.Plan("screenshot-each-frame-video")!.Requirement.HasFlag(PlayerMenuRequirement.ManagedCapture));
            Assert.False(PlayerMenuCatalog.Commands.Any(node => node.Commands.Any(command => command.Contains("vd-lavc-assume-old-x264"))));
        });
        Test("播放分析：已装箱脚本入口与能力要求共用目录", () =>
        {
            foreach (var id in new[] { "video-detect-crop", "skip-to-silence" })
            {
                var node = AdditionalShortcuts.Plan(id)!;
                Assert.Equal("script-message-to", node.Commands.Single()[0]);
                Assert.Equal(MpvAnalysis.ScriptName, node.Commands.Single()[1]);
                Assert.True(node.RequireProperties.Contains(MpvAnalysis.ReadyProperty));
                Assert.Equal("", node.Notice);
            }
            foreach (var value in new[] { "yes", "true", "1", "\"true\"" }) Assert.True(MpvAnalysis.IsTrue(value));
            Assert.False(MpvAnalysis.IsTrue("false"));
        });
        Test("扩展播放菜单：标点键可持久化并转换成mpv名称", () =>
        {
            foreach (var pair in new[] { ("Minus", "-"), ("Equal", "="), ("Comma", ","),
                ("Period", "."), ("Backquote", "`") })
            {
                var stroke = Key(pair.Item1, ctrl: true);
                Assert.True(ShortcutCatalog.TryParse(ShortcutCatalog.Serialize(stroke), out var parsed));
                Assert.Equal(stroke, parsed);
                Assert.Equal("Ctrl+" + pair.Item2, VideoWindowShortcuts.Key(stroke));
                Assert.Equal("Ctrl + " + pair.Item2, ShortcutCatalog.Format(stroke));
            }
        });
    }
}
