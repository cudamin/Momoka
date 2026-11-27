namespace Momoka.Playback;

/// <summary>New bindings preserve every existing default; menu commands remain in PlayerMenuCatalog.</summary>
internal static class NativeMenuShortcuts
{
    private static KeyStroke K(string key, bool ctrl = false, bool alt = false, bool shift = false) => new(key, ctrl, alt, shift);

    internal static IReadOnlyList<ShortcutAction> Actions { get; } =
    [
        new("video-detect-crop", "去黑边检测开关", K("C", shift: true)),
        new("skip-to-silence", "跳到下一个静音位置", K("F3")),
        new("osd-timeline", "OSD 时间轴开关", K("O")),
        new("osd-position", "显示当前播放位置", K("O", shift: true)),
        new("video-aspect-cycle", "循环切换宽高比", K("A", alt: true, shift: true)),
        new("video-rotate-left", "画面左旋转", K("Left", ctrl: true, shift: true)),
        new("video-rotate-right", "画面右旋转", K("Right", ctrl: true, shift: true)),
        new("video-crop", "裁切填充开关", K("P", alt: true, shift: true)),
        new("video-reset", "重置画面操作", K("Back", alt: true)),
        new("video-icc", "自动 ICC 校色开关", K("I", ctrl: true, shift: true)),
        new("video-nonlinear", "非线性色彩转换开关", K("I", alt: true, shift: true)),
        new("contrast-decrease", "对比度 −1", K("1")),
        new("contrast-increase", "对比度 +1", K("2")),
        new("brightness-decrease", "明度 −1", K("3")),
        new("brightness-increase", "明度 +1", K("4")),
        new("gamma-decrease", "伽马 −1", K("5")),
        new("gamma-increase", "伽马 +1", K("6")),
        new("saturation-decrease", "饱和度 −1", K("7")),
        new("saturation-increase", "饱和度 +1", K("8")),
        new("hue-decrease", "色相 −1", K("Minus")),
        new("hue-increase", "色相 +1", K("Equal")),
        new("color-reset", "重置调色", K("Back", ctrl: true)),
        new("deband-toggle", "去色带开关", K("D", alt: true, shift: true)),
        new("deband-increase", "去色带强度 +1", K("Z", alt: true)),
        new("deband-decrease", "去色带强度 −1", K("X", alt: true)),
        new("hdr-tone-map", "切换 HDR 映射曲线", K("H")),
        new("hdr-peak", "HDR 动态映射开关", K("H", alt: true)),
        new("hdr-output", "按显示器能力输出 HDR", K("H", ctrl: true)),
        new("hdr-sdr", "映射到 SDR", K("H", ctrl: true, shift: true)),
        new("hdr-trc", "切换显示器传输特性", K("T", ctrl: true)),
        new("hdr-white", "切换 HDR 参考白亮度", K("T", ctrl: true, shift: true)),
        new("hdr-gamut", "切换色域映射模式", K("G", ctrl: true)),
        new("hdr-visualize", "色调映射可视化开关", K("H", ctrl: true, alt: true)),
        new("video-next", "切换视频轨", K("V", ctrl: true, shift: true)),
        new("video-interpolation", "开启插值并使用显示同步", K("I", alt: true)),
        new("video-interpolation-off", "关闭插值", K("I", ctrl: true, alt: true)),
        new("video-deinterlace", "反交错开关", K("D", alt: true)),
        new("video-flip", "D3D11 flip 模式开关", K("F", ctrl: true, alt: true)),
        new("video-sync-cycle", "循环切换帧同步模式", K("V", ctrl: true, alt: true)),
        new("vf-clear", "清空视频滤镜", K("Backquote", ctrl: true)),
        new("vf-deblock", "去色块滤镜开关", K("V", alt: true)),
        new("vf-range-limit", "视频动态范围限制开关", K("1", ctrl: true, alt: true)),
        new("vf-flip-vertical", "视频垂直翻转开关", K("2", ctrl: true, alt: true)),
        new("vf-flip-horizontal", "视频水平翻转开关", K("3", ctrl: true, alt: true)),
        new("vf-rotate-180", "视频旋转 180 度滤镜开关", K("4", ctrl: true, alt: true)),
        new("vf-gamma", "伽马修正 2.2 滤镜开关", K("5", ctrl: true, alt: true)),
        new("vf-fps", "强制 59.94 帧滤镜开关", K("6", ctrl: true, alt: true)),
        new("vf-pad", "填充 16:9 黑边滤镜开关", K("8", ctrl: true, alt: true)),
        new("vf-temperature", "色温修正 6500 滤镜开关", K("7", ctrl: true, alt: true)),
        new("screenshot-window", "截图（整个播放窗口）", K("S", ctrl: true)),
        new("screenshot-subtitles", "截图（原始尺寸，含字幕）", K("S", ctrl: true, shift: true)),
        new("screenshot-each-frame-subtitles", "逐帧截图开关（含字幕）", K("S", alt: true, shift: true)),
        new("screenshot-each-frame-video", "逐帧截图开关（不含字幕）", K("S", ctrl: true, alt: true)),
        new("screenshot-each-frame-window", "逐帧截图开关（播放窗口）", K("S", ctrl: true, alt: true, shift: true)),
        new("audio-channels-cycle", "循环切换声道输出", K("Y", alt: true)),
        new("audio-normalization", "切换音量均衡", K("Y", shift: true)),
        new("audio-clear-filters", "清空音频滤镜", K("Backquote", alt: true)),
        new("audio-downmix", "下混归一化开关", K("Y", alt: true, shift: true)),
        new("audio-exclusive", "音频独占模式开关", K("Y", ctrl: true)),
        new("audio-hr-seek-drop", "精确跳转时丢帧开关", K("Y", ctrl: true, shift: true)),
        new("secondary-subtitle-next", "切换次字幕轨", K("K")),
        new("secondary-subtitle-toggle", "次字幕显示开关", K("V", alt: true, shift: true)),
        new("secondary-subtitle-position-up", "次字幕上移", K("Up", ctrl: true, shift: true)),
        new("secondary-subtitle-position-down", "次字幕下移", K("Down", ctrl: true, shift: true)),
        new("secondary-subtitle-delay-decrease", "次字幕延迟 −0.1 秒", K("Z", alt: true, shift: true)),
        new("secondary-subtitle-delay-increase", "次字幕延迟 +0.1 秒", K("X", alt: true, shift: true)),
        new("secondary-subtitle-delay-reset", "次字幕延迟归零", K("Z", ctrl: true, shift: true)),
        new("subtitle-secondary-style", "次字幕样式覆盖切换", K("R", alt: true, shift: true)),
        new("subtitle-blend", "字幕混合视频帧切换", K("T", alt: true, shift: true)),
        new("subtitle-color-compat", "字幕颜色转换方式切换", K("J", ctrl: true, shift: true)),
        new("subtitle-video-data", "字幕使用视频信息切换", K("V", ctrl: true)),
        new("subtitle-bidi", "Bidi 双向检测兼容开关", K("B", alt: true)),
        new("subtitle-border-scale", "ASS 阴影边框缩放切换", K("X", ctrl: true, alt: true, shift: true)),
        new("subtitle-font", "切换默认字幕字体", K("F", shift: true)),
        new("subtitle-reset", "恢复主次字幕位置、缩放与延迟", K("Back", ctrl: true, shift: true))
    ];

