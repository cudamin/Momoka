namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
}

namespace Microsoft.UI.Xaml
{
    public class UIElement;
    public class FrameworkElement : UIElement
    {
        public double Top { get; set; }
        public double ActualWidth { get; set; } = 600;
        public double ActualHeight { get; set; } = 20;
        public InfoTransform TransformToVisual(UIElement target) => new(Top - (target is Microsoft.UI.Xaml.Controls.ScrollView body ? body.VerticalOffset : 0));
    }
    public sealed class InfoPanelBoundary
    {
        public double Top { get; set; }
        public InfoTransform TransformToVisual(UIElement _) => new(Top);
    }
    public sealed class InfoTransform(double top)
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point _) => new(0, top);
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum ScrollingAnimationMode { Disabled }
    public sealed class ScrollingScrollOptions(ScrollingAnimationMode mode)
    {
        public ScrollingAnimationMode Mode { get; } = mode;
    }
    public sealed class ScrollView : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? Content { get; set; } = new Microsoft.UI.Xaml.UIElement();
        public double ScrollableHeight { get; set; }
        public double VerticalOffset { get; set; }
        public double ExtentHeight { get; set; }
        public double ViewportHeight { get; set; }
        public List<double> Requests { get; } = [];
        public int LayoutReads { get; private set; }
        public void UpdateLayout() => LayoutReads++;
        private int _correlation;
        public bool AutoComplete { get; set; } = true;
        public Action? BeforeScrollRequest { get; set; }
        public ScrollingInteractionState State { get; set; }
        public event Windows.Foundation.TypedEventHandler<ScrollView, object>? ViewChanged;
        public event Windows.Foundation.TypedEventHandler<ScrollView, object>? ExtentChanged;
        public event Windows.Foundation.TypedEventHandler<ScrollView, object>? StateChanged;
        public event Windows.Foundation.TypedEventHandler<ScrollView, ScrollingScrollCompletedEventArgs>? ScrollCompleted;
        private readonly Dictionary<string, List<Delegate>> _handlers = [];
        public int SubscriptionCount => (ViewChanged?.GetInvocationList().Length ?? 0) + (ExtentChanged?.GetInvocationList().Length ?? 0)
            + (StateChanged?.GetInvocationList().Length ?? 0) + (ScrollCompleted?.GetInvocationList().Length ?? 0)
            + _handlers.Values.Sum(list => list.Count);
        public int ScrollTo(double x, double y, ScrollingScrollOptions _)
        {
            BeforeScrollRequest?.Invoke();
            Requests.Add(y);
            var correlation = ++_correlation;
            if (AutoComplete) VerticalOffset = Math.Clamp(y, 0, ScrollableHeight);
            return correlation;
        }
        public void ChangeExtent(double max) { ScrollableHeight = max; ExtentChanged?.Invoke(this, new()); }
        public void ChangeOffset(double offset) { VerticalOffset = offset; ViewChanged?.Invoke(this, new()); }
        public void Complete(int correlation) { VerticalOffset = Math.Clamp(Requests[correlation - 1], 0, ScrollableHeight); ScrollCompleted?.Invoke(this, new(correlation)); }
        public void SignalCompleted(int correlation) => ScrollCompleted?.Invoke(this, new(correlation));
        public void Interact() { State = ScrollingInteractionState.Interaction; StateChanged?.Invoke(this, new()); }
        public void AddHandler(string signal, Delegate handler, bool handled)
        {
            if (!_handlers.TryGetValue(signal, out var list)) _handlers[signal] = list = [];
            list.Add(handler);
        }
        public void RemoveHandler(string signal, Delegate handler)
        {
            if (_handlers.TryGetValue(signal, out var list)) list.Remove(handler);
        }
        public void UserSignal(string signal, object args)
        {
            if (_handlers.TryGetValue(signal, out var list)) foreach (var handler in list.ToArray()) handler.DynamicInvoke(this, args);
        }
    }
    public enum ScrollingInteractionState { Idle, Interaction }
    public sealed class ScrollingScrollCompletedEventArgs(int correlation) { public int CorrelationId { get; } = correlation; }
    public sealed class ItemsRepeater : Microsoft.UI.Xaml.FrameworkElement
    {
        public Dictionary<int, Microsoft.UI.Xaml.UIElement> Rows { get; } = [];
        public Microsoft.UI.Xaml.UIElement? TryGetElement(int index) => Rows.GetValueOrDefault(index);
    }
}

namespace Momoka.Shell.Views
{
    internal sealed partial class DetailPage
    {
        internal Microsoft.UI.Xaml.Controls.ScrollView Body { get; } = new();
        internal Microsoft.UI.Xaml.InfoPanelBoundary InfoPanel { get; } = new();
        internal ReturnLoadBoundary ViewModel { get; } = new();
        internal Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        private const string PointerWheelChangedEvent = "wheel";
        private const string PointerPressedEvent = "pointer";
        private const string KeyDownEvent = "key";
        internal void EndRevealLayoutForFixture() { _revealArmedAt = Environment.TickCount64 - 601; _revealStopTimer!.Fire(); }
        internal void StopRevealForFixture() => StopScrollReveal();
        internal void SetRevealAgeForFixture(long age) => _revealArmedAt = Environment.TickCount64 - age;
        internal Dictionary<string, object> GeometryNames { get; } = [];
        public object? FindName(string name) => name == "Body" ? Body : GeometryNames.GetValueOrDefault(name);
    }
    internal sealed class ReturnLoadBoundary : System.ComponentModel.INotifyPropertyChanged
    {
        public bool Busy { get; set; }
        public bool IsReady { get; set; } = true;
        public List<int> InfoRows { get; } = [];
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        internal void Finish() { Busy = false; IsReady = true; PropertyChanged?.Invoke(this, new(nameof(IsReady))); }
    }
}
