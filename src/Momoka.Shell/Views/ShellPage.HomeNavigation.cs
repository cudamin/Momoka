using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool _syncingHomeNavigation;
    private double _homeNavigationSize = -1;
    private bool _homeNavigationSizePending;

    private void SyncHomeNavigation()
    {
        var available = _session?.IsSignedIn == true
            && FavoriteAccounts.Applicable(_session.Sources).Count > 0;
        var shown = available && _current is "home" or LibraryRequest.FavoritesTag;
        var visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (HomeNavigationHost.Visibility != visibility) HomeNavigationHost.Visibility = visibility;
        if (HomeNavigation.Visibility != visibility) HomeNavigation.Visibility = visibility;
        if (!shown)
        {
            UpdateHomeNavigationInset();
            return;
        }
        // HomePage already brings its scroller up through the title bar. Keep that origin unchanged
        // while a banner is visible; only plain home/favorites need a normal navigation row.
        UpdateHomeNavigationSize();
        UpdateHomeNavigationInset();
        _syncingHomeNavigation = true;
        try
        {
            var selectedItem = _current == LibraryRequest.FavoritesTag ? HomeNavigationFavorites : HomeNavigationHome;
            if (!ReferenceEquals(HomeNavigation.SelectedItem, selectedItem)) HomeNavigation.SelectedItem = selectedItem;
        }
        finally { _syncingHomeNavigation = false; }
        var selected = (Brush)Application.Current.Resources["EgOverlayPressedBrush"];
        HomeNavigationHome.Background = ReferenceEquals(HomeNavigation.SelectedItem, HomeNavigationHome) ? selected : null;
        HomeNavigationFavorites.Background = ReferenceEquals(HomeNavigation.SelectedItem, HomeNavigationFavorites) ? selected : null;
    }

    private void ScheduleHomeNavigationSize()
    {
        if (_homeNavigationSizePending || HomeNavigationHost.Visibility != Visibility.Visible) return;
        _homeNavigationSizePending = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _homeNavigationSizePending = false;
            if (_closing || HomeNavigationHost.Visibility != Visibility.Visible) return;
            UpdateHomeNavigationSize();
            UpdateHomeNavigationInset();
        })) _homeNavigationSizePending = false;
    }

    private void UpdateHomeNavigationInset()
    {
        var overlay = _current == "home" && _titleStrip == TitleStrip.OnScrim;
        var top = HomeNavigation.Visibility == Visibility.Visible && !overlay
            ? HomeNavigationHost.Height + HomeNavigationHost.Margin.Top * 2 : 0;
        if (Math.Abs(ContentFrame.Margin.Top - top) > 0.01)
            ContentFrame.Margin = new Thickness(0, top, 0, ContentFrame.Margin.Bottom);
    }

    private void PaintHomeNavigation()
    {
        var color = ((SolidColorBrush)Application.Current.Resources["EgSurfaceBrush"]).Color;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var dictionary = (ResourceDictionary)HomeNavigation.Resources.ThemeDictionaries[theme];
            var glass = (AcrylicBrush)dictionary["HomeNavigationBrush"];
            glass.TintColor = glass.FallbackColor = color;
        }
        var key = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast ? "HighContrast"
            : ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        HomeNavigation.Background = (Brush)((ResourceDictionary)HomeNavigation.Resources.ThemeDictionaries[key])["HomeNavigationBrush"];
    }

    private void UpdateHomeNavigationSize()
    {
        var width = ContentHost.ActualWidth;
        var amount = Math.Clamp((width - 600) / 600, 0, 1);
        if (Math.Abs(amount - _homeNavigationSize) < 0.001) return;
        _homeNavigationSize = amount;
        // Scale the fixed native selector as a whole. Changing its ItemsView row geometry during
        // sidebar reflow can cause a WinUI layout cycle, even from a deferred callback.
        var scale = (30 + 6 * amount) / 36;
        HomeNavigationHost.Width = 120 * scale;
        HomeNavigationHost.Height = 36 * scale;
        HomeNavigationHost.Margin = new Thickness(0, 8 + 4 * amount, 0, 0);
    }

    private void OnHomeNavigationChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncingHomeNavigation || HomeNavigation.Visibility != Visibility.Visible || _session?.IsSignedIn != true) return;
        if (ReferenceEquals(sender.SelectedItem, HomeNavigationHome)) OnHomeClicked(sender, new RoutedEventArgs());
        else if (ReferenceEquals(sender.SelectedItem, HomeNavigationFavorites)
            && FavoriteAccounts.Applicable(_session.Sources).Count > 0)
            GoTo(LibraryRequest.FavoritesTag);
    }
}
