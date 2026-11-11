using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool _syncingHomeNavigation;

    /// <summary>
    /// 首页／收藏胶囊站在标题栏最左边，占的是主页那颗按钮的位置（2026-11-11 用户令「移到主页左上角，
    /// 替换原有的按钮」）。只在首页和收藏页现身：其余页面收起、主页按钮回来；侧边栏展开时两边都有入口，
    /// 胶囊收起。没有收藏能力的来源（纯 Plex 之类）整个收起，主页按钮照旧。
    /// </summary>
    private void SyncHomeNavigation()
    {
        var favoritesAvailable = _session?.IsSignedIn == true
            && FavoriteAccounts.Applicable(_session.Sources).Count > 0;
        var shown = favoritesAvailable && !SidebarShown
            && _current is "home" or LibraryRequest.FavoritesTag;

        var visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (HomeNavigation.Visibility != visibility) HomeNavigation.Visibility = visibility;
        var homeVisibility = shown || SidebarShown ? Visibility.Collapsed : Visibility.Visible;
        if (HomeButton.Visibility != homeVisibility) HomeButton.Visibility = homeVisibility;
        if (!favoritesAvailable) return;

        _syncingHomeNavigation = true;
        try
        {
            var selected = _current == LibraryRequest.FavoritesTag ? HomeNavigationFavorites : HomeNavigationHome;
            if (!ReferenceEquals(HomeNavigation.SelectedItem, selected)) HomeNavigation.SelectedItem = selected;
        }
        finally { _syncingHomeNavigation = false; }

        var pressed = (Brush)Application.Current.Resources["EgOverlayPressedBrush"];
        HomeNavigationHome.Background = ReferenceEquals(HomeNavigation.SelectedItem, HomeNavigationHome) ? pressed : null;
        HomeNavigationFavorites.Background = ReferenceEquals(HomeNavigation.SelectedItem, HomeNavigationFavorites) ? pressed : null;
    }

    /// <summary>
    /// 胶囊的底：12% 的高透明亚克力，染色跟着主题的面色走（浅色主题另有一套，高对比度用系统色）。
    /// 由 <see cref="PaintTitleInk"/> 在主题或标题栏底变化时调用 —— 它压在轮播图上，底色不跟着换就会发灰。
    /// </summary>
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

    private void OnHomeNavigationChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_syncingHomeNavigation || HomeNavigation.Visibility != Visibility.Visible || _session?.IsSignedIn != true) return;
        if (ReferenceEquals(sender.SelectedItem, HomeNavigationHome)) OnHomeClicked(sender, new RoutedEventArgs());
        else if (ReferenceEquals(sender.SelectedItem, HomeNavigationFavorites)
            && FavoriteAccounts.Applicable(_session.Sources).Count > 0)
        {
            NavigationTiming.Click("favorites-button");
            GoTo(LibraryRequest.FavoritesTag);
        }
    }
}
