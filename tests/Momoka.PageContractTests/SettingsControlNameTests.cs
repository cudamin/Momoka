using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Template/resource contracts only; the Shell probe checks actual binding and UIA output.</summary>
internal static class SettingsControlNameTests
{
    internal static void Register()
    {
        foreach (var shape in new[] { "Slider", "Color" })
            Test("设置具体名称：" + shape + " 模板与资源不丢失当前行标签", () =>
            {
                var root = FindRoot();
                var page = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "SettingsPage.xaml"));
                var resources = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Strings", "zh-Hans", "Resources.resw"));
                var template = page.Descendants().Single(element => element.Name.LocalName == "SettingRowTemplates." + shape);
                var control = template.Descendants().Single(element => shape == "Slider"
                    ? element.Name.LocalName == "Slider"
                    : element.Name.LocalName == "Button" && element.Elements().Any(child => child.Name.LocalName == "Button.Flyout"));
                Assert.True(BindsRowName(control, resources), "控件必须绑定当前行 Label，且不能被通用 x:Uid 名称盖回去");
                var oldName = new XElement(control);
                oldName.SetAttributeValue("AutomationProperties.Name", "通用控件");
                Assert.False(BindsRowName(oldName, resources), "负向：统一名称必须被拒绝");
                var resourceOverride = new XDocument(resources);
                var withUid = new XElement(control);
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                withUid.SetAttributeValue(xaml + "Uid", "InjectedName");
                resourceOverride.Root!.Add(new XElement("data",
                    new XAttribute("name", "InjectedName.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"),
                    new XElement("value", "通用控件")));
                Assert.False(BindsRowName(withUid, resourceOverride), "负向：资源覆盖具体名称必须被拒绝");
            });
    }

    private static bool BindsRowName(XElement control, XDocument resources)
    {
        if (control.Attribute("AutomationProperties.Name")?.Value != "{x:Bind Label}") return false;
        var uid = control.Attribute(XName.Get("Uid", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value;
        return uid is null || !resources.Descendants("data").Any(entry =>
            entry.Attribute("name")?.Value == uid + ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name");
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到本次编译所属的工作树");
    }
}
