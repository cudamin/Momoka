using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Momoka.Tests;

// Supplies layout readings/container delivery, never timing, cancellation or animation policy.
internal sealed class MotionFixture : IDisposable
{
    private readonly Dictionary<SettingRow, RowVisual> _visuals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SettingRow, double> _heights = new(ReferenceEqualityComparer.Instance);
    private readonly List<RowVisual> _history = [];
    internal Grid Page { get; } = new() { LayoutX = 40, LayoutY = 30, ActualWidth = 800, NaturalHeight = 700 };
    internal ScrollView Scroll { get; } = new() { LayoutX = 180, LayoutY = 60, ActualWidth = 620, NaturalHeight = 500 };
    internal Border Surface { get; } = new() { ActualWidth = 620, Padding = new(6) };
    internal ItemsControl List { get; } = new() { ActualWidth = 608, LayoutX = 6, LayoutY = 6, ItemTemplateSelector = new() };
    internal SettingSection Section { get; } = new();
    internal TestRow Parent { get; } = new("上级开关");
    internal TestRow Quality { get; } = new("质量");
    internal TestRow Compression { get; } = new("压缩");
    internal TestRow Tail { get; } = new("后续有效行");
    internal SettingsRowsMotion Motion { get; private set; } = null!;
    internal UISettings SystemPreference => UISettings.Created.Last();
    internal IEnumerable<RowVisual> AllVisuals => _history.Concat(_visuals.Values);
    internal int LayoutReads { get; private set; }
    internal int QualityPreference { get; set; } = 73;
    internal int CompressionPreference { get; set; } = 2;

    internal MotionFixture(bool enabled = true, bool attachSurface = true)
    {
        HomeMotion.Reset();
        UISettings.Reset();
        Storyboard.Reset();
        HomeMotion.ApplyPreference(enabled);
        Page.AddVisualChild(Scroll);
        Scroll.AddVisualChild(Surface);
        if (attachSurface) Surface.AddVisualChild(List);
        List.ItemsSource = Section.Rows;
        foreach (var row in new[] { Parent, Quality, Compression, Tail }) Section.Rows.Add(row);
        List.OnUpdateLayout = Layout;
        Layout();
        Motion = new SettingsRowsMotion(List, Section);
    }

    internal void Layout()
    {
        LayoutReads++;
        foreach (var row in _visuals.Keys.Where(row => !Section.Rows.Contains(row)).ToArray())
        {
            var visual = _visuals[row];
            _history.Add(visual);
            List.RemoveContainer(row);
            _visuals.Remove(row);
        }
        var top = 0d;
        foreach (var row in Section.Rows)
        {
            if (!_visuals.TryGetValue(row, out var visual))
            {
                var container = new Grid { ActualWidth = List.ActualWidth };
                var target = new Grid { ActualWidth = List.ActualWidth };
                container.AddVisualChild(target);
                List.SetContainer(row, container);
                visual = new(row, container, target);
                _visuals.Add(row, visual);
            }
            var height = _heights.GetValueOrDefault(row, 50);
            visual.Container.LayoutY = top;
            visual.Container.NaturalHeight = height;
            visual.Target.NaturalHeight = height;
            visual.Target.ActualWidth = List.ActualWidth;
            top += height + 1;
        }
        List.NaturalHeight = Math.Max(0, top - 1);
        Surface.NaturalHeight = List.NaturalHeight + Surface.Padding.Top + Surface.Padding.Bottom;
        List.RaiseLayout();
    }

    internal RowVisual Visual(SettingRow row) => _visuals[row];
    internal void SetHeight(SettingRow row, double height) { _heights[row] = height; Layout(); }
    internal void RemoveDependencies()
    {
        Section.Rows.Remove(Quality);
        Section.Rows.Remove(Compression);
    }
    internal void RestoreDependencies()
    {
        Section.Rows.Insert(1, Quality);
        Section.Rows.Insert(2, Compression);
    }
    internal Storyboard Start()
    {
        List.DispatcherQueue.Drain();
        return Storyboard.Active.Single();
    }
    internal void Finish()
    {
        List.DispatcherQueue.Drain();
        Storyboard.Advance(SettingsRowsMotion.DurationMs);
        Layout();
    }
    internal SettingsRowsMotion Reattach()
    {
        Motion.Dispose();
        Motion = new SettingsRowsMotion(List, Section);
        return Motion;
    }
    internal void AssertClean()
    {
        Assert.False(Motion.IsAnimating);
        Assert.Equal(0, Storyboard.Count);
        Assert.True(double.IsNaN(Surface.Height), "必须恢复 Auto 高度");
        Assert.Null(Surface.Clip);
        Assert.True(Popup.Created.All(popup => !popup.IsOpen && popup.Child is null), "淡出副本必须关闭、断开 Child");
        foreach (var visual in AllVisuals)
        {
            Assert.Equal(1d, visual.Target.Opacity, "未设置透明度的真实行必须完全不透明");
            Assert.Null(visual.Target.RenderTransform, "动画的位移不能残留");
            Assert.Equal(1d, visual.Container.Opacity, "不动画框架容器");
            Assert.Null(visual.Container.RenderTransform);
            Assert.True(double.IsNaN(visual.Container.Height));
        }
    }
    public void Dispose()
    {
        Motion.Dispose();
        Assert.Equal(0, HomeMotion.PreferenceSubscriptionCount);
        Assert.Equal(0, SystemPreference.SubscriptionCount);
        Assert.Equal(0, Section.PropertySubscriptionCount);
        Assert.Equal(0, List.LayoutSubscriptionCount);
        Assert.Equal(0, List.UnloadedSubscriptionCount);
        Assert.Equal(0, Surface.SizeSubscriptionCount);
        foreach (var element in new FrameworkElement[] { Page, Scroll, Surface, List }) Assert.Equal(0, element.PropertySubscriptionCount);
        Storyboard.Reset();
        HomeMotion.Reset();
        UISettings.Reset();
    }

    internal sealed record RowVisual(SettingRow Row, Grid Container, Grid Target);
}
