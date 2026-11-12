using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Theming;
using Momoka.Shell;
using Windows.UI;

namespace Momoka.Tests
{
    // Endpoint fault injection only. No production connect/disconnect decisions are implemented here.
    internal static class MaterialFaults
    {
        private static readonly Dictionary<string, int> Failures = [];
        internal static readonly List<ControllerBoundary> Controllers = [];
        internal static readonly List<Windows.UI.Composition.CompositionColorBrush> Brushes = [];
        internal static readonly List<string> Trace = [];
        internal static bool MicaSupported = true;
        internal static bool AcrylicSupported = true;
        internal static bool MicaAddResult = true;
        internal static bool AcrylicAddResult = true;
        internal static SystemBackdropState NextState = SystemBackdropState.Active;

        internal static void FailOnce(string point) => Failures[point] = Failures.GetValueOrDefault(point) + 1;
        internal static void ClearFailures() => Failures.Clear();
        internal static void Hit(string point)
        {
            Trace.Add(point);
            if (Failures.GetValueOrDefault(point) == 0) return;
            if (--Failures[point] == 0) Failures.Remove(point);
            throw new InjectedFailure(point);
        }
        internal static void Reset()
        {
            Failures.Clear(); Controllers.Clear(); Brushes.Clear(); Trace.Clear();
            MicaSupported = AcrylicSupported = MicaAddResult = AcrylicAddResult = true;
            NextState = SystemBackdropState.Active;
            Windows.UI.ViewManagement.UISettings.Background = Color.FromArgb(255, 9, 10, 11);
            ThemeHost.ResetForTest(); Windows.UI.ViewManagement.AccessibilitySettings.Contrast = false; Momoka.Diagnostics.Log.Lines.Clear();
        }
    }
    internal sealed class InjectedFailure(string point) : Exception(point);

    // A target owns the legacy Windows compositor brush, exactly as the real target interface does.
    // Do not make MUX brushes inherit the legacy type: that would hide an invalid production API mix.
    internal sealed class MaterialTarget : ICompositionSupportsSystemBackdrop
    {
        private Windows.UI.Composition.CompositionBrush? _brush;
        internal readonly Windows.UI.Composition.Compositor Compositor = new();
        internal int BrushWrites { get; private set; }
        public Windows.UI.Composition.CompositionBrush? SystemBackdrop
        {
            get => _brush;
            set { MaterialFaults.Hit("target.Assign"); BrushWrites++; _brush = value; }
        }
    }

    public abstract class ControllerBoundary : IDisposable
    {
        private readonly string _kind;
        private readonly Dictionary<ICompositionSupportsSystemBackdrop, Windows.UI.Composition.CompositionBrush> _owned = [];
        public readonly HashSet<ICompositionSupportsSystemBackdrop> Targets = [];
        public SystemBackdropConfiguration? Configuration { get; private set; }
        public int AddCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool Disposed { get; private set; }
        private SystemBackdropState _state;
        public SystemBackdropState State
        {
            get { Property("State"); return _state; }
            set => _state = value;
        }
        protected ControllerBoundary(string kind)
        {
            _kind = kind;
            MaterialFaults.Hit(kind + ".New");
            MaterialFaults.Controllers.Add(this);
            State = MaterialFaults.NextState;
        }
        protected void Property(string name)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            MaterialFaults.Hit(_kind + "." + name);
        }
        public void SetSystemBackdropConfiguration(SystemBackdropConfiguration configuration)
        {
            Property("SetConfiguration");
            Configuration = configuration;
        }
        public bool AddSystemBackdropTarget(ICompositionSupportsSystemBackdrop target)
        {
            AddCalls++;
            Property("AddBefore");
            var result = _kind == "mica" ? MaterialFaults.MicaAddResult : MaterialFaults.AcrylicAddResult;
            if (!result) return false;
            if (!Targets.Add(target)) throw new InvalidOperationException("duplicate controller target");
            var brush = new Windows.UI.Composition.CompositionBrush();
            _owned.Add(target, brush);
            target.SystemBackdrop = brush;
            MaterialFaults.Hit(_kind + ".AddAfter");
            return true;
        }
        public void RemoveSystemBackdropTarget(ICompositionSupportsSystemBackdrop target)
        {
            RemoveCalls++;
            Property("Remove");
            Targets.Remove(target);
            if (_owned.Remove(target, out var brush) && ReferenceEquals(target.SystemBackdrop, brush)) target.SystemBackdrop = null;
        }
        public void Dispose()
        {
            DisposeCalls++;
            MaterialFaults.Hit(_kind + ".Dispose");
            Disposed = true;
            foreach (var pair in _owned.ToArray())
                if (ReferenceEquals(pair.Key.SystemBackdrop, pair.Value)) pair.Key.SystemBackdrop = null;
            _owned.Clear(); Targets.Clear();
        }
    }
}

