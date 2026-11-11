using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Emby;
using Momoka.Music;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MusicLibraryPage : Page, IShellContent
{
    private IShellActions? _actions;
    private LibraryRequest? _request;
    public MusicLibraryViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;
    public MusicLibraryPage() => InitializeComponent();
    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    protected override async void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is not LibraryRequest request) return;
        _request = request;
        _actions = request.Services.GetRequiredService<IShellActions>();
        _actions.SetTitleStrip(TitleStrip.Plain);
        ViewModel.Attach(request, request.Services.GetRequiredService<EmbySession>(), request.Services.GetRequiredService<EmbyImageStore>());
        await ViewModel.ReloadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args) { Release(); base.OnNavigatedFrom(args); }
    public void Release() { ViewModel.Release(); _actions = null; _request = null; }
    private async void OnRefresh(object sender, RoutedEventArgs args) => await ViewModel.ReloadAsync();
    private async void OnSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => await ViewModel.ReloadAsync();
    private async void OnMore(object sender, RoutedEventArgs args) => await ViewModel.LoadMoreAsync();
    private async void OnSection(object sender, RoutedEventArgs args)
    {
        if (_request is not null && sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var index))
            await ViewModel.SelectAsync((MusicSection)index);
    }
    private async void OnItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not CardItem card || _actions is null) return;
        if (MusicCatalog.IsAudio(card.Item.Type))
        {
            if (_actions is ShellPage shell) await shell.PlayMusicAsync(card.Item, ViewModel.Tracks, ViewModel.CaptureQueueLoader());
            else await _actions.PlayAsync(card.Item, episodes: ViewModel.Tracks);
        }
        else if (_actions is ShellPage shell && _request is not null)
            shell.OpenMusicContainer(card.Item, _request.MusicRootId ?? _request.ParentId);
    }
    private void OnContainerChanged(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not CardItem card) return;
        if (args.InRecycleQueue) card.ReleasePoster(args.ItemContainer, keepWaiting: false);
        else { card.ClaimCard(args.ItemContainer); _ = card.EnsurePosterAsync(); }
    }
}
