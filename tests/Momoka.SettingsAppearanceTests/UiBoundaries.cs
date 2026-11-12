using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Momoka.Shell;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Momoka.Tests
{
    internal sealed class InjectedFailure(string point) : Exception(point);
    internal static class AppearanceFaults
    {
        private static readonly Dictionary<string, int> Failures = [];
        internal static readonly List<AccessibilitySettings> Accessibility = [];
        internal static readonly List<UISettings> Settings = [];
        internal static readonly List<Window> Windows = [];
        internal static bool EventPresent = true;
        internal static bool ReadOffUi;
        internal static bool HighContrast;
        internal static bool Effects = true;
        internal static Color Background = Color.FromArgb(255, 20, 25, 30);
        internal static Color Foreground = Color.FromArgb(255, 240, 241, 242);
        internal static Color Highlight = Color.FromArgb(255, 44, 72, 120);
        internal static Color HighlightText = Color.FromArgb(255, 251, 252, 253);
        internal static int Reads;
        internal static bool PaintReturnsFalse;
        internal static void FailOnce(string point) => Failures[point] = Failures.GetValueOrDefault(point) + 1;
        internal static void Hit(string point)
        {
            if (Failures.GetValueOrDefault(point) == 0) return;
            if (--Failures[point] == 0) Failures.Remove(point);
            throw new InjectedFailure(point);
        }
        internal static void Read(string point)
        {
            if (ReadOffUi) throw new InvalidOperationException("System state read before UI dispatcher boundary");
            Reads++;
            Hit(point);
        }
        internal static void Reset()
        {
            Failures.Clear(); Accessibility.Clear(); Settings.Clear(); Windows.Clear();
            EventPresent = Effects = true; HighContrast = ReadOffUi = PaintReturnsFalse = false; Reads = 0;
            Background = Color.FromArgb(255, 20, 25, 30);
            Foreground = Color.FromArgb(255, 240, 241, 242);
            Highlight = Color.FromArgb(255, 44, 72, 120);
            HighlightText = Color.FromArgb(255, 251, 252, 253);
            Momoka.Diagnostics.Log.Lines.Clear();
            ThemeHost.Current = new Theme(Colors.Black);
        }
        internal static int Subscribers => Accessibility.Sum(item => item.Subscribers)
            + Settings.Sum(item => item.Subscribers) + Windows.Sum(item => item.Subscribers);
        internal static void FireContrast() { foreach (var item in Accessibility.ToArray()) item.Fire(); }
        internal static void FireColor() { foreach (var item in Settings.ToArray()) item.FireColor(); }
        internal static void FireEffects() { foreach (var item in Settings.ToArray()) item.FireEffects(); }
    }
}
namespace Windows.UI
{
    public struct Color : IEquatable<Color>
    {
        public byte A; public byte R; public byte G; public byte B;
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new() { A = a, R = r, G = g, B = b };
        public bool Equals(Color other) => A == other.A && R == other.R && G == other.G && B == other.B;
        public override bool Equals(object? other) => other is Color color && Equals(color);
        public override int GetHashCode() => HashCode.Combine(A, R, G, B);
        public static bool operator ==(Color a, Color b) => a.Equals(b);
        public static bool operator !=(Color a, Color b) => !a.Equals(b);
    }
    public static class Colors
    {
        public static Color Black => Color.FromArgb(255, 0, 0, 0);
        public static Color White => Color.FromArgb(255, 255, 255, 255);
    }
}
namespace Windows.Foundation.Metadata
{
    public static class ApiInformation
    {
        public static bool IsEventPresent(string type, string name)
        {
            Momoka.Tests.AppearanceFaults.Hit("api.IsEventPresent");
            return Momoka.Tests.AppearanceFaults.EventPresent;
        }
    }
}
namespace Windows.UI.ViewManagement
{
    public delegate void SystemEventHandler<T>(T sender, object args);
    public enum UIColorType { Background, Foreground }
    public enum UIElementType { Highlight, HighlightText }
    public sealed class AccessibilitySettings
    {
        private SystemEventHandler<AccessibilitySettings>? _changed;
        public AccessibilitySettings() => Momoka.Tests.AppearanceFaults.Accessibility.Add(this);
        public bool HighContrast { get { Momoka.Tests.AppearanceFaults.Read("read.HighContrast"); return Momoka.Tests.AppearanceFaults.HighContrast; } }
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public event SystemEventHandler<AccessibilitySettings> HighContrastChanged
        {
            add { Momoka.Tests.AppearanceFaults.Hit("add.Contrast"); _changed += value; }
            remove { Momoka.Tests.AppearanceFaults.Hit("remove.Contrast"); _changed -= value; }
        }
        public void Fire() => _changed?.Invoke(this, new object());
        internal void ReleaseFixtureSubscribers() => _changed = null;
    }
    public sealed class UISettings
    {
        private SystemEventHandler<UISettings>? _color;
        private SystemEventHandler<UISettings>? _effects;
        public UISettings() => Momoka.Tests.AppearanceFaults.Settings.Add(this);
        public bool AdvancedEffectsEnabled { get { Momoka.Tests.AppearanceFaults.Read("read.Effects"); return Momoka.Tests.AppearanceFaults.Effects; } }
        public int ColorSubscribers => _color?.GetInvocationList().Length ?? 0;
        public int EffectsSubscribers => _effects?.GetInvocationList().Length ?? 0;
        public int Subscribers => ColorSubscribers + EffectsSubscribers;
        public event SystemEventHandler<UISettings> ColorValuesChanged
        {
            add { Momoka.Tests.AppearanceFaults.Hit("add.Color"); _color += value; }
            remove { Momoka.Tests.AppearanceFaults.Hit("remove.Color"); _color -= value; }
        }
        public event SystemEventHandler<UISettings> AdvancedEffectsEnabledChanged
        {
            add { Momoka.Tests.AppearanceFaults.Hit("add.Effects"); _effects += value; }
            remove { Momoka.Tests.AppearanceFaults.Hit("remove.Effects"); _effects -= value; }
        }
        public Color GetColorValue(UIColorType type)
        {
            Momoka.Tests.AppearanceFaults.Read(type == UIColorType.Background ? "read.Background" : "read.Foreground");
            return type == UIColorType.Background ? Momoka.Tests.AppearanceFaults.Background : Momoka.Tests.AppearanceFaults.Foreground;
        }
        public Color UIElementColor(UIElementType type)
        {
            Momoka.Tests.AppearanceFaults.Read(type == UIElementType.Highlight ? "read.Highlight" : "read.HighlightText");
            return type == UIElementType.Highlight ? Momoka.Tests.AppearanceFaults.Highlight : Momoka.Tests.AppearanceFaults.HighlightText;
        }
        public void FireColor() => _color?.Invoke(this, new object());
        public void FireEffects() => _effects?.Invoke(this, new object());
        internal void ReleaseFixtureSubscribers() { _color = null; _effects = null; }
    }
}
namespace Microsoft.UI.Dispatching
{
    public delegate void DispatcherQueueHandler();
    public delegate void DispatcherQueueTimerHandler(DispatcherQueueTimer sender, object args);
    public sealed class DispatcherQueue
    {
        private readonly Queue<DispatcherQueueHandler> _pending = [];
        public bool Accept { get; set; } = true;
        public bool Throw { get; set; }
        public int EnqueueCalls { get; private set; }
        public int Pending => _pending.Count;
        public readonly List<DispatcherQueueTimer> Timers = [];
        public bool TryEnqueue(DispatcherQueueHandler callback)
        {
            EnqueueCalls++;
            if (Throw) throw new Momoka.Tests.InjectedFailure("queue.TryEnqueue");
            if (!Accept) return false;
            _pending.Enqueue(callback); return true;
        }
        public void Drain()
        {
            while (_pending.TryDequeue(out var callback)) callback();
        }
        public DispatcherQueueTimer CreateTimer()
        {
            Momoka.Tests.AppearanceFaults.Hit("timer.Create");
            var timer = new DispatcherQueueTimer(); Timers.Add(timer); return timer;
        }
    }
    public sealed class DispatcherQueueTimer
    {
        private DispatcherQueueTimerHandler? _tick;
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public int Subscribers => _tick?.GetInvocationList().Length ?? 0;
        public event DispatcherQueueTimerHandler Tick
        {
            add { Momoka.Tests.AppearanceFaults.Hit("timer.Add"); _tick += value; }
            remove { Momoka.Tests.AppearanceFaults.Hit("timer.Remove"); _tick -= value; }
        }
        public void Start() { Momoka.Tests.AppearanceFaults.Hit("timer.Start"); StartCalls++; IsRunning = true; }
        public void Stop() { Momoka.Tests.AppearanceFaults.Hit("timer.Stop"); StopCalls++; IsRunning = false; }
        public void Fire() { if (IsRunning) _tick?.Invoke(this, new object()); }
        internal void ReleaseFixtureSubscribers() => _tick = null;
    }
}
namespace Microsoft.UI.Xaml
{
    public sealed class WindowActivatedEventArgs : EventArgs { }
    public delegate void WindowActivatedEventHandler(object sender, WindowActivatedEventArgs args);
    public sealed class Window
    {
        private WindowActivatedEventHandler? _activated;
        public Window() => Momoka.Tests.AppearanceFaults.Windows.Add(this);
        public DispatcherQueue DispatcherQueue { get; } = new();
        public int Subscribers => _activated?.GetInvocationList().Length ?? 0;
        public event WindowActivatedEventHandler Activated
        {
            add { Momoka.Tests.AppearanceFaults.Hit("add.Activated"); _activated += value; }
            remove { Momoka.Tests.AppearanceFaults.Hit("remove.Activated"); _activated -= value; }
        }
        public void Activate() => _activated?.Invoke(this, new());
        internal void ReleaseFixtureSubscribers() => _activated = null;
    }
}
namespace Momoka.Shell
{
    public sealed record Theme(Color Window);
    public static class ThemeHost { public static Theme Current { get; set; } = new(Colors.Black); }
}
namespace Momoka.Diagnostics
{
    public static class Log
    {
        public static readonly List<string> Lines = [];
        public static void Warn(string category, string message, Exception? error = null) => Lines.Add(category + ":" + message + ":" + error?.Message);
    }
}
namespace Momoka.Shell.Views
{
    // Window creation, actual title bar APIs and Show/Hide integration remain runtime/source-contract coverage.
    internal sealed partial class SettingsWindow : IDisposable
    {
        private const string Category = "settings-appearance-fixture";
        private const byte CanvasAlpha = 0x99;
        private readonly Window _window = new();
        private bool _open;
        private bool _mica = true;
        private bool _initializedForTest;
        internal int PaintCalls { get; private set; }
        internal Color CanvasForTest { get; private set; }
        internal SettingsSystemAppearance? SnapshotForTest { get; private set; }
        internal DispatcherQueue Queue => _window.DispatcherQueue;
        internal void InitializeForTest(bool mica = true)
        {
            if (_initializedForTest) throw new InvalidOperationException("fixture initialized twice");
            _initializedForTest = true; _mica = mica;
            StartSystemAppearanceWatch();
        }
        internal void ShowForTest() { _open = true; ResumeSystemAppearance(); }
        internal void HideForTest() { _open = false; PauseSystemAppearance(); }
        internal void RefreshForTest() => RefreshSystemAppearance();
        internal void ActivateForTest() => _window.Activate();
        internal void PaintForTest() => PaintCaption();
        internal void ThemeForTest() => InvalidateSystemAppearance();
        internal void FailBackdropForTest()
        {
            _mica = false;
            CanvasForTest = ThemeHost.Current.Window;
            InvalidateSystemAppearance();
        }
        private bool PaintCaption()
        {
            Momoka.Tests.AppearanceFaults.Hit("caption.Paint");
            PaintCalls++;
            SnapshotForTest = ReadSystemAppearance();
            CanvasForTest = SelectCanvasColor(SnapshotForTest.Value, ThemeHost.Current.Window, _mica);
            return !Momoka.Tests.AppearanceFaults.PaintReturnsFalse;
        }
        public void Dispose() { _open = false; StopSystemAppearanceWatch(); }
    }
}
