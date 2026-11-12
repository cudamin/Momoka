using Momoka.Emby;

namespace Momoka.Shell;

internal static class InfoTableProbeFixture
{
    internal static EmbyItem Movie() => new()
    {
        Id = "info-movie", Name = "匿名媒体信息定位", Type = EmbyItemType.Movie,
        Overview = "固定匿名10行媒体信息，仅验证正文滚动、表格虚拟化和可达性。",
        MediaSources = [new MediaSource
        {
            Id = "info-source", Name = "anonymous-file.mkv", Container = "mkv", Size = 1024 * 1024 * 1024L,
            RunTimeTicks = TimeSpan.FromMinutes(90).Ticks,
            MediaStreams =
            [
                new() { Type = "Video", Index = 0, Codec = "h264", Width = 1920, Height = 1080, BitRate = 8_000_000, BitDepth = 8, AverageFrameRate = 24 },
                new() { Type = "Audio", Index = 1, Codec = "aac", IsDefault = true, DisplayTitle = "匿名音轨" },
                new() { Type = "Subtitle", Index = 2, Codec = "srt", DisplayTitle = "匿名字幕" }
            ]
        }],
        ImageTags = new() { ["Primary"] = "fixture" }, BackdropImageTags = ["fixture"]
    };

    internal static void RequireRendered(int expected, IReadOnlyList<int> realized, bool firstLabelRendered)
    {
        if (expected <= 0 || !firstLabelRendered || !realized.SequenceEqual(Enumerable.Range(0, expected)))
            throw new InvalidOperationException("固定信息表未完整呈现所有行与首标签，详见匿名几何JSON");
    }
}
