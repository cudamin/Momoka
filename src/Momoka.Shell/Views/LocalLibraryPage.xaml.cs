using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Momoka.LocalMedia;
using Momoka.Shell.Platform;
using Momoka.Shell.Services;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

internal sealed record LocalLibraryRequest(LocalLibraryCoordinator Service, string LibraryId, ISystemLauncher Launcher,
    Func<LocalMediaLibraryEntry, Task>? Play = null, Func<LocalLibraryGroup, Task>? Open = null)
{
    internal const string Prefix = "local-library:";
    internal string Key => Prefix + LibraryId;
}

public sealed partial class LocalLibraryPage : Page, IShellContent
{
    private LocalLibraryRequest? _request;
    public LocalLibraryViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;

    public LocalLibraryPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is LocalLibraryRequest request) Attach(request);
    }

    internal void Attach(LocalLibraryRequest request)
    {
        _request = request;
        Tag = request.Key;
        ViewModel.Attach(request.Service, request.LibraryId, request.Launcher, action => DispatcherQueue.TryEnqueue(() => action()),
            request.Play, request.Open);
    }

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void OnRefresh(object sender, RoutedEventArgs args) => _ = ViewModel.ReloadAsync();
    private void OnRetryIncomplete(object sender, RoutedEventArgs args) => _ = ViewModel.RetryIncompleteAsync();
    private void OnOpenSource(object sender, RoutedEventArgs args) => ViewModel.OpenSourceDirectory();

    /// <summary>点一张作品卡：交给外壳打开现有详情页，不在这一页另造一套详情。</summary>
    private void OnGroupInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is LocalLibraryGroup group) _ = ViewModel.OpenAsync(group);
    }

    private void OnGroupLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryGroup group }) _ = group.EnsurePosterAsync();
    }

    private void OnGroupUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryGroup group }) group.ReleasePoster();
    }

    /// <summary>悬停封面时浮出已看、收藏、更多；移开就收起，和 Emby 海报卡一样不占画面。</summary>
    private void OnGroupPointerEntered(object sender, PointerRoutedEventArgs args) => ShowGroupActions(sender, true);

    private void OnGroupPointerExited(object sender, PointerRoutedEventArgs args) => ShowGroupActions(sender, false);

    private static void ShowGroupActions(object sender, bool show)
    {
        if (sender is not FrameworkElement surface) return;
        if (surface.FindName("GroupActions") is UIElement actions)
        {
            actions.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            actions.Opacity = show ? 1 : 0;
        }
        if (surface.FindName("GroupHoverShade") is UIElement shade) shade.Opacity = show ? 0.78 : 0;
    }

    /// <summary>操作层上的点击不再交给作品卡，否则按一下收藏会顺带打开详情页。</summary>
    private void OnGroupActionTapped(object sender, TappedRoutedEventArgs args) => args.Handled = true;

    private void OnGroupWatched(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryGroup group }) _ = ViewModel.ToggleWatchedAsync(group);
    }

    private void OnGroupFavorite(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryGroup group }) _ = ViewModel.ToggleFavoriteAsync(group);
    }

    /// <summary>卡上的「更多」：播放、打开所在文件夹、重新识别并补全元数据、刷新媒体库。</summary>
    private void OnGroupMore(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: LocalLibraryGroup group } anchor) return;
        var menu = new MenuFlyout();
        var play = new MenuFlyoutItem { Text = "播放", Tag = group, Icon = new FontIcon { Glyph = "\uE768" } };
        play.Click += OnGroupPlay;
        menu.Items.Add(play);
        var open = new MenuFlyoutItem { Text = "打开所在文件夹", Tag = group, Icon = new FontIcon { Glyph = "\uE838" } };
        open.Click += OnGroupOpenLocation;
        menu.Items.Add(open);
        menu.Items.Add(new MenuFlyoutSeparator());
        var retry = new MenuFlyoutItem { Text = "重新识别并补全元数据", Icon = new FontIcon { Glyph = "\uE72C" } };
        retry.Click += (_, _) => _ = ViewModel.RetryIncompleteAsync();
        menu.Items.Add(retry);
        var refresh = new MenuFlyoutItem { Text = "刷新媒体库", Icon = new FontIcon { Glyph = "\uE72C" } };
        refresh.Click += (_, _) => _ = ViewModel.ReloadAsync();
        menu.Items.Add(refresh);
        menu.ShowAt(anchor);
    }

    private void OnGroupPlay(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryGroup group }) _ = ViewModel.PlayAsync(group);
    }

    private void OnGroupOpenLocation(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryGroup group }) ViewModel.OpenLocation(group);
    }

    public void Release() => ViewModel.Cancel();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { Release(); base.OnNavigatedFrom(args); }
}
