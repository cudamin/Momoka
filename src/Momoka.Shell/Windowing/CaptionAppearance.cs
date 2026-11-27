using Microsoft.UI;
using Microsoft.UI.Windowing;
using Momoka.Shell.Interop;
using Momoka.Theming;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Windowing;

/// <summary>
/// 读取系统高对比度/系统色，并统一主窗口与独立子窗口的标题栏非客户区与 DWM 属性着色。
/// </summary>
internal static class CaptionAppearance
{
    private static readonly AccessibilitySettings Accessibility = new();
    private static readonly UISettings Ui = new();

    internal readonly record struct SystemPalette(
        bool HighContrast,
        bool AdvancedEffects,
        Color Background,
        Color Foreground,
        Color Highlight,
        Color HighlightText,
        Color GrayText);

    internal static SystemPalette ReadSystem()
    {
        var contrast = SafeRead(() => Accessibility.HighContrast, false);
        var effects = SafeRead(() => Ui.AdvancedEffectsEnabled, true);
        var bg = SafeRead(() => Ui.GetColorValue(UIColorType.Background), Color.FromArgb(255, 0x0C, 0x11, 0x16));
        var fg = SafeRead(() => Ui.GetColorValue(UIColorType.Foreground), Color.FromArgb(255, 0xF2, 0xF6, 0xFA));
        var highlight = SafeRead(() => Ui.UIElementColor(UIElementType.Highlight), Color.FromArgb(255, 0x6E, 0xE7, 0xC5));
        var highlightText = SafeRead(() => Ui.UIElementColor(UIElementType.HighlightText), bg);
        var grayText = SafeRead(() => Ui.UIElementColor(UIElementType.GrayText), Color.FromArgb(255, 0x7A, 0x7F, 0x83));
        return new SystemPalette(contrast, effects, bg, fg, highlight, highlightText, grayText);
    }

    internal static void ApplyDwmChrome(IntPtr handle, UiTheme theme, bool useBackdrop, bool fullscreen, bool highContrast)
    {
        if (handle == IntPtr.Zero) return;

        var dark = !highContrast && theme.IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(handle, Native.DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        var corners = fullscreen ? Native.DwmCornerDoNotRound : Native.DwmCornerRound;
        Native.DwmSetWindowAttribute(handle, Native.DwmWindowCornerPreference, ref corners, sizeof(int));

        var backdrop = useBackdrop && ThemeHost.TransparencyEnabled && !highContrast
            ? Native.DwmBackdropMica
            : Native.DwmBackdropNone;
        Native.DwmSetWindowAttribute(handle, Native.DwmSystemBackdropType, ref backdrop, sizeof(int));
    }

    internal static void PaintTitleBar(
        AppWindowTitleBar titleBar,
        UiTheme theme,
        SystemPalette system,
        bool onScrim,
        bool transparentSurface,
        bool dimInactive)
    {
        var surface = transparentSurface
            ? Colors.Transparent
            : system.HighContrast
                ? system.Background
                : ThemeHost.ToColor(theme.Colors.Window);

        var activeInk = system.HighContrast
            ? system.Foreground
            : onScrim
                ? Color.FromArgb(255, 0xF3, 0xF5, 0xF8)
                : ThemeHost.ToColor(theme.Colors.Text);

        titleBar.BackgroundColor = surface;
        titleBar.ForegroundColor = activeInk;
        titleBar.ButtonBackgroundColor = surface;
        titleBar.ButtonForegroundColor = activeInk;

        titleBar.ButtonHoverBackgroundColor = system.HighContrast
            ? system.Highlight
            : theme.IsDark || onScrim
                ? Color.FromArgb(35, 255, 255, 255)
                : Color.FromArgb(25, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = system.HighContrast
            ? system.Highlight
            : theme.IsDark || onScrim
                ? Color.FromArgb(55, 255, 255, 255)
                : Color.FromArgb(45, 0, 0, 0);
        titleBar.ButtonHoverForegroundColor = system.HighContrast ? system.HighlightText : activeInk;
        titleBar.ButtonPressedForegroundColor = titleBar.ButtonHoverForegroundColor;

        titleBar.InactiveBackgroundColor = surface;
        titleBar.ButtonInactiveBackgroundColor = surface;

        var inactiveInk = dimInactive
            ? system.HighContrast
                ? system.GrayText
                : Color.FromArgb(0x99, activeInk.R, activeInk.G, activeInk.B)
            : activeInk;
        titleBar.InactiveForegroundColor = inactiveInk;
        titleBar.ButtonInactiveForegroundColor = inactiveInk;
    }

    internal static uint ToColorRef(Color color) =>
        (uint)(color.R | (color.G << 8) | (color.B << 16));

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }
}
