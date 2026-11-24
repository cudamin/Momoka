using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 播放器快捷键那张表的判断，全在 <see cref="ShortcutCatalog"/>（Core 纯函数）。这些是它的钉子 —— 单元测试进不到
/// 外壳那个程序集，而「这个键触发哪个动作」「绑到已占用的键上会不会拦下」「存文件长什么样、显示成什么样」都是
/// 屏上看不出对错、编译也看不出的判断。
/// </summary>
internal static partial class ShortcutTests
{
    private static Dictionary<string, string> Empty() => new(StringComparer.Ordinal);

    private static KeyStroke Key(string key, bool ctrl = false, bool alt = false, bool shift = false) =>
        new(key, ctrl, alt, shift);

    public static void Register()
    {
        RegisterNativeMenu();
        Test("快捷键表：新增动作保留原有动作，Id 不重复、默认键不重复、没有一个默认落在保留键上", () =>
        {
            Assert.Equal(151, ShortcutCatalog.Actions.Count);

            var ids = ShortcutCatalog.Actions.Select(a => a.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count(), "动作 Id 有重复");

            Assert.True(ShortcutCatalog.DefaultsAreUnique(), "装机就带着两个动作共用一个默认键 —— Rebind 拦不住这种，只有测试拦得住");

            foreach (var action in ShortcutCatalog.Actions)
                Assert.False(ShortcutCatalog.IsReserved(action.Default), $"{action.Id} 的默认键是保留键");
        });

        Test("快捷键表：弹幕不可用时收起那四条，可用时一条不少，次序照旧", () =>
        {
            // 设置页卡片与自检都读这一个判据（ShortcutCatalog.Available）。两处各写一份时，
            // 2026-10-27 本机把弹幕关掉就红成「59 行（应当 63 行）」—— 卡片是对的，判据错了。
            var all = ShortcutCatalog.Available(danmakuReady: true);
            var withoutDanmaku = ShortcutCatalog.Available(danmakuReady: false);

            Assert.Equal(ShortcutCatalog.Actions.Count, all.Count);
            Assert.Equal(ShortcutCatalog.Actions.Count - 4, withoutDanmaku.Count);
            Assert.Equal("toggle-danmaku,danmaku-delay-decrease,danmaku-delay-increase,danmaku-delay-reset",
                string.Join(',', all.Except(withoutDanmaku).Select(action => action.Id)));
            Assert.True(withoutDanmaku.All(action => !ShortcutCatalog.IsDanmaku(action)), "收起之后还留着弹幕动作");
            Assert.Equal(
                string.Join(',', all.Where(action => !ShortcutCatalog.IsDanmaku(action)).Select(action => action.Id)),
                string.Join(',', withoutDanmaku.Select(action => action.Id)));
        });

        Test("快捷键表：默认键就是改造前那张 switch 表 —— 空格暂停、F 全屏、Shift+Z 字幕延迟增大", () =>
        {
            KeyStroke Default(string id) => ShortcutCatalog.Actions.First(a => a.Id == id).Default;

            Assert.Equal(Key("Space"), Default("toggle-pause"));
            Assert.Equal(Key("F"), Default("toggle-fullscreen"));
            Assert.Equal(Key("BracketLeft"), Default("speed-down"));
            Assert.Equal(Key("Z"), Default("subtitle-delay-decrease"));
            Assert.Equal(Key("Z", shift: true), Default("subtitle-delay-increase"));
            // 2026-11-20 添的那一颗：窗口不在前台时单击画面播放/暂停（B＝Background）。
            Assert.Equal(Key("B"), Default("toggle-background-click"));
        });

        Test("快捷键表：四颗方向键各归一对步长，音量让到 Ctrl+方向键（2026-09-20 那两句话）", () =>
        {
            KeyStroke Default(string id) => ShortcutCatalog.Actions.First(a => a.Id == id).Default;

            // 「新增键盘上的左和右设置为播放进度快退五秒和快进五秒」
            Assert.Equal(Key("Left"), Default("seek-backward"));
            Assert.Equal(Key("Right"), Default("seek-forward"));

            // 「上方向键和下方向键改为播放进度快进30秒和快退30秒」
            Assert.Equal(Key("Down"), Default("seek-backward-long"));
            Assert.Equal(Key("Up"), Default("seek-forward-long"));

            // 上下被这两条占走之后，音量剩下的那份：带着 Ctrl，不是没有 —— 一颗键只归一个动作，但音量
            // 这东西不该因为方向键改用处就从键盘上消失。
            Assert.Equal(Key("Up", ctrl: true), Default("volume-up"));
            Assert.Equal(Key("Down", ctrl: true), Default("volume-down"));

            // 四颗方向键的裸键一个都没剩下给别人，四条大步/短步也不必同名同物。
            Assert.Equal("快退（大步）", ShortcutCatalog.Label("seek-backward-long"));
            Assert.Equal("快进（大步）", ShortcutCatalog.Label("seek-forward-long"));
        });

        Test("Resolve：没动过用默认，空串是解绑，动过用动过的", () =>
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["toggle-mute"] = "K",      // 动过
                ["toggle-pin"] = ""         // 显式解绑
            };

            var effective = ShortcutCatalog.Resolve(bindings);

            Assert.Equal(Key("Space"), effective["toggle-pause"], "没动过的应当落到默认");
            Assert.Equal(Key("K"), effective["toggle-mute"], "动过的应当用动过的");
            Assert.True(effective["toggle-pin"].IsEmpty, "空串应当是解绑");
        });

        Test("Resolve：认不出的写法退回默认，而不是让这个动作没有键", () =>
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal) { ["toggle-pause"] = "A+B" };
            var effective = ShortcutCatalog.Resolve(bindings);
            Assert.Equal(Key("Space"), effective["toggle-pause"]);
        });

        Test("Lookup：默认键找得到动作、解绑和空键都找不到、改过的旧键落空", () =>
        {
            Assert.Equal("toggle-pause", ShortcutCatalog.Lookup(Empty(), Key("Space")));
            Assert.Equal("toggle-fullscreen", ShortcutCatalog.Lookup(Empty(), Key("F")));
            Assert.Equal("subtitle-delay-increase", ShortcutCatalog.Lookup(Empty(), Key("Z", shift: true)));
            Assert.Null(ShortcutCatalog.Lookup(Empty(), KeyStroke.None), "空键谁都不触发");

            // 把静音从 M 改到 K：K 找得到静音，M 从此谁都不是。
            var moved = new Dictionary<string, string>(StringComparer.Ordinal) { ["toggle-mute"] = "K" };
            Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(moved, Key("K")));
            Assert.Null(ShortcutCatalog.Lookup(moved, Key("M")), "旧键改走之后就不该再触发");
        });

        Test("Rebind：绑一个空键，存进字典、查得到", () =>
        {
            var result = ShortcutCatalog.Rebind(Empty(), "toggle-mute", Key("F12"));

            Assert.Null(result.Conflict);
            Assert.Equal("F12", result.Bindings["toggle-mute"]);
            Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(result.Bindings, Key("F12")));
        });

        Test("Rebind：默认拒绝冲突，确认替换后交换两个动作的按键", () =>
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal) { ["toggle-mute"] = "K" };
            var rejected = ShortcutCatalog.Rebind(bindings, "toggle-mute", Key("F")); // F 是全屏的默认键

            Assert.Equal("toggle-fullscreen", rejected.Conflict);
            Assert.Equal("K", rejected.Bindings["toggle-mute"], "未确认时绑定不变");

            var replaced = ShortcutCatalog.Rebind(bindings, "toggle-mute", Key("F"), replaceConflict: true);
            Assert.Null(replaced.Conflict);
            Assert.True(replaced.Replaced);
            Assert.Equal("F", replaced.Bindings["toggle-mute"]);
            Assert.Equal("K", replaced.Bindings["toggle-fullscreen"]);
            Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(replaced.Bindings, Key("F")));
            Assert.Equal("toggle-fullscreen", ShortcutCatalog.Lookup(replaced.Bindings, Key("K")));
            Assert.Null(ShortcutCatalog.Lookup(replaced.Bindings, Key("M")));
            Assert.Equal("K", bindings["toggle-mute"], "原字典不能被修改");
        });

        Test("Rebind：替换用户已自定义的占用键时，原动作接收新动作之前的有效键", () =>
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["toggle-fullscreen"] = "Ctrl+F8",
                ["toggle-mute"] = "K"
            };
            var result = ShortcutCatalog.Rebind(bindings, "toggle-mute", Key("F8", ctrl: true), replaceConflict: true);
            Assert.True(result.Replaced);
            Assert.Equal("Ctrl+F8", result.Bindings["toggle-mute"]);
            Assert.Equal("K", result.Bindings["toggle-fullscreen"]);
            Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(result.Bindings, Key("F8", ctrl: true)));
            Assert.Equal("toggle-fullscreen", ShortcutCatalog.Lookup(result.Bindings, Key("K")));
        });

        Test("Rebind：替换占用键时新动作原本未绑定，原占用者解绑", () =>
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal) { ["video-pan-left"] = "" };
            var result = ShortcutCatalog.Rebind(bindings, "video-pan-left", Key("F"), replaceConflict: true);

            Assert.True(result.Replaced);
            Assert.Equal("F", result.Bindings["video-pan-left"]);
            Assert.Equal("", result.Bindings["toggle-fullscreen"], "新动作原来没有键，原占用者应当解绑");
            Assert.Equal("video-pan-left", ShortcutCatalog.Lookup(result.Bindings, Key("F")));
            Assert.Null(ShortcutCatalog.Lookup(result.Bindings, Key("Left", alt: true)), "解绑后旧键不再触发任何动作");
            Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(result.Bindings, Key("M")));
        });

        Test("Rebind：绑回自己的默认键，就把这条改动删掉（回到「没动过」）", () =>
        {
            var moved = new Dictionary<string, string>(StringComparer.Ordinal) { ["toggle-mute"] = "K" };
            var result = ShortcutCatalog.Rebind(moved, "toggle-mute", Key("M")); // M 就是静音的默认

            Assert.Null(result.Conflict);
            Assert.False(result.Bindings.ContainsKey("toggle-mute"), "绑回默认应当删掉这条改动记录，而不是把默认再写一遍");
            Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(result.Bindings, Key("M")));
        });

        Test("Rebind：Esc、回车、Delete 可绑定，空键仍是空操作", () =>
        {
            var esc = ShortcutCatalog.Rebind(Empty(), "toggle-pause", Key("Escape"));
            Assert.Null(esc.Conflict);
            Assert.Equal("Escape", esc.Bindings["toggle-pause"]);

            var enter = ShortcutCatalog.Rebind(Empty(), "toggle-pause", Key("Enter"));
            Assert.Equal("Enter", enter.Bindings["toggle-pause"]);

            var delete = ShortcutCatalog.Rebind(Empty(), "toggle-pause", Key("Delete"));
            Assert.Equal("Delete", delete.Bindings["toggle-pause"]);

            var empty = ShortcutCatalog.Rebind(Empty(), "toggle-pause", KeyStroke.None);
            Assert.Equal(0, empty.Bindings.Count);
        });

        Test("Clear：解绑存空串，Resolve 变成没有键、Lookup 落空", () =>
        {
            var cleared = ShortcutCatalog.Clear(Empty(), "toggle-pause");

            Assert.Equal("", cleared["toggle-pause"], "解绑存的是空串，不是删键 —— 删键会退回默认");
            Assert.True(ShortcutCatalog.Resolve(cleared)["toggle-pause"].IsEmpty);
            Assert.Null(ShortcutCatalog.Lookup(cleared, Key("Space")), "解绑之后原来的默认键就不该再触发");
        });

        Test("Serialize / Parse：来回一趟不走样，只有修饰键或两个键都算解析失败", () =>
        {
            void RoundTrip(KeyStroke stroke)
            {
                Assert.True(ShortcutCatalog.TryParse(ShortcutCatalog.Serialize(stroke), out var back));
                Assert.Equal(stroke, back);
            }

            RoundTrip(Key("Space"));
            RoundTrip(Key("Z", shift: true));
            RoundTrip(Key("S", ctrl: true, alt: true));
            RoundTrip(Key("BracketLeft"));
            RoundTrip(Key("NumPad7", ctrl: true));
            RoundTrip(Key("F24", shift: true));
            RoundTrip(Key("MediaPlayPause"));
            RoundTrip(Key("VkFF", alt: true));
            for (var vk = 1; vk <= 0xFF; vk++)
                foreach (var extended in new[] { false, true })
                    RoundTrip(ShortcutKeyCatalog.Stroke(ShortcutKeyCatalog.Token(vk, extended)!, false, false, false));

            Assert.Equal("Shift+Z", ShortcutCatalog.Serialize(Key("Z", shift: true)));
            Assert.Equal("Ctrl+Alt+S", ShortcutCatalog.Serialize(Key("S", ctrl: true, alt: true)));
            Assert.Equal("", ShortcutCatalog.Serialize(KeyStroke.None));

            Assert.False(ShortcutCatalog.TryParse("", out _), "空串不是一次按键");
            Assert.False(ShortcutCatalog.TryParse("Ctrl", out _), "只有修饰键不是一次按键");
            Assert.False(ShortcutCatalog.TryParse("A+B", out _), "两个非修饰键不是一次按键");
        });

        Test("Format：修饰键带空格、特殊键翻成人话、空键是「未设置」", () =>
        {
            Assert.Equal("空格", ShortcutCatalog.Format(Key("Space")));
            Assert.Equal("←", ShortcutCatalog.Format(Key("Left")));
            Assert.Equal("[", ShortcutCatalog.Format(Key("BracketLeft")));
            Assert.Equal("Ctrl + Alt + S", ShortcutCatalog.Format(Key("S", ctrl: true, alt: true)));
            Assert.Equal("Shift + Z", ShortcutCatalog.Format(Key("Z", shift: true)));
            Assert.Equal("Del", ShortcutCatalog.Format(Key("Delete")));
            Assert.Equal("未设置", ShortcutCatalog.Format(KeyStroke.None));
        });

        Test("IsReserved：快捷键设置不再保留 Esc、回车和 Delete", () =>
        {
            Assert.False(ShortcutCatalog.IsReserved(Key("Escape")));
            Assert.False(ShortcutCatalog.IsReserved(Key("Enter")));
            Assert.False(ShortcutCatalog.IsReserved(Key("Enter", ctrl: true)));
            Assert.False(ShortcutCatalog.IsReserved(Key("Delete")));
            Assert.False(ShortcutCatalog.IsReserved(Key("Delete", ctrl: true)));
            Assert.False(ShortcutCatalog.IsReserved(Key("Y")), "Y 随确认跳过改到回车而退役，从此可以绑");
            Assert.False(ShortcutCatalog.IsReserved(Key("F")));
            Assert.False(ShortcutCatalog.IsReserved(Key("Space")));
        });

        Test("Clean：认不出的 Id、解析不了的、落在保留键上的都清掉，空串留着", () =>
        {
            var dirty = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["不存在的动作"] = "F",           // 认不出的 Id
                ["toggle-pause"] = "Ctrl",        // 只有修饰键，解析不了
                ["seek-forward"] = "Right",       // 好的，留
                ["toggle-mute"] = "",             // 显式解绑，留
                ["volume-up"] = "Escape"          // Esc 已可绑定
            };

            var clean = ShortcutCatalog.Clean(dirty);

            Assert.False(clean.ContainsKey("不存在的动作"));
            Assert.False(clean.ContainsKey("toggle-pause"));
            Assert.Equal("Escape", clean["volume-up"]);
            Assert.Equal("Right", clean["seek-forward"]);
            Assert.Equal("", clean["toggle-mute"]);
        });

        Test("Clean：null 一份不炸，给回空字典", () =>
        {
            Assert.Equal(0, ShortcutCatalog.Clean(null).Count);
        });

        Test("快捷键扩展：25 个新动作都有默认键并可改绑，旧用户自定义键优先", () =>
        {
            Assert.Equal(Key("Left", shift: true), ShortcutCatalog.Actions.Single(action => action.Id == "seek-backward-fine").Default);
            Assert.Equal(Key("Left", alt: true), ShortcutCatalog.Actions.Single(action => action.Id == "video-pan-left").Default);
            Assert.Equal(Key("Z", ctrl: true), ShortcutCatalog.Actions.Single(action => action.Id == "subtitle-delay-reset").Default);
            foreach (var action in ShortcutCatalog.Actions.Skip(38))
            {
                var bindings = new Dictionary<string, string> { ["toggle-mute"] = ShortcutCatalog.Serialize(action.Default) };
                var effective = ShortcutCatalog.Resolve(bindings);
                Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(bindings, action.Default));
                Assert.True(effective[action.Id].IsEmpty, action.Id + " 抢走了旧用户的绑定");
                Assert.True(ShortcutCatalog.Clear(bindings, action.Id)[action.Id].Length == 0);
            }
        });

        Test("快捷键搜索：空白显示全部，中文名称与多个关键词都能匹配", () =>
        {
            Assert.True(ShortcutCatalog.MatchesSearch(null, "字幕延迟归零", Key("Z", ctrl: true)));
            Assert.True(ShortcutCatalog.MatchesSearch("  \t", "字幕延迟归零", Key("Z", ctrl: true)));
            Assert.True(ShortcutCatalog.MatchesSearch(" 字幕  归零 ", "字幕延迟归零", Key("Z", ctrl: true)));
            Assert.False(ShortcutCatalog.MatchesSearch("字幕 音量", "字幕延迟归零", Key("Z", ctrl: true)));
            Assert.True(ShortcutCatalog.MatchesSearch("画面 左移", "画面左移", Key("Left", alt: true)));
            Assert.False(ShortcutCatalog.MatchesSearch("完全不匹配", "画面左移", Key("Left", alt: true)));
        });

        Test("快捷键搜索：当前按键、英文别名、箭头与加号空白写法等价", () =>
        {
            foreach (var query in new[] { "ctrl+up", "Ctrl + Up", "CTRL ↑", "ctrl＋↑", "上方向键" })
                Assert.True(ShortcutCatalog.MatchesSearch(query, "音量增大", Key("Up", ctrl: true)), query);
            Assert.True(ShortcutCatalog.MatchesSearch("Spacebar", "播放 / 暂停", Key("Space")));
            Assert.True(ShortcutCatalog.MatchesSearch("空格", "播放 / 暂停", Key("Space")));
            Assert.True(ShortcutCatalog.MatchesSearch("Backspace", "恢复常速", Key("Back")));
            Assert.True(ShortcutCatalog.MatchesSearch("回车", "确认跳过", Key("Enter")));
            Assert.True(ShortcutCatalog.MatchesSearch("Esc", "退出全屏 / 停止", Key("Escape")));
            Assert.True(ShortcutCatalog.MatchesSearch("Alt + [", "字幕缩小", Key("BracketLeft", alt: true)));
        });

        Test("快捷键搜索：不含按键的文字按同一套词匹配（快捷键页的步长行）", () =>
        {
            Assert.True(ShortcutCatalog.MatchesText(null, "快进跨度（秒） ← 键跳这么多秒"));
            Assert.True(ShortcutCatalog.MatchesText("  ", "快进跨度（秒）"));
            Assert.True(ShortcutCatalog.MatchesText("快进 跨度", "快进跨度（秒） ← 键跳这么多秒"));
            Assert.True(ShortcutCatalog.MatchesText("快退跨度", "快退跨度（秒） ← 键跳这么多秒"));
            Assert.True(ShortcutCatalog.MatchesText("秒", "大步快退跨度（秒）"));
            Assert.False(ShortcutCatalog.MatchesText("快进 字幕", "快进跨度（秒）"));
        });

        Test("快捷键搜索：改绑后不搜索旧默认键，解绑显示未设置", () =>
        {
            var rebound = ShortcutCatalog.Resolve(new Dictionary<string, string> { ["toggle-pause"] = "Ctrl+K", ["toggle-mute"] = "" });
            Assert.True(ShortcutCatalog.MatchesSearch("Ctrl+K", "播放 / 暂停", rebound["toggle-pause"]));
            Assert.False(ShortcutCatalog.MatchesSearch("空格", "播放 / 暂停", rebound["toggle-pause"]));
            Assert.True(ShortcutCatalog.MatchesSearch("未设置", "静音", rebound["toggle-mute"]));
            Assert.False(ShortcutCatalog.MatchesSearch("M", "静音", rebound["toggle-mute"]));
        });

        Test("快捷键命令：精细跳转、字幕跳转、画面与字幕调整以及循环共用计划", () =>
        {
            var expected = new Dictionary<string, string>
            {
                ["seek-backward-fine"] = "seek|-1|relative+exact",
                ["seek-forward-fine"] = "seek|1|relative+exact",
                ["subtitle-seek-previous"] = "sub-seek|-1",
                ["subtitle-seek-next"] = "sub-seek|1",
                ["video-zoom-out"] = "add|video-zoom|-0.1",
                ["video-zoom-in"] = "add|video-zoom|0.1",
                ["video-zoom-reset"] = "set|video-zoom|0",
                ["video-pan-left"] = "add|video-pan-x|-0.1",
                ["video-pan-right"] = "add|video-pan-x|0.1",
                ["video-pan-up"] = "add|video-pan-y|-0.1",
                ["video-pan-down"] = "add|video-pan-y|0.1",
                ["subtitle-scale-down"] = "add|sub-scale|-0.1",
                ["subtitle-scale-up"] = "add|sub-scale|0.1",
                ["subtitle-position-up"] = "add|sub-pos|-1",
                ["subtitle-position-down"] = "add|sub-pos|1",
                ["subtitle-render-style"] = "cycle|sub-ass-override",
                ["subtitle-timing-fix"] = "cycle|sub-fix-timing",
                ["subtitle-ass-margins"] = "cycle|sub-ass-force-margins",
                ["subtitle-srt-margins"] = "cycle|sub-use-margins",
                ["subtitle-pgs-margins"] = "cycle|stretch-image-subs-to-screen",
                ["subtitle-pgs-gray"] = "cycle|sub-gray",
                ["subtitle-reload"] = "sub-reload",
                ["toggle-ab-loop"] = "ab-loop",
                ["toggle-loop"] = "cycle-values|loop-file|inf|no"
            };
            foreach (var (action, command) in expected)
            {
                var plan = AdditionalShortcuts.Plan(action)!;
                Assert.Equal(command, string.Join('|', plan.Commands.Single()));
                Assert.True(plan.Label.Length > 0);
            }
            Assert.Null(AdditionalShortcuts.Plan("不存在的动作"));
            Assert.Null(AdditionalShortcuts.Plan("speed-double"));
            Assert.Equal("set|audio-delay|0", string.Join('|', AdditionalShortcuts.Plan("audio-delay-reset")!.Commands.Single()));
        });

        Test("快捷键：字幕兼容性一组有默认键，PGS 两项用内核的位图字幕选项", () =>
        {
            KeyStroke Default(string id) => ShortcutCatalog.Actions.Single(action => action.Id == id).Default;

            Assert.Equal(Key("U"), Default("subtitle-render-style"));
            Assert.Equal(Key("U", shift: true), Default("subtitle-timing-fix"));
            Assert.Equal(Key("A", alt: true), Default("subtitle-ass-margins"));
            Assert.Equal(Key("S", alt: true), Default("subtitle-srt-margins"));
            Assert.Equal(Key("P", alt: true), Default("subtitle-pgs-margins"));
            Assert.Equal(Key("G", alt: true), Default("subtitle-pgs-gray"));
            Assert.Equal(Key("R"), Default("subtitle-reload"));

            // PGS 是位图：mpv 没有图形字幕的黑边开关，只能把字幕画布拉伸到整屏（stretch-image-subs-to-screen）；
            // 灰度同理，只对图形字幕生效（sub-gray）。两条都实测过随包内核可当场切换。
            Assert.Equal("stretch-image-subs-to-screen",
                AdditionalShortcuts.Plan("subtitle-pgs-margins")!.Commands.Single()[1]);
            Assert.Equal("sub-gray", AdditionalShortcuts.Plan("subtitle-pgs-gray")!.Commands.Single()[1]);
        });

        Test("快捷键命令：复位按顺序清两轴或两个循环点，不混成分号命令", () =>
        {
            Assert.Equal("set|video-pan-x|0;set|video-pan-y|0", string.Join(';', AdditionalShortcuts.Plan("video-pan-reset")!.Commands.Select(command => string.Join('|', command))));
            Assert.Equal("set|ab-loop-a|no;set|ab-loop-b|no", string.Join(';', AdditionalShortcuts.Plan("clear-ab-loop")!.Commands.Select(command => string.Join('|', command))));
            Assert.Equal("截图已保存到 ${screenshot-directory}", AdditionalShortcuts.Plan("screenshot")!.Notice);
        });

        Test("快捷键扩展：所有默认键都能翻译给独占窗口，自定义键和解绑不被默认绑回", () =>
        {
            var empty = Empty();
            var native = Momoka.Mpv.VideoWindowShortcuts.Bindings(empty);
            foreach (var action in ShortcutCatalog.Actions)
            {
                var key = Momoka.Mpv.VideoWindowShortcuts.Key(action.Default);
                Assert.True(key is not null, action.Id + " 独占模式不可达");
                Assert.Equal(action.Id, native[key!]);
            }
            var changed = Momoka.Mpv.VideoWindowShortcuts.Bindings(new Dictionary<string, string>
            { ["video-pan-left"] = "Ctrl+F9", ["toggle-ab-loop"] = "" });
            Assert.Equal("video-pan-left", changed["Ctrl+F9"]);
            Assert.Equal("", changed["Alt+LEFT"]);
            Assert.Equal("", changed["l"]);
        });

        Test("Label：找得到给中文名，找不到把 Id 原样给回去", () =>
        {
            Assert.Equal("播放 / 暂停", ShortcutCatalog.Label("toggle-pause"));
            Assert.Equal("没这个", ShortcutCatalog.Label("没这个"));
        });

        Test("长按判据：半秒才转倍速，← 每步按倍率往回走，只认两颗短跳箭头", () =>
        {
            // 用户令 2026-11-24「长按箭头以这个倍速播放」，问答里选的是「单击跳秒、按住约半秒转倍速」。
            Assert.False(SeekHold.IsSeek("seek-forward-long"), "大步不该吃长按");
            Assert.False(SeekHold.IsSeek("seek-forward-fine"), "精细不该吃长按");
            Assert.True(SeekHold.IsSeek("seek-forward") && SeekHold.IsSeek("seek-backward"));

            const long pressed = 1000;
            Assert.False(SeekHold.IsHeld(pressed, pressed + 499));
            Assert.True(SeekHold.IsHeld(pressed, pressed + 500));

            // 2 倍 → 0.4 秒／步（200ms 一步），10 倍 → 2 秒／步：进度往回走的速率就是倍率本身。
            Assert.Equal(0.4, SeekHold.RewindStepSeconds(2));
            Assert.Equal(2.0, SeekHold.RewindStepSeconds(10));
            Assert.Equal(1.0, SeekHold.RewindStepSeconds(10, 100));
            Assert.Equal(200, SeekHold.RewindStepMilliseconds);
        });

        Test("快捷键 token：能从回到 mpv 的那颗键反查物理键码，长按拿得到键", () =>
        {
            // 独占窗口不会送「松开」，播放页只能拿绑定去问 GetAsyncKeyState；查不到就退回单击跳秒。
            Assert.Equal(0x25, ShortcutKeyCatalog.KeyCode("Left"));
            Assert.Equal(0x27, ShortcutKeyCatalog.KeyCode("Right"));
            Assert.Equal(0x25, ShortcutKeyCatalog.KeyCode("NumPadLeft"), "数字键盘左也落在同一个 VK");
            Assert.Equal(0x20, ShortcutKeyCatalog.KeyCode("Space"));
            Assert.Equal(0xFF, ShortcutKeyCatalog.KeyCode("VkFF"), "认不出的键以 VkXX 存下来，也要能反查");
            Assert.Equal(0, ShortcutKeyCatalog.KeyCode("没这个"));
            Assert.Equal(0, ShortcutKeyCatalog.KeyCode(""));
        });
    }
}
