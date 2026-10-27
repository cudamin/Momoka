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
    private bool? _searchFocusScrolling;
    internal string SearchNavigationReading { get; private set; } = "尚未搜索";

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
        RestoreSearchFocusScrolling();
        var generation = ++_searchNavigationGeneration;
        var activity = _activityToken;
        _pendingSearchNavigation = result.Section is not null && result.Row is not null ? result : null;
        SearchNavigationReading = $"代次{generation} 排队";
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
            var desired = Math.Clamp(top - SettingsScroll.ActualHeight * 0.2, 0, SettingsScroll.ScrollableHeight);
            SearchNavigationReading = $"代次{generation} top={top:0.0} 目标={desired:0.0} 范围={SettingsScroll.ExtentHeight:0.0}";
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, ScrollViewerViewChangedEventArgs args)
            {
                if (!args.IsIntermediate && Math.Abs(SettingsScroll.VerticalOffset - desired) < 1) complete.TrySetResult();
            }
            SettingsScroll.ViewChanged += OnChanged;
            try
            {
                var accepted = SettingsScroll.ChangeView(0, desired, null, disableAnimation: true);
                if (Math.Abs(SettingsScroll.VerticalOffset - desired) >= 1)
                {
                    if (!accepted) throw new InvalidOperationException("设置搜索滚动请求未受理");
                    await complete.Task.WaitAsync(TimeSpan.FromSeconds(2), activity);
                }
            }
            finally { SettingsScroll.ViewChanged -= OnChanged; }
            if (!IsCurrent()) return;
            SearchNavigationReading += $" 完成={SettingsScroll.VerticalOffset:0.0}";
            // 搜索已明确决定位置，聚焦只负责键盘可达，不让它再发第二份自动滚动。
            _searchFocusScrolling ??= SettingsScroll.BringIntoViewOnFocusChange;
            SettingsScroll.BringIntoViewOnFocusChange = false;
            FirstFocusable(target)?.Focus(FocusState.Programmatic);
            await Task.Delay(16, activity);
        }
        catch (OperationCanceledException) when (activity.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsCurrent()) Log.Warn(Category, "定位设置搜索结果失败", error);
        }
        finally
        {
            if (generation == _searchNavigationGeneration)
            {
                RestoreSearchFocusScrolling();
                _pendingSearchNavigation = null;
            }
        }
    }

    private void RestoreSearchFocusScrolling()
    {
        if (_searchFocusScrolling is not { } original) return;
        _searchFocusScrolling = null;
        SettingsScroll.BringIntoViewOnFocusChange = original;
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
