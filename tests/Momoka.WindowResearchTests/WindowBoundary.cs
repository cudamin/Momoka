namespace Microsoft.UI.Windowing
{
    public delegate void AppWindowClosingHandler(AppWindow sender, AppWindowClosingEventArgs args);
    public sealed class AppWindowClosingEventArgs : EventArgs
    {
        public bool Cancel { get; set; }
    }
    public sealed class AppWindow
    {
        private AppWindowClosingHandler? _closing;
        internal bool Visible { get; set; }
        internal bool ThrowOnHide { get; set; }
        internal int HideCalls { get; private set; }
        internal int ClosingSubscriptions => _closing?.GetInvocationList().Length ?? 0;
        public event AppWindowClosingHandler? Closing { add => _closing += value; remove => _closing -= value; }
        public void Hide()
        {
            if (ThrowOnHide) throw new InvalidOperationException("Injected Hide failure.");
            Visible = false;
            HideCalls++;
        }
        internal bool RequestClose()
        {
            var args = new AppWindowClosingEventArgs();
            _closing?.Invoke(this, args);
            return !args.Cancel;
        }
    }
}

namespace Microsoft.UI.Xaml
{
    public delegate void WindowClosedHandler(object sender, WindowEventArgs args);
    public sealed class WindowEventArgs : EventArgs;
    public sealed class Window
    {
        private WindowClosedHandler? _closed;
        private object? _content;
        private bool _closedDelivered;
        public Microsoft.UI.Windowing.AppWindow AppWindow { get; } = new();
        public object? Content
        {
            get => _closedDelivered ? throw new InvalidOperationException("Cannot read a closed Desktop Window.") : _content;
            set => _content = value;
        }
        internal int ActivateCalls { get; private set; }
        internal int CloseCalls { get; private set; }
        internal bool ThrowOnActivate { get; set; }
        internal bool ThrowOnClose { get; set; }
        internal bool DeferClosed { get; set; }
        internal int ClosedSubscriptions => _closed?.GetInvocationList().Length ?? 0;
        public event WindowClosedHandler? Closed { add => _closed += value; remove => _closed -= value; }
        public void Activate()
        {
            if (ThrowOnActivate) throw new InvalidOperationException("Injected activation failure.");
            AppWindow.Visible = true;
            ActivateCalls++;
        }
        public void Close()
        {
            if (ThrowOnClose) throw new InvalidOperationException("Injected close failure.");
            if (!AppWindow.RequestClose()) return;
            CloseCalls++;
            AppWindow.Visible = false;
            if (!DeferClosed) DeliverClosed();
        }
        internal void DeliverClosed()
        {
            _closed?.Invoke(this, new());
            _closedDelivered = true;
        }
    }
}
