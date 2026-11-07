using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Views;

internal static class ControlTintTests
{
    internal static void Run()
    {
        var apply = typeof(ControlBackgroundBrushes).GetMethod("Apply",
            [typeof(double), typeof(bool), typeof(Windows.UI.Color?), typeof(double), typeof(double)]);
        if (apply is null) throw new InvalidOperationException("控件没有自选颜色入口");
        foreach (var key in new[] { "EgDetailActionBrush", "EgDetailActionHoverBrush", "EgDetailActionPressedBrush" })
            Application.Current.Resources[key] = new SolidColorBrush
            {
                Color = key switch
                {
                    "EgDetailActionHoverBrush" => 0x15FFFFFF,
                    "EgDetailActionPressedBrush" => 0x08FFFFFF,
                    _ => 0x0FFFFFFF
                }
            };
        foreach (var control in new Control[] { new Button(), new ComboBox(), new ToggleButton() })
        {
            var prefix = control is ComboBox ? "ComboBox" : control is ToggleButton ? "ToggleButton" : "Button";
            var backgrounds = ControlBackgroundBrushes.ForDetail(control, lightSurface: true);
            foreach (var color in new Windows.UI.Color[] { 0xFF78909C, 0xFFFFFFFF, 0xFF000000 })
                foreach (var acrylic in new[] { false, true })
                    foreach (var percent in new[] { 0d, 14.71, 100d })
                    {
                        apply.Invoke(backgrounds, [percent, acrylic, color, 0.37, 0.42]);
                        foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
                        {
                            var brush = (AcrylicBrush)control.Resources[prefix + "Background" + suffix];
                            if (brush.TintColor != color || brush.FallbackColor != color)
                                throw new InvalidOperationException(prefix + suffix + " 没有应用自选颜色");
                            if (acrylic)
                            {
                                // 亚克力只认三项：颜色、染色浓度（悬停/按下各 +6/+12 个百分点）与明暗浓度。
                                var expected = 0.37 + suffix switch { "PointerOver" => 0.06, "Pressed" => 0.12, _ => 0 };
                                if (Math.Abs(brush.TintOpacity - expected) > 0.000001)
                                    throw new InvalidOperationException(prefix + suffix + " 染色浓度不对：" + brush.TintOpacity);
                                if (Math.Abs((brush.TintLuminosityOpacity ?? -1) - 0.42) > 0.000001)
                                    throw new InvalidOperationException(prefix + suffix + " 明暗浓度不对");
                                if (brush.AlwaysUseFallback || brush.Opacity != 1)
                                    throw new InvalidOperationException(prefix + suffix + " 亚克力提前回退或整层透明");
                            }
                            else
                            {
                                if (percent == 0 && suffix.Length == 0 && brush.Opacity != 0)
                                    throw new InvalidOperationException("自选颜色零档不是透明");
                                if (percent == 100 && (brush.Opacity != 1 || !brush.AlwaysUseFallback))
                                    throw new InvalidOperationException("自选颜色满档不是实底");
                            }
                        }
                    }
            var white = Momoka.Theming.ThemeColor.Rgb(255, 255, 255);
            backgrounds.Apply(100, false, 0xFFFFFFFF);
            backgrounds.ApplyForeground(white, false);
            var firstInk = control.Foreground;
            if (((SolidColorBrush)firstInk!).Color != (Windows.UI.Color)0xFF161616)
                throw new InvalidOperationException("白色实底文字未改成深色");
            backgrounds.Apply(100, false, 0xFF000000);
            backgrounds.ApplyForeground(white, false);
            if (((SolidColorBrush)control.Foreground!).Color != (Windows.UI.Color)0xFFFFFFFF
                || !ReferenceEquals(firstInk, control.Foreground))
                throw new InvalidOperationException("黑色实底文字或前景对象不稳定");
            backgrounds.ApplyForeground(white, false, 0xFF123456);
            if (((SolidColorBrush)control.Foreground!).Color != (Windows.UI.Color)0xFF123456)
                throw new InvalidOperationException("系统高对比前景未优先");
            if (control is ComboBox && control.Resources.ContainsKey("ComboBoxDropDownForeground"))
                throw new InvalidOperationException("闭合选择框覆盖展开列表前景");
        }
    }
}
