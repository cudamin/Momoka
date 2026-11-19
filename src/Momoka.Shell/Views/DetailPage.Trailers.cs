using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private MenuFlyout? _trailerMenu;

    /// <summary>
    /// 预告片那一份选择菜单。入口自 2026-11-19 起只剩「更多」菜单里的一行（用户令「把沉浸模式中的预告片按钮
    /// 收进更多按钮中」），所以 <paramref name="anchor"/> 是那一颗「更多」键：按下「预告片」后更多菜单先收，
    /// 这一张在它旁边接着开。自检和重试也直接走这里，不必先点开更多。
    /// </summary>
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

    private void OnNoticeActionClick(object sender, RoutedEventArgs e) => ViewModel.InvokeNoticeAction();

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
