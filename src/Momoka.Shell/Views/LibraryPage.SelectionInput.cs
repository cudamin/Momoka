using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class LibraryPage
{
    private void OnSelectionKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_selecting || e.Key != Windows.System.VirtualKey.Escape) return;
        // PreviewKeyDown precedes the platform menu/dialog handler. Let Escape close its
        // current layer before another Escape ends selection or cancels remaining work.
        if (LibraryBatchCommandBar.IsOpen || VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Count > 0) return;
        if (_batchBusy) _batchCancellation?.Cancel();
        else EndSelection();
        e.Handled = true;
    }
}
