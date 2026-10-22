using Momoka.Diagnostics;
using Momoka.Playback;

namespace Momoka.Shell.Views;

/// <summary>
/// 播放页心跳的看门狗：住在<b>另一条线程</b>上，专门盯那件自己报不出来的事 —— 十赫兹心跳停了。
/// <para>
/// 为什么非得是另一条线程：心跳停掉之后，页面里所有「过一会儿自己会跑」的东西一起哑了（计时器、指针
/// 轮询、控件显隐），它自己没有任何机会说一句「我停了」。2026-10-22 那晚就是这样：22:36:36 之后日志里
/// 再没有一行播放页的字，用户点画面、移鼠标全无反应，屏上只剩一张静止的画面，而进程、窗口和播放器都
/// 还活着 —— 事后只能从「少了哪一行」倒推，倒不出是哪一拍断的。
/// </para>
/// <para>
/// 判据在 Core（<see cref="TickWatch"/>），这里只负责三件事：每秒问一次、写一行带状态的账、把计时器
/// 重挂一次。重挂那一步排在 UI 线程上 —— 它要是排不进去，本身就是结论（队列都不接了）。
/// </para>
/// </summary>
public sealed partial class PlayerPage
{
    /// <summary>看门狗的节拍。一秒一次：判据是三秒，够密也够便宜。</summary>
    private const int WatchdogMilliseconds = 1000;

    private Timer? _watchdog;

    /// <summary>最近一拍的时刻（<see cref="Now"/> 的读数）。心跳线程写、看门狗线程读。</summary>
    private long _lastTickAt;

    /// <summary>这一场停跳报过没有：一场只报一次，恢复了才归零。</summary>
    private int _stallReported;

    private void StartWatchdog() =>
        _watchdog ??= new Timer(_ => WatchTick(), null, WatchdogMilliseconds, WatchdogMilliseconds);

    private void StopWatchdog()
    {
        var watchdog = _watchdog;
        _watchdog = null;
        watchdog?.Dispose();
    }

    /// <summary>心跳又跳了一拍。由 <c>OnTick</c> 在开头调用。</summary>
    private void NoteTick() => Interlocked.Exchange(ref _lastTickAt, Now);

    private void WatchTick()
    {
        if (_watchdog is null) return;

        var now = Now;
        var last = Interlocked.Read(ref _lastTickAt);

        // 该跳的时候没跳才算停：页面没挂上、或者不在台上，心跳本来就该停着。
        if (!TickWatch.Stalled(now, last, Attached && _onStage))
        {
            if (Interlocked.Exchange(ref _stallReported, 0) == 1)
                Log.Info(LogCategory, $"心跳恢复：停了 {now - last}ms 之后又跳了");
            return;
        }

        // 一场停跳只报一行：看门狗每秒都会走到这儿，而这一行要的是「第一次发现」的那一刻。
        if (Interlocked.Exchange(ref _stallReported, 1) == 1) return;

        Log.Warn(LogCategory, $"心跳停了：{now - last}ms 没有一拍（"
            + $"挂上={Attached}、在台={_onStage}、纯净闸={_chrome.Silenced}、光标藏={_cursorHidden}、"
            + $"指针部件={_pointerOn}、暂停={ViewModel.Paused}、指针={(_cursorAtKnown ? $"{_cursorAt.X},{_cursorAt.Y}" : "读不到")}）");

        // 重挂一次：计时器「还在跑」和「还在兑现」是两件事，两种都试。排在 UI 线程上，排不进去就是
        // 结论本身 —— 那时候连队列都不接了，日志里这一行是唯一还能留下的证据。
        var queued = DispatcherQueue.TryEnqueue(() =>
        {
            if (_watchdog is null) return;

            var was = _ticker.IsEnabled;
            _ticker.Stop();
            NoteTick();
            _ticker.Start();
            Log.Warn(LogCategory, $"心跳停了：已重挂 10Hz 计时器（原来{(was ? "在跑" : "是停的")}）");
        });

        if (!queued) Log.Warn(LogCategory, "心跳停了：重挂请求排不进 UI 线程，队列不接活了");
    }
}
