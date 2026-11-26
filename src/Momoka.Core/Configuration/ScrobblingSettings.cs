namespace Momoka.Configuration;

/// <summary>Scrobbling preferences and local-only site connections.</summary>
public sealed class ScrobblingSettings
{
    private volatile bool _notifyOnFailure;

    // Keep the persisted setting name so existing preferences also apply to recorded skips.
    public bool NotifyOnFailure { get => _notifyOnFailure; set => _notifyOnFailure = value; }
    public BangumiScrobblingSettings Bangumi { get; set; } = new();
    public DoubanScrobblingSettings Douban { get; set; } = new();
}

public sealed class BangumiScrobblingSettings
{
    public bool Enabled { get; set; }
    public string ProtectedAccessToken { get; set; } = "";
}

public sealed class DoubanScrobblingSettings
{
    public bool Enabled { get; set; }
}
