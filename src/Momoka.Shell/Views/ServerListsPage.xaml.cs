using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Emby;
using Momoka.Shell.Services;
using Momoka.Shell.ViewModels;
using ListKind = Momoka.Shell.Services.ServerListKind;

namespace Momoka.Shell.Views;

internal sealed record ServerListsRequest(IServiceProvider Services, EmbySessionScope? Source = null, EmbyItem? Item = null)
{
    internal const string Tag = "server-lists";
}

public sealed partial class ServerListsPage : Page, IShellContent
{
    private ServerListsRequest? _request;
    private IShellActions? _actions;
    private bool _seeding;
    private bool _dialogOpen;
    private CancellationTokenSource? _dialogLifetime;
    private long _visit;
    public ServerListsViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;
    public ServerListsPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is not ServerListsRequest request) return;
        _request = request;
        var visit = ++_visit;
        _actions = request.Services.GetRequiredService<IShellActions>();
        _seeding = true;
        ViewModel.Attach(request.Services.GetRequiredService<EmbySession>(), request.Services.GetRequiredService<ServerListsService>(),
            action => DispatcherQueue.TryEnqueue(() => action()), request.Source);
        ServerListAccount.SelectedItem = ViewModel.Accounts.FirstOrDefault(account => account.Scope.Key == ViewModel.Scope?.Key);
        ServerListKind.SelectedIndex = 0;
        _seeding = false;
        if (request.Item is { } item) await ViewModel.OpenItemAsync(item);
        else await ViewModel.ReloadAsync();
        if (visit == _visit) SeedLibrary();
    }

    private void SeedLibrary()
    {
        _seeding = true;
        ServerListLibrary.SelectedItem = ViewModel.Library;
        _seeding = false;
    }
    private async void OnAccountChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_seeding || ServerListAccount.SelectedItem is not ServerListAccount account) return;
        var visit = _visit;
        await ViewModel.SelectAccountAsync(account);
        if (visit != _visit) return;
        _seeding = true;
        ServerListKind.SelectedIndex = 0;
        _seeding = false;
        SeedLibrary();
    }
    private async void OnKindChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_seeding) return;
        var visit = _visit;
        await ViewModel.SelectKindAsync(ServerListKind.SelectedIndex == 1 ? ListKind.Collection : ListKind.Playlist);
        if (visit == _visit) SeedLibrary();
    }
    private async void OnLibraryChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_seeding) await ViewModel.SelectLibraryAsync(ServerListLibrary.SelectedItem as ServerListLibrary);
    }
    private async void OnListClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is ServerList list) await ViewModel.OpenAsync(list);
    }
    private async void OnRefresh(object sender, RoutedEventArgs args)
    {
        var visit = _visit;
        await ViewModel.ReloadAsync();
        if (visit == _visit) SeedLibrary();
    }
    private async void OnBack(object sender, RoutedEventArgs args) => await ViewModel.BackAsync();
    private async void OnMoveUp(object sender, RoutedEventArgs args) => await ViewModel.MoveAsync(-1);
    private async void OnMoveDown(object sender, RoutedEventArgs args) => await ViewModel.MoveAsync(1);
    private void OnOpen(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Available && ViewModel.SelectedMember is { } member) _actions?.OpenItem(member.Item);
    }
    private async void OnPlay(object sender, RoutedEventArgs args)
    {
        if (_actions is not null && ViewModel.Available && ViewModel.SelectedMember is { Item.IsPlayable: true } member)
            await _actions.PlayAsync(member.Item);
    }

    private async void OnCreate(object sender, RoutedEventArgs args) => await EditAsync(create: true);
    private async void OnAdd(object sender, RoutedEventArgs args) => await EditAsync(create: false);
    private async Task EditAsync(bool create)
    {
        if (_dialogOpen || _request is null || ViewModel.Scope is not { IsAvailable: true } scope
            || create && !ViewModel.CanCreate || !create && !ViewModel.Editable) return;
        _dialogOpen = true;
        var visit = _visit;
        var revision = scope.PermissionRevision;
        var target = ViewModel.OpenedList;
        var selectedKind = ViewModel.Kind;
        var selectedLibrary = ViewModel.Library;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(scope.Lifetime);
        _dialogLifetime = lifetime;
        try
        {
            var service = _request.Services.GetRequiredService<ServerListsService>();
            var kind = create ? ViewModel.Kind : ViewModel.OpenedList!.Kind;
            var library = create ? ViewModel.Library?.Item : ViewModel.OpenedList is { Kind: ListKind.Collection } list
                ? new EmbyItem { Id = list.SectionId ?? "", CollectionType = list.ItemType == EmbyItemType.Series ? "tvshows" : "movies" }
                : null;
            var result = await ServerListDialogs.EditAsync(this, service, scope, kind, library, create, lifetime.Token);
            if (result is null || lifetime.IsCancellationRequested || visit != _visit
                || !ReferenceEquals(scope.UserStateOwner, ViewModel.Scope?.UserStateOwner) || scope.PermissionRevision != revision
                || !ReferenceEquals(target, ViewModel.OpenedList) || selectedKind != ViewModel.Kind || selectedLibrary != ViewModel.Library) return;
            if (create) await ViewModel.CreateAsync(result.Name, result.Items);
            else await ViewModel.AddAsync(result.Items);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!lifetime.IsCancellationRequested) _actions?.Notify(error.Message, InfoBarSeverity.Error); }
        finally { _dialogOpen = false; if (ReferenceEquals(_dialogLifetime, lifetime)) _dialogLifetime = null; }
    }
    private async void OnRemove(object sender, RoutedEventArgs args)
    {
        if (_dialogOpen || !ViewModel.Editable || ViewModel.SelectedMember is not { } member) return;
        _dialogOpen = true;
        var visit = _visit;
        var target = ViewModel.OpenedList;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ViewModel.Scope!.Lifetime);
        _dialogLifetime = lifetime;
        try
        {
            if (await ConfirmDialog.ForLibrary(this, async dialog =>
            {
                using var cancel = LibraryBatchDialogs.CancelDialog(this, dialog, lifetime.Token);
                return await dialog.ShowAsync();
            })("移出清单", $"将「{member.Name}」移出此清单？媒体文件会保留。", "移出")
                && !lifetime.IsCancellationRequested && visit == _visit && ReferenceEquals(target, ViewModel.OpenedList)
                && ReferenceEquals(ViewModel.SelectedMember, member)) await ViewModel.RemoveAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!lifetime.IsCancellationRequested) _actions?.Notify(error.Message, InfoBarSeverity.Error); }
        finally { _dialogOpen = false; if (ReferenceEquals(_dialogLifetime, lifetime)) _dialogLifetime = null; }
    }
    public void Release() { ++_visit; _dialogLifetime?.Cancel(); ViewModel.Cancel(); }
    protected override void OnNavigatedFrom(NavigationEventArgs args) { Release(); base.OnNavigatedFrom(args); }
}
