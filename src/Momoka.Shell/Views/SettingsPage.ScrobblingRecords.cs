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

        var dialog = CreateScrobblingRecordsDialog(root, provider, service.GetRecords(provider));
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

    internal static ContentDialog CreateScrobblingRecordsDialog(XamlRoot root, ScrobblingProvider provider,
        IReadOnlyList<ScrobblingRecord> records)
    {
        var title = provider == ScrobblingProvider.Bangumi ? "Bangumi 同步记录" : "豆瓣同步记录";
        var content = new ScrobblingRecordsView(records);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = UiText.Translate(title),
            Content = content,
            CloseButtonText = UiText.Translate("关闭对话框"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        dialog.Resources["ContentDialogMaxWidth"] = 700d;
        AutomationProperties.SetAutomationId(dialog, "ScrobblingRecordsDialog");
        return dialog;
    }
}
