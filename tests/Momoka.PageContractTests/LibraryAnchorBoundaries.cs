namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
}

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low }
    public sealed class DispatcherQueue
    {
        public Queue<Action> Pending { get; } = new();
        public bool TryEnqueue(DispatcherQueuePriority _, Action callback) { Pending.Enqueue(callback); return true; }
        public void Drain()
        {
            var count = 0;
            while (Pending.TryDequeue(out var callback))
            {
                if (++count > 100) throw new InvalidOperationException("Anchor queue did not settle");
                callback();
            }
        }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class BringIntoViewOptions
    {
        public bool AnimationDesired { get; set; }
        public double HorizontalAlignmentRatio { get; set; }
        public double VerticalAlignmentRatio { get; set; }
    }
    public enum ScrollingAnimationMode { Disabled }
    public sealed class ScrollingScrollOptions
    {
        public ScrollingScrollOptions(ScrollingAnimationMode _) { }
    }
}

namespace Momoka.Shell.Views
{
    public sealed class LibraryRequest
    {
        public string? AnchorIdentityKey { get; set; }
        public int AnchorIndex { get; set; }
        public double AnchorY { get; set; }
    }
    public sealed class LibraryVmBoundary
    {
        private CancellationTokenSource _query = new();
        public List<CardItem> Cards { get; } = [];
        public bool WindowEligible => true;
        public bool HasSavedAnchor => Request.AnchorIdentityKey is not null;
        public int WindowStart { get; set; } = 1500;
        public int GridColumns => 4;
        public LibraryRequest Request { get; } = new();
        public CancellationToken QueryToken => _query.Token;
        public bool QueryCurrent(CancellationToken token) => token == _query.Token && !token.IsCancellationRequested;
        public void CaptureAnchor(int local, double y, int _)
        {
            Request.AnchorIdentityKey = Cards[local].Item.IdentityKey;
            Request.AnchorIndex = WindowStart + local;
            Request.AnchorY = y;
        }
        public void ChangeQuery() { _query.Cancel(); _query = new(); }
    }
    public sealed partial class LibraryPage
    {
        private bool _released;
        private int _navigation = 1;
        private Microsoft.UI.Xaml.Controls.ScrollView? _scroll = new();
        private readonly LibraryRequest? _request;
        private double? _pendingScroll;
        private Momoka.Diagnostics.NavigationTimingRun? _timing;
        public LibraryVmBoundary ViewModel { get; } = new();
        public Microsoft.UI.Xaml.Controls.ItemsView CardWall { get; } = new();
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public int AlphaSyncs { get; private set; }
        public LibraryPage() { _request = ViewModel.Request; _released = false; _pendingScroll = null; _timing = null; }
        private void SyncAlphaFromScroll() => AlphaSyncs++;
        private static IEnumerable<Microsoft.UI.Xaml.DependencyObject> DescendantsOf(Microsoft.UI.Xaml.DependencyObject node)
        {
            yield return node;
            if (node is not Microsoft.UI.Xaml.FrameworkElement element) yield break;
            foreach (var child in element.Children)
                foreach (var descendant in DescendantsOf(child)) yield return descendant;
        }
    }
}
