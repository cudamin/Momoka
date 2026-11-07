using System.Reflection;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Shell;
using Momoka.Theming;
using Windows.UI;

var passed = 0;
var failed = 0;
var root = FindRoot();
var glassKeys = new[] { "EgActionGlassBrush", "EgActionGlassHoverBrush", "EgActionGlassPressedBrush" };
var palette = new ResourceDictionary { Source = new Uri("ms-appx:///Theme/Palette.xaml") };
var painted = new[] { "Dark", "Light" }.Select(_ => new ResourceDictionary()).ToArray();
for (var index = 0; index < painted.Length; index++)
{
    palette.ThemeDictionaries[index == 0 ? "Dark" : "Light"] = painted[index];
    foreach (var key in glassKeys) painted[index][key] = new AcrylicBrush();
    foreach (var key in new[] { "EgOnScrimBrush", "EgOnScrimDimBrush", "EgDetailActionTextBrush" })
        painted[index][key] = new SolidColorBrush();
    painted[index]["EgInputGlassBrush"] = new AcrylicBrush { TintOpacity = 0.65, TintLuminosityOpacity = 0.6 };
    painted[index]["EgFrostBrush"] = new AcrylicBrush { TintOpacity = 0.55, TintLuminosityOpacity = 0.62 };
}
var contrast = new ResourceDictionary();
var contrastBrush = new SolidColorBrush { Color = Color.FromArgb(255, 10, 20, 30) };
contrast[glassKeys[0]] = contrastBrush;
palette.ThemeDictionaries["HighContrast"] = contrast;
Application.Current.Resources.MergedDictionaries.Add(palette);