namespace Windows.UI
{
    public readonly record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    }
    public static class Colors
    {
        public static Color Black => Color.FromArgb(255, 0, 0, 0);
        public static Color White => Color.FromArgb(255, 255, 255, 255);
    }
}
namespace Windows.UI.ViewManagement
{
    public enum UIColorType { Background, Foreground }
    public sealed class UISettings
    {
        public static Color Background { get; set; } = Color.FromArgb(255, 9, 10, 11);
        public Color GetColorValue(UIColorType type)
        {
            Momoka.Tests.MaterialFaults.Hit("ui.GetColorValue");
            return type == UIColorType.Background ? Background : Colors.White;
        }
    }
}
namespace Windows.UI.Composition
{
    public class CompositionBrush : IDisposable
    {
        public Compositor Compositor { get; } = new();
        public virtual void Dispose() { }
    }
    public sealed class CompositionColorBrush : CompositionBrush
    {
        private Color _color;
        public Color Color
        {
            get => _color;
            set { Momoka.Tests.MaterialFaults.Hit("brush.Color"); _color = value; }
        }
        public int DisposeCalls { get; private set; }
        public bool Disposed { get; private set; }
        public override void Dispose()
        {
            DisposeCalls++;
            Momoka.Tests.MaterialFaults.Hit("brush.Dispose");
            Disposed = true;
        }
    }
    public sealed class Compositor : IDisposable
    {
        public CompositionColorBrush CreateColorBrush(Color color)
        {
            Momoka.Tests.MaterialFaults.Hit("legacy.CreateColorBrush");
            var brush = new CompositionColorBrush { Color = color };
            Momoka.Tests.MaterialFaults.Brushes.Add(brush);
            return brush;
        }
        public void Dispose() { }
    }
}
namespace Microsoft.UI.Composition
{
    public interface ICompositionSupportsSystemBackdrop
    {
        Windows.UI.Composition.CompositionBrush? SystemBackdrop { get; set; }
    }
    // Intentionally separate from Windows.UI.Composition to preserve real namespace compatibility.
    public abstract class CompositionBrush : IDisposable { public abstract void Dispose(); }
    public sealed class CompositionColorBrush : CompositionBrush
    {
        public Color Color { get; set; }
        public override void Dispose() { }
    }
    public sealed class Compositor
    {
        public CompositionColorBrush CreateColorBrush(Color color) => new() { Color = color };
    }
}
namespace Microsoft.UI.Composition.SystemBackdrops
{
    public enum SystemBackdropTheme { Default, Light, Dark }
    public enum SystemBackdropState { Active, Fallback, HighContrast }
    public enum MicaKind { Base, BaseAlt }
    public enum DesktopAcrylicKind { Base, Thin }
    public sealed class SystemBackdropConfiguration
    {
        private bool _active;
        private bool _contrast;
        private SystemBackdropTheme _theme;
        public bool IsInputActive
        {
            get => _active;
            set { Momoka.Tests.MaterialFaults.Hit("config.Active"); _active = value; }
        }
        public bool IsHighContrast
        {
            get => _contrast;
            set { Momoka.Tests.MaterialFaults.Hit("config.Contrast"); _contrast = value; }
        }
        public SystemBackdropTheme Theme
        {
            get => _theme;
            set { Momoka.Tests.MaterialFaults.Hit("config.Theme"); _theme = value; }
        }
    }
    public sealed class MicaController : Momoka.Tests.ControllerBoundary
    {
        private MicaKind _kind;
        public MicaController() : base("mica") { }
        public static bool IsSupported()
        {
            Momoka.Tests.MaterialFaults.Hit("mica.IsSupported");
            return Momoka.Tests.MaterialFaults.MicaSupported;
        }
        public MicaKind Kind { get => _kind; set { Property("Kind"); _kind = value; } }
    }
    public sealed class DesktopAcrylicController : Momoka.Tests.ControllerBoundary
    {
        private DesktopAcrylicKind _kind;
        private Color _tint;
        private Color _fallback;
        private float _tintOpacity;
        private float _luminosity;
        public DesktopAcrylicController() : base("acrylic") { }
        public static bool IsSupported()
        {
            Momoka.Tests.MaterialFaults.Hit("acrylic.IsSupported");
            return Momoka.Tests.MaterialFaults.AcrylicSupported;
        }
        public DesktopAcrylicKind Kind { get => _kind; set { Property("Kind"); _kind = value; } }
        public Color TintColor { get => _tint; set { Property("Tint"); _tint = value; } }
        public Color FallbackColor { get => _fallback; set { Property("Fallback"); _fallback = value; } }
        public float TintOpacity { get => _tintOpacity; set { Property("TintOpacity"); _tintOpacity = value; } }
        public float LuminosityOpacity { get => _luminosity; set { Property("Luminosity"); _luminosity = value; } }
    }
}
namespace Microsoft.UI.Xaml
{
    public sealed class XamlRoot
    {
        public SystemBackdropConfiguration Configuration { get; } = new();
    }
    public static class CompositionTarget
    {
        private static readonly Microsoft.UI.Composition.Compositor Compositor = new();
        public static Microsoft.UI.Composition.Compositor GetCompositorForCurrentThread() => Compositor;
    }
}
namespace Microsoft.UI.Xaml.Media
{
    public abstract class SystemBackdrop
    {
        private readonly Dictionary<ICompositionSupportsSystemBackdrop, XamlRoot> _roots = [];
        private readonly Dictionary<ICompositionSupportsSystemBackdrop, SystemBackdropConfiguration> _configurations = [];
        public int DefaultTargetCount => _roots.Count;
        public SystemBackdropConfiguration ConfigurationFor(ICompositionSupportsSystemBackdrop target) => _configurations[target];
        public void Connect(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            if (!_roots.TryAdd(target, root)) throw new InvalidOperationException("duplicate framework target");
            _configurations.Add(target, new SystemBackdropConfiguration());
            OnTargetConnected(target, root);
        }
        public void Disconnect(ICompositionSupportsSystemBackdrop target)
        {
            if (!_roots.TryGetValue(target, out _)) return;
            try { OnTargetDisconnected(target); }
            finally { _roots.Remove(target); _configurations.Remove(target); }
        }
        public void Change(ICompositionSupportsSystemBackdrop target, Action<SystemBackdropConfiguration> change)
        {
            var root = _roots[target];
            change(_configurations[target]);
            OnDefaultSystemBackdropConfigurationChanged(target, root);
        }
        protected virtual void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            Momoka.Tests.MaterialFaults.Hit("framework.Connect");
        }
        protected virtual void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
        {
            Momoka.Tests.MaterialFaults.Hit("framework.Disconnect");
        }
        protected virtual void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            Momoka.Tests.MaterialFaults.Hit("framework.Change");
        }
        protected SystemBackdropConfiguration GetDefaultSystemBackdropConfiguration(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            Momoka.Tests.MaterialFaults.Hit("framework.GetConfiguration");
            if (!_roots.TryGetValue(target, out var registered) || !ReferenceEquals(registered, root))
                throw new InvalidOperationException("unregistered framework target");
            return _configurations[target];
        }
    }
}
namespace Microsoft.UI.Xaml.Hosting
{
    public sealed class DesktopWindowXamlSource : ICompositionSupportsSystemBackdrop, IDisposable
    {
        private SystemBackdrop? _assigned;
        private Windows.UI.Composition.CompositionBrush? _brush;
        public XamlRoot Root { get; } = new();
        public int SetterCalls { get; private set; }
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public SystemBackdrop? SystemBackdrop
        {
            get => _assigned;
            set
            {
                SetterCalls++;
                if (ReferenceEquals(_assigned, value)) return;
                Momoka.Tests.MaterialFaults.Hit("host.Assign");
                var previous = _assigned;
                _assigned = null;
                if (previous is not null) { DisconnectCalls++; previous.Disconnect(this); }
                _assigned = value;
                if (value is not null) { ConnectCalls++; value.Connect(this, Root); }
            }
        }
        Windows.UI.Composition.CompositionBrush? ICompositionSupportsSystemBackdrop.SystemBackdrop
        {
            get => _brush;
            set { Momoka.Tests.MaterialFaults.Hit("host.BrushAssign"); _brush = value; }
        }
        public void Dispose() => SystemBackdrop = null;
    }
}
namespace Momoka.Theming
{
    public readonly record struct ThemeColor(byte R, byte G, byte B, byte A = 255)
    {
        public ThemeColor WithAlpha(byte alpha) => this with { A = alpha };
    }
    public sealed record ThemeColors(ThemeColor SurfaceAlt);
    public sealed record UiTheme(string Id, bool IsDark, ThemeColors Colors);
}
namespace Momoka.Shell
{
    public static class ThemeHost
    {
        private static Action<UiTheme>? _changed;
        public static UiTheme Current { get; private set; } = Palette(0);
        public static int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public static int AddCalls { get; private set; }
        public static int RemoveCalls { get; private set; }
        public static event Action<UiTheme> Changed
        {
            add { AddCalls++; _changed += value; }
            remove { RemoveCalls++; _changed -= value; }
        }
        public static Color ToColor(ThemeColor value) => Color.FromArgb(value.A, value.R, value.G, value.B);
        public static UiTheme Palette(int index, bool isDark = true, byte alpha = 255) =>
            new("fixture-" + index, isDark, new(new((byte)(20 + index * 5), (byte)(40 + index * 3), (byte)(60 + index * 2), alpha)));
        public static void Apply(UiTheme theme) { Current = theme; _changed?.Invoke(theme); }
        public static void ResetForTest() { _changed = null; AddCalls = RemoveCalls = 0; Current = Palette(0); }
    }
}
namespace Momoka.Diagnostics
{
    public static class Log
    {
        public static readonly List<string> Lines = [];
        public static void Info(string category, string message) => Lines.Add(category + ":" + message);
        public static void Debug(string category, string message) => Lines.Add(category + ":" + message);
        public static void Warn(string category, string message, Exception? error = null) => Lines.Add(category + ":" + message + (error?.GetType().Name ?? ""));
    }
}
namespace Momoka.Shell.Windowing
{
    internal sealed partial class HostWindow : IDisposable
    {
        private const string Category = "fixture-host";
        private Microsoft.UI.Xaml.Hosting.DesktopWindowXamlSource? _source;
        internal event Action<bool>? FocusChanged;
        internal Microsoft.UI.Xaml.Hosting.DesktopWindowXamlSource SourceForTest => _source ?? throw new InvalidOperationException("no source");
        internal void InitializeForTest()
        {
            _source = new();
            FocusChanged += OnBackdropFocusChanged; // Production setup wiring; behavior remains in linked partial.
            ApplyBackdrop();
        }
        internal void FocusForTest(bool active)
        {
            if (_source is null) OnBackdropFocusChanged(active);
            else FocusChanged?.Invoke(active);
        }
        internal void DisposeForTest() => ReleaseBackdrop();
        public void Dispose()
        {
            ReleaseBackdrop();
            _source?.Dispose();
            _source = null;
        }
    }
}

