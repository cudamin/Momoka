using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Windows.UI;

namespace Momoka.Shell.Views;

/// <summary>详情页模式、卡面和外观材质的即时更新。</summary>
public sealed partial class MoviePilotSubscriptionPage
{
    private UiSettings _appearanceSettings = new();
    private readonly List<ControlBackgroundBrushes> _actionBackgrounds = [];
    private readonly AcrylicBrush _backdropFill = new() { TintTransitionDuration = TimeSpan.Zero };
    private readonly SolidColorBrush _backdropShade = new();
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();
    private readonly Windows.UI.ViewManagement.UISettings _systemAppearance = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _appearanceSystemTimer;
    private bool _lastHighContrast;
    private bool _lastAdvancedEffects;

    private void PrepareActionAppearance()
    {
        foreach (var button in DetailActions.Children.OfType<Button>())
            _actionBackgrounds.Add(ControlBackgroundBrushes.ForDetail(button, lightSurface: true));
        ApplyMaterialAppearance();
    }

    private void StartMaterialWatch()
    {
        _lastHighContrast = _accessibility.HighContrast;
        _lastAdvancedEffects = _systemAppearance.AdvancedEffectsEnabled;
        _appearanceSystemTimer ??= DispatcherQueue.CreateTimer();
        _appearanceSystemTimer.Interval = TimeSpan.FromSeconds(1);
        _appearanceSystemTimer.Tick -= CheckMaterialSystem;
        _appearanceSystemTimer.Tick += CheckMaterialSystem;
        _appearanceSystemTimer.Start();
        ApplyMaterialAppearance();
    }

    private void CheckMaterialSystem(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var contrast = _accessibility.HighContrast;
        var effects = _systemAppearance.AdvancedEffectsEnabled;
        if (!_appearanceObserved || contrast == _lastHighContrast && effects == _lastAdvancedEffects) return;
        _lastHighContrast = contrast;
        _lastAdvancedEffects = effects;
        ApplyMaterialAppearance();
    }

    // 背景和按钮各使用自己的三项设置；窄窗保留媒体详情页的实色阅读罩。
    private void ApplyMaterialAppearance()
    {
        var ui = _appearanceSettings;
        var contrast = _accessibility.HighContrast;
        var tint = contrast ? _systemAppearance.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : ThemeHost.ToColor(ShellPrefs.AppearanceColor(true, ui));
        tint.A = byte.MaxValue;
        var narrow = MediaViewModel.PageWidth is > 0 and < DetailHero.CompactFloor;
        var material = !narrow;
        if (contrast || !material)
        {
            _backdropShade.Color = contrast ? tint : Color.FromArgb(
                AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, AppearanceOpacity.ReadingVeil), tint.R, tint.G, tint.B);
            DetailBackdropScrim.Background = _backdropShade;
        }
        else
        {
            _backdropFill.TintColor = tint;
            _backdropFill.FallbackColor = tint;
            _backdropFill.TintOpacity = AppearanceOpacity.Tint(ui.BackdropTintOpacity, UiSettings.DefaultBackdropTintOpacity);
            _backdropFill.TintLuminosityOpacity = AppearanceOpacity.Tint(ui.BackdropTintLuminosityOpacity, UiSettings.DefaultBackdropTintLuminosityOpacity);
            _backdropFill.Opacity = 1;
            _backdropFill.AlwaysUseFallback = false;
            DetailBackdropScrim.Background = _backdropFill;
        }

