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

Check("零档不增加按钮底色浓度", () => Equal(0, AppearanceOpacity.ControlBackground(0)));
Check("按钮增强程度按百分比", () => Equal(0.45, AppearanceOpacity.ControlBackground(45)));
Check("按钮透明度下限", () => Equal(0, AppearanceOpacity.ControlBackground(-1)));
Check("按钮透明度上限", () => Equal(1, AppearanceOpacity.ControlBackground(101)));
Check("非有限按钮值回到默认", () =>
{
    foreach (var value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        Equal(0, AppearanceOpacity.ControlBackground(value));
});
Check("零档普通背景保留0.9", () => Equal(DetailHero.BackdropOpacity, AppearanceOpacity.Backdrop(0, false)));
Check("零档沉浸背景保留0.96", () => Equal(DetailHero.ShowcaseBackdropOpacity, AppearanceOpacity.Backdrop(0, true)));
Check("半档普通背景更不透明到0.945", () => Equal(0.945, AppearanceOpacity.Backdrop(45, false)));
Check("半档沉浸背景更不透明到0.978", () => Equal(0.978, AppearanceOpacity.Backdrop(45, true)));
Check("100档在两种版式都完全不透明", () =>
{
    Equal(1, AppearanceOpacity.Backdrop(100, false));
    Equal(1, AppearanceOpacity.Backdrop(100, true));
});
Check("沉浸加亮不会超过1", () => Equal(1, AppearanceOpacity.Backdrop(100, true)));
Check("非有限背景值保留默认两档", () =>
{
    foreach (var value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
    {
        Equal(DetailHero.BackdropOpacity, AppearanceOpacity.Backdrop(value, false));
        Equal(DetailHero.ShowcaseBackdropOpacity, AppearanceOpacity.Backdrop(value, true));
    }
});
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
    Check($"{prefix} 底色副本保持三态颜色且不修改控件浓度", () =>
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
    Check($"{prefix} 调整后可恢复且新主题颜色同步", () =>
    {
        brushes!.Apply(0);
        var source = (SolidColorBrush)resources[(control is ToggleButton ? "ToggleButton" : "Button") + "Background"];
        Equal(source.Opacity, control.Background!.Opacity);
        if (((AcrylicBrush)control.Background).FallbackColor != source.Color)
            throw new InvalidOperationException("零档没有完整恢复原画刷");
        source.Color = 0x30112233;
        brushes.Apply(0);
        Equal(source.Opacity, control.Background.Opacity);
        if (((AcrylicBrush)control.Background).FallbackColor != source.Color)
            throw new InvalidOperationException("切换主题后副本仍显示旧颜色");
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
    var source = (SolidColorBrush)resources["ButtonBackground"];
    Equal(source.Opacity, control.Background!.Opacity);
    if (((AcrylicBrush)control.Background).FallbackColor != source.Color)
        throw new InvalidOperationException("非法值没有回到零档原始外观");
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
    if (((AcrylicBrush)control.Background!).FallbackColor != ((SolidColorBrush)bannerResources["EgBannerGhostRestBrush"]).Color)
        throw new InvalidOperationException("混色后零档没有恢复原颜色");
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
    var method = typeof(ControlBackgroundBrushes).GetMethod("Apply", [typeof(double), typeof(bool)])
        ?? throw new InvalidOperationException("缺少可切换的亚克力背景");
    var keys = control.Resources.Keys.ToArray();
    method.Invoke(brushes, [0d, true]);
    if (control.Background is not AcrylicBrush { AlwaysUseFallback: false } glass)
        throw new InvalidOperationException("亚克力选项没有启用实际材质");
    Equal(0.55, glass.TintOpacity);
    Equal(1, glass.Opacity);
    method.Invoke(brushes, [100d, true]);
    if (!glass.AlwaysUseFallback || glass.FallbackColor.A != 255 || glass.Opacity != 1)
        throw new InvalidOperationException("亚克力100档未完全不透明");
    method.Invoke(brushes, [0d, false]);
    var source = (SolidColorBrush)resources["ToggleButtonBackground"];
    if (!glass.AlwaysUseFallback || glass.FallbackColor != source.Color)
        throw new InvalidOperationException("现有底色零档未还原");
    Equal(source.Opacity, glass.Opacity);
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
            Equal(0.55 + 0.45 * 0.45, glass.TintOpacity);
            Equal(0.5 + 0.5 * 0.45, glass.TintLuminosityOpacity ?? -1);
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
    var baseline = source.Color.A / 255d * source.Opacity;
    var added = (1 - baseline) * percent / 100;
    var expected = baseline + added;
    var actual = copy.FallbackColor.A / 255d * copy.Opacity;
    Equal(expected, actual);
    var pairs = new[] { (source.Color.R, target.Color.R, copy.FallbackColor.R),
        (source.Color.G, target.Color.G, copy.FallbackColor.G), (source.Color.B, target.Color.B, copy.FallbackColor.B) };
    foreach (var (original, surface, result) in pairs)
    {
        var channel = (original * baseline + surface * added) / expected;
        if (Math.Abs(result - channel) > 0.500001)
            throw new InvalidOperationException($"期望混合通道约 {channel}，实际 {result}");
    }
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
