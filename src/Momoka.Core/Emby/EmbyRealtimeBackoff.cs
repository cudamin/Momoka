namespace Momoka.Emby;

/// <summary>
/// 实时通道断了之后隔多久再连。<b>纯计算</b>，不起钟、不连网 —— 退避序列是能单测钉死的，而「重连太密」这种毛病
/// 在屏上只表现为「一直好好的，偶尔卡一下」。
/// <para>
/// 参考 Emby Theater 的取向，但比它多一层：Theater 自己的 WebSocket <b>没有退避循环</b>（<c>apiclient.js</c>
/// 的 <c>onclose</c> 只把引用置空、触发 <c>websocketclose</c>），重连靠外部事件推（<c>onAppResume</c>、
/// <c>onNetworkChanged</c>，以及任何一次 HTTP 请求失败重连成功后跟一句 <c>ensureWebSocket</c>）。Momoka 的
/// 主页不像 Theater 那样持续发请求，没有那个「外部事件」可依赖，所以自己带一条退避：断了就自己重来，
/// 不必等下一次用户操作。
/// </para>
/// <para>
/// 上界卡在 <see cref="MaxSeconds"/>：通道断了本来就有 30 秒轮询兜底，重连间隔比兜底那一趟还长就没有意义了。
/// </para>
/// </summary>
public sealed class EmbyRealtimeBackoff
{
    /// <summary>第一次重连等这么久。快，是因为多数断线是瞬时的（切网、服务器重启刚起来）。</summary>
    public const int FirstSeconds = 1;

    /// <summary>退避的上界。到这儿就一直是这个间隔（见类型注释：再长不如交给 30 秒那道兜底）。</summary>
    public const int MaxSeconds = 30;

    private int _seconds;

    /// <summary>这一趟该等多久，并把下一次的间隔翻一倍（到顶为止）。</summary>
    public TimeSpan Next()
    {
        var wait = _seconds == 0 ? FirstSeconds : Math.Min(_seconds * 2, MaxSeconds);
        _seconds = wait;
        return TimeSpan.FromSeconds(wait);
    }

    /// <summary>
    /// 连上过一次就归零：下一次断线从头退避。
    /// <para>
    /// 不归零的话，一条跑了一整天、断过十几次的连接，最后一次断线会直接等满 30 秒才重连 —— 但那次断线可能
    /// 只是刚才那一瞬间的事，和之前那十几次没有关系。
    /// </para>
    /// </summary>
    public void Reset() => _seconds = 0;
}
