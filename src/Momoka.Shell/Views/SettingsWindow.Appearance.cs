using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Momoka.Diagnostics;
using System.Threading;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

// 系统通知只更新这扇窗的显示，框架仍管理 Mica 的系统策略和 target。
internal sealed partial class SettingsWindow
{
    private readonly AccessibilitySettings _appearanceAccessibility = new();
    private readonly UISettings _appearanceSettings = new();
    private bool _appearanceStopped;
    private bool _highContrastObserved;
    private bool _colorValuesObserved;
    private bool _effectsObserved;
    private bool _activationObserved;
    private DispatcherQueueTimer? _effectsFallbackTimer;
    private SettingsSystemAppearance? _lastSystemAppearance;
    private int _systemAppearanceQueued;
    private DispatcherQueue? _appearanceQueue;

    internal readonly record struct SettingsSystemAppearance(
        bool HighContrast, bool AdvancedEffects, Color Background, Color Foreground, Color Highlight, Color HighlightText);

    private SettingsSystemAppearance ReadSystemAppearance() => new(
        _appearanceAccessibility.HighContrast, _appearanceSettings.AdvancedEffectsEnabled,
        _appearanceSettings.GetColorValue(UIColorType.Background), _appearanceSettings.GetColorValue(UIColorType.Foreground),
        _appearanceSettings.UIElementColor(UIElementType.Highlight), _appearanceSettings.UIElementColor(UIElementType.HighlightText));

    // _mica means the Window.SystemBackdrop assignment succeeded; it never claims Active rendering.
    internal static Color SelectCanvasColor(SettingsSystemAppearance state, Color ordinaryWindow, bool micaAssigned)
    {
        var color = state.HighContrast ? state.Background : ordinaryWindow;
        color.A = !state.HighContrast && micaAssigned && state.AdvancedEffects ? CanvasAlpha : (byte)255;
        return color;
    }

    private void StartSystemAppearanceWatch()
    {
        _appearanceQueue = _window.DispatcherQueue;
        try
        {
            _appearanceAccessibility.HighContrastChanged += OnHighContrastChanged;
            _highContrastObserved = true;
            _appearanceSettings.ColorValuesChanged += OnColorValuesChanged;
            _colorValuesObserved = true;
            // This event is present in the current net.ref 19041 contract. Guard runtime availability.
            if (ApiInformation.IsEventPresent("Windows.UI.ViewManagement.UISettings", "AdvancedEffectsEnabledChanged"))
            {
                _appearanceSettings.AdvancedEffectsEnabledChanged += OnAdvancedEffectsChanged;
                _effectsObserved = true;
            }
            _window.Activated += OnAppearanceWindowActivated;
            _activationObserved = true;
        }
        catch (Exception error)
        {
            // Keep ordinary window usability; visible polling covers a missing event, not a setting change.
            Log.Warn(Category, "订阅设置窗口系统外观失败，显示时继续核对", error);
        }
        RefreshSystemAppearance();
    }

    private void OnHighContrastChanged(AccessibilitySettings sender, object args) => QueueSystemAppearance();
    private void OnColorValuesChanged(UISettings sender, object args) => QueueSystemAppearance();
    private void OnAdvancedEffectsChanged(UISettings sender, object args) => QueueSystemAppearance();
    private void OnAppearanceWindowActivated(object sender, WindowActivatedEventArgs args) => RefreshSystemAppearance();

    private void QueueSystemAppearance()
    {
        // System notifications may be raised off the UI thread; touch no XAML until the dispatcher runs.
        if (Interlocked.Exchange(ref _systemAppearanceQueued, 1) != 0) return;
        try
        {
            if (_appearanceQueue is null || !_appearanceQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _systemAppearanceQueued, 0);
                if (!_appearanceStopped) RefreshSystemAppearance();
            })) Interlocked.Exchange(ref _systemAppearanceQueued, 0);
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref _systemAppearanceQueued, 0);
            Log.Warn(Category, "派发设置窗口系统外观失败", error);
        }
    }

    private void RefreshSystemAppearance()
    {
        if (_appearanceStopped) return;
        try
        {
            var state = ReadSystemAppearance();
            if (_lastSystemAppearance == state) return;
            if (PaintCaption()) _lastSystemAppearance = state;
        }
        catch (Exception error) { Log.Warn(Category, "读取设置窗口系统外观失败", error); }
    }

    private void InvalidateSystemAppearance()
    {
        _lastSystemAppearance = null;
        RefreshSystemAppearance();
    }

    private void ResumeSystemAppearance()
    {
        RefreshSystemAppearance();
        if (_appearanceStopped || _highContrastObserved && _colorValuesObserved && _effectsObserved) return;
        if (_appearanceQueue is null) return;
        try
        {
            _effectsFallbackTimer ??= _appearanceQueue.CreateTimer();
            _effectsFallbackTimer.Interval = TimeSpan.FromSeconds(1);
            _effectsFallbackTimer.Tick -= OnEffectsFallbackTick;
            _effectsFallbackTimer.Tick += OnEffectsFallbackTick;
            _effectsFallbackTimer.Start();
        }
        catch (Exception error) { Log.Warn(Category, "启动设置外观降级观察失败，显示与激活时仍会核对", error); }
    }

    private void OnEffectsFallbackTick(DispatcherQueueTimer sender, object args)
    {
        if (_open && !_appearanceStopped) RefreshSystemAppearance();
        else PauseSystemAppearance();
    }

    private void PauseSystemAppearance()
    {
        try { _effectsFallbackTimer?.Stop(); }
        catch (Exception error) { Log.Warn(Category, "停止设置外观降级观察失败", error); }
    }

    private void StopSystemAppearanceWatch()
    {
        if (_appearanceStopped) return;
        _appearanceStopped = true;
        Unsubscribe(() => _effectsFallbackTimer?.Stop());
        Unsubscribe(() => { if (_effectsFallbackTimer is not null) _effectsFallbackTimer.Tick -= OnEffectsFallbackTick; });
        _effectsFallbackTimer = null;
        // Stop each successfully attached event independently, even if a preceding removal fails.
        Unsubscribe(() => { if (_highContrastObserved) _appearanceAccessibility.HighContrastChanged -= OnHighContrastChanged; });
        Unsubscribe(() => { if (_colorValuesObserved) _appearanceSettings.ColorValuesChanged -= OnColorValuesChanged; });
        Unsubscribe(() => { if (_effectsObserved) _appearanceSettings.AdvancedEffectsEnabledChanged -= OnAdvancedEffectsChanged; });
        Unsubscribe(() => { if (_activationObserved) _window.Activated -= OnAppearanceWindowActivated; });
        _highContrastObserved = _colorValuesObserved = _effectsObserved = _activationObserved = false;

        static void Unsubscribe(Action action)
        {
            try { action(); }
            catch (Exception error) { Log.Warn(Category, "退订设置窗口系统外观失败", error); }
        }
    }
}
