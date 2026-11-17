namespace Momoka.Emby;

/// <summary>
/// YouTube 预告的登录判定。只做纯规则，不联网、不碰 WebView2 —— Cookie 名单由外壳喂进来，
/// 这样「哪种状态算已登录」可以在控制台测试里钉住。
/// </summary>
public static class YouTubeTrailer
{
    /// <summary>
    /// Google 登录后写入的会话 Cookie。看到任意一个就认为这个 WebView2 配置已经登录；只看名字，
    /// 不读取、不记录 Cookie 值。
    /// </summary>
    public static bool HasSession(IEnumerable<string> cookieNames)
    {
        foreach (var name in cookieNames)
            if (name is "SAPISID" or "__Secure-1PSID" or "__Secure-3PSID" or "LOGIN_INFO") return true;
        return false;
    }
}
