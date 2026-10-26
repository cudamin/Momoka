// Only XAML/UI APIs and the external page/VM owner are substitutes. Search.cs is linked whole.
namespace Windows.Foundation
{
    public readonly record struct Point(double X = 0, double Y = 0);
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum FocusState { Programmatic }

    public class DependencyObject
    {
        public List<DependencyObject> Children { get; } = [];
    }

    public class FrameworkElement : DependencyObject
    {
        public string Name { get; set; } = "";
        public double ActualHeight { get; set; } = 40;
        public double Top { get; set; }
        public Visibility Visibility { get; set; } = Visibility.Visible;
        public int LayoutCalls { get; private set; }
        public Action? Layout { get; set; }
        public void UpdateLayout() { LayoutCalls++; Layout?.Invoke(); }
        public Media.GeneralTransform TransformToVisual(FrameworkElement _) => new(Top);
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public sealed class GeneralTransform(double top)
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point point) => new(point.X, point.Y + top);
    }

    public static class VisualTreeHelper
    {
        public static int GetChildrenCount(Xaml.DependencyObject root) => root.Children.Count;
        public static Xaml.DependencyObject GetChild(Xaml.DependencyObject root, int index) => root.Children[index];
    }
}

namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low }
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _callbacks = [];
        public bool Accept { get; set; } = true;
        public int Count => _callbacks.Count;
        public bool TryEnqueue(DispatcherQueuePriority _, Action callback)
        {
            if (!Accept) return false;
            _callbacks.Enqueue(callback);
            return true;
        }
        public void RunOne() => _callbacks.Dequeue()();
        public void Drain() { while (_callbacks.Count > 0) RunOne(); }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Control : FrameworkElement
    {
        public bool IsTabStop { get; set; } = true;
        public int FocusCalls { get; private set; }
        public Action? Focused { get; set; }
        public bool Focus(FocusState _) { FocusCalls++; Focused?.Invoke(); return true; }
    }

    public sealed class ItemsControl : FrameworkElement
    {
        public object? ItemsSource { get; set; }
        public Dictionary<object, DependencyObject> Containers { get; } = [];
        public DependencyObject? ContainerFromItem(object? item) => item is not null && Containers.TryGetValue(item, out var value) ? value : null;
    }

    public sealed class AutoSuggestBox : Control
    {
        public bool IsSuggestionListOpen { get; set; }
    }
    public sealed class AutoSuggestBoxQuerySubmittedEventArgs
    {
        public object? ChosenSuggestion { get; init; }
    }

    public enum ScrollingAnimationMode { Disabled }
    public enum ScrollingSnapPointsMode { Ignore }
    public sealed record ScrollingScrollOptions(ScrollingAnimationMode AnimationMode, ScrollingSnapPointsMode SnapPointsMode);
    public sealed class ScrollingScrollCompletedEventArgs(int correlationId)
    {
        public int CorrelationId { get; } = correlationId;
    }
    public sealed class ScrollingBringingIntoViewEventArgs
    {
        public bool Cancel { get; set; }
    }
    public sealed record ScrollRequest(int CorrelationId, double X, double Y, ScrollingScrollOptions Options);

    public sealed class ScrollView : FrameworkElement
    {
        private Action<ScrollView, ScrollingScrollCompletedEventArgs>? _completed;
        private int _correlation;
        private Action<ScrollView, ScrollingBringingIntoViewEventArgs>? _bringing;
        public int BringSubscribers { get; private set; }
        public event Action<ScrollView, ScrollingBringingIntoViewEventArgs> BringingIntoView
        {
            add { _bringing += value; BringSubscribers++; }
            remove { _bringing -= value; BringSubscribers--; }
        }
        public bool RaiseBringIntoView()
        {
            var args = new ScrollingBringingIntoViewEventArgs();
            _bringing?.Invoke(this, args);
            return args.Cancel;
        }
        public double ExtentHeight { get; set; } = 2000;
        public int Subscribers { get; private set; }
        public List<ScrollRequest> Requests { get; } = [];
        public Action<ScrollRequest>? OnScroll { get; set; }
        public bool NoOperation { get; set; }
        public event Action<ScrollView, ScrollingScrollCompletedEventArgs> ScrollCompleted
        {
            add { _completed += value; Subscribers++; }
            remove { _completed -= value; Subscribers--; }
        }
        public int ScrollTo(double x, double y, ScrollingScrollOptions options)
        {
            var id = NoOperation ? -1 : ++_correlation;
            var request = new ScrollRequest(id, x, y, options);
            Requests.Add(request);
            OnScroll?.Invoke(request);
            return id;
        }
        public void Complete(int correlationId) => _completed?.Invoke(this, new(correlationId));
    }
}

namespace Momoka.Diagnostics
{
    public static class Log
    {
        public static List<Exception> Warnings { get; } = [];
        public static void Warn(string _, string __, Exception error) => Warnings.Add(error);
    }
}

namespace Momoka.Shell.ViewModels
{
    public sealed class SettingRow;
    public sealed class SettingSection
    {
        public List<SettingRow> Rows { get; } = [];
    }
    public sealed record SettingsSearchResult(string Label, string Location, string CategoryKey,
        SettingSection? Section = null, SettingRow? Row = null);

    // The VM owns acceptance/category selection. Tests supply the acceptance outcome, not its algorithm.
    public sealed class SettingsViewModel
    {
        public string SelectedCategory { get; set; } = "old";
        public List<SettingsSearchResult> SettingsSearchResults { get; } = [];
        public bool Accept { get; set; } = true;
        public int SelectionCalls { get; private set; }
        public Action? Selected { get; set; }
        public bool SelectSettingsSearchResult(SettingsSearchResult result)
        {
            SelectionCalls++;
            if (!Accept) return false;
            SelectedCategory = result.CategoryKey;
            Selected?.Invoke();
            return true;
        }
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class SettingsPage : IDisposable
    {
        private const string Category = "Settings";
        private readonly ActivityOwner _activity = new();
        private CancellationToken _activityToken;
        public ViewModels.SettingsViewModel ViewModel { get; } = new();
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public Microsoft.UI.Xaml.FrameworkElement SettingsContent { get; } = new();
        public Microsoft.UI.Xaml.FrameworkElement SettingsCardsContent { get; } = new() { ActualHeight = 2000 };
        public Microsoft.UI.Xaml.FrameworkElement SettingsSections { get; } = new();
        public Microsoft.UI.Xaml.Controls.ScrollView SettingsScroll { get; } = new() { ActualHeight = 400 };
        public bool Pending => HasPendingSearchNavigation;
        public bool HasPendingOwner => _pendingSearchNavigation is not null;
        public SettingsPage() => _activityToken = _activity.Begin();
        public void Release() => _activity.Cancel();
        public void Reenter() => _activityToken = _activity.Begin();
        public void Submit(Microsoft.UI.Xaml.Controls.AutoSuggestBox box, object? suggestion) =>
            OnSettingsSearchSubmitted(box, new() { ChosenSuggestion = suggestion });
        public void Dispose() => _activity.Dispose();
    }

    // This is an external activity owner boundary, not a reproduction of search navigation logic.
    internal sealed class ActivityOwner : IDisposable
    {
        private CancellationTokenSource _current = new();
        public CancellationToken Begin()
        {
            _current.Cancel();
            _current.Dispose();
            _current = new();
            return _current.Token;
        }
        public bool IsCurrent(CancellationToken token) => !_current.IsCancellationRequested && _current.Token == token;
        public void Cancel() => _current.Cancel();
        public void Dispose() { _current.Cancel(); _current.Dispose(); }
    }
}
