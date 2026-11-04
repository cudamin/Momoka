using Momoka.Configuration;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private readonly List<ControlBackgroundBrushes> _controlBackgrounds = [];
    private bool _appearanceObserved;
    private int _appearanceLifetime;

    private void PrepareAppearance()
    {
        foreach (var group in new[] { ColumnActions, CompactBlock })
            foreach (var row in group.Children.OfType<WrapRow>())
                foreach (var control in row.Children.OfType<Microsoft.UI.Xaml.Controls.Control>())
                    _controlBackgrounds.Add(ControlBackgroundBrushes.ForDetail(control));

        foreach (var picker in new[] { ColumnSourcePicker, ColumnAudioPicker, ColumnSubtitlePicker,
            SourcePicker, AudioPicker, SubtitlePicker })
            _controlBackgrounds.Add(ControlBackgroundBrushes.ForDetail(picker));
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
        PaintBackdropFade();
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
        SettleShowcase();
    }

    private void PaintBackdropFade()
    {
        var color = PaperColor;
        var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0),
            EndPoint = new Windows.Foundation.Point(0.5, 1)
        };
        foreach (var (offset, alpha) in new[] { (0d, (byte)0), (0.42, (byte)166), (0.78, (byte)232), (1d, byte.MaxValue) })
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
            {
                Offset = offset,
                Color = Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B)
            });
        BackdropFade.Background = brush;
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
        // 信息卡的逐行标签、内联链接与数据统一留在实体深色卡面上，避免整段文字另做蒙版寿命管理。
        InfoPanel.Background = ViewModel.GaussianBackdropApplied && ViewModel.Showcase
            ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["EgSurfaceAltBrush"] : _panelFill;
    }
}
