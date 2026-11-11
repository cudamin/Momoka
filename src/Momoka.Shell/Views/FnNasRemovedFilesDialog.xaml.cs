using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Shell.Services;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class FnNasRemovedFilesDialog : ContentDialog
{
    internal FnNasRemovedFilesViewModel ViewModel { get; } = new();
    private bool _submitting;
    private bool _released;

    internal FnNasRemovedFilesDialog(EmbySession session, string sourceKey, FnNasLibraryRemovalService service,
        string libraryGuid, string libraryName)
    {
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Title = "已移除文件 · " + libraryName;
        AutomationProperties.SetAutomationId(this, "FnNasRemovedFilesDialog");
        ViewModel.AttachRemoval(session, sourceKey, service, libraryGuid, libraryName);
        Opened += async (_, _) => await ViewModel.ReloadAsync();
        Closed += (_, _) => ViewModel.Dispose();
    }

    private async void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FnNasRemovedFile entry) await ViewModel.OpenAsync(entry);
    }
    private async void OnBack(object sender, RoutedEventArgs e) => await ViewModel.BackAsync();
    private async void OnRefresh(object sender, RoutedEventArgs e) => await ViewModel.ReloadAsync();
    private async void OnRestore(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_submitting || !ViewModel.CanRestore) return;
        _submitting = true;
        try { await ViewModel.RestoreSelectedAsync(confirmed: true); }
        finally { _submitting = false; }
    }
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Once submitted, retain confirmation instead of treating an in-flight write as cancelled.
        args.Cancel = _submitting && !_released;
    }

    internal void Release()
    {
        _released = true;
        ViewModel.Cancel();
        Hide();
    }
}
