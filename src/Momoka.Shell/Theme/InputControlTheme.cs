using Momoka.Theming;

namespace Momoka.Shell;

/// <summary>
/// 输入区域沿用页面与侧边栏的窗口底色，不在卡片里再叠一档底色。
/// 聚焦与悬停只加强中性边线；选择文字、开关与下拉选中项仍保留强调色。
/// </summary>
internal static class InputControlTheme
{
    public static IReadOnlyList<(string Key, Func<UiTheme, ThemeColor> Pick)> Brushes { get; } =
    [
        ("ComboBoxBackground", theme => theme.Colors.Window),
        ("ComboBoxBackgroundPointerOver", theme => theme.Colors.Window),
        ("ComboBoxBackgroundPressed", theme => theme.Colors.Window),
        ("ComboBoxBackgroundFocused", theme => theme.Colors.Window),
        ("ComboBoxBackgroundUnfocused", theme => theme.Colors.Window),
        ("ComboBoxBackgroundDisabled", theme => theme.Colors.Window),
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
