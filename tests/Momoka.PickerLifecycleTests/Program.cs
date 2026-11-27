using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Case("同一存活打开请求在下一UI回合撑宽，保留原生受限弹层", fixture =>
        {
            fixture.Open();
            Assert.True(double.IsNaN(fixture.Picker.Width));
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(240d, fixture.Picker.Width);
            Assert.Equal(1, fixture.List.SizeSubscribers);
            Assert.True(fixture.Popup.ShouldConstrainToRootBounds);
        });
        Case("快速开关后旧队列不钉闭合宽度或订阅列表", fixture =>
        {
            fixture.Open(); fixture.Close();
            var writes = fixture.Picker.WidthWrites;
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(writes, fixture.Picker.WidthWrites);
            Assert.True(double.IsNaN(fixture.Picker.Width));
            Assert.Equal(0, fixture.List.SizeSubscribers);
        });
        Case("快速关后重开只允许最新请求，不借新打开复活旧请求", fixture =>
        {
            fixture.Open(); fixture.Close(); fixture.Open();
            fixture.Picker.DispatcherQueue.DrainOne();
            Assert.True(double.IsNaN(fixture.Picker.Width));
            Assert.Equal(0, fixture.List.SizeSubscribers);
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(240d, fixture.Picker.Width);
            Assert.Equal(1, fixture.List.SizeSubscribers);
        });
        Case("打开通知重复到达仍只挂一份当前订阅", fixture =>
        {
            fixture.Open(); fixture.Picker.NotifyOpened();
            fixture.Picker.DispatcherQueue.DrainOne();
            Assert.Equal(0, fixture.List.SizeSubscribers);
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(240d, fixture.Picker.Width);
            Assert.Equal(1, fixture.List.SizeSubscribers);
        });
        Case("卸载发生在队列前时旧展开请求不改控件", fixture =>
        {
            fixture.Open(); fixture.Picker.Unload();
            fixture.Picker.DispatcherQueue.Drain();
            Assert.True(double.IsNaN(fixture.Picker.Width));
            Assert.Equal(0, fixture.List.SizeSubscribers);
        });
        Case("已展开卸载即释放尺寸订阅和封顶", fixture =>
        {
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            fixture.List.Arrange(620);
            Assert.Equal(580d, fixture.List.MaxWidth);
            fixture.Picker.Unload();
            Assert.True(double.IsNaN(fixture.Picker.Width));
            Assert.Equal(0, fixture.List.SizeSubscribers);
            Assert.True(double.IsPositiveInfinity(fixture.List.MaxWidth));
        });
        Case("仍Loaded的延迟Unloaded不破坏当前打开", fixture =>
        {
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            fixture.Picker.DelayedUnload();
            Assert.Equal(240d, fixture.Picker.Width);
            Assert.Equal(1, fixture.List.SizeSubscribers);
        });
        Case("卸载重载当前仍打开的Popup允许新Loaded请求", fixture =>
        {
            fixture.Open(); fixture.Picker.Unload(); fixture.Picker.Load();
            fixture.Picker.DispatcherQueue.DrainOne();
            Assert.Equal(0, fixture.List.SizeSubscribers);
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(240d, fixture.Picker.Width);
            Assert.Equal(1, fixture.List.SizeSubscribers);
        });
        Case("重套模板结束旧Popup订阅，不让旧队列改新Popup", fixture =>
        {
            fixture.Open();
            var replacement = fixture.ReplacePopup(310);
            fixture.Open();
            fixture.Picker.DispatcherQueue.DrainOne();
            Assert.Equal(0, replacement.SizeSubscribers);
            Assert.True(double.IsNaN(fixture.Picker.Width));
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(310d, fixture.Picker.Width);
            Assert.Equal(1, replacement.SizeSubscribers);
            Assert.Equal(0, fixture.List.SizeSubscribers);
        });
        Case("已展开重套模板清旧MaxWidth和监听", fixture =>
        {
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain(); fixture.List.Arrange(420);
            fixture.ReplacePopup(310);
            Assert.Equal(0, fixture.List.SizeSubscribers);
            Assert.True(double.IsPositiveInfinity(fixture.List.MaxWidth));
            Assert.True(double.IsNaN(fixture.Picker.Width));
        });
        Case("模板缺Popup时旧请求失效且后续有效模板能恢复", fixture =>
        {
            fixture.Open(); fixture.Picker.ReapplyTemplate(null);
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Null(fixture.Picker.PopupPart);
            Assert.Equal(0, fixture.List.SizeSubscribers);
            Assert.True(double.IsNaN(fixture.Picker.Width));
            fixture.Picker.ReapplyTemplate(fixture.Popup); fixture.Close(); fixture.Open();
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(240d, fixture.Picker.Width);
        });
        Case("拒绝派发不挂半份监听，下次打开可重试", fixture =>
        {
            fixture.Picker.DispatcherQueue.Accept = false;
            fixture.Open();
            Assert.Equal(0, fixture.List.SizeSubscribers);
            Assert.True(double.IsNaN(fixture.Picker.Width));
            fixture.Close(); fixture.Picker.DispatcherQueue.Accept = true; fixture.Open();
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(240d, fixture.Picker.Width);
        });
        Case("Popup本体已关闭但控制器通知未到时旧请求无效", fixture =>
        {
            fixture.Open(); fixture.Popup.IsOpen = false;
            fixture.Picker.DispatcherQueue.Drain();
            Assert.True(double.IsNaN(fixture.Picker.Width));
            Assert.Equal(0, fixture.List.SizeSubscribers);
        });
        Case("晚到的旧列表尺寸变化不能改替换后的列表", fixture =>
        {
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            var replacement = new FrameworkElement(); replacement.Arrange(330); fixture.Lists.Add(replacement);
            fixture.Popup.Child = replacement;
            var oldWrites = fixture.List.MaxWidthWrites;
            fixture.List.Arrange(450);
            Assert.Equal(oldWrites, fixture.List.MaxWidthWrites);
            fixture.Picker.NotifyOpened(); fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(0, fixture.List.SizeSubscribers);
            Assert.Equal(1, replacement.SizeSubscribers);
            Assert.Equal(330d, fixture.Picker.Width);
        });
        Case("框保留闭合宽度并按测量阶段的可用空间封顶", fixture =>
        {
            fixture.Picker.Arrange(270); fixture.List.Arrange(210);
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(270d, fixture.Picker.Width);
            fixture.Close();
            fixture.Picker.ParentOffsetX = 380; fixture.List.Arrange(500);
            fixture.Picker.Measure(220);
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(220d, fixture.Picker.Width);
            Assert.Equal(220d, fixture.List.MaxWidth);
        });
        Case("条目晚到撑宽列表时选择框跟随增长，列表回落不再收窄", fixture =>
        {
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            fixture.List.Arrange(500); Assert.Equal(500d, fixture.Picker.Width);
            fixture.List.Arrange(210); Assert.Equal(500d, fixture.Picker.Width);
            fixture.Close(); fixture.List.Arrange(330);
            Assert.True(double.IsPositiveInfinity(fixture.List.MaxWidth));
        });
        Case("自然宽父级在展开时塌缩不再压窄选择框和列表", fixture =>
        {
            fixture.Picker.Measure(double.PositiveInfinity);
            fixture.Picker.Arrange(270);
            ((FrameworkElement)fixture.Picker.Parent!).Arrange(100);
            fixture.List.Arrange(310);
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(310d, fixture.Picker.Width);
            Assert.True(double.IsPositiveInfinity(fixture.List.MaxWidth));
            fixture.List.Arrange(420);
            Assert.Equal(420d, fixture.Picker.Width);
        });
        Case("展开态固定Width引起的重新测量不覆盖真实栏宽", fixture =>
        {
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            fixture.Picker.Measure(240);
            fixture.List.Arrange(500);
            Assert.Equal(500d, fixture.Picker.Width);
            fixture.List.Arrange(620);
            Assert.Equal(580d, fixture.Picker.Width);
            Assert.Equal(580d, fixture.List.MaxWidth);
        });
        Case("关闭后重新测量更新下一次展开上限而非沿用旧宽", fixture =>
        {
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            fixture.List.Arrange(500);
            fixture.Close(); fixture.Picker.Arrange(160); fixture.Picker.Measure(200);
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(200d, fixture.Picker.Width);
            Assert.Equal(200d, fixture.List.MaxWidth);
        });
        Case("首次重套模板不清调用方闭合Width", fixture =>
        {
            fixture.Picker.Width = 175;
            fixture.Picker.ReapplyTemplate(fixture.Popup);
            Assert.Equal(175d, fixture.Picker.Width);
        });
        Case("轨道可用性仍走真实Core TrackRow和原生容器", fixture =>
        {
            var container = new ComboBoxItem();
            fixture.Picker.Prepare(container, new TrackRow("不可用", null, false) { IsAvailable = false });
            Assert.False(container.IsEnabled);
            fixture.Picker.Prepare(container, new TrackRow("自动", null, false));
            Assert.True(container.IsEnabled);
            fixture.Picker.Prepare(container, "普通项");
            Assert.True(container.IsEnabled);
        });
        foreach (var member in new[] { nameof(TrackRow.Text), nameof(TrackRow.ShortText) })
        {
            Case($"闭合正文{member}在框架关闭回写后恢复，未换选中项也清除两行模板", fixture =>
            {
                var row = new TrackRow("自动 · English · 完整音轨", null, false) { ShortText = "English · 完整音轨" };
                fixture.Picker.SelectedItem = row;
                fixture.Picker.ItemTemplate = new object();
                fixture.Picker.ItemTemplateSelector = new object();
                fixture.Picker.SelectionTextMemberPath = member;
                fixture.Picker.DispatcherQueue.Drain();
                var expected = member == nameof(TrackRow.Text) ? row.Text : row.ShortText;
                var presenter = fixture.Picker.SelectionPresenter!;
                Assert.Equal(expected, presenter.Content);
                fixture.Open(); fixture.Picker.DispatcherQueue.Drain(); fixture.Close();
                Assert.True(ReferenceEquals(row, presenter.Content), "边界没有模拟框架关闭时重写正文");
                Assert.True(ReferenceEquals(fixture.Picker.ItemTemplate, presenter.ContentTemplate));
                Assert.True(ReferenceEquals(fixture.Picker.ItemTemplateSelector, presenter.ContentTemplateSelector));
                fixture.Picker.DispatcherQueue.Drain();
                Assert.Equal(expected, presenter.Content);
                Assert.Null(presenter.ContentTemplate);
                Assert.Null(presenter.ContentTemplateSelector);
                Assert.True(double.IsNaN(fixture.Picker.Width));
            });
        }
        Case("快速关闭重开只使用最新正文，旧撑宽队列不过界", fixture =>
        {
            var first = new TrackRow("自动 · first", null, false) { ShortText = "first" };
            var latest = new TrackRow("自动 · latest", null, false) { ShortText = "latest" };
            fixture.Picker.SelectedItem = first;
            fixture.Picker.SelectionTextMemberPath = nameof(TrackRow.ShortText);
            fixture.Picker.DispatcherQueue.Drain();
            fixture.Open(); fixture.Close();
            fixture.Picker.SelectedItem = latest;
            fixture.Picker.NotifySelectionChanged();
            fixture.Open();
            fixture.Picker.DispatcherQueue.DrainOne();
            Assert.True(double.IsNaN(fixture.Picker.Width));
            Assert.Equal(0, fixture.List.SizeSubscribers);
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal("latest", fixture.Picker.SelectionPresenter!.Content);
            Assert.Equal(240d, fixture.Picker.Width);
            fixture.Close(); fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal("latest", fixture.Picker.SelectionPresenter.Content);
        });
        Case("关闭后重套模板不让旧文本队列改旧正文", fixture =>
        {
            var row = new TrackRow("自动 · 原轨道", null, false) { ShortText = "原轨道" };
            fixture.Picker.SelectedItem = row;
            fixture.Picker.SelectionTextMemberPath = nameof(TrackRow.ShortText);
            fixture.Picker.DispatcherQueue.Drain();
            var previous = fixture.Picker.SelectionPresenter!;
            fixture.Open(); fixture.Close();
            fixture.ReplacePopup(310);
            fixture.Picker.DispatcherQueue.Drain();
            Assert.True(ReferenceEquals(row, previous.Content));
            Assert.Equal("原轨道", fixture.Picker.SelectionPresenter!.Content);
            Assert.Null(fixture.Picker.SelectionPresenter.ContentTemplate);
        });
        Case("未设闭合成员时保持框架正文和列表模板", fixture =>
        {
            var row = new TrackRow("完整轨道", null, false);
            fixture.Picker.SelectedItem = row;
            fixture.Picker.ItemTemplate = new object();
            fixture.Picker.ItemTemplateSelector = new object();
            fixture.Open(); fixture.Picker.DispatcherQueue.Drain(); fixture.Close();
            fixture.Picker.DispatcherQueue.Drain();
            var presenter = fixture.Picker.SelectionPresenter!;
            Assert.True(ReferenceEquals(row, presenter.Content));
            Assert.True(ReferenceEquals(fixture.Picker.ItemTemplate, presenter.ContentTemplate));
            Assert.True(ReferenceEquals(fixture.Picker.ItemTemplateSelector, presenter.ContentTemplateSelector));
        });
        Case("成员在模板前设置时由OnApplyTemplate与Loaded只应用当前正文", fixture =>
        {
            var row = new TrackRow("自动 · 中文", null, false) { ShortText = "中文" };
            var picker = new EgPicker
            {
                SelectedItem = row,
                SelectionTextMemberPath = nameof(TrackRow.ShortText)
            };
            picker.ReapplyTemplate(fixture.Popup);
            picker.Load();
            picker.DispatcherQueue.Drain();
            Assert.Equal("中文", picker.SelectionPresenter!.Content);
            Assert.Null(picker.SelectionPresenter.ContentTemplate);
            picker.Unload();
        });
        Case("关闭排队后清除成员不会被旧回调写回短名", fixture =>
        {
            var row = new TrackRow("自动 · English", null, false) { ShortText = "English" };
            fixture.Picker.SelectedItem = row;
            fixture.Picker.SelectionTextMemberPath = nameof(TrackRow.ShortText);
            fixture.Picker.DispatcherQueue.Drain();
            fixture.Open(); fixture.Close();
            fixture.Picker.DisplayMemberPath = nameof(TrackRow.Text);
            fixture.Picker.SelectionTextMemberPath = null;
            fixture.Picker.DispatcherQueue.Drain();
            Assert.Equal(row.Text, fixture.Picker.SelectionPresenter!.Content);
        });
        return TestHarness.Run();
    }

    private static void Case(string name, Action<PickerFixture> action) => Test(name, () =>
    {
        using var fixture = new PickerFixture();
        try { action(fixture); }
        finally { fixture.Dispose(); }
        Assert.True(fixture.Lists.All(list => list.SizeSubscribers == 0), "结束后仍有列表尺寸订阅");
        Assert.True(fixture.Lists.All(list => double.IsPositiveInfinity(list.MaxWidth)), "结束后仍有列表MaxWidth封顶");
    });

    private sealed class PickerFixture : IDisposable
    {
        internal EgPicker Picker { get; } = new();
        internal FrameworkElement List { get; } = new();
        internal Popup Popup { get; }
        internal List<FrameworkElement> Lists { get; } = [];

        internal PickerFixture()
        {
            var parent = new FrameworkElement(); parent.Arrange(600);
            Picker.Parent = parent; Picker.ParentOffsetX = 20;
            List.Arrange(240); Lists.Add(List);
            Popup = new() { Child = List };
            Picker.ReapplyTemplate(Popup); Picker.Load(); Picker.Arrange(180);
            Picker.Measure(580);
        }

        internal void Open() => Picker.IsDropDownOpen = true;
        internal void Close() => Picker.IsDropDownOpen = false;
        internal FrameworkElement ReplacePopup(double width)
        {
            var list = new FrameworkElement(); list.Arrange(width); Lists.Add(list);
            Picker.ReapplyTemplate(new() { Child = list });
            return list;
        }
        public void Dispose() { Close(); Picker.Unload(); Picker.DispatcherQueue.Drain(); }
    }
}
