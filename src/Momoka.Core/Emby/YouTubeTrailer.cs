namespace Momoka.Emby;

/// <summary>
/// YouTube 预告的登录判定。只做纯规则，不联网、不碰 WebView2 —— 播放器事件与 Cookie 名单由外壳喂进来，
/// 这样「哪种失败算需要登录」可以在控制台测试里钉住。
/// </summary>
public static class YouTubeTrailer
{
    /// <summary>
    /// YouTube IFrame API 把「需要登录」和「不允许嵌入」都报成 101／150：匿名嵌入拿到的是登录卡片，
    /// 已登录时仍报同一码的通常是视频本身禁止嵌入，由调用方再按登录状态收口。
    /// </summary>
    public static bool NeedsSignIn(int errorCode) => errorCode is 101 or 150;

    /// <summary>
    /// Google 登录后写入的会话 Cookie。看到任意一个就认为这个 WebView 配置已经登录；只看名字，
    /// 不读取、不记录 Cookie 值。
    /// </summary>
    public static bool HasSession(IEnumerable<string> cookieNames)
    {
        foreach (var name in cookieNames)
            if (name is "SAPISID" or "__Secure-1PSID" or "__Secure-3PSID" or "LOGIN_INFO") return true;
        return false;
    }
}
