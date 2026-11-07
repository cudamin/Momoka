using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Views;
using Momoka.Theming;

internal static class PickerEarlyForegroundTests
{
    private static readonly string[] Keys =
    [
        "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundPressed",
        "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed",
        "ComboBoxDropDownGlyphForeground", "ComboBoxDropDownGlyphForegroundFocused", "ComboBoxDropDownGlyphForegroundFocusedPressed"
    ];

    internal static void Run(Action<string, Action> check)
    {
        check("选择框模板创建前已有独立前景键，后续只改同一画刷", () =>
        {
            var picker = new ComboBox { Foreground = new SolidColorBrush { Color = 0xFFF2F6FAu } };
            var backgrounds = ControlBackgroundBrushes.ForDetail(picker, lightSurface: true);
            var captured = Keys.Select(key => picker.Resources.TryGetValue(key, out var value) ? value as SolidColorBrush : null).ToArray();
            if (captured.Any(brush => brush is null)) throw new InvalidOperationException("模板捕获时仍缺前景键");
            if (captured.Distinct().Count() != Keys.Length) throw new InvalidOperationException("前景键不是独立对象");
            var count = picker.Resources.Count;
            foreach (var color in new[] { ThemeColor.Rgb(255, 255, 255), ThemeColor.Rgb(0, 0, 0) })
            {
                backgrounds.Apply(0);
                backgrounds.ApplyForeground(color, false);
                var expected = BackdropReading.Foreground(color);
                foreach (var brush in captured)
                    if (brush!.Color.R != expected.R || brush.Color.G != expected.G || brush.Color.B != expected.B)
                        throw new InvalidOperationException("模板捕获的画刷没有跟随实际亮暗底更新");
                if (picker.Resources.Count != count || Keys.Where((key, index) => !ReferenceEquals(picker.Resources[key], captured[index])).Any())
                    throw new InvalidOperationException("Loaded后插入或替换前景键");
            }
            Windows.UI.Color system = 0xFF123456u;
            backgrounds.ApplyForeground(ThemeColor.Rgb(255, 255, 255), true, system);
            if (captured.Any(brush => brush!.Color != system)) throw new InvalidOperationException("系统前景没有更新已捕获对象");
            if (picker.Resources.ContainsKey("ComboBoxDropDownForeground") || picker.Resources.ContainsKey("ComboBoxItemForeground"))
                throw new InvalidOperationException("闭合前景修改了展开列表");
        });
        check("晚到前景键旧边界使模板仍握旧刷必须拒绝", () =>
        {
            var picker = new ComboBox { Foreground = new SolidColorBrush { Color = 0xFFFFFFFFu } };
            var backgrounds = ControlBackgroundBrushes.ForDetail(picker, lightSurface: true);
            var captured = (SolidColorBrush)picker.Resources["ComboBoxDropDownGlyphForeground"];
            picker.Resources["ComboBoxDropDownGlyphForeground"] = new SolidColorBrush { Color = 0xFFFFFFFFu };
            backgrounds.Apply(0);
            backgrounds.ApplyForeground(ThemeColor.Rgb(255, 255, 255), false);
            if (ReferenceEquals(captured, picker.Resources["ComboBoxDropDownGlyphForeground"]))
                throw new InvalidOperationException("旧模板引用反例没有形成");
            if (((SolidColorBrush)picker.Resources["ComboBoxDropDownGlyphForeground"]).Color == captured.Color)
                throw new InvalidOperationException("错误资源引用被伪装成已更新");
        });
    }
}
