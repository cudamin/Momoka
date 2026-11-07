using Momoka.Emby;
using Momoka.Shell;
using Momoka.Shell.Views;
using Momoka.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Xml.Linq;

var passed = 0;
var failed = 0;

Check("外观颜色预览与其他设置保存隔离，清除后恢复真实设置", () =>
{
    var ui = new UiSettings();
    var settingType = typeof(UiSettings);
    var control = settingType.GetProperty("ControlBackgroundColor");
    if (control is null) throw new InvalidOperationException("缺少真实颜色设置键");
    var preview = typeof(ShellPrefs).GetMethod("SetAppearanceColorPreview");
    var read = typeof(ShellPrefs).GetMethod("AppearanceColor");
    if (preview is null || read is null) throw new InvalidOperationException("缺少独立预览接口");
    preview.Invoke(null, new object?[] { false, "#123456", ui });
    try
    {
        if (((Momoka.Theming.ThemeColor)read.Invoke(null, new object[] { false, ui })!).ToHex() != "#FF123456")
            throw new InvalidOperationException("预览颜色未生效");
        if ((string)control.GetValue(ui)! != "#FFFFFF") throw new InvalidOperationException("预览修改了活设置");
        var saved = System.Text.Json.JsonSerializer.Serialize(new AppSettings { Ui = ui });
        if (saved.Contains("#123456", StringComparison.Ordinal)) throw new InvalidOperationException("其他保存写入预览颜色");
        if (((Momoka.Theming.ThemeColor)read.Invoke(null, new object[] { false, new UiSettings() })!).ToHex() != "#FFFFFFFF")
            throw new InvalidOperationException("预览泄漏到其他设置拥有者");
    }
    finally { preview.Invoke(null, new object?[] { false, null, ui }); }
    if (((Momoka.Theming.ThemeColor)read.Invoke(null, new object[] { false, ui })!).ToHex() != "#FFFFFFFF")
        throw new InvalidOperationException("取消未恢复真实颜色");
});
Check("零档没有按钮底色", () => Equal(0, AppearanceOpacity.ControlBackground(0)));
Check("按钮浓度就是百分比", () => Equal(0.45, AppearanceOpacity.ControlBackground(45)));
Check("按钮浓度下限", () => Equal(0, AppearanceOpacity.ControlBackground(-1)));
Check("按钮浓度上限", () => Equal(1, AppearanceOpacity.ControlBackground(101)));
Check("非有限按钮值回到零档", () =>
{
    foreach (var value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        Equal(0, AppearanceOpacity.ControlBackground(value));
});
Check("零档背景强度是不遮不糊", () => Equal(0, AppearanceOpacity.Backdrop(0)));
Check("出厂四十档强度是四成", () => Equal(0.4, AppearanceOpacity.Backdrop(UiSettings.DefaultBackdropOpacity)));
Check("背景强度就是百分比", () => Equal(0.45, AppearanceOpacity.Backdrop(45)));
Check("满档背景强度拉满", () =>
{
    Equal(1, AppearanceOpacity.Backdrop(100));
    Equal(1, AppearanceOpacity.Backdrop(140));
});
Check("非有限背景值回到零档", () =>
{
    foreach (var value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        Equal(0, AppearanceOpacity.Backdrop(value));
});
Check("遮罩强度归一到老观感那一档", () =>
{
    // 表上最浓的 0xB8 就是老版写死的浓度：强度 184/255 时它原样画出来。
    Equal((byte)0xB8, AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, DetailHero.ScrimCeiling / 255d));
});
Check("遮罩强度 100 完全不透明", () => Equal(byte.MaxValue, AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, 1)));
Check("遮罩强度 0 一点不遮", () => Equal((byte)0, AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, 0)));
Check("遮罩表的浅档按同一比例", () =>
{
    var share = DetailHero.ScrimCeiling / 255d;
    foreach (var (_, alpha) in DetailHero.ScrimStops)
        Equal(alpha, AppearanceOpacity.ScrimAlpha(alpha, share));
    Equal((byte)0, AppearanceOpacity.ScrimAlpha(0, 1));
});
Check("遮罩强度线性：45 是满档的四成半", () =>
{
    var full = AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, 1);
    var at45 = AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, 0.45);
    Equal((byte)Math.Round(full * 0.45), at45);
});
Check("非有限遮罩强度按零档", () =>
{
    foreach (var value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        Equal((byte)0, AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, value));
    Equal((byte)0, AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, -1));
    Equal(byte.MaxValue, AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, 2));
});
Check("高斯模糊强度换算：0 不糊、100 最糊", () =>
{
    Equal(0, GaussianBackdrop.SigmaFor(0));
    Equal(GaussianBackdrop.MaxSigma, GaussianBackdrop.SigmaFor(1));
    Equal(GaussianBackdrop.MaxSigma / 2, GaussianBackdrop.SigmaFor(0.5));
    Equal(0, GaussianBackdrop.SigmaFor(double.NaN));
    Equal(0, GaussianBackdrop.SigmaFor(-1));
    Equal(GaussianBackdrop.MaxSigma, GaussianBackdrop.SigmaFor(3));
});
Check("高斯轻罩随强度线性变化并保留图片", () =>
{
    foreach (var (strength, alpha) in new[] { (0.01, 1), (0.15, 13), (0.4, 36), (1d, 89) })
        Equal(alpha, AppearanceOpacity.ReadingShadeAlpha(strength, true));
});
Check("高斯轻罩边界与普通遮罩保持各自语义", () =>
{
    Equal(0, AppearanceOpacity.ReadingShadeAlpha(0, true));
    Equal(0, AppearanceOpacity.ReadingShadeAlpha(-1, true));
    Equal(89, AppearanceOpacity.ReadingShadeAlpha(2, true));
    foreach (var strength in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        Equal(0, AppearanceOpacity.ReadingShadeAlpha(strength, true));
    Equal(102, AppearanceOpacity.ReadingShadeAlpha(0.4, false));
    Equal(255, AppearanceOpacity.ReadingShadeAlpha(1, false));
});
Check("出厂四十档的模糊比老版默认略轻", () =>
    Equal((int)Math.Round(GaussianBackdrop.MaxSigma * 0.4),
        GaussianBackdrop.SigmaFor(UiSettings.DefaultBackdropOpacity / 100)));
Check("零档主页图片保留原始浓度", () => Equal(1, AppearanceOpacity.HomeBackdrop(0)));
Check("主页图片本已不透明继续保留", () => Equal(1, AppearanceOpacity.HomeBackdrop(45)));
Check("主页图片各档与无效值都保持不透明", () =>
{
    Equal(1, AppearanceOpacity.HomeBackdrop(-1));
    Equal(1, AppearanceOpacity.HomeBackdrop(100));
    Equal(1, AppearanceOpacity.HomeBackdrop(double.NaN));
});

var resources = Application.Current.Resources;
foreach (var prefix in new[] { "Button", "ToggleButton" })
{
    resources[prefix + "Background"] = new SolidColorBrush { Color = 0x20FFFFFF, Opacity = 0.8 };
    resources[prefix + "BackgroundPointerOver"] = new SolidColorBrush { Color = 0x38FFFFFF, Opacity = 0.9 };
    resources[prefix + "BackgroundPressed"] = new SolidColorBrush { Color = 0x50FFFFFF, Opacity = 1 };
    resources[prefix + "BackgroundDisabled"] = new SolidColorBrush { Color = 0x10FFFFFF, Opacity = 0.3 };
}
resources["EgSurfaceElevatedBrush"] = new SolidColorBrush { Color = 0xFF20262D };
resources["EgSurfaceHoverBrush"] = new SolidColorBrush { Color = 0xFF2C343D };
resources["EgSurfaceAltBrush"] = new SolidColorBrush { Color = 0xFF1C252E };
resources["EgDetailActionBrush"] = new SolidColorBrush { Color = 0x0FFFFFFF };
resources["EgDetailActionHoverBrush"] = new SolidColorBrush { Color = 0x15FFFFFF };
resources["EgDetailActionPressedBrush"] = new SolidColorBrush { Color = 0x08FFFFFF };
foreach (var (control, prefix) in new (Control, string)[] { (new Button(), "Button"), (new ToggleButton(), "ToggleButton"), (new ComboBox(), "ComboBox") })
    Check(prefix + "详情默认薄白三态、可调浓度和选中态共底", () =>
    {
        var brushes = ControlBackgroundBrushes.ForDetail(control, lightSurface: true);
        var original = control.Background;
        foreach (var useAcrylic in new[] { false, true, false })
            foreach (var amount in new[] { 0d, 5.88, 15d / 255 * 100, 6d, 19d, 25d, 50d, 72d, 100d, 0d })
            {
                brushes.Apply(amount, useAcrylic);
                var keys = new[] { "EgDetailActionBrush", "EgDetailActionHoverBrush", "EgDetailActionPressedBrush" };
                var states = new[] { "", "PointerOver", "Pressed" };
                for (var index = 0; index < states.Length; index++)
                {
                    var brush = (AcrylicBrush)control.Resources[prefix + "Background" + states[index]];
                    var floor = new[] { 15d, 21d, 8d }[index] / 255;
                    var normal = 15d / 255;
                    var expected = index == 0 ? amount / 100 : amount / 100 <= normal
                        ? floor : floor + (1 - floor) * (amount / 100 - normal) / (1 - normal);
                    if (brush.FallbackColor != (Windows.UI.Color)0xFFFFFFFF)
                        throw new InvalidOperationException("详情控件未使用半透明白色叠层");
                    if (useAcrylic && expected is > 0 and < 1)
                    {
                        if (brush.AlwaysUseFallback) throw new InvalidOperationException("亚克力中间档使用实色");
                        Equal(expected, brush.TintOpacity);
                        Equal(0, brush.TintLuminosityOpacity ?? -1);
                    }
                    else
                    {
                        if (!brush.AlwaysUseFallback) throw new InvalidOperationException("零档或满档未按精确实色处理");
                        Equal(expected, brush.Opacity);
                    }
                    if (control is ToggleButton && !ReferenceEquals(brush, control.Resources[prefix + "BackgroundChecked" + states[index]]))
                        throw new InvalidOperationException("连播选中时未共用薄白底");
                }
                if (!ReferenceEquals(original, control.Background)) throw new InvalidOperationException("材质切换重建画刷");
                Equal(1, control.Opacity);
            }
    });
Check("延迟连播别名在原字典重写时能复现键冲突", () =>
{
    var dictionary = new ResourceDictionary();
    var replacement = new SolidColorBrush();
    dictionary.Defer("ToggleButtonBackgroundChecked", () => dictionary["ToggleButtonBackground"]);
    dictionary["ToggleButtonBackground"] = replacement;
    try
    {
        dictionary["ToggleButtonBackgroundChecked"] = replacement;
        throw new InvalidOperationException("边界替身没有复现实际资源键冲突");
    }
    catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x800F0902)) { }
});
Check("新增背景不枚举原资源且保留延迟前景与附属字典", () =>
{
    var control = new ToggleButton();
    var dictionary = control.Resources;
    var ink = new SolidColorBrush { Color = 0xFF12AB34 };
    dictionary.Defer("ToggleButtonForegroundChecked", () => ink);
    var merged = new ResourceDictionary { ["unrelated"] = "retained" };
    var theme = new ResourceDictionary { ["foreground"] = ink };
    dictionary.MergedDictionaries.Add(merged);
    dictionary.ThemeDictionaries.Add("Dark", theme);
    dictionary.RejectEnumeration = true;
    var brushes = ControlBackgroundBrushes.ForDetail(control);
    brushes.Apply(45);
    dictionary.RejectEnumeration = false;
    foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
    {
        var copy = (AcrylicBrush)control.Resources["ToggleButtonBackgroundChecked" + suffix];
        var source = (SolidColorBrush)resources["ToggleButtonBackground" + suffix];
        Increased(source, copy, 45, TargetFor(suffix));
    }
    if (!ReferenceEquals(control.Resources["ToggleButtonForegroundChecked"], ink))
        throw new InvalidOperationException("新增背景资源丢失了强调色前景");
    if (!ReferenceEquals(control.Resources.MergedDictionaries.Single(), merged)
        || !ReferenceEquals(control.Resources.ThemeDictionaries["Dark"], theme)
        || !ReferenceEquals(control.Resources, dictionary))
        throw new InvalidOperationException("新增背景不应替换控件原资源字典");
});
Check("XAML 不再声明代码负责的背景键", () =>
{
    var root = FindRoot();
    var detail = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
    var banner = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "HomeBanner.xaml"));
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    foreach (var toggle in detail.Descendants().Where(element => element.Name.LocalName == "ToggleButton"))
        if (toggle.Descendants().Any(element => ((string?)element.Attribute(x + "Key"))?.StartsWith("ToggleButtonBackground", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("连播仍保留延迟背景别名");
    var open = banner.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "OpenButton");
    if (open.Descendants().Any(element => ((string?)element.Attribute(x + "Key"))?.StartsWith("ButtonBackground", StringComparison.Ordinal) == true))
        throw new InvalidOperationException("主页详情仍保留延迟背景别名");
});
foreach (var (control, prefix) in new (Control, string)[]
    { (new Button(), "Button"), (new ToggleButton(), "ToggleButton"), (new ComboBox(), "ComboBox") })
{
    ControlBackgroundBrushes? brushes = null;
    Check($"{prefix} 底色副本按三态浓度取主题表面色且不修改控件浓度", () =>
    {
        brushes = ControlBackgroundBrushes.ForDetail(control);
        brushes.Apply(45);
        var sourcePrefix = control is ToggleButton ? "ToggleButton" : "Button";
        foreach (var suffix in new[] { "Background", "BackgroundPointerOver", "BackgroundPressed", "BackgroundDisabled" })
        {
            var source = (SolidColorBrush)resources[sourcePrefix + suffix];
            var copy = (AcrylicBrush)control.Resources[prefix + suffix];
            if (ReferenceEquals(source, copy)) throw new InvalidOperationException("局部底色没有独立复制");
            Increased(source, copy, 45, TargetFor(suffix["Background".Length..]));
        }
        Equal(1, control.Opacity);
        Equal(0.8, ((SolidColorBrush)resources[sourcePrefix + "Background"]).Opacity);
        if (!ReferenceEquals(control.Background, control.Resources[prefix + "Background"]))
            throw new InvalidOperationException("正常态没有使用局部画刷");
    });
    Check($"{prefix} 零档常态没有底色、悬停按下仍留薄白", () =>
    {
        var sourcePrefix = control is ToggleButton ? "ToggleButton" : "Button";
        brushes!.Apply(0);
        var rest = (AcrylicBrush)control.Resources[prefix + "Background"];
        Equal(0, rest.Opacity);
        if (rest.FallbackColor.A != 0) throw new InvalidOperationException("零档常态还画着底色");
        foreach (var suffix in new[] { "BackgroundPointerOver", "BackgroundPressed" })
        {
            var copy = (AcrylicBrush)control.Resources[prefix + suffix];
            var source = (SolidColorBrush)resources[sourcePrefix + suffix];
            Equal(source.Opacity, copy.Opacity);
            if (copy.FallbackColor != source.Color)
                throw new InvalidOperationException("零档丢了" + suffix + "的薄白反馈");
        }
        // 换主题：悬停那层薄白跟着源画刷走，常态仍然不画。
        var hover = (SolidColorBrush)resources[sourcePrefix + "BackgroundPointerOver"];
        hover.Color = 0x30112233;
        try
        {
            brushes.Apply(0);
            if (((AcrylicBrush)control.Resources[prefix + "BackgroundPointerOver"]).FallbackColor != hover.Color)
                throw new InvalidOperationException("切换主题后零档的反馈色仍是旧颜色");
            Equal(0, ((AcrylicBrush)control.Resources[prefix + "Background"]).Opacity);
        }
        finally { hover.Color = 0x38FFFFFF; }
    });
}
Check("选择框聚焦失焦沿用闭合底色", () =>
{
    var control = new ComboBox();
    ControlBackgroundBrushes.ForDetail(control).Apply(50);
    if (!ReferenceEquals(control.Background, control.Resources["ComboBoxBackgroundFocused"])
        || !ReferenceEquals(control.Background, control.Resources["ComboBoxBackgroundUnfocused"]))
        throw new InvalidOperationException("选择框聚焦状态漏用底色副本");
});
foreach (var (control, prefix) in new (Control, string)[]
    { (new Button(), "Button"), (new ToggleButton(), "ToggleButton") })
    Check($"{prefix} 操作按钮恢复0.2.1默认三态并保留主题与自定义浓度", () =>
    {
        var brushes = ControlBackgroundBrushes.ForDetail(control, preserveDefault: true);
        foreach (var amount in new[] { 0d, 1d })
        {
            brushes.Apply(amount);
            foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Disabled" })
            {
                if (amount > 0 && suffix == "Disabled") continue;
                var source = (SolidColorBrush)resources[prefix + "Background" + suffix];
                var copy = (AcrylicBrush)control.Resources[prefix + "Background" + suffix];
                if (copy.FallbackColor != source.Color || copy.Opacity != source.Opacity || !copy.AlwaysUseFallback)
                    throw new InvalidOperationException("未保留平台" + suffix + "底色");
                if (control is ToggleButton && !ReferenceEquals(copy, control.Resources[prefix + "BackgroundChecked" + suffix]))
                    throw new InvalidOperationException("连播选中时换了背景");
            }
        }
        brushes.Apply(100);
        Equal(1, ((AcrylicBrush)control.Background!).Opacity);
        Equal(255, ((AcrylicBrush)control.Background!).FallbackColor.A);
        var rest = (SolidColorBrush)resources[prefix + "Background"];
        var original = rest.Color;
        try
        {
            rest.Color = 0x40112233;
            brushes.Apply(0);
            if (((AcrylicBrush)control.Background!).FallbackColor != rest.Color)
                throw new InvalidOperationException("默认按钮没有跟随主题画刷");
        }
        finally { rest.Color = original; }
        brushes.Apply(45, useAcrylic: true);
        if (((AcrylicBrush)control.Background!).AlwaysUseFallback)
            throw new InvalidOperationException("亚克力选项被默认背景覆盖");
    });
