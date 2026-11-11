using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private MenuFlyout? _extraMenu;

    private void OnExtrasClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button anchor) _ = ShowExtrasAsync(anchor);
    }

    internal async Task ShowExtrasAsync(Button anchor)
    {
        if (ViewModel.ExtraVisibility != Visibility.Visible) return;
        _extraMenu?.Hide();
        var menu = new MenuFlyout();
        _extraMenu = menu;
        menu.Items.Add(new MenuFlyoutItem { Text = "正在读取花絮与特典…", IsEnabled = false });
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_extraMenu, menu)) _extraMenu = null;
        };
        menu.ShowAt(anchor);
        var selection = await ViewModel.GetExtrasAsync();
        if (!ReferenceEquals(_extraMenu, menu)) return;
        if (selection is null || !ViewModel.OwnsExtras(selection))
        {
            menu.Hide();
            return;
        }
        menu.Items.Clear();
        foreach (var option in selection.Options)
        {
            var entry = new MenuFlyoutItem { Text = $"{option.Title} · {option.Category}" };
            AutomationProperties.SetAutomationId(entry, "ExtraOption" + menu.Items.Count);
            entry.Click += async (_, _) => await ViewModel.OpenExtraAsync(selection, option);
            menu.Items.Add(entry);
        }
        if (selection.Error is { } error)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(new MenuFlyoutItem { Text = error, IsEnabled = false });
            var retry = new MenuFlyoutItem { Text = "重新加载" };
            AutomationProperties.SetAutomationId(retry, "ExtraRetry");
            retry.Click += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.OwnsExtras(selection)) _ = ShowExtrasAsync(anchor);
            });
            menu.Items.Add(retry);
        }
        else if (menu.Items.Count == 0) menu.Hide();
    }

    private void OnExtraStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DetailViewModel.ExtraVisibility)
            && ViewModel.ExtraVisibility != Visibility.Visible) _extraMenu?.Hide();
    }
}
