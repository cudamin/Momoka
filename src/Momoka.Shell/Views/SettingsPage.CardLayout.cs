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
}
