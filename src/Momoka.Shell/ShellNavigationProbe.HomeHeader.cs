using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeHeaderAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var library = new EmbyItem { Id = "header-library", Name = "电影", Type = EmbyItemType.CollectionFolder };
        fixture.Session.Capture().Bind(library);
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, [library]));
        foreach (var row in fixture.Settings.Ui.HomeRows) row.Visible = row.Key == HomeLayout.Resume;
        var image = await FixtureImageAsync(1280, 720);
        var items = Enumerable.Range(0, 7).Select(index => new EmbyItem
        {
            Id = "header-movie-" + index,
            Name = "离线影片 " + (index + 1),
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        }).ToArray();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = items, TotalRecordCount = items.Length }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        _root.Children.Add(frame);
        HomePage? home = null;
        var readings = new List<string>();
        try
        {
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, [library], null));
            home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.HomeRows.OfType<CardShelf>().Any(shelf => shelf.HomeKey == HomeLayout.Resume));
            await LayoutAsync(home);
            var repeater = Get<ItemsRepeater>(home, "ShelfRepeater");
            var rowElement = (FrameworkElement)repeater.GetOrCreateElement(0);
            await LayoutAsync(rowElement);
            var head = HomeFoldMotion.Find<ShelfHead>(rowElement)!;
            var strip = HomeFoldMotion.Find<ShelfStrip>(rowElement)!;
            var scope = Get<Button>(head, "Scope");
            var title = Get<TextBlock>(head, "TitleText");
            var note = Get<Border>(head, "NoteCapsule");
            var glyph = Get<FontIcon>(head, "OpenGlyph");
            var rule = Get<Border>(head, "Rule");
            var initialTitle = head.Title;
            var initialNote = head.Note;
            var fired = 0;
            head.Invoked += (_, _) => fired++;

            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 760);
                await LayoutAsync(home);
                await Task.Delay(450);
                foreach (var caption in new[] { initialTitle, "离线服务器 · 最近添加 · 纪录片" })
                {
                    head.Title = caption;
                    foreach (var count in new[] { "7 项", "" })
                    {
                        head.Note = count;
                        await LayoutAsync(head);
                        Require(HitsScope(Center(title)) && HitsScope(Center(glyph)), "标题或箭头不能命中导航按钮");
                        if (count.Length > 0) Require(HitsScope(Center(note)), "数量胶囊不能命中导航按钮");
                        var edge = glyph.TransformToVisual(_root).TransformPoint(new Point(glyph.ActualWidth, glyph.ActualHeight / 2));
                        Require(!HitsScope(new Point(edge.X + 20, edge.Y)), "箭头右侧空白仍命中导航按钮");
                        Require(!HitsScope(Center(rule)), "通栏分隔线仍命中导航按钮");
                        Require(scope.ActualWidth < head.ActualWidth - 100, "标题点击范围仍铺满整行");
                        readings.Add($"{width} / {caption} / {count}: 标题按钮 {scope.ActualWidth:0.##}，整行 {head.ActualWidth:0.##}");
                    }
                }
                head.Title = initialTitle;
                head.Note = initialNote;
                await LayoutAsync(head);
                Require(scope.Focus(FocusState.Keyboard), "标题按钮失去键盘焦点入口");
                ((IInvokeProvider)new ButtonAutomationPeer(scope).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => fired == (width == 1280 ? 1 : 2));

                // 故意恢复整行覆盖，确认命中检查能抓住原缺陷，再恢复正式布局。
                var group = (FrameworkElement)VisualTreeHelper.GetParent(scope);
                var alignment = group.HorizontalAlignment;
                try
                {
                    group.HorizontalAlignment = HorizontalAlignment.Stretch;
                    await LayoutAsync(head);
                    var edge = glyph.TransformToVisual(_root).TransformPoint(new Point(glyph.ActualWidth, glyph.ActualHeight / 2));
                    Require(HitsScope(new Point(edge.X + 20, edge.Y)), "负向检查没有检测到整行点击回归");
                }
                finally { group.HorizontalAlignment = alignment; }
                head.Offer = false;
                await LayoutAsync(head);
                Require(!HitsScope(Center(title)), "不可进入的标题仍保留点击面");
                head.Offer = true;
                var trailing = new Button { Content = "独立操作" };
                head.Trailing = trailing;
                await LayoutAsync(head);
                Require(!HitsScope(Center(trailing)) && VisualTreeHelper.FindElementsInHostCoordinates(Center(trailing), _root).Contains(trailing),
                    "标题按钮覆盖了右端独立控件");
                head.Trailing = null;
                await LayoutAsync(head);
                Require(scope.Focus(FocusState.Programmatic), "恢复后标题无法取得焦点");
                await SettleAsync();

                var scroller = Get<ScrollView>(strip, "Scroller");
                scroller.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                await UntilAsync(() => scroller.HorizontalOffset < 1);
                Field(strip, "_hover", true);
                Call(strip, "SyncArrows");
                await Task.Delay(300);
                var next = Get<Button>(strip, "Next");
                Require(next.Visibility == Visibility.Visible && next.ActualWidth == 48 && next.ActualHeight == 48,
                    "翻页按钮隐藏或尺寸改变");
                var circle = Descendants(next).OfType<Ellipse>().Single();
                Require(circle.StrokeThickness == 2 && next.BorderThickness == new Thickness(2), "翻页外圈不是 2 DIP");
                foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
                {
                    Require(VisualStateManager.GoToState(next, state, false), "翻页按钮状态不存在：" + state);
                    await LayoutAsync(next);
                    Require(circle.StrokeThickness == 2 && circle.Fill is not null && circle.Stroke is not null,
                        "翻页按钮状态丢失外圈或底色：" + state);
                }
                VisualStateManager.GoToState(next, "PointerOver", false);
                var card = Descendants(strip).OfType<PosterCard>().First();
                var art = Get<Border>(card, "Art");
                var cardWidth = art.ActualWidth;
                Field(card, "_hovered", true);
                Call(card, "Paint");
                Get<Grid>(card, "Hover").Visibility = Visibility.Visible;
                Get<Grid>(card, "Hover").Opacity = 1;
                Field(head, "_hovered", true);
                Call(head, "PaintUnderline");
                await LayoutAsync(home);
                Require(art.BorderThickness == new Thickness(2) && Math.Abs(art.ActualWidth - cardWidth) < 1,
                    "封面悬停框未加粗或改变外部尺寸");
                await SaveProbeScreenAsync($"home-header-outline-{width}-screen.png");
                Field(head, "_hovered", false);
                Call(head, "PaintUnderline");
                Field(card, "_hovered", false);
                Call(card, "Paint");
                Get<Grid>(card, "Hover").Visibility = Visibility.Collapsed;
                Get<Grid>(card, "Hover").Opacity = 0;
                Require(art.BorderThickness == new Thickness(0), "离开封面后仍有边框");
                ((IInvokeProvider)new ButtonAutomationPeer(next).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => scroller.HorizontalOffset > 1);
                var previous = Get<Button>(strip, "Prev");
                await UntilAsync(() => previous.Visibility == Visibility.Visible);
                await LayoutAsync(previous);
                Require(Descendants(previous).OfType<Ellipse>().Single().StrokeThickness == 2, "左翻页按钮未加粗");
                ((IInvokeProvider)new ButtonAutomationPeer(previous).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => scroller.HorizontalOffset < 1);
                Field(strip, "_hover", false);
                Call(strip, "SyncArrows");
            }
            Require(fixture.Actions.Plays == 0, "标题与边框检查意外触发播放");
            File.WriteAllLines(System.IO.Path.Combine(_options.Paths.LogDirectory, "home-header-hit-area.txt"), readings);

            Point Center(FrameworkElement element) => element.TransformToVisual(_root)
                .TransformPoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
            bool HitsScope(Point point) => VisualTreeHelper.FindElementsInHostCoordinates(point, _root).Contains(scope);
        }
        finally
        {
            home?.Release();
            _root.Children.Remove(frame);
        }
    }
}
