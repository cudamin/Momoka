using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

/// <summary>
/// 每态固定一支材质画刷：现有底色走精确实色回退，亚克力走原生材质。文字、图标、边线与弹层沿用原有状态。
/// 框架 2.3.9 的 DefaultButtonStyle/DefaultToggleButtonStyle 在三种状态分别读取这些资源键，
/// 所以必须提供整组三态，不能只给 Background 填一支刷子。
/// <para>
/// 滑块的读数就是底色自己的不透明度（2026-11-06 用户令「0 改为完全透明，100 改为完全不透明」）：0 档常态什么
/// 都不画，悬停与按下仍留原来那层薄白 —— 那是指针的手感，不是要关掉的那块板。
/// 详情操作按钮与选择框使用中性灰底、白字；常态按滑块读数，悬停与按下增加少量反馈。
/// </para>
/// </summary>
internal sealed class ControlBackgroundBrushes
{
    private readonly List<(SolidColorBrush Source, SolidColorBrush Target, AcrylicBrush Copy, bool Feedback)> _brushes = [];
    private readonly bool _preserveDefault;
    private readonly bool _lightSurface;

    private ControlBackgroundBrushes(Control control, string prefix,
        SolidColorBrush normal, SolidColorBrush hover, SolidColorBrush pressed, SolidColorBrush disabled,
        bool preserveDefault = false, bool lightSurface = false)
    {
        _preserveDefault = preserveDefault;
        _lightSurface = lightSurface;
        var rest = Copy(normal, lightSurface ? "EgDetailActionBrush" : "EgSurfaceElevatedBrush", feedback: false);
        var over = Copy(hover, lightSurface ? "EgDetailActionHoverBrush" : "EgSurfaceHoverBrush", feedback: true);
        var down = Copy(pressed, lightSurface ? "EgDetailActionPressedBrush" : "EgSurfaceAltBrush", feedback: true);
        var off = Copy(disabled, "EgSurfaceElevatedBrush", feedback: false);
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

    public static ControlBackgroundBrushes ForDetail(Control control, bool preserveDefault = false, bool lightSurface = false)
    {
        var prefix = control is ComboBox ? "ComboBox" : control is ToggleButton ? "ToggleButton" : "Button";
        var source = control is ToggleButton ? "ToggleButton" : "Button";
        return new(control, prefix, Resource(lightSurface ? "EgDetailActionBrush" : source + "Background"),
            Resource(lightSurface ? "EgDetailActionHoverBrush" : source + "BackgroundPointerOver"),
            Resource(lightSurface ? "EgDetailActionPressedBrush" : source + "BackgroundPressed"),
            Resource(source + "BackgroundDisabled"), preserveDefault, lightSurface);
    }

    public static ControlBackgroundBrushes ForBanner(Button button, ResourceDictionary resources) =>
        new(button, "Button", (SolidColorBrush)resources["EgBannerGhostRestBrush"],
            (SolidColorBrush)resources["EgBannerGhostHoverBrush"], (SolidColorBrush)resources["EgBannerGhostPressBrush"],
            Resource("ButtonBackgroundDisabled"));

    public void Apply(double percent, bool useAcrylic = false)
    {
        var amount = AppearanceOpacity.ControlBackground(percent);
        foreach (var (source, target, copy, feedback) in _brushes)
        {
            if (_lightSurface)
            {
                // 颜色与浓度分开：不能为了浅色面钉实底或72%的下限。零与满档按精确实色处理。
                var feedbackAmount = ReferenceEquals(source, Resource("EgDetailActionPressedBrush")) ? 0.12 : 0.06;
                var strength = Math.Min(1, amount + (feedback ? feedbackAmount : 0));
                copy.TintColor = target.Color;
                copy.FallbackColor = target.Color;
                copy.TintOpacity = strength;
                copy.TintLuminosityOpacity = 0;
                copy.Opacity = useAcrylic && strength is > 0 and < 1 ? 1 : strength;
                copy.AlwaysUseFallback = !useAcrylic || strength is 0 or 1;
                continue;
            }
            if (useAcrylic && amount < 1)
            {
                // 亚克力跟着同一档浓度：0 档是一块什么都不画的玻璃，向右补到 100 的实底（由下面那条路接手）。
                // 画刷本身保持不透明，系统关闭透明效果时 FallbackColor 仍是一块可读的主题表面。
                copy.TintColor = target.Color;
                copy.FallbackColor = target.Color;
                copy.TintOpacity = amount;
                copy.TintLuminosityOpacity = amount;
                copy.Opacity = 1;
                copy.AlwaysUseFallback = false;
                continue;
            }
            var color = source.Color;
            // 原底色那一层薄白的有效浓度 —— 悬停与按下在它以下时仍然是那层白，不是一块淡掉的深色板。
            var floor = feedback || _preserveDefault ? color.A / 255d * source.Opacity : 0;
            // 详情操作键恢复 v0.2.1 的平台半透明底；用户提高浓度时继续走现有表面色，亚克力仍由上方分支处理。
            if (_preserveDefault && amount <= floor)
            {
                copy.FallbackColor = color;
                copy.Opacity = source.Opacity;
                copy.AlwaysUseFallback = true;
                continue;
            }
            if (amount == 0)
            {
                // 0 档常态没有底色。悬停与按下的副本仍留着原来那层薄白：指到按钮上要看得见反应。
                var clear = color;
                clear.A = 0;
                copy.FallbackColor = feedback ? color : clear;
                copy.Opacity = feedback ? source.Opacity : 0;
                copy.AlwaysUseFallback = true;
                continue;
            }
            if (amount <= floor)
            {
                // 滑块还没爬过原底色那一档：颜色和浓度照旧，免得悬停手感在零档附近忽然断掉。
                copy.FallbackColor = color;
                copy.Opacity = source.Opacity;
                copy.AlwaysUseFallback = true;
                continue;
            }
            // 底色就是主题那一档表面色，浓度直接等于百分比。旧版把原底色的 alpha 当起点往上加，
            // 于是 1% 一上来就是一层薄白、读数说不清；2026-11-06 起读数就是屏上的浓度。
            var surface = target.Color;
            surface.A = byte.MaxValue;
            copy.FallbackColor = surface;
            copy.Opacity = amount;
            copy.AlwaysUseFallback = true;
            if (amount == 1)
            {
                // 极近 100 的小数档可能已经被平台按 alpha 舍入成实底；先跨过不透明状态，
                // 再落到完全不透明，确保结束平台尚在执行的 fallback 过渡。
                copy.TintOpacity = 0;
                copy.TintColor = surface;
                copy.TintLuminosityOpacity = 1;
                copy.TintOpacity = 1;
            }
        }
    }

    private AcrylicBrush Copy(SolidColorBrush source, string targetKey, bool feedback)
    {
        var copy = new AcrylicBrush
        {
            FallbackColor = source.Color,
            Opacity = source.Opacity,
            AlwaysUseFallback = true,
            TintTransitionDuration = TimeSpan.Zero
        };
        _brushes.Add((source, Resource(targetKey), copy, feedback));
        return copy;
    }

    private static SolidColorBrush Resource(string key) => (SolidColorBrush)Application.Current.Resources[key];

}
