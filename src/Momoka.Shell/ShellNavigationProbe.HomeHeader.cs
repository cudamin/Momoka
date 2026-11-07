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
using Momoka.Theming;
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
        var originalTheme = ThemeHost.Current;
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
                Get<HoverWatch>(strip, "_watch").Leave();
                SetStripHover(true);
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
                var play = Get<Button>(card, "PlayButton");
                var host = Ancestors(card).OfType<Button>().First();
                var cardWidth = art.ActualWidth;
                Field(card, "_hovered", true);
                Call(card, "Paint");
                Get<Grid>(card, "Hover").Visibility = Visibility.Visible;
                Get<Grid>(card, "Hover").Opacity = 1;
                Field(head, "_hovered", true);
                Call(head, "PaintUnderline");
                await LayoutAsync(home);

                Require(art.BorderThickness == new Thickness(0) && Math.Abs(art.ActualWidth - cardWidth) < 1,
                    "封面悬停仍有描边或改变了尺寸");
                Require(host.Focus(FocusState.Keyboard), "主页卡片无法接受键盘焦点");
                await LayoutAsync(home);
                Require(art.BorderThickness == new Thickness(0), "键盘焦点仍让封面出现主题色外圈");
                Require(Colour(host.FocusVisualPrimaryBrush) == Role("EgTextBrush")
                    && Colour(host.FocusVisualSecondaryBrush) == Role("EgWindowBrush"), "主页卡片焦点提示不是中性色");
                Require(Colour(play.Background) == Role("EgSurfaceHoverBrush"), "封面悬停把播放键提前变成强调色");
                await SaveProbeScreenAsync($"home-card-neutral-{width}-screen.png");
                if (width == 1280)
                {
                    foreach (var theme in UiThemes.All)
                    {
                        ThemeHost.Apply(theme);
                        await LayoutAsync(home);
                        Require(VisualStateManager.GoToState(play, "Normal", false), "播放按钮缺少默认状态");
                        await LayoutAsync(play);
                        Require(Colour(Presenter(play)?.Background) == Role("EgSurfaceHoverBrush"), "播放键默认不是中性面色");
                        await SaveProbeScreenAsync($"home-card-play-normal-{theme.Id}-screen.png");
                        Require(VisualStateManager.GoToState(play, "PointerOver", false), "播放按钮缺少指针状态");
                        await LayoutAsync(play);
                        Require(Colour(Presenter(play)?.Background) == Role("EgAccentBrush"), "播放键指针态不是主题色");
                        Require(Colour(Presenter(play)?.Foreground) == Role("EgTextOnAccentBrush"), "播放键指针态文字对比色错误");
                        await SaveProbeScreenAsync($"home-card-play-hover-{theme.Id}-screen.png");
                        Require(VisualStateManager.GoToState(play, "Pressed", false), "播放按钮缺少按下状态");
                        await LayoutAsync(play);
                        Require(Colour(Presenter(play)?.Background) == Role("EgAccentPressedBrush"), "播放键按下状态没有使用主题色");
                        VisualStateManager.GoToState(play, "Normal", false);
                        await LayoutAsync(play);
                        Require(Colour(Presenter(play)?.Background) == Role("EgSurfaceHoverBrush"), "播放键离开后未恢复中性色");
                        var check = ShellSelfCheck.ReadCards(home);
                        Require(check is { Ok: true }, "实际封面自检不通过：" + check?.Detail);
                    }
                    ThemeHost.Apply(originalTheme);
                    await LayoutAsync(home);
                    art.BorderThickness = new Thickness(2);
                    try { Require(ShellSelfCheck.ReadCards(home) is { Ok: false }, "实际封面自检没有拒绝重新出现的外圈"); }
                    finally { art.ClearValue(Border.BorderThicknessProperty); }
                    Require(ShellSelfCheck.ReadCards(home) is { Ok: true }, "移除注入外圈后自检未恢复");
                }
                Field(head, "_hovered", false);
                Call(head, "PaintUnderline");
                Field(card, "_hovered", false);
                Call(card, "Paint");
                Get<Grid>(card, "Hover").Visibility = Visibility.Collapsed;
                Get<Grid>(card, "Hover").Opacity = 0;
                Require(art.BorderThickness == new Thickness(0), "离开封面后仍有边框");
                SetStripHover(true);
                ((IInvokeProvider)new ButtonAutomationPeer(next).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => scroller.HorizontalOffset > 1);
                var previous = Get<Button>(strip, "Prev");
                // 本检查模拟悬停，不占用真实鼠标；先确认离开会收起，再持有模拟前件等动画完成。
                await UntilAsync(() =>
                {
                    SetStripHover(false);
                    return previous.Visibility == Visibility.Collapsed && next.Visibility == Visibility.Collapsed;
                });
                try
                {
                    await UntilAsync(() =>
                    {
                        SetStripHover(true);
                        return previous.Visibility == Visibility.Visible && previous.IsHitTestVisible && previous.Opacity >= 0.99;
                    });
                }
                finally
                {
                    await File.AppendAllTextAsync(System.IO.Path.Combine(_options.Paths.LogDirectory, "home-arrow-state.txt"),
                        $"width={width}; hover={Get<bool>(strip, "_hover")}; offset={scroller.HorizontalOffset}; scrollable={scroller.ScrollableWidth}; "
                        + $"prev={previous.Visibility}/{previous.IsHitTestVisible}/{previous.Opacity}; requested={strip.Requested}\n");
                }
                await LayoutAsync(previous);
                Require(Descendants(previous).OfType<Ellipse>().Single().StrokeThickness == 2, "左翻页按钮未加粗");
                ((IInvokeProvider)new ButtonAutomationPeer(previous).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => scroller.HorizontalOffset < 1);
                SetStripHover(false);
            }
            Require(fixture.Actions.Plays == 0, "标题与边框检查意外触发播放");
            File.WriteAllLines(System.IO.Path.Combine(_options.Paths.LogDirectory, "home-header-hit-area.txt"), readings);

            void SetStripHover(bool visible)
            {
                // 真实 HoverWatch 可能在布局/主题切换时清掉反射前件；这里只控制本夹具的模拟状态。
                Field(strip, "_hover", visible);
                Call(strip, "SyncArrows");
            }

            Point Center(FrameworkElement element) => element.TransformToVisual(_root)
                .TransformPoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
            bool HitsScope(Point point) => VisualTreeHelper.FindElementsInHostCoordinates(point, _root).Contains(scope);

            // 角色表里那一支现在是什么色。ThemeHost 换主题改的是同一支画刷的 Color，所以每次现问。
            Windows.UI.Color Role(string key) => Colour(Application.Current.Resources[key] as Brush);

            static Windows.UI.Color Colour(Brush? brush) =>
                brush is SolidColorBrush solid ? solid.Color : default;

            static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
            {
                for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                    yield return parent;
            }

            static ContentPresenter? Presenter(Button button)
            {
                button.ApplyTemplate();
                return VisualTreeHelper.GetChildrenCount(button) > 0
                    ? VisualTreeHelper.GetChild(button, 0) as ContentPresenter
                    : null;
            }
        }
        finally
        {
            home?.Release();
            _root.Children.Remove(frame);
            ThemeHost.Apply(originalTheme);
        }
    }
}
