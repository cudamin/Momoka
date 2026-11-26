using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Scrobbling;
using Momoka.Shell.Localization;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class SettingsPage
{
    private ContentDialog? _scrobblingRecordsDialog;

    private async Task ShowScrobblingRecordsAsync(ScrobblingProvider provider)
    {
        if (!_activity.IsCurrent(_activityToken) || XamlRoot is not { } root
            || _scrobblingRecordsDialog is not null || _scrobblingService is not { } service) return;

        var records = service.GetRecords(provider);
        var content = records.Count == 0
            ? UiText.Translate("还没有自动标记记录。")
            : string.Join("\n\n", records.Select(record =>
            {
                var outcome = record.Outcome switch
                {
                    ScrobblingRecordOutcome.Watching => UiText.Translate("在看"),
                    ScrobblingRecordOutcome.Watched => UiText.Translate("已看"),
                    ScrobblingRecordOutcome.Skipped => UiText.Translate("已跳过"),
                    ScrobblingRecordOutcome.Failed => UiText.Translate("失败"),
                    _ => UiText.Translate("未知")
                };
                var episode = record.EpisodeNumber is { } number
                    ? string.Format(UiText.Translate("第 {0} 集"), number)
                    : UiText.Translate("集数未知");
                return $"{record.At.ToLocalTime():yyyy-MM-dd HH:mm:ss}　{outcome}\n{record.Title}　{episode}\n{UiText.Translate(record.Detail)}";
            }));
        var title = provider == ScrobblingProvider.Bangumi ? "Bangumi 同步记录" : "豆瓣同步记录";
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = UiText.Translate(title),
            Content = new ScrollViewer
            {
                MaxHeight = 480,
                Content = new TextBlock
                {
                    Text = content,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    MaxWidth = 620
                }
            },
            CloseButtonText = UiText.Translate("关闭对话框"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        AutomationProperties.SetAutomationId(dialog, "ScrobblingRecordsDialog");
        _scrobblingRecordsDialog = dialog;
        _dismissScrobblingRecords = dialog.Hide;
        try { await dialog.ShowAsync(); }
        catch (InvalidOperationException) { }
        finally
        {
            if (ReferenceEquals(_scrobblingRecordsDialog, dialog))
            {
                _scrobblingRecordsDialog = null;
                _dismissScrobblingRecords = null;
            }
        }
    }
}
