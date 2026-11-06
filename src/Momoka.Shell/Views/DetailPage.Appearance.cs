using Momoka.Configuration;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private readonly List<ControlBackgroundBrushes> _controlBackgrounds = [];
    private bool _appearanceObserved;
    private int _appearanceLifetime;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _actionAccessibility = new();
    private readonly Windows.UI.ViewManagement.UISettings _actionUiSettings = new();
    private readonly List<(Microsoft.UI.Xaml.Media.SolidColorBrush Brush, Windows.UI.Color Original)> _actionColors = [];

    private void PrepareAppearance()
    {
        foreach (var key in new[] { "EgDetailActionBrush", "EgDetailActionHoverBrush", "EgDetailActionPressedBrush",
            "EgDetailActionTextBrush", "EgDetailActionAccentBrush" })
        {
            var brush = (Microsoft.UI.Xaml.Media.SolidColorBrush)Microsoft.UI.Xaml.Application.Current.Resources[key];
            _actionColors.Add((brush, brush.Color));
        }
        foreach (var group in new[] { ColumnActions, CompactBlock })
            foreach (var row in group.Children.OfType<WrapRow>())
                foreach (var control in row.Children.OfType<Microsoft.UI.Xaml.Controls.Control>())
                {
                    var prefix = control is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton ? "ToggleButton" : "Button";
                    var ink = Microsoft.UI.Xaml.Application.Current.Resources["EgDetailActionTextBrush"];
                    foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
                        control.Resources[prefix + "Foreground" + suffix] = ink;
                    control.Foreground = (Microsoft.UI.Xaml.Media.Brush)ink;
                    _controlBackgrounds.Add(ControlBackgroundBrushes.ForDetail(control, lightSurface: true));
                }

        foreach (var picker in new[] { ColumnSourcePicker, ColumnAudioPicker, ColumnSubtitlePicker,
            SourcePicker, AudioPicker, SubtitlePicker })
        {
            var resources = Microsoft.UI.Xaml.Application.Current.Resources;
            var ink = resources["EgDetailActionTextBrush"];
            foreach (var key in new[] { "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundPressed",
                "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxDropDownForeground",
                "ComboBoxDropDownGlyphForeground", "ComboBoxDropDownGlyphForegroundFocused",
                "ComboBoxDropDownGlyphForegroundFocusedPressed" })
                picker.Resources[key] = ink;
            picker.Foreground = (Microsoft.UI.Xaml.Media.Brush)ink;
            picker.Resources["ComboBoxDropDownBackground"] = resources["EgDetailActionBrush"];
            foreach (var suffix in new[] { "", "Pressed", "PointerOver", "Selected", "SelectedUnfocused",
                "SelectedPressed", "SelectedPointerOver" })
                picker.Resources["ComboBoxItemForeground" + suffix] = ink;
            foreach (var suffix in new[] { "PointerOver", "Selected" })
                picker.Resources["ComboBoxItemBackground" + suffix] = resources["EgDetailActionHoverBrush"];
            foreach (var suffix in new[] { "Pressed", "SelectedUnfocused", "SelectedPressed", "SelectedPointerOver" })
                picker.Resources["ComboBoxItemBackground" + suffix] = resources["EgDetailActionPressedBrush"];
            _controlBackgrounds.Add(ControlBackgroundBrushes.ForDetail(picker, lightSurface: true));
        }
    }

    private void WatchAppearance()
    {
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
        ShellPrefs.AppearanceChanged += OnAppearanceChanged;
        _appearanceObserved = true;
        ApplyAppearance();
    }

    private void StopAppearance()
    {
        _appearanceObserved = false;
        _appearanceLifetime++;
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
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
        for (var index = 0; index < _actionColors.Count; index++)
        {
            var (brush, original) = _actionColors[index];
            brush.Color = contrast ? _actionUiSettings.GetColorValue(index < 3
                ? Windows.UI.ViewManagement.UIColorType.Background : Windows.UI.ViewManagement.UIColorType.Foreground) : original;
        }
        foreach (var brushes in _controlBackgrounds) brushes.Apply(ViewModel.ControlBackgroundOpacity, ViewModel.UseAcrylicControls);
        ViewModel.RefreshAppearance();
        DetailTitleInk.Visibility = ViewModel.GaussianBackdropApplied ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        DetailPlateInk.Visibility = DetailTitleInk.Visibility;
        DetailGenreInk.Visibility = DetailTitleInk.Visibility;
        DetailFactsInk.Visibility = DetailTitleInk.Visibility;
        DetailOverviewInk.Visibility = DetailTitleInk.Visibility;
        DetailScoreInk.Visibility = DetailTitleInk.Visibility;
        DetailScoreSourceInk.Visibility = DetailTitleInk.Visibility;
        DetailVideoInk.Visibility = DetailTitleInk.Visibility;
        DetailDirectorsInk.Visibility = DetailTitleInk.Visibility;
        PaintScrim();
        // 滑块也要更新正在动画中的目标；停止旧故事板后按最新偏好落定，避免收尾又写回旧浓度。
        SettleShowcase();
    }

    private void OnBackdropEffectChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ViewModels.DetailViewModel.GaussianBackdropApplied)) return;
        DetailTitleInk.Visibility = ViewModel.GaussianBackdropApplied ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        DetailPlateInk.Visibility = DetailTitleInk.Visibility;
        DetailGenreInk.Visibility = DetailTitleInk.Visibility;
        DetailFactsInk.Visibility = DetailTitleInk.Visibility;
        DetailOverviewInk.Visibility = DetailTitleInk.Visibility;
        DetailScoreInk.Visibility = DetailTitleInk.Visibility;
        DetailScoreSourceInk.Visibility = DetailTitleInk.Visibility;
        DetailVideoInk.Visibility = DetailTitleInk.Visibility;
        DetailDirectorsInk.Visibility = DetailTitleInk.Visibility;
        PaintScrim();
        // 缓存命中可同步完成渲染；尺寸和窗口状态仍在写入时，留给统一布局结算。
        if (!_sizing && !_applyingShowcase) SettleShowcase();
    }

    private void PaintBackdropReading()
    {
        foreach (var panel in BodySheet.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>())
            foreach (var head in panel.Children.OfType<ShelfHead>())
            {
                var floating = ViewModel.GaussianBackdropApplied && ViewModel.Showcase;
                head.Ink = floating;
                head.OnScrim = floating;
            }
        // 信息卡的逐行标签、内联链接与数据统一留在深色亚克力卡面上，避免整段文字另做蒙版寿命管理。
        InfoPanel.Background = ViewModel.GaussianBackdropApplied && ViewModel.Showcase
            ? CardMaterialSource.Background : _panelFill;
    }
}
