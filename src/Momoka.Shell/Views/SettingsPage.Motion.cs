using Momoka.Diagnostics;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class SettingsPage
{
    private readonly List<FrameworkElement> _enteringSections = [];
    private int _entranceGeneration;
    private string? _entranceCategory;
    internal const int PageTransitionDurationMs = 180;
    internal const double PageTransitionDistance = 12;

    /// <summary>
    /// Windows 设置式的页面刷新：导航留在原位，同屏内容一起短距离淡入，不逐卡排队。
    /// 保留卡片及输入控件，只动画模板根，不动画 ItemsControl 的布局容器。
    /// </summary>
    private void QueueSettingsEntrance()
    {
        StopSettingsEntrance();

        // 托管页在自己的 Loaded 中入场，父层不再叠加一次淡入和位移。
        if (!IsLoaded || ViewModel.ShowsHosted) return;

        var resetScroll = _entranceCategory != ViewModel.SelectedCategory;
        var category = ViewModel.SelectedCategory;
        var generation = _entranceGeneration;
        if (HomeMotion.AnimationsEnabled) SettingsContent.Opacity = 0;

        // SelectedCategory 的连带显隐通知尚未发完；合并这一轮绑定和快速连续选择，
        // 在新分类布局完成后只给视口内的分组入场，滚动不会补播屏外卡片。
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation != _entranceGeneration) return;
            if (!IsLoaded || ViewModel.ShowsHosted)
            {
                StopSettingsEntrance();
                return;
            }

            try
            {
                // 先提交新分类的测量，再复位；旧 extent 下的 ScrollTo 可能被下一轮锚定抵消。
                SettingsContent.UpdateLayout();
                if (resetScroll && !HasPendingSearchNavigation)
                    SettingsScroll.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
                // 只有有效队列真正处理了滚动才记录分类，重复 Loaded / 快速选择不能取消唯一的复位。
                _entranceCategory = category;
                if (!HomeMotion.AnimationsEnabled) return;
                foreach (var section in SectionRoots(SettingsSections))
                {
                    if (section.Visibility != Visibility.Visible || section.ActualHeight <= 0) continue;
                    var bounds = section.TransformToVisual(SettingsContent).TransformBounds(
                        new Rect(0, 0, section.ActualWidth, section.ActualHeight));
                    if (bounds.Bottom <= 0 || bounds.Top >= SettingsContent.ActualHeight) continue;

                    _enteringSections.Add(section);
                    HomeMotion.Reveal(section, 0, PageTransitionDurationMs, PageTransitionDistance);
                }
            }
            catch (Exception error)
            {
                StopSettingsEntrance();
                Log.Warn(Category, "设置入场动画失败，直接显示内容", error);
            }
            finally { SettingsContent.Opacity = 1; }
        })) StopSettingsEntrance();
    }

    private void StopSettingsEntrance()
    {
        _entranceGeneration++;
        foreach (var section in _enteringSections) HomeMotion.Stop(section);
        _enteringSections.Clear();
        SettingsContent.Opacity = 1;
    }

    private static IEnumerable<FrameworkElement> SectionRoots(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement { Name: "SettingsSection" } section) yield return section;
            else
                foreach (var descendant in SectionRoots(child)) yield return descendant;
        }
    }

    private readonly Dictionary<ListView, SettingsPriorityMotion> _priorityMotions = [];
    private readonly Dictionary<ItemsControl, SettingsRowsMotion> _rowMotions = [];

    private void OnPriorityListLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ListView list && list.Tag is SettingTitleRulesRow row && !_priorityMotions.ContainsKey(list))
            _priorityMotions.Add(list, new SettingsPriorityMotion(list, row));
    }

    private void OnPriorityListUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is ListView list && !list.IsLoaded && _priorityMotions.Remove(list, out var motion)) motion.Detach();
    }

    private void OnPriorityDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (sender is ListView list && _priorityMotions.TryGetValue(list, out var motion)) motion.BeginDrag(args);
        else args.Cancel = true;
    }

    private void OnPriorityDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (sender is ListView list && _priorityMotions.TryGetValue(list, out var motion)) motion.EndDrag();
    }

    private void OnSettingsRowsLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ItemsControl list && (list.Tag ?? list.DataContext) is SettingSection section && !_rowMotions.ContainsKey(list))
            _rowMotions.Add(list, new SettingsRowsMotion(list, section));
    }

    private void OnSettingsRowsUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is ItemsControl list && !list.IsLoaded && _rowMotions.Remove(list, out var motion)) motion.Detach();
    }

    private void ApplySettingsMotionPreference()
    {
        foreach (var motion in _rowMotions.Values) motion.ApplyPreference();
        if (!HomeMotion.AnimationsEnabled)
        {
            StopSettingsEntrance();
            foreach (var motion in _priorityMotions.Values) motion.Stop();
        }
    }

    private void StopSettingsContentMotion()
    {
        foreach (var motion in _rowMotions.Values) motion.Stop();
        foreach (var motion in _priorityMotions.Values) motion.Stop();
    }

    private void DetachSettingsContentMotion()
    {
        foreach (var motion in _rowMotions.Values) motion.Detach();
        foreach (var motion in _priorityMotions.Values) motion.Detach();
        _rowMotions.Clear();
        _priorityMotions.Clear();
    }

    internal bool IsContentMotionSettled => _rowMotions.Values.All(motion => !motion.IsAnimating)
        && _priorityMotions.Values.All(motion => !motion.IsAnimating);

    internal bool HasPrioritySpring(ListView list) => _priorityMotions.TryGetValue(list, out var motion) && motion.HasSpring;
    internal bool HasConditionalMotion(ItemsControl list) => _rowMotions.TryGetValue(list, out var motion) && motion.IsAnimating;
    internal bool HasConditionalTransitions(ItemsControl list) => _rowMotions.TryGetValue(list, out var motion) && motion.HasTransitions;
}
