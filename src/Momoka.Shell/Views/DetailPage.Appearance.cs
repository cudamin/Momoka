using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Theming;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private readonly List<ControlBackgroundBrushes> _controlBackgrounds = [];
    private bool _appearanceObserved;
    private int _appearanceLifetime;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _actionAccessibility = new();
    private readonly Windows.UI.ViewManagement.UISettings _actionUiSettings = new();
    private readonly Dictionary<TextBlock, SolidColorBrush> _readingColors = [];
    private readonly List<FontIcon> _plainActionIcons = [];
    private readonly Dictionary<FrameworkElement, Control> _explicitActionForegrounds = [];
    private bool _readingQueued;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _appearanceSystemTimer;
    private bool _lastContrast;
    private bool _lastEffects;

    private void PrepareAppearance()
    {
        foreach (var strip in new[] { EpisodeStrip, SeasonRow, CastRow, SimilarRow })
            strip.ReadingChanged += QueueReadingColors;
        foreach (var group in new[] { ColumnActions, CompactBlock })
            foreach (var row in group.Children.OfType<WrapRow>())
                foreach (var control in row.Children.OfType<Microsoft.UI.Xaml.Controls.Control>())
                {
                    _controlBackgrounds.Add(ControlBackgroundBrushes.ForDetail(control, lightSurface: true));
                }

        foreach (var picker in new[] { ColumnSourcePicker, ColumnAudioPicker, ColumnSubtitlePicker,
            SourcePicker, AudioPicker, SubtitlePicker })
        {
            // 展开面与行背景留给 Palette 的亚克力及 WinUI 原状态，闭合态才与按钮共用薄白。
            _controlBackgrounds.Add(ControlBackgroundBrushes.ForDetail(picker, lightSurface: true));
        }
    }

    private void QueueReadingColors()
    {
        if (!_appearanceObserved || _readingQueued) return;
        _readingQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _readingQueued = false;
            PaintReadingColors();
        })) _readingQueued = false;
    }

    private void WatchAppearance()
    {
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
        ShellPrefs.AppearanceChanged += OnAppearanceChanged;
        _appearanceObserved = true;
        _lastContrast = _actionAccessibility.HighContrast;
        _lastEffects = _actionUiSettings.AdvancedEffectsEnabled;
        _appearanceSystemTimer ??= DispatcherQueue.CreateTimer();
        _appearanceSystemTimer.Interval = TimeSpan.FromSeconds(1);
        _appearanceSystemTimer.Tick -= CheckAppearanceSystem;
        _appearanceSystemTimer.Tick += CheckAppearanceSystem;
        _appearanceSystemTimer.Start();
        ApplyAppearance();
    }

    private void StopAppearance()
    {
        _appearanceObserved = false;
        _appearanceSystemTimer?.Stop();
        _appearanceLifetime++;
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
    }

    private void CheckAppearanceSystem(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var contrast = _actionAccessibility.HighContrast;
        var effects = _actionUiSettings.AdvancedEffectsEnabled;
        if (!_appearanceObserved || contrast == _lastContrast && effects == _lastEffects) return;
        _lastContrast = contrast;
        _lastEffects = effects;
        ApplyAppearance();
    }

    private void OnAppearanceChanged(UiSettings ui)
    {
        if (!_appearanceObserved) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyAppearance();
            return;
        }
        var lifetime = _appearanceLifetime;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_appearanceObserved && lifetime == _appearanceLifetime) ApplyAppearance();
        });
    }

    private void ApplyAppearance()
    {
        var contrast = _actionAccessibility.HighContrast;
        var tint = contrast ? _actionUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : ThemeHost.ToColor(ShellPrefs.AppearanceColor(false, ViewModel.AppearanceSettings));
        foreach (var brushes in _controlBackgrounds)
            brushes.Apply(contrast ? 100 : ViewModel.ControlBackgroundOpacity, !contrast && ViewModel.UseAcrylicControls, tint,
                ViewModel.ControlTintOpacity, ViewModel.ControlTintLuminosityOpacity);
        ViewModel.RefreshAppearance();
        PaintScrim();
        // 模式与背景强度直接应用终值，窄窗仍使用自己的排版。
        SettleShowcase();
    }

    private void OnBackdropImageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ViewModels.DetailViewModel.HeroImage)) return;
        PaintReadingColors();
        QueueReadingColors();
    }

    private void PaintBackdropReading()
    {
        // 信息卡的逐行标签、内联链接与数据统一留在深色亚克力卡面上，避免整段文字另做蒙版寿命管理。
        // 2026-11-08 用户令「参考集页面给其他媒体页面的其他信息也加上亚克力背景」：这张卡在沉浸档四类页面上
        // 都保留卡面，不再只管单集页（电影、剧、季三页从前在这里让开，表里的暗字直接压在剧照上）。
        InfoPanel.Background = ViewModel.Showcase ? CardMaterialSource.Background : _panelFill;
        PaintReadingColors();
    }

    private ThemeColor ReadingBackground(FrameworkElement node)
    {
        var fallback = ThemeHost.Current.Colors.Window;
        if (!ViewModel.BackdropShown) return fallback;
        var sample = BackdropInk.Sample(node, BackdropPicture, ViewModel.BackdropPixels, true, fallback);
        var overlay = ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings);
        // 画了亚克力那一层的地方，字压在材料上：挡住多少就用 Coverage 估多少；没画那一层（窄窗、集页卡面）
        // 压的还是那道实色罩子。系统关掉透明效果时材质回退成不透明实色，这里也按满不透明算。
        var alpha = UsesBackdropFade
            ? AppearanceOpacity.Coverage(ViewModel.BackdropTintOpacity, ViewModel.BackdropTintLuminosityOpacity)
            : AppearanceOpacity.ReadingVeil;
        if (UsesBackdropFade && !_actionUiSettings.AdvancedEffectsEnabled) alpha = 1;
        return sample.Mix(overlay, alpha);
    }

    private void PaintReadingColors()
    {
        if (!_appearanceObserved) return;
        var contrast = _actionAccessibility.HighContrast;
        var forced = contrast ? _actionUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground) : (Color?)null;
        foreach (var backgrounds in _controlBackgrounds)
            backgrounds.ApplyDefaultForeground(forced);
        // 模板中显式指定过颜色的非强调图标/连播文字，跟同一颗按钮走。
        foreach (var backgrounds in _controlBackgrounds)
        {
            if (backgrounds.Control is ComboBox) continue;
            foreach (var child in AppearanceChildren(backgrounds.Control))
                if (child is TextBlock text && text.Name.Length == 0)
                {
                    if (!_explicitActionForegrounds.ContainsKey(text))
                    {
                        if (backgrounds.Control is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton)
                            text.Foreground = backgrounds.Control.Foreground;
                        else text.ClearValue(TextBlock.ForegroundProperty);
                        _explicitActionForegrounds.Add(text, backgrounds.Control);
                    }
                }
                else if (child is FontIcon icon)
                {
                    if (!_plainActionIcons.Contains(icon) && icon.Foreground is SolidColorBrush ink
                        && ink.Color == ((SolidColorBrush)Application.Current.Resources["EgDetailActionTextBrush"]).Color)
                        _plainActionIcons.Add(icon);
                    if (_plainActionIcons.Contains(icon) && !_explicitActionForegrounds.ContainsKey(icon))
                    {
                        icon.ClearValue(FontIcon.ForegroundProperty);
                        _explicitActionForegrounds.Add(icon, backgrounds.Control);
                    }
                }
        }
        foreach (var text in new[] { TitleText, GenreLine, HeroFacts, Overview, ScoreText, ScoreSourceText,
            DetailVideoText, DetailDirectorsText, ColumnAudioText, AudioText })
        {
            var defaultBrush = text == GenreLine ? (SolidColorBrush)ViewModel.SublineBrush
                : (SolidColorBrush)Application.Current.Resources[text == DetailDirectorsText ? "EgOnScrimDimBrush" : "EgOnScrimBrush"];
            var defaultColor = defaultBrush.Color;
            var color = forced is { } system ? ThemeColor.Rgb(system.R, system.G, system.B)
                : ThemeColor.Rgb(defaultColor.R, defaultColor.G, defaultColor.B);
            if (!_readingColors.TryGetValue(text, out var brush))
            {
                brush = new SolidColorBrush();
                _readingColors.Add(text, brush);
                text.Foreground = brush;
            }
            brush.Color = ThemeHost.ToColor(color);
        }
        foreach (var child in AppearanceChildren(BodySheet))
        {
            var systemInk = forced is { } system ? ThemeColor.Rgb(system.R, system.G, system.B) : (ThemeColor?)null;
            if (child is ShelfHead head) head.ApplyBackdropInk(systemInk);
            else if (child is PosterCard card)
                card.ApplyBackdropInk(systemInk is { } ink ? _ => ink : null);
        }
        foreach (var group in new[] { ColumnPickers, PickerPanel })
            foreach (var text in AppearanceChildren(group).OfType<TextBlock>())
            {
                if (IsPickerContent(text)) continue;
                if (!_readingColors.TryGetValue(text, out var brush))
                {
                    brush = new SolidColorBrush();
                    _readingColors.Add(text, brush);
                    text.Foreground = brush;
                }
                brush.Color = forced ?? ((SolidColorBrush)Application.Current.Resources["EgOnScrimBrush"]).Color;
            }
    }

    private static bool IsPickerContent(DependencyObject node)
    {
        for (var parent = VisualTreeHelper.GetParent(node); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ComboBox picker)
                return picker.Header is not FrameworkElement header || !ReferenceEquals(header, node);
        return false;
    }

    private static IEnumerable<DependencyObject> AppearanceChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in AppearanceChildren(child)) yield return nested;
        }
    }
}
