using System.Globalization;
using System.Xml.Linq;
using Momoka.Shell;
using Momoka.Theming;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>编译链接真实输入角色表，并核对首帧资源与局部覆盖；真实像素另由设置探针验。</summary>
internal static class InputControlThemeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string StylesFile = "src/Momoka.Shell/Theme/Styles.xaml";
    private const string SearchRadiusKey = "EgSearchCornerRadius";
    private const string SearchRadius = "{StaticResource EgSearchCornerRadius}";
    private const string ControlRadiusKey = "ControlCornerRadius";
    private const string OverlayRadiusKey = "OverlayCornerRadius";
    private const string CompactRadius = "{ThemeResource " + ControlRadiusKey + "}";
    private const string GlobalComboStyle = "{StaticResource EgComboBoxStyle}";
    private const string DetailPickerPage = "src/Momoka.Shell/Views/DetailPage.xaml";
    private static readonly (string Page, string Name)[] SearchTextInputs =
    [
        ("DanmakuPanel.xaml", "TitleBox"), ("FnNasMatchDialog.xaml", "KeywordBox"),
        ("IdentifyDialog.xaml", "NameBox"), ("MoviePilotDownloadDialog.xaml", "DownloadSearchText"),
        ("MoviePilotResourceFilterDialog.xaml", "FilterText"), ("MoviePilotSubscriptionEditor.xaml", "KeywordInput")
    ];
    private static readonly string[] SettingsInputPages =
    [
        "SettingsPage.xaml", "FnNasPasswordPage.xaml", "FnNasPreferencesPage.xaml",
        "FnNasLibrariesPage.xaml", "FnNasUsersPage.xaml", "FnNasSettingsPage.xaml"
    ];
    private static readonly string[] SettingsInputTypes =
        ["ComboBox", "TextBox", "PasswordBox", "NumberBox", "AutoSuggestBox"];
    private static readonly string[] TextBackgroundKeys =
    [
        "TextControlBackground", "TextControlBackgroundPointerOver",
        "TextControlBackgroundFocused", "TextControlBackgroundDisabled"
    ];
    private static readonly string[] ComboBackgroundKeys =
    [
        "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
        "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused", "ComboBoxBackgroundDisabled"
    ];
    private static readonly string[] StrongBorderKeys =
    [
        "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
        "ComboBoxBackgroundBorderBrushFocused"
    ];
    private static readonly string[] BorderKeys = ["TextControlBorderBrush", "TextControlBorderBrushDisabled"];

    public static void Register()
    {
        Test("圆角分组：WinUI 控件从共享资源或模板取得圆角", () =>
        {
            var styles = Read(StylesFile).Root!;
            var radiusKeys = styles.Elements(Xaml + "CornerRadius").Select(radius => (string?)radius.Attribute(X + "Key")).ToArray();
            Assert.True(radiusKeys.All(key => !string.IsNullOrWhiteSpace(key)), "共享圆角须有资源键");
            var protectedKeys = radiusKeys.OfType<string>().ToHashSet(StringComparer.Ordinal);
            Assert.Equal(radiusKeys.Length, protectedKeys.Count, "共享圆角资源键不可重复");
            var offenders = ProductXaml().SelectMany(entry => FindLocalCornerRadii(entry.Path, entry.Document, protectedKeys)).ToArray();
            Assert.True(offenders.Length == 0, string.Join(Environment.NewLine, offenders));
        });

        Test("圆角分组：拒绝控件、样式和页面资源重新定义圆角", () =>
        {
            var page = XDocument.Parse("""
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <CornerRadius x:Key="EgLocalRadius">8</CornerRadius>
                    <Border CornerRadius="6" />
                    <Border CornerRadius="{StaticResource EgCardCornerRadius}" />
                    <Border CornerRadius="{TemplateBinding CornerRadius}" />
                    <Border CornerRadius="0" />
                    <Rectangle RadiusX="4" RadiusY="4" />
                    <Style TargetType="Button">
                        <Setter Property="CornerRadius" Value="4" />
                    </Style>
                </ResourceDictionary>
                """);
            Assert.Equal(3, FindLocalCornerRadii("Fixture.xaml", page).Count());
            Assert.Equal(4, FindLocalCornerRadii("Fixture.xaml", page, []).Count());
            Assert.Equal(2, FindLocalCornerRadii(StylesFile, page).Count());
            var shadowed = XDocument.Parse("""
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <StaticResource x:Key="ControlCornerRadius" ResourceKey="EgSearchCornerRadius" />
                </ResourceDictionary>
                """);
            Assert.Equal(1, FindLocalCornerRadii("Fixture.xaml", shadowed, new HashSet<string> { ControlRadiusKey }).Count());
        });

        Test("输入主题：真实角色表无重复或遗漏", () =>
        {
            var expected = TextBackgroundKeys.Concat(ComboBackgroundKeys).Concat(StrongBorderKeys)
                .Concat(BorderKeys).Append("EgInputGlassBrush").Order().ToArray();
            var actual = InputControlTheme.Brushes.Select(entry => entry.Key).Order().ToArray();
            Assert.Equal(string.Join('|', expected), string.Join('|', actual));
            Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());
        });
        Test("输入主题：文本输入沿用侧边栏底色，选择框使用输入玻璃", () =>
        {
            foreach (var theme in UiThemes.All.Append(UiThemes.System(false)))
            {
                foreach (var key in TextBackgroundKeys)
                    Assert.Equal(theme.Colors.Window, Pick(key, theme), $"{theme.Id}/{key}");
                foreach (var key in ComboBackgroundKeys)
                    Assert.Equal(InputControlTheme.Glass(theme), Pick(key, theme), $"{theme.Id}/{key}");
                Assert.Equal(InputControlTheme.Glass(theme), Pick("EgInputGlassBrush", theme), theme.Id);
            }
        });
        Test("输入主题：玻璃 tint 深色取悬停面、浅色取凹面", () =>
        {
            foreach (var theme in UiThemes.All.Append(UiThemes.System(false)))
                Assert.Equal(theme.IsDark ? theme.Colors.SurfaceHover : theme.Colors.SurfaceAlt,
                    InputControlTheme.Glass(theme), theme.Id);
        });
        Test("输入主题：聚焦和悬停只加强中性边线，不使用强调色", () =>
        {
            foreach (var theme in UiThemes.All.Append(UiThemes.System(false)))
            {
                foreach (var key in StrongBorderKeys)
                {
                    Assert.Equal(theme.Colors.BorderStrong, Pick(key, theme), $"{theme.Id}/{key}");
                    Assert.True(Pick(key, theme) != theme.Colors.Accent, $"{theme.Id}/{key} 仍包主题色");
                }
                foreach (var key in BorderKeys)
                    Assert.Equal(theme.Colors.Border, Pick(key, theme), $"{theme.Id}/{key}");
            }
        });
        Test("输入主题：Dark 与 Light 首帧资源和真实选色规则一致", () =>
        {
            foreach (var dictionary in Dictionaries())
            {
                foreach (var key in TextBackgroundKeys) AssertBrush(dictionary, key, "EgWindowColor");
                foreach (var key in ComboBackgroundKeys) AssertAlias(dictionary, key, "EgInputGlassBrush");
                foreach (var key in StrongBorderKeys) AssertBrush(dictionary, key, "EgBorderStrongColor");
                foreach (var key in BorderKeys) AssertBrush(dictionary, key, "EgBorderColor");

                var glass = Resource(dictionary, "EgInputGlassBrush");
                var themeName = dictionary.Attribute(X + "Key")?.Value;
                var glassRole = themeName == "Light" ? "EgSurfaceAltColor" : "EgSurfaceHoverColor";
                Assert.Equal("AcrylicBrush", glass.Name.LocalName);
                Assert.Equal($"{{StaticResource {glassRole}}}", (string?)glass.Attribute("TintColor"),
                    themeName + "/玻璃 tint");
                Assert.Equal($"{{StaticResource {glassRole}}}", (string?)glass.Attribute("FallbackColor"),
                    themeName + "/玻璃回退色");
            }
        });
        Test("输入主题：默认Dark中性边线与Accent派生字节和真实主题一致", () =>
        {
            var dark = Dictionaries().Single(dictionary => (string?)dictionary.Attribute(X + "Key") == "Dark");
            var colors = UiThemes.Default.Colors;
            Assert.Equal(colors.BorderStrong.ToHex(), Resource(dark, "EgBorderStrongColor").Value);
            Assert.Equal(colors.AccentSoft.ToHex(), Resource(dark, "EgAccentSoftColor").Value);
            Assert.Equal(colors.Surface.Mix(colors.Accent, 0.28).ToHex(),
                (string?)Resource(dark, "AccentFillColorDisabledBrush").Attribute("Color"));
        });
        Test("输入主题：普通主题焦点四边保持 1 DIP，高对比度不覆盖系统输入资源", () =>
        {
            foreach (var dictionary in Dictionaries())
                Assert.Equal("1", Resource(dictionary, "TextControlBorderThemeThicknessFocused").Value.Trim());
            var highContrast = Palette().Descendants(Xaml + "ResourceDictionary")
                .Single(dictionary => (string?)dictionary.Attribute(X + "Key") == "HighContrast");
            var overrides = highContrast.Elements().Select(element => (string?)element.Attribute(X + "Key")).ToArray();
            Assert.True(!overrides.Any(key => key is not null &&
                (key.StartsWith("TextControl", StringComparison.Ordinal) ||
                 key.StartsWith("ComboBoxBackground", StringComparison.Ordinal))), "高对比度输入资源被产品配色覆盖");
            var glass = Resource(highContrast, "EgInputGlassBrush");
            Assert.Equal("SolidColorBrush", glass.Name.LocalName);
            Assert.Equal("{ThemeResource SystemColorWindowColor}", (string?)glass.Attribute("Color"));
        });
        Test("输入主题：设置卡片区的输入静止玻璃、聚焦回侧栏色，搜索框留在原样", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var scroll = page.Descendants(Xaml + "ScrollViewer")
                .Single(element => (string?)element.Attribute(X + "Name") == "SettingsScroll");
            var resources = scroll.Element(Xaml + "ScrollViewer.Resources")!.Element(Xaml + "ResourceDictionary")!;
            foreach (var theme in new[] { "Dark", "Light" })
            {
                var dictionary = resources.Descendants(Xaml + "ResourceDictionary")
                    .Single(element => (string?)element.Attribute(X + "Key") == theme);
                AssertAlias(dictionary, "TextControlBackground", "EgInputGlassBrush");
                AssertAlias(dictionary, "TextControlBackgroundPointerOver", "EgInputGlassBrush");
            }
            var contrast = resources.Descendants(Xaml + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(X + "Key") == "HighContrast");
            foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver" })
                Assert.Equal("{ThemeResource SystemColorWindowColor}", (string?)Resource(contrast, key).Attribute("Color"));
            Assert.True(!resources.Descendants().Any(element =>
                (string?)element.Attribute(X + "Key") == "TextControlBackgroundFocused"), "聚焦底色被局部覆盖，回不到侧栏色");

            var search = page.Descendants(Xaml + "AutoSuggestBox")
                .Single(element => (string?)element.Attribute(X + "Name") == "SettingsSearchBox");
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)search.Attribute("Background"));
            Assert.True(!search.Descendants().Attributes().Any(attribute => attribute.Value.Contains("EgAccentBrush", StringComparison.Ordinal)),
                "搜索框局部资源仍强制主题色边框");

            foreach (var number in page.Descendants(Xaml + "NumberBox"))
            {
                Assert.Equal("Hidden", (string?)number.Attribute("SpinButtonPlacementMode"));
                Assert.Equal("Center", (string?)number.Attribute("VerticalAlignment"));
            }
        });
        Test("输入主题：详情文件选项共用输入和选项圆角、保持按钮底色，集列表牌子不再挂下拉", () =>
        {
            var page = Read(DetailPickerPage);
            var pickerStyle = ImplicitStyle(page, "local:EgPicker");
            Assert.Equal(page.Root!.Element(Xaml + "Page.Resources"), pickerStyle.Parent);
            Assert.Equal(GlobalComboStyle, (string?)pickerStyle.Attribute("BasedOn"));
            Assert.Equal("{StaticResource EgSelectionItemStyle}", (string?)pickerStyle.Elements(Xaml + "Setter")
                .Single(setter => (string?)setter.Attribute("Property") == "ItemContainerStyle").Attribute("Value"));
            Assert.False(pickerStyle.Elements(Xaml + "Setter").Any(setter => (string?)setter.Attribute("Property") == "CornerRadius"),
                "详情选择框直接继承共享输入圆角");
            foreach (var name in new[] { "ColumnPickers", "PickerPanel" })
            {
                var panel = page.Descendants(Xaml + "StackPanel")
                    .Single(element => (string?)element.Attribute(X + "Name") == name);
                var resources = panel.Element(Xaml + "StackPanel.Resources")!;
                var themes = resources.Element(Xaml + "ResourceDictionary")!
                    .Element(Xaml + "ResourceDictionary.ThemeDictionaries")!;
                foreach (var themeKey in new[] { "Dark", "Light" })
                {
                    var dict = themes.Elements(Xaml + "ResourceDictionary")
                        .Single(element => (string?)element.Attribute(X + "Key") == themeKey);
                    foreach (var key in ComboBackgroundKeys)
                    {
                        var state = key["ComboBoxBackground".Length..];
                        var target = "ButtonBackground" + (state is "Focused" or "Unfocused" ? "" : state);
                        AssertAlias(dict, key, target);
                    }
                }
                Assert.NotNull(themes.Elements(Xaml + "ResourceDictionary")
                    .SingleOrDefault(element => (string?)element.Attribute(X + "Key") == "HighContrast"));
                Assert.False(resources.Descendants().Any(element =>
                    (string?)element.Attribute(X + "Key") == "ComboBoxDropDownBackground"), "弹层背景被局部覆盖");
                var pickers = panel.Descendants().Where(element => element.Name.LocalName == "EgPicker").ToArray();
                Assert.Equal(3, pickers.Length, name + " 的文件选项范围变了");
                foreach (var picker in pickers)
                {
                    Assert.Equal("{ThemeResource ButtonBackground}", (string?)picker.Attribute("Background"));
                    Assert.Null(picker.Attribute("CornerRadius"));
                    Assert.Null(picker.Attribute("ItemContainerStyle"));
                }
            }

            // 集列表牌子右端那颗季选择栏 2026-11-02 按用户令删了，连同它把六个全局下拉底钉回窗口色
            // （EgWindowBrush）的那套局部资源 —— 这一条现在盯着「别再长回来」：牌子上再有下拉，就得重新复核
            // 它的底色（全局那一支是实心块，压在这一页的底色上就是「贴上去的一块」）。
            var shelf = page.Descendants()
                .Single(element => (string?)element.Attribute(X + "Name") == "EpisodeHead");
            Assert.Null(shelf.Element(shelf.Name.Namespace + "ShelfHead.Resources"), "集列表牌子又有了局部下拉资源");
            Assert.False(shelf.Descendants().Any(element => element.Name.LocalName is "EgPicker" or "ComboBox"),
                "集列表牌子的右端又长出了季选择栏");

            // 2026-11-08 用户令「不要这个季度列表菜单了」：牌子只剩季名 —— 没有折角（Glyph）、没有点击面
            // （Offer）、也没有把季列表弹出来的那条事件（Invoked）。这一条盯着它别再长回来。
            Assert.Null(shelf.Attribute("Offer"));
            Assert.Null(shelf.Attribute("Invoked"));
            Assert.Null(shelf.Attribute("Glyph"));
        });
        Test("输入主题：点空白处把焦点交给卡片区，输入框回到玻璃", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            Assert.Equal("OnSettingsPointerPressed", (string?)page.Root!.Attribute("PointerPressed"));
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "SettingsPage.xaml.cs"));
            Assert.Contains("OnSettingsPointerPressed(e.OriginalSource as DependencyObject)", code);
            Assert.Contains("if (!HandlesPointerFocus(source)) FocusSettingsCards();", code);
            var focusStart = code.IndexOf("private void FocusSettingsCards", StringComparison.Ordinal);
            Assert.True(focusStart >= 0, "缺少卡片滚动区落焦");
            var focusEnd = code.IndexOf("\n    }", focusStart, StringComparison.Ordinal);
            Assert.True(focusEnd > focusStart, "无法读出卡片滚动区落焦的边界");
            var focus = code[focusStart..focusEnd];
            Assert.Contains("SettingsScroll.IsTabStop = true", focus);
            Assert.Contains("SettingsScroll.Focus(FocusState.Pointer)", focus);
            Assert.Contains("SettingsScroll.IsTabStop = tabStop", focus);
            var start = code.IndexOf("internal static bool HandlesPointerFocus", StringComparison.Ordinal);
            Assert.True(start >= 0, "缺少按下落点判断");
            var end = code.IndexOf("\n    }", start, StringComparison.Ordinal);
            Assert.True(end > start, "无法读出按下落点判断的边界");
            var guard = code[start..end];
            foreach (var control in new[] { "TextBox", "PasswordBox", "NumberBox", "ComboBox", "AutoSuggestBox",
                "ButtonBase", "ToggleSwitch", "Slider", "ListView", "ListViewItem", "ScrollBar",
                "ShortcutCaptureBox" })
                Assert.Contains(control, guard);
        });
        Test("输入主题：设置卡片亚克力统一使用卡片主题色", () =>
        {
            foreach (var dictionary in Dictionaries())
            {
                var card = Resource(dictionary, "CardBackgroundFillColorDefaultBrush");
                var themeName = dictionary.Attribute(X + "Key")?.Value;
                var role = "EgSurfaceAltColor";
                Assert.Equal("AcrylicBrush", card.Name.LocalName);
                Assert.Equal($"{{StaticResource {role}}}", (string?)card.Attribute("TintColor"), themeName + "/卡片 tint");
                Assert.Equal($"{{StaticResource {role}}}", (string?)card.Attribute("FallbackColor"), themeName + "/卡片回退色");
            }
        });
        Test("输入主题：设置搜索框不再局部覆盖为卡片底色", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var search = page.Descendants(Xaml + "AutoSuggestBox")
                .Single(element => (string?)element.Attribute(X + "Name") == "SettingsSearchBox");
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)search.Attribute("Background"));
            Assert.True(!search.Descendants().Attributes().Any(attribute => attribute.Value.Contains("EgAccentBrush", StringComparison.Ordinal)),
                "搜索框局部资源仍强制主题色边框");
        });
        Test("输入主题：播放器悬浮输入区仍沿用玻璃背景，不强制改成普通表单底色", () =>
        {
            var player = Read("src/Momoka.Shell/Views/PlayerPage.xaml");
            var input = player.Descendants(Xaml + "TextBox")
                .Single(element => (string?)element.Attribute(X + "Name") == "DanmakuInputBox");
            Assert.Equal("{StaticResource PlayerGlassTopBrush}", (string?)input.Attribute("Background"));
            var themes = input.Descendants(Xaml + "ResourceDictionary")
                .Where(dictionary => (string?)dictionary.Attribute(X + "Key") is "Dark" or "Light").ToArray();
            Assert.Equal(2, themes.Length);
            foreach (var dictionary in themes)
                foreach (var key in new[] { "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
                    Assert.Equal("PlayerGlassTopBrush", (string?)Resource(dictionary, key).Attribute("ResourceKey"));
        });
        Test("输入主题：快捷键抓键框静止用输入玻璃，抓键中回侧栏色和中性焦点", () =>
        {
            var capture = Read("src/Momoka.Shell/Views/ShortcutCaptureBox.xaml");
            var border = capture.Descendants(Xaml + "Border")
                .Single(element => (string?)element.Attribute(X + "Name") == "ComboBorder");
            Assert.Equal("{ThemeResource EgInputGlassBrush}", (string?)border.Attribute("Background"));
            var focus = capture.Descendants(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Target") == "ComboBorder.BorderBrush");
            Assert.Equal("{ThemeResource EgBorderStrongBrush}", (string?)focus.Attribute("Value"));
            var fill = capture.Descendants(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Target") == "ComboBorder.Background");
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)fill.Attribute("Value"));
        });
        Test("设置输入圆角：退役重复 SettingsInputs.xaml，设置与飞牛原生页复用全局输入圆角", () =>
        {
            Assert.False(File.Exists(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "SettingsInputs.xaml")),
                "SettingsInputs.xaml 已由 Styles.xaml 全局输入样式替代");
            foreach (var (relative, document) in ProductXaml())
            {
                Assert.False(document.Descendants().Attributes().Any(attribute =>
                    attribute.Value.Contains("SettingsInputs.xaml", StringComparison.Ordinal) ||
                    attribute.Value.Contains("EgSettingsInputCornerRadius", StringComparison.Ordinal) ||
                    attribute.Value.Contains("EgSettingsComboBoxStyle", StringComparison.Ordinal)),
                    relative + " 仍引用已退役的设置输入字典");
            }
            AssertGlobalInputStyles(Read(StylesFile));
        });
        Test("设置输入圆角：六页没有局部旧圆角或显式样式绕过，数字框直接接全局搜索圆角", () =>
        {
            foreach (var name in SettingsInputPages)
                AssertSettingsInputControls(name, Read("src/Momoka.Shell/Views/" + name));
            var libraries = Read("src/Momoka.Shell/Views/FnNasLibrariesPage.xaml");
            var number = Named(libraries, "FnNasMinimumFileSize");
            Assert.Equal(SearchRadius, (string?)number.Attribute("CornerRadius"));
            Assert.Equal("{x:Bind ViewModel.MinimumFileSize, Mode=TwoWay}", (string?)number.Attribute("Value"));
            Assert.Equal("{x:Bind ViewModel.MinimumSizeVisibility, Mode=OneWay}", (string?)number.Attribute("Visibility"));
        });
        Test("设置输入圆角：设置下拉仍用原列表项，搜索颜色内边距与绑定不变", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var combo = ImplicitStyle(page, "ComboBox");
            Assert.Equal(GlobalComboStyle, (string?)combo.Attribute("BasedOn"));
            var itemSetter = combo.Elements().Single();
            Assert.Equal("ItemContainerStyle", (string?)itemSetter.Attribute("Property"));
            Assert.Equal("{StaticResource EgComboItemStyle}", (string?)itemSetter.Attribute("Value"));
            var itemStyle = page.Descendants(Xaml + "Style")
                .Single(element => (string?)element.Attribute(X + "Key") == "EgComboItemStyle");
            Assert.Equal(CompactRadius, (string?)itemStyle.Elements(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "CornerRadius").Attribute("Value"));
            Assert.Equal(1, itemStyle.Descendants(Xaml + "ControlTemplate").Count());

            var searchStyle = page.Descendants(Xaml + "Style")
                .Single(element => (string?)element.Attribute(X + "Key") == "SettingsSearchTextBox");
            Assert.Equal("{StaticResource AutoSuggestBoxTextBoxStyle}", (string?)searchStyle.Attribute("BasedOn"));
            Assert.Equal(2, searchStyle.Elements().Count());
            Assert.Equal("32,6,8,6", (string?)searchStyle.Elements(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "Padding").Attribute("Value"));
            Assert.Equal(SearchRadius, (string?)searchStyle.Elements(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "CornerRadius").Attribute("Value"));
            var search = Named(page, "SettingsSearchBox");
            Assert.Equal(SearchRadius, (string?)search.Attribute("CornerRadius"));
            Assert.Equal("{StaticResource SettingsSearchTextBox}", (string?)search.Attribute("TextBoxStyle"));
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)search.Attribute("Background"));
            Assert.Equal("{x:Bind ViewModel.SettingsSearchQuery, Mode=TwoWay}", (string?)search.Attribute("Text"));
            var shortcuts = Named(page, "ShortcutSearchBox");
            Assert.Equal("Find", (string?)shortcuts.Attribute("QueryIcon"));
            Assert.Equal("{x:Bind ViewModel.ShortcutSearchQuery, Mode=TwoWay}", (string?)shortcuts.Attribute("Text"));
            Assert.True(shortcuts.Attribute("TextBoxStyle") is null, "快捷键搜索不替换平台文本样式");
            Assert.True(shortcuts.Attribute("Background") is null, "快捷键搜索不覆盖卡片区背景规则");
        });
        Test("设置圆角：语言下拉与普通输入同档，媒体库选项和设置选项同档", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var languagePicker = page.Descendants(Xaml + "DropDownButton").Single();
            Assert.Equal(SearchRadius, (string?)languagePicker.Attribute("CornerRadius"));

            var styles = Read("src/Momoka.Shell/Theme/Styles.xaml");
            AssertRadiusStyle(Resource(styles.Root!, "EgSelectionItemStyle"), "ComboBoxItem",
                "{StaticResource DefaultComboBoxItemStyle}", CompactRadius);

            var library = Read("src/Momoka.Shell/Views/LibrarySettingsPanel.xaml");
            Assert.Equal("{StaticResource EgCardCornerRadius}",
                (string?)Named(library, "LibraryDialogSurface").Attribute("CornerRadius"));
            foreach (var name in new[] { "ViewCombo", "SizeCombo" })
            {
                var combo = Named(library, name);
                Assert.True(combo.Attribute("CornerRadius") is null, name + " 继承全局输入圆角");
                Assert.Equal("{StaticResource EgSelectionItemStyle}", (string?)combo.Attribute("ItemContainerStyle"));
            }
        });
        Test("设置输入圆角：拒绝搜索恢复 8 DIP、输入框自设旧值和飞牛数字框漏接", () =>
        {
            var oldSearch = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            Named(oldSearch, "SettingsSearchBox").SetAttributeValue("CornerRadius", "8");
            Assert.Throws<AssertionException>(() => AssertSettingsInputControls("SettingsPage.xaml", oldSearch));
            var localText = Read("src/Momoka.Shell/Views/FnNasSettingsPage.xaml");
            Named(localText, "FnNasServerName").SetAttributeValue("CornerRadius", "4");
            Assert.Throws<AssertionException>(() => AssertSettingsInputControls("FnNasSettingsPage.xaml", localText));
            var missingNumber = Read("src/Momoka.Shell/Views/FnNasLibrariesPage.xaml");
            AssertSettingsInputControls("FnNasLibrariesPage.xaml", missingNumber);
            Named(missingNumber, "FnNasMinimumFileSize").Attribute("CornerRadius")!.Remove();
            Assert.Throws<AssertionException>(() => AssertSettingsInputControls("FnNasLibrariesPage.xaml", missingNumber));
        });
        Test("搜索输入圆角：应用级共享资源与设置同档，隐式样式只改圆角", () =>
        {
            var styles = Read("src/Momoka.Shell/Theme/Styles.xaml");
            AssertSearchDictionary(styles);
            var app = Read("src/Momoka.Shell/App.xaml");
            Assert.Equal(1, app.Root!.Element(Xaml + "Application.Resources")!
                .Descendants(Xaml + "ResourceDictionary").Count(element =>
                    (string?)element.Attribute("Source") == "ms-appx:///Theme/Styles.xaml"));
            var textStyle = Resource(styles.Root!, "EgSearchTextBoxStyle");
            Assert.Equal("{StaticResource AutoSuggestBoxTextBoxStyle}", (string?)textStyle.Attribute("BasedOn"));
            Assert.Equal(1, textStyle.Elements().Count(), "内层只改内边距，由外层 TemplateBinding 传入圆角");
            var padding = textStyle.Elements().Single();
            Assert.Equal("Padding", (string?)padding.Attribute("Property"));
            Assert.Equal("10,0,6,0", (string?)padding.Attribute("Value"));
        });
        Test("全站输入圆角：默认输入继承平台模板，专用选择框有独立的共享圆角", () =>
            AssertGlobalInputStyles(Read("src/Momoka.Shell/Theme/Styles.xaml")));
        Test("全站输入圆角：登录、弹窗、设置、播放器和显式样式没有旧值绕过", () =>
        {
            var shared = Read("src/Momoka.Shell/Theme/Styles.xaml");
            foreach (var (path, document) in ProductXaml()) AssertProductInputRadii(path, document, shared);
            var shortcut = Read("src/Momoka.Shell/Views/ShortcutCaptureBox.xaml");
            Assert.Equal(SearchRadius, (string?)Named(shortcut, "ComboBorder").Attribute("CornerRadius"));
            Assert.Equal(SearchRadius, (string?)Named(shortcut, "ClearButton").Attribute("CornerRadius"));
            foreach (var (page, name) in new[] { ("MoviePilotResourceFacetPicker.xaml", "OpenFacet"),
                ("MoviePilotSitePicker.xaml", "ChooseSites") })
            {
                var picker = Read("src/Momoka.Shell/Views/" + page);
                var button = Named(picker, name);
                Assert.Equal(SearchRadius, (string?)button.Attribute("CornerRadius"), page + "/" + name);
            }
        });
        Test("全站输入圆角：拒绝漏接数字框、登录旧值和显式下拉恢复平台样式", () =>
        {
            var shared = Read("src/Momoka.Shell/Theme/Styles.xaml");
            var missingNumber = new XDocument(shared);
            ImplicitStyle(missingNumber, "NumberBox").Remove();
            Assert.Throws<AssertionException>(() => AssertGlobalInputStyles(missingNumber));
            var login = Read("src/Momoka.Shell/Views/SignInPage.xaml");
            Named(login, "ServerBox").SetAttributeValue("CornerRadius", "8");
            Assert.Throws<AssertionException>(() => AssertProductInputRadii("SignInPage.xaml", login, shared));
            var editor = Read("src/Momoka.Shell/Views/MoviePilotSubscriptionEditor.xaml");
            Resource(editor.Root!.Element(Xaml + "UserControl.Resources")!, "EditorPicker")
                .SetAttributeValue("BasedOn", "{StaticResource DefaultComboBoxStyle}");
            Assert.Throws<AssertionException>(() => AssertProductInputRadii("MoviePilotSubscriptionEditor.xaml", editor, shared));
            var download = Read("src/Momoka.Shell/Views/MoviePilotDownloadDialog.xaml");
            download.Descendants(Xaml + "Setter").Single(setter =>
                (string?)setter.Attribute("Property") == "CornerRadius").SetAttributeValue("Value", "0");
            Assert.Throws<AssertionException>(() => AssertProductInputRadii("MoviePilotDownloadDialog.xaml", download, shared));
            var localImplicit = new XDocument(new XElement(Xaml + "Page",
                new XElement(Xaml + "Page.Resources", new XElement(Xaml + "Style",
                    new XAttribute("TargetType", "TextBox"), new XAttribute("BasedOn", "{StaticResource DefaultTextBoxStyle}")))));
            Assert.Throws<AssertionException>(() => AssertProductInputRadii("Page.xaml", localImplicit, shared));
        });
        Test("编辑订阅：优先级规则组复用订阅站点的就地展开字段", () =>
        {
            var editor = Read("src/Momoka.Shell/Views/MoviePilotSubscriptionEditor.xaml");
            var picker = Named(editor, "FilterGroupsInput");
            var panel = Named(editor, "FilterGroupsPanel");
            Assert.Equal(Xaml + "ToggleButton", picker.Name);
            Assert.Equal("{StaticResource EditorSitePicker}", (string?)picker.Attribute("Style"));
            Assert.Equal("{x:Bind ViewModel.FilterGroupsExpanded, Mode=TwoWay}", (string?)picker.Attribute("IsChecked"));
            Assert.Equal("{x:Bind ViewModel.FilterSummary, Mode=OneWay}",
                (string?)picker.Descendants(Xaml + "TextBlock").Single().Attribute("Text"));
            Assert.Equal(Xaml + "Border", panel.Name);
            Assert.True(ReferenceEquals(picker.ElementsAfterSelf().FirstOrDefault(), panel), "规则组面板应紧接在字段下面");
            Assert.Equal("{x:Bind ViewModel.FilterGroupsPanelVisibility, Mode=OneWay}", (string?)panel.Attribute("Visibility"));
            Assert.True(panel.Attribute("Width") is null && panel.Attribute("MaxWidth") is null
                && (string?)panel.Attribute("HorizontalAlignment") is null or "Stretch", "规则组面板应随字段拉伸");
            Assert.False(picker.Parent!.Descendants().Any(element => element.Name.LocalName.Contains("Flyout", StringComparison.Ordinal)),
                "规则组又使用了浮层");
        });
        Test("编辑订阅：规则组展开后复用站点选项行并保留清空入口", () =>
        {
            var editor = Read("src/Momoka.Shell/Views/MoviePilotSubscriptionEditor.xaml");
            var panel = Named(editor, "FilterGroupsPanel");
            var sitesPanel = Named(editor, "SubscriptionSitesPanel");
            foreach (var property in new[] { "Margin", "Background", "BorderBrush", "BorderThickness", "CornerRadius" })
                Assert.Equal((string?)sitesPanel.Attribute(property), (string?)panel.Attribute(property), property);

            var list = panel.Descendants(Xaml + "ScrollViewer").Single();
            Assert.Equal("260", (string?)list.Attribute("MaxHeight"));
            Assert.Equal("Auto", (string?)list.Attribute("VerticalScrollBarVisibility"));
            var options = list.Descendants(Xaml + "ItemsControl").Single();
            Assert.Equal("{x:Bind ViewModel.FilterGroups, Mode=OneWay}", (string?)options.Attribute("ItemsSource"));
            Assert.Equal("{StaticResource EditorSiteOptionTemplate}", (string?)options.Attribute("ItemTemplate"));
            var clear = Named(editor, "ClearEditorFilters");
            Assert.True(clear.Ancestors().Contains(panel), "跟随系统入口应在规则组面板内");
            Assert.Equal("跟随系统", (string?)clear.Attribute("Content"));
            Assert.Equal("OnClearFilters", (string?)clear.Attribute("Click"));
        });
        Test("编辑订阅：订阅站点是与搜索周期同一套壳的就地展开字段", () =>
        {
            // 2026-11-20 用户先拿参考图要「54 高、缺口标签、地球图标」；实屏后改口「不能改成跟下面
            // 搜索周期一样的吗」，并确认列表仍就地展开。于是壳换回 ComboBox 那档，展开行为不变。
            var editor = Read("src/Momoka.Shell/Views/MoviePilotSubscriptionEditor.xaml");
            var resources = editor.Root!.Element(Xaml + "UserControl.Resources")!;

            var fieldStyle = Resource(resources, "EditorSitePicker");
            Assert.Equal("ToggleButton", (string?)fieldStyle.Attribute("TargetType"));
            Assert.Equal("{StaticResource DefaultToggleButtonStyle}", (string?)fieldStyle.Attribute("BasedOn"));
            var fieldSetters = fieldStyle.Elements(Xaml + "Setter").ToDictionary(
                setter => (string)setter.Attribute("Property")!, setter => (string?)setter.Attribute("Value"));
            // 与搜索周期的 ComboBox 同一档：32 高、10 圆角（EgSearchCornerRadius）、1 像素边、
            // 12,5,0,7 内边距、组合框的底与描边。
            Assert.Equal("32", fieldSetters["MinHeight"], "订阅站点字段没有跟搜索周期一样高");
            Assert.Equal("{StaticResource EgSearchCornerRadius}", fieldSetters["CornerRadius"]);
            Assert.Equal("1", fieldSetters["BorderThickness"]);
            Assert.Equal("12,5,0,7", fieldSetters["Padding"]);
            Assert.Equal("{ThemeResource ComboBoxBackground}", fieldSetters["Background"]);
            Assert.Equal("{ThemeResource ComboBoxBorderBrush}", fieldSetters["BorderBrush"]);
            Assert.Equal("Stretch", fieldSetters["HorizontalAlignment"]);
            Assert.Equal("Stretch", fieldSetters["HorizontalContentAlignment"]);

            var field = Named(editor, "SubscriptionSitesInput");
            Assert.Equal(Xaml + "ToggleButton", field.Name);
            Assert.Equal("{StaticResource EditorSitePicker}", (string?)field.Attribute("Style"));
            Assert.Equal("{x:Bind ViewModel.SitesExpanded, Mode=TwoWay}", (string?)field.Attribute("IsChecked"));
            Assert.Equal("订阅站点", (string?)field.Attribute("AutomationProperties.Name"));
            // 三态颜色接 ComboBox 的键：收起是组合框的底与边，展开时描边接强调色，并按 ThemeDictionaries 解析。
            var fieldResources = field.Element(Xaml + "ToggleButton.Resources")!
                .Element(Xaml + "ResourceDictionary")!
                .Element(Xaml + "ResourceDictionary.ThemeDictionaries")!
                .Elements(Xaml + "ResourceDictionary")
                .Single(element => (string?)element.Attribute(X + "Key") == "Dark");
            var keys = fieldResources.Elements()
                .Select(brush => (string?)brush.Attribute(X + "Key")).ToHashSet();
            foreach (var key in new[] { "ToggleButtonBorderBrushChecked", "ToggleButtonBackgroundChecked",
                "ToggleButtonBorderBrushPointerOver", "ToggleButtonForeground" })
                Assert.True(keys.Contains(key), "订阅站点字段缺颜色键 " + key);
            foreach (var pickerName in new[] { "SubscriptionSitesInput", "FilterGroupsInput" })
                foreach (var state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
                {
                    var local = Named(editor, pickerName).Element(Xaml + "ToggleButton.Resources")!
                        .Element(Xaml + "ResourceDictionary")!
                        .Element(Xaml + "ResourceDictionary.ThemeDictionaries")!
                        .Elements(Xaml + "ResourceDictionary")
                        .Single(element => (string?)element.Attribute(X + "Key") == "Dark");
                    var checkedBorder = local.Elements().Single(brush =>
                        (string?)brush.Attribute(X + "Key") == "ToggleButtonBorderBrush" + state);
                    Assert.Equal(Xaml + "StaticResource", checkedBorder.Name);
                    Assert.Equal("EgAccentBrush", (string?)checkedBorder.Attribute("ResourceKey"),
                        "选中描边必须复用 ThemeHost 会就地更新、且高对比度已有系统映射的强调画刷");
                }
            var hover = fieldResources.Elements()
                .Single(brush => (string?)brush.Attribute(X + "Key") == "ToggleButtonBackgroundPointerOver");
            Assert.Equal("ComboBoxBackgroundPointerOver", (string?)hover.Attribute("ResourceKey"),
                "悬停底色没有接组合框那一档");
            // 字段里：已选摘要 + 会翻面的箭头，地球图标跟缺口标签一起退役。
            Assert.False(field.Descendants(Xaml + "FontIcon").Any(icon => (string?)icon.Attribute("Glyph") == "\uE774"),
                "订阅站点字段又放回了地球图标");
            Assert.Equal("{x:Bind ViewModel.SitesSummary, Mode=OneWay}",
                (string?)field.Descendants(Xaml + "TextBlock").Single().Attribute("Text"));
            var chevron = field.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "SiteChevron");
            Assert.Equal("FontIcon", chevron.Name.LocalName);
            Assert.Equal("\uE70D", (string?)chevron.Attribute("Glyph"));
            Assert.Equal("38", (string?)field.Descendants(Xaml + "ColumnDefinition").Last().Attribute("Width"),
                "箭头列不是 ComboBox 那档 38");
            Assert.Equal("0,0,14,0", (string?)chevron.Attribute("Margin"));
            Assert.Equal("{ThemeResource ComboBoxDropDownGlyphForeground}", (string?)chevron.Attribute("Foreground"));
            var rotation = chevron.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "SiteChevronRotation");
            Assert.Equal("6", (string?)rotation.Attribute("CenterX"));
            Assert.Equal("6", (string?)rotation.Attribute("CenterY"));
            var code = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionEditor.xaml.cs"));
            Assert.True(code.Contains("SyncChoicePanel(ViewModel.SitesExpanded, SiteChevronRotation, SubscriptionSitesPanel)", StringComparison.Ordinal)
                && code.Contains("rotation.Angle = expanded ? 180 : 0", StringComparison.Ordinal),
                "展开时箭头没有翻上去");

            // 标签回到框上方，与「质量」「搜索周期」同级：正文样式、4 的间距，不再有缺口垫片。
            var siteFields = Named(editor, "SiteFields");
            Assert.True(siteFields.Attribute("Margin") is null, "订阅站点还在自己加字段组上边距");
            var label = Named(editor, "SubscriptionSitesLabel");
            Assert.Equal("订阅站点", (string?)label.Attribute("Text"));
            Assert.Equal("{StaticResource EgBodyStyle}", (string?)label.Attribute("Style"));
            Assert.Equal("0,0,0,4", (string?)label.Attribute("Margin"));
            Assert.False(editor.Descendants(Xaml + "Border").Any(border =>
                    (string?)border.Attribute(X + "Name") == "SubscriptionSitesLabelPlate"),
                "缺口标签的垫片又回来了");

            // 展开的列表是字段下面一块独立的面板，把内容往下推；不再有浮层，也没有「跟随系统」那一行。
            var panel = Named(editor, "SubscriptionSitesPanel");
            Assert.Equal(Xaml + "Border", panel.Name);
            Assert.Equal("{x:Bind ViewModel.SitesPanelVisibility, Mode=OneWay}", (string?)panel.Attribute("Visibility"));
            Assert.Equal("{StaticResource EgSearchCornerRadius}", (string?)panel.Attribute("CornerRadius"));
            Assert.Equal("0,2,0,0", (string?)panel.Attribute("Margin"));
            Assert.Equal("0,4", (string?)panel.Attribute("Padding"));
            var options = panel.Descendants(Xaml + "ItemsControl").Single();
            Assert.Equal("{x:Bind ViewModel.Sites, Mode=OneWay}", (string?)options.Attribute("ItemsSource"));
            Assert.Equal("{StaticResource EditorSiteOptionTemplate}", (string?)options.Attribute("ItemTemplate"));
            Assert.Equal("260", (string?)panel.Descendants(Xaml + "ScrollViewer").Single().Attribute("MaxHeight"));
            Assert.False(editor.Descendants(Xaml + "HyperlinkButton").Any(link => (string?)link.Attribute("Content") == "跟随系统"
                    && (string?)link.Attribute(X + "Name") == "ClearEditorSites"),
                "订阅站点又放回了「跟随系统」清空行");
            Assert.False(File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views",
                "MoviePilotSubscriptionEditor.xaml.cs")).Contains("OnClearSites", StringComparison.Ordinal),
                "订阅站点还留着清空站点的处理器");

            var optionStyle = Resource(resources, "EditorSiteOption");
            var optionSetters = optionStyle.Elements(Xaml + "Setter").ToDictionary(
                setter => (string)setter.Attribute("Property")!, setter => (string?)setter.Attribute("Value"));
            // 用户一句「展开的选项太大了」：从 48 高／24 内缩／16 号改成组合框下拉那一档。
            Assert.Equal("32", optionSetters["MinHeight"], "站点行没有跟组合框下拉一样高");
            Assert.Equal("0,1", optionSetters["Margin"]);
            Assert.Equal("11,5,11,5", optionSetters["Padding"]);
            var optionTemplate = Resource(resources, "EditorSiteOptionTemplate");
            Assert.Equal("{StaticResource EgBodyStyle}",
                (string?)optionTemplate.Descendants(Xaml + "TextBlock").Single().Attribute("Style"),
                "站点行的文字没有回到 14 号正文");
            Assert.Equal("{StaticResource EditorSiteOption}",
                (string?)optionTemplate.Descendants(Xaml + "CheckBox").Single().Attribute("Style"));
        });
        Test("搜索输入圆角：所有产品 AutoSuggestBox 继承共享样式，无局部旧值或遮蔽", () =>
        {
            var count = 0;
            foreach (var (path, document) in ProductXaml())
            {
                foreach (var search in document.Descendants(Xaml + "AutoSuggestBox"))
                {
                    AssertSearchSuggestBox(path, search);
                    count++;
                }
                foreach (var style in document.Descendants(Xaml + "Style").Where(element =>
                    (string?)element.Attribute("TargetType") == "AutoSuggestBox"))
                    Assert.Equal("src/Momoka.Shell/Theme/Styles.xaml", path);
                foreach (var radius in document.Descendants().Where(element =>
                    (string?)element.Attribute(X + "Key") == SearchRadiusKey))
                    Assert.True(path == "src/Momoka.Shell/Theme/Styles.xaml" && radius.Parent == document.Root,
                        path + " 不局部重定义共享搜索圆角");
            }
            Assert.Equal(10, count, "番剧、主搜索、标题搜索、版本、整理、音乐与两处设置搜索均纳入检查");
            foreach (var page in new[] { "LibraryPage.xaml", "MoviePilotVersionsView.xaml" })
            {
                var search = Named(Read("src/Momoka.Shell/Views/" + page), "SearchInput");
                Assert.Equal(SearchRadius, (string?)search.Attribute("CornerRadius"), page);
                Assert.Equal("{StaticResource EgSearchTextBoxStyle}", (string?)search.Attribute("TextBoxStyle"));
            }
        });
        Test("搜索输入圆角：六处普通关键词框继续接共享键", () =>
        {
            foreach (var (page, name) in SearchTextInputs)
            {
                var search = Named(Read("src/Momoka.Shell/Views/" + page), name);
                Assert.Equal(Xaml + "TextBox", search.Name);
                Assert.Equal(SearchRadius, (string?)search.Attribute("CornerRadius"), page + "/" + name);
            }
        });
        Test("搜索输入圆角：内存反例拒绝样式夹带颜色和搜索恢复旧 20 DIP", () =>
        {
            var extraColor = Read("src/Momoka.Shell/Theme/Styles.xaml");
            AssertSearchDictionary(extraColor);
            ImplicitStyle(extraColor, "AutoSuggestBox").Add(new XElement(Xaml + "Setter",
                new XAttribute("Property", "Background"), new XAttribute("Value", "Transparent")));
            Assert.Throws<AssertionException>(() => AssertSearchDictionary(extraColor));
            const string libraryPath = "src/Momoka.Shell/Views/LibraryPage.xaml";
            var oldRadius = Named(Read(libraryPath), "SearchInput");
            AssertSearchSuggestBox(libraryPath, oldRadius);
            oldRadius.SetAttributeValue("CornerRadius", "20");
            Assert.Throws<AssertionException>(() => AssertSearchSuggestBox(libraryPath, oldRadius));
        });
    }

    private static void AssertSearchDictionary(XDocument document)
    {
        AssertSharedRadius(document, SearchRadiusKey);
        var style = ImplicitStyle(document, "AutoSuggestBox");
        Assert.Equal(document.Root, style.Parent, "隐式搜索样式定义在共享字典顶层");
        AssertRadiusStyle(style, "AutoSuggestBox", "{StaticResource DefaultAutoSuggestBoxStyle}", SearchRadius);
    }

    private static void AssertGlobalInputStyles(XDocument document)
    {
        AssertSearchDictionary(document);
        AssertSharedRadius(document, "EgPickerCornerRadius");
        foreach (var type in new[] { "TextBox", "PasswordBox", "ComboBox" })
        {
            AssertRadiusStyle(Resource(document.Root!, "Eg" + type + "Style"), type,
                $"{{StaticResource Default{type}Style}}", SearchRadius);
            var style = ImplicitStyle(document, type);
            Assert.Equal($"{{StaticResource Eg{type}Style}}", (string?)style.Attribute("BasedOn"));
            Assert.Equal(0, style.Elements().Count());
        }
        var number = ImplicitStyle(document, "NumberBox");
        Assert.True(number.Attribute("BasedOn") is null, "数字框保持类型默认模板，无命名默认样式可继承");
        Assert.Equal(SearchRadius, (string?)number.Elements(Xaml + "Setter").Single().Attribute("Value"));
        Assert.Equal("CornerRadius", (string?)number.Elements(Xaml + "Setter").Single().Attribute("Property"));
        Assert.Equal("{StaticResource EgComboBoxStyle}", (string?)ImplicitStyle(document, "local:EgPicker").Attribute("BasedOn"));
        foreach (var key in new[] { ControlRadiusKey, OverlayRadiusKey })
            AssertSharedRadius(document, key);
    }

    private static void AssertSharedRadius(XDocument document, string key)
    {
        var radius = document.Descendants().Single(element => (string?)element.Attribute(X + "Key") == key);
        Assert.Equal(Xaml + "CornerRadius", radius.Name);
        Assert.Equal(document.Root, radius.Parent, key + " 由应用共享字典统一提供");
        Assert.True(double.TryParse(radius.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) && value >= 0, key + " 须声明有效的统一半径");
    }

    private static void AssertProductInputRadii(string path, XDocument document, XDocument shared)
    {
        foreach (var control in document.Descendants().Where(element =>
            SettingsInputTypes.Contains(element.Name.LocalName, StringComparer.Ordinal)
                || element.Name.LocalName is "EgPicker"))
        {
            var radius = (string?)control.Attribute("CornerRadius");
            Assert.True(radius is null || IsSharedRadius(radius),
                path + "/" + control.Name.LocalName + " 自设旧圆角");
            var style = (string?)control.Attribute("Style");
            if (style is not null) Assert.True(StyleHasSharedRadius(style, document, shared, []), path + "/" + style);
        }
        foreach (var style in document.Descendants(Xaml + "Style").Where(style =>
            SettingsInputTypes.Contains((string?)style.Attribute("TargetType") ?? "", StringComparer.Ordinal) ||
            (string?)style.Attribute("TargetType") == "local:EgPicker"))
        {
            if (style.Attribute(X + "Key") is null && path != "src/Momoka.Shell/Theme/Styles.xaml")
            {
                var radiusSetter = style.Elements(Xaml + "Setter").FirstOrDefault(setter =>
                    (string?)setter.Attribute("Property") == "CornerRadius");
                var inheritsRadius = (string?)style.Attribute("BasedOn") == GlobalComboStyle ||
                    StyleHasSharedRadius((string?)style.Attribute("BasedOn") ?? "", document, shared, []);
                Assert.True(radiusSetter is not null && IsSharedRadius((string?)radiusSetter.Attribute("Value")) || inheritsRadius,
                    path + " 局部隐式样式绕过全站圆角");
            }
            foreach (var setter in style.Elements(Xaml + "Setter").Where(setter =>
                (string?)setter.Attribute("Property") == "CornerRadius"))
                Assert.True(IsSharedRadius((string?)setter.Attribute("Value")), path + " 样式自设旧圆角");
        }
    }

    private static bool StyleHasSharedRadius(string reference, XDocument local, XDocument shared, HashSet<string> seen)
    {
        if (!reference.StartsWith("{StaticResource ", StringComparison.Ordinal) || !reference.EndsWith('}')) return false;
        var key = reference[16..^1];
        if (!seen.Add(key)) return false;
        var style = local.Descendants(Xaml + "Style").FirstOrDefault(element => (string?)element.Attribute(X + "Key") == key)
            ?? shared.Descendants(Xaml + "Style").FirstOrDefault(element => (string?)element.Attribute(X + "Key") == key);
        if (style is null) return false;
        var radius = style.Elements(Xaml + "Setter").FirstOrDefault(setter => (string?)setter.Attribute("Property") == "CornerRadius");
        if (radius is not null) return IsSharedRadius((string?)radius.Attribute("Value"));
        return StyleHasSharedRadius((string?)style.Attribute("BasedOn") ?? "", local, shared, seen);
    }

    private static bool IsSharedRadius(string? value) => value is SearchRadius or "{StaticResource EgPickerCornerRadius}";

    private static void AssertSearchSuggestBox(string path, XElement search)
    {
        var identity = path + "/" + (string?)search.Attribute(X + "Name");
        foreach (var property in new[] { "Style", "Template", "CornerRadius" })
            Assert.True(search.Element(Xaml + "AutoSuggestBox." + property) is null,
                identity + " 不用属性元素绕过共享圆角或平台模板");
        Assert.True(search.Attribute("Style") is null && search.Attribute("Template") is null, identity);
        var radius = (string?)search.Attribute("CornerRadius");
        Assert.True(radius is null || radius == SearchRadius,
            identity + " 必须继承或显式引用全站统一输入圆角");
    }

    private static void AssertRadiusStyle(XElement style, string type, string basedOn, string radius = SearchRadius)
    {
        Assert.Equal(Xaml + "Style", style.Name);
        Assert.Equal(type, (string?)style.Attribute("TargetType"));
        Assert.Equal(basedOn, (string?)style.Attribute("BasedOn"));
        Assert.Equal(1, style.Elements().Count(), type + " 不改颜色、模板、绑定或内边距");
        var setter = style.Elements().Single();
        Assert.Equal(Xaml + "Setter", setter.Name);
        Assert.Equal("CornerRadius", (string?)setter.Attribute("Property"));
        Assert.Equal(radius, (string?)setter.Attribute("Value"));
        Assert.Equal(0, setter.Elements().Count());
    }

    private static void AssertSettingsInputControls(string name, XDocument page)
    {
        Assert.False(page.Descendants().Any(element =>
            (string?)element.Attribute(X + "Key") is "EgSettingsInputCornerRadius" or "EgSettingsComboBoxStyle"),
            name + " 不保留已退役设置输入键");
        foreach (var control in page.Descendants().Where(element => element.Name.Namespace == Xaml &&
            SettingsInputTypes.Contains(element.Name.LocalName, StringComparer.Ordinal)))
        {
            var identity = name + "/" + ((string?)control.Attribute(X + "Name") ?? control.Name.LocalName);
            Assert.True(control.Attribute("Style") is null, identity + " 不绕过页面输入样式");
            var radius = (string?)control.Attribute("CornerRadius");
            Assert.True(radius is null || radius == SearchRadius, identity + " 自设旧圆角");
            // NumberBox 模板内 InputBox 显式使用 NumberBoxTextBoxStyle，并把自身圆角传给它。
            // 因而不能只靠局部 TextBox 隐式样式；数字框本身必须直接接入同一键。
            if (control.Name.LocalName == "NumberBox" || (string?)control.Attribute(X + "Name") == "SettingsSearchBox")
                Assert.Equal(SearchRadius, radius, identity);
        }
        var localInputStyles = page.Descendants(Xaml + "Style").Where(element =>
            SettingsInputTypes.Contains((string?)element.Attribute("TargetType") ?? "", StringComparer.Ordinal)).ToArray();
        Assert.Equal(name == "SettingsPage.xaml" ? 2 : 0, localInputStyles.Length,
            name + " 不能用页面内新输入样式盖掉全局字典");
    }

    private static XElement ImplicitStyle(XDocument document, string type)
    {
        var styles = document.Descendants(Xaml + "Style").Where(element =>
            (string?)element.Attribute("TargetType") == type && element.Attribute(X + "Key") is null).ToArray();
        Assert.Equal(1, styles.Length, type + " 隐式样式缺失或重复");
        return styles.Single();
    }

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    private static ThemeColor Pick(string key, UiTheme theme) =>
        InputControlTheme.Brushes.Single(entry => entry.Key == key).Pick(theme);

    private static void AssertBrush(XElement dictionary, string key, string color) =>
        Assert.Equal($"{{StaticResource {color}}}", (string?)Resource(dictionary, key).Attribute("Color"),
            $"{dictionary.Attribute(X + "Key")?.Value}/{key}");

    private static void AssertAlias(XElement dictionary, string key, string resource) =>
        Assert.Equal(resource, (string?)Resource(dictionary, key).Attribute("ResourceKey"),
            $"{dictionary.Attribute(X + "Key")?.Value}/{key}");

    private static XElement Resource(XElement dictionary, string key) =>
        dictionary.Elements().Single(element => (string?)element.Attribute(X + "Key") == key);

    private static IEnumerable<XElement> Dictionaries() => Palette().Descendants(Xaml + "ResourceDictionary")
        .Where(dictionary => (string?)dictionary.Attribute(X + "Key") is "Dark" or "Light");

    private static XDocument Palette() => Read("src/Momoka.Shell/Theme/Palette.xaml");

    private static XDocument Read(string relativePath) =>
        XDocument.Load(Path.Combine(Root(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static IEnumerable<string> FindLocalCornerRadii(string path, XDocument document, HashSet<string>? protectedKeys = null)
    {
        foreach (var element in document.Descendants().Where(element => element.Name.Namespace == Xaml))
        {
            var key = (string?)element.Attribute(X + "Key");
            if (path == StylesFile && element.Parent == document.Root && element.Name == Xaml + "CornerRadius" && key is not null)
                continue;

            var line = ((System.Xml.IXmlLineInfo)element).LineNumber;
            if (key is not null && protectedKeys?.Contains(key) == true)
                yield return $"{path}:{line} 局部覆盖了共享圆角 {key}";

            var value = element.Attributes().FirstOrDefault(attribute => IsCornerRadius(attribute.Name.LocalName))?.Value;
            if (element.Name == Xaml + "Setter" &&
                (IsCornerRadius((string?)element.Attribute("Property")) || IsCornerRadius((string?)element.Attribute("Target"))))
                value = (string?)element.Attribute("Value");
            if (!element.HasElements && IsCornerRadius(element.Name.LocalName)) value = element.Value;

            var radius = value?.Trim();
            if (string.IsNullOrEmpty(radius) || radius == "0") continue;

            const string staticResource = "{StaticResource ";
            const string themeResource = "{ThemeResource ";
            var prefix = radius.StartsWith(staticResource, StringComparison.Ordinal) ? staticResource : themeResource;
            if (protectedKeys is not null && radius.StartsWith(prefix, StringComparison.Ordinal) && radius.EndsWith('}'))
            {
                var resource = radius[prefix.Length..^1];
                if (!protectedKeys.Contains(resource)) yield return $"{path}:{line} 引用了未集中声明的圆角 {resource}";
            }
            if (radius[0] == '{') continue;

            var owner = element.AncestorsAndSelf().Select(node => (string?)node.Attribute(X + "Name") ??
                (string?)node.Attribute(X + "Key")).FirstOrDefault(name => name is not null);
            yield return $"{path}:{line} {owner ?? element.Name.LocalName} 使用局部圆角 {radius}";
        }
    }

    private static bool IsCornerRadius(string? name) =>
        name == "CornerRadius" || name?.EndsWith(".CornerRadius", StringComparison.Ordinal) == true;

    private static IEnumerable<(string Path, XDocument Document)> ProductXaml()
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Momoka.Shell"),
            "*.xaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Root(), path).Replace('\\', '/');
            if (relative.StartsWith("src/Momoka.Shell/bin/", StringComparison.Ordinal) ||
                relative.StartsWith("src/Momoka.Shell/obj/", StringComparison.Ordinal)) continue;
            yield return (relative, XDocument.Load(path, LoadOptions.SetLineInfo));
        }
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
