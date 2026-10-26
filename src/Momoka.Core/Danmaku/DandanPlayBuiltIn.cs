namespace Momoka.Danmaku;

/// <summary>
/// 内置弹幕凭据端点的固定参数。
/// <para>
/// 这里不是机密：端点公开、密钥随客户端分发，愿意动手的人总能把 AppSecret 取出来。它做到的是把密钥从公开
/// 仓库与安装包源码里挪到服务器上，并且能在不发新版的情况下轮换；它挡不住有意提取的人，也不改变共享额度。
/// 真正保密只能走服务端代理，见 docs/弹幕与快捷键.md。
/// </para>
/// </summary>
public static class DandanPlayBuiltIn
{
    /// <summary>
    /// 凭据端点：官网项目（Cloudflare Workers 或 Pages Function）的
    /// <c>/api/danmaku/credential</c>。换域名或换部署时只改这一处。
    /// </summary>
    public const string Endpoint = "https://yuanshenqidon.dpdns.org/api/danmaku/credential";

    /// <summary>AES-GCM 密钥（base64，32 字节）。必须与 Worker 的 DANDANPLAY_CREDENTIAL_KEY 相同。</summary>
    public const string Key = "5DWehU1OekRi1kSL/cYFRiAF2gLOJMLRGJoVropNNDI=";

    public const int MaximumResponseBytes = 64 * 1024;

    /// <summary>到期前多久开始换新；缓存文件在到期前仍可用于抵抗一次网络抖动。</summary>
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromDays(1);
}
