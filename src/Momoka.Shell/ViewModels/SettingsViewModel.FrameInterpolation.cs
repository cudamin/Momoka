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

        var svp = MpvSvp.IsEnabled(Settings.Mpv);
        var rows = new List<SettingRow>();
        if (!svp)
        {
            // SVP's own profile owns frame generation while enabled; hide these choices rather
            // than keeping a second inactive mode selector, and never clear their saved values.
            rows.Add(Pick("模式", Annotate(note, "interpolation、tscale、video-sync、vf"), modes,
                () => FrameInterpolationOptions.NormalizeMode(video.FrameInterpolationMode),
                value => video.FrameInterpolationMode = value, EqualityComparer<FrameInterpolationMode>.Default,
                after: RefreshFrameInterpolationMode));
        }

        var smoothRenderer = Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv
            || video.Renderer is "" or "gpu" or "gpu-next";
        if (!svp && mode == FrameInterpolationMode.Smooth && smoothRenderer)
        {
            rows.Add(Mpv("平滑算法", MpvOutputOptions.InterpolationKernels, () => video.Tscale,
                value => video.Tscale = value, "tscale",
                "与基础输出共用算法偏好。使用显示同步／音频重采样，增加 GPU 渲染开销，可能产生模糊、重影或振铃；"
                + "基础输出的高帧率／高刷新率音频同步回退仍适用。", RefreshFrameInterpolationMode));
        }
        else if (!svp && mode == FrameInterpolationMode.MotionCompensated)
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

        AddSvpRows(rows);
        return new SettingSection(VideoCategory, FrameInterpolationTitle, NextPlayback
            + " CPU 运动补偿增加 CPU 负载、内存复制和延迟，只处理 8-bit SDR；已识别 HDR／高位深片源会跳过。"
            + "SVP 是可选外部组件，补帧算法和目标帧率在 SVP 中设置。两者都可能产生运动伪影，不承诺实时性能或 HDR 保持。", rows);
    }

    private void AddSvpRows(List<SettingRow> rows)
    {
        if (!MpvSvp.IsAvailable(Settings.Mpv)) return;
        var mpv = Settings.Mpv;
        rows.Add(Toggle("接入 SVP", "默认关闭，需自行安装并运行 SVP Manager。算法、目标帧率和可选 RIFE 模型在 SVP 中设置；"
                + "关闭后恢复本页原有补帧偏好，下次播放生效。",
            () => mpv.SvpEnabled, value => mpv.SvpEnabled = value, "input-ipc-server", RefreshFrameInterpolationMode));
        if (!mpv.SvpEnabled) return;
        if (!MpvSvp.IsEnabled(mpv))
        {
            rows.Add(Fact("SVP 接入状态", "自定义 mpv.conf 可能安装其他滤镜／脚本，首版不与 SVP 同时接管。"
                + "关闭自定义配置后恢复 SVP 接入；两个开关与文件路径都会保留。", "当前使用自定义 mpv.conf，SVP 不接入",
                "重新检测", RefreshFrameInterpolationMode));
            return;
        }
        rows.Add(PathBox("VapourSynth 组件", "留空自动查找，或填写 VSScript.dll 的完整路径",
            () => mpv.SvpVapourSynthPath ?? "", value =>
            {
                mpv.SvpVapourSynthPath = value;
                RefreshVideoBackend();
            }, "只接受本机 x64 VSScript.dll。SVP 安装器中的 mpv／VapourSynth 组件须完整；"
                + "按 SVP 的说明设置 Python 环境后重启 Momoka。"));
        var status = MpvSvp.FindLibrary(mpv.SvpVapourSynthPath);
        rows.Add(Fact("SVP 接入状态", status.Problem ?? "仅确认本地 x64 文件存在，不代表 Python／滤镜已加载或 SVP 已连接。"
                + "请先运行 SVP Manager；播放开始后在 SVP 窗口确认接管状态。",
            status.Found ? "已找到组件，等待播放时检查" : "缺少可用组件，SVP 播放暂不可用",
            "重新检测", RefreshFrameInterpolationMode));
        rows.Add(Fact("SVP 设置", "算法、目标帧率、质量与可选 RIFE 模型在 SVP Manager 中管理。"
            + "本次使用复制硬解／软件解码、音频同步，并关闭 Momoka 的其他补帧；保存偏好不变。"
            + "首版启用 SVP 后换片会重建视频窗口；mpvpipe 被其他播放器占用时会提示并停止起播，不连接别人的播放器。",
            "由 SVP Manager 管理"));
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
