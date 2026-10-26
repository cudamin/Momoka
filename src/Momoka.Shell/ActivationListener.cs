using Momoka.Diagnostics;

namespace Momoka.Shell;

/// <summary>监听下一次启动；停止只发信号，工作线程退出后才释放自己正在使用的句柄。</summary>
internal static class ActivationListener
{
    public static IDisposable Start(string eventName, Func<Action, bool> enqueue, Action activate,
        Action<Thread>? startThread = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(enqueue);
        ArgumentNullException.ThrowIfNull(activate);
        var lifetime = new ListenerLifetime(eventName);
        var thread = new Thread(() =>
        {
            try
            {
                // 停止排第一位：两个信号同时就绪时，关闭优先于新激活。
                var handles = new WaitHandle[] { lifetime.Stopping, lifetime.Signal };
                while (WaitHandle.WaitAny(handles) == 1)
                {
                    if (lifetime.IsStopped) break;
                    if (!enqueue(() =>
                    {
                        // UI 队列中尚未执行的通知也不能在窗口关闭后重新激活它。
                        if (!lifetime.IsStopped) activate();
                    })) break;
                }
            }
            catch (Exception error)
            {
                // 专用 Thread 的异常不会进入 TaskScheduler；结束监听而不是打断应用收尾。
                Log.Warn("启动", $"激活监听器意外结束（{error.GetType().Name}）");
            }
            finally
            {
                lifetime.Release();
            }
        })
        {
            IsBackground = true,
            Name = "activation-listener"
        };

        try
        {
            // 调度入口供离线测试推迟首次线程执行；生产路径直接启动。
            if (startThread is null) thread.Start();
            else startThread(thread);
            return lifetime;
        }
        catch
        {
            lifetime.Release();
            throw;
        }
    }

    private sealed class ListenerLifetime : IDisposable
    {
        private readonly object _gate = new();
        private bool _stopped;
        private bool _released;

        internal ListenerLifetime(string eventName)
        {
            Signal = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            try { Stopping = new ManualResetEvent(false); }
            catch
            {
                Signal.Dispose();
                throw;
            }
        }

        internal EventWaitHandle Signal { get; }
        internal ManualResetEvent Stopping { get; }
        internal bool IsStopped { get { lock (_gate) return _stopped; } }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_stopped || _released) return;
                _stopped = true;
                Stopping.Set();
                // 不在 UI 线程 Join；延迟初始化和正在 WaitAny 的线程都仍握着有效句柄。
            }
        }

        internal void Release()
        {
            lock (_gate)
            {
                if (_released) return;
                _stopped = true;
                _released = true;
                Signal.Dispose();
                Stopping.Dispose();
            }
        }
    }
}
