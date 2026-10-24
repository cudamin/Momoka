using Momoka.Infrastructure;

namespace Momoka.Translation;

/// <summary>
/// 本次翻译会话内的有界 LRU。不落磁盘、不保存密钥；Reset 在换片、换密钥、
/// 换 provider/语言/endpoint/model 后由父调用，旧 scope 不能读写新缓存。
/// </summary>
public sealed class SubtitleTranslationCache
{
    public const int DefaultCapacity = 128;
    public const int MaxCapacity = 512;
    private readonly object _gate = new();
    private readonly LruCache<string, string> _entries;
    private long _scope;

    public SubtitleTranslationCache(int capacity = DefaultCapacity)
    {
        if (capacity is < 1 or > MaxCapacity) throw new ArgumentOutOfRangeException(nameof(capacity), "字幕缓存容量必须在 1–512 之间。");
        _entries = new LruCache<string, string>(capacity);
    }

    public int Count { get { lock (_gate) return _entries.Count; } }
    public int Capacity => _entries.Capacity;
    public long Scope { get { lock (_gate) return _scope; } }

    public long Reset()
    {
        lock (_gate)
        {
            _entries.Clear();
            return ++_scope;
        }
    }

    public bool TryGet(long scope, string text, out string translated)
    {
        lock (_gate)
        {
            translated = "";
            if (scope != _scope || !ValidSource(text) || !_entries.TryGet(text, out var value)) return false;
            translated = value;
            return true;
        }
    }

    public bool Store(long scope, string text, string translated)
    {
        lock (_gate)
        {
            if (scope != _scope || !ValidSource(text) || string.IsNullOrWhiteSpace(translated) ||
                translated.Length > SubtitleTranslationClient.MaxTranslatedTextLength || translated.Contains('\0')) return false;
            _entries.Set(text, translated);
            return true;
        }
    }

    private static bool ValidSource(string text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= SubtitleTranslationClient.MaxTextLength;
}
