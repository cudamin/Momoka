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
    private const string SettingsInputDictionary = "src/Momoka.Shell/Theme/SettingsInputs.xaml";
    private const string SettingsInputSource = "ms-appx:///Theme/SettingsInputs.xaml";
    private const string SettingsInputRadiusKey = "EgSettingsInputCornerRadius";
    private const string SettingsInputRadius = "{StaticResource EgSettingsInputCornerRadius}";
    private const string SettingsComboStyle = "{StaticResource EgSettingsComboBoxStyle}";
    private const string SearchRadiusKey = "EgSearchCornerRadius";
    private const string SearchRadius = "{StaticResource EgSearchCornerRadius}";
    private static readonly (string Page, string Name)[] SearchTextInputs =
    [
        ("DanmakuPanel.xaml", "TitleBox"), ("FnNasMatchDialog.xaml", "KeywordBox"),
        ("IdentifyDialog.xaml", "NameBox"), ("MoviePilotDownloadDialog.xaml", "DownloadSearchText"),
        ("MoviePilotResourceBrowser.xaml", "FilterText"), ("MoviePilotSubscriptionEditor.xaml", "KeywordInput")
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
            foreach (var theme in UiThemes.All)
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
            foreach (var theme in UiThemes.All)
                Assert.Equal(theme.IsDark ? theme.Colors.SurfaceHover : theme.Colors.SurfaceAlt,
                    InputControlTheme.Glass(theme), theme.Id);
        });
        Test("输入主题：聚焦和悬停只加强中性边线，不使用强调色", () =>
        {
            foreach (var theme in UiThemes.All)
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
            var resources = scroll.Element(Xaml + "ScrollViewer.Resources")!;
            AssertAlias(resources, "TextControlBackground", "EgInputGlassBrush");
            AssertAlias(resources, "TextControlBackgroundPointerOver", "EgInputGlassBrush");
            Assert.True(!resources.Elements().Any(element =>
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
        Test("输入主题：详情文件选项与上方按钮同底，集列表牌子不再挂下拉", () =>
        {
            var page = Read("src/Momoka.Shell/Views/DetailPage.xaml");
            foreach (var name in new[] { "ColumnPickers", "PickerPanel" })
            {
                var panel = page.Descendants(Xaml + "StackPanel")
                    .Single(element => (string?)element.Attribute(X + "Name") == name);
                var resources = panel.Element(Xaml + "StackPanel.Resources")!;
                foreach (var key in ComboBackgroundKeys)
                {
                    var state = key["ComboBoxBackground".Length..];
                    var target = "ButtonBackground" + (state is "Focused" or "Unfocused" ? "" : state);
                    AssertAlias(resources, key, target);
                }
                Assert.False(resources.Elements().Any(element =>
                    (string?)element.Attribute(X + "Key") == "ComboBoxDropDownBackground"), "弹层背景被局部覆盖");
                var pickers = panel.Descendants().Where(element => element.Name.LocalName == "EgPicker").ToArray();
                Assert.Equal(3, pickers.Length, name + " 的文件选项范围变了");
                foreach (var picker in pickers)
                {
                    Assert.Equal("{ThemeResource ButtonBackground}", (string?)picker.Attribute("Background"));
                    Assert.Equal("{StaticResource EgPickerCornerRadius}", (string?)picker.Attribute("CornerRadius"));
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
        Test("设置输入圆角：局部字典只改闭合表面的 10 DIP，保留平台模板", () =>
            AssertSettingsInputDictionary(Read(SettingsInputDictionary)));
        Test("设置输入圆角：设置与五个飞牛原生页都在 Page.Resources 接入", () =>
        {
            foreach (var name in SettingsInputPages)
                AssertSettingsInputImport(Read("src/Momoka.Shell/Views/" + name));
        });
        Test("设置输入圆角：页面入口保留，设置与详情旧键都引用全站圆角", () =>
        {
            var allowed = SettingsInputPages.Select(name => "src/Momoka.Shell/Views/" + name)
                .Append(SettingsInputDictionary).ToHashSet(StringComparer.Ordinal);
            foreach (var (relative, document) in ProductXaml())
            {
                if (!document.Descendants().Attributes().Any(attribute =>
                    attribute.Value.Contains("SettingsInputs.xaml", StringComparison.Ordinal) ||
                    attribute.Value.Contains(SettingsInputRadiusKey, StringComparison.Ordinal) ||
                    attribute.Value.Contains("EgSettingsComboBoxStyle", StringComparison.Ordinal))) continue;
                Assert.True(allowed.Contains(relative), relative + " 引用了设置专用输入资源");
            }
            var styles = Read("src/Momoka.Shell/Theme/Styles.xaml");
            AssertAlias(styles.Root!, "EgPickerCornerRadius", SearchRadiusKey);
            AssertAlias(Read(SettingsInputDictionary).Root!, SettingsInputRadiusKey, SearchRadiusKey);
        });
        Test("设置输入圆角：六页没有局部旧圆角或显式样式绕过，数字框直接接键", () =>
        {
            foreach (var name in SettingsInputPages)
                AssertSettingsInputControls(name, Read("src/Momoka.Shell/Views/" + name));
            var libraries = Read("src/Momoka.Shell/Views/FnNasLibrariesPage.xaml");
            var number = Named(libraries, "FnNasMinimumFileSize");
            Assert.Equal(SettingsInputRadius, (string?)number.Attribute("CornerRadius"));
            Assert.Equal("{x:Bind ViewModel.MinimumFileSize, Mode=TwoWay}", (string?)number.Attribute("Value"));
            Assert.Equal("{x:Bind ViewModel.MinimumSizeVisibility, Mode=OneWay}", (string?)number.Attribute("Visibility"));
        });
        Test("设置输入圆角：设置下拉仍用原列表项，搜索颜色内边距与绑定不变", () =>
        {
            var page = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var combo = ImplicitStyle(page, "ComboBox");
            Assert.Equal(SettingsComboStyle, (string?)combo.Attribute("BasedOn"));
            var itemSetter = combo.Elements().Single();
            Assert.Equal("ItemContainerStyle", (string?)itemSetter.Attribute("Property"));
            Assert.Equal("{StaticResource EgComboItemStyle}", (string?)itemSetter.Attribute("Value"));
            var itemStyle = page.Descendants(Xaml + "Style")
                .Single(element => (string?)element.Attribute(X + "Key") == "EgComboItemStyle");
            Assert.Equal("4", (string?)itemStyle.Elements(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "CornerRadius").Attribute("Value"));
            Assert.Equal(1, itemStyle.Descendants(Xaml + "ControlTemplate").Count());

            var searchStyle = page.Descendants(Xaml + "Style")
                .Single(element => (string?)element.Attribute(X + "Key") == "SettingsSearchTextBox");
            Assert.Equal("{StaticResource AutoSuggestBoxTextBoxStyle}", (string?)searchStyle.Attribute("BasedOn"));
            Assert.Equal(2, searchStyle.Elements().Count());
            Assert.Equal("32,6,8,6", (string?)searchStyle.Elements(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "Padding").Attribute("Value"));
            Assert.Equal(SettingsInputRadius, (string?)searchStyle.Elements(Xaml + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "CornerRadius").Attribute("Value"));
            var search = Named(page, "SettingsSearchBox");
            Assert.Equal(SettingsInputRadius, (string?)search.Attribute("CornerRadius"));
            Assert.Equal("{StaticResource SettingsSearchTextBox}", (string?)search.Attribute("TextBoxStyle"));
            Assert.Equal("{ThemeResource EgWindowBrush}", (string?)search.Attribute("Background"));
            Assert.Equal("{x:Bind ViewModel.SettingsSearchQuery, Mode=TwoWay}", (string?)search.Attribute("Text"));
            var shortcuts = Named(page, "ShortcutSearchBox");
            Assert.Equal("Find", (string?)shortcuts.Attribute("QueryIcon"));
            Assert.Equal("{x:Bind ViewModel.ShortcutSearchQuery, Mode=TwoWay}", (string?)shortcuts.Attribute("Text"));
            Assert.True(shortcuts.Attribute("TextBoxStyle") is null, "快捷键搜索不替换平台文本样式");
            Assert.True(shortcuts.Attribute("Background") is null, "快捷键搜索不覆盖卡片区背景规则");
        });
        Test("设置输入圆角：拒绝错误值、缺失类型、错误继承和非圆角 setter", () =>
        {
            var original = Read(SettingsInputDictionary);
            AssertSettingsInputDictionary(original);
            var wrongRadius = new XDocument(original);
            Resource(wrongRadius.Root!, SettingsInputRadiusKey).SetAttributeValue("ResourceKey", "ControlCornerRadius");
            Assert.Throws<AssertionException>(() => AssertSettingsInputDictionary(wrongRadius));
            foreach (var type in new[] { "TextBox", "PasswordBox", "AutoSuggestBox" })
            {
                var missing = new XDocument(original);
                ImplicitStyle(missing, type).Remove();
                Assert.Throws<AssertionException>(() => AssertSettingsInputDictionary(missing), type);
                var wrongBase = new XDocument(original);
                ImplicitStyle(wrongBase, type).SetAttributeValue("BasedOn", "{StaticResource DefaultButtonStyle}");
                Assert.Throws<AssertionException>(() => AssertSettingsInputDictionary(wrongBase), type);
                var extraSetter = new XDocument(original);
                ImplicitStyle(extraSetter, type).Add(new XElement(Xaml + "Setter",
                    new XAttribute("Property", "Background"), new XAttribute("Value", "Transparent")));
                Assert.Throws<AssertionException>(() => AssertSettingsInputDictionary(extraSetter), type);
            }
            var wrongCombo = new XDocument(original);
            ImplicitStyle(wrongCombo, "ComboBox").SetAttributeValue("BasedOn", "{StaticResource DefaultComboBoxStyle}");
            Assert.Throws<AssertionException>(() => AssertSettingsInputDictionary(wrongCombo));
            var wrongSetter = new XDocument(original);
            Resource(wrongSetter.Root!, "EgSettingsComboBoxStyle").Element(Xaml + "Setter")!
                .SetAttributeValue("Value", "4");
            Assert.Throws<AssertionException>(() => AssertSettingsInputDictionary(wrongSetter));
        });
        Test("设置输入圆角：拒绝漏接页面或只接卡片滚动区", () =>
        {
            foreach (var name in SettingsInputPages)
            {
                var missing = Read("src/Momoka.Shell/Views/" + name);
                AssertSettingsInputImport(missing);
                SettingsInputImports(missing).Single().Remove();
                Assert.Throws<AssertionException>(() => AssertSettingsInputImport(missing), name);
            }
            var narrow = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var import = SettingsInputImports(narrow).Single();
            import.Remove();
            Named(narrow, "SettingsScroll").Element(Xaml + "ScrollViewer.Resources")!.Add(
                new XElement(Xaml + "ResourceDictionary.MergedDictionaries", import));
            Assert.Throws<AssertionException>(() => AssertSettingsInputImport(narrow));
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
        Test("搜索输入圆角：应用级共享 10 DIP 与设置同档，隐式样式只改圆角", () =>
        {
            var styles = Read("src/Momoka.Shell/Theme/Styles.xaml");
            AssertSearchDictionary(styles);
            AssertAlias(Read(SettingsInputDictionary).Root!, SettingsInputRadiusKey, SearchRadiusKey);
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
        Test("全站输入圆角：五种输入及派生下拉继承平台模板，只有一份数值", () =>
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
                    Assert.True(path is "src/Momoka.Shell/Theme/Styles.xaml" or SettingsInputDictionary,
                        path + " 不另设搜索样式绕过共享圆角");
                foreach (var radius in document.Descendants().Where(element =>
                    (string?)element.Attribute(X + "Key") == SearchRadiusKey))
                    Assert.True(path == "src/Momoka.Shell/Theme/Styles.xaml" && radius.Parent == document.Root,
                        path + " 不局部重定义共享搜索圆角");
            }
            Assert.Equal(8, count, "番剧、主搜索、版本、整理与两处设置搜索均纳入检查");
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
        var radius = Resource(document.Root!, SearchRadiusKey);
        Assert.Equal(Xaml + "CornerRadius", radius.Name);
        Assert.Equal("10", radius.Value.Trim());
        var style = ImplicitStyle(document, "AutoSuggestBox");
        Assert.Equal(document.Root, style.Parent, "隐式搜索样式定义在共享字典顶层");
        AssertRadiusStyle(style, "AutoSuggestBox", "{StaticResource DefaultAutoSuggestBoxStyle}", SearchRadius);
    }

    private static void AssertGlobalInputStyles(XDocument document)
    {
        AssertSearchDictionary(document);
        AssertAlias(document.Root!, "EgPickerCornerRadius", SearchRadiusKey);
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
        Assert.False(document.Descendants().Any(element => (string?)element.Attribute(X + "Key") == "ControlCornerRadius"),
            "不能改变所有平台控件的圆角来代替输入样式");
    }

    private static void AssertProductInputRadii(string path, XDocument document, XDocument shared)
    {
        foreach (var control in document.Descendants().Where(element =>
            SettingsInputTypes.Contains(element.Name.LocalName, StringComparer.Ordinal) || element.Name.LocalName == "EgPicker"))
        {
            var radius = (string?)control.Attribute("CornerRadius");
            Assert.True(radius is null || IsSharedRadius(radius), path + "/" + control.Name.LocalName + " 自设旧圆角");
            var style = (string?)control.Attribute("Style");
            if (style is not null) Assert.True(StyleHasSharedRadius(style, document, shared, []), path + "/" + style);
        }
        foreach (var style in document.Descendants(Xaml + "Style").Where(style =>
            SettingsInputTypes.Contains((string?)style.Attribute("TargetType") ?? "", StringComparer.Ordinal) ||
            (string?)style.Attribute("TargetType") == "local:EgPicker"))
        {
            if (style.Attribute(X + "Key") is null && path != "src/Momoka.Shell/Theme/Styles.xaml" && path != SettingsInputDictionary)
            {
                var radiusSetter = style.Elements(Xaml + "Setter").FirstOrDefault(setter =>
                    (string?)setter.Attribute("Property") == "CornerRadius");
                var inheritsRadius = (string?)style.Attribute("BasedOn") == SettingsComboStyle ||
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

    private static bool IsSharedRadius(string? value) => value is SearchRadius or SettingsInputRadius or "{StaticResource EgPickerCornerRadius}";

    private static void AssertSearchSuggestBox(string path, XElement search)
    {
        var identity = path + "/" + (string?)search.Attribute(X + "Name");
        foreach (var property in new[] { "Style", "Template", "CornerRadius" })
            Assert.True(search.Element(Xaml + "AutoSuggestBox." + property) is null,
                identity + " 不用属性元素绕过共享圆角或平台模板");
        Assert.True(search.Attribute("Style") is null && search.Attribute("Template") is null, identity);
        var radius = (string?)search.Attribute("CornerRadius");
        Assert.True(radius is null || radius == SearchRadius ||
            (path == "src/Momoka.Shell/Views/SettingsPage.xaml" && radius == SettingsInputRadius),
            identity + " 必须继承搜索圆角或设置页同档 10 DIP");
    }

    private static void AssertSettingsInputDictionary(XDocument document)
    {
        var dictionary = document.Root!;
        Assert.Equal(Xaml + "ResourceDictionary", dictionary.Name);
        // 旧设置键保留接线入口，实际数值由全站搜索资源唯一决定。
        Assert.Equal(6, dictionary.Elements().Count(), "只允许一个圆角资源与五条样式");
        var radius = Resource(dictionary, SettingsInputRadiusKey);
        Assert.Equal(Xaml + "StaticResource", radius.Name);
        Assert.Equal(SearchRadiusKey, (string?)radius.Attribute("ResourceKey"));
        foreach (var type in new[] { "TextBox", "PasswordBox", "AutoSuggestBox" })
            AssertRadiusStyle(ImplicitStyle(document, type), type, $"{{StaticResource Default{type}Style}}");
        var keyedCombo = Resource(dictionary, "EgSettingsComboBoxStyle");
        AssertRadiusStyle(keyedCombo, "ComboBox", "{StaticResource DefaultComboBoxStyle}");
        var combo = ImplicitStyle(document, "ComboBox");
        Assert.Equal(SettingsComboStyle, (string?)combo.Attribute("BasedOn"));
        Assert.Equal(0, combo.Elements().Count(), "隐式选择框仅继承具名圆角样式");
    }

    private static void AssertRadiusStyle(XElement style, string type, string basedOn, string radius = SettingsInputRadius)
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

    private static void AssertSettingsInputImport(XDocument page)
    {
        var imports = SettingsInputImports(page).ToArray();
        Assert.Equal(1, imports.Length, "页面须接入一次设置输入字典");
        var import = imports.Single();
        Assert.Equal(Xaml + "ResourceDictionary.MergedDictionaries", import.Parent!.Name);
        var resourceOwner = import.Ancestors()
            .FirstOrDefault(element => element.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal));
        var pageResources = page.Root!.Element(Xaml + "Page.Resources");
        Assert.NotNull(pageResources);
        Assert.Equal(pageResources, resourceOwner,
            "字典必须覆盖整个页面，不能只挂在卡片区或一个控件下");
    }

    private static void AssertSettingsInputControls(string name, XDocument page)
    {
        Assert.False(page.Descendants().Any(element => (string?)element.Attribute(X + "Key") == SettingsInputRadiusKey),
            name + " 不局部重定义圆角键");
        foreach (var control in page.Descendants().Where(element => element.Name.Namespace == Xaml &&
            SettingsInputTypes.Contains(element.Name.LocalName, StringComparer.Ordinal)))
        {
            var identity = name + "/" + ((string?)control.Attribute(X + "Name") ?? control.Name.LocalName);
            Assert.True(control.Attribute("Style") is null, identity + " 不绕过页面输入样式");
            var radius = (string?)control.Attribute("CornerRadius");
            Assert.True(radius is null || radius == SettingsInputRadius, identity + " 自设旧圆角");
            // NumberBox 模板内 InputBox 显式使用 NumberBoxTextBoxStyle，并把自身圆角传给它。
            // 因而不能只靠局部 TextBox 隐式样式；数字框本身必须直接接入同一键。
            if (control.Name.LocalName == "NumberBox" || (string?)control.Attribute(X + "Name") == "SettingsSearchBox")
                Assert.Equal(SettingsInputRadius, radius, identity);
        }
        var localInputStyles = page.Descendants(Xaml + "Style").Where(element =>
            SettingsInputTypes.Contains((string?)element.Attribute("TargetType") ?? "", StringComparer.Ordinal)).ToArray();
        Assert.Equal(name == "SettingsPage.xaml" ? 2 : 0, localInputStyles.Length,
            name + " 不能用页面内新输入样式盖掉共享字典");
    }

    private static IEnumerable<XElement> SettingsInputImports(XDocument document) =>
        document.Descendants(Xaml + "ResourceDictionary")
            .Where(element => (string?)element.Attribute("Source") == SettingsInputSource);

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

    private static IEnumerable<(string Path, XDocument Document)> ProductXaml()
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Momoka.Shell"),
            "*.xaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Root(), path).Replace('\\', '/');
            if (relative.StartsWith("src/Momoka.Shell/bin/", StringComparison.Ordinal) ||
                relative.StartsWith("src/Momoka.Shell/obj/", StringComparison.Ordinal)) continue;
            yield return (relative, XDocument.Load(path));
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