namespace Windows.UI.ViewManagement
{
    public sealed class AccessibilitySettings
    {
        public static bool Contrast { get; set; }
        public bool HighContrast => Contrast;
    }
}
namespace Microsoft.UI.Xaml
{
    public delegate void DependencyPropertyChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs args);
    public delegate void DependencyPropertyChangedHandler(DependencyObject sender, DependencyProperty property);
    public sealed class DependencyPropertyChangedEventArgs(object? oldValue, object? newValue)
    {
        public object? OldValue { get; } = oldValue;
        public object? NewValue { get; } = newValue;
    }
    public sealed class PropertyMetadata(object? defaultValue, DependencyPropertyChangedCallback? changed = null)
    {
        public object? DefaultValue { get; } = defaultValue;
        public DependencyPropertyChangedCallback? Changed { get; } = changed;
    }
    public sealed class DependencyProperty
    {
        public PropertyMetadata Metadata { get; }
        private DependencyProperty(PropertyMetadata metadata) => Metadata = metadata;
        public static DependencyProperty RegisterAttached(string name, Type type, Type owner, PropertyMetadata metadata) => new(metadata);
    }
    public class DependencyObject
    {
        private readonly Dictionary<DependencyProperty, object?> _values = [];
        private readonly Dictionary<long, (DependencyProperty Property, DependencyPropertyChangedHandler Handler)> _callbacks = [];
        private long _nextToken;
        public object GetValue(DependencyProperty property) => _values.GetValueOrDefault(property, property.Metadata.DefaultValue)!;
        public void SetValue(DependencyProperty property, object? value)
        {
            Momoka.Tests.MaterialFaults.Hit("dependency.Set");
            var previous = GetValue(property);
            if (Equals(previous, value)) return;
            _values[property] = value;
            property.Metadata.Changed?.Invoke(this, new(previous, value));
            foreach (var entry in _callbacks.Values.ToArray())
                if (entry.Property == property) entry.Handler(this, property);
        }
        public void ClearValue(DependencyProperty property)
        {
            var previous = GetValue(property);
            _values.Remove(property);
            var value = property.Metadata.DefaultValue;
            if (Equals(previous, value)) return;
            property.Metadata.Changed?.Invoke(this, new(previous, value));
            foreach (var entry in _callbacks.Values.ToArray())
                if (entry.Property == property) entry.Handler(this, property);
        }
        public long RegisterPropertyChangedCallback(DependencyProperty property, DependencyPropertyChangedHandler callback)
        {
            Momoka.Tests.MaterialFaults.Hit("dependency.Register");
            var token = ++_nextToken;
            _callbacks.Add(token, (property, callback));
            return token;
        }
        public void UnregisterPropertyChangedCallback(DependencyProperty property, long token)
        {
            if (_callbacks.TryGetValue(token, out var entry) && entry.Property == property) _callbacks.Remove(token);
        }
        public int PropertyCallbackCount => _callbacks.Count;
    }
    public sealed class RoutedEventArgs : EventArgs { }
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
}
namespace Microsoft.UI.Xaml.Media
{
    public abstract class Brush { }
    public sealed class SolidColorBrush : Brush
    {
        public SolidColorBrush() { }
        public SolidColorBrush(Color color) => Color = color;
        public Color Color { get; set; }
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public class Control : DependencyObject
    {
        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.RegisterAttached(
            "Background", typeof(Brush), typeof(Control), new PropertyMetadata(null));
        public Brush? Background { get => GetValue(BackgroundProperty) as Brush; set => SetValue(BackgroundProperty, value); }
        public Brush? Foreground { get; set; }
    }
    public sealed class MenuFlyoutPresenter : Control
    {
        public static readonly DependencyProperty SystemBackdropProperty = DependencyProperty.RegisterAttached(
            "SystemBackdrop", typeof(SystemBackdrop), typeof(MenuFlyoutPresenter), new PropertyMetadata(null));
        private RoutedEventHandler? _loaded;
        private RoutedEventHandler? _unloaded;
        public bool IsLoaded { get; private set; }
        public int LoadedHandlerCount => _loaded?.GetInvocationList().Length ?? 0;
        public int UnloadedHandlerCount => _unloaded?.GetInvocationList().Length ?? 0;
        public event RoutedEventHandler Loaded { add => _loaded += value; remove => _loaded -= value; }
        public event RoutedEventHandler Unloaded { add => _unloaded += value; remove => _unloaded -= value; }
        public SystemBackdrop? SystemBackdrop { get => GetValue(SystemBackdropProperty) as SystemBackdrop; set => SetValue(SystemBackdropProperty, value); }
        public void Load() { IsLoaded = true; _loaded?.Invoke(this, new()); }
        public void Unload() { IsLoaded = false; _unloaded?.Invoke(this, new()); }
        public void DelayedUnload() => _unloaded?.Invoke(this, new());
    }
}
