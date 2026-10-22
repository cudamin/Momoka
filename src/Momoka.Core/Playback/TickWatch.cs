namespace Momoka.Playback;

/// <summary>
/// 播放页那颗十赫兹心跳的看门狗判据：<b>该跳的时候多久没跳，才算真的停了</b>。
/// <para>
/// 播放页上几乎每一样「过一会儿自己会发生」的事都挂在这颗心跳上 —— 控件按空闲钟收起、光标自动隐藏、
/// 双击全屏那一下的纯净闸解锁、拖动窗口时跟手的重报、跳过的倒计时。它停掉的时候，屏上只剩一张静止的
/// 画面：点画面不暂停、移鼠标不出控件，而进程、窗口和播放器都还活着（2026-10-22 用户报的正是这个，
/// 从双击退出全屏那一拍起，日志里再没有一行播放页的字）。
/// </para>
/// <para>
/// 这条判据存在的唯一理由是让那种状态<b>说得出话</b>：看门狗住在别的线程上，心跳停了它还在跑，于是
/// 它能写下「停在第几毫秒、当时是什么状态」，让下一次偶发有账可查；顺手再把计时器重挂一次，能自愈就
/// 自愈。
/// </para>
/// <para>
/// 三千毫秒是三十拍。取这么宽是因为心跳本来就允许偶尔掉几拍：拖动窗口、GPU 着色器重编译、一次强制
/// 布局都可能占住 UI 线程一两百毫秒，而那是正常的；真正停掉的时候没有人会在一秒之内把它救回来，所以
/// 晚报三秒不损失什么，早报三秒则会让日志里全是误报。
/// </para>
/// </summary>
public static class TickWatch
{
    /// <summary>多久没心跳算停：三十拍。</summary>
    public const long StalledMilliseconds = 3000;

    /// <summary>
    /// 此刻是不是该报警。<paramref name="watching"/> 为假（页面没挂上、或不在台上）时一律不报 ——
    /// 那时候心跳本来就该停着。<paramref name="lastTickAt"/> 为 0 表示还没立过参照点，也不算停。
    /// </summary>
    public static bool Stalled(long now, long lastTickAt, bool watching, long limit = StalledMilliseconds) =>
        watching && lastTickAt > 0 && now - lastTickAt >= limit;
}
