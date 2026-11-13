using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class InfoGeometryTraceTests
{
    internal static void Register()
    {
        Test("媒体信息数字轨迹保存每拍索引和前后几何，返回后的7行不覆盖前拍证据", () =>
        {
            var saved = "";
            var page = Page();
            ShellSelfCheck.BeginInfoFixture(text => saved = text);
            ShellSelfCheck.ReadInfoFixture(page, false, false);
            var table = (ItemsRepeater)page.FindName("InfoTable")!;
            for (var index = 0; index < 3; index++) table.Rows.Remove(index);
            page.Body.VerticalOffset = 801;
            ShellSelfCheck.ReadInfoFixture(page, true, true);
            using var json = JsonDocument.Parse(saved);
            Assert.Equal(2, json.RootElement.GetArrayLength());
            Assert.Equal(10, json.RootElement[0].GetProperty("realized").GetArrayLength());
            Assert.Equal(7, json.RootElement[1].GetProperty("realized").GetArrayLength());
            Assert.Equal(3, json.RootElement[1].GetProperty("realized")[0].GetInt32());
            Assert.Equal("after-return", json.RootElement[1].GetProperty("phase").GetString());
            Assert.Equal(801d, json.RootElement[1].GetProperty("body").GetProperty("VerticalOffset").GetDouble());
            Assert.Equal(99d, json.RootElement[1].GetProperty("tableViewport").GetProperty("y").GetDouble());
            Assert.False(ShellSelfCheck.InfoFixtureFailed);
        });
        Test("媒体信息数字证据写入失败保留原红且下一拍可存全部先前证据", () =>
        {
            var saved = "";
            var failure = 0;
            var page = Page();
            ShellSelfCheck.BeginInfoFixture(text => { if (++failure == 1) throw new IOException("fixture"); saved = text; });
            ShellSelfCheck.ReadInfoFixture(page, false, false);
            Assert.True(ShellSelfCheck.InfoFixtureFailed);
            ShellSelfCheck.ReadInfoFixture(page, true, false);
            Assert.True(ShellSelfCheck.InfoFixtureFailed);
            using var json = JsonDocument.Parse(saved);
            Assert.Equal(2, json.RootElement.GetArrayLength());
        });
        Test("媒体信息数字证据缺目标必须红，非有限几何存null而不注入文本", () =>
        {
            var saved = "";
            var page = Page();
            ShellSelfCheck.BeginInfoFixture(text => saved = text);
            page.GeometryNames.Remove("InfoTable");
            ShellSelfCheck.ReadInfoFixture(page, false, false);
            Assert.True(ShellSelfCheck.InfoFixtureFailed);
            Assert.Equal("", saved);
            page = Page();
            ((ItemsRepeater)page.FindName("InfoTable")!).Top = double.NaN;
            ShellSelfCheck.BeginInfoFixture(text => saved = text);
            ShellSelfCheck.ReadInfoFixture(page, false, false);
            using var json = JsonDocument.Parse(saved);
            Assert.Equal(JsonValueKind.Null, json.RootElement[0].GetProperty("tableViewport").GetProperty("y").ValueKind);
            Assert.False(ShellSelfCheck.InfoFixtureFailed);
            var root = Root();
            var source = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellSelfCheck.InfoGeometry.cs"));
            source = source[source.IndexOf("private static void RecordInfoGeometry(", StringComparison.Ordinal)..];
            foreach (var forbidden in new[] { ".Label", ".Title", ".Text", ".Url", ".Path", "EnsurePoster", "GetOrCreateElement", "UpdateLayout", "ScrollTo(" })
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal), "只允许数字只读字段：" + forbidden);
            var read = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellSelfCheck.Reads.cs"));
            Assert.Contains("RecordInfoGeometry(page);", read);
            var schedule = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellSelfCheck.cs"));
            Assert.Contains("selfcheck-info-geometry.json", schedule);
            Assert.Contains("InfoGeometryReadings.Clear();", schedule);
            Assert.Contains("_info = ReadInfo(shell);", schedule);
        });
    }

    private static DetailPage Page()
    {
        var page = new DetailPage();
        page.Body.ExtentHeight = 1509;
        page.Body.ScrollableHeight = 801;
        page.Body.ViewportHeight = 708;
        page.Body.Content = new FrameworkElement { ActualHeight = 1509 };
        page.GeometryNames["InfoPanel"] = new FrameworkElement { Top = 800, ActualHeight = 450 };
        var table = new ItemsRepeater { Top = 900, ActualHeight = 300 };
        page.GeometryNames["InfoTable"] = table;
        for (var index = 0; index < 10; index++)
        {
            page.ViewModel.InfoRows.Add(index);
            table.Rows[index] = new FrameworkElement { Top = 900 + index * 30, ActualHeight = 30 };
        }
        return page;
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
