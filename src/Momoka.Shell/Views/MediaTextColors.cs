using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>媒体正文只按主题选择字色，图片、滚动与材质浓度不参与。</summary>
internal static class MediaTextColors
{
    private static readonly Windows.UI.ViewManagement.AccessibilitySettings Accessibility = new();
    private static readonly Windows.UI.ViewManagement.UISettings SystemColors = new();

    internal static Windows.UI.Color Current => Accessibility.HighContrast
        ? SystemColors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
        : ThemeHost.ToColor(ThemeHost.Current.IsDark ? ThemeColor.Rgb(255, 255, 255) : ThemeColor.Rgb(22, 22, 22));

    internal static void Apply(FrameworkElement page)
    {
        var ink = Current;
        foreach (var key in new[] { "EgTextBrush", "EgTextDimBrush", "EgTextFaintBrush" })
            if (page.Resources.TryGetValue(key, out var resource) && resource is SolidColorBrush brush)
                brush.Color = ink;
    }

    internal static void Attach(FrameworkElement page)
    {
        void Paint(UiTheme theme) => Apply(page);
        page.Loaded += (_, _) =>
        {
            ThemeHost.Changed -= Paint;
            ThemeHost.Changed += Paint;
            Apply(page);
        };
        page.Unloaded += (_, _) => ThemeHost.Changed -= Paint;
        page.ActualThemeChanged += (_, _) => Apply(page);
        Apply(page);
    }
}
