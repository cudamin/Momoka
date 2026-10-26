using System.Globalization;
using Momoka.Configuration;

namespace Momoka.Mpv;

/// <summary>Temporal frame blending and CPU motion compensation are different operations.</summary>
public enum FrameInterpolationMode
{
    Off = 0,
    Smooth = 1,
    MotionCompensated = 2
}

public enum FrameInterpolationQuality
{
    Fast = 0,
    Balanced = 1,
    High = 2
}

/// <summary>A per-playback decision. Resolving it never changes the saved video preferences.</summary>
public readonly record struct FrameInterpolationDecision(
    FrameInterpolationMode Mode,
    int Fps,
    FrameInterpolationQuality Quality,
    string HardwareDecoding,
    string? Filter,
    string? StandDown);

/// <summary>
/// mpv's interpolation/tscale mixes adjacent frames; FFmpeg minterpolate with mi_mode=mci
/// estimates motion on the CPU. No external scripts, models or AI runtime are required.
/// </summary>
public static class FrameInterpolationOptions
{
    public const int MinimumFps = 24;
    public const int MaximumFps = 120;
    public const int DefaultFps = 60;
    public const string FilterLabel = "@momoka-frame-interpolation:";

    public static int ClampFps(int fps) => Math.Clamp(fps, MinimumFps, MaximumFps);

    public static FrameInterpolationMode NormalizeMode(FrameInterpolationMode mode) => mode switch
    {
        FrameInterpolationMode.Smooth or FrameInterpolationMode.MotionCompensated => mode,
        _ => FrameInterpolationMode.Off
    };

    public static FrameInterpolationQuality NormalizeQuality(FrameInterpolationQuality quality) => quality switch
    {
        FrameInterpolationQuality.Fast or FrameInterpolationQuality.High => quality,
        _ => FrameInterpolationQuality.Balanced
    };

    public static FrameInterpolationDecision Resolve(VideoSettings video, SourceProfile? source = null)
    {
        var mode = NormalizeMode(video.FrameInterpolationMode);
        var fps = ClampFps(video.FrameInterpolationFps);
        var quality = NormalizeQuality(video.FrameInterpolationQuality);
        var hardware = video.HardwareDecoding ?? "";

        if (mode != FrameInterpolationMode.MotionCompensated)
            return new(mode, fps, quality, hardware, null, null);

        // The bundled FFmpeg minterpolate accepts 8-bit planar formats only. Do not silently
        // quantize an identified HDR/10-bit source, discard dynamic metadata, or promise HDR passthrough.
        // Unknown metadata is not proof of SDR; the card explains this remaining coverage limit.
        if (source is { IsHdr: true } or { BitDepth: > 8 })
            return new(FrameInterpolationMode.Off, fps, quality, hardware, null,
                "CPU 运动补偿仅用于 8-bit SDR；本片为 HDR 或高位深，已跳过补帧并保留偏好");

        // FFmpeg drops frames when its requested fps is below the source rate. This is an
        // interpolation feature, not a hidden high-frame-rate downconverter.
        if (source is { FrameRate: > 0 } known && double.IsFinite(known.FrameRate) && known.FrameRate >= fps)
            return new(FrameInterpolationMode.Off, fps, quality, hardware, null,
                $"片源 {known.FrameRate.ToString("0.###", CultureInfo.InvariantCulture)}fps 已达到目标 {fps}fps，已跳过补帧并保留偏好");

        return new(mode, fps, quality, CopyCompatibleHardwareDecoder(hardware),
            MotionFilter(fps, quality), null);
    }

    /// <summary>
    /// minterpolate needs frames in system memory. Preserve explicit copy modes and software decoding;
    /// map native APIs to their copy variants, and use software for Vulkan/unknown modes with no safe copy path.
    /// This is an effective launch value, not a settings migration.
    /// </summary>
    public static string CopyCompatibleHardwareDecoder(string? hardware) => (hardware ?? "").Trim().ToLowerInvariant() switch
    {
        "auto" or "auto-safe" or "auto-unsafe" or "auto-copy" or "auto-copy-safe" => "auto-copy-safe",
        "d3d11va" or "d3d11va-copy" => "d3d11va-copy",
        "d3d12va-copy" => "d3d12va-copy",
        "dxva2" or "dxva2-copy" => "dxva2-copy",
        "nvdec" or "nvdec-copy" => "nvdec-copy",
        _ => "no"
    };

    public static string MotionFilter(int fps, FrameInterpolationQuality quality)
    {
        var (compensation, estimation, variableBlocks) = NormalizeQuality(quality) switch
        {
            FrameInterpolationQuality.Fast => ("obmc", "bidir", "0"),
            FrameInterpolationQuality.High => ("aobmc", "bilat", "1"),
            _ => ("aobmc", "bilat", "0")
        };
        return FilterLabel + "lavfi=[minterpolate=fps=" + ClampFps(fps).ToString(CultureInfo.InvariantCulture)
            + $":mi_mode=mci:mc_mode={compensation}:me_mode={estimation}:vsbmc={variableBlocks}]";
    }

    /// <summary>
    /// Append to the serialized mpv filter list, keeping existing labels and graph quoting intact.
    /// Emit the complete vf value: mpv_set_option_string does not accept command-line vf-append,
    /// and InlineSwitch already restores vf as one complete per-film list (including the empty default).
    /// Shader prerequisites/glsl-shaders are separate options and must not be rewritten here.
    /// </summary>
    public static void AppendVideoFilter(List<KeyValuePair<string, string>> options, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) throw new ArgumentException("视频滤镜不能为空", nameof(filter));
        var index = options.FindLastIndex(option => option.Key == "vf");
        if (index < 0)
        {
            options.Add(new("vf", filter));
            return;
        }

        var existing = options[index].Value;
        options[index] = new("vf", string.IsNullOrEmpty(existing) ? filter : existing + "," + filter);
    }
}
