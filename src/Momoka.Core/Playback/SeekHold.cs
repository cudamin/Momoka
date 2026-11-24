namespace Momoka.Playback;

/// <summary>
/// 长按 ← / → 的判据与算术（2026-11-24 用户令「在快退和快进下方新增，快进跨度（倍），长按箭头以这个
/// 倍速播放」）。这里只有纯函数，怎么按、怎么轮询、怎么把命令发出去都在播放页的视图模型里。
/// <para>
/// 语义按用户在问答里选的那一档：<b>单击仍跳 <c>SeekForwardSeconds</c></b>；<b>按住约半秒后</b>，
/// → 改为按 <c>SeekForwardHoldMultiplier</c> 播放、松手回到按下之前的速度，← 不用改播放速度，
/// 只按 <c>SeekBackwardHoldMultiplier</c> 把进度往回推（2026-11-24 随后「快退也要」，两格各自保存）。
/// 两条管线（集成模式与内置独占窗口）共用这一套判据，差别只在「按键状态从哪里读」。
/// </para>
/// </summary>
public static class SeekHold
{
    public const string Forward = "seek-forward";

    public const string Backward = "seek-backward";

    /// <summary>按住多久才算长按。用户答的是「约半秒」；短于这个的按下只跳一次秒数。</summary>
    public const int HoldMilliseconds = 500;

    /// <summary>按键状态的轮询间隔。50ms 够跟手，又不会在空闲时吵醒线程；判定点因此有 ±50ms 的抖动。</summary>
    public const int PollMilliseconds = 50;

    /// <summary>连续快退每一步的间隔。200ms 一步、每步按倍率走 0.2 秒，5 步／秒时解码压力还能接受。</summary>
    public const int RewindStepMilliseconds = 200;

    /// <summary>只有这两颗方向键的短跳动作吃长按；大步、精细、逐帧都不吃。</summary>
    public static bool IsSeek(string? action) => action is Forward or Backward;

    /// <summary>
    /// 连续快退每一步应该往回跳多少秒：倍率 × 步长。2 倍 → 0.4 秒／步，10 倍 → 2 秒／步，
    /// 正好是「进度往回走的速率是常速播放的倍率」。
    /// </summary>
    public static double RewindStepSeconds(int multiplier, int stepMilliseconds = RewindStepMilliseconds) =>
        multiplier * stepMilliseconds / 1000.0;

    /// <summary>这一按是否已经越过长按判定时间。</summary>
    public static bool IsHeld(long pressedAt, long now, int holdMilliseconds = HoldMilliseconds) =>
        now - pressedAt >= holdMilliseconds;
}
