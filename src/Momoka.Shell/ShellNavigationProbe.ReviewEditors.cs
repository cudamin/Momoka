using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // Real controls and UIA Invoke/Toggle; text is assigned through control properties. No pointer,
    // server session, HTTP, account or notification delivery is used by these anonymous delegates.
    private async Task ReviewMetadataEditorAsync()
    {
        foreach (var width in new[] { 1100, 760 })
        {
            ResizeProbeClient(width, 900);
            await LayoutAsync(_root);
            var original = new JsonObject
            {
                ["Id"] = "offline-review-metadata", ["Name"] = "离线标签与草稿",
                ["Tags"] = new JsonArray("Sci-Fi, Fantasy"), ["Genres"] = new JsonArray("动画")
            };
            var attempts = 0;
            var pending = Pending<bool>();
            JsonObject? submitted = null;
            var dialog = new MetadataDialog(new EmbyItem { Id = "offline-review-metadata", Name = "离线标签与草稿", Type = EmbyItemType.Movie },
                ItemMetadataEdit.Read(original), async edit =>
                {
                    attempts++;
                    submitted = (JsonObject)original.DeepClone();
                    var changed = edit.ApplyTo(submitted);
                    if (attempts == 1) throw new InvalidOperationException("离线模拟：保存暂不可用，未访问服务器");
                    await pending.Task;
                    return changed;
                }) { XamlRoot = _root.XamlRoot, RequestedTheme = ElementTheme.Dark };
            var shown = dialog.ShowAsync().AsTask();
            try
            {
                await LayoutAsync(dialog);
                Require(!dialog.Edit.ApplyTo((JsonObject)original.DeepClone()), "含逗号标签的原样表单改变了数据");
                var title = Get<TextBox>(dialog, "NameBox");
                var tags = Get<TextBox>(dialog, "TagsBox");
                title.Text = "离线修改后保留的草稿";
                tags.Text = "\"Sci-Fi, Fantasy\"、新标签";
                await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
                ReviewEditorBounds(dialog);
                MetadataInvoke(MetadataButton(dialog, "PrimaryButton"));
                await UntilAsync(() => attempts == 1 && !Get<bool>(dialog, "_saving"));
                Require(!shown.IsCompleted && title.Text == "离线修改后保留的草稿"
                    && tags.Text == "\"Sci-Fi, Fantasy\"、新标签" && Get<InfoBar>(dialog, "Problem").IsOpen
                    && dialog.IsPrimaryButtonEnabled, "Jellyfin 表保存失败丢了输入、反馈或重试入口");
                Require(submitted?["Tags"] is JsonArray { Count: 2 } values && values[0]?.GetValue<string>() == "Sci-Fi, Fantasy",
                    "实际表单把单个带逗号标签拆开");
                await SaveProbeScreenAsync($"review-metadata-{width}-failed-screen.png");
                MetadataInvoke(MetadataButton(dialog, "PrimaryButton"));
                await UntilAsync(() => attempts == 2 && Get<bool>(dialog, "_saving"));
                Require(!dialog.IsPrimaryButtonEnabled && !Get<ScrollViewer>(dialog, "EditorScroll").IsEnabled
                    && Get<ProgressBar>(dialog, "SavingProgress").Visibility == Visibility.Visible, "保存中缺少进度或允许重复编辑");
                MetadataInvoke(MetadataButton(dialog, "CloseButton"));
                await SettleAsync();
                Require(!shown.IsCompleted, "在途保存被取消按钮丢弃");
                await SaveProbeScreenAsync($"review-metadata-{width}-saving-screen.png");
                pending.TrySetResult(true);
                Require(await shown == ContentDialogResult.Primary && dialog.Changed && attempts == 2,
                    "重试成功未关闭或未交付已确认结果");
                Require(original["Name"]?.GetValue<string>() == "离线标签与草稿" && original["Tags"]!.AsArray().Count == 1,
                    "匿名保存修改了原始文档，下一次重试会变成假无改动");
            }
            finally { pending.TrySetResult(false); dialog.Hide(); await shown; }

            var cancelledWrites = 0;
            var cancelled = new MetadataDialog(new EmbyItem { Name = "取消草稿", Type = EmbyItemType.Movie }, ItemMetadataEdit.Read(original),
                _ => { cancelledWrites++; return Task.FromResult(true); }) { XamlRoot = _root.XamlRoot, RequestedTheme = ElementTheme.Dark };
            var cancelledShown = cancelled.ShowAsync().AsTask();
            try
            {
                await LayoutAsync(cancelled);
                Get<TextBox>(cancelled, "NameBox").Text = "取消后不保存";
                MetadataInvoke(MetadataButton(cancelled, "CloseButton"));
                Require(await cancelledShown == ContentDialogResult.None && cancelledWrites == 0, "取消Jellyfin表触发保存委托");
            }
            finally { cancelled.Hide(); await cancelledShown; }
        }
    }

    private async Task ReviewNotificationEditorAsync()
    {
        foreach (var width in new[] { 1100, 760 })
        foreach (var missing in new[] { false, true })
        {
            ResizeProbeClient(width, 900);
            await LayoutAsync(_root);
            var entry = new UserNotificationInfo
            {
                Id = "offline-notification", FriendlyName = "离线通知草稿", NotifierKey = "offline-service",
                EventIds = ["playback.stop", "unlisted-event"], UserIds = ["u1"], LibraryIds = ["l1"], DeviceIds = ["d1"]
            };
            var attempts = 0;
            var testCalls = 0;
            UserNotificationInfo? saved = null;
            var context = new NotificationEditorContext(entry.Clone(), false,
                [new() { Id = "playback", Name = "播放事件", Events = [new() { Id = "playback.stop", Name = "播放停止" }, new() { Id = "playback.start", Name = "播放开始" }] }],
                missing ? [new() { Id = "u2", Name = "其他用户" }] : null,
                missing ? [new() { Guid = "l2", Name = "其他媒体库" }] : null,
                missing ? [new() { Id = "d2", Name = "其他设备" }] : null,
                value =>
                {
                    saved = value.Clone();
                    attempts++;
                    return attempts == 1 ? Task.FromException(new InvalidOperationException("离线模拟保存失败，未发送通知")) : Task.CompletedTask;
                },
                _ => { testCalls++; throw new InvalidOperationException("此探针不得发送测试通知"); });
            var dialog = new NotificationEditDialog(context) { XamlRoot = _root.XamlRoot, RequestedTheme = ElementTheme.Dark };
            var shown = dialog.ShowAsync().AsTask();
            try
            {
                await LayoutAsync(dialog);
                ReviewEditorBounds(dialog);
                var title = Get<TextBox>(dialog, "NameBox");
                title.Text = "离线修改后的通知";
                var events = Get<StackPanel>(dialog, "EventList");
                var stop = Descendants(events).OfType<CheckBox>().Single(box => Equals(box.Tag, "playback.stop"));
                var start = Descendants(events).OfType<CheckBox>().Single(box => Equals(box.Tag, "playback.start"));
                Require(stop.IsChecked == true && start.IsChecked == false, "事件勾选未按原有ID还原");
                MetadataInvoke(MetadataButton(dialog, "PrimaryButton"));
                await UntilAsync(() => attempts == 1 && !Get<bool>(dialog, "_saving"));
                Require(!shown.IsCompleted && Get<InfoBar>(dialog, "ResultBar").IsOpen && title.Text == "离线修改后的通知",
                    "通知保存失败丢了草稿或反馈");
                Require(saved is not null && saved.EventIds.Order().SequenceEqual(entry.EventIds.Order()), "只改名称丢失原事件ID");
                ReviewNotificationScope(saved!);
                var scroll = (ScrollViewer)dialog.Content;
                await ScrollProbeAsync(scroll, scroll.ScrollableHeight);
                await SaveProbeScreenAsync($"review-notification-{(missing ? "missing" : "failed")}-{width}-draft-screen.png");
                ((IToggleProvider)new CheckBoxAutomationPeer(stop).GetPattern(PatternInterface.Toggle)).Toggle();
                ((IToggleProvider)new CheckBoxAutomationPeer(start).GetPattern(PatternInterface.Toggle)).Toggle();
                Require(stop.IsChecked == false && start.IsChecked == true, "UIA Toggle没有改变真实事件控件");
                MetadataInvoke(MetadataButton(dialog, "PrimaryButton"));
                await UntilAsync(() => shown.IsCompleted);
                Require(saved is not null && saved.EventIds.Order().SequenceEqual(new[] { "playback.start", "unlisted-event" }.Order()),
                    "改选事件没有保存有效ID或清掉了未列出的原事件");
                ReviewNotificationScope(saved!);
                Require(attempts == 2 && testCalls == 0 && entry.FriendlyName == "离线通知草稿",
                    "通知编辑污染原条目、发送了测试通知或重复保存");
            }
            finally { dialog.Hide(); await shown; }
        }
    }

    private static void ReviewNotificationScope(UserNotificationInfo entry) => Require(
        entry.UserIds.SequenceEqual(["u1"]) && entry.LibraryIds.SequenceEqual(["l1"]) && entry.DeviceIds.SequenceEqual(["d1"]),
        "通知名单失败或缺项清空了未编辑的范围限制");

    private void ReviewEditorBounds(ContentDialog dialog)
    {
        foreach (var name in new[] { "PrimaryButton", "CloseButton" })
        {
            var button = MetadataButton(dialog, name);
            var bounds = button.TransformToVisual(_root).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
            Require(button.ActualWidth > 0 && button.ActualHeight > 0 && bounds.Left >= -1 && bounds.Top >= -1
                && bounds.Right <= _root.ActualWidth + 1 && bounds.Bottom <= _root.ActualHeight + 1,
                $"{dialog.GetType().Name}/{name} 在当前宽度被裁掉");
        }
    }
}
