using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public static class ComboBoxPopupChrome
{
    // WinUI 2.3.9 generic.xaml: DefaultComboBoxStyle 的 PopupBorder 从主题资源取圆角。
    // 局部覆盖保留原生模板，其他浮层仍从应用主题取值。
    private const string OverlayCornerRadiusKey = "OverlayCornerRadius";

    public static readonly DependencyProperty MatchControlCornerRadiusProperty = DependencyProperty.RegisterAttached(
        "MatchControlCornerRadius", typeof(bool), typeof(ComboBoxPopupChrome), new PropertyMetadata(false, OnMatchChanged));

    private static readonly DependencyProperty CallbackTokenProperty = DependencyProperty.RegisterAttached(
        "CallbackToken", typeof(long), typeof(ComboBoxPopupChrome), new PropertyMetadata(0L));

    public static bool GetMatchControlCornerRadius(DependencyObject target) =>
        (bool)target.GetValue(MatchControlCornerRadiusProperty);

    public static void SetMatchControlCornerRadius(DependencyObject target, bool value) =>
        target.SetValue(MatchControlCornerRadiusProperty, value);

    private static void OnMatchChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ComboBox combo) return;
        if (args.NewValue is true)
        {
            var token = combo.RegisterPropertyChangedCallback(Control.CornerRadiusProperty, OnCornerRadiusChanged);
            combo.SetValue(CallbackTokenProperty, token);
            ApplyCornerRadius(combo);
        }
        else if (combo.ReadLocalValue(CallbackTokenProperty) is long token)
        {
            combo.UnregisterPropertyChangedCallback(Control.CornerRadiusProperty, token);
            combo.ClearValue(CallbackTokenProperty);
            combo.Resources.Remove(OverlayCornerRadiusKey);
        }
    }

    private static void OnCornerRadiusChanged(DependencyObject target, DependencyProperty property) =>
        ApplyCornerRadius((ComboBox)target);

    private static void ApplyCornerRadius(ComboBox combo) =>
        combo.Resources[OverlayCornerRadiusKey] = combo.CornerRadius;
}
