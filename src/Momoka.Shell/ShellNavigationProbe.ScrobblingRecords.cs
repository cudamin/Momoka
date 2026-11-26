using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Scrobbling;
using Momoka.Shell.Localization;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task ScrobblingRecordsAsync()
    {
        var at = DateTimeOffset.UtcNow;
        ScrobblingRecord[] records =
        [
            new(at, ScrobblingProvider.Douban, "离线示例一", 8, ScrobblingRecordOutcome.Failed, "匿名读取失败"),
            new(at.AddMinutes(-1), ScrobblingProvider.Douban, "离线示例二", 3, ScrobblingRecordOutcome.Watching, "已标记在看"),
            new(at.AddMinutes(-2), ScrobblingProvider.Douban, "离线示例三", 12, ScrobblingRecordOutcome.Watched, "已标记看过"),
            new(at.AddMinutes(-3), ScrobblingProvider.Douban, "离线示例四", 1, ScrobblingRecordOutcome.Skipped, "对应集号无法确认")
        ];
        await InspectScrobblingDialogAsync(ScrobblingProvider.Douban, records, async content =>
        {
            var filter = Get<ComboBox>(content, "ScrobblingRecordsFilter");
            var list = Get<ListView>(content, "ScrobblingRecordsList");
            Require(filter.Visibility == Visibility.Visible && filter.SelectedIndex == 0 && filter.Items.Count == 5,
                "同步记录没有显示完整的结果筛选选项");
            Require(list.Items.Count == records.Length && list.Visibility == Visibility.Visible, "全部记录未完整显示");
            await UntilAsync(() => Descendants(list).OfType<TextBlock>()
                .Any(text => text.Text.StartsWith(records[0].Title, StringComparison.Ordinal) && text.ActualHeight > 0));
            var outcomes = new[] { "在看", "已看", "已跳过", "失败" };
            for (var index = 0; index < outcomes.Length; index++)
            {
                filter.SelectedIndex = index + 1;
                Require(list.Items.Count == 1 && list.Items[0] is ScrobblingRecordRow row
                    && row.Outcome == UiText.Translate(outcomes[index]), $"「{outcomes[index]}」筛选显示了错误的记录");
            }
            filter.SelectedIndex = 0;
            Require(list.Items.Count == records.Length, "切回全部未恢复完整记录");
        });
        await InspectScrobblingDialogAsync(ScrobblingProvider.Bangumi, [], content =>
        {
            var filter = Get<ComboBox>(content, "ScrobblingRecordsFilter");
            var list = Get<ListView>(content, "ScrobblingRecordsList");
            var empty = Get<TextBlock>(content, "RecordsEmpty");
            Require(filter.Visibility == Visibility.Collapsed && list.Visibility == Visibility.Collapsed
                && list.Items.Count == 0 && empty.Visibility == Visibility.Visible && empty.ActualHeight > 0,
                "没有同步记录时，弹窗未正确显示空状态");
            return Task.CompletedTask;
        });
    }

    private async Task InspectScrobblingDialogAsync(ScrobblingProvider provider, IReadOnlyList<ScrobblingRecord> records,
        Func<ScrobblingRecordsView, Task> check)
    {
        var dialog = SettingsPage.CreateScrobblingRecordsDialog(_root.XamlRoot, provider, records);
        var content = (ScrobblingRecordsView)dialog.Content;
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(content);
            Require(Descendants(dialog).OfType<Button>().Any(button => button.Name == "CloseButton" && button.ActualWidth > 0)
                && DialogDismiss.GetClosesOnOutsideClick(dialog), "同步记录未加载统一弹窗模板");
            await check(content);
        }
        finally
        {
            dialog.Hide();
            await shown.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}
