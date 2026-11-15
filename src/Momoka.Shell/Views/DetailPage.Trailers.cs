using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private MenuFlyout? _trailerMenu;

    private void OnTrailersClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button anchor) _ = ShowTrailersAsync(anchor);
    }

    internal async Task ShowTrailersAsync(Button anchor)
    {
        if (ViewModel.TrailerVisibility != Visibility.Visible) return;
        _trailerMenu?.Hide();
        var menu = new MenuFlyout();
        _trailerMenu = menu;
        menu.Items.Add(new MenuFlyoutItem { Text = "正在读取预告片…", IsEnabled = false });
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trailerMenu, menu)) _trailerMenu = null;
        };
        menu.ShowAt(anchor);
        var selection = await ViewModel.GetTrailersAsync();
        if (!ReferenceEquals(_trailerMenu, menu)) return;
        if (selection is null || !ViewModel.OwnsTrailers(selection))
        {
            menu.Hide();
            return;
        }
        menu.Items.Clear();
        foreach (var option in selection.Options)
        {
            var entry = new MenuFlyoutItem { Text = $"{option.Title} · {option.Destination}" };
            AutomationProperties.SetAutomationId(entry, "TrailerOption" + menu.Items.Count);
            entry.Click += async (_, _) => await ViewModel.OpenTrailerAsync(selection, option);
            menu.Items.Add(entry);
        }
        if (selection.Error is { } error)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(new MenuFlyoutItem { Text = error, IsEnabled = false });
            var retry = new MenuFlyoutItem { Text = "重新加载" };
            AutomationProperties.SetAutomationId(retry, "TrailerRetry");
            retry.Click += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.OwnsTrailers(selection)) _ = ShowTrailersAsync(anchor);
            });
            menu.Items.Add(retry);
        }
        else if (menu.Items.Count == 0)
            menu.Items.Add(new MenuFlyoutItem { Text = "服务器未返回可用的预告片", IsEnabled = false });
    }

    private void OnTrailerStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DetailViewModel.TrailerVisibility)) UpdateStoreTrailer();
        if (args.PropertyName == nameof(DetailViewModel.StoreAmbientHeight) && IsLoaded && ViewModel.StoreMode
            && (_storeTrailerPlayer is not null || _storeYouTube is not null)) LayoutStoreTrailer();
        if (args.PropertyName == nameof(DetailViewModel.TrailerVisibility)
            && ViewModel.TrailerVisibility != Visibility.Visible)
            _trailerMenu?.Hide();
    }
}
