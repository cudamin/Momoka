using Momoka.Playback;

namespace Momoka.Mpv;

[Flags]
public enum PlayerMenuRequirement
{
    None = 0,
    Video = 1,
    Audio = 2,
    Subtitle = 4,
    SecondarySubtitle = 8,
    TextSubtitle = 16,
    AssSubtitle = 32,
    ImageSubtitle = 64,
    Hdr = 128,
    D3D11 = 256,
    H264 = 512,
    Chapters = 1024,
    ManagedCapture = 2048,
    CropDetection = 4096,
    SilenceDetection = 8192
}

/// <summary>What one row of the 画面菜单 is.</summary>
public enum PlayerMenuKind
{
    /// <summary>Runs its <see cref="PlayerMenuNode.Commands"/> and echoes its <see cref="PlayerMenuNode.Notice"/>.</summary>
    Command,

    /// <summary>Opens its <see cref="PlayerMenuNode.Children"/>.</summary>
    Group,

    /// <summary>A rule between two runs of items. No label, no command, nothing to click.</summary>
    Separator
}

/// <summary>
/// One row of the 画面菜单: what it is called, what it does, and what to say on screen once it has.
/// </summary>
/// <param name="Kind">Item, submenu or rule.</param>
/// <param name="Label">What the row reads, taken from the bundled <c>input.conf</c>'s own menu text.</param>
/// <param name="Commands">
/// The mpv commands to run, in order, each already split into the argument list
/// <c>PlaybackService.CommandAsync</c> takes. Usually one; a 「重置」 row is several, because mpv has no
/// command that sets six properties at once and <c>input.conf</c>'s <c>;</c> chaining is input syntax
/// rather than something <c>mpv_command</c> understands.
/// </param>
/// <param name="Notice">
/// What to hand to <c>show-text</c> afterwards. May contain <c>${property}</c>, which mpv expands itself
/// — that is the point of routing this through mpv rather than the client's own toast: the value shown is
/// read after the command ran, so 「宽高比:16:9」 cannot disagree with what is actually set.
/// </param>
/// <param name="Children">The submenu's rows, empty for anything else.</param>
public sealed record PlayerMenuNode(
    PlayerMenuKind Kind,
    string Label,
    IReadOnlyList<IReadOnlyList<string>> Commands,
    string Notice,
    IReadOnlyList<PlayerMenuNode> Children)
{
    /// <summary>
    /// How the row shows it is the current one — the property to read and how to read the answer — or null
    /// for the rows that cannot honestly say (rotates, 视频滤镜 链开关, 数值微调, 纯动作). Explicit rather than
    /// derived from <see cref="Commands"/>: see <see cref="PlayerMenuState"/>.
    /// </summary>
    public PlayerMenuState? State { get; init; }

    /// <summary>Runtime capabilities required for this row; hiding never changes preferences.</summary>
    public PlayerMenuRequirement Requirement { get; init; }

    /// <summary>Additional readable properties needed by multi-command or non-property actions.</summary>
    public IReadOnlyList<string> RequireProperties { get; init; } = [];

    /// <summary>Host-owned action for dynamic rows; mutually exclusive with native Commands.</summary>
    public string? HostAction { get; init; }

    /// <summary>
    /// The rebindable action（<see cref="ShortcutCatalog"/>）that does exactly what this row does, or null for
    /// the rows no key reaches. Display only — the row still runs <see cref="Commands"/>. Both menus draw the
    /// action's current key in their right-hand column, so rebinding it in 设置 moves this hint with it.
    /// </summary>
    public string? Shortcut { get; init; }

    /// <summary>
    /// Whether to tick this row, given the values read for <see cref="PlayerMenuCatalog.CheckProperties"/>.
    /// A row with no <see cref="State"/>, or one whose property read back null, is never ticked.
    /// </summary>
    public bool IsCheckedBy(IReadOnlyDictionary<string, string?> values) =>
        State is { } state && values.TryGetValue(state.Property, out var current) && state.IsChecked(current);
}

