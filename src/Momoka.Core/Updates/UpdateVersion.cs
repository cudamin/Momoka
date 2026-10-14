namespace Momoka.Updates;

/// <summary>
/// 版本号怎么读、怎么比大小 —— 「启动时自动检查新版本」这件事的全部判据，也就是全部会悄悄错掉的地方。
/// <para>
/// 两条都错得起，而两条都不会在屏上报错。tag 认错了（发布页上的 tag 一律带一个 <c>v</c>，本地手上这一版不带），
/// 比出来的结果就是「没有新版本」，用户从此再也看不到更新，而界面上一切正常；比错了（<c>0.1.6</c> 与
/// <c>0.1.6.0</c> 在 <see cref="Version"/> 眼里是两个不同的版本，缺省的段是 -1 而不是 0），同一版会被当成新版本
/// 反复提示，用户按提示去下一遍，下回来的还是手上这一份。所以这两件事住在 Core、由单测钉着，不在界面那一层现算。
/// </para>
/// </summary>
public static class UpdateVersion
{
    /// <summary>
    /// 从发布页的 tag 读出版本号，认不出来返回 false。发布页上可能有 <c>nightly</c>、<c>v0.1.6-beta</c> 这种
    /// 不是版本号的 tag —— 跳过一个比猜一个准，猜错了要么漏掉更新，要么把一版预览当成正式版推给用户。
    /// </summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0);

        var text = tag?.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        if (text[0] is 'v' or 'V') text = text[1..].Trim();

        // 至少要「主.次」两段：单独一个 `1` 也会被 Version.TryParse 收下（变成 1.0），那是另一个东西。
        if (!text.Contains('.') || !Version.TryParse(text, out var parsed)) return false;

        version = Normalize(parsed);
        return true;
    }

    /// <summary>
    /// 补齐到四段。<see cref="Version"/> 缺省的那两段是 -1 而不是 0，于是 <c>0.1.6</c> 与 <c>0.1.6.0</c> 比出来
    /// 并不相等 —— 不补齐的话，同一版会被当成新版本反复提示，而且只在其中一边写成四段时才发作。
    /// </summary>
    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>比大小，正数表示 <paramref name="left"/> 更新。</summary>
    public static int Compare(Version left, Version right) => Normalize(left).CompareTo(Normalize(right));

    /// <summary>
    /// 这份发布清单里比 <paramref name="current"/> 新的最高一版，没有就是 null。
    /// <para>
    /// <b>不吃 <c>draft</c>，但照收 <c>prerelease</c>。</b>草稿是还没发布的东西，连发布页上都不显示，拿它当
    /// 「有新版本」等于把一个还没做完的构建推给用户。<c>prerelease</c> 则是另一回事：本项目的 Release 全被标成了
    /// pre-release（见发布记录），按它过滤就等于永远查不到任何更新 —— 而那种错屏上一个字都不说，只有「怎么从来不
    /// 提示更新」这一个症状。
    /// </para>
    /// </summary>
    public static AppRelease? NewestNewer(IEnumerable<AppRelease> releases, Version current)
    {
        AppRelease? newest = null;

        foreach (var release in releases)
        {
            if (Compare(release.Version, current) <= 0) continue;
            if (newest is null || Compare(release.Version, newest.Version) > 0) newest = release;
        }

        return newest;
    }
}
