namespace Momoka.Translation;

/// <summary>
/// 无计时器、无后台循环的最新句规则。清空、换片、换设置均使在途结果失效；
/// 同一句 A→B→A 也不能接回旧 A。父 Runtime 持有取消和 mpv 写入通道。
/// </summary>
public sealed class LatestCue
{
    private readonly object _gate = new();
    private long _revision;
    private long _fileGeneration;
    private TranslationCue? _current;

    public TranslationCue? Current { get { lock (_gate) return _current; } }

    public void Reset(long fileGeneration)
    {
        lock (_gate)
        {
            _fileGeneration = fileGeneration;
            _revision++;
            _current = null;
        }
    }

    /// <summary>重复原句返回原 cue；空/长句清空，确保迟到响应不能重新显示。</summary>
    public TranslationCue? Update(long fileGeneration, string? text)
    {
        lock (_gate)
        {
            // 文件代次单调递增；旧文件回调不能把最新状态退回上一个文件。
            if (fileGeneration < _fileGeneration) return null;
            if (fileGeneration != _fileGeneration)
            {
                _fileGeneration = fileGeneration;
                _revision++;
                _current = null;
            }
            if (string.IsNullOrWhiteSpace(text) || text.Length > SubtitleTranslationClient.MaxTextLength)
            {
                if (_current.HasValue) _revision++;
                _current = null;
                return null;
            }
            if (_current is { } cue && string.Equals(cue.Text, text, StringComparison.Ordinal)) return cue;
            _current = new TranslationCue(fileGeneration, ++_revision, text);
            return _current;
        }
    }

    public bool IsCurrent(TranslationCue cue)
    {
        lock (_gate) return _current is { } current && current == cue;
    }
}