Check("全局材质原地应用三参数和状态浓度，不改输入、卡面或高对比", () =>
{
    var ui = new UiSettings
    {
        ControlBackgroundColor = "#123456",
        ControlBackgroundOpacity = 37,
        ControlTintOpacity = 12.5,
        ControlTintLuminosityOpacity = 78.25
    };
    var original = painted[0][glassKeys[0]];
    Apply(ui);
    foreach (var dictionary in painted)
        for (var index = 0; index < glassKeys.Length; index++)
        {
            var brush = (AcrylicBrush)dictionary[glassKeys[index]];
            Equal(Color.FromArgb(255, 18, 52, 86), brush.TintColor);
            Equal(brush.TintColor, brush.FallbackColor);
            Near(0.125, brush.TintOpacity);
            Near(0.7825, brush.TintLuminosityOpacity ?? -1);
            Near(index switch { 1 => 0.38575, 2 => 0.351625, _ => 0.37 }, brush.Opacity, 0.00001);
            Equal(false, brush.AlwaysUseFallback);
        }
    Equal(true, ReferenceEquals(original, painted[0][glassKeys[0]]));
    Near(0.65, ((AcrylicBrush)painted[0]["EgInputGlassBrush"]).TintOpacity);
    Near(0.62, ((AcrylicBrush)painted[0]["EgFrostBrush"]).TintLuminosityOpacity ?? -1);
    Equal(Color.FromArgb(255, 10, 20, 30), contrastBrush.Color);
});
Check("全局材质零档与满档端点，系统回退保持实际Tint", () =>
{
    var ui = new UiSettings { ControlBackgroundColor = "#A1B2C3", ControlBackgroundOpacity = 0 };
    Apply(ui);
    Near(0, ((AcrylicBrush)painted[0][glassKeys[0]]).Opacity);
    Near(21d / 255, ((AcrylicBrush)painted[0][glassKeys[1]]).Opacity);
    Near(8d / 255, ((AcrylicBrush)painted[0][glassKeys[2]]).Opacity);
    ui.ControlBackgroundOpacity = 100;
    Apply(ui);
    foreach (var key in glassKeys)
    {
        var brush = (AcrylicBrush)painted[0][key];
        Near(1, brush.Opacity);
        Equal(true, brush.AlwaysUseFallback);
        Equal(Color.FromArgb(255, 161, 178, 195), brush.FallbackColor);
    }
});
Check("主题切换重放最近偏好，颜色预览与实际设置保持隔离", () =>
{
    var ui = new UiSettings
    {
        ControlBackgroundColor = "#123456",
        ControlBackgroundOpacity = 45,
        ControlTintOpacity = 20,
        ControlTintLuminosityOpacity = 80
    };
    Apply(ui);
    var brush = (AcrylicBrush)painted[0][glassKeys[0]];
    var notifications = 0;
    void Observe(UiTheme theme)
    {
        notifications++;
        Equal(Color.FromArgb(255, 18, 52, 86), brush.TintColor);
        Near(0.45, brush.Opacity);
    }
    ThemeHost.Changed += Observe;
    try { ThemeHost.Apply(UiThemes.All.Last()); }
    finally { ThemeHost.Changed -= Observe; }
    Equal(1, notifications);
    Equal(Color.FromArgb(255, 18, 52, 86), brush.TintColor);
    Near(0.45, brush.Opacity);
    ShellPrefs.SetAppearanceColorPreview(false, "#654321", ui);
    Apply(ui);
    Equal(Color.FromArgb(255, 101, 67, 33), brush.TintColor);
    Equal("#123456", ui.ControlBackgroundColor);
    ShellPrefs.SetAppearanceColorPreview(false, null, ui);
    Apply(ui);
    Equal(Color.FromArgb(255, 18, 52, 86), brush.TintColor);
});
Check("全局材质异常读数安全夹取，失效参数回55%，不替换画刷", () =>
{
    var ui = new UiSettings
    {
        ControlBackgroundOpacity = double.NaN,
        ControlTintOpacity = double.NaN,
        ControlTintLuminosityOpacity = double.PositiveInfinity
    };
    Apply(ui);
    var brush = (AcrylicBrush)painted[0][glassKeys[0]];
    Near(0, brush.Opacity);
    Near(0.55, brush.TintOpacity);
    Near(0.55, brush.TintLuminosityOpacity ?? -1);
    ui.ControlBackgroundOpacity = 110;
    ui.ControlTintOpacity = -10;
    ui.ControlTintLuminosityOpacity = 120;
    Apply(ui);
    Near(1, brush.Opacity);
    Near(0, brush.TintOpacity);
    Near(1, brush.TintLuminosityOpacity ?? -1);
});
Check("标准与显式控件背景引用全局三刷，输入框角色继续独立", () =>
{
    var document = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Theme/Palette.xaml"));
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    foreach (var dictionary in document.Descendants().Where(element => element.Name.LocalName == "ResourceDictionary"
        && (string?)element.Attribute(x + "Key") is "Dark" or "Light"))
    {
        foreach (var type in new[] { "Button", "ToggleButton", "ComboBox" })
            foreach (var (suffix, target) in new[] { ("", glassKeys[0]), ("PointerOver", glassKeys[1]), ("Pressed", glassKeys[2]) })
                Equal(target, dictionary.Elements().Single(element => (string?)element.Attribute(x + "Key")
                    == type + "Background" + suffix).Attribute("ResourceKey")?.Value);
        foreach (var prefix in new[] { "AccentButtonBackground", "ToggleButtonBackgroundChecked" })
            foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
                Equal(glassKeys[suffix == "" ? 0 : suffix == "PointerOver" ? 1 : 2], dictionary.Elements().Single(element =>
                    (string?)element.Attribute(x + "Key") == prefix + suffix).Attribute("ResourceKey")?.Value);
        foreach (var suffix in new[] { "Focused", "Unfocused" })
            Equal(glassKeys[0], dictionary.Elements().Single(element => (string?)element.Attribute(x + "Key")
                == "ComboBoxBackground" + suffix).Attribute("ResourceKey")?.Value);
        Equal("EgInputGlassBrush", dictionary.Elements().Single(element => (string?)element.Attribute(x + "Key")
            == "ComboBoxBackgroundDisabled").Attribute("ResourceKey")?.Value);
        Equal(false, dictionary.Elements().Any(element => (string?)element.Attribute(x + "Key") is
            "ButtonBackgroundDisabled" or "ToggleButtonBackgroundDisabled"));
        foreach (var key in new[] { "AccentButtonForeground", "ToggleButtonForeground", "ToggleButtonForegroundChecked" })
            foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
                Equal("EgTextBrush", dictionary.Elements().Single(element => (string?)element.Attribute(x + "Key")
                    == key + suffix).Attribute("ResourceKey")?.Value);
    }
    var styles = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Theme/Styles.xaml"));
    foreach (var name in new[] { "EgSecondaryButtonStyle", "EgIconButtonStyle", "EgResourceButtonStyle", "EgResourceFilterStyle", "EgArtworkTileStyle" })
    {
        var style = styles.Descendants().Single(element => (string?)element.Attribute(x + "Key") == name);
        Equal("{ThemeResource EgActionGlassBrush}", style.Elements().Single(element => (string?)element.Attribute("Property") == "Background")
            .Attribute("Value")?.Value);
    }
});
Check("局部按钮和闭合选择框共用全局背景，不被页面样式覆盖", () =>
{
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    foreach (var (file, key) in new[]
    {
        ("FilterPanel", "EgFilterPickerStyle"), ("MoviePilotReorganizeDialog", "ReorganizePickerStyle"),
        ("MoviePilotReorganizeDialog", "ReorganizeActionStyle"), ("MoviePilotDownloadDialog", "DownloadPickerStyle"),
        ("LibraryPage", "LibraryPillStyle"), ("LibraryPage", "LibraryTabStyle"),
        ("LibraryPage", "RecentSearchButtonStyle"), ("LibraryPage", "FnNasFilterChoiceStyle"),
        ("ServersPage", "ServersToolbarButtonStyle"), ("ServersPage", "ServersActionButtonStyle"),
        ("SignInPage", "SignInSecondaryButtonStyle"), ("SettingsPage", "SettingsGroupToggle"),
        ("SettingsPage", "EgHomeShiftStyle"), ("ShellPage", "AccountActionStyle")
    })
    {
        var xml = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/" + file + ".xaml"));
        var style = xml.Descendants().Single(node => (string?)node.Attribute(x + "Key") == key);
        Equal("{ThemeResource EgActionGlassBrush}", style.Elements()
            .Single(node => (string?)node.Attribute("Property") == "Background").Attribute("Value")?.Value);
    }
    foreach (var (file, name) in new[]
    {
        ("MoviePilotResourceFacetPicker", "OpenFacet"), ("MoviePilotResourceBrowser", "FilterExpander"),
        ("MoviePilotResourceBrowser", "SortOrder"), ("MoviePilotResourceBrowser", "RemoveFilter"),
        ("FilterPanel", "FilterCloseButton"), ("LibrarySettingsPanel", "SettingsCloseButton"),
        ("MoviePilotDownloadDialog", "ToggleDownloadAdvanced"), ("MoviePilotSitePicker", "ChooseSites"),
        ("DiagnosticsPage", "DiagnosticsDetailsButton"),
        ("SubtitleDialog", "HeaderCloseButton"), ("SettingsPage", "SettingsSearchButton"),
        ("ShellPage", "AccountButton"), ("ShellPage", "SidebarSettingsButton")
    })
    {
        var xml = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/" + file + ".xaml"));
        Equal("{ThemeResource EgActionGlassBrush}", xml.Descendants()
            .Single(node => (string?)node.Attribute(x + "Name") == name).Attribute("Background")?.Value);
    }
});
Check("封面按钮保留透明操作和原播放底色，不随全局亚克力改变", () =>
{
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var poster = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/PosterCard.xaml"));
    var action = poster.Descendants().Single(node => (string?)node.Attribute(x + "Key") == "EgCardActionButtonStyle");
    Equal("Transparent", action.Elements().Single(node => (string?)node.Attribute("Property") == "Background").Attribute("Value")?.Value);
    var play = poster.Descendants().Single(node => (string?)node.Attribute(x + "Key") == "EgCardPlayButtonStyle");
    Equal("{ThemeResource EgSurfaceHoverBrush}", play.Elements().Single(node => (string?)node.Attribute("Property") == "Background").Attribute("Value")?.Value);
    var subscription = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/MoviePilotSubscriptionPoster.xaml"));
    var more = subscription.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "SubscriptionMore");
    Equal("Transparent", more.Attribute("Background")?.Value);
    foreach (var suffix in new[] { "PointerOver", "Pressed" })
        Equal("Transparent", more.Descendants().Single(node => (string?)node.Attribute(x + "Key") == "ButtonBackground" + suffix).Attribute("Color")?.Value);
});
Check("媒体前景深色纯白浅色深字，高对比使用系统色", () =>
{
    foreach (var theme in UiThemes.All)
    {
        ThemeHost.Apply(theme);
        foreach (var key in new[] { "EgOnScrimBrush", "EgOnScrimDimBrush", "EgDetailActionTextBrush" })
        {
            Equal(Color.FromArgb(255, 255, 255, 255), ((SolidColorBrush)painted[0][key]).Color);
            Equal(Color.FromArgb(255, 22, 22, 22), ((SolidColorBrush)painted[1][key]).Color);
            Equal(true, ThemeHost.BrushKeys.Contains(key));
        }
    }
    var document = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Theme/Palette.xaml"));
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    foreach (var (name, expected) in new[] { ("Dark", "#FFFFFFFF"), ("Light", "#FF161616"),
        ("HighContrast", "{ThemeResource SystemColorWindowTextColor}") })
    {
        var dictionary = document.Descendants().Single(element => element.Name.LocalName == "ResourceDictionary"
            && (string?)element.Attribute(x + "Key") == name);
        foreach (var key in new[] { "EgOnScrimBrush", "EgOnScrimDimBrush", "EgDetailActionTextBrush" })
        {
            var color = dictionary.Elements().Single(element => (string?)element.Attribute(x + "Key") == key).Attribute("Color")?.Value;
            if (name != "HighContrast")
            {
                Equal("{StaticResource EgMediaTextColor}", color);
                color = dictionary.Elements().Single(element => (string?)element.Attribute(x + "Key") == "EgMediaTextColor").Value;
            }
            Equal(expected, color);
        }
    }
});
Console.WriteLine($"TOTAL {passed + failed}; PASS {passed}; FAIL {failed}; SKIP 0");
return failed == 0 ? 0 : 1;

void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
void Apply(UiSettings ui)
{
    var method = typeof(ThemeHost).Assembly.GetType("Momoka.Shell.GlobalControlAppearance")?.GetMethod("Apply");
    if (method is null) throw new InvalidOperationException("缺少全局控件材质应用");
    method.Invoke(null, [ui]);
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"期望{expected}，实际{actual}");
}
static void Near(double expected, double actual, double tolerance = 0.000001)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance) throw new InvalidOperationException($"期望{expected}，实际{actual}");
}
static string FindRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
    throw new InvalidOperationException("未找到仓库");
}
