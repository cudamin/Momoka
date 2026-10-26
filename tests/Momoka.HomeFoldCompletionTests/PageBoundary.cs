using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Emby;

namespace Momoka.Shell.Views
{
    internal sealed class CardShelf
    {
        internal double RowHeight { get; set; } = 180;
        internal bool OnScrim { get; set; }
    }
    internal sealed class MoviePilotHomeRow
    {
        internal double RowHeight { get; set; }
    }
    internal sealed class ShelfStrip : FrameworkElement;
    internal sealed class ShelfHead : FrameworkElement;
    internal sealed class TestHomeState
    {
        internal bool LibraryOnBanner { get; set; }
        internal CardShelf? LibraryShelf { get; set; }
        internal List<CardShelf> Shelves { get; } = [];
        internal List<object> Slides { get; } = [new object()];
        internal HomeLibraryPlacement LibraryPlacement { get; set; } = HomeLibraryPlacement.BelowCarousel;
        internal Action<bool>? PublishLayout { get; set; }
        internal int CancelCalls { get; private set; }
        // This is an input/publication boundary, not a copy of HomeViewModel's collection logic.
        internal void SetLibraryOverlay(bool on)
        {
            LibraryOnBanner = on;
            PublishLayout?.Invoke(on);
        }
        internal void Cancel() => CancelCalls++;
    }
    internal sealed class TestBanner : FrameworkElement
    {
        internal FrameworkElement InfoBlock { get; } = new() { RenderTransform = new TranslateTransform(), TestTop = 100 };
        internal bool OverlayOn { get; private set; }
        internal double OverlayHeight { get; private set; }
        internal void SetLibraryOverlay(bool on, double height)
        {
            OverlayOn = on;
            OverlayHeight = height;
            InfoBlock.TestTop = on ? 20 : 100;
        }
    }
    internal sealed class TestWindow
    {
        internal bool PlayerLayer { get; set; }
        internal event Action? PlayerLayerEnded;
        internal void EndPlayerLayer()
        {
            PlayerLayer = false;
            PlayerLayerEnded?.Invoke();
        }
    }
    internal enum TitleStrip { Plain }
    internal sealed class TestShellActions
    {
        internal void SetTitleStrip(TitleStrip strip) { }
    }
    internal sealed class TestSubscriptions
    {
        internal void Release() { }
    }
    internal static class ShellPrefs
    {
        internal static event Action? Changed;
        internal static void Notify() => Changed?.Invoke();
    }
    internal static class HomeMotion
    {
        internal static bool AnimationsEnabled { get; set; } = true;
        internal static int EnterStops { get; private set; }
        internal static FrameworkElement? TargetOf(FrameworkElement row) => row;
        internal static void Stop(FrameworkElement row)
        {
            if (row.RenderTransform is TranslateTransform drift) drift.Y = 0;
            row.Opacity = 1;
        }
        internal static void StopEnter(ItemsRepeater rows)
        {
            EnterStops++;
            foreach (var row in rows.Elements.Values) Stop(row);
        }
    }

    public sealed partial class HomePage
    {
        private const string Category = "主页";
        private readonly DispatcherQueueTimer _watch = new();
        private TestWindow? _window = new();
        private TestShellActions? _actions = new();
        private object? _homeSettings = new();
        private int _homeLibraryFlowIndex;
        private double _libraryFlowHeight;
        internal TestHomeState ViewModel { get; } = new();
        internal ObservableCollection<object> HomeRows { get; } = [];
        internal TestSubscriptions Subscriptions { get; } = new();
        internal TestBanner Banner { get; } = new() { ActualWidth = 1280, ActualHeight = 400 };
        internal FrameworkElement BannerZone { get; } = new();
        internal FrameworkElement LibraryOverlay { get; } = new()
        {
            RenderTransform = new TranslateTransform(),
            ActualHeight = 180,
            TestTop = 260
        };
        internal ContentControl LibraryHost { get; } = new();
        internal ItemsRepeater ShelfRepeater { get; } = new();
        internal ScrollViewer Scroller { get; } = new() { ActualHeight = 900 };
        internal HomeFoldMotion? MainMotion => _fold;
        internal HomeFoldMotion? LateMotion => _foldLate;
        internal bool EntranceSuppressed => _entranceSuppressed;
        internal bool HasSettings => _homeSettings is not null;
        internal DispatcherQueueTimer? Retry => _foldRetry;
        internal void Apply() => ApplyLibraryChrome();
        internal void UpdateFold() => UpdateLibraryOverlay();
        internal void QueueFold() => ScheduleFold();
        internal void EndPlayerLayer() => OnPlayerLayerEnded();
        internal void SetPlayerLayer(bool on) => _window!.PlayerLayer = on;
        internal void ExpireSettle() => _foldPlacedAt = Environment.TickCount64 - 10000;
        internal void ClearBefore() => _before = null;
        internal void SetBefore(double? strip, double? info = null) =>
            _before = new FoldSites(new Dictionary<object, double>(), strip, info);
        internal void SetReal(bool real) => _foldReal = real;
        internal void SetPlaced(bool on) => _foldOn = on;
        internal void ReplaceMain(HomeFoldMotion motion) => _fold = motion;
        internal void ReplaceLate(HomeFoldMotion motion) => _foldLate = motion;
        internal void SetFlowIndex(int index) => _homeLibraryFlowIndex = index;
        private void OnShellPrefsChanged() { }
        private (double Line, bool HasNext)? MeasureMixedCoversLine(double gap) =>
            throw new InvalidOperationException("Mixed directory/VM rows are outside R066's fixture");
        internal static Storyboard BoardOf(HomeFoldMotion motion) =>
            (Storyboard)(typeof(HomeFoldMotion).GetField("_board", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(motion) ?? throw new InvalidOperationException("Missing production storyboard"));
    }
}
