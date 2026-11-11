namespace Momoka.Emby;

/// <summary>Server-provided conversion choices, filtered to ConvertSyncProvider.</summary>
public sealed class EmbyConversionOptions
{
    public List<EmbyConversionTarget> Targets { get; set; } = [];
    public List<string> Options { get; set; } = [];
    public List<EmbyConversionProfile> ProfileOptions { get; set; } = [];
    public List<EmbyConversionQuality> QualityOptions { get; set; } = [];
}

public sealed class EmbyConversionTarget
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class EmbyConversionProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool? IsDefault { get; set; }
    public bool? EnableQualityOptions { get; set; }
}

public sealed class EmbyConversionQuality
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsOriginalQuality { get; set; }
}

/// <summary>A native dialog's selection; item IDs and account identity remain owned by the operation.</summary>
public sealed record EmbyConversionSelection(string TargetId, string? Profile = null, string? Quality = null,
    int? Bitrate = null, string? Container = null, string? VideoCodec = null, string? AudioCodec = null,
    bool? UnwatchedOnly = null, bool? SyncNewContent = null, int? ItemLimit = null);

/// <summary>Acceptance of a server job is separate from completion of its conversions.</summary>
public sealed class EmbyConversionJobReceipt
{
    public EmbyConversionJob? Job { get; set; }
    public List<EmbyConversionJobItem> JobItems { get; set; } = [];
}

public sealed class EmbyConversionJob
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Status { get; set; }
}

public sealed class EmbyConversionJobItem
{
    public string Id { get; set; } = "";
    public string? ItemId { get; set; }
    public string? Status { get; set; }
}

public sealed class EmbyPlaylistCreationResult
{
    public string Id { get; set; } = "";
    public int? ItemAddedCount { get; set; }
}

/// <summary>Only server-advertised fields may be submitted by the native conversion form.</summary>
public static class EmbyConversion
{
    public static void Validate(EmbyConversionOptions options, EmbyConversionSelection selection)
    {
        if (!options.Targets.Any(target => target.Id == selection.TargetId))
            throw new InvalidOperationException("转换目标已不可用，请重新选择");
        var profile = options.ProfileOptions.FirstOrDefault(value => value.Id == selection.Profile);
        if (options.Options.Contains("Profile", StringComparer.Ordinal) && options.ProfileOptions.Count > 0 && profile is null)
            throw new InvalidOperationException("请选择服务器提供的转换配置");
        if (!string.IsNullOrEmpty(selection.Profile)
            && (!options.Options.Contains("Profile", StringComparer.Ordinal) || profile is null))
            throw new InvalidOperationException("服务器不支持此转换配置");
        var hasQuality = options.Options.Contains("Quality", StringComparer.Ordinal) && options.QualityOptions.Count > 0
            && (profile is null || profile.EnableQualityOptions == true);
        if (hasQuality && !options.QualityOptions.Any(value => value.Id == selection.Quality))
            throw new InvalidOperationException("请选择服务器提供的转换画质");
        if (!hasQuality && !string.IsNullOrEmpty(selection.Quality))
            throw new InvalidOperationException("此转换配置不支持画质选项");
        if (selection.Bitrate is not null && (!hasQuality || selection.Quality != "custom" || selection.Bitrate <= 0))
            throw new InvalidOperationException("自定义码率必须大于零且使用自定义画质");
        if (hasQuality && selection.Quality == "custom" && selection.Bitrate is null)
            throw new InvalidOperationException("请填写自定义码率");
        if (selection.Profile == "custom")
        {
            // These are the choices in Emby 4.10.1 Web's native custom-profile form.
            if (selection.Container is not ("mkv" or "mp4" or "ts")
                || selection.VideoCodec is not ("h264" or "hevc" or "h264,hevc" or "hevc,h264")
                || selection.AudioCodec is not ("aac" or "mp3" or "aac,mp3" or "aac,mp3,ac3"))
                throw new InvalidOperationException("请选择有效的容器和音视频编码");
        }
        else if (selection.Container is not null || selection.VideoCodec is not null || selection.AudioCodec is not null)
            throw new InvalidOperationException("容器和编码只用于自定义转换配置");
        if (selection.UnwatchedOnly is not null && !options.Options.Contains("UnwatchedOnly", StringComparer.Ordinal)
            || selection.SyncNewContent is not null && !options.Options.Contains("SyncNewContent", StringComparer.Ordinal)
            || selection.ItemLimit is not null && (!options.Options.Contains("ItemLimit", StringComparer.Ordinal) || selection.ItemLimit <= 0))
            throw new InvalidOperationException("服务器不支持所选转换选项");
    }
}
