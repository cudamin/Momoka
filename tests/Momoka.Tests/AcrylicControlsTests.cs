using System.Globalization;
using System.Xml.Linq;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>真实 XAML 的局部材质接线；运行期颜色、模板和输入仍由 Shell 探针核对。</summary>
internal static class AcrylicControlsTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] GlassKeys = ["EgActionGlassBrush", "EgActionGlassHoverBrush", "EgActionGlassPressedBrush"];
    private static readonly string[] States = ["", "PointerOver", "Pressed"];

    public static void Register()
    {
        foreach (var theme in new[] { "Dark", "Light" })
            Test($"按钮亚克力：{theme} 首帧三态有主题 tint 和不透明回退", () =>
            {
                var dictionary = Theme(theme);
                foreach (var (key, role) in GlassKeys.Zip(new[] { "EgSurfaceElevatedColor", "EgSurfaceHoverColor", "EgSurfaceAltColor" }))
                {
                    var brush = Resource(dictionary, key);
                    Assert.Equal("AcrylicBrush", brush.Name.LocalName, theme + "/" + key);
                    Assert.Equal("{StaticResource " + role + "}", (string?)brush.Attribute("TintColor"));
                    Assert.Equal((string?)brush.Attribute("TintColor"), (string?)brush.Attribute("FallbackColor"));
                    Assert.True(Resource(dictionary, role).Value.Trim().StartsWith("#FF", StringComparison.Ordinal),
                        theme + "/" + key + " 回退色仍半透明");
                    foreach (var opacity in new[] { "TintOpacity", "TintLuminosityOpacity" })
                    {
                        var value = double.Parse((string?)brush.Attribute(opacity) ?? "1", CultureInfo.InvariantCulture);
                        Assert.True(value is > 0 and < 1, theme + "/" + key + "/" + opacity + " 不透背后的内容");
                    }
                    Assert.Null(brush.Attribute("Opacity"), "整体透明度会让实色回退也半透明");
                }
            });

        Test("按钮亚克力：高对比度三态只用系统窗口实色", () =>
        {
            foreach (var key in GlassKeys)
            {
                var brush = Resource(Theme("HighContrast"), key);
                Assert.Equal("SolidColorBrush", brush.Name.LocalName);
                Assert.Equal("{ThemeResource SystemColorWindowColor}", (string?)brush.Attribute("Color"));
                Assert.Null(brush.Attribute("Opacity"));
            }
        });

        Test("按钮亚克力：局部平台模板背景按 Dark、Light 和高对比度分别解析", () =>
        {
            var document = Read("Theme/ActionButtons.xaml");
            foreach (var theme in new[] { "Dark", "Light", "HighContrast" })
            {
                var dictionary = document.Descendants(Xaml + "ResourceDictionary")
                    .Single(element => (string?)element.Attribute(X + "Key") == theme);
                foreach (var type in new[] { "Button", "ToggleButton", "ToggleButtonBackgroundChecked" })
                    for (var index = 0; index < States.Length; index++)
                    {
                        var key = type == "ToggleButtonBackgroundChecked" ? type + States[index] : type + "Background" + States[index];
                        var resource = Resource(dictionary, key);
                        if (theme == "HighContrast")
                        {
                            Assert.Equal("SolidColorBrush", resource.Name.LocalName);
                            Assert.Equal("{ThemeResource SystemColorWindowColor}", (string?)resource.Attribute("Color"));
                            Assert.Null(resource.Attribute("Opacity"));
                        }
                        else
                            Assert.Equal(GlassKeys[index], (string?)resource.Attribute("ResourceKey"));
                    }
            }
        });

        foreach (var name in new[] { "ColumnActions", "CompactBlock" })
            Test($"按钮亚克力：{name} 六颗操作保留平台模板且三态接玻璃", () =>
            {
                var page = Read("Views/DetailPage.xaml");
                var actions = Named(page, name).Elements().First(element => element.Name.LocalName == "WrapRow");
                var buttons = actions.Elements().Where(element => element.Name.LocalName is "Button" or "ToggleButton").ToArray();
                Assert.Equal(6, buttons.Length, name + " 的动作范围变了");
                foreach (var button in buttons)
                {
                    var type = button.Name.LocalName;
                    var styleKey = Reference((string?)button.Attribute("Style"));
                    var style = Resource(page.Root!.Element(Xaml + "Page.Resources")!, styleKey);
                    Assert.Equal("{StaticResource Default" + type + "Style}", (string?)style.Attribute("BasedOn"));
                    Assert.False(style.Descendants(Xaml + "ControlTemplate").Any(), "详情按钮不应重写平台模板");
                    var background = style.Elements(Xaml + "Setter").SingleOrDefault(element =>
                        (string?)element.Attribute("Property") == "Background");
                    Assert.NotNull(background, name + "/" + styleKey + " 的常态没有玻璃");
                    Assert.Equal("{ThemeResource EgActionGlassBrush}", (string?)background!.Attribute("Value"));
                    for (var index = 0; index < States.Length; index++)
                        Assert.Equal(GlassKeys[index], LocalAlias(button, type + "Background" + States[index]));
                    Assert.Null(FindLocalResource(button, type + "BackgroundDisabled"), "禁用态保留平台实色");
                }
            });

        Test("按钮亚克力：连播勾选保持同底，强调色只交给图标", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            foreach (var button in page.Descendants(Xaml + "ToggleButton").Where(element =>
                (string?)element.Attribute("Style") == "{StaticResource EgPillToggleButtonStyle}"))
            {
                for (var index = 0; index < States.Length; index++)
                {
                    Assert.Equal(GlassKeys[index], LocalAlias(button, "ToggleButtonBackgroundChecked" + States[index]));
                    Assert.Equal("EgAccentBrush", LocalAlias(button, "ToggleButtonForegroundChecked" + States[index]));
                }
                Assert.Null(FindLocalResource(button, "ToggleButtonBackgroundCheckedDisabled"));
                var label = button.Descendants(Xaml + "TextBlock").Single();
                Assert.Equal("{ThemeResource EgOnScrimBrush}", (string?)label.Attribute("Foreground"));
            }
        });

        Test("按钮亚克力：高对比度的播放字、连播字和状态图标可跟随系统文字色", () =>
        {
            var aliases = Read("Theme/ActionButtons.xaml");
            var contrast = aliases.Descendants(Xaml + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(X + "Key") == "HighContrast");
            foreach (var key in new[] { "EgOnScrimBrush", "EgAccentBrush", "EgDangerBrush",
                "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed" })
                Assert.Equal("{ThemeResource SystemColorWindowTextColor}", (string?)Resource(contrast, key).Attribute("Color"));

            var page = Read("Views/DetailPage.xaml");
            var play = Resource(page.Root!.Element(Xaml + "Page.Resources")!, "EgPlayButtonStyle");
            Assert.Equal("{ThemeResource EgOnScrimBrush}", (string?)play.Elements(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "Foreground").Attribute("Value"));
            foreach (var name in new[] { "ColumnActions", "CompactBlock" })
            {
                var actions = Named(page, name).Elements().First(element => element.Name.LocalName == "WrapRow");
                foreach (var foreground in actions.Descendants().Attributes("Foreground"))
                    Assert.True(foreground.Value.StartsWith("{ThemeResource ", StringComparison.Ordinal),
                        name + " 的前景仍固定在浅墨，系统白底上会消失");
                foreach (var button in actions.Elements(Xaml + "ToggleButton"))
                    foreach (var state in States)
                        Assert.Null(button.Element(Xaml + "ToggleButton.Resources")?.Elements().FirstOrDefault(element =>
                            (string?)element.Attribute(X + "Key") == "ToggleButtonForegroundChecked" + state),
                            "勾选前景的无主题本地别名挡住高对比度字典");
            }
        });

        Test("按钮亚克力：左右翻页圆的三态填充接玻璃，禁用态保留平台实色", () =>
        {
            var page = Read("Views/ShelfStrip.xaml");
            var style = Resource(page.Root!.Element(Xaml + "UserControl.Resources")!, "EgShelfPageButtonStyle");
            Assert.Equal("{ThemeResource EgActionGlassBrush}", (string?)style.Elements(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "Background").Attribute("Value"));
            var circle = style.Descendants(Xaml + "Ellipse").Single();
            Assert.Equal("{TemplateBinding Background}", (string?)circle.Attribute("Fill"));
            foreach (var name in new[] { "Prev", "Next" })
            {
                var button = Named(page, name);
                foreach (var (state, glass) in States.Skip(1).Zip(GlassKeys.Skip(1)))
                {
                    var visualState = style.Descendants(Xaml + "VisualState")
                        .Single(element => (string?)element.Attribute(X + "Name") == state);
                    Assert.Equal("{ThemeResource ButtonBackground" + state + "}", (string?)visualState
                        .Descendants(Xaml + "Setter").Single(element => (string?)element.Attribute("Target") == "Circle.Fill")
                        .Attribute("Value"));
                    Assert.Equal(glass, LocalAlias(button, "ButtonBackground" + state));
                }
                Assert.Null(FindLocalResource(button, "ButtonBackgroundDisabled"));
            }
        });

        Test("按钮亚克力：高对比度普通和勾选三态前景配系统窗口底，圆钮本地别名不挡住系统色", () =>
        {
            var actions = Read("Theme/ActionButtons.xaml").Descendants(Xaml + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(X + "Key") == "HighContrast");
            foreach (var type in new[] { "Button", "ToggleButton" })
                foreach (var state in States)
                    Assert.Equal("{ThemeResource SystemColorWindowTextColor}",
                        (string?)Resource(actions, type + "Foreground" + state).Attribute("Color"));

            var shelf = Read("Views/ShelfStrip.xaml");
            foreach (var name in new[] { "Prev", "Next" })
            {
                var resources = Named(shelf, name).Element(Xaml + "Button.Resources")!.Element(Xaml + "ResourceDictionary")!;
                var contrast = resources.Descendants(Xaml + "ResourceDictionary")
                    .SingleOrDefault(element => (string?)element.Attribute(X + "Key") == "HighContrast");
                Assert.NotNull(contrast, name + " 仍以无主题本地别名挡住系统前景");
                foreach (var key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
                {
                    Assert.Null(resources.Elements().SingleOrDefault(element => (string?)element.Attribute(X + "Key") == key));
                    Assert.Equal("{ThemeResource SystemColorWindowTextColor}", (string?)Resource(contrast!, key).Attribute("Color"));
                }
            }
        });

        Test("设置卡片亚克力：账号、聚合工具、通知条目和接管复用设置卡片刷子", () =>
        {
            var servers = Read("Views/ServersPage.xaml");
            var notifications = Read("Views/NotificationsPage.xaml");
            var cards = new[]
            {
                Resource(servers.Root!.Element(Xaml + "Page.Resources")!, "AccountTemplate").Element(Xaml + "Border")!,
                servers.Descendants(Xaml + "Border").Single(element =>
                    (string?)element.Attribute("AutomationProperties.AutomationId") == "ServersAggregateCard"),
                Resource(notifications.Root!.Element(Xaml + "Page.Resources")!, "EntryTemplate").Element(Xaml + "Border")!,
                Named(notifications, "StopReportToggle").Ancestors(Xaml + "Border").First()
            };
            foreach (var card in cards)
            {
                Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", (string?)card.Attribute("Background"));
                Assert.Equal("{ThemeResource EgBorderBrush}", (string?)card.Attribute("BorderBrush"));
                Assert.Equal("{StaticResource EgCardBorderThickness}", (string?)card.Attribute("BorderThickness"));
                Assert.Equal("{StaticResource EgCardCornerRadius}", (string?)card.Attribute("CornerRadius"));
            }
        });

        Test("卡片亚克力：海报播放键与右下操作组接按钮玻璃", () =>
        {
            var card = Read("Views/PosterCard.xaml");
            var resources = card.Root!.Element(Xaml + "UserControl.Resources")!;
            var play = Resource(resources, "EgCardPlayButtonStyle");
            Assert.Equal("{ThemeResource EgActionGlassBrush}", Setter(play, "Background"));
            var action = Resource(resources, "EgCardActionButtonStyle");
            Assert.Equal("{ThemeResource EgActionGlassBrush}", Setter(action, "Background"));
            Assert.Equal("8", Setter(action, "CornerRadius"));

            foreach (var name in new[] { "WatchedButton", "FavoriteButton", "MoreButton" })
            {
                var button = Named(card, name);
                for (var index = 0; index < States.Length; index++)
                    Assert.Equal(GlassKeys[index], LocalAlias(button, "ButtonBackground" + States[index]));
                Assert.Equal("EgOnScrimBrush", FindLocalResource(button, "ButtonForegroundPointerOver")
                    ?.Attribute("ResourceKey")?.Value);
                Assert.Equal("EgOnScrimDimBrush", FindLocalResource(button, "ButtonForegroundPressed")
                    ?.Attribute("ResourceKey")?.Value);
                Assert.Null(FindLocalResource(button, "ButtonBackgroundDisabled"), "禁用态保留平台实色");
            }

            var actions = Named(card, "Actions").Element(Xaml + "StackPanel.Resources")!
                .Element(Xaml + "ResourceDictionary")!;
            Assert.True(actions.Element(Xaml + "ResourceDictionary.MergedDictionaries")!.Elements().Any(
                element => (string?)element.Attribute("Source") == "ms-appx:///Theme/ActionButtons.xaml"),
                "右下操作组没有合入 ActionButtons 字典");
            var contrast = actions.Element(Xaml + "ResourceDictionary.ThemeDictionaries")!
                .Elements(Xaml + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(X + "Key") == "HighContrast");
            foreach (var key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
                Assert.Equal("{ThemeResource SystemColorWindowTextColor}",
                    (string?)Resource(contrast, key).Attribute("Color"));
        });

        Test("卡片亚克力：媒体库分类胶囊、搜索来源栏和飞牛筛选复用设置卡片刷子", () =>
        {
            var page = Read("Views/LibraryPage.xaml");
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}",
                (string?)Named(page, "LibrarySectionsSurface").Attribute("Background"));
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}",
                (string?)Named(page, "FnNasFilterPanel").Attribute("Background"));
            var style = Named(page, "SourceTabs").Elements()
                .Single(element => element.Name.LocalName == "SearchSourceTabs.Style")
                .Elements().Single(element => element.Name.LocalName == "Style");
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", Setter(style, "Background"));
            Assert.Equal("16", Setter(style, "CornerRadius"), "来源栏圆角应保持原样");
        });

        Test("卡片亚克力：MoviePilot 与 AniBT 资源卡、进度卡复用设置卡片刷子", () =>
        {
            foreach (var (file, name) in new[]
            {
                ("Views/MoviePilotResourceBrowser.xaml", "ResourceCard"),
                ("Views/MoviePilotSearchProgressView.xaml", "ProgressCard"),
                ("Views/AniBtResourceBrowser.xaml", "ProgressCard"),
                ("Views/AniBtResourceBrowser.xaml", "ReleaseGroup")
            })
                Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}",
                    (string?)Named(Read(file), name).Attribute("Background"), file + "/" + name);
        });

        Test("按钮亚克力：局部按钮资源不覆盖全局菜单、正文和输入", () =>
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                var dictionary = Theme(theme);
                foreach (var entry in dictionary.Elements().Where(element =>
                    (string?)element.Attribute("ResourceKey") is { } key && GlassKeys.Contains(key)))
                    Assert.False(((string?)entry.Attribute(X + "Key"))?.StartsWith("Button", StringComparison.Ordinal) == true
                        || ((string?)entry.Attribute(X + "Key"))?.StartsWith("ToggleButton", StringComparison.Ordinal) == true,
                        "按钮模板资源被改成了全局亚克力");
                Assert.Equal("EgSurfaceBrush", (string?)Resource(dictionary, "MenuFlyoutPresenterBackground").Attribute("ResourceKey"));
                Assert.Equal("EgSurfaceBrush", (string?)Resource(dictionary, "FlyoutPresenterBackground").Attribute("ResourceKey"));
                Assert.Equal("{StaticResource EgWindowColor}", (string?)Resource(dictionary, "EgFrostBrush").Attribute("TintColor"));
            }
        });

        Test("亚克力探针：materials 是唯一全量分区项且拒绝真实播放组合", () =>
        {
            Assert.True(ShellProbeSections.IsKnown("MATERIALS"), "材料分区尚未接入");
            Assert.Equal(1, ShellProbeSections.All.Count(section => section == "materials"));
            Assert.True(StartupArgs.ShellProbeSectionFilter(["--probe-shell", "--probe-section", "materials"])!
                .SetEquals(["materials"]));
            StartupArgs.ValidateShellProbe(["--probe-shell", "--probe-section", "materials"]);
            Assert.Throws<ArgumentException>(() => StartupArgs.ValidateShellProbe(
                ["--probe-shell", "--probe-section", "materials", "--play"]));
        });

        Test("亚克力探针：运行入口登记材料分区并保留完整五主题宽窄覆盖", () =>
        {
            var runner = Source("ShellNavigationProbe.cs");
            Assert.Equal(1, runner.Split("AcrylicControlsAsync,", StringSplitOptions.None).Length - 1);
            Assert.Contains("AcrylicControlsAsync, 180, section: ShellProbeSections.Materials", runner);
            var source = Source("ShellNavigationProbe.Materials.cs");
            Assert.Contains("new[] { \"detail\", \"shelf\", \"servers\", \"notifications\", \"library\", \"mpcards\", \"anibtcards\" }", source);
            Assert.Contains("foreach (var theme in UiThemes.All)", source);
            Assert.Contains("foreach (var width in new[] { 1280, 760 })", source);
            Assert.Contains("CheckRoles(theme);", source);
            Assert.Contains("detail.ViewModel.PlayTarget?.HasResumePosition == true", source);
            Assert.Contains("== 6);", source);
            Assert.Contains("hover.ActionGlass && hover.PlayGlassRest", source);
            Assert.Contains("acrylic-poster-actions-screen.png", source);
            Assert.Contains("CheckLibraryMaterials", source);
            Assert.Contains("CheckMoviePilotCardsAsync", source);
            Assert.Contains("CheckAniBtCardsAsync", source);
            Assert.Contains("acrylic-{stage}-fallback-card-screen.png", source);
            Assert.Contains("acrylic-{stage}-{theme.Id}-{width}-screen.png", source);
            Assert.Contains("acrylic-controls.txt", source);
        });

        Test("亚克力探针：假服务拒绝写入，实际三态、回退和卡片反例保留核验与恢复", () =>
        {
            // 此项只钉住离线探针的源接线；真实控件和回退效果要实际运行该分区才能证明。
            var source = Source("ShellNavigationProbe.Materials.cs");
            Assert.Contains("Require(request.Method == HttpMethod.Get || request.Method == HttpMethod.Head", source);
            Assert.Contains("Require(fixture.Actions.Plays == 0", source);
            Assert.Contains("fixture.Transport.Requests.All", source);
            Assert.Contains("acrylic.TintColor == ThemeHost.ToColor(colors[index])", source);
            Assert.Contains("acrylic.FallbackColor == ThemeHost.ToColor(colors[index])", source);
            Assert.Contains("VisualStateManager.GoToState(control, states[index], false)", source);
            Assert.Contains("ReferenceEquals(Fill(), expected)", source);
            Assert.Contains("\"Checked\", \"CheckedPointerOver\", \"CheckedPressed\"", source);
            Assert.Contains("OfType<ContentPresenter>()", source);
            Assert.Contains("OfType<Ellipse>()", source);
            Assert.Contains("item.Brush.AlwaysUseFallback = true", source);
            Assert.Contains("item.Brush.AlwaysUseFallback = item.Previous", source);
            Assert.Contains("card.Background = (Brush)dark[\"EgSurfaceBrush\"]", source);
            Assert.Contains("Require(!Correct()", source);
            Assert.Contains("finally { card.Background = original; }", source);
            Assert.Contains("HomeMotion.ApplyPreference(savedMotion); ThemeHost.Apply(savedTheme); _root.Background = savedBackground", source);
        });
    }

    private static string? Setter(XElement style, string property) => style.Elements(Xaml + "Setter")
        .SingleOrDefault(element => (string?)element.Attribute("Property") == property)?.Attribute("Value")?.Value;

    private static string LocalAlias(XElement element, string key)
    {
        var entry = FindLocalResource(element, key);
        Assert.NotNull(entry, key + " 缺少局部主题资源");
        var target = (string?)entry!.Attribute("ResourceKey");
        Assert.NotNull(target, key + " 应复用共享刷子");
        return GlassKeys.Contains(target) || target == "EgAccentBrush" ? target! : LocalAlias(element, target!);
    }

    private static XElement? FindLocalResource(XElement element, string key) =>
        element.AncestorsAndSelf().SelectMany(owner => owner.Elements(owner.Name.Namespace + (owner.Name.LocalName + ".Resources")))
            .SelectMany(Resources).FirstOrDefault(resource => (string?)resource.Attribute(X + "Key") == key);

    private static IEnumerable<XElement> Resources(XElement owner)
    {
        var source = (string?)owner.Attribute("Source");
        if (source is not null)
            return Resources(Read(source.Replace("ms-appx:///", "", StringComparison.Ordinal)).Root!);
        var themes = owner.Element(Xaml + "ResourceDictionary.ThemeDictionaries");
        var themed = themes?.Elements(Xaml + "ResourceDictionary")
            .Single(element => (string?)element.Attribute(X + "Key") == "Dark").Elements() ?? [];
        var merged = owner.Element(Xaml + "ResourceDictionary.MergedDictionaries")?.Elements()
            .SelectMany(Resources) ?? [];
        var direct = owner.Elements().Where(element => !element.Name.LocalName.StartsWith("ResourceDictionary.", StringComparison.Ordinal));
        return themed.Concat(direct.Where(element => element.Name.LocalName != "ResourceDictionary"))
            .Concat(direct.Where(element => element.Name.LocalName == "ResourceDictionary").SelectMany(Resources))
            .Concat(merged);
    }

    private static string Reference(string? value) =>
        value?.Split(' ', 2) is [_, var key] ? key.TrimEnd('}') : "";

    private static XElement Resource(XElement owner, string key)
    {
        var element = owner.Elements().SingleOrDefault(element => (string?)element.Attribute(X + "Key") == key);
        Assert.NotNull(element, key + " 资源缺失");
        return element!;
    }

    private static XElement Theme(string name) => Read("Theme/Palette.xaml").Descendants(Xaml + "ResourceDictionary")
        .Single(element => (string?)element.Attribute(X + "Key") == name);

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => (string?)element.Attribute(X + "Name") == name);

    private static XDocument Read(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            {
                var fullPath = Path.Combine(directory.FullName, "src", "Momoka.Shell", path.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(fullPath), path + " 材质资源缺失");
                return XDocument.Load(fullPath);
            }
        throw new DirectoryNotFoundException("未找到当前 Momoka 工作树");
    }

    private static string Source(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "Momoka.Shell", path));
        throw new DirectoryNotFoundException("未找到当前 Momoka 工作树");
    }
}
