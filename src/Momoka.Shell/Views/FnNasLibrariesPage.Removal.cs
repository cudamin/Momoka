using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class FnNasLibrariesPage
{
    private async void OnRemovedFiles(object sender, RoutedEventArgs e)
    {
        if (_dialog is not null || XamlRoot is null || sender is not Button { Tag: FnNasLibraryRow row }
            || row.Category == "IPTV" || ViewModel.RemovalContext() is not { } context) return;
        var dialog = new FnNasRemovedFilesDialog(context.Session, context.SourceKey, context.Service, row.Id, row.Name)
        { XamlRoot = XamlRoot };
        _dialog = dialog;
        try { await dialog.ShowAsync(); }
        catch (Exception error)
        {
            ViewModel.ReportRemovalFailure(error);
            dialog.ViewModel.Dispose();
        }
        finally { _dialog = null; }
    }
}
