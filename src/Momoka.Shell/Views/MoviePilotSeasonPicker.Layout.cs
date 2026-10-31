using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Momoka.Shell.ViewModels;
using Windows.System;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSeasonPicker
{
    private void OnPickerSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // ContentDialog 表面按内容定宽，正文先给出目标宽度，窄窗自然换行。
        var width = Math.Min(672, Math.Max(232, args.NewSize.Width - 80));
        PickerBody.Width = width;
        PickerTitleBar.Width = width;
        PickerScroll.MaxHeight = Math.Max(180, (XamlRoot?.Size.Height ?? 900) - 220);
    }

    private void OnPickerClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.Submitting) args.Cancel = true;
    }

    private void OnDismissPicker(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.Submitting) Hide();
    }

    private void OnToggleSeasonCard(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: MoviePilotMediaSeasonRow { Editable: true } row }) row.Selected = !row.Selected;
    }

    private void OnSeasonModeTapped(object sender, TappedRoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: MoviePilotMediaSeasonRow { Editable: true } row }) row.MarkModePicked();
    }

    private void OnSeasonModeKeyDown(object sender, KeyRoutedEventArgs args)
    {
        // 只把用户输入记为显式选择，异步入库状态引起的 SelectedIndex 变化不算。
        if (args.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End or VirtualKey.Space or VirtualKey.Enter
            && sender is Segmented { Tag: MoviePilotMediaSeasonRow { Editable: true } row }) row.MarkModePicked();
    }

    private void OnGroupRailChanged(object sender, ScrollViewerViewChangedEventArgs args) => RefreshGroupArrows();
    private void OnGroupRailSizeChanged(object sender, SizeChangedEventArgs args) => RefreshGroupArrows();
    private void RefreshGroupArrows()
    {
        if (PreviousEpisodeGroups is null || NextEpisodeGroups is null) return;
        PreviousEpisodeGroups.Visibility = EpisodeGroupRail.HorizontalOffset > 4 ? Visibility.Visible : Visibility.Collapsed;
        NextEpisodeGroups.Visibility = EpisodeGroupRail.ScrollableWidth - EpisodeGroupRail.HorizontalOffset > 4 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnPreviousGroups(object sender, RoutedEventArgs args) => ScrollGroups(-1);
    private void OnNextGroups(object sender, RoutedEventArgs args) => ScrollGroups(1);
    private void ScrollGroups(int direction) => EpisodeGroupRail.ChangeView(
        Math.Clamp(EpisodeGroupRail.HorizontalOffset + direction * Math.Max(EpisodeGroupRail.ViewportWidth * 0.72, 240), 0, EpisodeGroupRail.ScrollableWidth), null, null);
}
