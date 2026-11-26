using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Scrobbling;
using Momoka.Shell.Localization;
using Momoka.Theming;

namespace Momoka.Shell.Views;

public sealed partial class SettingsPage
{
    private ContentDialog? _scrobblingRecordsDialog;

    private async Task ShowScrobblingRecordsAsync(ScrobblingProvider provider)
    {
        var activity = _activityToken;
        if (!_activity.IsCurrent(activity) || XamlRoot is not { } root
            || _scrobblingRecordsDialog is not null || _scrobblingService is not { } service) return;

        ContentDialog? dialog = null;
        try
        {
            dialog = CreateScrobblingRecordsDialog(root, provider, service.GetRecords(provider));
            _scrobblingRecordsDialog = dialog;
            _dismissScrobblingRecords = dialog.Hide;
            await ShowScrobblingRecordsDialogAsync(dialog, service, provider, () => _activity.IsCurrent(activity)
                && ReferenceEquals(_scrobblingService, service) && ReferenceEquals(_scrobblingRecordsDialog, dialog)
                && _dismissScrobblingRecords is not null);
        }
        catch (Exception error)
        {
            Log.Warn(Category, "无法打开同步记录", error);
            if (_activity.IsCurrent(activity)) ViewModel.ReportScrobblingRecordsOpenFailed();
        }
        finally
        {
            if (ReferenceEquals(_scrobblingRecordsDialog, dialog))
            {
                _scrobblingRecordsDialog = null;
                _dismissScrobblingRecords = null;
            }
        }
    }

    internal static async Task ShowScrobblingRecordsDialogAsync(ContentDialog dialog, ScrobblingService service,
        ScrobblingProvider provider, Func<bool> isCurrent)
    {
        var content = (ScrobblingRecordsView)dialog.Content;
        var root = dialog.XamlRoot;
        var queue = content.DispatcherQueue;
        var refreshQueued = 0;
        var closed = false;

        void Refresh()
        {
            if (!closed && isCurrent()) content.UpdateRecords(service.GetRecords(provider));
        }

        void OnChanged()
        {
            if (Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
            if (!queue.TryEnqueue(() =>
                {
                    Interlocked.Exchange(ref refreshQueued, 0);
                    try { Refresh(); }
                    catch (Exception error) { Log.Warn(Category, "无法刷新同步记录", error); }
                })) Interlocked.Exchange(ref refreshQueued, 0);
        }

        void Detach()
        {
            if (closed) return;
            closed = true;
            service.Changed -= OnChanged;
            root.Changed -= OnRootChanged;
        }

        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => content.ApplySize(sender);
        void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => Detach();

        dialog.Closed += OnClosed;
        root.Changed += OnRootChanged;
        service.Changed += OnChanged;
        try
        {
            content.ApplySize(root);
            Refresh();
            await dialog.ShowAsync();
        }
        finally
        {
            Detach();
            dialog.Closed -= OnClosed;
        }
    }

    internal static ContentDialog CreateScrobblingRecordsDialog(XamlRoot root, ScrobblingProvider provider,
        IReadOnlyList<ScrobblingRecord> records, ScrobblingRecordOutcome? initialOutcome = null)
    {
        var title = provider == ScrobblingProvider.Bangumi ? "Bangumi 同步记录" : "豆瓣同步记录";
        var content = new ScrobblingRecordsView(records, initialOutcome);
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
        dialog.Resources["ContentDialogMinWidth"] = 0d;
        AutomationProperties.SetAutomationId(dialog, "ScrobblingRecordsDialog");
        return dialog;
    }
}
