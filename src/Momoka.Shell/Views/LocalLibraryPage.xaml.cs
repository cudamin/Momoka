using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.LocalMedia;
using Momoka.Shell.Platform;
using Momoka.Shell.Services;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

internal sealed record LocalLibraryRequest(LocalLibraryCoordinator Service, string LibraryId, ISystemLauncher Launcher,
    Func<LocalMediaLibraryEntry, Task>? Play = null)
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
        ViewModel.Attach(request.Service, request.LibraryId, request.Launcher, action => DispatcherQueue.TryEnqueue(() => action()), request.Play);
    }

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void OnRefresh(object sender, RoutedEventArgs args) => _ = ViewModel.ReloadAsync();
    private void OnOpenSource(object sender, RoutedEventArgs args) => ViewModel.OpenSourceDirectory();
    private void OnOpenLocation(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryRow row }) ViewModel.OpenLocation(row);
    }
    private void OnPlay(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryRow row }) _ = ViewModel.PlayAsync(row);
    }
    private void OnRowLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryRow row }) _ = row.EnsurePosterAsync();
    }
    private void OnRowUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: LocalLibraryRow row }) row.ReleasePoster();
    }
    public void Release() => ViewModel.Cancel();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { Release(); base.OnNavigatedFrom(args); }
}
