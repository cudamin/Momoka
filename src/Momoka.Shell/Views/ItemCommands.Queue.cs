using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

internal static partial class ItemCommands
{
    private static void AddQueueRows(List<MenuFlyoutItemBase> rows, IShellActions shell, CardItem card,
        Func<bool>? canContinue)
    {
        var queue = new List<MenuFlyoutItemBase>();
        if (card.IsCurrent && shell.CanQueueVideo(card.Item))
        {
            queue.Add(QueueEntry("下一项播放", "ItemPlayNext", "\uE893", () =>
            {
                if (card.IsCurrent && shell.CanQueueVideo(card.Item)) shell.QueueVideo(card.Item, next: true);
            }));
            queue.Add(QueueEntry("追加到队尾", "ItemAppendQueue", "\uE8F4", () =>
            {
                if (card.IsCurrent && shell.CanQueueVideo(card.Item)) shell.QueueVideo(card.Item, next: false);
            }));
        }
        if (shell.HasPendingVideoQueue)
        {
            var pending = new MenuFlyoutSubItem { Text = "本次播放队列", Icon = new FontIcon { Glyph = "\uE142" } };
            AutomationProperties.SetAutomationId(pending, "SessionVideoQueue");
            if (shell.CanResumeVideoQueue)
                pending.Items.Add(QueueEntry("继续本次队列", "ResumeVideoQueue", "\uE768", () =>
                {
                    if (shell.CanResumeVideoQueue) _ = shell.ResumeVideoQueueAsync();
                }));
            pending.Items.Add(QueueEntry("清空待播项", "ClearVideoQueue", "\uE711", shell.ClearVideoQueue));
            queue.Add(pending);
        }
        if (queue.Count == 0) return;
        rows.Add(new MenuFlyoutSeparator());
        rows.AddRange(queue);

        MenuFlyoutItem QueueEntry(string text, string id, string glyph, Action run)
        {
            var entry = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            AutomationProperties.SetAutomationId(entry, id);
            entry.Click += (_, _) => { if (canContinue?.Invoke() != false) run(); };
            return entry;
        }
    }
}
