using Momoka.Diagnostics;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class SettingsPage
{
    private int _searchNavigationGeneration;
    private SettingsSearchResult? _pendingSearchNavigation;

    // 分类入场不能抢走搜索的目标位置；两个滚动请求只允许搜索这一份负责。
    private bool HasPendingSearchNavigation => _pendingSearchNavigation?.CategoryKey == ViewModel.SelectedCategory;

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
        _pendingSearchNavigation = result.Section is not null && result.Row is not null ? result : null;
        if (_pendingSearchNavigation is null) return true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = PositionSearchResultAsync(result, generation, activity)))
            _pendingSearchNavigation = null;
        return true;
    }

    private async Task PositionSearchResultAsync(SettingsSearchResult result, int generation, CancellationToken activity)
    {
        bool IsCurrent() => generation == _searchNavigationGeneration && _activity.IsCurrent(activity)
            && ViewModel.SelectedCategory == result.CategoryKey
            && result.Section is { } section && result.Row is { } row && section.Rows.Contains(row);
        try
        {
            FrameworkElement? target = null;
            // UpdateLayout 完成不等于 ScrollPresenter 已接收新 extent。等实际滚动内容与目标
            // 同时就绪，避免窄窗/发布版把目标偏移钳在上一分类的短内容范围内。
            for (var attempt = 0; attempt < 120 && IsCurrent(); attempt++)
            {
                SettingsContent.UpdateLayout();
                var list = RowLists(SettingsSections).FirstOrDefault(list => ReferenceEquals(list.ItemsSource, result.Section!.Rows));
                if (list?.ContainerFromItem(result.Row) is FrameworkElement container)
                {
                    target = VisualTreeHelper.GetChildrenCount(container) > 0
                        ? VisualTreeHelper.GetChild(container, 0) as FrameworkElement ?? container : container;
                    if (target.ActualHeight > 0 && SettingsScroll.ActualHeight > 0
                        && SettingsScroll.ExtentHeight + 1 >= SettingsCardsContent.ActualHeight) break;
                    target = null;
                }
                await Task.Delay(16, activity);
            }
            if (!IsCurrent() || target is null) return;
            var top = target.TransformToVisual(SettingsCardsContent).TransformPoint(new Windows.Foundation.Point()).Y;
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var seen = new HashSet<int>();
            var correlation = -1;
            void OnCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
            {
                seen.Add(args.CorrelationId);
                if (args.CorrelationId == correlation) complete.TrySetResult();
            }
            SettingsScroll.ScrollCompleted += OnCompleted;
            try
            {
                correlation = SettingsScroll.ScrollTo(0, Math.Max(0, top - SettingsScroll.ActualHeight * 0.2),
                    new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
                if (correlation != -1 && !seen.Contains(correlation))
                    await complete.Task.WaitAsync(TimeSpan.FromSeconds(2), activity);
            }
            finally { SettingsScroll.ScrollCompleted -= OnCompleted; }
            if (IsCurrent()) FirstFocusable(target)?.Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) when (activity.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsCurrent()) Log.Warn(Category, "定位设置搜索结果失败", error);
        }
        finally
        {
            if (generation == _searchNavigationGeneration) _pendingSearchNavigation = null;
        }
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
