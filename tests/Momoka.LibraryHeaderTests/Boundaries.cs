using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// Value-only WinUI boundaries. The fixture executes the actual page methods, not a layout engine.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum FocusState { Keyboard }
    public readonly record struct CornerRadius(double Value);
    public sealed class SizeChangedEventArgs(double height) : EventArgs
    {
        public (double Width, double Height) NewSize => (30, height);
    }
    public static class VisualStateManager
    {
        // Characterized by the 25416 real-window log: states on this UserControl were never entered.
        public static bool GoToState(object control, string state, bool transitions) => false;
    }
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double value) : this(value, value, value, value) { }
    }
    public sealed record Style(string Key);
    public class FrameworkElement
    {
        public double ActualWidth { get; set; } = 900;
        public double Width { get; set; }
        public double MaxWidth { get; set; }
        public double RowSpacing { get; set; }
        public double ColumnSpacing { get; set; }
        public bool CenterRows { get; set; } = true;
        public Visibility Visibility { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public Thickness Margin { get; set; }
        public Thickness Padding { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Style? Style { get; set; }
        public object Parent { get; set; } = null!;
        internal int Row, Column, Span;
    }
    public sealed class Application
    {
        public static Application Current { get; } = new();
        public Dictionary<string, object> Resources { get; } = new()
        {
            ["EgPageMargin"] = new Thickness(32, 28, 32, 28),
            ["EgSectionTitleStyle"] = new Style("EgSectionTitleStyle"),
            ["EgPageTitleStyle"] = new Style("EgPageTitleStyle")
        };
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public class UserControl : FrameworkElement { public Dictionary<string, object> Resources { get; } = new(); }
    public class Border : FrameworkElement;
    public class ContentControl : FrameworkElement { public object? Content { get; set; } }
    public sealed class StackPanel : FrameworkElement
    {
        public double Spacing { get; set; }
        public List<FrameworkElement> Children { get; } = [];
    }
    public sealed class Button : ContentControl
    {
        public int TabIndex { get; set; }
        public bool UseSystemFocusVisuals { get; set; }
        public event EventHandler? Click;
        public event Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? KeyDown;
        public bool Focus(FocusState state) => true;
        public void Invoke() => Click?.Invoke(this, EventArgs.Empty);
        public void Press(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args) => KeyDown?.Invoke(this, args);
    }
    public class Grid : Microsoft.UI.Xaml.FrameworkElement
    {
        public static void SetRow(Microsoft.UI.Xaml.FrameworkElement item, int value) => item.Row = value;
        public static void SetColumn(Microsoft.UI.Xaml.FrameworkElement item, int value) => item.Column = value;
        public static void SetColumnSpan(Microsoft.UI.Xaml.FrameworkElement item, int value) => item.Span = value;
    }
    public class TextBlock : Microsoft.UI.Xaml.FrameworkElement { public string Text { get; set; } = ""; }
}
namespace Microsoft.UI.Xaml.Input
{
    public sealed class KeyRoutedEventArgs : EventArgs
    {
        public Windows.System.VirtualKey Key { get; set; }
        public bool Handled { get; set; }
    }
}
namespace Windows.System { public enum VirtualKey { Up, Down, Left, Right, Home, End } }
namespace CommunityToolkit.WinUI.Controls
{
    public sealed class SegmentedItem : Microsoft.UI.Xaml.FrameworkElement { public object? Content { get; set; } }
}
namespace Momoka.Shell.Views
{
    public sealed partial class AlphaBar
    {
        internal Border Rail { get; } = new() { Padding = new Thickness(0, 5, 0, 5), CornerRadius = new CornerRadius(13) };
        internal ContentControl Letters { get; } = new();
        private void InitializeComponent()
        {
            foreach (var name in new[] { "EgAlphaButtonStyle", "EgAlphaCurrentStyle", "EgAlphaCompactButtonStyle", "EgAlphaCompactCurrentStyle" })
                Resources[name] = new Style(name);
        }
        internal void Resize(double height) => OnSizeChanged(this, new SizeChangedEventArgs(height));
        internal string ButtonStyle(string letter) => _buttons[letter].Style!.Key;
    }
    public sealed class HeaderRequest { public bool IsSearch { get; set; } }
    public sealed class HeaderViewModel
    {
        public bool IsSectionLibrary, IsFolderDrillDown, IsFnNas, IsJellyfinListPage, IsUpcomingPage, IsFavoritesPage, IsJellyfin;
        public Visibility MoviePilotTabsVisibility { get; set; }
    }
    public sealed class HeaderTabs : FrameworkElement
    {
        public List<CommunityToolkit.WinUI.Controls.SegmentedItem> Items { get; } = [];
    }
    public sealed class HeaderSitePicker : FrameworkElement { public void SetCompact(double width) { } }

    public sealed partial class LibraryPage
    {
        private bool _attaching;
        private readonly HeaderRequest _request = new();
        private readonly FrameworkElement _moviePilotPanel = new(), _moviePilotResources = new(), _aniBtPanel = new();
        public HeaderViewModel ViewModel { get; } = new();
        public Grid HeaderLayout { get; } = new();
        public Grid PageRoot { get; } = new();
        public Grid HeaderGrid { get; } = new();
        public FrameworkElement UpcomingScroll { get; } = new() { Parent = new Grid() };
        public FrameworkElement SearchCategories { get; } = new();
        public FrameworkElement LibrarySectionsSurface { get; } = new();
        public FrameworkElement LibrarySections { get; } = new();
        public FrameworkElement ToolbarSurface { get; } = new();
        public FrameworkElement Toolbar { get; } = new();
        public FrameworkElement LibraryBrowseGroup { get; } = new();
        public FrameworkElement LibraryFilterChips { get; } = new();
        public FrameworkElement FnNasFilterPanel { get; } = new();
        public FrameworkElement EmbyContent { get; } = new();
        public FrameworkElement LibraryBusyBar { get; } = new();
        public FrameworkElement LibraryNoticeBar { get; } = new();
        public FrameworkElement LibraryTitle { get; } = new();
        public FrameworkElement SearchSurface { get; } = new();
        public FrameworkElement SearchEntry { get; } = new();
        public FrameworkElement SearchForm { get; } = new();
        public FrameworkElement Alpha { get; } = new();
        public HeaderTabs SourceTabs { get; } = new();
        public HeaderSitePicker SearchSitePicker { get; } = new();
        public int SearchSource { get; set; }
        public bool SearchSourceAvailable { get; set; }
        public int HeaderApplies { get; private set; }
        public void Apply(bool search = false, bool attaching = false)
        {
            _request.IsSearch = search;
            _attaching = attaching;
            ApplyHeaderLayout();
        }
    }
}
