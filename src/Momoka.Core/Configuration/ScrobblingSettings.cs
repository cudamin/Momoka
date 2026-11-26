namespace Momoka.Configuration;

/// <summary>Local scrobbling connections; never included in preference backups.</summary>
public sealed class ScrobblingSettings
{
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
