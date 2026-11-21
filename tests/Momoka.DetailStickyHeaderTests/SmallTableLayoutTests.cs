using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;
using Windows.Foundation;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SmallTableLayoutTests
{
    internal static void Register()
    {
        Test("媒体小表按全部行的实际高度测量，窄宽切换重测换行且无额外间距", () =>
        {
            var layout = new HomeRowsLayout();
            var children = Enumerable.Range(0, 10).Select(index => new UIElement()).ToArray();
            var reads = 0;
            foreach (var child in children)
                child.OnMeasure = size =>
                {
                    reads++;
                    Assert.True(double.IsPositiveInfinity(size.Height));
                    child.DesiredSize = new(size.Width, size.Width < 400 ? 44 : 22);
                };
            var context = new NonVirtualizingLayoutContext(children);
            Assert.Equal(new Size(760, 220), layout.MeasureForFixture(context, new(760, 80)));
            Assert.Equal(10, reads, "不可只测视口内的行");
            Assert.Equal(new Size(300, 440), layout.MeasureForFixture(context, new(300, 80)));
            Assert.Equal(20, reads);
            layout.ArrangeForFixture(context, new(300, 440));
            for (var index = 0; index < children.Length; index++)
                Assert.Equal(new Rect(0, index * 44, 300, 44), children[index].ArrangedBounds!.Value);
        });
        Test("逐行布局空表为零，主页既有间距与最终行尾保持", () =>
        {
            var layout = new HomeRowsLayout { Spacing = 4 };
            var empty = new NonVirtualizingLayoutContext([]);
            Assert.Equal(new Size(0, 0), layout.MeasureForFixture(empty, new(400, 600)));
            var children = new[] { new UIElement { DesiredSize = new(100, 10) }, new UIElement { DesiredSize = new(150, 30) } };
            var context = new NonVirtualizingLayoutContext(children);
            Assert.Equal(new Size(150, 44), layout.MeasureForFixture(context, new(400, 600)));
            layout.ArrangeForFixture(context, new(400, 44));
            Assert.Equal(new Rect(0, 14, 400, 30), children[1].ArrangedBounds!.Value);
        });
        Test("正式媒体表选择小表布局，原渲染与视口失败判据保留", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
            Assert.NotNull(root);
            var xaml = XDocument.Load(Path.Combine(root!.FullName, "src/Momoka.Shell/Views/DetailPage.xaml"));
            XNamespace names = "http://schemas.microsoft.com/winfx/2006/xaml";
            var table = xaml.Descendants().Single(element => (string?)element.Attribute(names + "Name") == "InfoTable");
            Assert.Equal("HomeRowsLayout", table.Descendants().Single(element => element.Name.LocalName.EndsWith("Layout", StringComparison.Ordinal) && element.Name.LocalName != "ItemsRepeater.Layout").Name.LocalName);
            var probe = File.ReadAllText(Path.Combine(root.FullName, "src/Momoka.Shell/ShellNavigationProbe.InfoTable.cs"));
            Assert.Contains("InfoTableProbeFixture.RequireRendered(10, realized, first.Contains('：'))", probe);
            Assert.Contains("tableTop + table.ActualHeight > 0 && tableTop < body.ViewportHeight", probe);
            Assert.Contains("SaveProbeScreenAsync($\"info-table-{size.Width}-{size.Height}-screen.png\")", probe);
            Assert.Contains("SaveSettledFrameAsync(page, $\"info-table-{size.Width}-{size.Height}-render.png\")", probe);
        });
    }
}
