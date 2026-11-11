using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Shell.Views;

public sealed partial class LibraryPage
{
    /// <summary>Collect only the captured selection, then hand one explicit queue to the resident player.</summary>
    private async Task PlaySelectedAsync(CardItem[] cards, bool shuffle, bool fromBeginning)
    {
        if (!_selecting || _batchBusy || _released || cards.Length == 0 || _actions is not { } actions
            || cards[0].Scope is not { IsAvailable: true, Connection.ServerKind: MediaServerKind.Emby } source) return;
        var query = _selectionQuery;
        var visit = _navigation;
        var generation = _selectionGeneration;
        var selected = cards.Select(card => card.Item).ToArray();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(query, source.Lifetime);
        if (!SelectionCurrent()) return;
        _confirmationGeneration++;
        _batchCancellation = cancellation;
        _batchBusy = true;
        LibraryBatchResult.Message = "正在收集所选媒体…";
        LibraryBatchResult.Severity = InfoBarSeverity.Informational;
        LibraryBatchResult.IsOpen = true;
        RefreshBatchControls();
        EmbyItem[]? queue = null;
        try
        {
            var items = await ItemBatchPlayback.CollectAsync(source, selected,
                () => SelectionCurrent() && ReferenceEquals(_batchCancellation, cancellation), cancellation.Token).ConfigureAwait(true);
            if (!SelectionCurrent() || !ReferenceEquals(_batchCancellation, cancellation) || items.Count == 0) return;
            queue = items.ToArray();
            if (shuffle)
                for (var index = queue.Length - 1; index > 0; index--)
                {
                    var swap = Random.Shared.Next(index + 1);
                    (queue[index], queue[swap]) = (queue[swap], queue[index]);
                }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!SelectionCurrent()) return;
            LibraryBatchResult.Message = $"无法播放所选媒体：{error.Message}";
            LibraryBatchResult.Severity = InfoBarSeverity.Error;
            LibraryBatchResult.IsOpen = true;
        }
        finally
        {
            if (ReferenceEquals(_batchCancellation, cancellation))
            {
                _batchCancellation = null;
                _batchBusy = false;
                if (queue is null && LibraryBatchResult.Severity == InfoBarSeverity.Informational) LibraryBatchResult.IsOpen = false;
                if (!_released && visit == _navigation) RefreshBatchControls();
            }
        }
        if (queue is null || !SelectionCurrent()) return;
        // EndSelection cancels its batch token. Collection is finished and the player owns the
        // subsequent lifetime, so clear batch ownership before handing over the completed queue.
        EndSelection();
        LibraryBatchResult.IsOpen = false;
        var choice = fromBeginning ? new PlaybackChoice(null, null, null, false, 0) : null;
        try
        {
            await actions.PlayAsync(queue[0], choice: choice, episodes: new UserPlaybackQueue(queue)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            actions.Notify($"播放失败：{error.Message}", InfoBarSeverity.Error);
        }

        bool SelectionCurrent() => !_released && _selecting && visit == _navigation && generation == _selectionGeneration
            && !cancellation.IsCancellationRequested && ViewModel.QueryCurrent(query) && source.IsAvailable
            && cards.Select((card, index) => card.IsCurrent && ReferenceEquals(card.Item, selected[index])
                && ViewModel.Cards.Contains(card) && CardWall.SelectedItems.Contains(card)
                && card.Scope is { IsAvailable: true } owner && ReferenceEquals(owner.UserStateOwner, source.UserStateOwner)).All(current => current);
    }
}
