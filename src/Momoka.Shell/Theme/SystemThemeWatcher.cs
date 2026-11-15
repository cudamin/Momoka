using Microsoft.UI.Dispatching;
using Momoka.Diagnostics;
using Windows.UI.ViewManagement;

namespace Momoka.Shell;

/// <summary>只在系统模式存活。Windows 通知可以来自后台线程，读色和换肤统一回到 UI 队列。</summary>
internal sealed class SystemThemeWatcher : IDisposable
{
    private const string Category = "主题";
    private readonly UISettings _settings = new();
    private readonly DispatcherQueue _queue;
    private readonly Action<bool> _changed;
    private bool _observed;
    private bool _lastDark = true;
    private int _queued;
    private int _stopped;

    internal SystemThemeWatcher(Action<bool> changed)
    {
        _changed = changed;
        _queue = DispatcherQueue.GetForCurrentThread();
        try
        {
            _settings.ColorValuesChanged += OnColorsChanged;
            _observed = true;
        }
        catch (Exception error) { Log.Warn(Category, "订阅系统主题失败，保留当前配色", error); }
    }

    internal bool IsDark
    {
        get
        {
            try
            {
                // Microsoft Learn apply-windows-themes：亮前景表示深色应用模式。
                // Gallery v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7
                // WinUIGallery/Helpers/ThemeHelper.cs 将偏好与 ActualTheme 分开；
                // Momoka 应用级 RequestedTheme 固定 Dark，因此从 UISettings 读真实模式，再给根设置显式深浅。
                var color = _settings.GetColorValue(UIColorType.Foreground);
                _lastDark = 5 * color.G + 2 * color.R + color.B > 8 * 128;
            }
            catch (Exception error) { Log.Warn(Category, "读取系统主题失败，保留当前配色", error); }
            return _lastDark;
        }
    }

    private void OnColorsChanged(UISettings sender, object args)
    {
        if (Volatile.Read(ref _stopped) != 0 || Interlocked.Exchange(ref _queued, 1) != 0) return;
        try
        {
            if (!_queue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _queued, 0);
                if (Volatile.Read(ref _stopped) == 0) _changed(IsDark);
            }))
                Interlocked.Exchange(ref _queued, 0);
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref _queued, 0);
            Log.Warn(Category, "排队系统主题更新失败，等待下一次通知", error);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        if (_observed)
        {
            try { _settings.ColorValuesChanged -= OnColorsChanged; }
            catch (Exception error) { Log.Warn(Category, "退订系统主题失败，已停止回调", error); }
            _observed = false;
        }
    }
}
