using Momoka.Diagnostics;

namespace Momoka.Shell.Views;

/// <summary>有限的匿名结果，不携带账号、媒体名称、地址或图片标识。</summary>
internal enum ConnectedMotionReason
{
    None,
    Disabled,
    SourceUnavailable,
    TargetUnavailable,
    IdentityChanged,
    ArtworkChanged,
    PrepareFailed,
    StartRejected,
    PrepareTimeout,
    CompletionTimeout,
    NavigationCancelled,
    Superseded
}

/// <summary>成功计时止于 TryStart(true) 返回；拒绝计时止于尝试入口，无尝试时为 null。</summary>
internal sealed record CardDetailTransitionReceipt(
    string Direction, bool Started, bool Completed, bool TimedOut, ConnectedMotionReason Reason,
    bool Terminal, double? PrepareToStartMs, double ElapsedMs);

/// <summary>Started 和唯一 Terminal 分开发布，Completed 不由 Started 推断。</summary>
internal sealed record PersonTransitionReceipt(
    bool Started, bool Completed, bool TimedOut, ConnectedMotionReason Reason,
    bool Terminal, double? PrepareToStartMs, double ElapsedMs);

internal readonly record struct ConnectedMotionState(
    bool Started, bool Completed, bool TimedOut, ConnectedMotionReason Reason,
    bool Terminal, double? PrepareToStartMs, double ElapsedMs);

/// <summary>
/// 单次原生 Prepare 的计时与结算；不管理动画、不排定时器。返回准备使用新实例，
/// 所以它不会继承前进的时间戳或终态。TimeProvider 只替换单调时钟供离线边界验证。
/// </summary>
internal sealed class ConnectedMotionLifecycle(string surface, string direction, TimeProvider clock, long preparedTimestamp)
{
    private readonly NavigationTimingRun? _timing = NavigationTiming.Capture();
    private readonly string _prefix = surface + "-" + direction;
    private bool _startReported;
    private bool _terminal;
    private long? _startTimestamp;

    internal void Prepared() => _timing?.Mark(_prefix + "-prepared");

    internal void StartAttempt() => _startTimestamp ??= clock.GetTimestamp();

    // 成功回执使用 TryStart(true) 返回的时点；拒绝回执保留该次尝试开始的时点。
    internal void StartSucceeded() => _startTimestamp = clock.GetTimestamp();

    internal ConnectedMotionState? Report(bool started, bool completed, ConnectedMotionReason reason, bool terminal)
    {
        if (_terminal || !terminal && _startReported) return null;
        _startReported |= started;
        _terminal = terminal;
        var elapsed = Math.Max(0, clock.GetElapsedTime(preparedTimestamp).TotalMilliseconds);
        var startElapsed = _startTimestamp is { } stamp
            ? Math.Max(0, clock.GetElapsedTime(preparedTimestamp, stamp).TotalMilliseconds) : (double?)null;
        _timing?.Mark(_prefix + "-" + (completed ? "completed" : reason == ConnectedMotionReason.None ? "started" : ReasonStage(reason)));
        return new(started, completed, reason is ConnectedMotionReason.PrepareTimeout or ConnectedMotionReason.CompletionTimeout,
            reason, terminal, startElapsed, elapsed);
    }

    internal static void PrepareRejected(string surface, string direction, ConnectedMotionReason reason)
    {
        if (NavigationTiming.Enabled) NavigationTiming.Mark(surface + "-" + direction + "-" + ReasonStage(reason));
    }

    private static string ReasonStage(ConnectedMotionReason reason) => reason switch
    {
        ConnectedMotionReason.Disabled => "disabled",
        ConnectedMotionReason.SourceUnavailable => "source-unavailable",
        ConnectedMotionReason.TargetUnavailable => "target-unavailable",
        ConnectedMotionReason.IdentityChanged => "identity-changed",
        ConnectedMotionReason.ArtworkChanged => "artwork-changed",
        ConnectedMotionReason.PrepareFailed => "prepare-failed",
        ConnectedMotionReason.StartRejected => "start-rejected",
        ConnectedMotionReason.PrepareTimeout => "prepare-timeout",
        ConnectedMotionReason.CompletionTimeout => "completion-timeout",
        ConnectedMotionReason.NavigationCancelled => "navigation-cancelled",
        ConnectedMotionReason.Superseded => "superseded",
        _ => "none"
    };
}
