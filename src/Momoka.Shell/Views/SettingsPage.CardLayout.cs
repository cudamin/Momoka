using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class SettingsPage
{
    private void OnHomeLayoutListLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ListView list && list.Tag is SettingHomeLayoutRow { EnableSpring: true } row
            && !_priorityMotions.ContainsKey(list))
            _priorityMotions.Add(list, new SettingsPriorityMotion(list, row.Rows, () => row.Rows.Cast<object>(),
                row.BeginReorder, row.EndReorder, element => element.Name == "CardOrderRoot",
                item => item is HomeRowChoice { Pinned: false }));
    }

    internal void RefreshHomeLibraryDirectory()
    {
        if (_activity.IsCurrent(_activityToken)) ViewModel.RefreshHomeRows();
    }

    private void OnHomeLibraryEntryDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (sender is ListView { Tag: SettingHomeLayoutRow row }) row.BeginReorder();
    }

    private void OnHomeLibraryEntryDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (sender is not ListView { Tag: SettingHomeLayoutRow row }) return;
        if (args.DropResult == Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) row.EndReorder();
        else row.CancelReorder();
    }

    private void OnHomeLibraryEntryListUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is ListView { IsLoaded: false, Tag: SettingHomeLayoutRow row }) row.CancelReorder();
    }

    private void OnHomeLibraryEntriesCollapsed(Expander sender, ExpanderCollapsedEventArgs args)
    {
        if (sender.DataContext is HomeRowChoice { LibraryEntries: { } row }) row.CancelReorder();
    }
}
