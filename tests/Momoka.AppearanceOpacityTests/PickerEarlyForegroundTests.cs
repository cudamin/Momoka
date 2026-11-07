using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
        check("详情文字恢复默认：亮暗底色、材质切换与系统前景不替换画刷", () =>
        {
            var method = typeof(ControlBackgroundBrushes).GetMethod("ApplyDefaultForeground",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (method is null) throw new InvalidOperationException("缺少详情默认前景入口");
            var normal = new SolidColorBrush { Color = 0xFFF3F5F8u };
            Microsoft.UI.Xaml.Application.Current.Resources["EgDetailActionTextBrush"] = normal;
            foreach (var control in new Microsoft.UI.Xaml.Controls.Control[]
                { new Button(), new ToggleButton(), new ComboBox() })
            {
                var backgrounds = ControlBackgroundBrushes.ForDetail(control, lightSurface: true);
                var captured = control is ComboBox ? Keys.Select(key => (SolidColorBrush)control.Resources[key]).ToArray() : [];
                foreach (var color in new Windows.UI.Color[] { 0xFFFFFFFFu, 0xFF000000u })
                    foreach (var glass in new[] { false, true })
                    {
                        backgrounds.Apply(100, glass, color);
                        method.Invoke(backgrounds, [null]);
                        if (control.Foreground is not SolidColorBrush ink || ink.Color != normal.Color
                            || captured.Any(brush => brush.Color != normal.Color))
                            throw new InvalidOperationException("详情字色仍跟背景变化");
                    }
                normal.Color = 0xFFF1EDF8u;
                method.Invoke(backgrounds, [null]);
                if (((SolidColorBrush)control.Foreground!).Color != normal.Color)
                    throw new InvalidOperationException("默认资源更新后保留旧前景");
                method.Invoke(backgrounds, [(Windows.UI.Color?)0xFF123456u]);
                if (((SolidColorBrush)control.Foreground!).Color != (Windows.UI.Color)0xFF123456u
                    || captured.Any(brush => brush.Color != (Windows.UI.Color)0xFF123456u))
                    throw new InvalidOperationException("系统前景没有优先");
                if (control is ComboBox && Keys.Where((key, index) => !ReferenceEquals(control.Resources[key], captured[index])).Any())
                    throw new InvalidOperationException("替换了模板已捕获的前景");
            }
        });
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
