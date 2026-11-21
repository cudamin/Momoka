using System.Text.Json;
using Momoka.Shell;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SelfCheckReadinessTests
{
    internal static void Register()
    {
        Test("返回自检在数据Ready但600ms恢复期仍在时等待，不改真实偏移", () =>
        {
            var saved = "";
            var page = Page(1032);
            ShellSelfCheck.BeginReturnReadFixture(page, 1032, text => saved = text);
            try
            {
                page.KeepScrollThroughReveal(1032);
                page.Body.VerticalOffset = 685;
                page.SetRevealAgeForFixture(344);
                Assert.False(page.IsPlaybackReturnSettled);
                Assert.True(page.ViewModel.IsReady);
                Assert.True(ShellSelfCheck.ReadReturnFixture(page, 20));
                Assert.Null(ShellSelfCheck.PlaybackReturnFixtureRead);
                Assert.Equal(685d, page.Body.VerticalOffset);
                using (var pending = JsonDocument.Parse(saved))
                    Assert.Equal("waiting-for-reveal", pending.RootElement.EnumerateArray().Last().GetProperty("stage").GetString());
                page.Body.VerticalOffset = 1032;
                page.EndRevealLayoutForFixture();
                Assert.True(page.IsPlaybackReturnSettled);
                Assert.False(ShellSelfCheck.ReadReturnFixture(page, 21));
                Assert.True(ShellSelfCheck.PlaybackReturnFixtureRead!.Value.Ok);
            }
            finally { page.StopRevealForFixture(); ShellSelfCheck.EndReturnFixture(); }
        });
        Test("返回恢复期结束仍在685时原ReturnRead保留红，不接受临时范围", () =>
        {
            var page = Page(1032);
            ShellSelfCheck.BeginReturnReadFixture(page, 1032, _ => { });
            try
            {
                page.KeepScrollThroughReveal(1032);
                page.Body.VerticalOffset = 685;
                page.EndRevealLayoutForFixture();
                Assert.False(ShellSelfCheck.ReadReturnFixture(page, 21));
                var reading = ShellSelfCheck.PlaybackReturnFixtureRead!.Value;
                Assert.False(reading.Ok);
                Assert.Contains("读数 685", reading.Detail);
                Assert.Contains("这一趟回来改变了页面滚动位置", reading.Detail);
            }
            finally { page.StopRevealForFixture(); ShellSelfCheck.EndReturnFixture(); }
        });
        Test("返回自检沿用阶段总截止，恢复永久未结束也读取原红而不无限等待", () =>
        {
            var saved = "";
            var page = Page(1032);
            page.ViewModel.Busy = true;
            page.ViewModel.IsReady = false;
            ShellSelfCheck.BeginReturnReadFixture(page, 1032, text => saved = text);
            try
            {
                page.KeepScrollThroughReveal(1032);
                page.Body.VerticalOffset = 685;
                var deadline = ShellSelfCheck.PlaybackReturnFixtureDeadline;
                Assert.True(ShellSelfCheck.ReadReturnFixture(page, deadline - 1));
                Assert.Null(ShellSelfCheck.PlaybackReturnFixtureRead);
                Assert.False(ShellSelfCheck.ReadReturnFixture(page, deadline));
                Assert.False(ShellSelfCheck.PlaybackReturnFixtureRead!.Value.Ok);
                using var json = JsonDocument.Parse(saved);
                Assert.Equal("final-read", json.RootElement.EnumerateArray().Last().GetProperty("stage").GetString());
                Assert.Equal(685d, json.RootElement.EnumerateArray().Last().GetProperty("observation").GetProperty("Offset").GetDouble());
            }
            finally { page.StopRevealForFixture(); ShellSelfCheck.EndReturnFixture(); }
        });
        Test("返回原判据仍拒绝集带错层；旧读取入口必须调用实际等待helper", () =>
        {
            var page = Page(1032);
            page.BodySheet.Children.Insert(0, new Microsoft.UI.Xaml.UIElement());
            ShellSelfCheck.BeginReturnReadFixture(page, 1032, _ => { });
            try
            {
                Assert.True(page.IsPlaybackReturnSettled);
                Assert.False(ShellSelfCheck.ReadReturnFixture(page, 1));
                Assert.False(ShellSelfCheck.PlaybackReturnFixtureRead!.Value.Ok);
                Assert.Contains("集带没排在纸的第一块", ShellSelfCheck.PlaybackReturnFixtureRead!.Value.Detail);
                var source = File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/ShellSelfCheck.cs"));
                Assert.Contains("shell.Pages.Content is DetailPage back && ReadPlaybackReturn(back)", source);
                var guard = source.IndexOf("&& WaitForPlaybackReturn(returning)) return true;", StringComparison.Ordinal);
                var info = source.IndexOf("_info = ReadInfo(shell);", StringComparison.Ordinal);
                Assert.True(guard >= 0 && info > guard, "返回等待应先于可能重新定位的ReadInfo");
                Assert.Contains("var opened = RecordSettingsStop(page);", source);
            }
            finally { ShellSelfCheck.EndReturnFixture(); }
        });
        Test("实际返回轨迹记录pending请求与ScrollCompleted关联，最终读取后解绑", () =>
        {
            var saved = "";
            var page = Page(0);
            page.Body.AutoComplete = false;
            ShellSelfCheck.BeginReturnReadFixture(page, 1032, text => saved = text);
            try
            {
                page.KeepScrollThroughReveal(1032);
                page.Body.ChangeOffset(685);
                using (var pending = JsonDocument.Parse(saved))
                {
                    var observation = pending.RootElement.EnumerateArray().Last().GetProperty("observation");
                    Assert.Equal(1, observation.GetProperty("PendingCorrelation").GetInt32());
                    Assert.Equal(1032d, observation.GetProperty("RequestedOffset").GetDouble());
                }
                page.Body.Complete(1);
                using (var completed = JsonDocument.Parse(saved))
                {
                    var receipt = completed.RootElement.EnumerateArray().Single(row => row.GetProperty("stage").GetString() == "scroll-completed");
                    Assert.Equal(1, receipt.GetProperty("completedCorrelation").GetInt32());
                    Assert.Equal(1032d, receipt.GetProperty("observation").GetProperty("Offset").GetDouble());
                }
                page.EndRevealLayoutForFixture();
                Assert.False(ShellSelfCheck.ReadReturnFixture(page, 2));
                Assert.True(ShellSelfCheck.PlaybackReturnFixtureRead!.Value.Ok);
                var final = saved;
                page.Body.SignalCompleted(99);
                page.Body.ChangeOffset(42);
                Assert.Equal(final, saved);
                Assert.Equal(0, page.Body.SubscriptionCount);
            }
            finally { page.StopRevealForFixture(); ShellSelfCheck.EndReturnFixture(); }
        });
        Test("设置逐卡记录先布局，0个容器量后2个按真实值记录", () =>
        {
            var page = new SettingsPage { Cards = [("主页", 2)], SelectedCategory = "主页" };
            page.OnLayout = () => page.Realised = 2;
            ShellSelfCheck.BeginSettingsWalkFixture();
            Assert.True(ShellSelfCheck.WalkSettingsFixture(page));
            Assert.Equal(1, page.LayoutCalls);
            var reading = ShellSelfCheck.SettingsWalkFixtureReadings.Single();
            Assert.Equal(("主页", 2, 2, 2), reading);
            Assert.False(ShellSelfCheck.WalkSettingsFixture(page));
            Assert.Equal(1, ShellSelfCheck.SettingsWalkFixtureReadings.Length);
        });
        Test("设置量后仍缺模板保留原306预期和304红读数，不补足或降级", () =>
        {
            var page = new SettingsPage { Cards = [("主页", 306)], SelectedCategory = "主页", Realised = 304 };
            ShellSelfCheck.BeginSettingsWalkFixture();
            ShellSelfCheck.WalkSettingsFixture(page);
            var reading = ShellSelfCheck.SettingsWalkFixtureReadings.Single();
            Assert.Equal(306, reading.Expected);
            Assert.Equal(304, reading.Drawn);
            Assert.True(reading.Drawn < reading.Expected);
        });
        Test("设置每卡只记一次，布局后的容器总数按已打开分类累计", () =>
        {
            var page = new SettingsPage { Cards = [("主页", 2), ("外观", 3)], SelectedCategory = "主页" };
            page.OnLayout = () => page.Realised = page.SelectedCategory == "主页" ? 2 : 5;
            ShellSelfCheck.BeginSettingsWalkFixture();
            Assert.True(ShellSelfCheck.WalkSettingsFixture(page));
            Assert.Equal("外观", page.SelectedCategory);
            Assert.True(ShellSelfCheck.WalkSettingsFixture(page));
            Assert.False(ShellSelfCheck.WalkSettingsFixture(page));
            Assert.Equal(2, page.LayoutCalls);
            var readings = ShellSelfCheck.SettingsWalkFixtureReadings;
            Assert.Equal(2, readings.Length);
            Assert.Equal(("主页", 2, 2, 2), readings[0]);
            Assert.Equal(("外观", 3, 5, 5), readings[1]);
        });
    }

    private static DetailPage Page(double offset)
    {
        var page = new DetailPage();
        page.Body.ScrollableHeight = 1032;
        page.Body.VerticalOffset = offset;
        page.BodySheet.Children.Add(page.EpisodePanel);
        return page;
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("找不到本树源码");
    }
}
