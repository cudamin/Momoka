using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // Both endpoints are anonymous tasks; Test never calls a server or sends a notification.
    private async Task NotificationInFlightRecoveryAsync()
    {
        foreach (var testing in new[] { false, true })
        {
            var pending = Pending<bool>();
            var calls = 0;
            var savedDraft = "";
            var entry = new UserNotificationInfo { FriendlyName = "匿名原通知", NotifierKey = "offline-service" };
            Task Work(UserNotificationInfo value)
            {
                calls++;
                savedDraft = value.FriendlyName ?? "";
                return pending.Task;
            }
            var context = new NotificationEditorContext(entry.Clone(), false, [], [], [], [],
                value => testing ? throw new InvalidOperationException("测试阶段禁止保存") : Work(value),
                async value => { if (!testing) throw new InvalidOperationException("保存阶段禁止测试"); await Work(value); return true; });
            var dialog = new NotificationEditDialog(context) { XamlRoot = _root.XamlRoot };
            var shown = dialog.ShowAsync().AsTask();
            try
            {
                ResizeProbeClient(760, 900);
                await LayoutAsync(dialog);
                var name = Get<TextBox>(dialog, "NameBox");
                name.Text = "保留在飞通知草稿";
                var submit = testing ? Get<Button>(dialog, "TestButton") : MetadataButton(dialog, "PrimaryButton");
                MetadataInvoke(submit);
                await UntilAsync(() => calls == 1 && Get<bool>(dialog, testing ? "_testing" : "_saving"));
                MetadataInvoke(MetadataButton(dialog, "CloseButton"));
                dialog.Hide();
                await SettleAsync();
                Require(!shown.IsCompleted && !dialog.IsPrimaryButtonEnabled && !Get<Button>(dialog, "TestButton").IsEnabled
                    && name.Text == "保留在飞通知草稿" && savedDraft == name.Text && calls == 1,
                    "通知在飞时关闭、重复提交或丢失草稿");
                var mode = testing ? "test" : "save";
                await SaveProbeScreenAsync($"notification-{mode}-pending-screen.png");
                pending.TrySetException(new IOException("匿名在飞操作失败"));
                await UntilAsync(() => !Get<bool>(dialog, "_saving") && !Get<bool>(dialog, "_testing"));
                Require(!shown.IsCompleted && dialog.IsPrimaryButtonEnabled && Get<Button>(dialog, "TestButton").IsEnabled
                    && Get<InfoBar>(dialog, "ResultBar").IsOpen && name.Text == "保留在飞通知草稿"
                    && entry.FriendlyName == "匿名原通知", "通知失败后未恢复草稿、反馈或关闭入口");
                await SaveProbeScreenAsync($"notification-{mode}-failed-screen.png");
                MetadataInvoke(MetadataButton(dialog, "CloseButton"));
                Require(await shown == ContentDialogResult.None && calls == 1, "通知失败后的取消重放了操作");
            }
            finally
            {
                pending.TrySetResult(false);
                await UntilAsync(() => !Get<bool>(dialog, "_saving") && !Get<bool>(dialog, "_testing"));
                dialog.Hide();
                await shown;
                ResizeInspect(1420, 980);
            }
        }
    }
}
