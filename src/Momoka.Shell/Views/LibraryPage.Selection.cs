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
    private CancellationTokenSource? _batchConfirmation;
    private readonly HashSet<CardItem> _selectionCards = [];
    // 批量工具栏进出只动不透明度与位移；释放走立即收起，不让 140ms 淡出拖住离页。
    private SurfaceMotion? _batchSurfaceMotion;

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
        if (_selecting) RefreshBatchControls();
    }

    private Func<CardItem, Action?> SelectionCallback()
    {
        var visit = _navigation;
        var query = ViewModel.QueryToken;
        return card =>
        {
            if (_released || _selecting || visit != _navigation || !ViewModel.QueryCurrent(query)
                || !CanSelectItems || !CanSelectCard(card)) return null;
            var index = ViewModel.Cards.IndexOf(card);
            if (index < 0) return null;
            return () =>
            {
                if (_released || visit != _navigation || !ViewModel.QueryCurrent(query)
                    || !CanSelectItems || !CanSelectCard(card)) return;
                var currentIndex = ViewModel.Cards.IndexOf(card);
                if (currentIndex < 0) return;
                BeginSelection();
                if (_selecting) CardWall.Select(currentIndex);
            };
        };
    }

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
        (_batchSurfaceMotion ??= new SurfaceMotion(LibraryBatchSurface)).Enter(180, 10);
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

    private void EndSelection() => EndSelection(animated: true);

    /// <summary>页面释放用：不播淡出，立即收起工具栏，落定不依赖 140ms 的回调。</summary>
    private void EndSelectionImmediately() => EndSelection(animated: false);

    private void EndSelection(bool animated)
    {
        _batchConfirmation?.Cancel();
        _batchConfirmation = null;
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
        LibraryBatchCommandBar.IsOpen = false;
        if (animated)
        {
            // 先淡出再收起；重复退出或中途重新进入会由 SurfaceMotion 打断，旧的落定回调不会覆盖新状态。
            (_batchSurfaceMotion ??= new SurfaceMotion(LibraryBatchSurface)).Exit(140, 8, () =>
            {
                LibraryBatchSurface.Visibility = Visibility.Collapsed;
                PlaceBatchEmptyNotice();
            });
            return;
        }
        _batchSurfaceMotion?.Collapse();
        LibraryBatchSurface.Visibility = Visibility.Collapsed;
        PlaceBatchEmptyNotice();
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
        // Keep available commands current with selection; opening still rechecks the
        // current source and permissions before a native menu item can be invoked.
        OnBatchMenuOpening(this, EventArgs.Empty);
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

    private static string BatchLabel(ItemCommand command) => command switch
    {
        ItemCommand.Play => "立即播放",
        ItemCommand.PlayFromBeginning => "从头播放",
        ItemCommand.Shuffle => "随机播放",
        ItemCommand.MarkPlayed => "标记为已看",
        ItemCommand.MarkUnplayed => "标记为未看",
        ItemCommand.Favorite => "添加收藏",
        ItemCommand.Unfavorite => "取消收藏",
        ItemCommand.AddToCollection => "加入合集…",
        ItemCommand.AddToPlaylist => "加入播放列表…",
        ItemCommand.Convert => "转换…",
        ItemCommand.Delete => "删除…",
        ItemCommand.ResetMetadata => "移除识别…",
        ItemCommand.RefreshMetadata => "刷新元数据…",
        ItemCommand.ScanLibraryFiles => "扫描媒体库文件",
        ItemCommand.RemoveFromCollection => "移出当前合集",
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private void OnBatchMenuOpening(object sender, object e)
    {
        LibraryBatchMenu.Items.Clear();
        var commands = ProbeBatchCommands();
        if (commands.Contains(ItemCommand.Play))
        {
            var play = new MenuFlyoutSubItem { Text = "播放", Icon = new FontIcon { Glyph = "\uE768" } };
            AutomationProperties.SetAutomationId(play, "LibraryBatchPlayback");
            play.Items.Add(Entry(ItemCommand.Play));
            if (commands.Contains(ItemCommand.PlayFromBeginning)) play.Items.Add(Entry(ItemCommand.PlayFromBeginning));
            LibraryBatchMenu.Items.Add(play);
        }
        foreach (var command in commands.Where(command => command is not (ItemCommand.Play or ItemCommand.PlayFromBeginning)))
            LibraryBatchMenu.Items.Add(Entry(command));

        MenuFlyoutItem Entry(ItemCommand command)
        {
            var item = new MenuFlyoutItem { Text = BatchLabel(command), Tag = command, Icon = new FontIcon { Glyph = BatchGlyph(command) } };
            AutomationProperties.SetAutomationId(item, "LibraryBatch" + command);
            item.Click += OnBatchCommand;
            return item;
        }
    }

    private static string BatchGlyph(ItemCommand command) => command switch
    {
        ItemCommand.Play or ItemCommand.PlayFromBeginning => "\uE768",
        ItemCommand.Shuffle => "\uE8B1",
        ItemCommand.AddToCollection or ItemCommand.AddToPlaylist => "\uE8F4",
        ItemCommand.Favorite or ItemCommand.Unfavorite => "\uEB52",
        ItemCommand.MarkPlayed or ItemCommand.MarkUnplayed => "\uE73E",
        ItemCommand.Delete => "\uE74D",
        ItemCommand.ResetMetadata => "\uE8E4",
        _ => "\uE72C"
    };

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
        _batchConfirmation?.Cancel();
        using var confirmationCancellation = CancellationTokenSource.CreateLinkedTokenSource(query, source.Lifetime);
        _batchConfirmation = confirmationCancellation;
        var confirmationToken = confirmationCancellation.Token;
        string? collectionId = null;
        ItemBatchOptions? options = null;
        var executing = false;
        bool Current() => _selecting && !_released && visit == _navigation && generation == _selectionGeneration
            && confirmation == _confirmationGeneration && ViewModel.QueryCurrent(query) && source.IsAvailable
            && cards.All(card => CardWall.SelectedItems.Contains(card)
                && ItemBatch.Allowed(command, source, new(card.Item, card.CollectionMembership), _actions!.MenuPermissionsFor(source)));
        try
        {
            if (!Current()) return;
            if (command is ItemCommand.Play or ItemCommand.PlayFromBeginning or ItemCommand.Shuffle)
            {
                await PlaySelectedAsync(cards, command == ItemCommand.Shuffle, command == ItemCommand.PlayFromBeginning);
                return;
            }
            if (command == ItemCommand.AddToCollection)
            {
                var collections = await source.ExecuteAsync((client, token) => client.GetCollectionsAsync(token), confirmationToken).ConfigureAwait(true);
                if (!_selecting || _released || visit != _navigation || generation != _selectionGeneration
                    || confirmation != _confirmationGeneration || !ViewModel.QueryCurrent(query)) return;
                var dialog = new CollectionDialog(cards[0].Item, collections, allowCreate: false)
                {
                    XamlRoot = XamlRoot,
                    Title = $"将 {cards.Length} 项加入合集"
                };
                using var close = LibraryBatchDialogs.CancelDialog(this, dialog, confirmationToken);
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Chosen is not { } chosen) return;
                collectionId = chosen.Id;
            }
            else if (command == ItemCommand.AddToPlaylist)
            {
                var playlists = await source.ExecuteAsync((client, token) => client.GetEditableVideoPlaylistsAsync(token), confirmationToken).ConfigureAwait(true);
                if (!Current()) return;
                options = await LibraryBatchDialogs.PlaylistAsync(this, cards.Length, playlists, confirmationToken);
                if (options is null) return;
            }
            else if (command == ItemCommand.Convert)
            {
                var ids = cards.Select(card => card.Item.Id).ToArray();
                var available = await source.ExecuteAsync((client, token) => client.GetConversionOptionsAsync(ids, null, token), confirmationToken).ConfigureAwait(true);
                if (!Current()) return;
                var conversion = await LibraryBatchDialogs.ConvertAsync(this, cards.Length, available,
                    (target, token) => source.ExecuteAsync((client, ct) => client.GetConversionOptionsAsync(ids, target, ct), token), confirmationToken);
                if (conversion is null) return;
                options = new(Conversion: conversion);
            }
            else if (command == ItemCommand.RefreshMetadata)
            {
                var dialog = new RefreshMetadataDialog { XamlRoot = XamlRoot, Title = $"刷新所选 {cards.Length} 项的元数据" };
                using var close = LibraryBatchDialogs.CancelDialog(this, dialog, confirmationToken);
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                options = new(RefreshMode: dialog.Mode, ReplaceImages: dialog.ReplaceImages, ReplaceThumbnails: dialog.ReplaceThumbnails);
            }
            else
            {
                var names = string.Join("\n", cards.Take(8).Select(card => "• " + card.Item.Name));
                var warning = command switch
                {
                    ItemCommand.Delete => "这些媒体及其磁盘文件将被永久删除；剧集、季或目录包含的内容也会删除，无法撤销。",
                    ItemCommand.ResetMetadata => "将重置所选媒体的识别信息，手动编辑的片名、简介和分级等元数据也会丢失，无法撤销。媒体文件会保留。",
                    ItemCommand.ScanLibraryFiles => "将扫描所选目录中的媒体文件，由服务器在后台处理。",
                    _ => $"对所选 {cards.Length} 项执行“{BatchLabel(command)}”？"
                };
                if (!await ConfirmDialog.ForLibrary(this, async dialog =>
                    {
                        using var close = LibraryBatchDialogs.CancelDialog(this, dialog, confirmationToken);
                        return confirmationToken.IsCancellationRequested ? ContentDialogResult.None : await dialog.ShowAsync();
                    })(BatchLabel(command), warning + "\n\n" + names,
                    command == ItemCommand.Delete ? "永久删除" : "确认")) return;
            }
            if (!Current()) return;
            executing = true;
            await ExecuteBatchAsync(cards, command, collectionId, default, options).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_released || visit != _navigation || generation != _selectionGeneration || !ViewModel.QueryCurrent(query)
                || !executing && confirmation != _confirmationGeneration) return;
            LibraryBatchResult.Message = error.Message;
            LibraryBatchResult.Severity = InfoBarSeverity.Error;
            LibraryBatchResult.IsOpen = true;
        }
        finally
        {
            if (ReferenceEquals(_batchConfirmation, confirmationCancellation)) _batchConfirmation = null;
        }
    }

    internal Task ProbeBatchOperationAsync(ItemCommand command, CancellationToken token = default) =>
        ExecuteBatchAsync(SelectedBatchCards(), command, null, token);

    private async Task ExecuteBatchAsync(CardItem[] cards, ItemCommand command, string? collectionId, CancellationToken token,
        ItemBatchOptions? options = null)
    {
        if (!_selecting || _batchBusy || _released || cards.Length == 0 || _actions is not { } actions
            || cards[0].Scope is not { IsAvailable: true } source) return;
        if (!ViewModel.QueryCurrent(_selectionQuery) || cards.Any(card => !CardWall.SelectedItems.Contains(card))) return;
        _confirmationGeneration++;
        ViewModel.PrepareCollectionMembers();
        var entries = cards.Select(card => new ItemBatchEntry(card.Item, card.CollectionMembership)).ToArray();
        var deletionAnchor = command == ItemCommand.Delete ? ReadScrollAnchor() : null;
        var deletionOffset = command == ItemCommand.Delete ? ContentScrollOffset : 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _selectionQuery);
        _batchCancellation = cancellation;
        _batchBusy = true;
        if (command is ItemCommand.RemoveFromCollection or ItemCommand.Delete) CardWall.DeselectAll();
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
                    if (command == ItemCommand.Delete) ViewModel.ConfirmBatchDeletion(receipt.Item);
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
                    if (index >= 0 && command is not (ItemCommand.RemoveFromCollection or ItemCommand.Delete)) CardWall.Deselect(index);
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
                }, options).ConfigureAwait(true);
            if (_released || visit != _navigation || !ReferenceEquals(_batchCancellation, cancellation)) return;
            LastBatchResult = result;
            if (command == ItemCommand.Delete)
            {
                var deleted = result.Results.Where(receipt => receipt.Status == ItemBatchStatus.Succeeded).Select(receipt => receipt.Item).ToArray();
                if (deleted.Length > 0)
                {
                    _removingCard = true;
                    string? refreshError = null;
                    try
                    {
                        if (!await ViewModel.RemoveBatchAsync(deleted, _selectionQuery).ConfigureAwait(true))
                            refreshError = "媒体已删除，刷新列表失败，请稍后刷新。";
                    }
                    catch (Exception error) { refreshError = error.Message; }
                    finally { _removingCard = false; }
                    if (_released || visit != _navigation || !ViewModel.QueryCurrent(_selectionQuery)) return;
                    if (refreshError is not null)
                    {
                        // A failed UI refresh cannot turn an accepted deletion into a retryable write.
                        result = new(result.Results.Select(item => item.Status == ItemBatchStatus.Succeeded
                            ? item with { RefreshError = refreshError } : item).ToArray());
                        LastBatchResult = result;
                    }
                    if (deletionAnchor is not null) { _pendingAnchor = deletionAnchor; ApplyPendingScroll(); }
                    else
                    {
                        CardWall.UpdateLayout();
                        _scroll?.ScrollTo(0, deletionOffset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                    }
                }
                actions.RefreshFavorites();
            }
            if (command is ItemCommand.RemoveFromCollection or ItemCommand.Delete)
            {
                CardWall.DeselectAll();
                foreach (var receipt in result.Results.Where(receipt => receipt.Status != ItemBatchStatus.Succeeded))
                {
                    var index = ViewModel.Cards.ToList().FindIndex(card => card.Item.IdentityKey == receipt.Item.IdentityKey);
                    if (index >= 0) CardWall.Select(index);
                }
            }
            var details = result.Results.Where(receipt => receipt.Error is not null || receipt.RefreshError is not null)
                .Select(receipt => $"{receipt.Item.Name}：{receipt.Error ?? "已保存，但刷新失败：" + receipt.RefreshError}");
            var successLabel = command == ItemCommand.Convert ? "已提交转换任务" : command is ItemCommand.RefreshMetadata
                or ItemCommand.ScanLibraryFiles or ItemCommand.ResetMetadata ? "已提交" : "成功";
            LibraryBatchResult.Message = $"{successLabel} {result.Succeeded} 项，失败 {result.Failed} 项，取消 {result.Canceled} 项。"
                + (details.Any() ? "\n" + string.Join("\n", details.Take(5)) : "");
            LibraryBatchResult.Severity = result.Failed > 0 ? InfoBarSeverity.Warning
                : result.Results.Any(receipt => receipt.RefreshError is not null) ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            LibraryBatchResult.IsOpen = true;
            if (command is ItemCommand.RemoveFromCollection or ItemCommand.Delete && result.Succeeded > 0) PumpIfShort();
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