/// <summary>
/// Shared native playback menu for the integrated page and standalone window.
/// Commands originate in dyphire/mpv-config's 2026-08-12 input.conf; script-dependent rows are
/// supplied by the host only when their tools are configured. Typed argument lists, current-value
/// checks and capability requirements keep display, keyboard dispatch and file reset in agreement.
/// </summary>
public static class PlayerMenuCatalog
{
    /// <summary>The menu, top level first. Order is the configuration's.</summary>
    public static IReadOnlyList<PlayerMenuNode> Root { get; } = Describe(
    [
        Group("导航",
            // 2026-11-20 用户令「移到导航里」：这一行原来拼在「更多」尾部，现在进目录树，两条管线一起从
            // 这里取。它不是 mpv 属性行：勾由宿主读 PlaybackSettings 后填值，点中走 HostAction。
            new PlayerMenuNode(PlayerMenuKind.Command, "窗口不在前台时单击画面播放/暂停", [], "", [])
            {
                HostAction = ActivationClickAction,
                Shortcut = BackgroundClickShortcut,
                State = new PlayerMenuState(ActivationClickSettingProperty, PlayerMenuMatch.Bool)
            },
            Item("跳到下一个静音位置", "", "script-message-to", MpvAnalysis.ScriptName, "silence")
                with { Shortcut = "skip-to-silence", Requirement = PlayerMenuRequirement.SilenceDetection,
                    RequireProperties = [MpvAnalysis.ReadyProperty, MpvAnalysis.SilenceProperty] },
            Item("开/关 显示 OSD 时间轴", "OSD 时间轴:${osd-level}", "cycle-values", "osd-level", "3", "1")
                with { Shortcut = "osd-timeline" },
            Item("显示当前播放位置", "播放位置:${time-pos} / ${duration}", "show-progress")
                with { Shortcut = "osd-position" },
            Item("跳到片头", "已跳到片头", "seek", "0", "absolute+exact") with { Shortcut = "seek-start" },
            Item("回到上次跳转前的位置", "已返回跳转前的位置", "revert-seek") with { Shortcut = "revert-seek" },
            Rule,
            Item("上一章节", "章节:${chapter}", "add", "chapter", "-1") with { Shortcut = "chapter-previous" },
            Item("下一章节", "章节:${chapter}", "add", "chapter", "1") with { Shortcut = "chapter-next" },
            Rule,
            Item("精准后退 1 秒", "后退 1 秒", "seek", "-1", "relative+exact") with { Shortcut = "seek-backward-fine" },
            Item("精准前进 1 秒", "前进 1 秒", "seek", "1", "relative+exact") with { Shortcut = "seek-forward-fine" },
            Item("上一帧", "当前帧:${estimated-frame-number}", "frame-back-step") with { Shortcut = "frame-backward" },
            Item("下一帧", "当前帧:${estimated-frame-number}", "frame-step") with { Shortcut = "frame-forward" }),

        Group("画面",
            Group("切换 宽高比",
                Aspect("默认值", "宽高比:${video-aspect-override}", "no", "set", "video-aspect-override", "no"),
                Aspect("16:9", "宽高比:${video-aspect-override}", "16:9", "set", "video-aspect-override", "16:9"),
                Aspect("4:3", "宽高比:${video-aspect-override}", "4:3", "set", "video-aspect-override", "4:3"),
                Aspect("2.35:1", "宽高比:${video-aspect-override}", "2.35:1", "set", "video-aspect-override", "2.35:1"),
                Item("循环切换", "宽高比:${video-aspect-override}",
                    "cycle-values", "video-aspect-override", "16:9", "4:3", "2.35:1", "no")),
            Item("左旋转", "视频旋转:${video-rotate}", "cycle-values", "video-rotate", "0", "270", "180", "90"),
            Item("右旋转", "视频旋转:${video-rotate}", "cycle-values", "video-rotate", "0", "90", "180", "270"),
            Group("画面缩放",
                Item("画面缩小", "画面缩小:${video-zoom}", "add", "video-zoom", "-0.1") with { Shortcut = "video-zoom-out" },
                Item("画面放大", "画面放大:${video-zoom}", "add", "video-zoom", "0.1") with { Shortcut = "video-zoom-in" },
                Item("画面左移动", "画面左移动:${video-pan-x}", "add", "video-pan-x", "-0.1") with { Shortcut = "video-pan-left" },
                Item("画面右移动", "画面右移动:${video-pan-x}", "add", "video-pan-x", "0.1") with { Shortcut = "video-pan-right" },
                Item("画面上移动", "画面上移动:${video-pan-y}", "add", "video-pan-y", "-0.1") with { Shortcut = "video-pan-up" },
                Item("画面下移动", "画面下移动:${video-pan-y}", "add", "video-pan-y", "0.1") with { Shortcut = "video-pan-down" }),
            // README's 缩放窗口时按画面比例联动 keeps the window the shape of the picture, so there is
            // normally nothing to crop. This row is for the times that is not what someone wants: a 2.35:1
            // film on a 16:9 screen, filled rather than letterboxed.
            Filled("开/关 裁切填充", "裁切填充:${panscan}", "panscan", "cycle-values", "panscan", "0.0", "1.0"),
            Steps("重置以上画面操作", "重置画面操作",
                ["set", "video-zoom", "0"],
                ["set", "panscan", "0"],
                ["set", "video-rotate", "0"],
                ["set", "video-pan-x", "0"],
                ["set", "video-pan-y", "0"],
                ["set", "video-aspect-override", "no"]),
            Rule,
            Flag("开/关 自动 ICC 校色", "ICC 自动校色:${icc-profile-auto}", "icc-profile-auto", "cycle", "icc-profile-auto"),
            Flag("开/关 非线性色彩转换", "非线性色彩转换:${sigmoid-upscaling}", "sigmoid-upscaling", "cycle", "sigmoid-upscaling")
                with { Shortcut = "video-nonlinear" },
            Group("调色",
                Item("对比度 -1", "对比度:${contrast}", "add", "contrast", "-1"),
                Item("对比度 +1", "对比度:${contrast}", "add", "contrast", "1"),
                Item("明度 -1", "明度:${brightness}", "add", "brightness", "-1"),
                Item("明度 +1", "明度:${brightness}", "add", "brightness", "1"),
                Item("伽马 -1", "伽马:${gamma}", "add", "gamma", "-1"),
                Item("伽马 +1", "伽马:${gamma}", "add", "gamma", "1"),
                Item("饱和度 -1", "饱和度:${saturation}", "add", "saturation", "-1"),
                Item("饱和度 +1", "饱和度:${saturation}", "add", "saturation", "1"),
                Item("色相 -1", "色相:${hue}", "add", "hue", "-1"),
                Item("色相 +1", "色相:${hue}", "add", "hue", "1"),
                Rule,
                Steps("重置", "重置调色",
                    ["set", "contrast", "0"],
                    ["set", "brightness", "0"],
                    ["set", "gamma", "0"],
                    ["set", "saturation", "0"],
                    ["set", "hue", "0"])),
            Group("去色带",
                Flag("deband 开关", "去色带:${deband}", "deband", "cycle", "deband"),
                Item("deband 强度 +1", "去色带强度:${deband-iterations}", "add", "deband-iterations", "1"),
                Item("deband 强度 -1", "去色带强度:${deband-iterations}", "add", "deband-iterations", "-1")),
            Group("HDR 相关",
                Item("切换 HDR 映射曲线", "HDR 映射曲线:${tone-mapping}",
                    "cycle-values", "tone-mapping",
                    "auto", "spline", "bt.2390", "hable", "bt.2446a", "st2094-40", "st2094-10"),
                Flag("切换 HDR 动态映射", "HDR 动态映射:${hdr-compute-peak}", "hdr-compute-peak",
                    "cycle-values", "hdr-compute-peak", "yes", "no"),
                Group("输出模式",
                    Steps("HDR 输出（按显示器能力映射）", "HDR 输出按显示器能力映射；峰值恢复自动，ICC 已关闭",
                        ["set", "icc-profile-auto", "no"],
                        ["set", "target-peak", "auto"],
                        ["set", "target-trc", "auto"],
                        ["set", "target-prim", "auto"],
                        ["set", "target-colorspace-hint-mode", "target"],
                        ["set", "target-colorspace-hint", "auto"]),
                    Steps("映射到 SDR", "已映射到 SDR；HDR 峰值限制与 ICC 已清除",
                        ["set", "icc-profile-auto", "no"],
                        ["set", "target-peak", "auto"],
                        ["set", "target-colorspace-hint", "no"],
                        ["set", "target-trc", "bt.1886"],
                        ["set", "target-prim", "bt.709"])),
                Item("切换 显示器传输特性", "显示器传输特性:${target-trc}",
                    "cycle-values", "target-trc", "auto", "pq", "gamma2.2"),
                Item("切换 HDR 参考白亮度", "HDR 参考白亮度:${hdr-reference-white}",
                    "cycle-values", "hdr-reference-white", "100", "203"),
                Item("切换 色域映射模式", "色域映射模式:${gamut-mapping-mode}", "cycle", "gamut-mapping-mode"),
                Flag("切换 色调映射可视化", "色调映射可视化:${tone-mapping-visualize}", "tone-mapping-visualize",
                    "cycle", "tone-mapping-visualize") with { Shortcut = "hdr-visualize" })),

        Group("视频",
            Item("开/关 去黑边", "", "script-message-to", MpvAnalysis.ScriptName, "crop")
                with { Shortcut = "video-detect-crop", Requirement = PlayerMenuRequirement.CropDetection,
                    RequireProperties = [MpvAnalysis.ReadyProperty, MpvAnalysis.CropProperty] },
            Item("切换 视频轨", "视频轨:${vid}", "cycle", "vid") with { Shortcut = "video-next" },
            Group("切换 解码方式",
                Pick("软解", "解码方式:${hwdec}", "hwdec", "no", "set", "hwdec", "no"),
                Pick("自动选择硬解加速模式", "解码方式:${hwdec}", "hwdec", "auto-safe", "set", "hwdec", "auto-safe"),
                Pick("自动选择 copy 硬解模式", "解码方式:${hwdec}", "hwdec", "auto-copy-safe", "set", "hwdec", "auto-copy-safe"),
                Rule,
                Pick("nvdec 硬解", "解码方式:${hwdec}", "hwdec", "nvdec", "set", "hwdec", "nvdec"),
                Pick("d3d11va 硬解", "解码方式:${hwdec}", "hwdec", "d3d11va", "set", "hwdec", "d3d11va"),
                Pick("nvdec-copy 硬解", "解码方式:${hwdec}", "hwdec", "nvdec-copy", "set", "hwdec", "nvdec-copy"),
                Pick("d3d11va-copy 硬解", "解码方式:${hwdec}", "hwdec", "d3d11va-copy", "set", "hwdec", "d3d11va-copy"),
                Pick("d3d12va-copy 硬解", "解码方式:${hwdec}", "hwdec", "d3d12va-copy", "set", "hwdec", "d3d12va-copy")),
            Group("插值 / 抖动补偿",
                Steps("开启（同时使用显示同步）", "插值已开启，使用显示同步",
                    ["set", "video-sync", "display-resample"], ["set", "interpolation", "yes"])
                    with { State = new("interpolation", PlayerMenuMatch.Bool, Radio: true) },
                Pick("关闭", "插值已关闭", "interpolation", "no", "set", "interpolation", "no")),
            Flag("开/关 flip 模式", "D3D11 flip 模式:${d3d11-flip}", "d3d11-flip", "cycle", "d3d11-flip")
                with { Shortcut = "video-flip", Requirement = PlayerMenuRequirement.D3D11 },
            Flag("开/关 反交错", "去交错:${deinterlace}", "deinterlace", "cycle", "deinterlace"),
            Group("切换 帧同步模式",
                Pick("audio", "帧同步模式:${video-sync}", "video-sync", "audio", "set", "video-sync", "audio"),
                Pick("display-resample", "帧同步模式:${video-sync}", "video-sync", "display-resample",
                    "set", "video-sync", "display-resample"),
                Pick("display-tempo", "帧同步模式:${video-sync}", "video-sync", "display-tempo", "set", "video-sync", "display-tempo"),
                Pick("display-vdrop", "帧同步模式:${video-sync}", "video-sync", "display-vdrop", "set", "video-sync", "display-vdrop"),
                Pick("display-resample-vdrop", "帧同步模式:${video-sync}", "video-sync", "display-resample-vdrop",
                    "set", "video-sync", "display-resample-vdrop"),
                Item("循环切换", "帧同步模式:${video-sync}", "cycle-values", "video-sync",
                    "display-resample", "display-tempo", "audio", "display-vdrop", "display-resample-vdrop")
                    with { Shortcut = "video-sync-cycle" })),

        Group("视频滤镜",
            Item("清空", "清空视频滤镜", "vf", "clr", ""),
            Rule,
            Item("开/关 去色块滤镜", "视频滤镜:${vf}", "vf", "toggle", "deblock=filter=weak:block=4"),
            Item("开/关 动态范围限制", "视频滤镜:${vf}", "vf", "toggle", "format=colorlevels=limited"),
            Item("开/关 垂直翻转", "视频滤镜:${vf}", "vf", "toggle", "vflip"),
            Item("开/关 水平翻转", "视频滤镜:${vf}", "vf", "toggle", "hflip"),
            Item("开/关 旋转 180", "视频滤镜:${vf}", "vf", "toggle", "rotate=angle=180*PI/180"),
            Item("开/关 伽马修正 2.2", "视频滤镜:${vf}", "vf", "toggle", "format:gamma=gamma2.2"),
            Item("开/关 强制帧数 59.94", "视频滤镜:${vf}", "vf", "toggle", "fps=fps=60/1.001"),
            Item("开/关 填充 16:9 的黑边并居中", "视频滤镜:${vf}", "vf", "toggle", "pad=aspect=16/9:x=-1:y=-1"),
            Item("开/关 色温修正 6500", "视频滤镜:${vf}", "vf", "toggle", "colortemperature=temperature=6500")),

        Group("截屏",
            // The three differ in what ends up in the file, and that is the only thing worth putting on the
            // labels: mpv's own words for them (subtitles / video / window) say nothing to somebody who has
            // not read the manual. 「带着色器」 matters here more than in most players — this client's whole
            // 画质档位 scheme is shaders, so 「what the chain did to this frame」 is a real question, and only
            // the first row answers it.
            Item("截图 — 当前尺寸（含字幕和着色器）", "截图已保存到 ${screenshot-directory}", "screenshot", "scaled+subtitles")
                with { Shortcut = "screenshot" },
            Item("截图 — 原始尺寸（不带字幕）", "截图已保存到 ${screenshot-directory}", "screenshot", "video")
                with { Shortcut = "screenshot-original" },
            Item("截图 — 整个播放窗口（含控件）", "截图已保存到 ${screenshot-directory}", "screenshot", "window")
                with { Shortcut = "screenshot-window" },
            Item("截图 — 原始尺寸（含字幕）", "截图已保存到 ${screenshot-directory}", "screenshot", "subtitles")
                with { Shortcut = "screenshot-subtitles" },
            Rule,
            Item("开/关 逐帧截图（原始尺寸，含字幕）", "已切换逐帧截图；再次执行停止，目录:${screenshot-directory}",
                "screenshot", "subtitles+each-frame") with { Shortcut = "screenshot-each-frame-subtitles" },
            Item("开/关 逐帧截图（原始尺寸，不含字幕）", "已切换逐帧截图；再次执行停止，目录:${screenshot-directory}",
                "screenshot", "video+each-frame") with { Shortcut = "screenshot-each-frame-video" },
            Item("开/关 逐帧截图（整个播放窗口）", "已切换逐帧截图；再次执行停止，目录:${screenshot-directory}",
                "screenshot", "window+each-frame") with { Shortcut = "screenshot-each-frame-window" }),

        Group("音频",
            Item("切换 音频轨", "音频轨:${aid}", "cycle", "aid") with { Shortcut = "audio-next" },
            Flag("切换 静音", "静音:${mute}", "mute", "cycle", "mute") with { Shortcut = "toggle-mute" },
            Item("延迟 -0.1", "音频延迟:${audio-delay}", "add", "audio-delay", "-0.1") with { Shortcut = "audio-delay-decrease" },
            Item("延迟 +0.1", "音频延迟:${audio-delay}", "add", "audio-delay", "0.1") with { Shortcut = "audio-delay-increase" },
            Item("延迟 重置", "音频延迟:${audio-delay}", "set", "audio-delay", "0") with { Shortcut = "audio-delay-reset" },
            Rule,
            Group("音频通道输出方式",
                Pick("7.1 声道输出", "音频通道输出方式:${audio-channels}", "audio-channels", "7.1",
                    "set", "audio-channels", "7.1"),
                Pick("5.1 声道输出", "音频通道输出方式:${audio-channels}", "audio-channels", "5.1",
                    "set", "audio-channels", "5.1"),
                Pick("双通道输出", "音频通道输出方式:${audio-channels}", "audio-channels", "stereo",
                    "set", "audio-channels", "stereo"),
                Pick("自动选择以上输出方式", "音频通道输出方式:${audio-channels}", "audio-channels", "7.1,5.1,stereo",
                    "set", "audio-channels", "7.1,5.1,stereo"),
                Item("循环切换", "音频通道输出方式:${audio-channels}",
                    "cycle-values", "audio-channels", "7.1,5.1,stereo", "7.1", "5.1", "stereo", "auto-safe", "auto")),
            // 音量均衡. The two filter strings are MpvOutputOptions', not this file's: 设置 → 音频输出 →
            // 音量均衡 sends the same two at launch, and a menu row that meant something slightly different
            // from the settings row of the same name is the 「界面在骗人」 shape all over again. This row is
            // still worth having — it is the only way to hear the three side by side within one film — but it
            // does not persist, because every playback is a fresh mpv under --no-config.
            Item("切换 音量均衡", "音量均衡:${af}",
                "cycle-values", "af",
                MpvOutputOptions.DynAudNorm,
                MpvOutputOptions.LoudNorm,
                ""),
            Item("清空 af 滤镜", "清空音频滤镜", "af", "clr", ""),
            Flag("开/关 下混归一化", "5.1 下混归一化:${audio-normalize-downmix}", "audio-normalize-downmix",
                "cycle", "audio-normalize-downmix"),
            Rule,
            Flag("切换 音频独占模式", "音频独占模式:${audio-exclusive}", "audio-exclusive", "cycle", "audio-exclusive"),
            Flag("开/关 精确跳转时丢帧", "精确跳转时丢帧:${hr-seek-framedrop}", "hr-seek-framedrop", "cycle", "hr-seek-framedrop")
                with { Shortcut = "audio-hr-seek-drop", Requirement = PlayerMenuRequirement.Video }),

        Group("字幕",
            Item("切换 字幕轨", "字幕轨:${sid}", "cycle", "sid") with { Shortcut = "subtitle-next" },
            Item("切换 次字幕", "次字幕轨:${secondary-sid}", "cycle", "secondary-sid") with { Shortcut = "secondary-subtitle-next" },
            Flag("切换 字幕可见性", "字幕可见性:${sub-visibility}", "sub-visibility", "cycle", "sub-visibility")
                with { Shortcut = "toggle-subtitle" },
            Flag("切换 次字幕可见性", "次字幕可见性:${secondary-sub-visibility}", "secondary-sub-visibility", "cycle", "secondary-sub-visibility")
                with { Shortcut = "secondary-subtitle-toggle", Requirement = PlayerMenuRequirement.SecondarySubtitle },
            Item("切换 默认字体", "字幕字体:${sub-font}", "cycle-values", "sub-font", "sans-serif", "Microsoft YaHei", "SimSun")
                with { Shortcut = "subtitle-font", Requirement = PlayerMenuRequirement.TextSubtitle },
            Rule,
            Item("次字幕上移", "次字幕位置:${secondary-sub-pos}", "add", "secondary-sub-pos", "-1")
                with { Shortcut = "secondary-subtitle-position-up", Requirement = PlayerMenuRequirement.SecondarySubtitle },
            Item("次字幕下移", "次字幕位置:${secondary-sub-pos}", "add", "secondary-sub-pos", "1")
                with { Shortcut = "secondary-subtitle-position-down", Requirement = PlayerMenuRequirement.SecondarySubtitle },
            Item("字幕延迟 -0.1", "字幕延迟:${sub-delay}", "add", "sub-delay", "-0.1") with { Shortcut = "subtitle-delay-decrease" },
            Item("字幕延迟 +0.1", "字幕延迟:${sub-delay}", "add", "sub-delay", "0.1") with { Shortcut = "subtitle-delay-increase" },
            Item("次字幕延迟 -0.1", "次字幕延迟:${secondary-sub-delay}", "add", "secondary-sub-delay", "-0.1")
                with { Shortcut = "secondary-subtitle-delay-decrease", Requirement = PlayerMenuRequirement.SecondarySubtitle },
            Item("次字幕延迟 +0.1", "次字幕延迟:${secondary-sub-delay}", "add", "secondary-sub-delay", "0.1")
                with { Shortcut = "secondary-subtitle-delay-increase", Requirement = PlayerMenuRequirement.SecondarySubtitle },
            Item("次字幕延迟归零", "次字幕延迟:${secondary-sub-delay}", "set", "secondary-sub-delay", "0")
                with { Shortcut = "secondary-subtitle-delay-reset", Requirement = PlayerMenuRequirement.SecondarySubtitle },
            Item("字幕上移", "字幕上移:${sub-pos}", "add", "sub-pos", "-1") with { Shortcut = "subtitle-position-up" },
            Item("字幕下移", "字幕下移:${sub-pos}", "add", "sub-pos", "1") with { Shortcut = "subtitle-position-down" },
            Item("字号 -0.1", "字幕缩小:${sub-scale}", "add", "sub-scale", "-0.1") with { Shortcut = "subtitle-scale-down" },
            Item("字号 +0.1", "字幕放大:${sub-scale}", "add", "sub-scale", "0.1") with { Shortcut = "subtitle-scale-up" },
            Rule,
            Item("跳转上一条字幕", "上一条字幕", "sub-seek", "-1") with { Shortcut = "subtitle-seek-previous" },
            Item("跳转下一条字幕", "下一条字幕", "sub-seek", "1") with { Shortcut = "subtitle-seek-next" },
            Rule,
            Group("兼容性",
                Item("切换 次字幕样式覆盖", "次字幕样式覆盖:${secondary-sub-ass-override}", "cycle", "secondary-sub-ass-override")
                    with { Shortcut = "subtitle-secondary-style", Requirement = PlayerMenuRequirement.SecondarySubtitle },
                Item("切换 字幕混合视频帧", "字幕混合视频帧:${blend-subtitles}", "cycle", "blend-subtitles")
                    with { Shortcut = "subtitle-blend", Requirement = PlayerMenuRequirement.Video },
                Item("切换 字幕颜色转换方式", "字幕颜色转换方式:${sub-ass-vsfilter-color-compat}", "cycle", "sub-ass-vsfilter-color-compat")
                    with { Shortcut = "subtitle-color-compat", Requirement = PlayerMenuRequirement.AssSubtitle },
                Item("切换 使用视频信息", "字幕使用视频信息:${sub-ass-use-video-data}", "cycle", "sub-ass-use-video-data")
                    with { Shortcut = "subtitle-video-data", Requirement = PlayerMenuRequirement.AssSubtitle },
                Flag("切换 bidi 双向检测兼容性", "bidi 双向检测兼容性:${sub-vsfilter-bidi-compat}", "sub-vsfilter-bidi-compat",
                    "cycle", "sub-vsfilter-bidi-compat") with { Shortcut = "subtitle-bidi", Requirement = PlayerMenuRequirement.TextSubtitle },
                Item("切换 ass 字幕阴影边框缩放", "ASS 阴影边框缩放:${sub-ass-style-overrides}", "cycle-values", "sub-ass-style-overrides",
                    "ScaledBorderAndShadow=no", "ScaledBorderAndShadow=yes")
                    with { Shortcut = "subtitle-border-scale", Requirement = PlayerMenuRequirement.AssSubtitle },
                Item("切换 渲染样式", "字幕渲染样式:${sub-ass-override}", "cycle", "sub-ass-override")
                    with { Shortcut = "subtitle-render-style" },
                Flag("切换 字幕时序修复", "字幕时序修复:${sub-fix-timing}", "sub-fix-timing", "cycle", "sub-fix-timing")
                    with { Shortcut = "subtitle-timing-fix" },
                Flag("切换 ass 字幕输出到黑边", "ass 字幕输出黑边:${sub-ass-force-margins}", "sub-ass-force-margins",
                    "cycle", "sub-ass-force-margins") with { Shortcut = "subtitle-ass-margins" },
                Flag("切换 srt 字幕输出到黑边", "srt 字幕输出黑边:${sub-use-margins}", "sub-use-margins",
                    "cycle", "sub-use-margins") with { Shortcut = "subtitle-srt-margins" },
                // PGS/VobSub 是位图，mpv 没有 sub-ass-force-margins / sub-use-margins 那样的图形字幕黑边开关；
                // 唯一的内置手段是 stretch-image-subs-to-screen（图形字幕画布铺到整屏、落进黑边，代价是被拉伸）。
                Flag("切换 pgs 字幕输出到黑边", "pgs 字幕输出黑边:${stretch-image-subs-to-screen}",
                    "stretch-image-subs-to-screen", "cycle", "stretch-image-subs-to-screen")
                    with { Shortcut = "subtitle-pgs-margins" },
                Flag("切换 pgs 字幕灰度转换", "pgs 字幕灰度:${sub-gray}", "sub-gray", "cycle", "sub-gray")
                    with { Shortcut = "subtitle-pgs-gray" },
                Item("重载当前字幕", "重载当前字幕", "sub-reload")
                    with { Shortcut = "subtitle-reload" }),
            Steps("恢复初始", "重置字幕状态",
                ["set", "sub-pos", "100"],
                ["set", "sub-scale", "1.0"],
                ["set", "sub-delay", "0"],
                ["set", "secondary-sub-pos", "0"],
                ["set", "secondary-sub-delay", "0"]) with { Shortcut = "subtitle-reset" }),

        Group("片段循环",
            Item("设定/清除 片段循环", "片段循环:${ab-loop-a} - ${ab-loop-b}", "ab-loop")
                with { Shortcut = "toggle-ab-loop" },
            Toggle("开/关 循环播放", "循环播放:${loop-file}", "loop-file", "inf", "cycle-values", "loop-file", "inf", "no")
                with { Shortcut = "toggle-loop" })
    ]);