Check("连播选中态保留三态浓度", () =>
{
    var control = new ToggleButton();
    ControlBackgroundBrushes.ForDetail(control).Apply(50);
    foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Disabled" })
        if (!ReferenceEquals(control.Resources["ToggleButtonBackground" + suffix],
            control.Resources["ToggleButtonBackgroundChecked" + suffix]))
            throw new InvalidOperationException("连播选中态漏用局部底色");
});
Check("主页详情按钮单独复制三态底色", () =>
{
    var bannerResources = new ResourceDictionary
    {
        ["EgBannerGhostRestBrush"] = new SolidColorBrush { Color = 0x20F3F5F8 },
        ["EgBannerGhostHoverBrush"] = new SolidColorBrush { Color = 0x38F3F5F8 },
        ["EgBannerGhostPressBrush"] = new SolidColorBrush { Color = 0x50F3F5F8 }
    };
    var control = new Button();
    var brushes = ControlBackgroundBrushes.ForBanner(control, bannerResources);
    brushes.Apply(30);
    Increased((SolidColorBrush)bannerResources["EgBannerGhostRestBrush"], (AcrylicBrush)control.Background!, 30, TargetFor(""));
    Increased((SolidColorBrush)bannerResources["EgBannerGhostHoverBrush"],
        (AcrylicBrush)control.Resources["ButtonBackgroundPointerOver"], 30, TargetFor("PointerOver"));
    Increased((SolidColorBrush)bannerResources["EgBannerGhostPressBrush"],
        (AcrylicBrush)control.Resources["ButtonBackgroundPressed"], 30, TargetFor("Pressed"));
    Equal(1, ((SolidColorBrush)bannerResources["EgBannerGhostRestBrush"]).Opacity);
    brushes.Apply(100);
    Equal(1, control.Background!.Opacity);
    Equal(255, ((AcrylicBrush)control.Background).FallbackColor.A);
});
Check("按钮全程单调增加且100档消除颜色和画刷双重透明", () =>
{
    var control = new Button();
    var brushes = ControlBackgroundBrushes.ForDetail(control);
    var previous = -1d;
    foreach (var percent in new[] { 0d, 0.001, 1, 10, 30, 45, 70, 99, 100 })
    {
        brushes.Apply(percent);
        var copy = (AcrylicBrush)control.Background!;
        var alpha = copy.FallbackColor.A / 255d * copy.Opacity;
        if (alpha < previous) throw new InvalidOperationException("滑块增加时底色更透明了");
        previous = alpha;
    }
    Equal(1, previous);
    Equal(1, control.Opacity);
    brushes.Apply(double.NaN);
    var zero = (AcrylicBrush)control.Background!;
    Equal(0, zero.Opacity);
    if (zero.FallbackColor.A != 0)
        throw new InvalidOperationException("非法值没有回到零档：零档不该有底色");
});
Check("R16-001主页详情按钮零档不绘制亚克力或系统回退实底", () =>
{
    var resources = new ResourceDictionary
    {
        ["EgBannerGhostRestBrush"] = new SolidColorBrush { Color = 0x20F3F5F8 },
        ["EgBannerGhostHoverBrush"] = new SolidColorBrush { Color = 0x38F3F5F8 },
        ["EgBannerGhostPressBrush"] = new SolidColorBrush { Color = 0x50F3F5F8 }
    };
    var control = new Button();
    var backgrounds = ControlBackgroundBrushes.ForBanner(control, resources);
    foreach (var value in new[] { 0d, 50d, 100d, 0d })
    {
        backgrounds.Apply(value, useAcrylic: true);
        var rest = (AcrylicBrush)control.Resources["ButtonBackground"];
        if (value == 0)
        {
            Equal(0, rest.Opacity);
            if (!rest.AlwaysUseFallback) throw new InvalidOperationException("zero still composes acrylic");
        }
    }
});
Check("薄白背景增强到100仍为深色且三态保持差别", () =>
{
    var bannerResources = new ResourceDictionary
    {
        ["EgBannerGhostRestBrush"] = new SolidColorBrush { Color = 0x20F3F5F8 },
        ["EgBannerGhostHoverBrush"] = new SolidColorBrush { Color = 0x38F3F5F8 },
        ["EgBannerGhostPressBrush"] = new SolidColorBrush { Color = 0x50F3F5F8 }
    };
    var control = new Button();
    var brushes = ControlBackgroundBrushes.ForBanner(control, bannerResources);
    brushes.Apply(100);
    var colors = new List<Windows.UI.Color>();
    foreach (var (suffix, sourceKey) in new[] { ("", "EgBannerGhostRestBrush"),
        ("PointerOver", "EgBannerGhostHoverBrush"), ("Pressed", "EgBannerGhostPressBrush") })
    {
        var copy = (AcrylicBrush)control.Resources["ButtonBackground" + suffix];
        Equal(1, copy.Opacity);
        Equal(255, copy.FallbackColor.A);
        if (copy.FallbackColor.R >= 128 || copy.FallbackColor.G >= 128 || copy.FallbackColor.B >= 128)
            throw new InvalidOperationException(suffix + " 增强到100出现白块，白字无法保持可读");
        Increased((SolidColorBrush)bannerResources[sourceKey], copy, 100, TargetFor(suffix));
        colors.Add(copy.FallbackColor);
    }
    if (colors.Distinct().Count() != 3) throw new InvalidOperationException("增强到100丢失了三态底色差别");
    brushes.Apply(0);
    if (((AcrylicBrush)control.Background!).FallbackColor.A != 0 || control.Background!.Opacity != 0)
        throw new InvalidOperationException("混色后零档还留着底色");
});
Check("增强中的底色跟随主题目标变化", () =>
{
    var control = new Button();
    var brushes = ControlBackgroundBrushes.ForDetail(control);
    var target = TargetFor("");
    var previousTarget = target.Color;
    try
    {
        brushes.Apply(75);
        var before = ((AcrylicBrush)control.Background!).FallbackColor;
        target.Color = 0xFF35472B;
        brushes.Apply(75);
        var after = ((AcrylicBrush)control.Background).FallbackColor;
        if (before == after) throw new InvalidOperationException("新主题目标没有参与增强配色");
        Increased((SolidColorBrush)resources["ButtonBackground"], (AcrylicBrush)control.Background, 75, target);
    }
    finally { target.Color = previousTarget; }
});
Check("外观通知不重新查询主页且导入默认通知全部", () =>
{
    var home = 0;
    var appearance = 0;
    var layout = 0;
    var ui = new UiSettings();
    void Home(UiSettings _) => home++;
    void Appearance(UiSettings _) => appearance++;
    void Layout(UiSettings _) => layout++;
    ShellPrefs.Changed += Home;
    ShellPrefs.AppearanceChanged += Appearance;
    ShellPrefs.DetailLayoutChanged += Layout;
    try
    {
        ShellPrefs.ApplyAppearance(ui);
        Equal(0, home);
        Equal(1, appearance);
        Equal(0, layout);
        ShellPrefs.ApplyDetailLayout(ui);
        Equal(0, home);
        Equal(1, layout);
        ShellPrefs.Apply(ui);
        Equal(1, home);
        Equal(2, appearance);
        Equal(2, layout);
        if (!HomeMotion.Enabled) throw new InvalidOperationException("导入后的动画开关没有恢复");
    }
    finally
    {
        ShellPrefs.Changed -= Home;
        ShellPrefs.AppearanceChanged -= Appearance;
        ShellPrefs.DetailLayoutChanged -= Layout;
    }
});

