using System.Xml.Linq;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SystemManagedThemeRoleTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("系统管理角色只认当前明确别名及三个实际原生画刷", () =>
        {
            var seen = new List<string>();
            Assert.True(SystemManagedThemeRole.IsManaged("EgDetailContentLayerBrush", (theme, key) =>
            {
                Assert.Equal("EgDetailContentLayerBrush", key);
                seen.Add(theme);
                return true;
            }));
            Assert.Equal("Dark|Light|HighContrast", string.Join('|', seen));
            foreach (var missing in new[] { "Dark", "Light", "HighContrast" })
                Assert.False(SystemManagedThemeRole.IsManaged("EgDetailContentLayerBrush", (theme, _) => theme != missing));
            foreach (var unknown in new[] { "EgOtherBrush", "EgCanvasBrush", "LayerFillColorDefaultBrush", "egDetailContentLayerBrush" })
                Assert.False(SystemManagedThemeRole.IsManaged(unknown, (_, _) => throw new AssertionException("未知键不得查询为系统管理")));
            Assert.Throws<IOException>(() => SystemManagedThemeRole.IsManaged("EgDetailContentLayerBrush", (_, _) => throw new IOException("resource read failure")));
        });
        Test("系统管理别名源契约必须保持三主题动态Color且不由ThemeHost重涂", () =>
        {
            var root = Root();
            var palette = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Theme/Palette.xaml"));
            RequireDynamicAlias(palette);
            var theme = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Theme/ThemeHost.cs"));
            Assert.DoesNotContain("(\"EgDetailContentLayerBrush\",", theme, "不能把系统动态色别名登记到自涂列表");
            Assert.Contains("SystemManagedThemeRole.IsManaged(key", theme);
            Assert.Contains("Dictionary(theme)", theme);
            Assert.Contains("value is SolidColorBrush", theme);
            var selfCheck = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellSelfCheck.Theme.cs"));
            Assert.Contains("darkKeys.Where(ThemeHost.IsSystemManagedBrush)", selfCheck);
            Assert.Contains("&& !painted.Contains(key)", selfCheck);
            Assert.Contains("&& !systemManaged.Contains(key)", selfCheck);
        });
        Test("系统管理动态源反例拒绝静态值、错角色、缺字典、重复键和扩大别名", () =>
        {
            var original = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Theme/Palette.xaml"));
            foreach (var mutation in new Action<XDocument>[]
            {
                doc => Alias(doc, "Dark").SetAttributeValue("Color", "#FF112233"),
                doc => Alias(doc, "Light").SetAttributeValue("Color", "{StaticResource LayerFillColorDefault}"),
                doc => Alias(doc, "HighContrast").SetAttributeValue("Color", "{ThemeResource LayerFillColorDefault}"),
                doc => Alias(doc, "HighContrast").Remove(),
                doc => Alias(doc, "Dark").Parent!.Add(new XElement(Alias(doc, "Dark"))),
                doc => Alias(doc, "Dark").SetAttributeValue(Xaml + "Key", "EgOtherBrush"),
                doc => Alias(doc, "Light").Name = Alias(doc, "Light").Name.Namespace + "AcrylicBrush",
            })
            {
                var wrong = new XDocument(original);
                mutation(wrong);
                Assert.Throws<AssertionException>(() => RequireDynamicAlias(wrong));
            }
        });
    }

    private static void RequireDynamicAlias(XDocument doc)
    {
        foreach (var theme in new[] { "Dark", "Light", "HighContrast" })
        {
            var dictionary = doc.Descendants().SingleOrDefault(element => element.Name.LocalName == "ResourceDictionary"
                && element.Attribute(Xaml + "Key")?.Value == theme);
            Assert.NotNull(dictionary);
            var matches = dictionary!.Elements().Where(element => element.Attribute(Xaml + "Key")?.Value
                == SystemManagedThemeRole.DetailContentLayerBrush).ToArray();
            Assert.Equal(1, matches.Length);
            Assert.Equal("SolidColorBrush", matches[0].Name.LocalName);
            Assert.Equal(theme == "HighContrast" ? "{ThemeResource SystemColorWindowColor}" : "{ThemeResource LayerFillColorDefault}",
                matches[0].Attribute("Color")?.Value);
        }
    }

    private static XElement Alias(XDocument doc, string theme) => doc.Descendants().Single(element => element.Name.LocalName == "ResourceDictionary"
        && element.Attribute(Xaml + "Key")?.Value == theme).Elements().Single(element => element.Attribute(Xaml + "Key")?.Value == "EgDetailContentLayerBrush");

    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
        return root?.FullName ?? throw new AssertionException("缺少本树源");
    }
}
