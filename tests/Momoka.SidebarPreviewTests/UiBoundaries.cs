namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
    public readonly record struct Size(double Width, double Height);
    public readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public bool Contains(Point point) => point.X >= X && point.Y >= Y && point.X < X + Width && point.Y < Y + Height;
    }
}

namespace Microsoft.UI.Input
{
    public enum PointerDeviceType { Mouse, Touch, Pen }
}

namespace Microsoft.UI
{
    public static class Win32Interop
    {
        public static IntPtr GetWindowFromWindowId(object id) => new(1);
    }
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
        public int Starts { get; private set; }
        public event Action<DispatcherQueueTimer, object>? Tick;
        public void Start() { IsRunning = true; Starts++; }
        public void Stop() => IsRunning = false;
        internal void Fire() => Tick?.Invoke(this, new object());
    }
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum TextTrimming { CharacterEllipsis }
    public enum TextWrapping { Wrap }
    public enum HorizontalAlignment { Left, Center, Stretch }
    public enum VerticalAlignment { Top, Center, Stretch }
    public readonly record struct CornerRadius(double Value);
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }
    }
    public sealed class Style;
    public sealed class RoutedEventArgs : EventArgs;
    public class UIElement
    {
        public static object PointerPressedEvent { get; } = new();
        public static object PointerMovedEvent { get; } = new();
        // 生产代码只负责接线；真实事件的触发由夹具的内部方法直接调处理函数，这里只把订阅记下来。
        private readonly List<Input.PointerEventHandler> _entered = [];
        private readonly List<Input.PointerEventHandler> _exited = [];
        public event Input.PointerEventHandler PointerEntered
        {
            add => _entered.Add(value);
            remove => _entered.Remove(value);
        }
        public event Input.PointerEventHandler PointerExited
        {
            add => _exited.Add(value);
            remove => _exited.Remove(value);
        }
        private readonly List<Input.PointerEventHandler> _pointerPressed = [];
        public void AddHandler(object routedEvent, Input.PointerEventHandler handler, bool handledEventsToo) => _pointerPressed.Add(handler);
        internal void Press(Input.PointerRoutedEventArgs args)
        {
            foreach (var handler in _pointerPressed) handler(this, args);
        }
        public Visibility Visibility { get; set; } = Visibility.Visible;
        public double Opacity { get; set; } = 1;
        public object? RenderTransform { get; set; }
        public string? AutomationName { get; set; }
        public string? AutomationId { get; set; }
        public Automation.Peers.AccessibilityView AccessibilityView { get; set; } = Automation.Peers.AccessibilityView.Control;
        public bool IsHitTestVisible { get; set; } = true;
    }
    public class FrameworkElement : UIElement
    {
        public object? Tag { get; set; }
        public double Width { get; set; } = 100;
        public double Height { get; set; } = 150;
        public double ActualWidth { get; set; } = 100;
        public double ActualHeight { get; set; } = 150;
        public double MaxHeight { get; set; } = double.PositiveInfinity;
        public XamlRoot? XamlRoot { get; set; } = new();
        public Thickness Margin { get; set; }
        public Thickness Padding { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public Style? Style { get; set; }
        public Dictionary<string, object> Resources { get; } = [];
        public Windows.Foundation.Point Origin { get; set; }
        public Windows.Foundation.Size DesiredSize { get; private set; } = new(360, 232);
        // WinUI 的 DesiredSize 含 Margin（WPF 不含）：摆位代码依赖这条语义，边界也要照它建模，
        // 否则「上一次的 top 被算进这次量到的高度」这一类回归在离线夹具里看不出来。
        public void Measure(Windows.Foundation.Size size) => DesiredSize = new(
            size.Width + Margin.Left + Margin.Right, 232 + Margin.Top + Margin.Bottom);
        public Media.GeneralTransform TransformToVisual(UIElement target) => new(new Windows.Foundation.Point(Origin.X + Margin.Left, Origin.Y + Margin.Top));
    }
    public sealed class XamlRoot
    {
        public UIElement Content { get; set; } = new();
        public double RasterizationScale { get; set; } = 1;
        public ContentIslandEnvironment ContentIslandEnvironment { get; } = new();
    }
    public sealed class ContentIslandEnvironment
    {
        public object AppWindowId { get; } = new();
    }
    public sealed class Application
    {
        public static Application Current { get; } = new();
        public Dictionary<string, object> Resources { get; } = new()
        {
            ["EgTitleFontSize"] = 20d,
            ["EgTextFaintBrush"] = new Media.Brush(),
            ["EgTextBrush"] = new Media.Brush(),
            ["EgCaptionStyle"] = new Style(),
            ["EgPosterCornerRadius"] = new CornerRadius(10),
            ["EgSurfaceAltBrush"] = new Media.Brush(),
            ["EgFrameStyle"] = new Style(),
            ["EgPosterButtonStyle"] = new Style(),
            ["EgArtworkTileStyle"] = new Style(),
            ["EgTextOnAccentBrush"] = new Media.Brush()
        };
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class StackPanel : FrameworkElement
    {
        public List<UIElement> Children { get; } = [];
        public double Spacing { get; set; }
    }
    public sealed class NavigationViewItem : FrameworkElement
    {
        public object? Content { get; set; }
    }
    public sealed class NavigationView : FrameworkElement
    {
        public bool IsPaneOpen { get; set; }
        public double CompactPaneLength { get; set; } = 56;
    }
    public sealed class Border : FrameworkElement
    {
        public UIElement? Child { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Media.Brush? Background { get; set; }
    }
    public sealed class Grid : FrameworkElement
    {
        public List<UIElement> Children { get; } = [];
        public static void SetColumn(UIElement element, int column) { }
    }
    public sealed class Image : FrameworkElement
    {
        public object? Source { get; set; }
        public Media.Stretch Stretch { get; set; }
    }
    public sealed class FontIcon : FrameworkElement
    {
        public string? Glyph { get; set; }
        public double FontSize { get; set; }
        public Media.Brush? Foreground { get; set; }
    }
    public sealed class Button : FrameworkElement
    {
        public object? Content { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Media.Brush? Background { get; set; }
        public Media.Brush? BorderBrush { get; set; }
        public Thickness BorderThickness { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool InvokeAvailable { get; set; } = true;
        public event Action<object, RoutedEventArgs>? Click;
        internal void Invoke() => Click?.Invoke(this, new());
    }
    public sealed class TextBlock : FrameworkElement
    {
        public string Text { get; set; } = "";
        public int FontWeight { get; set; }
        public Media.Brush? Foreground { get; set; }
        public int MaxLines { get; set; }
        public TextTrimming TextTrimming { get; set; }
        public TextWrapping TextWrapping { get; set; }
    }
    public sealed class ProgressRing : FrameworkElement
    {
        public bool IsActive { get; set; }
    }
    public static class ToolTipService
    {
        public static void SetToolTip(FrameworkElement element, object tooltip) { }
    }
    public sealed class ToolTip
    {
        public Media.Brush? Background { get; set; }
        public Thickness BorderThickness { get; set; }
        public Thickness Padding { get; set; }
    }
}

namespace Microsoft.UI.Text
{
    public static class FontWeights
    {
        public static int SemiBold => 600;
    }
}

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        public static void SetName(UIElement element, string name) => element.AutomationName = name;
        public static string GetName(UIElement element) => element.AutomationName ?? "";
        public static void SetAutomationId(UIElement element, string id) => element.AutomationId = id;
        public static string GetAutomationId(UIElement element) => element.AutomationId ?? "";
        public static void SetAccessibilityView(UIElement element, Peers.AccessibilityView view) => element.AccessibilityView = view;
    }
}

namespace Microsoft.UI.Xaml.Automation.Peers
{
    public enum AccessibilityView { Raw, Control }
    public enum PatternInterface { Invoke }
    public sealed class ButtonAutomationPeer(Controls.Button button)
    {
        public object? GetPattern(PatternInterface pattern) => button.InvokeAvailable ? new InvokeBoundary() : null;
        private sealed class InvokeBoundary : Provider.IInvokeProvider;
    }
}

namespace Microsoft.UI.Xaml.Automation.Provider
{
    public interface IInvokeProvider;
}

namespace Microsoft.UI.Xaml.Input
{
    public delegate void PointerEventHandler(object sender, PointerRoutedEventArgs args);
    public sealed class Pointer
    {
        public Microsoft.UI.Input.PointerDeviceType PointerDeviceType { get; set; } = Microsoft.UI.Input.PointerDeviceType.Mouse;
    }
    public sealed class PointerProperties
    {
        public bool IsLeftButtonPressed { get; set; }
        public bool IsRightButtonPressed { get; set; }
        public bool IsMiddleButtonPressed { get; set; }
    }
    public sealed class PointerPoint
    {
        public Windows.Foundation.Point Position { get; set; }
        public PointerProperties Properties { get; } = new();
    }
    public sealed class PointerRoutedEventArgs : EventArgs
    {
        public Pointer Pointer { get; } = new();
        public PointerPoint Point { get; } = new();
        public bool Handled { get; set; }
        public PointerPoint GetCurrentPoint(UIElement element) => Point;
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public class Brush;
    public enum Stretch { UniformToFill }
    public sealed class ScaleTransform
    {
        public double ScaleX { get; set; } = 1;
        public double ScaleY { get; set; } = 1;
    }
    public sealed class GeneralTransform(Windows.Foundation.Point origin)
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point point) => new(point.X + origin.X, point.Y + origin.Y);
        public Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect rect) => new(rect.X + origin.X, rect.Y + origin.Y, rect.Width, rect.Height);
    }
}

