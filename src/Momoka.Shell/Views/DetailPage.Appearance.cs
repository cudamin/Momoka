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
        foreach (var brushes in _controlBackgrounds) brushes.Apply(ViewModel.ControlBackgroundOpacity);
        ViewModel.RefreshAppearance();
        // 滑块也要更新正在动画中的目标；停止旧故事板后按最新偏好落定，避免收尾又写回旧浓度。
        SettleShowcase();
    }
}
