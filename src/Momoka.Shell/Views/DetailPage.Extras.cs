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
        if (_extraMenu is { } previous)
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnClosed(object? sender, object args) => closed.TrySetResult();
            previous.Closed += OnClosed;
            try
            {
                previous.Hide();
                await closed.Task;
            }
            finally { previous.Closed -= OnClosed; }
            if (ViewModel.ExtraVisibility != Visibility.Visible || _extraMenu is not null) return;
        }
        var menu = new MenuFlyout();
        DetailViewModel.ExtraSelection? selection = null;
        var retryRequested = false;
        _extraMenu = menu;
        menu.Items.Add(new MenuFlyoutItem { Text = "正在读取花絮与特典…", IsEnabled = false });
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_extraMenu, menu)) return;
            _extraMenu = null;
            ViewModel.CloseExtras(selection);
            if (retryRequested && selection is not null)
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_extraMenu is null && ViewModel.CanRetryExtras(selection)) _ = ShowExtrasAsync(anchor);
                });
        };
        menu.ShowAt(anchor);
        selection = await ViewModel.GetExtrasAsync();
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
            entry.Click += async (_, _) =>
            {
                if (ReferenceEquals(_extraMenu, menu) && menu.IsOpen)
                    await ViewModel.OpenExtraAsync(selection, option);
            };
            menu.Items.Add(entry);
        }
        if (selection.Error is { } error)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(new MenuFlyoutItem { Text = error, IsEnabled = false });
            var retry = new MenuFlyoutItem { Text = "重新加载" };
            AutomationProperties.SetAutomationId(retry, "ExtraRetry");
            retry.Click += (_, _) =>
            {
                retryRequested = ReferenceEquals(_extraMenu, menu) && menu.IsOpen && ViewModel.OwnsExtras(selection);
            };
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
