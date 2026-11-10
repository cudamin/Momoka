using System.Globalization;
using Momoka.Configuration;

namespace Momoka.Playback;

public readonly record struct PlayerImageCrop(int X, int Y, int Width, int Height);

/// <summary>裁切与字幕拼图只处理 mpv 已保存的图片，不再次打开影片或传递认证。</summary>
public static class PlayerImageTools
{
    public const int MaxStitchImages = 100;

    public static PlayerToolPlan Crop(PlayerToolsSettings settings, string inputImage, string outputImage, PlayerImageCrop crop)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 ||
            crop.X > 100_000 || crop.Y > 100_000 || crop.Width > 100_000 || crop.Height > 100_000)
            throw new PlayerToolException("请先选择有效的图片裁切区域。");
        var input = Input(inputImage);
        var output = Output(outputImage);
        var executable = Executable(settings);
        var filter = string.Create(CultureInfo.InvariantCulture, $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}");
        string[] args = ["-hide_banner", "-nostdin", "-loglevel", "error", "-n", "-protocol_whitelist", "file,pipe",
            "-i", input, "-vf", filter, "-frames:v", "1", output];
        return PlayerToolPlan.Image(executable, Array.AsReadOnly(args), output, Array.AsReadOnly(new[] { input }));
    }

    public static PlayerToolPlan Stitch(PlayerToolsSettings settings, IReadOnlyList<string> inputImages, string outputImage)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(inputImages);
        if (inputImages.Count is < 2 or > MaxStitchImages) throw new PlayerToolException("字幕拼图需要 2 至 100 张图片。");
        var inputs = inputImages.Select(Input).ToArray();
        var output = Output(outputImage);
        var executable = Executable(settings);
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-n" };
        foreach (var input in inputs) args.AddRange(["-protocol_whitelist", "file,pipe", "-i", input]);
        // 同一个字幕区域的截图保持原宽度和像素，不为拼接默认缩小图片。
        var filter = "";
        for (var i = 0; i < inputs.Length; i++)
            filter += string.Create(CultureInfo.InvariantCulture, $"[{i}:v]setsar=1[s{i}];");
        filter += string.Concat(Enumerable.Range(0, inputs.Length).Select(i => "[s" + i.ToString(CultureInfo.InvariantCulture) + "]")) +
            "vstack=inputs=" + inputs.Length.ToString(CultureInfo.InvariantCulture) + "[stitched]";
        args.AddRange(["-filter_complex", filter, "-map", "[stitched]", "-frames:v", "1", output]);
        return PlayerToolPlan.Image(executable, args.AsReadOnly(), output, Array.AsReadOnly(inputs));
    }

    private static string Executable(PlayerToolsSettings settings) => PlayerToolPaths.IsExecutableAvailable(settings.FfmpegPath)
        ? PlayerToolPaths.RequireInput(settings.FfmpegPath) : throw new PlayerToolException("请先配置 FFmpeg 图片工具。");
    private static string Input(string path)
    {
        var full = PlayerToolPaths.RequireMediaInput(path);
        ImageExtension(full);
        return full;
    }
    private static string Output(string path)
    {
        var full = PlayerToolPaths.RequireOutput(path);
        ImageExtension(full);
        return full;
    }
    private static void ImageExtension(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            throw new PlayerToolException("图片工具支持 PNG、JPEG 和 WebP 格式。");
    }
}
