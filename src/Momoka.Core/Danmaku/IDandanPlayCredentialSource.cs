namespace Momoka.Danmaku;

/// <summary>
/// 一份可用于签名的弹弹play 应用凭据的来源。
/// <para>
/// 产品里只有一种实现：向 Momoka 自己的端点取内置凭据（<see cref="DandanPlayBuiltInCredentialSource"/>）。
/// 抽成接口是为了让离线探针与单元测试能塞一份固定凭据进去 —— 它们要验的是签名、缓存、重定向与发送语义，
/// 不是凭据端点本身；不塞的话每一条请求都要先多走一次假端点，把「发了几个请求」这类判据全带偏。
/// </para>
/// </summary>
public interface IDandanPlayCredentialSource
{
    Task<DandanPlayCredential> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>丢掉手里的这一份，下一次 <see cref="GetAsync"/> 重新要一份。鉴权失败时调用。</summary>
    void Invalidate();
}
