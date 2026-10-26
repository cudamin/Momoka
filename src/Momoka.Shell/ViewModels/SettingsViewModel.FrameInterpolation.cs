using Momoka.Configuration;
using Momoka.Mpv;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    private const string FrameInterpolationTitle = "补帧";

    private SettingSection FrameInterpolationCard()
    {
        var video = Settings.Video;
        var mode = FrameInterpolationOptions.NormalizeMode(video.FrameInterpolationMode);
        var modes = new (string Label, FrameInterpolationMode Value)[]
        {
            ("关闭", FrameInterpolationMode.Off),
            ("平滑插值（轻量）", FrameInterpolationMode.Smooth),
            ("运动补偿（CPU）", FrameInterpolationMode.MotionCompensated)
        };
        var custom = MpvCustomConfig.IsAvailable(Settings.Mpv) && Settings.Mpv.CustomConfigEnabled;
        var note = "关闭只关闭这张卡的功能，保留基础输出中的插值与算法偏好。平滑插值混合相邻帧，"
            + "不做运动估计；运动补偿使用随包 FFmpeg 的 CPU minterpolate，不是 GPU／AI 补帧。";
        if (custom) note += " 自定义 mpv.conf 会在本页选项之后加载，可能覆盖补帧、同步或硬解；不保证本页选择最终生效。";

        var rows = new List<SettingRow>
        {
            // Choice has no after parameter. Use the existing Pick factory rather than changing it.
            Pick("模式", Annotate(note, "interpolation、tscale、video-sync、vf"), modes,
                () => FrameInterpolationOptions.NormalizeMode(video.FrameInterpolationMode),
                value => video.FrameInterpolationMode = value, EqualityComparer<FrameInterpolationMode>.Default,
                after: RefreshFrameInterpolationMode)
        };

        var smoothRenderer = Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv
            || video.Renderer is "" or "gpu" or "gpu-next";
        if (mode == FrameInterpolationMode.Smooth && smoothRenderer)
        {
            rows.Add(Mpv("平滑算法", MpvOutputOptions.InterpolationKernels, () => video.Tscale,
                value => video.Tscale = value, "tscale",
                "与基础输出共用算法偏好。使用显示同步／音频重采样，增加 GPU 渲染开销，可能产生模糊、重影或振铃；"
                + "基础输出的高帧率／高刷新率音频同步回退仍适用。", RefreshFrameInterpolationMode));
        }
        else if (mode == FrameInterpolationMode.MotionCompensated)
        {
            rows.Add(Number("目标帧率", FrameInterpolationOptions.MinimumFps, FrameInterpolationOptions.MaximumFps,
                () => FrameInterpolationOptions.ClampFps(video.FrameInterpolationFps),
                value => video.FrameInterpolationFps = FrameInterpolationOptions.ClampFps(value),
                "24–120 fps；默认 60。片源已达到目标时跳过，不为了补帧丢弃原有高帧率帧。",
                mpvOption: "vf / minterpolate / fps"));
            rows.Add(Pick("计算质量", "质量与速度取舍，不是实测性能评级。高档启用边界处的小块运动补偿；"
                    + "高分辨率与高目标帧率可能无法实时处理，卡顿时先降低目标或选速度优先。",
                new (string Label, FrameInterpolationQuality Value)[]
                {
                    ("速度优先", FrameInterpolationQuality.Fast),
                    ("均衡", FrameInterpolationQuality.Balanced),
                    ("细节优先（更重）", FrameInterpolationQuality.High)
                }, () => FrameInterpolationOptions.NormalizeQuality(video.FrameInterpolationQuality),
                value => video.FrameInterpolationQuality = value, EqualityComparer<FrameInterpolationQuality>.Default));
        }

        return new SettingSection(VideoCategory, FrameInterpolationTitle, NextPlayback
            + " 运动补偿会增加 CPU 负载、内存复制和延迟，快速运动、遮挡与切镜可能出现边缘扭曲等伪影。"
            + "运动补偿仅处理 8-bit SDR；已识别 HDR／高位深片源自动跳过，保留原色彩与补帧偏好，元数据未知时不能保证识别。"
            + "复制硬解或软件解码只改变本次有效输出，不改保存的硬解偏好。", rows);
    }

    /// <summary>Only update this card; the parent owns backend/card refresh wiring.</summary>
    private void RefreshFrameInterpolationRows()
    {
        var section = Sections.FirstOrDefault(section => section.Category == VideoCategory
            && section.Title == FrameInterpolationTitle);
        section?.UpdateRows(FrameInterpolationCard().Rows);
    }

    private void RefreshFrameInterpolationMode()
    {
        RefreshFrameInterpolationRows();
        // The parent's RefreshVideoBackend also refreshes our rows, but our row-only method never
        // calls it back. This keeps same-page effective basic-output state current without recursion.
        RefreshVideoBackend();
    }
}
