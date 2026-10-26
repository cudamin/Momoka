using Momoka.Shell.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class SettingsPage
{
    private int _searchNavigationGeneration;

    private void OnSettingsSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var result = args.ChosenSuggestion as SettingsSearchResult ?? ViewModel.SettingsSearchResults.FirstOrDefault();
        if (result is not null) NavigateSettingsSearchResult(result);
        sender.IsSuggestionListOpen = false;
    }

    internal bool NavigateSettingsSearchResult(SettingsSearchResult result)
    {
        if (!_activity.IsCurrent(_activityToken) || !ViewModel.SelectSettingsSearchResult(result)) return false;
        var generation = ++_searchNavigationGeneration;
        var activity = _activityToken;
        if (result.Section is not { } section || result.Row is not { } row) return true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation != _searchNavigationGeneration || !_activity.IsCurrent(activity)
                || ViewModel.SelectedCategory != result.CategoryKey || !section.Rows.Contains(row)) return;
            SettingsSections.UpdateLayout();
            var list = RowLists(SettingsSections).FirstOrDefault(list => ReferenceEquals(list.ItemsSource, section.Rows));
            if (list?.ContainerFromItem(row) is not FrameworkElement container) return;
            var target = VisualTreeHelper.GetChildrenCount(container) > 0
                ? VisualTreeHelper.GetChild(container, 0) as FrameworkElement ?? container : container;
            FirstFocusable(target)?.Focus(FocusState.Programmatic);
            // 分类刷新也排了异步 ScrollTo(0)。最后用内容坐标提交明确位置，不依赖
            // StartBringIntoView 与先前复位的时序，输入焦点引起的滚动也由这次请求收口。
            var top = target.TransformToVisual(SettingsCardsContent).TransformPoint(new Windows.Foundation.Point()).Y;
            SettingsScroll.ScrollTo(0, Math.Max(0, top - SettingsScroll.ActualHeight * 0.2),
                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
        });
        return true;
    }

    private static Control? FirstFocusable(DependencyObject root)
    {
        if (root is Control { IsTabStop: true, Visibility: Visibility.Visible } control) return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FirstFocusable(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
    }

    private static IEnumerable<ItemsControl> RowLists(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ItemsControl { Name: "SettingsRowsList" } list) yield return list;
            else
                foreach (var descendant in RowLists(child)) yield return descendant;
        }
    }
}