    private static IReadOnlyList<PlayerMenuNode> Describe(IReadOnlyList<PlayerMenuNode> nodes,
        PlayerMenuRequirement inherited = PlayerMenuRequirement.None)
    {
        return nodes.Select(node =>
        {
            var requirement = inherited | node.Requirement;
            if (node.Kind == PlayerMenuKind.Group)
            {
                requirement |= node.Label switch
                {
                    "画面" or "视频" or "视频滤镜" or "截屏" => PlayerMenuRequirement.Video,
                    "音频" => PlayerMenuRequirement.Audio,
                    "字幕" => PlayerMenuRequirement.Subtitle,
                    "HDR 相关" => PlayerMenuRequirement.Hdr,
                    _ => PlayerMenuRequirement.None
                };
                return node with { Requirement = requirement, Children = Describe(node.Children, requirement) };
            }
            if (node.Kind != PlayerMenuKind.Command) return node;
            var shortcut = node.Shortcut ?? NativeMenuShortcuts.ActionFor(node.Commands);
            requirement |= shortcut switch
            {
                "chapter-previous" or "chapter-next" => PlayerMenuRequirement.Chapters,
                "screenshot-each-frame-subtitles" or "screenshot-each-frame-video" or "screenshot-each-frame-window"
                    => PlayerMenuRequirement.ManagedCapture,
                "frame-forward" or "frame-backward" => PlayerMenuRequirement.Video,
                "subtitle-ass-margins" => PlayerMenuRequirement.AssSubtitle,
                "subtitle-pgs-margins" or "subtitle-pgs-gray" => PlayerMenuRequirement.ImageSubtitle,
                "subtitle-position-up" or "subtitle-position-down" or "subtitle-scale-down" or "subtitle-scale-up"
                    or "subtitle-seek-previous" or "subtitle-seek-next" or "subtitle-render-style"
                    or "subtitle-timing-fix" or "subtitle-srt-margins" => PlayerMenuRequirement.TextSubtitle,
                _ => PlayerMenuRequirement.None
            };
            var properties = node.RequireProperties.Concat(node.Commands.SelectMany(command =>
                command.Count > 1 && command[0] is "set" or "cycle" or "cycle-values" or "add" or "multiply" or "toggle"
                    ? new[] { command[1] }
                    : command.Count > 0 && command[0] is "vf" or "af" ? new[] { command[0] } : []))
                .Where(property => property != "chapter").Distinct(StringComparer.Ordinal).ToArray();
            return node with { Shortcut = shortcut, Requirement = requirement, RequireProperties = properties };
        }).ToArray();
    }

