using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private readonly SessionPlaybackQueue _sessionQueue = new();

    internal event EventHandler? QueueChanged;
    internal event Action<bool>? QueueRequested;

    internal sealed record QueueArrangement(long Intent, int Attempt, EmbyItem Item, EmbySessionScope Scope);

    internal EmbySessionScope? CurrentQueueScope => _playbackScope;

    internal bool CanArrangeQueue => _nowPlaying is { } item && CanQueue(item);

    internal QueueArrangement? CaptureQueueArrangement() => CanArrangeQueue
        ? new(_activeIntent, _playbackAttempt, _nowPlaying!, _playbackScope!) : null;

    internal bool IsCurrentQueueArrangement(QueueArrangement arrangement) => CanArrangeQueue
        && arrangement.Intent == _activeIntent && arrangement.Attempt == _playbackAttempt
        && ReferenceEquals(arrangement.Item, _nowPlaying) && arrangement.Scope.IsAvailable
        && ReferenceEquals(arrangement.Scope.UserStateOwner, _playbackScope?.UserStateOwner);

    internal void RequestQueue(bool next)
    {
        if (CanArrangeQueue) QueueRequested?.Invoke(next);
    }

    internal async Task<ItemsResult> SearchQueueAsync(QueueArrangement arrangement, string term, int start, CancellationToken token)
    {
        if (!IsCurrentQueueArrangement(arrangement)) throw new OperationCanceledException("播放安排所属的本次播放已改变");
        var result = await arrangement.Scope.ExecuteAsync((client, ct) => client.GetItemsAsync(new ItemQuery
        {
            SearchTerm = term.Trim(),
            Recursive = true,
            IncludeItemTypes = [EmbyItemType.Movie, EmbyItemType.Episode, EmbyItemType.Video],
            StartIndex = start,
            Limit = 50
        }, ct), token).ConfigureAwait(true);
        if (!IsCurrentQueueArrangement(arrangement)) throw new OperationCanceledException("播放安排所属的本次播放已改变");
        return result;
    }

    internal bool ArrangeQueue(QueueArrangement arrangement, IReadOnlyList<EmbyItem> items, bool next)
    {
        if (!IsCurrentQueueArrangement(arrangement) || items.Count == 0
            || items.Any(item => !CanQueue(item) || item.Source is null
                || !ReferenceEquals(item.Source.UserStateOwner, arrangement.Scope.UserStateOwner))) return false;
        var ordered = next ? items.Reverse() : items;
        foreach (var item in ordered)
            if (!_sessionQueue.Add(item, arrangement.Scope, next)) return false;
        QueueChanged?.Invoke(this, EventArgs.Empty);
        Noticed?.Invoke(next ? $"已安排 {items.Count} 项接下来播放" : $"已追加 {items.Count} 项到本次队尾", InfoBarSeverity.Success);
        return true;
    }

    internal bool CanQueue(EmbyItem item) => !_lifetime.IsCancellationRequested && _playback.IsPlaying
        && _nowPlaying is not null && _playbackScope?.IsAvailable == true && _startIntent.IsCurrent(_activeIntent)
        && !_sessionQueue.HasReservation && SessionPlaybackQueue.Supports(item) && item.Source?.IsAvailable == true;

    internal bool HasPendingQueue => _sessionQueue.Peek() is not null;

    internal bool CanResumeQueue => !_lifetime.IsCancellationRequested && !_playback.IsPlaying
        && _playerHold == 0 && HasPendingQueue;

    internal IReadOnlyList<SessionPlaybackQueue.Entry> PendingQueue => _sessionQueue.Pending;

    internal bool Queue(EmbyItem item, bool next)
    {
        if (!CanQueue(item)) return false;
        var scope = _session.Capture(item);
        if (!_sessionQueue.Add(item, scope, next)) return false;
        QueueChanged?.Invoke(this, EventArgs.Empty);
        Noticed?.Invoke(next ? $"下一项播放：{item.ToPlaybackTitle()}" : $"已追加到本次队列：{item.ToPlaybackTitle()}",
            InfoBarSeverity.Success);
        return true;
    }

    internal void ClearQueue()
    {
        if (_sessionQueue.HasReservation && _nowPlaying is null)
        {
            _startIntent.CancelAll();
            _preparing?.Cancel();
        }
        _sessionQueue.Clear();
        if (Episodes is UserPlaybackQueue && _nowPlaying is { } item)
        {
            Episodes = new UserPlaybackQueue([item]) { CurrentIndex = 0 };
            EpisodeControlsVisible = item.Type == EmbyItemType.Episode && !string.IsNullOrEmpty(item.SeriesId);
            NoteEpisodeCount();
        }
        // A running file remains a valid new queue anchor after the user clears its pending items.
        if (_playback.IsPlaying) _sessionQueue.Begin();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    internal async Task ResumeQueueAsync()
    {
        if (!CanResumeQueue || _sessionQueue.Peek() is not { } entry || !_sessionQueue.Reserve(entry)) return;
        try
        {
            await StartPlaybackAsync(entry.Item, null, null, entry.Navigation, replaceExisting: false,
                identity: entry.Scope).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Noticed?.Invoke($"队列播放失败：{error.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            _sessionQueue.Release(entry);
        }
    }

    private SessionPlaybackQueue.Entry? QueuedAfterEnd(PlaybackResult result, long intent, bool newest)
    {
        if (!newest || result.Exit.Reason != PlaybackEndReason.EndOfFile || _lifetime.IsCancellationRequested
            || !_startIntent.IsCurrent(intent)) return null;
        return _sessionQueue.Peek(Settings.Playback.AutoPlayNextEpisode);
    }

    private async Task ContinueQueuedAsync(SessionPlaybackQueue.Entry entry, EmbyItem played, EmbySessionScope scope,
        long intent, PlaybackContinuation? continuation)
    {
        if (!_startIntent.IsCurrent(intent) || !_sessionQueue.Reserve(entry)) return;
        try
        {
            // The captured account owns this slot even when the user changes the default source.
            entry.Scope.ThrowIfUnavailable();
            await StartPlaybackAsync(entry.Item, ParentForFollowing(played, entry.Item, entry.Scope, scope),
                null, entry.Navigation, replaceExisting: true, identity: entry.Scope, continuingIntent: intent,
                continuationCancellation: continuation?.Lifetime ?? default, expectedContinuation: continuation)
                .ConfigureAwait(true);
        }
        finally
        {
            _sessionQueue.Release(entry);
        }
    }

    private void AdoptQueueStart(EmbyItem item, PendingPlayback pending)
    {
        if (!_sessionQueue.Confirm(pending.QueueEntry)) _sessionQueue.AdoptInitial(item, pending.Episodes);
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FinishQueueViewing()
    {
        // Failed preparation/start retains the unconfirmed head for an explicit retry. A completed
        // queue has nothing to restore and must not leak into a later manual viewing.
        if (_sessionQueue.Count == 0) _sessionQueue.Clear();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateQueueAvailability()
    {
        _sessionQueue.Peek();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }
}