    internal static string? ActionFor(IReadOnlyList<IReadOnlyList<string>> commands)
    {
        if (commands.Count == 0) return null;
        var command = commands[0];
        if (command.Count < 2) return null;
        var verb = command[0];
        var property = command[1];
        if (verb == "vf" && command.Count >= 3) return command[2] switch
        {
            "" => "vf-clear",
            "deblock=filter=weak:block=4" => "vf-deblock",
            "format=colorlevels=limited" => "vf-range-limit",
            "vflip" => "vf-flip-vertical",
            "hflip" => "vf-flip-horizontal",
            "rotate=angle=180*PI/180" => "vf-rotate-180",
            "format:gamma=gamma2.2" => "vf-gamma",
            "fps=fps=60/1.001" => "vf-fps",
            "pad=aspect=16/9:x=-1:y=-1" => "vf-pad",
            "colortemperature=temperature=6500" => "vf-temperature",
            _ => null
        };
        if (verb == "af") return "audio-clear-filters";
        return property switch
        {
            "video-aspect-override" when verb == "cycle-values" => "video-aspect-cycle",
            "video-rotate" when verb == "cycle-values" && command.Count >= 4 => command[3] == "270" ? "video-rotate-left" : "video-rotate-right",
            "video-zoom" when commands.Count > 1 => "video-reset",
            "panscan" => "video-crop",
            "icc-profile-auto" when commands.Count == 1 => "video-icc",
            "icc-profile-auto" when commands.Any(row => row.Count >= 3 && row[1] == "target-trc" && row[2] == "bt.1886") => "hdr-sdr",
            "icc-profile-auto" when commands.Count > 1 => "hdr-output",
            "contrast" when commands.Count > 1 => "color-reset",
            "contrast" or "brightness" or "gamma" or "saturation" or "hue" when verb == "add" =>
                property + (command[2].StartsWith('-') ? "-decrease" : "-increase"),
            "deband" => "deband-toggle",
            "deband-iterations" => command[2].StartsWith('-') ? "deband-decrease" : "deband-increase",
            "tone-mapping" => "hdr-tone-map",
            "hdr-compute-peak" => "hdr-peak",
            "target-trc" => "hdr-trc",
            "hdr-reference-white" => "hdr-white",
            "gamut-mapping-mode" => "hdr-gamut",
            "video-sync" when commands.Count > 1 => "video-interpolation",
            "interpolation" => "video-interpolation-off",
            "deinterlace" => "video-deinterlace",
            "audio-channels" when verb == "cycle-values" => "audio-channels-cycle",
            "af" => "audio-normalization",
            "audio-normalize-downmix" => "audio-downmix",
            "audio-exclusive" => "audio-exclusive",
            _ => null
        };
    }
}
