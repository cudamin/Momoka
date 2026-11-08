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
        Test("详情头图：分类和固定说明统一视频白色，主题链接与星标不变", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            foreach (var name in new[] { "ScoreSourceText", "HeroFacts", "Overview" })
                Assert.Equal("{StaticResource EgOnScrimBrush}", (string?)Named(page, name).Attribute("Foreground"));
            Assert.Contains("public Brush SublineBrush => Painted(\"EgOnScrimBrush\")", Source("ViewModels/DetailViewModel.cs"));
            var readMore = page.Descendants(Xaml + "HyperlinkButton").Single(element =>
                (string?)element.Attribute("AutomationProperties.AutomationId") == "DetailReadMoreButton");
            Assert.Null(readMore.Attribute("Foreground"));
            Assert.Equal("{ThemeResource EgAccentBrush}", (string?)Named(page, "ScoreStack")
                .Elements(Xaml + "FontIcon").Single().Attribute("Foreground"));
        });
        Test("详情沉浸：卡片副行换成压图浅灰白，纸面档保持主题暗字", () =>
        {
            var palette = Read("Theme/Palette.xaml").Root!;
            Assert.Equal("#FFC9D0D9", (string?)Resource(palette, "EgOnScrimDimBrush").Attribute("Color"));
            Assert.Equal("#FF9FA4A8", Resource(Theme("Dark"), "EgTextDimColor").Value);
            var styles = Read("Theme/Styles.xaml").Root!;
            Assert.Equal("{StaticResource EgOnScrimDimBrush}", Setter(Resource(styles, "EgOnScrimDataStyle"), "Foreground"));
            Assert.Equal("{ThemeResource EgTextDimBrush}", Setter(Resource(styles, "EgDataStyle"), "Foreground"));
            // 卡片自己那一支：OnScrim 换的是样式，不是把画刷抄一份（抄了就不跟主题走）。
            Assert.Contains("EgOnScrimDataStyle", Source("Views/PosterCard.xaml.cs"));
            // 详情页：沉浸档四条卡片带一起换墨，纸面档换回来。
            var showcase = Source("Views/DetailPage.Showcase.cs");
            foreach (var strip in new[] { "EpisodeStrip", "SeasonRow", "CastRow", "SimilarRow" })
                Assert.Contains(strip, showcase);
            Assert.Contains("strip.OnScrim = showcase;", showcase);
        });
        Test("详情沉浸：四类页面的其他信息卡都保留亚克力卡面", () =>
        {
            var code = Source("Views/DetailPage.Appearance.cs");
            Assert.Contains("InfoPanel.Background = ViewModel.Showcase ? CardMaterialSource.Background : _panelFill;", code);
            // 从前只管单集页；用户 2026-11-08 要求电影、剧、季也加同一块卡面，判据里不该再有类型分支。
            var paint = code[code.IndexOf("private void PaintBackdropReading()", StringComparison.Ordinal)..];
            paint = paint[..paint.IndexOf('}')];
            Assert.False(paint.Contains("EmbyItemType", StringComparison.Ordinal));
            var card = Resource(Theme("Dark"), "CardBackgroundFillColorDefaultBrush");
            Assert.Equal("AcrylicBrush", card.Name.LocalName);
            Assert.Equal("0.65", (string?)card.Attribute("TintOpacity"));
            Assert.Equal("0.45", (string?)card.Attribute("TintLuminosityOpacity"));
            Assert.Equal("{StaticResource EgSurfaceAltColor}", (string?)card.Attribute("TintColor"));
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
        Test("详情背景：阅读遮罩保持单层，底部独立收向主题底色", () =>
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
            // 2026-11-07 起背景固定亚克力：图上那层是三参数画笔，高对比度才退回实色。
            Assert.Contains("new AcrylicBrush", code);
            Assert.Contains("TintLuminosityOpacity", code);
            Assert.Contains("BackdropEdgeFade", code);
        });
        Test("详情接缝：图片底部渐变不移动纸面和正文布局", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            var body = Named(page, "BodySheet");
            var paper = Named(page, "BodyPaper");
            Assert.Equal(body.Parent, paper.Parent);
            Assert.Null(body.Attribute("Background"));
            Assert.Null(paper.Attribute("Margin"));
            var edge = Named(page, "BackdropEdgeFade");
            Assert.Equal(Named(page, "BackdropPicture").Parent, edge.Parent);
            Assert.Equal("False", (string?)edge.Attribute("IsHitTestVisible"));
            Assert.True(edge.Descendants(Xaml + "LinearGradientBrush").Any());
            Assert.False(page.Descendants().Any(element =>
                (string?)element.Attribute(X + "Name") is "BodyEdgeFade" or "BackdropSeam"));
            var code = Source("Views/DetailPage.xaml.cs");
            Assert.False(code.Contains("PrepareBackdropEdgeFade", StringComparison.Ordinal));
            Assert.False(code.Contains("UpdateBackdropEdgeFade", StringComparison.Ordinal));
        });
        Test("详情操作：按钮与闭合选择框共用平台薄白，展开保持亚克力", () =>
        {
            var code = Source("Views/DetailPage.Appearance.cs");
            Assert.Contains("ForDetail(control, lightSurface: true)", code);
            Assert.Contains("ForDetail(picker, lightSurface: true)", code);
            foreach (var (key, color) in new[] { ("EgDetailActionBrush", "#0FFFFFFF"),
                ("EgDetailActionHoverBrush", "#15FFFFFF"), ("EgDetailActionPressedBrush", "#08FFFFFF"),
                ("EgDetailActionTextBrush", "#FFFFFFFF") })
                Assert.Equal(color, (string?)Resource(Read("Theme/Palette.xaml").Root!, key).Attribute("Color"));
            Assert.False(Source("Views/DetailPage.Appearance.cs").Contains("TextInk.Attach", StringComparison.Ordinal));
            Assert.False(code.Contains("picker.Resources[\"ComboBoxDropDownBackground\"]", StringComparison.Ordinal));
            Assert.False(code.Contains("picker.Resources[\"ComboBoxItemBackground\"", StringComparison.Ordinal));
        });
        Test("详情正文：卡片左右留白等宽，背景纸面仍铺满", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            // 2026-11-08 用户令「右边空位的太多了，左边加宽一点点，然后两边对齐」：这一层从前挂的是
            // EgScrollBarLane（0,0,20,0）—— 只让右边 20，屏上就是左边贴边、右边空一条，卡片偏左。
            // 现在把那条车道对半分，左右各 10；两个数必须相等，否则整页又歪回去。
            var lane = (string?)Named(page, "BodySheet").Attribute("Margin");
            Assert.Equal("10,0,10,0", lane);
            var sides = lane!.Split(',');
            Assert.Equal(sides[0], sides[2]);
            Assert.Null(Named(page, "BodyPaper").Attribute("Margin"));
            Assert.Null(Named(page, "Body").Attribute("Padding"));
            var styles = Read("Theme/Styles.xaml");
            // 车道本身还是 20（12 的滚动条 + 2 + 6 的空隙），只是详情页不再把它整条压到右边。
            Assert.Equal("0,0,20,0", Resource(styles.Root!, "EgScrollBarLane").Value);
            var code = Source("Views/DetailPage.xaml.cs");
            var seal = code[code.IndexOf("internal (bool Ok, string Detail) BodySeal()", StringComparison.Ordinal)..];
            var paperBounds = seal.IndexOf("BodyPaper.TransformToVisual(root)", StringComparison.Ordinal);
            Assert.True(paperBounds >= 0 && paperBounds < seal.IndexOf("var tail = HeroTail", StringComparison.Ordinal));
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
            // 2026-11-08 起资源外面包了一层显式 ResourceDictionary（高对比度要一份 ThemeDictionaries）。
            var resources = page.Root!.Element(Xaml + "UserControl.Resources")!.Element(Xaml + "ResourceDictionary")!;
            var style = Resource(resources, "EgBannerGhostStyle");
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
            Test($"详情操作：{name} 六颗键共用灰色背景和平台模板", () =>
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

        Test("详情操作：连播勾选保持同底，文字与图标均使用白色", () =>
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
                Assert.Equal("{ThemeResource EgDetailActionTextBrush}", (string?)label.Attribute("Foreground"));
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

        Test("海报卡悬停操作：播放键改成亚克力，右下三颗仍不画底色", () =>
        {
            // 2026-11-08 用户令「封面上的播放按钮改成亚克力材质」：样式搬进 Styles.xaml 与详情页封面共用。
            var styles = Read("Theme/Styles.xaml");
            var play = Resource(styles.Root!, "EgOverlayPlayButtonStyle");
            Assert.Equal("{ThemeResource EgPlayGlassBrush}", Setter(play, "Background"));
            var card = Read("Views/PosterCard.xaml");
            var button = Named(card, "PlayButton");
            Assert.Equal("{StaticResource EgOverlayPlayButtonStyle}", (string?)button.Attribute("Style"));
            foreach (var (key, glass) in new[]
            {
                ("ButtonBackgroundPointerOver", "EgPlayGlassHoverBrush"),
                ("ButtonBackgroundPressed", "EgPlayGlassPressedBrush")
            })
                Assert.Equal(glass, LocalTarget(button, key), key);
            var resources = card.Root!.Element(Xaml + "UserControl.Resources")!;
            var action = Resource(resources, "EgCardActionButtonStyle");
            Assert.Equal("Transparent", Setter(action, "Background"));
            Assert.Null(Setter(action, "CornerRadius"), "右下操作组不再带玻璃圆角");

            foreach (var name in new[] { "WatchedButton", "FavoriteButton", "MoreButton" })
            {
                var actionButton = Named(card, name);
                Assert.Null(FindLocalResource(actionButton, "ButtonBackground"), "按钮常态仍带局部底色");
                Assert.Equal("Transparent", (string?)FindLocalResource(actionButton, "ButtonBackgroundPointerOver")?.Attribute("Color"));
                Assert.Equal("Transparent", (string?)FindLocalResource(actionButton, "ButtonBackgroundPressed")?.Attribute("Color"));
                Assert.Equal("EgOnScrimBrush", FindLocalResource(actionButton, "ButtonForegroundPointerOver")
                    ?.Attribute("ResourceKey")?.Value);
                Assert.Equal("EgOnScrimDimBrush", FindLocalResource(actionButton, "ButtonForegroundPressed")
                    ?.Attribute("ResourceKey")?.Value);
            }

            var actions = Named(card, "Actions").Element(Xaml + "StackPanel.Resources")!;
            Assert.False(actions.Descendants().Any(element =>
                (string?)element.Attribute("Source") == "ms-appx:///Theme/ActionButtons.xaml"),
                "右下操作组仍合入亚克力字典");
        });

        Test("封面播放键亚克力：两份主题三态是玻璃，高对比度退回系统实色", () =>
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                var dictionary = Theme(theme);
                foreach (var (key, tint) in new[]
                {
                    ("EgPlayGlassBrush", "EgWindowColor"),
                    ("EgPlayGlassHoverBrush", "EgAccentColor"),
                    ("EgPlayGlassPressedBrush", "EgAccentPressedColor")
                })
                {
                    var brush = Resource(dictionary, key);
                    Assert.Equal("AcrylicBrush", brush.Name.LocalName, theme + "/" + key);
                    Assert.Equal("{StaticResource " + tint + "}", (string?)brush.Attribute("TintColor"), theme + "/" + key);
                    Assert.Equal((string?)brush.Attribute("TintColor"), (string?)brush.Attribute("FallbackColor"), theme + "/" + key);
                    foreach (var opacity in new[] { "TintOpacity", "TintLuminosityOpacity" })
                    {
                        var value = double.Parse((string?)brush.Attribute(opacity) ?? "1", CultureInfo.InvariantCulture);
                        Assert.True(value is > 0 and < 1, theme + "/" + key + "/" + opacity + " 不透背后的画面");
                    }
                }
            }

            var contrast = Theme("HighContrast");
            Assert.Equal("SolidColorBrush", Resource(contrast, "EgPlayGlassBrush").Name.LocalName);
            Assert.Equal("SolidColorBrush", Resource(contrast, "EgPlayGlassHoverBrush").Name.LocalName);
            Assert.Equal("SolidColorBrush", Resource(contrast, "EgPlayGlassPressedBrush").Name.LocalName);

            // ThemeHost 不涂这几支的话，换主题时它们会停在第一帧的字面值上 —— 自检「主题角色覆盖」正是在问这个。
            var host = Source("Theme/ThemeHost.cs");
            foreach (var key in new[] { "EgPlayGlassBrush", "EgPlayGlassHoverBrush", "EgPlayGlassPressedBrush" })
                Assert.Contains("(\"" + key + "\",", host);
        });

        Test("详情封面播放键：只在电影与剧页出现，悬停浮现并复用海报卡那一份玻璃", () =>
        {
            var page = Read("Views/DetailPage.xaml");
            var slot = Named(page, "CoverSlot");
            var cover = Named(page, "CoverButton");
            var play = Named(page, "CoverPlayButton");
            Assert.Equal(slot, cover.Parent, "封面按钮要留在同一个槽里");
            Assert.Equal(slot, play.Parent, "播放键必须是封面按钮的兄弟：电影、剧两页的封面按钮命令停用、整颗不可命中");
            Assert.Equal("OnCoverPointerEntered", (string?)slot.Attribute("PointerEntered"));
            Assert.Equal("OnCoverPointerExited", (string?)slot.Attribute("PointerExited"));
            Assert.Equal("{x:Bind ViewModel.CoverAlignment, Mode=OneWay}", (string?)slot.Attribute("VerticalAlignment"),
                "对齐从封面按钮挪到槽位，集页居中、其余页靠顶仍要靠同一条判据");
            Assert.Equal("{x:Bind ViewModel.CoverPlayVisibility, Mode=OneWay}", (string?)play.Attribute("Visibility"));
            Assert.Equal("{x:Bind ViewModel.PlayCommand}", (string?)play.Attribute("Command"));
            Assert.Equal("{StaticResource EgOverlayPlayButtonStyle}", (string?)play.Attribute("Style"));
            Assert.Equal("False", (string?)play.Attribute("IsTabStop"), "主播放键已经在操作排里，封面这颗不进 Tab 顺序");
            Assert.Equal("0", (string?)play.Attribute("Opacity"));
            Assert.Equal("False", (string?)play.Attribute("IsHitTestVisible"));
            Assert.Equal("0.5,0.5", (string?)play.Attribute("RenderTransformOrigin"), "缩放要从圆心起，量出来的中心才不会偏");
            Assert.Equal("{StaticResource EgPlayGlyphStyle}",
                (string?)play.Elements(Xaml + "FontIcon").Single().Attribute("Style"));
            foreach (var (key, glass) in new[]
            {
                ("ButtonBackgroundPointerOver", "EgPlayGlassHoverBrush"),
                ("ButtonBackgroundPressed", "EgPlayGlassPressedBrush"),
                ("ButtonForegroundPointerOver", "EgTextOnAccentBrush")
            })
                Assert.Equal(glass, LocalTarget(play, key), key);

            // 「只有这两页」和「跟着播放目标走」是视图模型的判据，页面只照着绑。
            var vm = Source("ViewModels/DetailViewModel.cs");
            Assert.Contains("public Visibility CoverPlayVisibility => Show(CoverPlayShown);", vm);
            Assert.Contains("private bool CoverPlayShown => StillVisibility == Visibility.Visible && PlayTarget is not null", vm);
            Assert.Contains("&& ItemType is EmbyItemType.Movie or EmbyItemType.Series;", vm);
            Assert.Contains("[NotifyPropertyChangedFor(nameof(CoverPlayVisibility))]", vm);
            Assert.Equal(3, vm.Split("OnPropertyChanged(nameof(CoverPlayVisibility));", StringSplitOptions.None).Length - 1,
                "版面、重读与类型更正三处都要重喊这一格");
        });

        Test("翻页按钮亚克力：轮播箭头三态换玻璃，高对比度留系统实色", () =>
        {
            var banner = Read("Views/HomeBanner.xaml");
            var dictionary = banner.Root!.Element(Xaml + "UserControl.Resources")!.Element(Xaml + "ResourceDictionary")!;
            foreach (var (key, opacity) in new[]
            {
                ("EgBannerPageRestBrush", "0.55"),
                ("EgBannerPageHoverBrush", "0.72"),
                ("EgBannerPagePressBrush", "0.88")
            })
            {
                var brush = Resource(dictionary, key);
                Assert.Equal("AcrylicBrush", brush.Name.LocalName, key);
                Assert.Equal("{StaticResource EgBannerPageColor}", (string?)brush.Attribute("TintColor"), key);
                Assert.Equal(opacity, (string?)brush.Attribute("TintOpacity"), key);
                var luminosity = double.Parse((string?)brush.Attribute("TintLuminosityOpacity") ?? "1", CultureInfo.InvariantCulture);
                Assert.True(luminosity is > 0 and < 0.5, key + " 的明暗浓度还停在不透的那一档");
            }

            var style = Resource(dictionary, "EgBannerPageButtonStyle");
            Assert.Equal("{StaticResource EgBannerPageRestBrush}", Setter(style, "Background"));
            foreach (var name in new[] { "PrevButton", "NextButton" })
            {
                var button = Named(banner, name);
                Assert.Equal("EgBannerPageHoverBrush", LocalTarget(button, "ButtonBackgroundPointerOver"), name);
                Assert.Equal("EgBannerPagePressBrush", LocalTarget(button, "ButtonBackgroundPressed"), name);
            }

            var themes = dictionary.Element(Xaml + "ResourceDictionary.ThemeDictionaries")!;
            var contrast = themes.Elements().Single(element => (string?)element.Attribute(X + "Key") == "HighContrast");
            foreach (var key in new[] { "EgBannerPageRestBrush", "EgBannerPageHoverBrush", "EgBannerPagePressBrush" })
                Assert.Equal("SolidColorBrush", Resource(contrast, key).Name.LocalName, key);
        });

        Test("主页轮播：详情按钮不跟着翻页键换材质", () =>
        {
            // 2026-11-08 用户令「轮播图的详情按钮不改」：翻页键换成玻璃，那颗「详情」键仍走原来的
            // 半透明叠层画刷与 EgOnScrim 墨；两者只共用「不跟主题走」这一条。
            var banner = Read("Views/HomeBanner.xaml");
            var dictionary = banner.Root!.Element(Xaml + "UserControl.Resources")!.Element(Xaml + "ResourceDictionary")!;
            var ghost = Resource(dictionary, "EgBannerGhostStyle");
            Assert.Equal("{StaticResource EgBannerGhostRestBrush}", Setter(ghost, "Background"));
            Assert.Equal("{StaticResource EgOnScrimBrush}", Setter(ghost, "Foreground"));
            var page = Resource(dictionary, "EgBannerPageButtonStyle");
            Assert.Equal("{StaticResource EgBannerPageInkBrush}", Setter(page, "Foreground"));
            Assert.Contains("ControlBackgroundBrushes.ForBanner(OpenButton, Resources)", Source("Views/HomeBanner.xaml.cs"));
            Assert.False(Source("Views/HomeBanner.xaml.cs").Contains("EgBannerPage", StringComparison.Ordinal),
                "详情按钮不该接翻页键那套玻璃");
        });

        Test("翻页按钮亚克力：货架圆钮三态透得出背后的封面", () =>
        {
            var roles = new Dictionary<string, string>
            {
                ["EgActionGlassBrush"] = "EgSurfaceElevatedColor",
                ["EgActionGlassHoverBrush"] = "EgSurfaceHoverColor",
                ["EgActionGlassPressedBrush"] = "EgSurfaceAltColor"
            };
            foreach (var (key, role) in roles)
            {
                var dark = Resource(Theme("Dark"), key);
                Assert.Equal("AcrylicBrush", dark.Name.LocalName, key);
                Assert.Equal("{StaticResource " + role + "}", (string?)dark.Attribute("TintColor"), key);
                var light = Resource(Theme("Light"), key);
                Assert.Equal("AcrylicBrush", light.Name.LocalName, "Light/" + key);
                Assert.Equal("0.8", (string?)light.Attribute("TintOpacity"), "Light/" + key);
            }

            // 深色那档的明暗浓度要从 0.5 松下来，否则 48 DIP 的圆钮和一块实心圆在屏上没分别。
            foreach (var (key, opacity) in new[]
            {
                ("EgActionGlassBrush", "0.45"),
                ("EgActionGlassHoverBrush", "0.55"),
                ("EgActionGlassPressedBrush", "0.7")
            })
            {
                var dark = Resource(Theme("Dark"), key);
                Assert.Equal(opacity, (string?)dark.Attribute("TintOpacity"), key);
                var luminosity = double.Parse((string?)dark.Attribute("TintLuminosityOpacity") ?? "1", CultureInfo.InvariantCulture);
                Assert.True(luminosity <= 0.3, key + " 的明暗浓度还停在不透的那一档");
            }
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
            Assert.Contains("海报悬停按钮仍画底色或播放键未回到中性玻璃", source);
            Assert.Contains("封面播放键没有落在封面正中", source);
            Assert.Contains("ProbeCoverPlay()", source);
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
