using Momoka.Danmaku;

namespace Momoka.Shell;

/// <summary>
/// 离线探针用的固定应用凭据：不联网、不读 DPAPI，只让签名那条路径有东西可签。
/// <para>
/// 产品里没有任何地方用它 —— 真实客户端只接 <see cref="DandanPlayBuiltInCredentialSource"/>。探针要验的是
/// 面板、输入栏、发送回执与版式，不是凭据端点；走真端点的话每条请求前面都会多一次假网络请求，把「发了几次」
/// 这类判据全带偏。
/// </para>
/// </summary>
internal sealed class DanmakuProbeCredentials(string appId, string secret) : IDandanPlayCredentialSource
{
    public Task<DandanPlayCredential> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DandanPlayCredential(appId, secret, "probe"));

    public void Invalidate() { }
}