namespace Microsoft.UI.Xaml.Media.Animation
{
    public enum EasingMode { EaseOut }
    public sealed class CubicEase { public EasingMode EasingMode { get; set; } }
    public sealed class DoubleAnimation
    {
        public double From { get; set; }
        public double To { get; set; }
        public TimeSpan Duration { get; set; }
        public CubicEase? EasingFunction { get; set; }
    }
    public sealed class Storyboard
    {
        public List<DoubleAnimation> Children { get; } = [];
        public void Stop() { }
        public void Begin() { }
        public static void SetTarget(DoubleAnimation animation, object target) { }
        public static void SetTargetProperty(DoubleAnimation animation, string property) { }
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceProviderExtensions
    {
        public static T? GetService<T>(this IServiceProvider? provider) where T : class => provider?.GetService(typeof(T)) as T;
    }
}

namespace Momoka.Shell.Interop
{
    public struct NativePoint { public int X; public int Y; }
    public static class Native
    {
        internal static NativePoint PointerPosition;
        internal static bool PointerAvailable = true;
        public static bool GetCursorPos(out NativePoint point) { point = PointerPosition; return PointerAvailable; }
        public static bool ScreenToClient(IntPtr window, ref NativePoint point) => window != IntPtr.Zero;
        public static IntPtr WindowFromPoint(NativePoint point) => new(1);
        public static bool IsChild(IntPtr parent, IntPtr child) => true;
    }
}
