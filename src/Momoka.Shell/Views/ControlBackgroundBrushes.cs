using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

/// <summary>
/// 控件自己的底色副本。以主题深色表面补足不透明度，文字、图标、边线与弹层沿用控件的原有状态。
/// 框架 2.3.9 的 DefaultButtonStyle/DefaultToggleButtonStyle 在三种状态分别读取这些资源键，
/// 所以必须提供整组三态，不能只给 Background 填一支刷子。
/// </summary>
internal sealed class ControlBackgroundBrushes
{
    private readonly List<(SolidColorBrush Source, SolidColorBrush Target, SolidColorBrush Copy)> _brushes = [];

    private ControlBackgroundBrushes(Control control, string prefix,
        SolidColorBrush normal, SolidColorBrush hover, SolidColorBrush pressed, SolidColorBrush disabled)
    {
        var rest = Copy(normal, "EgSurfaceElevatedBrush");
        var over = Copy(hover, "EgSurfaceHoverBrush");
        var down = Copy(pressed, "EgSurfaceAltBrush");
        var off = Copy(disabled, "EgSurfaceElevatedBrush");
        // XAML 不声明下面这些键；此处仅在构造时新增一次，以后只修改画刷。
        // WinUI 的别名重写按对象移除，可能报 0x800F0902；资源枚举也不能作为别名复制依据。
        // 保留原字典与所有前景资源，避免重写、移除或枚举已经存在的别名。
        var resources = control.Resources;
        resources[prefix + "Background"] = rest;
        resources[prefix + "BackgroundPointerOver"] = over;
        resources[prefix + "BackgroundPressed"] = down;
        resources[prefix + "BackgroundDisabled"] = off;
        if (control is ComboBox)
        {
            resources[prefix + "BackgroundFocused"] = rest;
            resources[prefix + "BackgroundUnfocused"] = rest;
        }
        if (control is ToggleButton)
        {
            resources[prefix + "BackgroundChecked"] = rest;
            resources[prefix + "BackgroundCheckedPointerOver"] = over;
            resources[prefix + "BackgroundCheckedPressed"] = down;
            resources[prefix + "BackgroundCheckedDisabled"] = off;
        }
        control.Background = rest;
    }

    public static ControlBackgroundBrushes ForDetail(Control control)
    {
        var prefix = control is ComboBox ? "ComboBox" : control is ToggleButton ? "ToggleButton" : "Button";
        var source = control is ToggleButton ? "ToggleButton" : "Button";
        return new(control, prefix, Resource(source + "Background"), Resource(source + "BackgroundPointerOver"),
            Resource(source + "BackgroundPressed"), Resource(source + "BackgroundDisabled"));
    }

    public static ControlBackgroundBrushes ForBanner(Button button, ResourceDictionary resources) =>
        new(button, "Button", (SolidColorBrush)resources["EgBannerGhostRestBrush"],
            (SolidColorBrush)resources["EgBannerGhostHoverBrush"], (SolidColorBrush)resources["EgBannerGhostPressBrush"],
            Resource("ButtonBackgroundDisabled"));

    public void Apply(double percent)
    {
        var amount = AppearanceOpacity.ControlBackground(percent);
        foreach (var (source, target, copy) in _brushes)
        {
            var color = source.Color;
            if (amount == 0)
            {
                copy.Color = color;
                copy.Opacity = source.Opacity;
                continue;
            }
            // 原底色常为一层薄白，直接变浓会成白块白字。补上的不透明层用当前主题的深色表面，
            // 按各层有效 alpha 混色；趋近零档时仍连续，100 档既完全不透明也保留三态区别。
            var baseline = color.A / 255d * source.Opacity;
            var added = (1 - baseline) * amount;
            var opacity = baseline + added;
            var surface = target.Color;
            color.R = Channel(color.R, surface.R, baseline, added, opacity);
            color.G = Channel(color.G, surface.G, baseline, added, opacity);
            color.B = Channel(color.B, surface.B, baseline, added, opacity);
            color.A = byte.MaxValue;
            copy.Color = color;
            copy.Opacity = opacity;
        }
    }

    private SolidColorBrush Copy(SolidColorBrush source, string targetKey)
    {
        var copy = new SolidColorBrush { Color = source.Color, Opacity = source.Opacity };
        _brushes.Add((source, Resource(targetKey), copy));
        return copy;
    }

    private static SolidColorBrush Resource(string key) => (SolidColorBrush)Application.Current.Resources[key];

    private static byte Channel(byte source, byte target, double baseline, double added, double opacity) =>
        (byte)Math.Clamp(Math.Round((source * baseline + target * added) / opacity), 0, 255);

}
