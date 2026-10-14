namespace Momoka.Updates;

/// <summary>
/// 发布页上的一个版本：它哪一版、tag 叫什么、点开会到哪儿。
/// <para>
/// 只有这三样，因为这件事只做到「告诉你有没有新版、给你一个下载页的入口」—— 不自动下载、不静默安装，所以资产
/// 清单、校验值、更新说明一个字都不必带。要不要下、下哪一个（安装包还是便携版），是用户到发布页上自己挑的事。
/// </para>
/// </summary>
/// <param name="Version">
/// 从 tag 读出来的版本号，已补齐四段（见 <see cref="UpdateVersion.Normalize"/>），比较用。
/// </param>
/// <param name="Tag">发布页上那个 tag 的原样，如 <c>v0.1.6</c>，写日志用。</param>
/// <param name="PageUrl">这一版的发布页地址，用户按下「打开下载页」时浏览器打开的就是它。</param>
public sealed record AppRelease(Version Version, string Tag, string PageUrl)
{
    /// <summary>给人看的版本号，固定三段：<c>0.1.6</c>。</summary>
    public string VersionText => Version.ToString(3);
}
