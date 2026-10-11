using Momoka.Configuration;
using Momoka.Mpv;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    private const string VideoCategory = "视频输出";
    private const string VideoBasicsTitle = "基础输出";
    private const string NextPlayback = "自动保存，下次播放生效。";

    private void RefreshVideoBackend()
    {
        var index = Sections.ToList().FindIndex(section => section.Title == VideoBasicsTitle);
        if (index >= 0) Sections[index] = VideoCard();
        RefreshCustomMpvConfig();
    }

    private SettingSection VideoCard()
    {
        var video = Settings.Video;
        var rows = new List<SettingRow>();
        var builtIn = Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv;
        // 只构造当前可用的行；重建不写回偏好，切回原模式时恢复原值。
        if (builtIn)
        {
            rows.Add(Pick("渲染管线", "集成模式在主窗口播放，固定使用 Direct3D 11。独占模式使用单独的视频窗口，"
                    + "可选择图形接口，播放时仍能浏览媒体库；关闭视频窗口即停止播放。",
                Pipelines, () => Settings.Mpv.Pipeline, value => Settings.Mpv.Pipeline = value,
                EqualityComparer<VideoPipelineKind>.Default, after: RefreshVideoBackend));
        }
        else
        {
            rows.Add(Mpv("视频渲染", MpvOutputOptions.Renderers, () => video.Renderer, value => video.Renderer = value,
                "vo", "通常使用 GPU-Next；遇到兼容问题可尝试 GPU。外部 mpv 使用本页设置，不读取 mpv.conf。"));
        }
        if (builtIn && Settings.Mpv.Pipeline == VideoPipelineKind.Integrated)
        {
            // 用户要求保留只读文字；展示有效值，不改独占模式的接口偏好。
            rows.Add(Fact("图形接口",
                Annotate("集成模式固定使用 D3D11；切换到独占模式后可选择其他接口。", "gpu-api"),
                "Direct3D 11（D3D11）"));
        }
        else
        {
            rows.Add(Mpv("图形接口", MpvOutputOptions.GpuApis, () => video.GpuApi, value => video.GpuApi = value,
                "gpu-api", "默认 Vulkan。若无法播放或画面异常，可尝试自动或 Direct3D 11；性能取决于显卡和驱动。"));
        }

        var sync = Mpv("视频同步", MpvOutputOptions.VideoSync, () => video.VideoSync, value => video.VideoSync = value,
            "video-sync", SyncNote(video), RefreshVideoBackend);
        rows.AddRange(
        [
            Mpv("硬件解码", MpvOutputOptions.HardwareDecoders, () => video.HardwareDecoding, value => video.HardwareDecoding = value,
                "hwdec", "推荐自动（复制回内存），兼顾硬解与滤镜兼容性。原生硬解的开销较低，但受显卡、图形接口和滤镜限制。"
                    + "无法硬解时会尝试软件解码；画面异常时可选关闭。"),
            Mpv("色彩范围", MpvOutputOptions.OutputLevels, () => video.OutputLevels, value => video.OutputLevels = value,
                "video-output-levels", "与显示设备的输入范围保持一致。电脑显示器通常选全范围；范围不匹配会造成灰黑或暗部细节丢失。"),
            Mpv("反交错", MpvOutputOptions.DeinterlaceModes, () => video.DeinterlaceMode, value => video.DeinterlaceMode = value,
                "deinterlace", "用于消除隔行视频运动边缘的梳齿。老电视片源可选自动；普通逐行视频保持关闭。"),
            sync
        ]);
        if (video.VideoSync != "audio")
        {
            rows.Add(Toggle("启用插值", "混合相邻帧，减轻视频帧率与屏幕刷新率不匹配造成的顿挫；不生成运动补偿帧。",
                () => video.Interpolation, value => video.Interpolation = value, "interpolation",
                RefreshVideoBackend));
            if (video.Interpolation)
                rows.Add(Mpv("插值算法", MpvOutputOptions.InterpolationKernels, () => video.Tscale, value => video.Tscale = value,
                    "tscale", "过采样开销较低；其他算法可能更平滑，也可能在运动边缘产生重影或振铃。"));
        }
        var (effectiveSync, _, _) = MpvOutputOptions.ResolveSync(video);
        if (effectiveSync.StartsWith("display-", StringComparison.Ordinal))
        {
            rows.Add(Toggle("高帧率或高刷新率时使用音频同步", "片源超过 47 fps，或屏幕刷新率超过设定阈值时，停用插值并改用音频同步，以降低渲染开销。",
                () => video.HighFrameRateAudioSync, value => video.HighFrameRateAudioSync = value,
                "video-sync、interpolation", RefreshVideoBackend));
            if (video.HighFrameRateAudioSync)
                rows.Add(Number("音频同步回退阈值（Hz）", VideoSettings.MinimumHighRefreshRateLimitHz, VideoSettings.MaximumHighRefreshRateLimitHz,
                    () => video.HighRefreshRateLimitHz, value => video.HighRefreshRateLimitHz = value,
                    "播放开始时，屏幕刷新率超过此值就改用音频同步。即使未启用插值，也会影响显示同步。",
                    () => sync.Restate(Annotate(SyncNote(video), "video-sync")!), "video-sync、interpolation"));
        }
        rows.AddRange(
        [
            Toggle("宽于 16:9 的片源裁切填充", "放大宽银幕画面以减少黑边，部分画面和贴边字幕可能被裁掉。播放时也可在菜单中临时调整。",
                () => video.FillWideSources, value => video.FillWideSources = value, "panscan"),
            Slider("网络缓冲（MB）", 0, 4096, 64, () => video.NetworkCacheMegabytes, value => video.NetworkCacheMegabytes = (int)value,
                "前向缓存上限，另保留约一半用于向后跳转。0 使用默认值；调高会增加内存占用，不能提高网速。", "demuxer-max-bytes"),
            Mpv("抖动", MpvOutputOptions.Dithers, () => video.Dither, value => video.Dither = value,
                "dither、dither-depth", "在输出时加入细微噪点，减少位深转换产生的色带。继承预设时，是否启用由预设决定。", RefreshVideoBackend)
        ]);
        // 随包内核的 fast 预设使用 dither=no，单改位深不能启用算法；外部版本的预设未知。
        if (video.Dither != "no" && !(builtIn && video.Dither.Length == 0 && video.QualityPreset == "fast"))
            rows.Add(Mpv("抖动目标位深", MpvOutputOptions.DitherDepths, () => video.DitherDepth, value => video.DitherDepth = value,
                "dither-depth", "通常选自动；手动值应与实际输出位深匹配，不会改变显示器的硬件位深。"));
        rows.Add(Mpv("去色带", MpvOutputOptions.DebandModes, () => video.Deband, value => video.Deband = value,
            "deband", "平滑渐变中的色阶断层，可能损失细节。着色器的老片源修复生效时，会替代这里的处理。", RefreshVideoBackend));
        if (video.Deband is "auto" or "anime" or "yes")
            rows.Add(Mpv("去色带强度", MpvOutputOptions.DebandStrengths, () => video.DebandStrength, value => video.DebandStrength = value,
                "deband-iterations、deband-threshold", "越高越容易抹掉纹理和颗粒。只控制内置去色带，不影响着色器的老片源修复。"));
        return new SettingSection(VideoCategory, VideoBasicsTitle, NextPlayback, rows);
    }

    private static string SyncNote(VideoSettings video)
    {
        var (sync, interpolation, reason) = MpvOutputOptions.ResolveSync(video);
        var label = sync.Length == 0 ? "音频同步" : MpvOutputOptions.Describe(MpvOutputOptions.VideoSync, sync);
        var note = $"下次播放：{label}；插值{(interpolation ? "开启" : "关闭")}。";
        if (reason is not null) note += reason + "。";
        if (video.HighFrameRateAudioSync && sync.StartsWith("display-", StringComparison.Ordinal))
            note += $"片源超过 47 fps 或屏幕超过 {video.HighRefreshRateLimitHz} Hz 时改用音频同步，并停用插值。";
        return note;
    }

    private SettingSection HdrCard()
    {
        var video = Settings.Video;
        return new SettingSection(VideoCategory, "HDR 与杜比视界", NextPlayback + " 亮度单位为 nits，不会直接调整显示器背光。",
        [
            Mpv("HDR 处理", MpvOutputOptions.HdrModes, () => video.HdrMode, value => video.HdrMode = value,
                "target-colorspace-hint", "HDR 输出要求系统和显示器支持 HDR；不支持时由内核映射。不是电视原生杜比视界直通。"),
            Optional("HDR 输出峰值亮度", () => video.HdrPeakNits, value => video.HdrPeakNits = value, 1000,
                "自动使用显示器上报或内核推定值。手动按屏幕实际峰值填写，例如 600 或 1000；映射到 SDR 时不套用此上限。", "target-peak"),
            Mpv("HDR 映射算法", HdrOptions.ToneMappings, () => video.ToneMapping, value => video.ToneMapping = value,
                "tone-mapping", "处理超过目标亮度的高光。自动或 Spline 适合先试；不会再强制截断高光。"),
            Mpv("动态峰值检测", HdrOptions.PeakDetection, () => video.HdrComputePeak, value => video.HdrComputePeak = value,
                "hdr-compute-peak", "按画面估算亮度而不只信任片源元数据；需要额外 GPU 算力。"),
            Optional("HDR 参考白亮度", () => video.HdrReferenceWhiteNits, value => video.HdrReferenceWhiteNits = value, 203,
                "控制 HDR 环境中的普通白色与 SDR 内容亮度，不是高光上限。自动跟随系统；SDR 输出时会影响映射目标。", "hdr-reference-white"),
            Optional("文字字幕与 OSD 亮度", () => video.HdrSubtitleNits, value => video.HdrSubtitleNits = value, 203,
                "自动跟随参考白；文字字幕和屏幕控件过亮时可单独降低。不改变视频亮度。", "sub-hdr-peak"),
            Optional("图形字幕亮度", () => video.HdrImageSubtitleNits, value => video.HdrImageSubtitleNits = value, 203,
                "PGS、VobSub 等图像字幕单独控制。自动保留内核默认（当前 1000）；过亮可试 203。", "image-subs-hdr-peak"),
            new SettingOptionalNumberRow("HDR 对比度恢复", Annotate("自动继承画质预设；手动范围 0–2，越高局部对比越强，也可能产生光晕。", "hdr-contrast-recovery")!,
                0, 2, 0.3, video.HdrContrastRecovery, value => video.HdrContrastRecovery = value, Save),
            Toggle("使用杜比视界元数据", "默认开启，使用片源的 RPU 信息进行色彩处理。关闭仅供有兼容基础层的片源排障；Profile 5 关闭后可能偏色，不等于 HDR10 回退。",
                () => video.DolbyVisionMetadata, value => video.DolbyVisionMetadata = value, "vf=format:dolbyvision"),
            Toggle("使用杜比视界增强层", "默认开启。只有内核和解码路径实际提供增强层时才有效；开关不代表所有 Profile 7 FEL 都已完整支持。",
                () => video.DolbyVisionEnhancementLayer, value => video.DolbyVisionEnhancementLayer = value, "vf=format:enhancement-layer"),
            Toggle("使用 HDR10+ 元数据", "默认保留动态元数据供播放器映射；不代表向电视原样发送 HDR10+。",
                () => video.Hdr10PlusMetadata, value => video.Hdr10PlusMetadata = value, "vf=format:hdr10plus"),
            Toggle("自动 ICC 校色", "按系统显示器配置校色。明确选择 HDR 输出或 HDR 转 SDR 时，本次 HDR 播放优先执行该模式，不让 ICC 覆盖输出目标。",
                () => video.IccProfileAuto, value => video.IccProfileAuto = value, "icc-profile-auto")
        ]);
    }

    private SettingOptionalNumberRow Optional(string label, Func<double?> read, Action<double?> write,
        double fallback, string note, string option) =>
        new(label, Annotate(note + " 亮度按整数 nits 保存，小数四舍五入。", option)!, HdrOptions.MinimumNits, HdrOptions.MaximumNits,
            fallback, read(), write, Save, HdrOptions.ClampNits);

    private SettingSection ShaderCard()
    {
        var shaders = Settings.Shaders;
        var video = Settings.Video;
        (string Label, GpuTier Value)[] tiers =
        [
            ("低档（核显或入门显卡）", GpuTier.Low),
            ("中档（入门独显）", GpuTier.Medium),
            ("高档（较强独显）", GpuTier.High)
        ];
        (string Label, string Value)[] chains =
        [
            ("自动（按全屏目标选择）", ""),
            .. _shaders!.Catalog.Select(group => (group.DisplayName, group.Id))
        ];
        return new SettingSection(VideoCategory, "画质与着色器", NextPlayback + " 预设与着色器开关相互独立。",
        [
            Mpv("画质预设", MpvOutputOptions.QualityPresets, () => video.QualityPreset, value => video.QualityPreset = value,
                "profile", "先应用预设，再应用视频设置，最后应用着色器链的前置参数。fast 在加载较重着色器时不保证低开销。", RefreshVideoBackend),
            Toggle("启用着色器", "默认关闭。开启后按显示器全屏目标、片源类型和显卡档位选链；拖动或全屏切换不反复换链，避免冷编译卡顿。",
                () => shaders.Enabled, value => shaders.Enabled = value, "glsl-shaders"),
            Choice("显卡档位", tiers, () => shaders.Gpu, value => shaders.Gpu = value,
                "决定链的复杂度，不是自动测出的性能评级。集成管线固定 D3D11，动画 CNN 链较重；"
                    + "独占模式可选择其他图形接口。卡顿时先降低档位或关闭着色器。"),
            Choice("手动指定档位", chains, () => shaders.ManualGroup, value => shaders.ManualGroup = value,
                "这里保存长期偏好；播放中的临时对比请用播放器的着色器菜单，不会改写这里。"),
            Toggle("自动识别动画", "按 Emby 类型与标签匹配当前关键词：" + string.Join("、", shaders.AnimeKeywords)
                + "。命中后使用动画链，手动指定的档位优先。",
                () => shaders.AutoAnimeProfile, value => shaders.AutoAnimeProfile = value),
            Toggle("老片源修复", "高度不超过 576 的片源增加 hdeband；中高档再增加降噪。会代替上方内置去色带，也可能损失颗粒与细节。",
                () => shaders.RestoreVintageSources, value => shaders.RestoreVintageSources = value),
            Toggle("8K 片源关闭着色器", "宽 ≥7000 或高 ≥3000 时不自动加载任何链，避免 GPU 过载。",
                () => shaders.DisableForUltraHighRes, value => shaders.DisableForUltraHighRes = value)
        ]);
    }
}
