using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Theming;
using Windows.UI;

namespace Momoka.Shell;

/// <summary>就地更新全局控件三态材质；只改交互专用画刷，保留输入区、卡面和系统高对比资源。</summary>
public static class GlobalControlAppearance
{
    private static readonly string[] GlassKeys = ["EgActionGlassBrush", "EgActionGlassHoverBrush", "EgActionGlassPressedBrush"];
    private static readonly double[] FeedbackFloors = [0, 21d / 255, 8d / 255];
    private static UiSettings? _settings;

    public static void Apply(UiSettings ui)
    {
        _settings = ui;
        Refresh();
    }

    /// <summary>没有外观偏好时沿用主题；设置和拾色草稿存在时三态共用实际染色。</summary>
    public static ThemeColor TintColor(ThemeColor fallback) =>
        _settings is { } ui ? ShellPrefs.AppearanceColor(false, ui) : fallback;

    /// <summary>主题刷写入后重放最近的外观偏好，不再发送外观事件。</summary>
    public static void Refresh()
    {
        if (_settings is not { } ui || Application.Current is not { } application) return;
        var rgb = ShellPrefs.AppearanceColor(false, ui);
        var tint = Color.FromArgb(byte.MaxValue, rgb.R, rgb.G, rgb.B);
        var amount = Percent(ui.ControlBackgroundOpacity, 0);
        var normal = 15d / 255;
        foreach (var palette in application.Resources.MergedDictionaries)
        {
            if (palette.Source is null || !palette.Source.ToString().EndsWith("Palette.xaml", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var name in new[] { "Dark", "Light" })
            {
                if (!palette.ThemeDictionaries.TryGetValue(name, out var dictionary) || dictionary is not ResourceDictionary entries) continue;
                for (var index = 0; index < GlassKeys.Length; index++)
                {
                    if (!entries.TryGetValue(GlassKeys[index], out var value) || value is not AcrylicBrush brush) continue;
                    var floor = FeedbackFloors[index];
                    var strength = index == 0 ? amount : amount <= normal ? floor
                        : floor + (1 - floor) * (amount - normal) / (1 - normal);
                    brush.TintColor = tint;
                    brush.FallbackColor = tint;
                    brush.TintOpacity = Percent(ui.ControlTintOpacity, UiSettings.DefaultAcrylicTintOpacity);
                    brush.TintLuminosityOpacity = Percent(ui.ControlTintLuminosityOpacity, UiSettings.DefaultAcrylicTintOpacity);
                    brush.Opacity = strength;
                    brush.AlwaysUseFallback = strength is 0 or 1;
                    brush.TintTransitionDuration = TimeSpan.Zero;
                }
            }
        }
    }

    private static double Percent(double value, double fallback) =>
        (double.IsFinite(value) ? Math.Clamp(value, 0, 100) : fallback) / 100;
}
