#if ITEM_USER_STATE_ORDER_FIXTURE
using Momoka.Emby;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// Only framework values, click dispatch and unrelated commands are substitutes.
// ItemCommands.cs, CardItem.cs, IShellActions.cs and the shared request helpers are real sources.
namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum TextAlignment { Left, Center }
    public enum GridUnitType { Auto, Pixel, Star }
    public readonly record struct GridLength(double Value, GridUnitType GridUnitType);
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double value) : this(value, value, value, value) { }
    }
    public class FrameworkElement
    {
        public object? DataContext { get; set; }
    }
    public sealed class RoutedEventArgs : EventArgs;
}

namespace Microsoft.UI.Xaml.Media
{
    public enum Stretch { None, Fill, Uniform, UniformToFill }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage;
}

namespace Microsoft.UI.Xaml.Input
{
    public sealed class ContextRequestedEventArgs : EventArgs
    {
        public bool Handled { get; set; }
        public bool TryGetPosition(FrameworkElement owner, out Windows.Foundation.Point point)
        {
            point = default;
            return false;
        }
    }
}

namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public enum FlyoutPlacementMode { BottomEdgeAlignedLeft }
    public sealed class FlyoutShowOptions
    {
        public Windows.Foundation.Point Position { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        public static void SetAutomationId(object element, string id) { }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public class ContentControl : FrameworkElement
    {
        public object? Content { get; set; }
    }
    public sealed class FontIcon
    {
        public string Glyph { get; set; } = "";
    }
    public class MenuFlyoutItemBase
    {
        public object? Tag { get; set; }
    }
    public sealed class MenuFlyoutSeparator : MenuFlyoutItemBase;
    public sealed class MenuFlyoutItem : MenuFlyoutItemBase
    {
        public string Text { get; set; } = "";
        public FontIcon? Icon { get; set; }
        public event EventHandler<RoutedEventArgs>? Click;
        public void ClickForFixture() => Click?.Invoke(this, new RoutedEventArgs());
    }
    public sealed class MenuFlyoutSubItem : MenuFlyoutItemBase
    {
        public string Text { get; set; } = "";
        public FontIcon? Icon { get; set; }
        public List<MenuFlyoutItemBase> Items { get; } = [];
    }
    public sealed class MenuFlyout
    {
        public Controls.Primitives.FlyoutPlacementMode Placement { get; set; }
        public List<MenuFlyoutItemBase> Items { get; } = [];
        public event EventHandler? Opened;
        public event EventHandler? Closed;
        public void Hide() => Closed?.Invoke(this, EventArgs.Empty);
        public void ShowAt(FrameworkElement owner) => Opened?.Invoke(this, EventArgs.Empty);
        public void ShowAt(FrameworkElement owner, Controls.Primitives.FlyoutShowOptions options) => ShowAt(owner);
    }
}

namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        public static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeAsync(byte[] bytes, int width) =>
            throw new InvalidOperationException("R-016 fixture must not load or decode artwork");
    }
}

namespace Momoka.Shell.Views
{
    public sealed class PosterCard : FrameworkElement
    {
        public CardItem? Card { get; set; }
    }
    public sealed class EpisodeRow : FrameworkElement
    {
        public CardItem? Card { get; set; }
    }

    internal static partial class ItemCommands
    {
        private static Exception Unrelated() => new InvalidOperationException("R-016 fixture reached an unrelated server/dialog command");
        private static void HideFromResume(EmbySessionScope scope, IShellActions shell, CardItem card, Action? changed) => throw Unrelated();
        private static void ScanLibrary(EmbySessionScope scope, IShellActions shell) => throw Unrelated();
        private static void OpenSeries(EmbySessionScope scope, IShellActions shell, EmbyItem item) => throw Unrelated();
        private static Task CollectAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card) => throw Unrelated();
        private static Task DownloadAsync(EmbySessionScope scope, IShellActions shell, CardItem card) => throw Unrelated();
        private static Task EditAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed) => throw Unrelated();
        private static Task CoverAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed) => throw Unrelated();
        private static Task SubtitlesAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed) => throw Unrelated();
        private static Task IdentifyAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed) => throw Unrelated();
        private static Task FnNasVersionsAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed) => throw Unrelated();
        private static Task FnNasMatchAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed) => throw Unrelated();
        private static Task FnNasUnmatchAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed) => throw Unrelated();
        private static Task RefreshAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card) => throw Unrelated();
        private static Task MissingEpisodesAsync(EmbySessionScope scope, IShellActions shell, CardItem card) => throw Unrelated();
        private static Task DeleteAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed, Action<CardItem>? removed) => throw Unrelated();
    }
}
#endif
