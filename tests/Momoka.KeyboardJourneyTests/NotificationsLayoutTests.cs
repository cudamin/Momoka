using System.Text.Json;
using Momoka.Shell;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class NotificationsLayoutTests
{
    internal static void Register()
    {
        Test("通知自检等待实际量测，Ready先于模板时下一拍真实行出现再离页", () =>
        {
            var saved = "";
            var notifications = new NotificationsPage { IsReady = true, Realised = (1, 0) };
            var page = new SettingsPage { Notifications = notifications };
            ShellSelfCheck.BeginNotificationsFixture(text => saved = text);
            Assert.True(ShellSelfCheck.VisitNotificationsFixture(page));
            Assert.Equal(0, page.NextCalls);
            Assert.Null(ShellSelfCheck.NotificationsFixtureReading);
            notifications.OnLayout = () => notifications.Realised = (1, 1);
            Assert.True(ShellSelfCheck.VisitNotificationsFixture(page));
            Assert.Equal(1, page.NextCalls);
            Assert.Equal((1, 1), ShellSelfCheck.NotificationsFixtureReading!.Value);
            using var json = JsonDocument.Parse(saved);
            Assert.Equal(2, json.RootElement.GetArrayLength());
            Assert.Equal(0, json.RootElement[0].GetProperty("drawn").GetInt32());
            Assert.Equal(1, json.RootElement[1].GetProperty("drawn").GetInt32());
        });
        Test("通知自检空列表无额外等待，未Ready不读未测模板", () =>
        {
            var page = new SettingsPage { Notifications = new NotificationsPage { Realised = (1, 0) } };
            ShellSelfCheck.BeginNotificationsFixture(_ => { });
            ShellSelfCheck.VisitNotificationsFixture(page);
            Assert.Equal(0, page.Notifications.LayoutCalls);
            Assert.Equal(0, ShellSelfCheck.NotificationsFixtureWaits);
            page.Notifications.IsReady = true;
            page.Notifications.Realised = (0, 0);
            ShellSelfCheck.VisitNotificationsFixture(page);
            Assert.Equal(1, page.NextCalls);
            Assert.Equal(0, ShellSelfCheck.NotificationsFixtureWaits);
            Assert.Equal((0, 0), ShellSelfCheck.NotificationsFixtureReading!.Value);
        });
        Test("通知自检至多八拍，期满保留1/0红读数而不强制realize或降级", () =>
        {
            var page = new SettingsPage { Notifications = new NotificationsPage { IsReady = true, Realised = (1, 0) } };
            ShellSelfCheck.BeginNotificationsFixture(_ => { });
            for (var look = 0; look < 8; look++)
            {
                ShellSelfCheck.VisitNotificationsFixture(page);
                Assert.Equal(0, page.NextCalls);
            }
            ShellSelfCheck.VisitNotificationsFixture(page);
            Assert.Equal(8, ShellSelfCheck.NotificationsFixtureWaits);
            Assert.Equal(1, page.NextCalls);
            Assert.Equal((1, 0), ShellSelfCheck.NotificationsFixtureReading!.Value);
            Assert.False(ShellSelfCheck.VisitNotificationsFixture(page));
        });
        Test("通知布局证据写入失败不抹红资格，也不把等待变无限", () =>
        {
            var page = new SettingsPage { Notifications = new NotificationsPage { IsReady = true, Realised = (1, 1) } };
            ShellSelfCheck.BeginNotificationsFixture(_ => throw new IOException("fixture"));
            ShellSelfCheck.VisitNotificationsFixture(page);
            Assert.True(ShellSelfCheck.InfoFixtureFailed);
            Assert.Equal(1, page.NextCalls);
            Assert.Equal((1, 1), ShellSelfCheck.NotificationsFixtureReading!.Value);
        });
    }
}