Check("材质切换不重写资源键，亚克力100档回实底", () =>
{
    var control = new ToggleButton();
    var brushes = ControlBackgroundBrushes.ForDetail(control);
    var method = typeof(ControlBackgroundBrushes).GetMethod("Apply", [typeof(double), typeof(bool), typeof(Windows.UI.Color?)])
        ?? throw new InvalidOperationException("缺少可切换的亚克力背景");
    var keys = control.Resources.Keys.ToArray();
    method.Invoke(brushes, [0d, true, null]);
    if (control.Background is not AcrylicBrush { AlwaysUseFallback: true } glass)
        throw new InvalidOperationException("零档仍启用实际材质");
    Equal(0, glass.Opacity);
    method.Invoke(brushes, [100d, true, null]);
    if (!glass.AlwaysUseFallback || glass.FallbackColor.A != 255 || glass.Opacity != 1)
        throw new InvalidOperationException("亚克力100档未完全不透明");
    method.Invoke(brushes, [0d, false, null]);
    if (!glass.AlwaysUseFallback || glass.FallbackColor.A != 0 || glass.Opacity != 0)
        throw new InvalidOperationException("现有底色零档还画着东西");
    if (!keys.SequenceEqual(control.Resources.Keys) || !ReferenceEquals(glass, control.Background))
        throw new InvalidOperationException("切换材质替换了资源字典或画刷");
});
Check("亚克力三态与checked、主题和回退独立", () =>
{
    foreach (var control in new Control[] { new Button(), new ToggleButton(), new ComboBox() })
    {
        var prefix = control is ComboBox ? "ComboBox" : control is ToggleButton ? "ToggleButton" : "Button";
        var backgrounds = ControlBackgroundBrushes.ForDetail(control);
        backgrounds.Apply(45, true);
        foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            var glass = (AcrylicBrush)control.Resources[prefix + "Background" + suffix];
            var target = TargetFor(suffix);
            if (glass.TintColor != target.Color || glass.FallbackColor != target.Color || glass.AlwaysUseFallback)
                throw new InvalidOperationException(prefix + suffix + " 亚克力未跟主题或已提前回退");
            Equal(0.45, glass.TintOpacity);
            Equal(0.45, glass.TintLuminosityOpacity ?? -1);
            if (control is ToggleButton && !ReferenceEquals(glass, control.Resources[prefix + "BackgroundChecked" + suffix]))
                throw new InvalidOperationException("选中态没有沿用对应亚克力");
            var before = target.Color;
            try
            {
                target.Color = 0xFF38492B;
                backgrounds.Apply(45, true);
                if (glass.TintColor != target.Color || glass.FallbackColor != target.Color)
                    throw new InvalidOperationException("亚克力切主题保留旧色");
            }
            finally { target.Color = before; }
        }
        Equal(1, control.Opacity);
    }
});

