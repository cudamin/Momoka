using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class LibraryPage
{
    private bool _selecting;
    private bool _batchBusy;
    private bool _syncingSelection;
    private int _selectionGeneration;
    private int _confirmationGeneration;
    private CancellationToken _selectionQuery;
    private CancellationTokenSource? _batchCancellation;
    private readonly HashSet<CardItem> _selectionCards = [];

    private void InitializeSelection()
    {
        CardWall.SelectionChanged += OnBatchSelectionChanged;
        ViewModel.Cards.CollectionChanged += OnBatchCardsChanged;
        LibraryBatchSurface.SizeChanged += (_, _) => PlaceBatchEmptyNotice();
        PreviewKeyDown += OnSelectionKeyDown;
    }

    private bool CanSelectItems => !_released && _request?.IsSearch != true
        && ViewModel.GridVisibility == Visibility.Visible
        && ViewModel.SourceServerKind is MediaServerKind.Emby or MediaServerKind.Jellyfin or MediaServerKind.Plex
        && ViewModel.Cards.Any(CanSelectCard);

    private bool CanSelectCard(CardItem card) => !card.IsNavigation && card.Scope is { IsAvailable: true } source
        && ItemBatch.Commands.Any(command => ItemBatch.Allowed(command, source,
            new(card.Item, card.CollectionMembership), _actions?.MenuPermissionsFor(source) ?? default));

    private void RefreshSelectionAvailability()
    {
        if (_selecting && (!ViewModel.QueryCurrent(_selectionQuery)
            || !CanSelectItems && !_batchBusy && LastBatchResult is null)) EndSelection();
        LibrarySelectButton.Visibility = CanSelectItems && !_selecting ? Visibility.Visible : Visibility.Collapsed;
        if (_selecting) RefreshBatchControls();
    }

    private void OnBeginSelection(object sender, RoutedEventArgs e) => BeginSelection();

    private void BeginSelection()
    {
        if (_selecting || !CanSelectItems) return;
        _selecting = true;
        _selectionGeneration++;
        _selectionQuery = ViewModel.QueryToken;
        ViewModel.SelectingItems = true;
        LibraryBatchResult.IsOpen = false;
        LastBatchResult = null;
        CardWall.IsItemInvokedEnabled = false;
        CardWall.SelectionMode = ItemsViewSelectionMode.Multiple;
        LibraryBatchSurface.Visibility = Visibility.Visible;
        ApplySelectingToCards();
        RefreshSelectionAvailability();
    }

    private void ApplySelectingToCards()
    {
        // Retain the cards until exit so recycled/removed containers never keep a stale mode.
        foreach (var card in ViewModel.Cards)
        {
            _selectionCards.Add(card);
            card.IsSelecting = true;
        }
    }

    private void OnBatchCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selecting)
        {
            if (!ViewModel.QueryCurrent(_selectionQuery)) EndSelection();
            else ApplySelectingToCards();
        }
        // ObservableCollection invokes this handler before ItemsView has adjusted its native
        // selection indices. Reading SelectedItems here can dereference an already removed row.
        if (!_attaching) QueueSelectionRefresh();
    }

    private void OnEndSelection(object sender, RoutedEventArgs e) => EndSelection();

    private void EndSelection()
    {
        _batchCancellation?.Cancel();
        _batchCancellation = null;
        _batchBusy = false;
        _selecting = false;
        _selectionGeneration++;
        _confirmationGeneration++;
        ViewModel.SelectingItems = false;
        _syncingSelection = true;
        try
        {
            CardWall.DeselectAll();
            CardWall.SelectionMode = ItemsViewSelectionMode.None;
            CardWall.IsItemInvokedEnabled = true;
            CardWall.IsHitTestVisible = true;
            foreach (var card in _selectionCards) card.IsSelecting = false;
            _selectionCards.Clear();
        }
        finally { _syncingSelection = false; }
        LibraryBatchMenu.Hide();
        LibraryBatchSurface.Visibility = Visibility.Collapsed;
        PlaceBatchEmptyNotice();
        LibrarySelectButton.Visibility = CanSelectItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private CardItem[] SelectedBatchCards() => CardWall.SelectedItems.OfType<CardItem>().ToArray();

    private void OnBatchPermissionsChanged(object? sender, EmbySessionScope source) => QueueSelectionRefresh();
    private void OnBatchSourcesChanged(object? sender, EventArgs e) => QueueSelectionRefresh();

    private void QueueSelectionRefresh()
    {
        var visit = _navigation;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_released || visit != _navigation) return;
            RefreshSelectionAvailability();
            if (_selecting) OnBatchMenuOpening(this, EventArgs.Empty);
        });
    }

    private void OnBatchSelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        if (!_selecting || _syncingSelection || _batchBusy) return;
        var cards = SelectedBatchCards();
        var source = cards.FirstOrDefault()?.Scope;
        _syncingSelection = true;
        try
        {
            foreach (var card in cards)
                if (!CanSelectCard(card) || source is null || card.Scope is not { IsAvailable: true } owner
                    || !ReferenceEquals(owner.UserStateOwner, source.UserStateOwner))
                {
                    var index = ViewModel.Cards.IndexOf(card);
                    if (index >= 0) CardWall.Deselect(index);
                }
        }
        finally { _syncingSelection = false; }
        RefreshBatchControls();
    }

    private void RefreshBatchControls()
    {
        var count = SelectedBatchCards().Length;
        LibrarySelectionCount.Text = $"已选 {count} 项";
        LibrarySelectLoaded.IsEnabled = !_batchBusy;
        LibraryClearSelection.IsEnabled = !_batchBusy && count > 0;
        LibraryBatchActions.IsEnabled = !_batchBusy && count > 0;
        LibraryEndSelection.IsEnabled = !_batchBusy;
        LibraryCancelBatch.Visibility = _batchBusy ? Visibility.Visible : Visibility.Collapsed;
        CardWall.IsHitTestVisible = !_batchBusy;
        PlaceBatchEmptyNotice();
    }

    private void PlaceBatchEmptyNotice() =>
        LibraryEmptyPanel.Margin = new Thickness(24, 72 + (_selecting ? LibraryBatchSurface.ActualHeight : 0), 24, 0);

    private void OnSelectLoaded(object sender, RoutedEventArgs e)
    {
        if (!_selecting || _batchBusy) return;
        var source = SelectedBatchCards().FirstOrDefault()?.Scope ?? ViewModel.Cards.FirstOrDefault()?.Scope;
        for (var i = 0; i < ViewModel.Cards.Count; i++)
            if (CanSelectCard(ViewModel.Cards[i]) && ViewModel.Cards[i].Scope is { IsAvailable: true } owner && source is not null
                && ReferenceEquals(owner.UserStateOwner, source.UserStateOwner)) CardWall.Select(i);
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        if (!_batchBusy) CardWall.DeselectAll();
    }

    private void OnCancelBatch(object sender, RoutedEventArgs e) => _batchCancellation?.Cancel();

    private void OnSelectionKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_selecting || e.Key != Windows.System.VirtualKey.Escape) return;
        if (_batchBusy) _batchCancellation?.Cancel();
        else EndSelection();
        e.Handled = true;
    }

    private static string BatchLabel(ItemCommand command) => command switch
    {
        ItemCommand.MarkPlayed => "标记为已看",
        ItemCommand.MarkUnplayed => "标记为未看",
        ItemCommand.Favorite => "添加收藏",
        ItemCommand.Unfavorite => "取消收藏",
        ItemCommand.AddToCollection => "加入合集…",
        ItemCommand.RemoveFromCollection => "移出当前合集",
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private void OnBatchMenuOpening(object sender, object e)
    {
        LibraryBatchMenu.Items.Clear();
        foreach (var command in ProbeBatchCommands())
        {
            var item = new MenuFlyoutItem { Text = BatchLabel(command), Tag = command };
            AutomationProperties.SetAutomationId(item, "LibraryBatch" + command);
            item.Click += OnBatchCommand;
            LibraryBatchMenu.Items.Add(item);
        }
    }

    internal void ProbeBeginSelection() => BeginSelection();
    internal void ProbeSelectBatch(int index) { if (_selecting) CardWall.Select(index); }
    internal void ProbeCancelBatch() => _batchCancellation?.Cancel();
    internal void ProbeEndSelection() => EndSelection();

    internal ItemBatchResult? LastBatchResult { get; private set; }

    internal ItemCommand[] ProbeBatchCommands()
    {
        if (!_selecting || _actions is null) return [];
        var cards = SelectedBatchCards();
        if (cards.Length == 0 || cards[0].Scope is not { IsAvailable: true } source) return [];
        ViewModel.PrepareCollectionMembers();
        var permissions = _actions.MenuPermissionsFor(source);
        return ItemBatch.Commands.Where(command => cards.All(card =>
            ItemBatch.Allowed(command, source, new(card.Item, card.CollectionMembership), permissions))).ToArray();
    }

    private async void OnBatchCommand(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ItemCommand command } || _batchBusy || !_selecting) return;
        var cards = SelectedBatchCards();
        if (cards.Length == 0 || cards[0].Scope is not { IsAvailable: true } source) return;
        var query = _selectionQuery;
        var visit = _navigation;
        var generation = _selectionGeneration;
        var confirmation = ++_confirmationGeneration;
        string? collectionId = null;
        try
        {
            if (command == ItemCommand.AddToCollection)
            {
                var collections = await source.ExecuteAsync((client, token) => client.GetCollectionsAsync(token), query).ConfigureAwait(true);
                if (!_selecting || _released || visit != _navigation || generation != _selectionGeneration
                    || confirmation != _confirmationGeneration || !ViewModel.QueryCurrent(query)) return;
                var dialog = new CollectionDialog(cards[0].Item, collections, allowCreate: false)
                {
                    XamlRoot = XamlRoot,
                    Title = $"将 {cards.Length} 项加入合集"
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Chosen is not { } chosen) return;
                collectionId = chosen.Id;
            }
            else if (!await ConfirmDialog.ForLibrary(this)(BatchLabel(command),
                $"对所选 {cards.Length} 项执行“{BatchLabel(command)}”？", "确认")) return;
            if (!_selecting || _released || visit != _navigation || generation != _selectionGeneration
                || confirmation != _confirmationGeneration || !ViewModel.QueryCurrent(query)) return;
            await ExecuteBatchAsync(cards, command, collectionId, default).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_released || visit != _navigation) return;
            LibraryBatchResult.Message = error.Message;
            LibraryBatchResult.Severity = InfoBarSeverity.Error;
            LibraryBatchResult.IsOpen = true;
        }
    }

    internal Task ProbeBatchOperationAsync(ItemCommand command, CancellationToken token = default) =>
        ExecuteBatchAsync(SelectedBatchCards(), command, null, token);

    private async Task ExecuteBatchAsync(CardItem[] cards, ItemCommand command, string? collectionId, CancellationToken token)
    {
        if (!_selecting || _batchBusy || _released || cards.Length == 0 || _actions is not { } actions
            || cards[0].Scope is not { IsAvailable: true } source) return;
        if (!ViewModel.QueryCurrent(_selectionQuery) || cards.Any(card => !CardWall.SelectedItems.Contains(card))) return;
        _confirmationGeneration++;
        ViewModel.PrepareCollectionMembers();
        var entries = cards.Select(card => new ItemBatchEntry(card.Item, card.CollectionMembership)).ToArray();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _selectionQuery);
        _batchCancellation = cancellation;
        _batchBusy = true;
        if (command == ItemCommand.RemoveFromCollection) CardWall.DeselectAll();
        LibraryBatchResult.IsOpen = false;
        RefreshBatchControls();
        var visit = _navigation;
        var stateChanges = new Dictionary<CardItem, (ItemUserStateOrder Order, ItemUserStateOrder.Change Change)>();
        var watched = command is ItemCommand.MarkPlayed or ItemCommand.MarkUnplayed;
        if (watched || command is ItemCommand.Favorite or ItemCommand.Unfavorite)
            foreach (var card in cards)
            {
                var order = card.UserStateOrder;
                stateChanges.Add(card, (order, watched ? order.BeginWatched(command == ItemCommand.MarkPlayed)
                    : order.BeginFavorite(command == ItemCommand.Favorite)));
            }
        try
        {
            var result = await new ItemBatchService().ExecuteAsync(source, entries, command,
                () => actions.MenuPermissionsFor(source), collectionId, cancellation.Token, receipt =>
                {
                    if (receipt.Status != ItemBatchStatus.Succeeded) return;
                    var card = cards.First(candidate => ReferenceEquals(candidate.Item, receipt.Item));
                    var currentState = true;
                    if (stateChanges.TryGetValue(card, out var edit) && card.IsCurrent)
                    {
                        if (edit.Order.Apply(edit.Change, card.Item.UserData, receipt.UserData) is { } state)
                            card.ApplyUserState(state, watched);
                        else currentState = false;
                    }
                    if (_released || visit != _navigation || !ReferenceEquals(_batchCancellation, cancellation)) return;
                    var index = ViewModel.Cards.IndexOf(card);
                    if (index >= 0 && command != ItemCommand.RemoveFromCollection) CardWall.Deselect(index);
                    if (command == ItemCommand.MarkPlayed && currentState)
                    {
                        actions.ReportMarkedWatched(card.Item);
                        ViewModel.MarkedWatched(card);
                    }
                    else if (command is ItemCommand.Favorite or ItemCommand.Unfavorite) actions.RefreshFavorites(card.Item);
                    else if (command == ItemCommand.RemoveFromCollection)
                    {
                        ViewModel.ForgetCollectionMember(card, _selectionQuery);
                    }
                }, receipt =>
                {
                    var card = cards.First(candidate => ReferenceEquals(candidate.Item, receipt.Item));
                    if (card.IsCurrent && stateChanges.TryGetValue(card, out var edit) && receipt.RefreshedUserData is { } refreshed
                        && edit.Order.RefreshConfirmed(edit.Change, card.Item.UserData, refreshed) is { } state)
                        card.ApplyUserState(state, watched: false);
                }).ConfigureAwait(true);
            if (_released || visit != _navigation || !ReferenceEquals(_batchCancellation, cancellation)) return;
            LastBatchResult = result;
            if (command == ItemCommand.RemoveFromCollection)
            {
                CardWall.DeselectAll();
                foreach (var receipt in result.Results.Where(receipt => receipt.Status != ItemBatchStatus.Succeeded))
                {
                    var index = ViewModel.Cards.ToList().FindIndex(card => ReferenceEquals(card.Item, receipt.Item));
                    if (index >= 0) CardWall.Select(index);
                }
            }
            var details = result.Results.Where(receipt => receipt.Error is not null || receipt.RefreshError is not null)
                .Select(receipt => $"{receipt.Item.Name}：{receipt.Error ?? "已保存，但刷新失败：" + receipt.RefreshError}");
            LibraryBatchResult.Message = $"成功 {result.Succeeded} 项，失败 {result.Failed} 项，取消 {result.Canceled} 项。"
                + (details.Any() ? "\n" + string.Join("\n", details.Take(5)) : "");
            LibraryBatchResult.Severity = result.Failed > 0 ? InfoBarSeverity.Warning
                : result.Results.Any(receipt => receipt.RefreshError is not null) ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            LibraryBatchResult.IsOpen = true;
            if (command == ItemCommand.RemoveFromCollection && result.Succeeded > 0) PumpIfShort();
        }
        finally
        {
            foreach (var edit in stateChanges.Values) edit.Order.Complete(edit.Change);
            if (ReferenceEquals(_batchCancellation, cancellation))
            {
                _batchCancellation = null;
                _batchBusy = false;
                RefreshBatchControls();
            }
        }
    }
}
