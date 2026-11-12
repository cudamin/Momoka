using System.Xml.Linq;
using Momoka.Shell.Views;
using Windows.System;

namespace Momoka.Tests;

/// <summary>Links the real preview handler; popup/input endpoints are inert, not WinUI key delivery.</summary>
internal static class SelectionInputTests
{
    internal static void Register()
    {
        TestHarness.Test("多选Escape：没有浮层时退出且处理按键", () =>
        {
            var page = new LibraryPage();
            page.SetSelection(true);
            Assert.True(page.PressSelectionKey(VirtualKey.Escape).Handled);
            Assert.Equal(1, page.SelectionEnds);
        });
        TestHarness.Test("多选Escape：未选择或其他按键不处理", () =>
        {
            var page = new LibraryPage();
            Assert.False(page.PressSelectionKey(VirtualKey.Escape).Handled);
            page.SetSelection(true);
            Assert.False(page.PressSelectionKey(VirtualKey.Enter).Handled);
            Assert.Equal(0, page.SelectionEnds);
        });
        foreach (var overflow in new[] { false, true })
        {
            TestHarness.Test($"多选Escape：{(overflow ? "溢出菜单" : "原生浮层")}先交给平台关闭", () =>
            {
                var page = new LibraryPage();
                page.SetSelection(true);
                if (overflow) page.LibraryBatchCommandBar.IsOpen = true;
                else page.XamlRoot.Add(new object());
                Assert.False(page.PressSelectionKey(VirtualKey.Escape).Handled);
                Assert.Equal(0, page.SelectionEnds);
                page.LibraryBatchCommandBar.IsOpen = false;
                page.XamlRoot.Clear();
                Assert.True(page.PressSelectionKey(VirtualKey.Escape).Handled);
                Assert.Equal(1, page.SelectionEnds);
            });
            TestHarness.Test($"多选Escape：执行中{(overflow ? "溢出菜单" : "确认浮层")}不能先取消任务", () =>
            {
                var page = new LibraryPage();
                using var cancellation = new CancellationTokenSource();
                page.SetSelection(true, cancellation);
                if (overflow) page.LibraryBatchCommandBar.IsOpen = true;
                else page.XamlRoot.Add(new object());
                Assert.False(page.PressSelectionKey(VirtualKey.Escape).Handled);
                Assert.False(cancellation.IsCancellationRequested);
                page.LibraryBatchCommandBar.IsOpen = false;
                page.XamlRoot.Clear();
                Assert.True(page.PressSelectionKey(VirtualKey.Escape).Handled);
                Assert.True(cancellation.IsCancellationRequested);
                Assert.Equal(0, page.SelectionEnds);
            });
        }
        TestHarness.Test("多选CommandBar：计数、主次操作和原生菜单接线", () =>
        {
            var root = FindRoot();
            var document = XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "LibraryPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var bar = document.Descendants().Single(element => element.Attribute(x + "Name")?.Value == "LibraryBatchCommandBar");
            Assert.Equal("CommandBar", bar.Name.LocalName);
            Assert.Equal("Right", bar.Attribute("DefaultLabelPosition")?.Value);
            Assert.Equal(null, bar.Attribute("IsDynamicOverflowEnabled")?.Value);
            var content = bar.Elements().Single(element => element.Name.LocalName == "CommandBar.Content").Elements().Single();
            Assert.Equal("LibrarySelectionCount", content.Attribute(x + "Name")?.Value);
            Assert.Equal("Polite", content.Attribute("AutomationProperties.LiveSetting")?.Value);
            var primary = bar.Elements().Where(element => element.Name.LocalName == "AppBarButton").ToArray();
            Assert.True(primary.Select(element => element.Attribute(x + "Name")?.Value).SequenceEqual(new[]
            {
                "LibrarySelectLoaded", "LibraryBatchActions", "LibraryCancelBatch", "LibraryEndSelection"
            }));
            Assert.True(primary.All(element => !string.IsNullOrWhiteSpace(element.Attribute("Label")?.Value)));
            var menu = primary[1].Descendants().Single(element => element.Name.LocalName == "MenuFlyout");
            Assert.Equal("LibraryBatchMenu", menu.Attribute(x + "Name")?.Value);
            Assert.Equal("OnBatchMenuOpening", menu.Attribute("Opening")?.Value);
            var clear = bar.Elements().Single(element => element.Name.LocalName == "CommandBar.SecondaryCommands").Elements().Single();
            Assert.Equal("LibraryClearSelection", clear.Attribute(x + "Name")?.Value);
            Assert.Equal("OnClearSelection", clear.Attribute("Click")?.Value);
        });
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("Cannot find this fixture's source tree");
    }
}
