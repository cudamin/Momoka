namespace Windows.Foundation
{
    public delegate void TypedEventHandler<in TSender, in TArgs>(TSender sender, TArgs args);
}

namespace Microsoft.UI.Xaml.Input
{
    public delegate void PointerEventHandler(object sender, PointerRoutedEventArgs args);
    public delegate void KeyEventHandler(object sender, KeyRoutedEventArgs args);
    public sealed class PointerRoutedEventArgs;
    public sealed class KeyRoutedEventArgs(Windows.System.VirtualKey key) { public Windows.System.VirtualKey Key { get; } = key; }
}

namespace Windows.System
{
    public enum VirtualKey { Tab, Up, Down, Left, Right, Home, End, PageUp, PageDown, A }
}

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public DispatcherQueueTimer CreateTimer() => new();
    }
    public sealed class DispatcherQueueTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object>? Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire() { if (!IsRunning) return; IsRunning = false; Tick?.Invoke(this, new()); }
    }
}