    /// <summary>
    /// Every node in the tree, parents before children. For anything that has to look at all of them —
    /// the tests that check no submenu is empty and every row runs something.
    /// </summary>
    public static IEnumerable<PlayerMenuNode> Flatten(IEnumerable<PlayerMenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    /// <summary>
    /// The runnable rows, flattened in the tree's own DFS pre-order — groups and separators dropped.
    /// <para>
    /// This is the addressing the 独占模式 picture menu uses: the shell serialises <see cref="Root"/> to the
    /// uosc menu and gives each command row a 1-based index into <b>this</b> list; a click sends that index
    /// back (<see cref="Mpv.VideoWindowContract.MenuIndex"/>) and the host runs <c>Commands[index-1]</c>
    /// through the same <c>RunMenuNodeAsync</c> the integrated menu uses. One catalogue, one execution — so
    /// the two pipelines cannot describe different menus. The serialiser must number command rows in this
    /// same order (a test pins that the two walks agree).
    /// </para>
    /// </summary>
    public static IReadOnlyList<PlayerMenuNode> Commands { get; } =
        [.. Flatten(Root).Where(node => node.Kind == PlayerMenuKind.Command)];

    /// <summary>
    /// 「更多」里「播放信息…」那一行执行的快捷键动作。那一行不在目录树里，而集成侧与独占侧的「更多」都要
    /// 从同一个 Id 取当前键，所以它留在这里，和目录行的 <see cref="PlayerMenuNode.Shortcut"/> 是同一份词汇。
    /// </summary>
    public const string PlaybackInfoShortcut = "show-playback-info";

    /// <summary>
    /// 目录里「窗口不在前台时单击画面播放/暂停」那一行的宿主动作（2026-11-20 从「更多」移进「导航」）。
    /// 与 <see cref="BackgroundClickShortcut"/> 同一个 Id：菜单行、快捷键、uosc 的 MenuIndex 回包走的是同一件事。
    /// </summary>
    public const string ActivationClickAction = "toggle-background-click";

    /// <summary>
    /// 「窗口不在前台时单击画面播放/暂停」那一行的快捷键动作（2026-11-20 用户令「记得加快捷键」）。
    /// 与 <see cref="PlaybackInfoShortcut"/> 同一个道理：菜单行的右列都要从同一个 Id 取当前键。
    /// </summary>
    public const string BackgroundClickShortcut = ActivationClickAction;

    /// <summary>
    /// 上面那一行打勾用的键 —— 不是 mpv 属性，宿主读 <c>PlaybackSettings.ActivationClickTogglesPlayback</c>
    /// 后以 yes/no 填进读数表（<c>PlayerViewModel.ReadMenuChecksAsync</c>），两条管线据此显示当前档。
    /// </summary>
    public const string ActivationClickSettingProperty = "momoka/settings/activation-click";

    /// <summary>
    /// 目录行右列要显示的快捷键文字：动作 Id → 当前生效键的屏上写法。取自 <see cref="ShortcutCatalog.Resolve"/>，
    /// 所以设置页重绑后这里跟着变；显式解绑的动作不进表，那一行就不显示右列，而不是显示一个按下去没用的键。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ShortcutTexts(IReadOnlyDictionary<string, string> bindings) =>
        ShortcutCatalog.Resolve(bindings)
            .Where(pair => !pair.Value.IsEmpty)
            .ToDictionary(pair => pair.Key, pair => ShortcutCatalog.Format(pair.Value), StringComparer.Ordinal);

    /// <summary>
    /// The distinct property keys any row's <see cref="PlayerMenuNode.State"/> reads, deduplicated — 解码方式's
    /// eight rows share one 「hwdec」 read. The client reads exactly these when the 画面菜单 opens and ticks each
    /// row against the answers（<see cref="PlayerMenuNode.IsCheckedBy"/>）, so the read cost is one round trip
    /// per property however many rows use it. 唯一例外是 <see cref="ActivationClickSettingProperty"/>：它不是
    /// mpv 属性，宿主填设置值；读表时先认它，别发给 mpv。
    /// </summary>
    public static IReadOnlyList<string> CheckProperties { get; } =
        [.. Flatten(Root)
            .Where(node => node.State is not null)
            .Select(node => node.State!.Property)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// A rule between two runs of items. A property rather than a shared instance so that
    /// <see cref="Root"/> can use it: a static initializer reading another one declared below it gets
    /// null, and a null separator is a crash rather than a missing line.
    /// </summary>
    private static PlayerMenuNode Rule => new(PlayerMenuKind.Separator, string.Empty, [], string.Empty, []);

    private static PlayerMenuNode Item(string label, string notice, params string[] command) =>
        new(PlayerMenuKind.Command, label, [command], notice, []);

    /// <summary>A radio choice: ticked when <paramref name="property"/> reads back exactly <paramref name="value"/>.</summary>
    private static PlayerMenuNode Pick(string label, string notice, string property, string value, params string[] command) =>
        Item(label, notice, command) with { State = new PlayerMenuState(property, PlayerMenuMatch.Equals, value, Radio: true) };

    /// <summary>A 宽高比 radio choice: ticked when video-aspect-override matches the ratio（「no」＝关）.</summary>
    private static PlayerMenuNode Aspect(string label, string notice, string value, params string[] command) =>
        Item(label, notice, command) with
        {
            State = new PlayerMenuState("video-aspect-override", PlayerMenuMatch.Ratio, value, Radio: true)
        };

    /// <summary>An on/off row: ticked when <paramref name="property"/> reads truthy（mpv 的 flag 回 「yes」）.</summary>
    private static PlayerMenuNode Flag(string label, string notice, string property, params string[] command) =>
        Item(label, notice, command) with { State = new PlayerMenuState(property, PlayerMenuMatch.Bool) };

    /// <summary>An on/off row whose 「on」 is a positive number（裁切填充 panscan 0↔1）.</summary>
    private static PlayerMenuNode Filled(string label, string notice, string property, params string[] command) =>
        Item(label, notice, command) with { State = new PlayerMenuState(property, PlayerMenuMatch.Positive) };

    /// <summary>An on/off row whose 「on」 is one named value（loop-file 的 inf↔no）—— toggle, not radio.</summary>
    private static PlayerMenuNode Toggle(string label, string notice, string property, string on, params string[] command) =>
        Item(label, notice, command) with { State = new PlayerMenuState(property, PlayerMenuMatch.Equals, on) };

    private static PlayerMenuNode Steps(string label, string notice, params string[][] commands) =>
        new(PlayerMenuKind.Command, label, commands, notice, []);

    private static PlayerMenuNode Group(string label, params PlayerMenuNode[] children) =>
        new(PlayerMenuKind.Group, label, [], string.Empty, children);
}
