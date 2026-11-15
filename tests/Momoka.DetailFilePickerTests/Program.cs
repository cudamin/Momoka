using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.System;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Case("平台组合保留左对齐受限浮层和单选焦点分离", fixture =>
        {
            Assert.Equal(typeof(DropDownButton), fixture.Picker.DefaultStyleKey);
            Assert.Equal(Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.LeftEdgeAlignedTop, fixture.Picker.PickerFlyout.Placement);
            Assert.True(fixture.Picker.PickerFlyout.ShouldConstrainToRootBounds);
            Assert.Equal(ListViewSelectionMode.Single, fixture.List.SelectionMode);
            Assert.False(fixture.List.SingleSelectionFollowsFocus);
            Assert.Contains("AreStickyGroupHeadersEnabled=\"False\"", fixture.List.ItemsPanel!.Markup);
            Assert.True(fixture.List.GroupStyle.Single().HidesIfEmpty);
        });
        Case("按钮内标签与当前值同框且继承闭合文字颜色", fixture =>
        {
            var content = (Grid)fixture.Picker.Content!;
            var label = (TextBlock)content.FindName("PickerLabel");
            var value = (TextBlock)content.FindName("PickerValue");
            Assert.Equal("版本", label.Text);
            Assert.Equal("版本一", value.Text);
            Assert.True(ReferenceEquals(fixture.Picker, label.Bindings[TextBlock.ForegroundProperty].Source));
            Assert.True(ReferenceEquals(fixture.Picker, value.Bindings[TextBlock.ForegroundProperty].Source));
            Assert.False(fixture.List.Bindings.ContainsKey(Control.ForegroundProperty));
            Assert.NotNull(content.FindName("PickerLabelSurface"));
            Assert.NotNull(content.FindName("PickerSeparator"));
        });
        Case("当前和其他分组保留原对象且标题不进入选择列表", fixture =>
        {
            var groups = fixture.Groups();
            Assert.Equal("当前版本", Key(groups[0]));
            Assert.Equal("其他版本", Key(groups[1]));
            Assert.True(ReferenceEquals(fixture.First, ((IEnumerable)groups[0]).Cast<object>().Single()));
            Assert.True(ReferenceEquals(fixture.Second, ((IEnumerable)groups[1]).Cast<object>().First()));
            Assert.Equal(3, fixture.List.Items.Count);
            Assert.Equal(1, fixture.List.Items.Count(item => ReferenceEquals(item, fixture.First)));
            Assert.True(fixture.List.Items.All(item => fixture.Source.Any(row => ReferenceEquals(row, item))));
        });
        Case("打开预选及键盘焦点移动不会提交", fixture =>
        {
            fixture.Open();
            Assert.True(fixture.Picker.IsDropDownOpen);
            Assert.True(ReferenceEquals(fixture.First, fixture.List.SelectedItem));
            fixture.List.MoveFocus(fixture.Second);
            Assert.True(ReferenceEquals(fixture.First, fixture.Picker.SelectedItem));
            Assert.Equal(0, fixture.SelectionWrites);
            Assert.True(fixture.Picker.IsDropDownOpen);
        });
        foreach (var selectFirst in new[] { true, false })
            Case($"点击在选择事件{(selectFirst ? "之前" : "之后")}到达仍只提交一次", fixture =>
            {
                fixture.Open();
                fixture.List.Click(fixture.Second, selectFirst);
                Assert.True(ReferenceEquals(fixture.Second, fixture.Picker.SelectedItem));
                Assert.Equal(1, fixture.SelectionWrites);
                Assert.False(fixture.Picker.IsDropDownOpen);
                Assert.Equal(1, fixture.Picker.PickerFlyout.HideCount);
            });
        Case("点击当前项关闭而不制造一次双向写入", fixture =>
        {
            fixture.Open();
            fixture.List.Click(fixture.First);
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal(0, fixture.SelectionWrites);
            Assert.Equal(1, fixture.Picker.PickerFlyout.HideCount);
        });
        Case("UIA单选提供者直接选中原始对象并关闭", fixture =>
        {
            fixture.Open();
            fixture.List.AutomationSelect(fixture.Second);
            Assert.True(ReferenceEquals(fixture.Second, fixture.Picker.SelectedItem));
            Assert.Equal(1, fixture.SelectionWrites);
            Assert.False(fixture.Picker.IsDropDownOpen);
        });
        foreach (var key in new[] { VirtualKey.Enter, VirtualKey.Space })
            Case($"焦点项目按{key}提交原对象并关闭", fixture =>
            {
                fixture.Open();
                fixture.List.MoveFocus(fixture.Second);
                Assert.True(fixture.List.RaiseKey(key).Handled);
                Assert.True(ReferenceEquals(fixture.Second, fixture.Picker.SelectedItem));
                Assert.Equal(1, fixture.SelectionWrites);
                Assert.False(fixture.Picker.IsDropDownOpen);
            });
        Case("点外关闭不改变预选或回写VM", fixture =>
        {
            fixture.Open();
            fixture.List.MoveFocus(fixture.Second);
            fixture.Picker.PickerFlyout.LightDismiss();
            fixture.List.RaiseLateClick(fixture.Second);
            Assert.True(ReferenceEquals(fixture.First, fixture.Picker.SelectedItem));
            Assert.Equal(0, fixture.SelectionWrites);
            Assert.False(fixture.Picker.IsDropDownOpen);
        });
        Case("Escape关闭且晚到选择事件无效", fixture =>
        {
            fixture.Open();
            fixture.List.MoveFocus(fixture.Second);
            Assert.True(fixture.List.RaiseKey(VirtualKey.Escape).Handled);
            fixture.List.RaiseLateSelection(fixture.Second);
            Assert.True(ReferenceEquals(fixture.First, fixture.Picker.SelectedItem));
            Assert.Equal(0, fixture.SelectionWrites);
        });
        Case("不可用TrackRow被禁用且所有提交端点拒绝", fixture =>
        {
            var invalid = new TrackRow("不可用音轨", new MediaStream(), false) { IsAvailable = false };
            fixture.Source.Add(invalid);
            fixture.Open();
            Assert.False(((ListViewItem)fixture.List.ContainerFromItem(invalid)!).IsEnabled);
            fixture.List.AutomationSelect(invalid);
            fixture.List.RaiseLateClick(invalid);
            fixture.List.MoveFocus(invalid);
            fixture.List.RaiseKey(VirtualKey.Enter);
            Assert.True(ReferenceEquals(fixture.First, fixture.Picker.SelectedItem));
            Assert.Equal(0, fixture.SelectionWrites);
        });
        Case("容器被禁用后拒绝迟到点击", fixture =>
        {
            fixture.Open();
            ((ListViewItem)fixture.List.ContainerFromItem(fixture.Second)!).IsEnabled = false;
            fixture.List.RaiseLateClick(fixture.Second);
            Assert.Equal(0, fixture.SelectionWrites);
            Assert.True(ReferenceEquals(fixture.First, fixture.Picker.SelectedItem));
        });
        Case("不使用字幕Disable=true是有效原选项", fixture =>
        {
            var automatic = new TrackRow("自动", null, false);
            var noSubtitle = new TrackRow("不使用字幕", null, true);
            fixture.Picker.Label = "字幕";
            fixture.Source.ReplaceAll([automatic, noSubtitle]);
            fixture.Picker.SelectedItem = automatic;
            fixture.ResetWrites();
            fixture.Open();
            Assert.True(fixture.Picker.HasItems);
            Assert.True(((ListViewItem)fixture.List.ContainerFromItem(noSubtitle)!).IsEnabled);
            fixture.List.AutomationSelect(noSubtitle);
            Assert.True(ReferenceEquals(noSubtitle, fixture.Picker.SelectedItem));
            Assert.Equal(1, fixture.SelectionWrites);
        });
        Case("只有自动空音轨保留栏和Items但无空浮层", fixture =>
        {
            var automatic = new TrackRow("自动", null, false);
            fixture.Picker.Label = "音频";
            fixture.Source.ReplaceAll([automatic]);
            fixture.Picker.SelectedItem = automatic;
            fixture.ResetWrites();
            Assert.Equal(1, fixture.Picker.Items.Count);
            Assert.True(ReferenceEquals(automatic, fixture.Picker.Items[0]));
            Assert.False(fixture.Picker.HasItems);
            Assert.Equal("无音轨", fixture.Value.Text);
            Assert.Null(fixture.Picker.Flyout);
            fixture.Open();
            fixture.Picker.Invoke();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal(0, fixture.SelectionWrites);
            Assert.Equal(0, fixture.Picker.PickerFlyout.ShowCount);
        });
        Case("真实音轨到来立即恢复有效菜单", fixture =>
        {
            var automatic = new TrackRow("自动", null, false);
            fixture.Source.ReplaceAll([automatic]);
            fixture.Picker.SelectedItem = automatic;
            fixture.Source.Add(new TrackRow("英语", new MediaStream(), false));
            Assert.True(fixture.Picker.HasItems);
            Assert.Equal("自动", fixture.Value.Text);
            fixture.Open();
            Assert.True(fixture.Picker.IsDropDownOpen);
        });
        Case("空集合显示EmptyText不伪造选项", fixture =>
        {
            fixture.Source.Clear();
            fixture.Picker.EmptyText = "暂无版本";
            Assert.False(fixture.Picker.HasItems);
            Assert.Equal(0, fixture.Picker.Items.Count);
            Assert.Equal(0, fixture.List.Items.Count);
            Assert.Equal("暂无版本", fixture.Value.Text);
            fixture.Open();
            Assert.False(fixture.Picker.IsDropDownOpen);
        });
        foreach (var operation in new[] { "Clear", "Add", "Replace", "Remove", "Move" })
            Case($"集合{operation}立即关闭并拒绝旧菜单事件", fixture =>
            {
                fixture.Open();
                switch (operation)
                {
                    case "Clear": fixture.Source.Clear(); break;
                    case "Add": fixture.Source.Add(new object()); break;
                    case "Replace": fixture.Source[1] = new SourceRow(new MediaSource(), "替换"); break;
                    case "Remove": fixture.Source.RemoveAt(1); break;
                    case "Move": fixture.Source.Move(1, 0); break;
                }
                Assert.False(fixture.Picker.IsDropDownOpen);
                fixture.List.RaiseLateSelection(fixture.Second);
                fixture.List.RaiseLateClick(fixture.Second);
                Assert.Equal(0, fixture.SelectionWrites);
                Assert.True(ReferenceEquals(fixture.First, fixture.Picker.SelectedItem));
            });
        Case("值相等的替换记录仍拒绝旧实例", fixture =>
        {
            fixture.Open();
            var old = fixture.Second;
            fixture.Source[1] = fixture.Second with { };
            fixture.Open();
            fixture.List.RaiseLateSelection(old);
            fixture.List.RaiseLateClick(old);
            Assert.Equal(0, fixture.SelectionWrites);
            Assert.True(ReferenceEquals(fixture.First, fixture.Picker.SelectedItem));
        });
        Case("换ItemsSource解绑旧集合并关闭旧菜单", fixture =>
        {
            fixture.Open();
            var replacement = new ObservedRows([fixture.First, fixture.Second]);
            fixture.Picker.ItemsSource = replacement;
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal(0, fixture.Source.Subscribers);
            Assert.Equal(1, replacement.Subscribers);
            fixture.Open();
            fixture.Source.Clear();
            Assert.True(fixture.Picker.IsDropDownOpen);
            fixture.Picker.ItemsSource = fixture.Source;
            Assert.Equal(0, replacement.Subscribers);
        });
        Case("外部SelectedItem变化关闭菜单并同步当前分组", fixture =>
        {
            fixture.Open();
            fixture.Picker.SelectedItem = fixture.Second;
            fixture.ResetWrites();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal("版本二", fixture.Value.Text);
            fixture.List.RaiseLateClick(fixture.Third);
            Assert.True(ReferenceEquals(fixture.Second, fixture.Picker.SelectedItem));
            Assert.Equal(0, fixture.SelectionWrites);
            Assert.True(ReferenceEquals(fixture.Second, ((IEnumerable)fixture.Groups()[0]).Cast<object>().Single()));
        });
        Case("选择回写同步重建音频字幕时旧事件不重复写入", fixture =>
        {
            fixture.Picker.RegisterPropertyChangedCallback(DetailFilePicker.SelectedItemProperty, (_, _) =>
            {
                fixture.Source.Clear();
                fixture.Source.Add(fixture.Second);
            });
            fixture.Open();
            fixture.List.Click(fixture.Second);
            fixture.List.RaiseLateSelection(fixture.Third);
            Assert.Equal(1, fixture.SelectionWrites);
            Assert.True(ReferenceEquals(fixture.Second, fixture.Picker.SelectedItem));
            Assert.False(fixture.Picker.IsDropDownOpen);
        });
        Case("Loaded重复通知只保留一套集合订阅且不订阅系统事件", fixture =>
        {
            fixture.Picker.NotifyLoaded();
            fixture.Picker.NotifyLoaded();
            Assert.Equal(1, fixture.Source.Subscribers);
            Assert.Equal(0, fixture.Accessibility.Subscribers);
            Assert.Equal(0, fixture.SystemColors.Subscribers);
            Assert.Equal(3, fixture.Picker.PropertyCallbacks);
        });
        Case("Unloaded关闭解绑且卸载期集合变化不触发", fixture =>
        {
            fixture.Open();
            fixture.Picker.Unload();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal(0, fixture.Source.Subscribers);
            Assert.Equal(0, fixture.Accessibility.Subscribers);
            Assert.Equal(0, fixture.SystemColors.Subscribers);
            Assert.Equal(1, fixture.Picker.PropertyCallbacks);
            fixture.Source.Clear();
            Assert.Equal(3, fixture.Picker.Items.Count);
            fixture.Picker.Load();
            Assert.Equal(0, fixture.Picker.Items.Count);
            Assert.Equal(1, fixture.Source.Subscribers);
        });
        Case("延迟Unloaded不拆当前已Loaded菜单", fixture =>
        {
            fixture.Open();
            fixture.Picker.DelayedUnload();
            Assert.True(fixture.Picker.IsDropDownOpen);
            Assert.Equal(1, fixture.Source.Subscribers);
        });
        Case("隐藏和禁用立即关闭且恢复后可重新打开", fixture =>
        {
            fixture.Open();
            fixture.Picker.Visibility = Visibility.Collapsed;
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Null(fixture.Picker.Flyout);
            fixture.Picker.Visibility = Visibility.Visible;
            fixture.Open();
            Assert.True(fixture.Picker.IsDropDownOpen);
            fixture.Picker.IsEnabled = false;
            Assert.False(fixture.Picker.IsDropDownOpen);
            fixture.Picker.IsEnabled = true;
            fixture.Open();
            Assert.True(fixture.Picker.IsDropDownOpen);
        });
        Case("打开后未收到Opened即隐藏仍拒绝晚到Opened", fixture =>
        {
            fixture.Picker.PickerFlyout.DelayOpened = true;
            fixture.Open();
            fixture.Picker.Visibility = Visibility.Collapsed;
            fixture.Picker.PickerFlyout.NotifyOpened();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal(0, fixture.SelectionWrites);
        });
        Case("同一UI回合快速关开等待Closed后重开并可提交", fixture =>
        {
            fixture.Picker.PickerFlyout.DelayClosed = true;
            fixture.Open();
            fixture.Picker.IsDropDownOpen = false;
            fixture.Open();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal(1, fixture.Picker.PickerFlyout.ShowCount);
            fixture.Picker.PickerFlyout.NotifyClosed();
            Assert.False(fixture.Picker.IsDropDownOpen);
            fixture.Picker.DispatcherQueue.Drain();
            Assert.True(fixture.Picker.IsDropDownOpen);
            Assert.Equal(2, fixture.Picker.PickerFlyout.ShowCount);
            fixture.List.AutomationSelect(fixture.Second);
            Assert.True(ReferenceEquals(fixture.Second, fixture.Picker.SelectedItem));
            Assert.Equal(1, fixture.SelectionWrites);
        });
        Case("重复Closed通知只重开一次且晚到旧Closed不拆新菜单", fixture =>
        {
            fixture.Picker.PickerFlyout.DelayClosed = true;
            fixture.Open();
            fixture.Picker.IsDropDownOpen = false;
            fixture.Open();
            fixture.Picker.PickerFlyout.NotifyClosed();
            fixture.Picker.PickerFlyout.NotifyClosed();
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(2, fixture.Picker.PickerFlyout.ShowCount);
            fixture.Picker.PickerFlyout.NotifyClosed();
            Assert.True(fixture.Picker.IsDropDownOpen);
            fixture.List.AutomationSelect(fixture.Second);
            Assert.Equal(1, fixture.SelectionWrites);
        });
        Case("原生按钮点击在旧Closed前到达仍登记重开", fixture =>
        {
            fixture.Picker.PickerFlyout.DelayClosed = true;
            fixture.Picker.Invoke();
            Assert.True(fixture.Picker.IsDropDownOpen);
            fixture.Picker.IsDropDownOpen = false;
            fixture.Picker.Invoke();
            Assert.False(fixture.Picker.IsDropDownOpen);
            fixture.Picker.PickerFlyout.NotifyClosed();
            fixture.Picker.DispatcherQueue.Drain();
            Assert.True(fixture.Picker.IsDropDownOpen);
            fixture.List.AutomationSelect(fixture.Second);
            Assert.Equal(1, fixture.SelectionWrites);
        });
        Case("重复快速原生点击仍只排一个存活重开", fixture =>
        {
            fixture.Picker.PickerFlyout.DelayClosed = true;
            fixture.Picker.Invoke();
            fixture.Picker.IsDropDownOpen = false;
            fixture.Picker.Invoke();
            fixture.Picker.Invoke();
            fixture.Picker.PickerFlyout.NotifyClosed();
            fixture.Picker.DispatcherQueue.Drain();
            Assert.True(fixture.Picker.IsDropDownOpen);
            Assert.Equal(4, fixture.Picker.PickerFlyout.ShowCount);
            Assert.Equal(0, fixture.Picker.DispatcherQueue.Pending);
        });
        Case("排队重开在集合变更后失效", fixture =>
        {
            fixture.Picker.PickerFlyout.DelayClosed = true;
            fixture.Open();
            fixture.Picker.IsDropDownOpen = false;
            fixture.Open();
            fixture.Picker.PickerFlyout.NotifyClosed();
            fixture.Source.Add(new object());
            fixture.Picker.DispatcherQueue.Drain();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal(1, fixture.Picker.PickerFlyout.ShowCount);
        });
        Case("排队重开在卸载或隐藏后失效", fixture =>
        {
            fixture.Picker.PickerFlyout.DelayClosed = true;
            fixture.Open();
            fixture.Picker.IsDropDownOpen = false;
            fixture.Open();
            fixture.Picker.PickerFlyout.NotifyClosed();
            fixture.Picker.Unload();
            fixture.Picker.Load();
            fixture.Picker.DispatcherQueue.Drain();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.Equal(1, fixture.Picker.PickerFlyout.ShowCount);
        });
        Case("祖先隐藏时旧UIA事件不能提交且禁止新打开", fixture =>
        {
            var parent = new Grid();
            fixture.Picker.Parent = parent;
            fixture.Open();
            parent.Visibility = Visibility.Collapsed;
            fixture.List.AutomationSelect(fixture.Second);
            Assert.Equal(0, fixture.SelectionWrites);
            fixture.Picker.IsDropDownOpen = false;
            fixture.Open();
            Assert.False(fixture.Picker.IsDropDownOpen);
        });
        Case("祖先隐藏导致原生Opening取消后不等待不存在的Closed", fixture =>
        {
            var parent = new Grid { Visibility = Visibility.Collapsed };
            fixture.Picker.Parent = parent;
            fixture.Picker.Invoke();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.False(fixture.Picker.PickerFlyout.IsOpen);
            parent.Visibility = Visibility.Visible;
            fixture.Picker.Invoke();
            Assert.True(fixture.Picker.IsDropDownOpen);
            fixture.List.AutomationSelect(fixture.Second);
            Assert.Equal(1, fixture.SelectionWrites);
        });
        Case("Opening回合隐藏取消后恢复仍可新开", fixture =>
        {
            var cancelOnce = true;
            fixture.Picker.PickerFlyout.Opening += (_, _) =>
            {
                if (!cancelOnce) return;
                cancelOnce = false;
                fixture.Picker.Visibility = Visibility.Collapsed;
            };
            fixture.Open();
            Assert.False(fixture.Picker.IsDropDownOpen);
            Assert.False(fixture.Picker.PickerFlyout.IsOpen);
            fixture.Picker.Visibility = Visibility.Visible;
            fixture.Open();
            Assert.True(fixture.Picker.IsDropDownOpen);
        });
        Case("卸载期页面外观刷新无效而重载重新核对系统状态", fixture =>
        {
            var brushes = fixture.Brushes();
            fixture.Picker.Unload();
            AccessibilitySettings.SystemHighContrast = true;
            fixture.Picker.RefreshSystemAppearance();
            Assert.Equal((byte)15, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundSelected"]).Color.A);
            fixture.Picker.Load();
            Assert.Equal(UISettings.Highlight, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundSelected"]).Color);
            AccessibilitySettings.SystemHighContrast = false;
            fixture.Picker.RefreshSystemAppearance();
            Assert.Equal((byte)15, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundSelected"]).Color.A);
            Assert.True(brushes.All(pair => ReferenceEquals(pair.Value, fixture.List.Resources[pair.Key])));
        });
        Case("重复打开六态画刷实例稳定不留旧模板资源", fixture =>
        {
            var brushes = fixture.Brushes();
            for (var i = 0; i < 3; i++) { fixture.Open(); fixture.Picker.IsDropDownOpen = false; }
            Assert.True(brushes.All(pair => ReferenceEquals(pair.Value, fixture.List.Resources[pair.Key])));
            Assert.Equal((byte)15, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundPointerOver"]).Color.A);
            Assert.Equal((byte)10, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundPressed"]).Color.A);
        });
        Case("高对比用系统选中和窗口实色原位更新", fixture =>
        {
            var brushes = fixture.Brushes();
            AccessibilitySettings.SystemHighContrast = true;
            fixture.Picker.RefreshSystemAppearance();
            Assert.Equal(UISettings.Highlight, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundSelected"]).Color);
            Assert.Equal(UISettings.Window, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundSelectedDisabled"]).Color);
            UISettings.Highlight = new(255, 12, 34, 56);
            fixture.Picker.RefreshSystemAppearance();
            Assert.Equal(UISettings.Highlight, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundSelected"]).Color);
            Assert.True(brushes.All(pair => ReferenceEquals(pair.Value, fixture.List.Resources[pair.Key])));
            AccessibilitySettings.SystemHighContrast = false;
            fixture.Picker.RefreshSystemAppearance();
            Assert.Equal((byte)15, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundSelected"]).Color.A);
        });
        Case("页面外观刷新不依赖系统事件和派发队列", fixture =>
        {
            fixture.Picker.DispatcherQueue.Accept = false;
            AccessibilitySettings.SystemHighContrast = true;
            fixture.Picker.RefreshSystemAppearance();
            Assert.Equal(UISettings.Highlight, ((SolidColorBrush)fixture.List.Resources["ListViewItemBackgroundSelected"]).Color);
            Assert.Equal(0, fixture.Picker.DispatcherQueue.Pending);
            Assert.Equal(0, fixture.Accessibility.Subscribers);
            Assert.Equal(0, fixture.SystemColors.Subscribers);
        });
        Case("浮层宽度按可用根空间与480上限约束", fixture =>
        {
            fixture.Picker.ActualWidth = 900;
            fixture.Picker.XamlRoot!.Size = new(1280, 720);
            fixture.Open();
            Assert.Equal(480d, fixture.Picker.FlyoutContent.MaxWidth);
            Assert.Equal(480d, fixture.Picker.FlyoutContent.MinWidth);
            fixture.Picker.IsDropDownOpen = false;
            fixture.Picker.XamlRoot.Size = new(220, 480);
            fixture.Open();
            Assert.Equal(196d, fixture.Picker.FlyoutContent.MaxWidth);
            Assert.Equal(196d, fixture.Picker.FlyoutContent.MinWidth);
        });
        Case("外部AutomationName不被内部文字刷新覆盖", fixture =>
        {
            AutomationProperties.SetName(fixture.Picker, "自定义无障碍名称");
            fixture.Picker.SelectedItem = fixture.Second;
            Assert.Equal("自定义无障碍名称", AutomationProperties.GetName(fixture.Picker));
            Assert.Equal("版本二", AutomationProperties.GetName(fixture.List.ContainerFromItem(fixture.Second)!));
        });
        return TestHarness.Run();
    }

    private static string Key(object group) => (string)group.GetType().GetProperty("Key")!.GetValue(group)!;
    private static void Case(string name, Action<PickerFixture> run) => Test(name, () =>
    {
        AccessibilitySettings.SystemHighContrast = false;
        using var fixture = new PickerFixture();
        run(fixture);
        fixture.Dispose();
        Assert.Equal(0, fixture.Source.Subscribers, "集合仍订阅");
        Assert.Equal(0, fixture.Accessibility.Subscribers, "高对比仍订阅");
        Assert.Equal(0, fixture.SystemColors.Subscribers, "系统颜色仍订阅");
    });

    private sealed class PickerFixture : IDisposable
    {
        public SourceRow First { get; } = new(new MediaSource(), "版本一");
        public SourceRow Second { get; } = new(new MediaSource(), "版本二");
        public SourceRow Third { get; } = new(new MediaSource(), "版本三");
        public ObservedRows Source { get; }
        public DetailFilePicker Picker { get; }
        public ListView List => Picker.ItemsList;
        public TextBlock Value => (TextBlock)((Grid)Picker.Content!).FindName("PickerValue");
        public AccessibilitySettings Accessibility { get; }
        public UISettings SystemColors { get; }
        public int SelectionWrites { get; private set; }
        public PickerFixture()
        {
            Source = new([First, Second, Third]);
            Picker = new() { Label = "版本", ItemsSource = Source, SelectedItem = First };
            Accessibility = AccessibilitySettings.Instances.Last();
            SystemColors = UISettings.Instances.Last();
            Picker.RegisterPropertyChangedCallback(DetailFilePicker.SelectedItemProperty, (_, _) => SelectionWrites++);
            Picker.Load();
        }
        public void Open() => Picker.IsDropDownOpen = true;
        public void ResetWrites() => SelectionWrites = 0;
        public object[] Groups()
        {
            var source = (CollectionViewSource)typeof(DetailFilePicker).GetField("_groupedItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Picker)!;
            return ((IEnumerable)source.Source!).Cast<object>().ToArray();
        }
        public Dictionary<string, object> Brushes() => List.Resources.ToDictionary();
        public void Dispose()
        {
            Picker.Unload();
            Picker.DispatcherQueue.Drain();
            AccessibilitySettings.SystemHighContrast = false;
            FocusManager.Current = null;
        }
    }
}

internal sealed class ObservedRows(IEnumerable<object> items) : IList<object>, INotifyCollectionChanged
{
    private readonly List<object> _items = [.. items];
    private NotifyCollectionChangedEventHandler? _changed;
    public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
    public event NotifyCollectionChangedEventHandler? CollectionChanged { add => _changed += value; remove => _changed -= value; }
    public object this[int index]
    {
        get => _items[index];
        set
        {
            var old = _items[index];
            _items[index] = value;
            _changed?.Invoke(this, new(NotifyCollectionChangedAction.Replace, value, old, index));
        }
    }
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public void Add(object item) { _items.Add(item); _changed?.Invoke(this, new(NotifyCollectionChangedAction.Add, item, Count - 1)); }
    public void Clear() { _items.Clear(); _changed?.Invoke(this, new(NotifyCollectionChangedAction.Reset)); }
    public void Insert(int index, object item) { _items.Insert(index, item); _changed?.Invoke(this, new(NotifyCollectionChangedAction.Add, item, index)); }
    public void RemoveAt(int index) { var item = _items[index]; _items.RemoveAt(index); _changed?.Invoke(this, new(NotifyCollectionChangedAction.Remove, item, index)); }
    public void Move(int from, int to)
    {
        var item = _items[from];
        _items.RemoveAt(from); _items.Insert(to, item);
        _changed?.Invoke(this, new(NotifyCollectionChangedAction.Move, item, to, from));
    }
    public void ReplaceAll(IEnumerable<object> values) { Clear(); foreach (var value in values) Add(value); }
    public bool Remove(object item) { var index = IndexOf(item); if (index < 0) return false; RemoveAt(index); return true; }
    public bool Contains(object item) => _items.Contains(item);
    public int IndexOf(object item) => _items.IndexOf(item);
    public void CopyTo(object[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
