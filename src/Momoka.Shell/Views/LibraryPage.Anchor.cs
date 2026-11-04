using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class LibraryPage
{
    private sealed record ScrollAnchor(string IdentityKey, int AbsoluteIndex, double Y);
    private ScrollAnchor? _pendingAnchor;
    private bool _restoringAnchor;
    private int _anchorVersion;
    private int _anchorAttempts;

    private bool AnchorContinuationCurrent(int version, int navigation, CancellationToken query)
    {
        if (version != _anchorVersion) return false;
        if (!_released && navigation == _navigation && ViewModel.QueryCurrent(query)) return true;
        _pendingAnchor = null;
        _restoringAnchor = false;
        _anchorAttempts = 0;
        return false;
    }

    private ScrollAnchor? SavedScrollAnchor() => ViewModel.HasSavedAnchor && _request is { } request
        ? new(request.AnchorIdentityKey!, request.AnchorIndex, request.AnchorY) : null;

    /// <summary>Use the real layout's nearest item, then its live visual position relative to the viewport.</summary>
    private ScrollAnchor? ReadScrollAnchor(bool allowReleased = false)
    {
        if (_released && !allowReleased || !ViewModel.WindowEligible || _scroll is null || ViewModel.Cards.Count == 0
            || !CardWall.TryGetItemIndex(0, 0, out var index) || index < 0 || index >= ViewModel.Cards.Count) return null;
        var card = ViewModel.Cards[index];
        var container = RealizedAnchor(card.Item.IdentityKey);
        if (container is null) return null;
        var y = container.TransformToVisual(_scroll).TransformPoint(new Point(0, 0)).Y;
        return double.IsFinite(y) ? new(card.Item.IdentityKey, ViewModel.WindowStart + index, y) : null;
    }

    private FrameworkElement? RealizedAnchor(string identityKey)
    {
        foreach (var node in DescendantsOf(CardWall).OfType<FrameworkElement>())
        {
            var card = node switch
            {
                PosterCard poster => poster.Card,
                EpisodeRow row => row.Card,
                _ => node.DataContext as CardItem
            };
            if (card?.Item.IdentityKey == identityKey && node.ActualHeight > 0) return node;
        }
        return null;
    }

    private void RememberScrollAnchor(bool allowReleased = false)
    {
        if (ReadScrollAnchor(allowReleased) is not { } anchor) return;
        ViewModel.CaptureAnchor(anchor.AbsoluteIndex - ViewModel.WindowStart, anchor.Y, ViewModel.GridColumns);
    }

    private void BeforeWindowChange()
    {
        if (_released) return;
        _pendingAnchor = ReadScrollAnchor() ?? SavedScrollAnchor();
        _restoringAnchor = _pendingAnchor is not null;
        _anchorAttempts = 0;
        if (_pendingAnchor is { } anchor)
            ViewModel.CaptureAnchor(anchor.AbsoluteIndex - ViewModel.WindowStart, anchor.Y, ViewModel.GridColumns);
    }

    private void AfterWindowChange()
    {
        if (_released) return;
        ApplyPendingScroll();
    }

    /// <summary>
    /// Bring the identity into the live layout before correcting the viewport by its measured Y.
    /// A changed column count or a prepended row therefore never reuses an old absolute scroll offset.
    /// </summary>
    private void RestorePendingAnchor()
    {
        if (_released || _pendingAnchor is not { } anchor || _scroll is null || ViewModel.Cards.Count == 0) return;
        var local = -1;
        for (var index = 0; index < ViewModel.Cards.Count; index++)
            if (ViewModel.Cards[index].Item.IdentityKey == anchor.IdentityKey) { local = index; break; }
        if (local < 0) local = Math.Clamp(anchor.AbsoluteIndex - ViewModel.WindowStart, 0, ViewModel.Cards.Count - 1);
        var identity = ViewModel.Cards[local].Item.IdentityKey;
        _restoringAnchor = true;
        var version = ++_anchorVersion;
        var navigation = _navigation;
        var token = ViewModel.QueryToken;
        CardWall.UpdateLayout();
        CardWall.StartBringItemIntoView(local, new BringIntoViewOptions
        {
            AnimationDesired = false,
            HorizontalAlignmentRatio = 0,
            VerticalAlignmentRatio = 0
        });
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!AnchorContinuationCurrent(version, navigation, token)) return;
            CardWall.UpdateLayout();
            if (RealizedAnchor(identity) is not { } element)
            {
                if (++_anchorAttempts < 4) { RestorePendingAnchor(); return; }
                _pendingAnchor = null;
                _restoringAnchor = false;
                return;
            }
            var y = element.TransformToVisual(_scroll).TransformPoint(new Point(0, 0)).Y;
            if (double.IsFinite(y))
                _scroll.ScrollTo(0, Math.Max(0, _scroll.VerticalOffset + y - anchor.Y),
                    new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (!AnchorContinuationCurrent(version, navigation, token)) return;
                _pendingAnchor = null;
                _restoringAnchor = false;
                _anchorAttempts = 0;
                RememberScrollAnchor();
                SyncAlphaFromScroll();
            });
        });
    }
}
