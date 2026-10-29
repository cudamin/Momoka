using Momoka.Theming;

namespace Momoka.Shell;

/// <summary>
/// 输入区域的底色规则（2026-10-29 改为输入玻璃）：
/// 选择框的闭合态全用 <c>EgInputGlassBrush</c>；文本输入的静止态由设置页卡片区的滚动资源
/// 局部接到同一支玻璃，聚焦态仍回窗口/侧边栏底色。
/// 聚焦与悬停只加强中性边线；选择文字、开关与下拉选中项仍保留强调色。
/// </summary>
internal static class InputControlTheme
{
    /// <summary>输入玻璃的 tint：深色抬两档、浅色凹一档，Palette 两份字典的浓度各自配。</summary>
    public static ThemeColor Glass(UiTheme theme) =>
        theme.IsDark ? theme.Colors.SurfaceHover : theme.Colors.SurfaceAlt;

    public static IReadOnlyList<(string Key, Func<UiTheme, ThemeColor> Pick)> Brushes { get; } =
    [
        ("EgInputGlassBrush", Glass),
        ("ComboBoxBackground", Glass),
        ("ComboBoxBackgroundPointerOver", Glass),
        ("ComboBoxBackgroundPressed", Glass),
        ("ComboBoxBackgroundFocused", Glass),
        ("ComboBoxBackgroundUnfocused", Glass),
        ("ComboBoxBackgroundDisabled", Glass),
        ("ComboBoxBackgroundBorderBrushFocused", theme => theme.Colors.BorderStrong),
        ("TextControlBackground", theme => theme.Colors.Window),
        ("TextControlBackgroundPointerOver", theme => theme.Colors.Window),
        ("TextControlBackgroundFocused", theme => theme.Colors.Window),
        ("TextControlBackgroundDisabled", theme => theme.Colors.Window),
        ("TextControlBorderBrush", theme => theme.Colors.Border),
        ("TextControlBorderBrushPointerOver", theme => theme.Colors.BorderStrong),
        ("TextControlBorderBrushFocused", theme => theme.Colors.BorderStrong),
        ("TextControlBorderBrushDisabled", theme => theme.Colors.Border)
    ];
}
