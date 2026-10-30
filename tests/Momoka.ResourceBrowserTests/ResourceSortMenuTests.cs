using System.Text.Json;
using System.Xml.Linq;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ResourceSortMenuTests
{
    public static void Register(string sourceRoot)
    {
        Test("网页资源排序：默认字段与降序、名称和方向属性通知", () =>
        {
            var vm = new MoviePilotResourceBrowserViewModel();
            Assert.Equal(0, vm.SortIndex);
            Assert.True(vm.SortDescending);
            Assert.Equal("默认排序", vm.SortLabel);
            var notifications = new List<string>();
            vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName ?? "");
            vm.SortIndex = (int)MoviePilotResourceSort.Seeders;
            Assert.Equal("做种数", vm.SortLabel);
            Assert.Contains("做种数，降序", vm.SortAutomationName);
            Assert.True(notifications.Contains(nameof(vm.SortLabel)) && notifications.Contains(nameof(vm.SortAutomationName)));
            notifications.Clear();
            vm.SortDescending = false;
            Assert.Contains("做种数，升序", vm.SortAutomationName);
            Assert.True(notifications.Contains(nameof(vm.SortAutomationName)));
        });
        foreach (var descending in new[] { false, true })
        {
            var direction = descending;
            foreach (var field in Enum.GetValues<MoviePilotResourceSort>())
            {
                var sort = field;
                Test($"网页资源排序：实际VM {sort} 方向={direction}，集合和下载反馈稳定", () =>
                {
                    var vm = new MoviePilotResourceBrowserViewModel();
                    var data = Rows();
                    vm.SetResults(data);
                    var rows = vm.Rows;
                    var groups = vm.Groups;
                    var originalGroups = groups.ToArray();
                    data[0].State = MoviePilotDownloadState.Uncertain;
                    data[1].State = MoviePilotDownloadState.Done;
                    vm.SortIndex = (int)sort;
                    vm.SortDescending = direction;
                    var expected = sort == MoviePilotResourceSort.Original ? "middle,small,large" :
                        direction ? "large,middle,small" : "small,middle,large";
                    Assert.Equal(expected, string.Join(',', vm.Rows.Select(row => row.Title)));
                    Assert.Equal(expected, string.Join(',', vm.Groups.SelectMany(group => group.Rows).Select(row => row.Title)));
                    Assert.True(ReferenceEquals(rows, vm.Rows) && ReferenceEquals(groups, vm.Groups));
                    Assert.True(groups.All(originalGroups.Contains), "重排复用现有组，不能丢失展开状态");
                    Assert.Equal(MoviePilotDownloadState.Uncertain, data[0].State);
                    Assert.Equal(MoviePilotDownloadState.Done, data[1].State);
                    Assert.True(vm.Rows.All(data.Contains));
                    Assert.Equal(MoviePilotResourceBrowserViewModel.SortLabels[(int)sort], vm.SortLabel);
                });
            }
        }
        Test("网页资源排序：清除筛选保留排序，新搜索恢复默认降序", () =>
        {
            var vm = new MoviePilotResourceBrowserViewModel();
            var data = Rows();
            vm.SetResults(data);
            vm.SortIndex = (int)MoviePilotResourceSort.Size;
            vm.SortDescending = false;
            vm.Text = "middle";
            Assert.Equal(1, vm.Rows.Count);
            vm.ResetCommand.Execute(null);
            Assert.Equal((int)MoviePilotResourceSort.Size, vm.SortIndex);
            Assert.False(vm.SortDescending);
            Assert.Equal("small,middle,large", string.Join(',', vm.Rows.Select(row => row.Title)));
            vm.SetResults(data);
            Assert.Equal(0, vm.SortIndex);
            Assert.True(vm.SortDescending);
            Assert.Equal("middle,small,large", string.Join(',', vm.Rows.Select(row => row.Title)));
        });
        Test("网页资源排序：两个浏览器实例的字段方向互不影响", () =>
        {
            var first = new MoviePilotResourceBrowserViewModel();
            var second = new MoviePilotResourceBrowserViewModel();
            first.SetResults(Rows());
            second.SetResults(Rows());
            first.SortIndex = (int)MoviePilotResourceSort.Size;
            first.SortDescending = false;
            Assert.Equal(0, second.SortIndex);
            Assert.True(second.SortDescending);
            Assert.Equal("middle,small,large", string.Join(',', second.Rows.Select(row => row.Title)));
        });
        Test("网页资源排序：模板为透明文本按钮、原生下拉两组单选和动态图标", () =>
        {
            var xml = XDocument.Load(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/MoviePilotResourceBrowser.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            XElement Named(string name) => xml.Descendants().Single(node => node.Attribute(x + "Name")?.Value == name);
            var button = Named("SortOrder");
            Assert.Equal("DropDownButton", button.Name.LocalName);
            Assert.Equal("{ThemeResource ControlFillColorTransparentBrush}", button.Attribute("Background")?.Value);
            Assert.Equal("{x:Bind ViewModel.SortAutomationName, Mode=OneWay}", button.Attribute("AutomationProperties.Name")?.Value);
            Assert.Null(button.Attribute("Width"), "不是固定宽度的设置页下拉框");
            Assert.Equal("32", button.Attribute("Height")?.Value);
            Assert.Equal("{x:Bind ViewModel.SortLabel, Mode=OneWay}", Named("SortLabel").Attribute("Text")?.Value);
            Assert.Equal("{x:Bind local:MoviePilotResourceBrowser.AscendingIconVisibility(ViewModel.SortDescending), Mode=OneWay}",
                Named("SortAscendingIcon").Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind local:MoviePilotResourceBrowser.DescendingIconVisibility(ViewModel.SortDescending), Mode=OneWay}",
                Named("SortDescendingIcon").Attribute("Visibility")?.Value);
            Assert.NotNull(Named("SortAscendingIcon").Attribute("Data"));
            Assert.NotNull(Named("SortDescendingIcon").Attribute("Data"));
            var menu = Named("SortMenu");
            Assert.Equal("MenuFlyout", menu.Name.LocalName);
            Assert.Equal("Bottom", menu.Attribute("Placement")?.Value);
            Assert.Equal("RadioMenuFlyoutItem,RadioMenuFlyoutItem,MenuFlyoutSeparator," +
                string.Join(',', Enumerable.Repeat("RadioMenuFlyoutItem", 5)), string.Join(',', menu.Elements().Select(node => node.Name.LocalName)));
            Assert.Equal("OnSortMenuOpening", menu.Attribute("Opening")?.Value);
            Assert.Null(Named("SortAscending").Attribute("IsChecked"), "打开时从当前VM读回，不和原生单选重算竞争");
            Assert.Null(Named("SortDescending").Attribute("IsChecked"));
            Assert.Equal("OnSortDirectionClick", Named("SortAscending").Attribute("Click")?.Value);
            Assert.Equal("OnSortDirectionClick", Named("SortDescending").Attribute("Click")?.Value);
            var fields = menu.Elements().Where(node => node.Attribute("Tag") is not null).ToArray();
            Assert.Equal(string.Join(',', MoviePilotResourceBrowserViewModel.SortLabels), string.Join(',', fields.Select(node => node.Attribute("Text")?.Value)));
            for (var index = 0; index < fields.Length; index++)
            {
                Assert.Equal(index.ToString(System.Globalization.CultureInfo.InvariantCulture), fields[index].Attribute("Tag")?.Value);
                Assert.Null(fields[index].Attribute("IsChecked"));
                Assert.Equal("OnSortFieldClick", fields[index].Attribute("Click")?.Value);
                Assert.NotNull(fields[index].Attribute(x + "Name"));
            }
            Assert.Equal("{x:Bind SortDirectionGroup}", Named("SortAscending").Attribute("GroupName")?.Value);
            Assert.Equal("{x:Bind SortDirectionGroup}", Named("SortDescending").Attribute("GroupName")?.Value);
            Assert.True(fields.All(node => node.Attribute("GroupName")?.Value == "{x:Bind SortFieldGroup}"),
                "两组原生单选必须隔开，组名由每个浏览器实例生成");
        });
    }

    private static MoviePilotResourceRow[] Rows()
    {
        var now = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        var middle = new MoviePilotResource
        {
            Title = "middle",
            SiteName = "B",
            Size = 20,
            Seeders = 20,
            PublishedAt = now.AddDays(-2),
            TorrentInfo = JsonSerializer.SerializeToElement(new { title = "anonymous" })
        };
        return new[] { middle, middle with { Title = "small", SiteName = "A", Size = 10, Seeders = 10, PublishedAt = now.AddDays(-3) },
            middle with { Title = "large", SiteName = "C", Size = 30, Seeders = 30, PublishedAt = now.AddDays(-1) } }
            .Select(resource => new MoviePilotResourceRow(resource, _ => Task.CompletedTask)).ToArray();
    }
}
