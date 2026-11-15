using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage : Page
{
    internal DetailViewModel ViewModel { get; } = new();
    public Border StickyHeader { get; } = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    public Grid StickyHeaderContent { get; } = new() { ColumnSpacing = 16 };
    public Image StickyPoster { get; } = new();
    public Button StickyPlayButton { get; } = new();
    public TextBlock StickyPlayText { get; } = new();
    public Button ColumnPlayButton { get; } = new();
    public Button CompactPlayButton { get; } = new();
    public ScrollView Body { get; } = new() { ActualHeight = 700 };

    public DetailPage()
    {
        StickyHeader.Margin = new(32, 12, 32, 0);
        StickyHeader.Padding = new(16, 12, 16, 12);
        ColumnPlayButton.Parent = Body;
        CompactPlayButton.Parent = Body;
        foreach (var element in new FrameworkElement[] { StickyHeader, StickyHeaderContent, StickyPoster,
            StickyPlayButton, StickyPlayText, ColumnPlayButton, CompactPlayButton, Body })
        {
            element.XamlRoot = XamlRoot;
            element.DispatcherQueue = DispatcherQueue;
        }
        ColumnPlayButton.Command = ViewModel.PlayCommand;
        CompactPlayButton.Command = ViewModel.PlayCommand;
        StickyPlayButton.Command = ViewModel.PlayCommand;
        ColumnPlayButton.LayoutY = 100;
        CompactPlayButton.LayoutY = 200;
        PrepareStickyHeader();
    }

    public void LoadHeader() => RaiseLoaded();
    public void ReleaseHeader() => StopStickyHeader();
    public void ResetHeader() => ResetStickyHeader();
    public void UpdateHeader() => UpdateStickyHeader();
    public void Scroll(double offset) { Body.VerticalOffset = offset; UpdateStickyHeader(); }
    public bool Shown => _headerShown;
    public bool Active => _headerActive;
    public bool Queued => _headerQueued;
    public bool PlayMoving => _headerPlayAnimation is not null;
    public DispatcherTimer? PlayTimeout => _headerPlayTimeout;
}
