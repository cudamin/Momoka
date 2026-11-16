using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Momoka.Shell.Views;

// XAML, viewport and realization boundaries; focus/retry/key method bodies are generated from production.
public sealed partial class ShelfFocusFixture
{
    private bool _live;
    private int _focusedIndex = -1;
    private int _pendingFocusedIndex = -1;
    private bool _focusRetryQueued;
    private bool _applyingFocusRetry;
    private int _focusGeneration;
    private int _focusScrollCorrelation = -1;
    private FrameworkElement? _focusReadyElement;
    private UIElement? _focusBringElement;
    internal XamlRoot? XamlRoot { get; set; } = new();
    internal bool Live => _live;
    private readonly WatchBoundary _watch = new();
    private readonly Button Prev = new();
    private readonly Button Next = new();
    internal ItemsRepeater Repeater { get; } = new();
    internal DispatcherQueue DispatcherQueue { get; } = new();
    internal ViewportBoundary Scroller { get; } = new();
    internal double Step { get; set; } = 400;
    internal int Pending => _pendingFocusedIndex;
    internal int Focused => _focusedIndex;
    internal bool Queued => _focusRetryQueued;
    internal int ScrollCorrelation => _focusScrollCorrelation;
    internal List<UIElement> Revealed { get; } = [];
    internal List<object?> ArrowStops { get; } = [];

    internal void Load() => OnLoaded(this, new());
    internal void Unload()
    {
        XamlRoot = null;
        OnUnloaded(this, new());
    }
    internal void ReparentUnloaded() => OnUnloaded(this, new());
    internal void FocusAt(int index) => FocusItem(index);
    internal void Layout() => ApplyPendingFocusedItem();
    internal void CompleteScroll(int correlation)
    {
        if (correlation == _focusScrollCorrelation) Scroller.ScrollSettled = true;
        OnFocusScrollCompleted(new(), new(correlation));
    }
    internal void BringFromElement(int correlation)
    {
        OnFocusBringingIntoView(new(), new(correlation) { RequestEventArgs = new() { TargetElement = _focusBringElement } });
        var args = new BringIntoViewRequestedEventArgs();
        OnFocusBringRequested(new UIElement(), args);
        BringHandled = args.Handled;
    }
    internal bool BringHandled { get; private set; }
    internal void TakeOtherFocus(int index)
    {
        var element = Repeater.TryGetElement(index) ?? Repeater.GetOrCreateElement(index);
        OnGotFocus(this, new() { OriginalSource = element });
    }
    internal void ReplaceItems(int count)
    {
        Repeater.ItemsSourceView = new(count);
        _focusedIndex = -1;
        _pendingFocusedIndex = -1;
        _focusRetryQueued = false;
        _focusScrollCorrelation = -1;
        ClearFocusReadyElement();
        _focusBringElement = null;
        _focusGeneration++;
    }
    internal bool Key(VirtualKey key)
    {
        var args = new KeyRoutedEventArgs(key);
        OnKeyDown(this, args);
        return args.Handled;
    }
    internal bool Direction(FocusNavigationDirection direction)
    {
        var args = new NoFocusCandidateFoundEventArgs(direction);
        OnNoFocusCandidateFound(Repeater, args);
        return args.Handled;
    }
    private double Measure() => 200;
    internal void AssumeScrollSettled() => Scroller.ScrollSettled = true;
    private int BringFocusedElementIntoView(UIElement element)
    {
        Revealed.Add(element);
        return Scroller.ScrollSettled ? -1 : Scroller.BeginRequest();
    }
    private void SetArrowAnimation(Button button, object? value) => ArrowStops.Add(value);

    internal sealed class ViewportBoundary
    {
        internal double HorizontalOffset { get; set; }
        internal double ScrollableWidth { get; set; } = 1800;
        internal bool ReuseCurrentRequest { get; set; }
        internal bool ScrollSettled { get; set; }
        internal int LastRequest { get; private set; }
        internal int BeginRequest() => ReuseCurrentRequest && LastRequest > 0 ? LastRequest : ++LastRequest;
    }
    private sealed class WatchBoundary
    {
        internal void Leave() { }
    }
}
