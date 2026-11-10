using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Momoka.Configuration;
using Momoka.Translation;

namespace Momoka.Playback;

public sealed record SrtCue(long StartMilliseconds, long EndMilliseconds, string Text);

/// <summary>只解析 SRT 时间轴与原文；全片翻译逐条复用进程内客户端，不执行字幕内容中的指令。</summary>
public sealed partial class SrtDocument
{
    private SrtDocument(IReadOnlyList<SrtCue> cues) => Cues = cues;
    public IReadOnlyList<SrtCue> Cues { get; }
    public const int MaxDocumentLength = 4 * 1024 * 1024;

    public static SrtDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxDocumentLength || text.Contains('\0')) throw new PlayerToolException("字幕文件过大或内容无效。");
        var normalized = text.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var blocks = Blocks().Split(normalized);
        var cues = new List<SrtCue>();
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timeLine = lines.Length > 1 && long.TryParse(lines[0].Trim(), out _) ? 1 : 0;
            var match = Timeline().Match(lines[timeLine]);
            if (!match.Success || lines.Length <= timeLine + 1) throw new PlayerToolException("当前字幕不是有效的 SRT 文件。");
            var start = Milliseconds(match.Groups, 1);
            var end = Milliseconds(match.Groups, 5);
            var cueText = string.Join('\n', lines.Skip(timeLine + 1));
            if (end <= start || string.IsNullOrWhiteSpace(cueText)) throw new PlayerToolException("SRT 字幕时间轴或原文无效。");
            cues.Add(new(start, end, cueText));
        }
        if (cues.Count == 0) throw new PlayerToolException("当前字幕没有可处理的 SRT 内容。");
        return new(cues.AsReadOnly());
    }

    public string ToText()
    {
        var text = new StringBuilder();
        for (var i = 0; i < Cues.Count; i++)
        {
            var cue = Cues[i];
            text.Append(i + 1).Append('\n').Append(Timestamp(cue.StartMilliseconds)).Append(" --> ")
                .Append(Timestamp(cue.EndMilliseconds)).Append('\n').Append(cue.Text).Append("\n\n");
        }
        return text.ToString();
    }

    public SrtDocument ShiftMilliseconds(long offset)
    {
        try
        {
            var shifted = Cues.Select(cue => cue with
            {
                StartMilliseconds = checked(cue.StartMilliseconds + offset),
                EndMilliseconds = checked(cue.EndMilliseconds + offset)
            }).ToArray();
            if (shifted.Any(cue => cue.StartMilliseconds < 0 || cue.EndMilliseconds <= cue.StartMilliseconds))
                throw new PlayerToolException("字幕时间偏移超出有效范围。");
            return new(Array.AsReadOnly(shifted));
        }
        catch (OverflowException) { throw new PlayerToolException("字幕时间偏移超出有效范围。"); }
    }

    public async Task<SrtDocument> TranslateAsync(SubtitleTranslationClient client, SubtitleTranslationSettings settings,
        string apiKey, CancellationToken cancellationToken, IProgress<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        if (!settings.Enabled) throw new PlayerToolException("请先启用并配置字幕翻译服务。");
        // 整份字幕使用一份服务快照，切换设置不把同一个输出发给两个不同服务。
        var snapshot = new SubtitleTranslationSettings
        {
            Enabled = true, Provider = settings.Provider, SourceLanguage = settings.SourceLanguage,
            TargetLanguage = settings.TargetLanguage, GoogleEndpoint = settings.GoogleEndpoint,
            MicrosoftEndpoint = settings.MicrosoftEndpoint, MicrosoftRegion = settings.MicrosoftRegion,
            AiEndpoint = settings.AiEndpoint, AiModel = settings.AiModel
        };
        if (Cues.Any(cue => cue.Text.Length > SubtitleTranslationClient.MaxTextLength))
            throw new PlayerToolException("字幕中存在过长的单句，无法整片翻译。");
        var translated = new List<SrtCue>(Cues.Count);
        foreach (var cue in Cues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await client.TranslateAsync(snapshot, cue.Text, apiKey, cancellationToken).ConfigureAwait(false);
            // 空白行会在 SRT 中结束一条字幕，因此把服务返回的空行规范为单行分隔。
            text = Blocks().Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim(), "\n");
            translated.Add(cue with { Text = text });
            progress?.Report(translated.Count * 100 / Cues.Count);
        }
        return new(translated.AsReadOnly());
    }

    private static long Milliseconds(GroupCollection groups, int offset)
    {
        var hours = long.Parse(groups[offset].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(groups[offset + 1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(groups[offset + 2].Value, CultureInfo.InvariantCulture);
        var milliseconds = int.Parse(groups[offset + 3].Value, CultureInfo.InvariantCulture);
        if (minutes > 59 || seconds > 59) throw new PlayerToolException("SRT 字幕时间格式无效。");
        return ((hours * 60 + minutes) * 60 + seconds) * 1000 + milliseconds;
    }
    private static string Timestamp(long value) => string.Create(CultureInfo.InvariantCulture,
        $"{value / 3_600_000:00}:{value / 60_000 % 60:00}:{value / 1000 % 60:00},{value % 1000:000}");

    [GeneratedRegex(@"\n[ \t]*\n+", RegexOptions.CultureInvariant)]
    private static partial Regex Blocks();
    [GeneratedRegex(@"^\s*(\d{1,5}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{1,5}):(\d{2}):(\d{2})[,.](\d{3})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Timeline();
}
