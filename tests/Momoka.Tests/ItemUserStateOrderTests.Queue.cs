#if ITEM_USER_STATE_ORDER_FIXTURE
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class ItemUserStateOrderTests
{
    private static void RegisterQueueMenu()
    {
        Case("batch5 actual card menu hides unavailable queue and wires next/tail separately", async () =>
        {
            await using var f = new ItemUserStateFixture();
            var unavailable = ItemCommands.Build(f.Session, f.Shell, f.Owner, f.Card);
            Assert.False(unavailable.Items.OfType<MenuFlyoutItem>().Any(row => row.Text is "下一项播放" or "追加到队尾"));
            f.Shell.QueueAvailable = true;
            var menu = ItemCommands.Build(f.Session, f.Shell, f.Owner, f.Card);
            menu.Items.OfType<MenuFlyoutItem>().Single(row => row.Text == "下一项播放").ClickForFixture();
            menu.Items.OfType<MenuFlyoutItem>().Single(row => row.Text == "追加到队尾").ClickForFixture();
            Assert.Equal(2, f.Shell.Queued.Count);
            Assert.True(f.Shell.Queued[0].Next);
            Assert.False(f.Shell.Queued[1].Next);
            Assert.True(ReferenceEquals(f.Card.Item, f.Shell.Queued[0].Item), "card's bound identity passed unchanged");
            Assert.Equal(0, f.Handler.Count, "current queue does not write a server playlist");
        });
        Case("batch5 actual card menu closes stale action and restores queue recovery", async () =>
        {
            await using var f = new ItemUserStateFixture();
            f.Shell.QueueAvailable = true;
            var current = true;
            var menu = ItemCommands.Build(f.Session, f.Shell, f.Owner, f.Card, canContinue: () => current);
            current = false;
            menu.Items.OfType<MenuFlyoutItem>().Single(row => row.Text == "下一项播放").ClickForFixture();
            Assert.Equal(0, f.Shell.Queued.Count, "closed/invalidated menu cannot queue");
            f.Shell.HasPendingVideoQueue = true;
            f.Shell.CanResumeVideoQueue = true;
            menu = ItemCommands.Build(f.Session, f.Shell, f.Owner, f.Card);
            var recovery = menu.Items.OfType<MenuFlyoutSubItem>().Single(row => row.Text == "本次播放队列");
            recovery.Items.OfType<MenuFlyoutItem>().Single(row => row.Text == "继续本次队列").ClickForFixture();
            recovery.Items.OfType<MenuFlyoutItem>().Single(row => row.Text == "清空待播项").ClickForFixture();
            Assert.Equal(1, f.Shell.QueueResumes);
            Assert.Equal(1, f.Shell.QueueClears);
            Assert.Equal(0, f.Handler.Count);
        });
    }
}
#endif
