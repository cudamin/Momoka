using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 阶段五设计收敛契约：圆角尺度统一、设置窗口十个宿主二级页模板对齐、Segoe Fluent 字形、
/// 热区下限（>= 32px）与服务器/账号键盘换序入口。
/// </summary>
internal static class FluentStageFiveTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] HostedSubpages =
    [
        "ServersPage.xaml",
        "NotificationsPage.xaml",
        "DashboardPage.xaml",
        "DiagnosticsPage.xaml",
        "FnNasLibrariesPage.xaml",
        "FnNasPreferencesPage.xaml",
        "FnNasSettingsPage.xaml",
        "FnNasTasksPage.xaml",
        "FnNasUsersPage.xaml",
        "FnNasPasswordPage.xaml"
    ];

    private static readonly string[] FnNasPages =
    [
        "FnNasLibrariesPage.xaml",
        "FnNasPreferencesPage.xaml",
        "FnNasSettingsPage.xaml",
        "FnNasTasksPage.xaml",
        "FnNasUsersPage.xaml",
        "FnNasPasswordPage.xaml"
    ];

    internal static void Register()
    {
        Test("圆角收敛：设置分组卡、商店详情卡、筛选组、进度卡、字幕确认与按钮统一层级", () =>
        {
            var styles = Read("src/Momoka.Shell/Theme/Styles.xaml");
            var storeCard = KeyedStyle(styles, "EgStoreDetailCardStyle");
            Assert.Equal("{StaticResource EgBodyPanelStyle}", (string?)storeCard.Attribute("BasedOn"));
            Assert.Null(SetterValue(storeCard, "CornerRadius"));

            var settings = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var sectionBorder = Named(settings, "SettingsSection").Elements(Xaml + "Border").Single();
            Assert.Equal("{StaticResource EgCardCornerRadius}", (string?)sectionBorder.Attribute("CornerRadius"));

            foreach (var pageName in new[] { "NotificationsPage.xaml", "DashboardPage.xaml" })
            {
                var doc = Read("src/Momoka.Shell/Views/" + pageName);
                var bar = doc.Descendants(Xaml + "CommandBar").Single();
                Assert.Equal("{StaticResource EgCardCornerRadius}", (string?)bar.Attribute("CornerRadius"), pageName);
                Assert.Equal("{ThemeResource EgCardStrokeBrush}", (string?)bar.Attribute("BorderBrush"), pageName);
                Assert.Equal("{StaticResource EgCardBorderThickness}", (string?)bar.Attribute("BorderThickness"), pageName);
            }

            var filter = Read("src/Momoka.Shell/Views/MoviePilotResourceFilterDialog.xaml");
            var filterGroup = filter.Descendants(Xaml + "DataTemplate")
                .Single(t => (string?)t.Attribute(X + "Key") == "ResourceFilterGroupTemplate")
                .Elements(Xaml + "Border").Single();
            Assert.Equal("{StaticResource EgCardCornerRadius}", (string?)filterGroup.Attribute("CornerRadius"));
            foreach (var btnName in new[] { "SelectAllGroup", "ClearGroup" })
                Assert.Equal("32", (string?)Named(filter, btnName).Attribute("MinHeight"), btnName);

            var progress = Read("src/Momoka.Shell/Views/MoviePilotSearchProgressView.xaml");
            var progressCard = Named(progress, "ProgressCard");
            Assert.Equal("{StaticResource EgCardCornerRadius}", (string?)progressCard.Attribute("CornerRadius"));
            Assert.Equal("{ThemeResource EgCardStrokeBrush}", (string?)progressCard.Attribute("BorderBrush"));
            Assert.Equal("{StaticResource EgCardBorderThickness}", (string?)progressCard.Attribute("BorderThickness"));
            foreach (var badge in progress.Descendants(Xaml + "WrapRow").Elements(Xaml + "Border"))
                Assert.Equal("{StaticResource EgCornerBadgeCornerRadius}", (string?)badge.Attribute("CornerRadius"));

            var subtitle = Read("src/Momoka.Shell/Views/SubtitleDialog.xaml");
            var deleteConfirm = Named(subtitle, "DeleteConfirmation");
            Assert.Equal("{StaticResource EgCardCornerRadius}", (string?)deleteConfirm.Attribute("CornerRadius"));
            Assert.Equal("32", (string?)Named(subtitle, "HeaderCloseButton").Attribute("MinWidth"));
            Assert.Equal("32", (string?)Named(subtitle, "HeaderCloseButton").Attribute("MinHeight"));
            Assert.Equal("{StaticResource EgSubpagePrimaryButtonStyle}", (string?)Named(subtitle, "UploadSubtitleButton").Attribute("Style"));
            Assert.Equal("{StaticResource EgSubpagePrimaryButtonStyle}", (string?)Named(subtitle, "SearchButton").Attribute("Style"));
            Assert.Equal("{StaticResource EgDialogSecondaryButtonStyle}", (string?)Named(subtitle, "CancelDeleteButton").Attribute("Style"));
            Assert.Equal("{StaticResource EgDialogSecondaryButtonStyle}", (string?)Named(subtitle, "ConfirmDeleteButton").Attribute("Style"));

            var music = Read("src/Momoka.Shell/Views/MusicLibraryPage.xaml");
            var trackItemStyle = music.Descendants(Xaml + "Style")
                .Single(s => (string?)s.Attribute("TargetType") == "ListViewItem" && s.Attribute(X + "Key") is null);
            Assert.Equal("{StaticResource EgListRowCornerRadius}", SetterValue(trackItemStyle, "CornerRadius"));

            var person = Read("src/Momoka.Shell/Views/PersonPage.xaml");
            var creditStyle = KeyedStyle(person, "PersonCreditRowStyle");
            Assert.Equal("12,8", SetterValue(creditStyle, "Padding"));
            Assert.Equal("{StaticResource EgSearchCornerRadius}", SetterValue(creditStyle, "CornerRadius"));

            var signIn = Read("src/Momoka.Shell/Views/SignInPage.xaml");
            Assert.Equal("{StaticResource EgCardCornerRadius}", (string?)Named(signIn, "WelcomePanel").Attribute("CornerRadius"));
            Assert.Equal("48", SetterValue(KeyedStyle(signIn, "SignInPrimaryButtonStyle"), "MinHeight"));
            Assert.Equal("{StaticResource EgSearchCornerRadius}", SetterValue(KeyedStyle(signIn, "SignInPrimaryButtonStyle"), "CornerRadius"));
            Assert.Equal("{StaticResource EgSearchCornerRadius}", SetterValue(KeyedStyle(signIn, "SignInSecondaryButtonStyle"), "CornerRadius"));
        });

        Test("设置二级页统一：十个宿主子页共用边距、48x48徽章、26px标题，飞牛页无裸AccentButton或On/Off残留", () =>
        {
            foreach (var name in HostedSubpages)
                AssertUnifiedSubpage(name, Read("src/Momoka.Shell/Views/" + name));

            var broken = Read("src/Momoka.Shell/Views/DiagnosticsPage.xaml");
            Named(broken, "PageLayout").SetAttributeValue("Margin", "24,20");
            Assert.Throws<AssertionException>(() => AssertUnifiedSubpage("DiagnosticsPage.xaml", broken));

            var wrongTitle = Read("src/Momoka.Shell/Views/FnNasLibrariesPage.xaml");
            wrongTitle.Descendants(Xaml + "TextBlock")
                .First(tb => (string?)tb.Attribute("Style") == "{StaticResource EgSubpageTitleStyle}")
                .SetAttributeValue("Style", "{StaticResource EgPageTitleStyle}");
            Assert.Throws<AssertionException>(() => AssertUnifiedSubpage("FnNasLibrariesPage.xaml", wrongTitle));

            var rawAccent = Read("src/Momoka.Shell/Views/FnNasPasswordPage.xaml");
            Named(rawAccent, "FnNasPasswordSave").SetAttributeValue("Style", "{StaticResource AccentButtonStyle}");
            Assert.Throws<AssertionException>(() => AssertUnifiedSubpage("FnNasPasswordPage.xaml", rawAccent));

            var defaultToggle = Read("src/Momoka.Shell/Views/FnNasTasksPage.xaml");
            Named(defaultToggle, "FnNasTasksEnabled").Attribute("OnContent")!.Remove();
            Assert.Throws<AssertionException>(() => AssertUnifiedSubpage("FnNasTasksPage.xaml", defaultToggle));
        });

        Test("图标、热区与键盘重排：关键词规则和服务器账号去掉Unicode符号并支持>=32px按键与键盘换序", () =>
        {
            var settings = Read("src/Momoka.Shell/Views/SettingsPage.xaml");
            var shiftStyle = KeyedStyle(settings, "EgHomeShiftStyle");
            Assert.Equal("32", SetterValue(shiftStyle, "MinWidth"));
            Assert.Equal("32", SetterValue(shiftStyle, "MinHeight"));

            var titleRulesRoot = Named(settings, "PriorityRuleRoot");
            Assert.False(titleRulesRoot.Descendants(Xaml + "TextBlock").Any(tb =>
                tb.Attribute("Text")?.Value is "≡" or "↑" or "↓" or "✕"), "TitleRules 仍残留 Unicode 符号");
            var gripGlyph = titleRulesRoot.Descendants(Xaml + "FontIcon")
                .First(fi => (string?)fi.Attribute(X + "Uid") == "SettingsPage_Localization12");
            Assert.Equal("\uE76F", (string?)gripGlyph.Attribute("Glyph"));
            foreach (var (buttonName, glyph) in new[] { ("PriorityRuleUp", "\uE70E"), ("PriorityRuleDown", "\uE70D"), ("PriorityRuleDelete", "\uE711") })
            {
                var button = Named(settings, buttonName);
                Assert.Equal("{StaticResource EgHomeShiftStyle}", (string?)button.Attribute("Style"));
                Assert.Equal(glyph, button.Descendants(Xaml + "FontIcon").Single().Attribute("Glyph")?.Value);
            }

            var servers = Read("src/Momoka.Shell/Views/ServersPage.xaml");
            var serverShiftStyle = KeyedStyle(servers, "ServerShiftButtonStyle");
            Assert.Equal("32", SetterValue(serverShiftStyle, "MinWidth"));
            Assert.Equal("32", SetterValue(serverShiftStyle, "MinHeight"));

            Assert.False(servers.Descendants(Xaml + "TextBlock").Any(tb =>
                tb.Attribute("Text")?.Value is "≡" or "⋮⋮"), "ServersPage 仍残留 Unicode 拖手字符");
            foreach (var badge in servers.Descendants(Xaml + "DataTemplate")
                .Single(t => (string?)t.Attribute(X + "Key") == "AccountTemplate")
                .Elements(Xaml + "Border").Single()
                .Descendants().Single(e => e.Name.LocalName == "WrapRow")
                .Elements(Xaml + "Border"))
                Assert.Equal("{StaticResource EgCornerBadgeCornerRadius}", (string?)badge.Attribute("CornerRadius"));

            foreach (var name in new[] { "ServerMoveUpButton", "ServerMoveDownButton", "AccountMoveUpButton", "AccountMoveDownButton" })
            {
                var button = Named(servers, name);
                Assert.Equal("{StaticResource ServerShiftButtonStyle}", (string?)button.Attribute("Style"));
                Assert.False(string.IsNullOrEmpty((string?)button.Attribute("AutomationProperties.Name")), name);
            }
            Assert.Equal("OnServerListPreviewKeyDown", (string?)Named(servers, "ServersList").Attribute("PreviewKeyDown"));
            Assert.Equal("OnAccountListPreviewKeyDown", (string?)Named(servers, "AccountList").Attribute("PreviewKeyDown"));

            var serversCode = File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/Views/ServersPage.xaml.cs"));
            Assert.Contains("VirtualKey.Menu", serversCode);
            Assert.Contains("TryKeyboardReorder", serversCode);
            Assert.Contains("OnShiftButtonClick", serversCode);

            var vmCode = File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/ViewModels/ServersViewModel.cs"));
            foreach (var member in new[] { "MoveServerUp", "MoveServerDown", "MoveAccountUp", "MoveAccountDown", "UpdateReorderStates" })
                Assert.Contains(member, vmCode);

            var rowCode = File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/ViewModels/ServerRow.cs"));
            foreach (var member in new[] { "CanMoveUp", "CanMoveDown", "ReorderVisibility", "MoveUpName", "MoveDownName" })
                Assert.Contains(member, rowCode);
        });

        Test("自检对齐：封面框三主题厚度与播放菜单回退检查一致", () =>
        {
            var palette = Read("src/Momoka.Shell/Theme/Palette.xaml");
            var themeDicts = palette.Descendants(Xaml + "ResourceDictionary")
                .Where(d => d.Attribute(X + "Key") is not null)
                .ToDictionary(d => d.Attribute(X + "Key")!.Value);
            foreach (var (themeKey, thickness) in new[] { ("Dark", "0"), ("Light", "0"), ("HighContrast", "1") })
            {
                var entry = themeDicts[themeKey].Elements(Xaml + "Thickness")
                    .Single(e => (string?)e.Attribute(X + "Key") == "EgFrameBorderThickness");
                Assert.Equal(thickness, entry.Value.Trim(), themeKey);
            }

            var styles = Read("src/Momoka.Shell/Theme/Styles.xaml");
            var frameStyle = KeyedStyle(styles, "EgFrameStyle");
            Assert.Equal("{ThemeResource EgFrameBorderThickness}", SetterValue(frameStyle, "BorderThickness"));
            Assert.Equal("{ThemeResource EgBorderBrush}", SetterValue(frameStyle, "BorderBrush"));

            var playerPalette = File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/Views/PlayerPage.Palette.cs"));
            Assert.Contains("private static ThemeColor MenuBrushColor(", playerPalette);
            Assert.Contains("MenuBrushColor(key, colour)", playerPalette);
            Assert.Contains("ThemeHost.ToColor(MenuBrushColor(key, colour))", playerPalette);
        });
    }

    private static void AssertUnifiedSubpage(string fileName, XDocument doc)
    {
        var layout = Named(doc, "PageLayout");
        Assert.Equal("{StaticResource EgSettingsSubpageMargin}", (string?)layout.Attribute("Margin"), fileName);

        var hasBadge = doc.Descendants(Xaml + "Border").Any(border =>
            (string?)border.Attribute("Style") == "{StaticResource EgSubpageBadgeBorderStyle}"
            && border.Descendants(Xaml + "FontIcon").Any(icon =>
                (string?)icon.Attribute("Style") == "{StaticResource EgSubpageBadgeIconStyle}"));
        Assert.True(hasBadge, fileName + " 缺少共享 48x48 徽章头");

        Assert.True(doc.Descendants(Xaml + "TextBlock").Any(tb =>
            (string?)tb.Attribute("Style") == "{StaticResource EgSubpageTitleStyle}"),
            fileName + " 未使用 EgSubpageTitleStyle（26px）主标题");

        if (Array.IndexOf(FnNasPages, fileName) >= 0)
        {
            Assert.False(doc.Descendants().Attributes("Style").Any(attr => attr.Value == "{StaticResource AccentButtonStyle}"),
                fileName + " 仍直接使用裸 AccentButtonStyle");
            foreach (var toggle in doc.Descendants(Xaml + "ToggleSwitch"))
            {
                Assert.Equal("", (string?)toggle.Attribute("OnContent"), fileName);
                Assert.Equal("", (string?)toggle.Attribute("OffContent"), fileName);
            }
        }
    }

    private static XElement KeyedStyle(XDocument doc, string key) =>
        doc.Descendants(Xaml + "Style").Single(style => (string?)style.Attribute(X + "Key") == key);

    private static string? SetterValue(XElement style, string property) =>
        style.Elements(Xaml + "Setter").FirstOrDefault(setter => (string?)setter.Attribute("Property") == property)?.Attribute("Value")?.Value;

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().First(element => (string?)element.Attribute(X + "Name") == name);

    private static XDocument Read(string relativePath) =>
        XDocument.Load(Path.Combine(Root(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("找不到 Momoka 工作树");
    }
}
