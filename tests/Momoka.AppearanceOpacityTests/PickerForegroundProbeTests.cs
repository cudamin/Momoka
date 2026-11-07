using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell;
using Momoka.Shell.Views;
using Momoka.Theming;

internal static class PickerForegroundProbeTests
{
    internal static void Run(Action<string, Action> check)
    {
        foreach (var light in new[] { false, true })
            check("选择框前景探针核对实际" + (light ? "亮" : "暗") + "底并拒绝相反色", () =>
            {
                var background = ThemeColor.Rgb(light ? (byte)255 : (byte)0, light ? (byte)255 : (byte)0, light ? (byte)255 : (byte)0);
                var picker = new ComboBox();
                var brushes = ControlBackgroundBrushes.ForDetail(picker, lightSurface: true);
                brushes.Apply(0);
                brushes.ApplyForeground(background, false);
                var fill = (AcrylicBrush)picker.Resources["ComboBoxBackground"];
                Require(ShellNavigationProbe.PickerForegroundMatches(picker.Foreground, background, fill, false), "实际生产前景被探针拒绝");
                var expected = BackdropReading.Foreground(background);
                var observed = ((SolidColorBrush)picker.Foreground!).Color;
                Require(observed.R == expected.R && observed.G == expected.G && observed.B == expected.B, "生产前景没有按明暗背景选择");
                var wrong = new SolidColorBrush { Color = light ? 0xFFFFFFFFu : 0xFF161616u };
                Require(!ShellNavigationProbe.PickerForegroundMatches(wrong, background, fill, false), "探针放行了相反前景");
            });
        check("选择框前景探针遵守实际材质回退和强制系统色", () =>
        {
            var picker = new ComboBox();
            var brushes = ControlBackgroundBrushes.ForDetail(picker, lightSurface: true);
            var background = ThemeColor.Rgb(255, 255, 255);
            brushes.Apply(25, true, 0xFF000000u);
            foreach (var fallback in new[] { false, true })
            {
                brushes.ApplyForeground(background, fallback);
                Require(ShellNavigationProbe.PickerForegroundMatches(picker.Foreground, background,
                    (AcrylicBrush)picker.Resources["ComboBoxBackground"], fallback), "材质回退的生产前景被拒绝");
            }
            Windows.UI.Color system = 0xFF00FF00u;
            brushes.ApplyForeground(background, true, system);
            var fill = (AcrylicBrush)picker.Resources["ComboBoxBackground"];
            Require(ShellNavigationProbe.PickerForegroundMatches(picker.Foreground, background, fill, true, system), "系统前景被拒绝");
            Require(!ShellNavigationProbe.PickerForegroundMatches(new SolidColorBrush { Color = 0xFFFFFFFFu },
                background, fill, true, system), "强制系统色时仍放行固定白色");
        });
        check("主页材质探针以非零验亚克力再逐态核对零档", () =>
        {
            var button = new Button();
            var resources = new Microsoft.UI.Xaml.ResourceDictionary
            {
                ["EgBannerGhostRestBrush"] = new SolidColorBrush { Color = 0x0FFFFFFFu },
                ["EgBannerGhostHoverBrush"] = new SolidColorBrush { Color = 0x15FFFFFFu },
                ["EgBannerGhostPressBrush"] = new SolidColorBrush { Color = 0x08FFFFFFu }
            };
            var backgrounds = ControlBackgroundBrushes.ForBanner(button, resources);
            backgrounds.Apply(37, true);
            foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
            {
                var fill = (AcrylicBrush)button.Resources["ButtonBackground" + suffix];
                Require(!fill.AlwaysUseFallback && fill.Opacity == 1, "非零材质未开启");
            }
            backgrounds.Apply(0, true);
            foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
            {
                var fill = (AcrylicBrush)button.Resources["ButtonBackground" + suffix];
                Require(fill.AlwaysUseFallback && (suffix.Length == 0 ? fill.Opacity == 0
                    : fill.Opacity * fill.FallbackColor.A / 255d > 0), "零档没有关闭常态或保留反馈");
                Require(!(fill is { AlwaysUseFallback: false, Opacity: 1 }), "旧零档启用亚克力判据仍被满足");
            }
        });
        check("高斯诊断保留实际代次半径与布局变化而不判成通过", () =>
        {
            var before = new GaussianProbeReading(11, 7, true, false, 1100, false, true, true, true);
            var after = before with { Radius = 16, Generation = 8, RequestedGaussian = false, Rendering = true, PageWidth = 760, Compact = true, Showcase = false, Ready = false, Applied = false };
            Require(before != after && before.ToString() != after.ToString(), "诊断丢失了实际变化");
            foreach (var fact in new[] { "radius=16", "generation=8", "requested=False", "rendering=True", "width=760", "compact=True", "showcase=False", "ready=False", "applied=False" })
                Require(after.ToString().Contains(fact, StringComparison.Ordinal), "诊断缺少实际字段：" + fact);
        });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
