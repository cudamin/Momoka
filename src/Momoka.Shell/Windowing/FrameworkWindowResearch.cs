using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.Windowing;

/// <summary>
/// P7 隔离研究窗口。只供匿名探针比较 Window/AppWindow 生命周期，不替代 HostWindow 或承诺视频兼容。
/// 点击关闭先隐藏以验证缓存重开；研究收尾才真正关闭并解除事件。
/// </summary>
internal sealed class FrameworkWindowResearch : IDisposable
{
    private readonly Window _window;
    private bool _closing;

    internal FrameworkWindowResearch(Window window)
    {
        _window = window;
        window.AppWindow.Closing += OnClosing;
        window.Closed += OnClosed;
    }

    internal bool IsOpen { get; private set; }
    internal bool IsClosed { get; private set; }
    internal bool ContentReleased { get; private set; }
    internal Window Window => _window;
    internal event Action? Hidden;

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        _window.Activate();
        IsOpen = true;
    }

    internal void Hide()
    {
        if (!IsOpen || IsClosed) return;
        _window.AppWindow.Hide();
        IsOpen = false;
        Hidden?.Invoke();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing) return;
        args.Cancel = true;
        Hide();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        IsOpen = false;
        IsClosed = true;
        _window.AppWindow.Closing -= OnClosing;
        _window.Closed -= OnClosed;
        _window.Content = null;
        ContentReleased = true;
        Hidden = null;
    }

    public void Dispose()
    {
        if (IsClosed || _closing) return;
        _closing = true;
        try { _window.Close(); }
        catch
        {
            _closing = false;
            throw;
        }
    }
}
