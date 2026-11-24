using System.Globalization;
using System.Text.Json;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private IReadOnlyDictionary<string, string?> _menuReadings = new Dictionary<string, string?>();

    internal IReadOnlyList<PlayerMenuNode> BuildCurrentMenu(IReadOnlyDictionary<string, string?>? readings = null)
    {
        if (readings is not null) _menuReadings = readings;
        var available = AvailableMenuRequirements(_menuReadings);
        var nodes = FilterMenu(MpvSvp.Menu(PlayingWithSvp), available, _menuReadings).ToList();
        var tracks = new List<PlayerMenuNode>();
        AddTracks("视频轨道", "video", "vid", false);
        AddTracks("音频轨道", "audio", "aid", false);
        AddTracks("主字幕", "sub", "sid", true);
        AddTracks("次字幕", "sub", "secondary-sid", true);
        if (CurrentChapters.Count > 0)
            tracks.Add(DynamicGroup("章节列表", CurrentChapters.Select((chapter, index) =>
                new PlayerMenuNode(PlayerMenuKind.Command,
                    string.IsNullOrWhiteSpace(chapter.Title) ? $"章节 {index + 1}" : chapter.Title,
                    [["seek", chapter.Start.ToString(CultureInfo.InvariantCulture), "absolute+exact"]], "章节：${chapter}", []))));
        if (tracks.Count > 0) nodes.Insert(0, DynamicGroup("轨道与章节", tracks));
        var audioIndex = nodes.FindIndex(node => node.Label == "音频");
        if (audioIndex >= 0 && BuildAudioDevices(_menuReadings) is { } devices)
            nodes[audioIndex] = nodes[audioIndex] with { Children = [devices, .. nodes[audioIndex].Children] };
        var navigation = nodes.FindIndex(node => node.Label == "导航");
        if (navigation >= 0 && MpvAnalysis.IsTrue(_menuReadings.GetValueOrDefault(MpvAnalysis.ReadyProperty)))
            nodes[navigation] = nodes[navigation] with { Children = [.. nodes[navigation].Children, ChapterToolMenu()] };
        if (navigation >= 0 && Episodes.Count > 1)
            nodes[navigation] = nodes[navigation] with
            {
                Children =
                [HostRow("上一集", "previous-episode", "previous-episode"), HostRow("下一集", "next-episode", "next-episode"), .. nodes[navigation].Children]
            };
        if (navigation >= 0 && CanArrangeQueue)
            nodes[navigation] = nodes[navigation] with { Children = [.. nodes[navigation].Children, BuildQueueMenu()] };
        var subtitleIndex = nodes.FindIndex(node => node.Label == "字幕");
        if (subtitleIndex >= 0 && (available & PlayerMenuRequirement.TextSubtitle) != 0)
            nodes[subtitleIndex] = nodes[subtitleIndex] with { Children = [.. nodes[subtitleIndex].Children, HostRow("查看当前字幕内容", "subtitle-content")] };
        if (CanDownloadPlayingSubtitles)
        {
            var download = HostRow("搜索与下载字幕", "subtitle-download");
            if (subtitleIndex >= 0) nodes[subtitleIndex] = nodes[subtitleIndex] with { Children = [.. nodes[subtitleIndex].Children, download] };
            else nodes.Add(DynamicGroup("字幕", [download]));
        }
        return nodes;

        void AddTracks(string label, string type, string property, bool off)
        {
            var matching = Tracks.Where(track => track.Type == type).ToArray();
            if (matching.Length == 0) return;
            var rows = new List<PlayerMenuNode>();
            if (off) rows.Add(DynamicCommand("关闭", property, "no"));
            rows.AddRange(matching.Select(track =>
            {
                var row = DynamicCommand(string.IsNullOrWhiteSpace(track.DisplayName) ? $"轨道 {track.Id}" : track.DisplayName,
                    property, track.Id.ToString(CultureInfo.InvariantCulture), "轨道已切换");
                return type == "sub" ? row with
                {
                    Commands = [.. row.Commands,
                    new[] { "set", property == "secondary-sid" ? "secondary-sub-visibility" : "sub-visibility", "yes" }]
                } : row;
            }));
            tracks.Add(DynamicGroup(label, rows));
        }
    }

    private PlayerMenuNode BuildQueueMenu()
    {
        var rows = new List<PlayerMenuNode>
        {
            HostRow("下一项播放…", "video-queue-next") with { AutomationId = "PlayerQueueNext" },
            HostRow("追加到队尾…", "video-queue-append") with { AutomationId = "PlayerQueueAppend" }
        };
        if (HasPendingQueue)
        {
            rows.Add(HostRow("管理待播项…", "video-queue-manage") with { AutomationId = "PlayerQueueManage" });
            rows.Add(HostRow("清空待播项", "video-queue-clear") with { AutomationId = "PlayerQueueClear" });
        }
        return DynamicGroup("本次播放队列", rows) with { AutomationId = "PlayerSessionVideoQueue" };
    }

    private PlayerMenuRequirement AvailableMenuRequirements(IReadOnlyDictionary<string, string?> values)
    {
        var result = PlayerMenuRequirement.None;
        if (MpvAnalysis.IsTrue(values.GetValueOrDefault(MpvAnalysis.CropProperty))) result |= PlayerMenuRequirement.CropDetection;
        if (MpvAnalysis.IsTrue(values.GetValueOrDefault(MpvAnalysis.SilenceProperty))) result |= PlayerMenuRequirement.SilenceDetection;
        if (Settings.Mpv.Backend == Momoka.Configuration.MpvBackendKind.BuiltInLibMpv && !Settings.Mpv.CustomConfigEnabled)
            result |= PlayerMenuRequirement.ManagedCapture;
        if (Tracks.Any(track => track.Type == "video")) result |= PlayerMenuRequirement.Video;
        if (Tracks.Any(track => track.IsAudio)) result |= PlayerMenuRequirement.Audio;
        if (Tracks.Any(track => track.IsSubtitle)) result |= PlayerMenuRequirement.Subtitle;
        if (Tracks.Any(track => track.IsSubtitle && track.Selected && track.MainSelection == 1)) result |= PlayerMenuRequirement.SecondarySubtitle;
        var primary = Tracks.FirstOrDefault(track => track.IsSubtitle && track.Selected && track.MainSelection != 1);
        if (primary is { Image: true }) result |= PlayerMenuRequirement.ImageSubtitle;
        if (primary is { Image: false }) result |= PlayerMenuRequirement.TextSubtitle;
        if (primary?.Codec is "ass" or "ssa") result |= PlayerMenuRequirement.AssSubtitle;
        if (Tracks.Any(track => track.Type == "video" && track.Codec == "h264")) result |= PlayerMenuRequirement.H264;
        if (ChapterMarks.Count > 0) result |= PlayerMenuRequirement.Chapters;
        if (values.GetValueOrDefault("video-params/gamma") is "pq" or "hlg") result |= PlayerMenuRequirement.Hdr;
        if (values.GetValueOrDefault("current-gpu-context")?.Contains("d3d11", StringComparison.OrdinalIgnoreCase) == true
            || values.GetValueOrDefault("gpu-api") == "d3d11") result |= PlayerMenuRequirement.D3D11;
        return result;
    }

    private IReadOnlyList<PlayerMenuNode> FilterMenu(IReadOnlyList<PlayerMenuNode> nodes, PlayerMenuRequirement available,
        IReadOnlyDictionary<string, string?> values)
    {
        var result = new List<PlayerMenuNode>();
        foreach (var node in nodes)
        {
            if (node.Kind == PlayerMenuKind.Separator)
            {
                if (result.Count > 0 && result[^1].Kind != PlayerMenuKind.Separator) result.Add(node);
                continue;
            }
            if ((node.Requirement & available) != node.Requirement) continue;
            if (node.Kind == PlayerMenuKind.Group)
            {
                var children = FilterMenu(node.Children, available, values);
                if (children.Count > 0) result.Add(node with { Children = children });
            }
            else if (node.RequireProperties.All(property => values.GetValueOrDefault(property) is not null)
                && !node.Commands.Any(command => ShaderChainRules.ConflictsWith(ActiveShader, command))) result.Add(node);
        }
        if (result.Count > 0 && result[^1].Kind == PlayerMenuKind.Separator) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static PlayerMenuNode? BuildAudioDevices(IReadOnlyDictionary<string, string?> values)
    {
        if (values.GetValueOrDefault("audio-device-list") is not { Length: > 0 } json) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var rows = new List<PlayerMenuNode>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var property) || property.GetString() is not { Length: > 0 } name) continue;
                var label = item.TryGetProperty("description", out var description) ? description.GetString() : name;
                rows.Add(DynamicCommand(label ?? name, "audio-device", name, "音频设备：${audio-device}"));
            }
            return rows.Count > 0 ? DynamicGroup("音频设备", rows) : null;
        }
        catch (JsonException) { return null; }
    }
}
