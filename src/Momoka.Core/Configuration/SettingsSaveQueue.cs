namespace Momoka.Configuration;

/// <summary>
/// 仅供设置滑块：一个定时器、一个捕获请求、一个写入，200ms 静默合并且最长等待 1s。
/// 捕获通过拥有线程的队列执行；后台写入只拿独立快照。失败停止自动重试，保留 Store 欠项。
/// </summary>
internal sealed class SettingsSaveQueue : IDisposable
{
    internal const int QuietPeriodMs = 200;
    internal const int MaximumDelayMs = 1000;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly Func<Action, bool> _post;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private long _firstChange = -1;
    private long _lastChange;
    private bool _capturePosted;
    private bool _writing;
    private bool _disposed;
    private TaskCompletionSource<bool>? _flush;

    internal SettingsSaveQueue(SettingsStore store, AppSettings settings, Func<Action, bool> post)
    {
        _store = store;
        _settings = settings;
        _post = post;
        _timer = new Timer(_ => PostCapture(), null, Timeout.Infinite, Timeout.Infinite);
    }

    internal void RequestSave()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SettingsSaveQueue));
            _store.RegisterDeferredChange();
            var now = Environment.TickCount64;
            if (_firstChange < 0) _firstChange = now;
            _lastChange = now;
            Schedule();
        }
    }

    internal Task<bool> FlushAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(false);
            if (_store.PendingVersion == 0 && !_writing && !_capturePosted)
                return Task.FromResult(!_store.HasUnsavedChanges);
            _flush ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            Schedule();
            return _flush.Task;
        }
    }

    private void Schedule()
    {
        if (_disposed || _writing || _capturePosted) return;
        var now = Environment.TickCount64;
        var delay = _flush is not null ? 0 : _firstChange < 0 ? QuietPeriodMs
            : (int)Math.Max(0, Math.Min(QuietPeriodMs - (now - _lastChange), MaximumDelayMs - (now - _firstChange)));
        _timer.Change(delay, Timeout.Infinite);
    }

    private void PostCapture()
    {
        long version;
        lock (_gate)
        {
            if (_disposed || _writing || _capturePosted) return;
            // 已在途的旧 timer 回调可能与新编辑交错；重新核对静默/最长等待，不提前写每一步。
            var now = Environment.TickCount64;
            if (_flush is null && _firstChange >= 0
                && now - _lastChange < QuietPeriodMs && now - _firstChange < MaximumDelayMs)
            {
                Schedule();
                return;
            }
            _capturePosted = true;
            version = _store.PendingVersion;
        }
        try
        {
            if (!_post(CaptureAndWrite)) throw new InvalidOperationException("界面队列已关闭，设置仍未保存");
        }
        catch (Exception error)
        {
            _store.ReportPendingSaveFailure(error);
            Finish(success: false, version);
        }
    }

    private void CaptureAndWrite()
    {
        long version;
        lock (_gate)
        {
            if (_disposed) return;
            _capturePosted = false;
            _writing = true;
            _firstChange = -1;
            version = _store.PendingVersion;
        }
        try
        {
            var snapshot = _store.CapturePendingSave(_settings);
            if (snapshot is null) Finish(success: true, version);
            else _ = WriteAsync(snapshot);
        }
        catch
        {
            // CapturePendingSave 已报告真实异常，不能用成功覆盖它或清掉欠项。
            Finish(success: false, version);
        }
    }

    private async Task WriteAsync(SettingsSaveSnapshot snapshot)
    {
        var success = await Task.Run(() => _store.TrySaveSnapshot(snapshot)).ConfigureAwait(false);
        Finish(success, snapshot.Version);
    }

    private void Finish(bool success, long attemptedVersion)
    {
        lock (_gate)
        {
            _capturePosted = false;
            _writing = false;
            if (_disposed) return;
            // 失败通知可重入一笔更新的同步成功；旧尝试不能把已排空的新文档报红。
            var pendingVersion = _store.PendingVersion;
            var pending = pendingVersion != 0;
            if (!pending && !_store.HasUnsavedChanges) success = true;
            // 旧失败只停止该代次的自动重试；在途期间/失败通知里登记的新编辑仍须排空。
            if (pending && (success || pendingVersion > attemptedVersion))
            {
                Schedule();
                return;
            }
            _firstChange = -1;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            var completed = _flush;
            _flush = null;
            // RunContinuationsAsynchronously 允许在锁内完成；新的 Request 不会插入已排空的边界之前。
            completed?.TrySetResult(success && !_store.HasUnsavedChanges);
        }
    }

    public void Dispose()
    {
        TaskCompletionSource<bool>? completed;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            completed = _flush;
            _flush = null;
        }
        completed?.TrySetResult(false);
    }
}