        var controlTint = contrast ? tint : ThemeHost.ToColor(ShellPrefs.AppearanceColor(false, ui));
        foreach (var brushes in _actionBackgrounds)
        {
            brushes.Apply(contrast ? 100 : ui.ControlBackgroundOpacity, !contrast && ui.UseAcrylicControls, controlTint,
                AppearanceOpacity.Tint(ui.ControlTintOpacity, UiSettings.DefaultControlTintOpacity),
                AppearanceOpacity.Tint(ui.ControlTintLuminosityOpacity, UiSettings.DefaultControlTintLuminosityOpacity));
        }
        RefreshActionInk();
    }

    private void OnDetailViewChanged(object sender, ScrollViewerViewChangedEventArgs args) => RefreshActionInk();

    // 滚动改变按钮背后的固定画面，只重算文字颜色，不重建或重置材质。
    private void RefreshActionInk()
    {
        var ui = _appearanceSettings;
        var material = MediaViewModel.PageWidth <= 0 || MediaViewModel.PageWidth >= DetailHero.CompactFloor;
        var foreground = _accessibility.HighContrast
            ? _systemAppearance.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground) : (Color?)null;
        foreach (var brushes in _actionBackgrounds)
        {
            var reading = ThemeHost.Current.Colors.Window;
            if (MediaViewModel.Immersive && MediaViewModel.Backdrop is not null)
            {
                reading = BackdropInk.Sample(brushes.Control, DetailBackdropBand, MediaViewModel.BackdropPixels, true, reading);
                var overlay = ShellPrefs.AppearanceColor(true, ui);
                var coverage = material ? AppearanceOpacity.Coverage(
                    AppearanceOpacity.Tint(ui.BackdropTintOpacity, UiSettings.DefaultBackdropTintOpacity),
                    AppearanceOpacity.Tint(ui.BackdropTintLuminosityOpacity, UiSettings.DefaultBackdropTintLuminosityOpacity))
                    : AppearanceOpacity.ReadingVeil;
                if (material && !_systemAppearance.AdvancedEffectsEnabled) coverage = 1;
                reading = reading.Mix(overlay, coverage);
            }
            brushes.ApplyForeground(reading, !_systemAppearance.AdvancedEffectsEnabled, foreground);
        }
    }

    private readonly SolidColorBrush _sheetBrush = new();

    private readonly AcrylicBrush _panelFill = new();
    private readonly SolidColorBrush _panelLine = new();

    private IEnumerable<StackPanel> BodyPanels => [SeasonPanel, PeoplePanel, RecommendationsPanel, SimilarPanel];

    private void PrepareShowcase()
    {
        _sheetBrush.Color = PaperColor;
        BodySheet.Background = _sheetBrush;
        DetailBackdropBand.Opacity = DetailHero.BackdropOpacity;

        // 监听本页的 ThemeResource 替换，覆盖系统高对比度；不订阅需要 CoreWindow 的系统事件。
        CardMaterialSource.RegisterPropertyChangedCallback(Border.BackgroundProperty, (_, _) => RefreshPanelBrushes());
        RefreshPanelBrushes();
        foreach (var panel in BodyPanels)
        {
            panel.Background = _panelFill;
            panel.BorderBrush = _panelLine;
        }
    }

    private void WatchShowcaseTheme()
    {
        Loaded += (_, _) =>
        {
            ThemeHost.Changed -= OnShowcaseThemeChanged;
            ThemeHost.Changed += OnShowcaseThemeChanged;
        };
        Unloaded += (_, _) => ThemeHost.Changed -= OnShowcaseThemeChanged;
    }

    private void OnShowcaseThemeChanged(Momoka.Theming.UiTheme theme)
    {
        RefreshPanelBrushes();
        if (!MediaViewModel.Showcase) _sheetBrush.Color = PaperColor;
        ApplyMaterialAppearance();
    }

    private void RefreshPanelBrushes()
    {
        if (CardMaterialSource.Background is AcrylicBrush template)
        {
            _panelFill.AlwaysUseFallback = template.AlwaysUseFallback;
            _panelFill.TintColor = template.TintColor;
            _panelFill.FallbackColor = template.FallbackColor;
            _panelFill.TintOpacity = template.TintOpacity;
            _panelFill.TintLuminosityOpacity = template.TintLuminosityOpacity;
        }
        else if (CardMaterialSource.Background is SolidColorBrush contrast)
        {
            _panelFill.AlwaysUseFallback = true;
            _panelFill.FallbackColor = contrast.Color;
        }
        _panelLine.Color = BrushColor("EgBorderBrush");
    }

    private static Color PaperColor => BrushColor("EgWindowBrush");

    private static Color BrushColor(string key) => Application.Current is { } app
        && app.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush
        ? brush.Color
        : key switch
        {
            "EgWindowBrush" => Color.FromArgb(255, 0x0C, 0x11, 0x16),
            "EgSurfaceAltBrush" => Color.FromArgb(255, 0x1C, 0x25, 0x2E),
            _ => Color.FromArgb(255, 0x30, 0x39, 0x41)
        };

    // 背景亚克力覆盖整页；正文不再重复叠暗，以免上下出现硬分界。
    private static Color WashColor => Color.FromArgb(0, 0, 0, 0);

    private void OnShowcaseChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MoviePilotMediaViewModel.Showcase) or nameof(MoviePilotMediaViewModel.Plain))
        {
            SettleShowcase();
            ApplyMaterialAppearance();
        }
    }

    private void SettleShowcase()
    {
        var showcase = MediaViewModel.Showcase;
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        _panelFill.Opacity = showcase ? 0 : 1;
        _panelLine.Opacity = showcase ? 0 : 1;
        DetailBackdropBand.Opacity = showcase
            ? DetailHero.ShowcaseBackdropOpacity
            : DetailHero.BackdropOpacity;
    }
}
