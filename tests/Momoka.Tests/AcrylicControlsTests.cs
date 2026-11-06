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
        Test("卡片材质：共用深色染色和浓度，高对比度保留系统实色", () =>
        {
            foreach (var theme in new[] { "Dark", "Light" })
                foreach (var key in new[] { "CardBackgroundFillColorDefaultBrush", "CardBackgroundFillColorSecondaryBrush" })
                {
                    var brush = Resource(Theme(theme), key);
                    Assert.Equal("AcrylicBrush", brush.Name.LocalName);
                    Assert.Equal("{StaticResource EgSurfaceAltColor}", (string?)brush.Attribute("TintColor"));
                    Assert.Equal("{StaticResource EgSurfaceAltColor}", (string?)brush.Attribute("FallbackColor"));
                    Assert.Equal("0.65", (string?)brush.Attribute("TintOpacity"));
                    Assert.Equal("0.45", (string?)brush.Attribute("TintLuminosityOpacity"));
                    Assert.Equal("SolidColorBrush", Resource(Theme("HighContrast"), key).Name.LocalName);
                }
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}",
                Setter(Resource(Read("Theme/Styles.xaml").Root!, "EgBodyPanelStyle"), "Background"));
            foreach (var name in new[] { "FnNasPassword", "FnNasPreferences", "FnNasLibraries", "FnNasSettings", "FnNasTasks", "FnNasUsers" })
            {
                var page = Read("Views/" + name + "Page.xaml");
                var card = page.Descendants(Xaml + "Style").Single(row => (string?)row.Attribute(X + "Key") == "FnNasCard");
                Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", Setter(card, "Background"));
            }
        });
        Test("详情音视频行：文字与选择框统一正文字号", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            var audioLabel = Read("Strings/zh-Hans/Resources.resw").Descendants("data")
                .Single(row => (string?)row.Attribute("name") == "DetailPage_TextBlock3.Text");
            Assert.Equal("音频：", audioLabel.Element("value")?.Value);
            foreach (var name in new[] { "ColumnSourcePicker", "ColumnAudioPicker", "ColumnSubtitlePicker", "SourcePicker", "AudioPicker", "SubtitlePicker" })
                Assert.Equal("{StaticResource EgBodyFontSize}", (string?)Named(page, name).Attribute("FontSize"));
            foreach (var name in new[] { "DetailVideoText", "ColumnAudioText", "AudioText" })
                Assert.Equal("{StaticResource EgOnScrimBodyStyle}", (string?)Named(page, name).Attribute("Style"));
        });
        Test("详情菜单：透明表面使用轻透系统亚克力", () =>
        {
            var style = Resource(Read("Theme/Styles.xaml").Root!, "EgDetailMenuPresenterStyle");
            Assert.Equal("{StaticResource DefaultMenuFlyoutPresenterStyle}", (string?)style.Attribute("BasedOn"));
            Assert.Equal("{ThemeResource DesktopAcrylicTransparentBrush}",
                (string?)style.Elements().Single(row => (string?)row.Attribute("Property") == "Background").Attribute("Value"));
            Assert.False(style.Elements().Any(row => (string?)row.Attribute("Property") == "Template"));
            Assert.True(style.Descendants().Any(row => row.Name.LocalName == "DetailMenuBackdrop"));
        });
        Test("详情背景：仅保留单层阅读遮罩，不画下沿黑色渐变", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            var backdrop = Named(page, "Backdrop");
            var picture = Named(page, "BackdropPicture");
            var fade = Named(page, "BackdropFade");
            Assert.Equal(backdrop, fade.Parent);
            Assert.False(page.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "BackdropShade"));
            Assert.Equal((string?)picture.Attribute("Height"), (string?)fade.Attribute("Height"));
            Assert.Equal("Top", (string?)fade.Attribute("VerticalAlignment"));
            Assert.Equal("False", (string?)fade.Attribute("IsHitTestVisible"));
            Assert.Null(fade.Attribute("Opacity"));
            Assert.True(backdrop.Elements().ToList().IndexOf(fade) > backdrop.Elements().ToList().IndexOf(picture));
            var code = Source("Views/DetailPage.Backdrop.cs");
            Assert.Contains("new SolidColorBrush(ReadingShadeColor)", code);
            Assert.False(code.Contains("LinearGradientBrush", StringComparison.Ordinal));
        });
        Test("详情接缝：高斯位于正文文字下方，纸面让出下半带且固定图片也有跨边载体", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            var body = Named(page, "BodySheet");
            var paper = Named(page, "BodyPaper");
            var seam = Named(page, "BodySeam");
            var fixedSeam = Named(page, "BackdropSeam");
            Assert.Equal(body.Parent, paper.Parent);
            Assert.Equal(body.Parent, seam.Parent);
            Assert.Null(body.Attribute("Background"));
            Assert.True(body.Parent!.Elements().ToList().IndexOf(seam) < body.Parent.Elements().ToList().IndexOf(body));
            Assert.Equal("False", (string?)seam.Attribute("IsHitTestVisible"));
            Assert.Equal(Named(page, "Backdrop"), fixedSeam.Parent);
            var code = Source("Views/DetailPage.Seam.cs");
            Assert.Contains("BodyPaper.Margin = new Thickness(0, height / 2, 0, 0)", code);
            Assert.Contains("key.PictureHeight - height / 2", code);
            Assert.Contains("key.PictureHeight + height / 2 - paperTop", code);
            Assert.Contains("TransformToVisual(Backdrop)", code);
        });
        Test("主页轮播：底边没有渐变层，保留标题和侧边阅读遮罩", () =>
        {
            var banner = Read("Views/HomeBanner.xaml");
            var shades = Named(banner, "Band").Elements(Xaml + "Border").ToList();
            Assert.False(shades.Any(row => (string?)row.Attribute("VerticalAlignment") == "Bottom"));
            Assert.Equal(3, shades.Count);
        });
        Test("主页轮播：详情按钮有默认文字并保留本地化与导航", () =>
        {
            var button = Named(Read("Views/HomeBanner.xaml"), "OpenButton");
            Assert.Equal("详情", (string?)button.Attribute("Content"));
            Assert.Equal("HomeBanner_OpenButton", (string?)button.Attribute(X + "Uid"));
            Assert.Equal("OnOpenClicked", (string?)button.Attribute("Click"));
        });
        Test("主页轮播：详情圆角底色与边线共用矢量轮廓，内容不再绘制圆角背景", () =>
        {
            var page = Read("Views/HomeBanner.xaml");
            var style = Resource(page.Root!.Element(Xaml + "UserControl.Resources")!, "EgBannerGhostStyle");
            var surface = style.Descendants(Xaml + "Rectangle").Single();
            Assert.Equal("{TemplateBinding Background}", (string?)surface.Attribute("Fill"));
            Assert.Equal("{TemplateBinding BorderBrush}", (string?)surface.Attribute("Stroke"));
            Assert.Equal(Setter(style, "BorderThickness"), (string?)surface.Attribute("StrokeThickness"));
            var radius = Resource(Read("Theme/Styles.xaml").Root!, "EgCardCornerRadius").Value.Trim();
            Assert.Equal(radius, (string?)surface.Attribute("RadiusX"));
            Assert.Equal(radius, (string?)surface.Attribute("RadiusY"));
            Assert.Equal("False", (string?)surface.Attribute("UseLayoutRounding"));
            Assert.Equal("False", (string?)surface.Attribute("IsHitTestVisible"));
            var presenter = style.Descendants(Xaml + "ContentPresenter").Single();
            Assert.Null(presenter.Attribute("Background"));
            Assert.Null(presenter.Attribute("BorderBrush"));
            Assert.Null(presenter.Attribute("CornerRadius"));
            Assert.Equal("{TemplateBinding BorderThickness}", (string?)presenter.Attribute("BorderThickness"));
            foreach (var state in new[] { "PointerOver", "Pressed", "Disabled" })
            {
                var visual = style.Descendants(Xaml + "VisualState").Single(element => (string?)element.Attribute(X + "Name") == state);
                Assert.True(visual.Descendants(Xaml + "Setter").Any(element =>
                    (string?)element.Attribute("Target") == "Surface.Fill"
                    && (string?)element.Attribute("Value") == "{ThemeResource ButtonBackground" + state + "}"));
            }
        });
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
            Test($"详情操作：{name} 六颗键回到平台默认底，不再接玻璃", () =>
            {
                var page = Read("Views/DetailPage.xaml");
                var actions = Named(page, name).Elements().First(element => element.Name.LocalName == "WrapRow");
                Assert.False(actions.Elements().Any(element => element.Name.LocalName == "WrapRow.Resources"),
                    name + " 仍带局部材质字典");
                var buttons = actions.Elements().Where(element => element.Name.LocalName is "Button" or "ToggleButton").ToArray();
                Assert.Equal(6, buttons.Length, name + " 的动作范围变了");
                foreach (var button in buttons)
                {
                    var type = button.Name.LocalName;
                    var styleKey = Reference((string?)button.Attribute("Style"));
                    var style = Resource(page.Root!.Element(Xaml + "Page.Resources")!, styleKey);
                    Assert.Equal("{StaticResource Default" + type + "Style}", (string?)style.Attribute("BasedOn"));
                    Assert.False(style.Descendants(Xaml + "ControlTemplate").Any(), "详情按钮不应重写平台模板");
                    Assert.Null(Setter(style, "Background"), name + "/" + styleKey + " 仍自设固定背景");
                }
                Assert.False(page.Descendants().Any(element =>
                        (string?)element.Attribute("Source") == "ms-appx:///Theme/ActionButtons.xaml"),
                    "详情页仍合入按钮亚克力字典");
            });

        Test("详情操作：连播勾选保持同底，强调色只交给图标", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            foreach (var button in page.Descendants(Xaml + "ToggleButton").Where(element =>
                (string?)element.Attribute("Style") == "{StaticResource EgPillToggleButtonStyle}"))
            {
                for (var index = 0; index < States.Length; index++)
                {
                    // 背景现在由 ControlBackgroundBrushes 构造时提供，源链接专项覆盖三态与选中态共用副本。
                    // 本地 XAML 不能再声明别名，否则 WinUI 原地覆盖会按对象删错键。
                    Assert.Null(FindLocalResource(button, "ToggleButtonBackgroundChecked" + States[index]));
                    Assert.Equal("EgAccentBrush", LocalTarget(button, "ToggleButtonForegroundChecked" + States[index]));
                }
                Assert.Null(FindLocalResource(button, "ToggleButtonBackgroundCheckedDisabled"));
                var label = button.Descendants(Xaml + "TextBlock").Single();
                Assert.Equal("{StaticResource EgOnScrimBrush}", (string?)label.Attribute("Foreground"));
            }
        });

        Test("按钮亚克力：货架翻页字典的高对比度文字与图标仍跟随系统色", () =>
        {
            var aliases = Read("Theme/ActionButtons.xaml");
            var contrast = aliases.Descendants(Xaml + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(X + "Key") == "HighContrast");
            foreach (var key in new[] { "EgOnScrimBrush", "EgAccentBrush", "EgDangerBrush",
                "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed" })
                Assert.Equal("{ThemeResource SystemColorWindowTextColor}", (string?)Resource(contrast, key).Attribute("Color"));
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

        Test("海报卡悬停操作：播放键回到中性灰底，右下三颗不再画亚克力", () =>
        {
            var card = Read("Views/PosterCard.xaml");
            var resources = card.Root!.Element(Xaml + "UserControl.Resources")!;
            var play = Resource(resources, "EgCardPlayButtonStyle");
            Assert.Equal("{ThemeResource EgSurfaceHoverBrush}", Setter(play, "Background"));
            var action = Resource(resources, "EgCardActionButtonStyle");
            Assert.Equal("Transparent", Setter(action, "Background"));
            Assert.Null(Setter(action, "CornerRadius"), "右下操作组不再带玻璃圆角");

            foreach (var name in new[] { "WatchedButton", "FavoriteButton", "MoreButton" })
            {
                var button = Named(card, name);
                Assert.Null(FindLocalResource(button, "ButtonBackground"), "按钮常态仍带局部底色");
                Assert.Equal("Transparent", (string?)FindLocalResource(button, "ButtonBackgroundPointerOver")?.Attribute("Color"));
                Assert.Equal("Transparent", (string?)FindLocalResource(button, "ButtonBackgroundPressed")?.Attribute("Color"));
                Assert.Equal("EgOnScrimBrush", FindLocalResource(button, "ButtonForegroundPointerOver")
                    ?.Attribute("ResourceKey")?.Value);
                Assert.Equal("EgOnScrimDimBrush", FindLocalResource(button, "ButtonForegroundPressed")
                    ?.Attribute("ResourceKey")?.Value);
            }

            var actions = Named(card, "Actions").Element(Xaml + "StackPanel.Resources")!;
            Assert.False(actions.Descendants().Any(element =>
                (string?)element.Attribute("Source") == "ms-appx:///Theme/ActionButtons.xaml"),
                "右下操作组仍合入亚克力字典");
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
            Assert.Contains("new[] { \"detail\", \"shelf\", \"servers\", \"notifications\", \"library\", \"mpcards\", \"anibtcards\", \"fnnas\" }", source);
            Assert.Contains("foreach (var theme in UiThemes.All)", source);
            Assert.Contains("foreach (var width in new[] { 1280, 760 })", source);
            Assert.Contains("CheckRoles(theme);", source);
            Assert.Contains("detail.ViewModel.PlayTarget?.HasResumePosition == true", source);
            Assert.Contains("== 6,", source);
            Assert.Contains("详情操作排未在时限内出现", source);
            Assert.Contains("海报悬停按钮仍画底色或播放键未回到中性灰底", source);
            Assert.Contains("poster-actions-restored-screen.png", source);
            Assert.Contains("详情操作按钮仍接共享玻璃", source);
            Assert.Contains("detail-actions-restored-screen.png", source);
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

    private static string? LocalTarget(XElement element, string key) =>
        (string?)FindLocalResource(element, key)?.Attribute("ResourceKey");

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