BackdropReadingTests.Run(Check);
PickerForegroundProbeTests.Run(Check);
DetailCustomizationCaseTests.Run(Check);
PickerEarlyForegroundTests.Run(Check);

Check("自选控件颜色贯穿按钮选择框三态与端点", ControlTintTests.Run);
Console.WriteLine($"外观透明度：{passed} 通过，{failed} 失败。");
Console.WriteLine($"TOTAL {passed + failed}; PASS {passed}; FAIL {failed}; SKIP 0");
return failed == 0 ? 0 : 1;

void Check(string name, Action test)
{
    try
    {
        test();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {error.Message}");
    }
}

static void Equal(double expected, double actual)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 0.000001)
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
}

static void Increased(SolidColorBrush source, AcrylicBrush copy, double percent, SolidColorBrush target)
{
    var amount = percent / 100;
    var floor = source.Color.A / 255d * source.Opacity;
    if (amount <= floor)
    {
        // 还没爬过原底色那一层薄白：颜色和浓度照旧，悬停手感在零档附近不断。
        if (copy.FallbackColor != source.Color)
            throw new InvalidOperationException($"期望原底色 {source.Color}，实际 {copy.FallbackColor}");
        Equal(source.Opacity, copy.Opacity);
        return;
    }
    Equal(amount, copy.Opacity);
    if (copy.FallbackColor != target.Color || copy.FallbackColor.A != 255)
        throw new InvalidOperationException($"期望主题表面色 {target.Color}，实际 {copy.FallbackColor}");
}

static SolidColorBrush TargetFor(string state) => (SolidColorBrush)Application.Current.Resources[state switch
{
    "PointerOver" => "EgSurfaceHoverBrush",
    "Pressed" => "EgSurfaceAltBrush",
    _ => "EgSurfaceElevatedBrush"
}];

static string FindRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
    throw new InvalidOperationException("未找到项目根目录");
}
