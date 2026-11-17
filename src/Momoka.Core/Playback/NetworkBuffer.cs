using Momoka.Configuration;

namespace Momoka.Playback;

/// <summary>
/// 一次起播要发给播放器内核的那一组缓冲数：前向字节上限与按秒的上限，已经按
/// <see cref="NetworkScope"/> 从 <see cref="BufferSettings"/> 里挑好、夹好。
/// <para>
/// 单独一个类型而不是把 <see cref="BufferSettings"/> 整个交给 <see cref="Mpv.MpvOutputOptions"/>：
/// 「哪一组」是起播那一刻按服务器地址定的结论，定下来之后下发端只需要两个数 —— 把四选二的判断留在
/// <see cref="For(BufferSettings, NetworkScope)"/> 一处，单测才钉得住「内网地址真的走内网那组」。
/// </para>
/// </summary>
public readonly record struct NetworkBuffer(int Megabytes, int Seconds)
{
    /// <summary>前向缓冲上限，字节。mpv 的 <c>demuxer-max-bytes</c>。</summary>
    public long Bytes => (long)Megabytes * 1024 * 1024;

    /// <summary>后向缓冲上限，字节：前向的一半，短距离回跳不用重下。mpv 的 <c>demuxer-max-back-bytes</c>。</summary>
    public long BackBytes => Bytes / 2;

    /// <summary>按网络类型取一组，并夹进存储端同一对范围（手改文件写出的越界值到不了 mpv）。</summary>
    public static NetworkBuffer For(BufferSettings settings, NetworkScope scope) => scope == NetworkScope.Lan
        ? new(BufferSettings.ClampMegabytes(settings.LanMegabytes), BufferSettings.ClampSeconds(settings.LanSeconds))
        : new(BufferSettings.ClampMegabytes(settings.WanMegabytes), BufferSettings.ClampSeconds(settings.WanSeconds));

    /// <summary>按服务器地址取一组 —— 起播那条路用的就是这一句。</summary>
    public static NetworkBuffer For(BufferSettings settings, Uri? address) =>
        For(settings, NetworkScopeRule.Classify(address));
}
