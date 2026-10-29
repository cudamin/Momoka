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

        Test("输入主题探针：按业务行定位并等待视口、真实焦点和Focused模板", () =>
        {
            var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "ShellNavigationProbe.InputTheme.cs"));
            Assert.True(source.Contains("AutomationProperties.GetName(control) == \"轮播图用什么媒体\"", StringComparison.Ordinal));
            Assert.True(source.Contains("AutomationProperties.GetName(control) == \"轮播图张数\"", StringComparison.Ordinal));
            var focusStart = source.IndexOf("async Task FocusAsync(", StringComparison.Ordinal);
            var focusEnd = source.IndexOf("bool TextFocusSettled(", focusStart, StringComparison.Ordinal);
            var focus = source[focusStart..focusEnd];
            var viewport = focus.IndexOf("await UntilAsync(() => InViewport(control, scroll))", StringComparison.Ordinal);
            var request = focus.IndexOf("control.Focus(FocusState.Keyboard)", StringComparison.Ordinal);
            Assert.True(viewport >= 0 && request > viewport, "先等待可见区域，再请求焦点");
            Assert.True(focus.Contains("parent.Visibility == Visibility.Visible", StringComparison.Ordinal));
            var settledEnd = source.IndexOf("static Border TextFrame", focusEnd, StringComparison.Ordinal);
            var settled = source[focusEnd..settledEnd];
            Assert.True(settled.Contains("FocusManager.GetFocusedElement", StringComparison.Ordinal));
            Assert.True(settled.Contains("TextState(control) == \"Focused\"", StringComparison.Ordinal));
            Assert.True(settled.Contains("line.Color == border", StringComparison.Ordinal));
            Assert.True(settled.Contains("frame.BorderThickness == new Thickness(1)", StringComparison.Ordinal));
            Assert.False(source.Contains("VisualStateManager.GoToState", StringComparison.Ordinal), "探针不可手动制造焦点样式");
            Assert.True(source.Contains("Require(!TextFocusSettled(searchInput", StringComparison.Ordinal), "保留实际离焦负向路径");
            Assert.True(source.Contains("finally\n        {\n            File.WriteAllText", StringComparison.Ordinal), "失败时也保留本轮诊断读数");
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
