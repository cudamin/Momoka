using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task FilterValuesAsync()
    {
        var savedTheme = ThemeHost.Current.Id;
        var panel = new FilterPanel();
        var holder = new Border { Child = panel, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _root.Children.Add(holder);
        try
        {
            var selection = new ItemFilters();
            selection.Genres.Add("动画");
            var changes = 0;
            panel.Changed += (_, _) => changes++;
            var reads = 0;
            panel.Open(selection, "mixed", new Version(4, 10, 1), (_, _) =>
            {
                reads++;
                return Task.FromResult<IReadOnlyList<string>>(Enumerable.Range(1997, 30).Reverse().Select(value => value.ToString()).ToArray());
            });
            foreach (var size in new[] { (Width: 1100, Height: 900), (Width: 760, Height: 700), (Width: 360, Height: 500) })
            {
                ResizeProbeClient(size.Width, size.Height);
                await LayoutAsync(_root);
                holder.Width = _root.ActualWidth - 48;
                holder.Height = _root.ActualHeight - 48;
                await LayoutAsync(panel);
                var row = panel.ViewModel.Rows.OfType<FilterValuesRow>().Single(value => value.Field.ListKey == EmbyFilterBy.Keys.Years);
                var button = Descendants(panel).OfType<Button>().Single(value => ReferenceEquals(value.DataContext, row));
                button.StartBringIntoView();
                await LayoutAsync(panel);
                var flyout = (Flyout)button.Flyout;
                flyout.ShowAt(button);
                try
                {
                    var surface = (Grid)flyout.Content;
                    await LayoutAsync(surface);
                    FlyoutPresenter? presenter = null;
                    // Content is wrapped by the platform template; locate the actual popup presenter.
                    for (DependencyObject? parent = surface; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                        if (parent is FlyoutPresenter found) { presenter = found; break; }
                    Require(presenter is not null, "没有找到当前下拉表面");
                    Require(Math.Abs(presenter!.ActualWidth - button.ActualWidth) <= 2,
                        $"{size.Width}：下拉宽 {presenter.ActualWidth}，入口宽 {button.ActualWidth}，读取={reads}，Open={row.IsOpen}，样式setter={flyout.FlyoutPresenterStyle?.Setters.Count}");
                    var scroll = Descendants(surface).OfType<ScrollViewer>().Single(value => value.Name == "FilterValuesScroll");
                    var footer = Descendants(surface).OfType<HyperlinkButton>().Single();
                    var options = Descendants(surface).OfType<CheckBox>().ToArray();
                    await SaveProbeScreenAsync($"filter-values-{size.Width}-opened-screen.png");
                    Require(options.Length == 30 && options.All(value => value.ActualWidth > surface.ActualWidth - 24),
                        $"复选行没有填满弹层：数量{options.Length}，VM={row.Values.Count}，行宽{string.Join(',', options.Take(3).Select(value => value.ActualWidth))}，表面{surface.ActualWidth}");
                    Require(scroll.ScrollableHeight > 0 && footer.ActualHeight >= 32, "滚动区域或全选入口不可达");
                    var toggle = FrameworkElementAutomationPeer.CreatePeerForElement(options[0])?.GetPattern(PatternInterface.Toggle) as IToggleProvider;
                    Require(toggle is not null, "复选行没有 UIA Toggle 入口");
                    var before = changes;
                    toggle!.Toggle();
                    await LayoutAsync(surface);
                    Require(selection.Years.SequenceEqual(new[] { "2026" }) && changes == before + 1, "勾选没有单次写入当前年份");
                    await SaveProbeScreenAsync($"filter-values-{size.Width}-screen.png");
                    scroll.ChangeView(null, double.MaxValue, null, disableAnimation: true);
                    await LayoutAsync(surface);
                    Require(scroll.VerticalOffset > 0 && footer.ActualHeight >= 32, "滚到底后全选消失");
                    if (size.Width == 1100)
                        foreach (var theme in UiThemes.All)
                        {
                            ThemeHost.Apply(theme.Id);
                            scroll.ChangeView(null, 0, null, disableAnimation: true);
                            var label = Descendants(options[0]).OfType<TextBlock>().Single(value => value.Text == "2026");
                            Require(label.VerticalAlignment == VerticalAlignment.Center && options[0].VerticalContentAlignment == VerticalAlignment.Center,
                                "年份文字和复选框没有垂直居中");
                            var rectangle = Descendants(options[0]).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>().Single(value => value.Name == "NormalRectangle");
                            Require(BrushColor(rectangle.Fill) == ThemeHost.ToColor(theme.Colors.Accent), "勾选框没有跟随当前主题：" + theme.Id);
                            Require(BrushColor(label.Foreground) == ThemeHost.ToColor(theme.Colors.Text), "年份文字没有保持正文颜色");
                            await LayoutAsync(surface);
                            await SaveProbeScreenAsync($"filter-values-{theme.Id}-screen.png");
                        }
                    var invoke = FrameworkElementAutomationPeer.CreatePeerForElement(footer)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                    Require(invoke is not null, "全选没有 UIA Invoke 入口");
                    invoke!.Invoke();
                    await LayoutAsync(panel);
                    Require(selection.Years.Count == 0 && selection.Genres.SequenceEqual(new[] { "动画" }) && changes == before + 2,
                        "全选清掉其它字段或重复刷新");
                    Require(!row.IsOpen, "全选后列表没有关闭");
                    await UntilAsync(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                        .Any(popup => ReferenceEquals(popup.Child, presenter)));
                }
                finally { flyout.Hide(); }
            }
            Require(reads == 1, "重开列表重复请求");
        }
        finally
        {
            panel.Dismiss();
            _root.Children.Remove(holder);
            ThemeHost.Apply(savedTheme);
        }
    }
}
