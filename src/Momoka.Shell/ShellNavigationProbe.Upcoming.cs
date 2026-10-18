using System.Net;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LibraryUpcomingAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var poster = await FixtureImageAsync();
        var waiting = Pending<HttpResponseMessage>();
        var first = UpcomingFixtureItems();
        fixture.Transport.Reply = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            if (request.RequestUri.AbsolutePath.EndsWith("/Shows/Upcoming", StringComparison.Ordinal)) return waiting.Task;
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        _root.Children.Add(frame);
        LibraryPage? page = null;
        try
        {
            var request = new LibraryRequest { Services = fixture.Services, Title = "电视节目", ParentId = "library-a", CollectionType = "tvshows" };
            frame.Navigate(typeof(LibraryPage), request);
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            var tabs = Get<WrapRow>(page, "LibrarySections");
            var upcomingTab = tabs.Children.OfType<ToggleButton>().Single(tab => (LibrarySection)tab.Tag == LibrarySection.Upcoming);
            ((IToggleProvider)new ToggleButtonAutomationPeer(upcomingTab).GetPattern(PatternInterface.Toggle)).Toggle();
            await UntilAsync(() => page.ViewModel.UpcomingLoading);
            ResizeProbeClient(1100, 800);
            await LayoutAsync(page);
            var ring = page.UpcomingLoadingRing;
            Require(ring.IsActive && ring.IsIndeterminate && ring.ActualWidth >= 40,
                "预告未加载时没有运行中的加载动画");
            Require(Get<StackPanel>(page, "UpcomingLoadingPanel").Visibility == Visibility.Visible
                && page.ViewModel.Cards.Count == 0 && page.ViewModel.UpcomingDays.Count == 0
                && page.TableScroll.Visibility == Visibility.Collapsed, "预告等待时闪出旧列表或空态");
            Require(Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Collapsed
                && page.ViewModel.EmptyVisibility == Visibility.Collapsed, "预告等待时仍有工具栏或空态");
            await SaveProbeScreenAsync("library-tvshows-upcoming-loading-screen.png");
            waiting.SetResult(Json(new { Items = first, TotalRecordCount = first.Length }));
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            await LayoutAsync(page);
            Require(!ring.IsActive && Get<StackPanel>(page, "UpcomingLoadingPanel").Visibility == Visibility.Collapsed,
                "预告完成后加载动画没有停止");
            Require(page.ViewModel.UpcomingDays.Count == 4 && page.ViewModel.UpcomingDays[0].Title == "昨天"
                && page.ViewModel.UpcomingDays[1].Title == DateOnly.FromDateTime(DateTime.Now)
                    .ToString("M月d日dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN")), "预告没有按播出日期分组");
            Require(page.ViewModel.UpcomingDays[1].Cards.Count == 4, "同一天的预告没有排在同一组");
            Require(page.ViewModel.Cards[0].Title == "JOJO的奇妙冒险"
                && page.ViewModel.Cards[0].Subtitle == "S6:E5 - 来自远方的恐怖分子"
                && page.ViewModel.Cards[0].Item.Name == "来自远方的恐怖分子", "预告两行文案不对或改坏真实条目");
            Require(page.ViewModel.AlphaVisibility == Visibility.Collapsed && page.ViewModel.SettingsVisibility == Visibility.Collapsed,
                "预告页仍有不生效的字母跳转或设置");
            await UntilAsync(() => page.ViewModel.UpcomingDays[0].Cards.All(card => card.Poster is not null));
            Require(fixture.Transport.Requests.Any(value => value.Contains("/Items/upcoming-series/Images/Thumb", StringComparison.Ordinal)),
                "无单集图片时没有读取真正所属剧集的缩略图");
            var inherited = page.ViewModel.Cards.Single(card => card.Item.Id == "upcoming-b");
            Require(inherited.PlayVisibility == Visibility.Collapsed, "虚拟预告单集仍然显示播放按钮");
            foreach (var width in new[] { 1100, 760, 360 })
            {
                ResizeProbeClient(width, 800);
                await LayoutAsync(page);
                await UntilAsync(() => Descendants(page.UpcomingGroups).OfType<PosterCard>().Any());
                var title = Get<TextBlock>(page, "LibraryTitle");
                Require(title.Text == "电视节目" && title.Visibility == Visibility.Visible, "预告页左上角没有库名");
                var surface = Get<Border>(page, "LibrarySectionsSurface");
                var header = surface.TransformToVisual(page).TransformBounds(new(0, 0, surface.ActualWidth, surface.ActualHeight));
                Require(header.Left >= -1 && header.Right <= page.ActualWidth + 1
                    && Math.Abs(header.Left + header.Width / 2 - page.ActualWidth / 2) <= 2, "预告分类栏不居中或被裁切");
                await SaveProbeScreenAsync($"library-tvshows-upcoming-{width}-screen.png");
                var visible = 0;
                for (var groupIndex = 0; groupIndex < page.ViewModel.UpcomingDays.Count; groupIndex++)
                {
                    if (page.UpcomingGroups.TryGetElement(groupIndex) is not FrameworkElement group) continue;
                    var repeater = Descendants(group).OfType<ItemsRepeater>().First();
                    for (var cardIndex = 0; cardIndex < page.ViewModel.UpcomingDays[groupIndex].Cards.Count; cardIndex++)
                    {
                        if (repeater.TryGetElement(cardIndex) is not Button { Content: PosterCard card }) continue;
                        var box = card.TransformToVisual(page).TransformBounds(new(0, 0, card.ActualWidth, card.ActualHeight));
                        File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "upcoming-layout.txt"),
                            $"width={width}, page={page.ActualWidth:0}, viewport={page.UpcomingScroll.ActualWidth:0}, card={card.CardWidth:0}, bounds={box}, id={card.Card?.Item.Id}\n");
                        var viewport = page.UpcomingScroll.TransformToVisual(page)
                            .TransformBounds(new(0, 0, page.UpcomingScroll.ActualWidth, page.UpcomingScroll.ActualHeight));
                        if (box.Bottom <= viewport.Top || box.Top >= viewport.Bottom) continue;
                        visible++;
                        RequireUpcomingCardFits(page, card, width);
                        Require(Math.Abs(card.PosterHeight - Math.Round(card.CardWidth * 9 / 16)) <= 1, "预告卡片不是横向 16:9");
                        Require(Get<TextBlock>(card, "TitleLine").TextAlignment == TextAlignment.Center
                            && Get<TextBlock>(card, "SubLine").TextAlignment == TextAlignment.Center, "预告卡片文字不居中");
                    }
                }
                Require(visible > 0, "预告页没有真正可见的卡片");
            }
            ResizeProbeClient(1100, 800);
            await LayoutAsync(page);
            var visibleCard = Descendants(page.UpcomingGroups).OfType<PosterCard>()
                .First(card => card.TransformToVisual(page).TransformBounds(new(0, 0, card.ActualWidth, card.ActualHeight)).Left >= 0);
            var cardWidth = visibleCard.CardWidth;
            try
            {
                visibleCard.CardWidth = page.ActualWidth * 2;
                await LayoutAsync(page);
                var rejected = false;
                try { RequireUpcomingCardFits(page, visibleCard, 1100); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "预告布局检查没有拒绝真正越界的卡片");
            }
            finally
            {
                visibleCard.CardWidth = cardWidth;
                page.UpdateLayout();
            }
            page.UpcomingScroll.ChangeView(null, double.MaxValue, null, disableAnimation: true);
            await LayoutAsync(page);
            Require(page.UpcomingScroll.VerticalOffset > 0, "日期分组预告不能竖向滚动");
            await SaveProbeScreenAsync("library-tvshows-upcoming-bottom-screen.png");
            var offset = page.UpcomingScroll.VerticalOffset;
            page.Release();
            Require(Math.Abs(request.ScrollOffset - offset) <= 1 && request.LoadedCount == first.Length,
                "预告离页没有保存滚动位置和已加载数量");
            fixture.Transport.Reply = (httpRequest, _) => httpRequest.RequestUri!.AbsolutePath.EndsWith("/Shows/Upcoming", StringComparison.Ordinal)
                ? Task.FromResult(Json(new { Items = first, TotalRecordCount = first.Length })) : Task.FromResult(FakeTransport.Standard(httpRequest));
            frame.Navigate(typeof(LibraryPage), request);
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            await LayoutAsync(page);
            Require(Math.Abs(page.UpcomingScroll.VerticalOffset - offset) <= 2, "返回预告页没有恢复滚动位置");

            var day = page.ViewModel.UpcomingDays[0];
            var duplicate = first[0];
            var extra = new EmbyItem
            {
                Id = "upcoming-extra",
                Name = "同日追加",
                SeriesName = "追加剧集",
                Type = EmbyItemType.Episode,
                PremiereDate = first[0].PremiereDate,
                ParentIndexNumber = 1,
                IndexNumber = 2
            };
            fixture.Transport.Reply = (httpRequest, _) => httpRequest.RequestUri!.AbsolutePath.EndsWith("/Shows/Upcoming", StringComparison.Ordinal)
                ? Task.FromResult(Json(Query(httpRequest, "StartIndex") == "0"
                    ? new { Items = first, TotalRecordCount = first.Length + 2 }
                    : new { Items = new[] { duplicate, extra }, TotalRecordCount = first.Length + 2 }))
                : Task.FromResult(FakeTransport.Standard(httpRequest));
            await page.ViewModel.ReloadAsync();
            day = page.ViewModel.UpcomingDays[0];
            var requested = fixture.Transport.Requests.Count;
            await page.ViewModel.LoadUntilAsync(first.Length);
            Require(ReferenceEquals(day, page.ViewModel.UpcomingDays[0]) && day.Cards.Count == 3,
                "跨页同日预告拆成两组或重建日期组");
            Require(page.ViewModel.Cards.Count == first.Length + 1 && !page.ViewModel.HasMore,
                "重复分页条目导致丢失、重复或不断请求");
            Require(fixture.Transport.Requests.Skip(requested).Any(value => value.Contains($"StartIndex={first.Length}", StringComparison.Ordinal)),
                "预告分页起点没有跟随服务器已返回条数");

            waiting = Pending<HttpResponseMessage>();
            fixture.Transport.Reply = (httpRequest, _) => httpRequest.RequestUri!.AbsolutePath.EndsWith("/Shows/Upcoming", StringComparison.Ordinal)
                ? waiting.Task : Task.FromResult(FakeTransport.Standard(httpRequest));
            var late = page.ViewModel.ReloadAsync();
            Require(page.UpcomingLoadingRing.IsActive && page.ViewModel.UpcomingLoading, "预告刷新没有进入加载状态");
            await page.ViewModel.ChooseSectionAsync(LibrarySection.Items);
            waiting.SetResult(Json(new { Items = first, TotalRecordCount = first.Length }));
            await late;
            Require(page.ViewModel.Section == LibrarySection.Items && page.ViewModel.UpcomingDays.Count == 0
                && !page.UpcomingLoadingRing.IsActive, "迟到预告覆盖节目页或留下加载动画");

            fixture.Transport.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            await page.ViewModel.ChooseSectionAsync(LibrarySection.Upcoming);
            await LayoutAsync(page);
            Require(!page.UpcomingLoadingRing.IsActive && page.ViewModel.NoticeOpen
                && Get<Button>(page, "UpcomingRetryButton").Visibility == Visibility.Visible, "预告失败仍转圈或不能重试");
            await SaveProbeScreenAsync("library-tvshows-upcoming-error-screen.png");
            fixture.Transport.Reply = (_, _) => Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            var retry = Get<Button>(page, "UpcomingRetryButton");
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(retry)!.GetPattern(PatternInterface.Invoke)!).Invoke();
            await UntilAsync(() => !page.ViewModel.Busy && !page.ViewModel.NoticeOpen);
            await LayoutAsync(page);
            Require(page.ViewModel.EmptyVisibility == Visibility.Visible && page.ViewModel.EmptyNotice.Contains("暂无即将播出", StringComparison.Ordinal)
                && retry.Visibility == Visibility.Collapsed && !page.UpcomingLoadingRing.IsActive, "预告空态不明确或重试未恢复");
            await SaveProbeScreenAsync("library-tvshows-upcoming-empty-screen.png");
            Require(fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)), "预告验证产生写请求");
        }
        finally
        {
            waiting.TrySetResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            page?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }

    private static void RequireUpcomingCardFits(LibraryPage page, PosterCard card, int width)
    {
        var art = Get<Border>(card, "Art");
        var box = art.TransformToVisual(page).TransformBounds(new(0, 0, art.ActualWidth, art.ActualHeight));
        Require(box.Left >= -1 && box.Right <= page.ActualWidth + 1,
            $"预告卡片被窗口裁切（宽 {width}，页面 {page.ActualWidth:0}，卡片 {card.CardWidth:0}，区域 {box}）");
    }

    private static EmbyItem[] UpcomingFixtureItems()
    {
        var today = DateTimeOffset.Now.Date;
        EmbyItem EpisodeAt(string id, string series, string title, int day, int season, int number, bool inherited = false)
        {
            var item = new EmbyItem
            {
                Id = id,
                Name = title,
                SeriesName = series,
                SeriesId = "upcoming-series",
                Type = EmbyItemType.Episode,
                ParentIndexNumber = season,
                IndexNumber = number,
                PremiereDate = new DateTimeOffset(today.AddDays(day).AddHours(12)),
                LocationType = inherited ? "Virtual" : "FileSystem"
            };
            if (inherited) item.SeriesThumbImageTag = "upcoming-fixture";
            else item.ImageTags["Primary"] = "upcoming-fixture";
            return item;
        }
        return
        [
            EpisodeAt("upcoming-a", "JOJO的奇妙冒险", "来自远方的恐怖分子", -1, 6, 5),
            EpisodeAt("upcoming-b", "药屋少女的呢喃", "如果没有面包", -1, 1, 51, true),
            EpisodeAt("upcoming-c", "青之箱", "只有一件事哦", 0, 1, 28),
            EpisodeAt("upcoming-d", "魔法☆探险家", "小黄油不得不品的桥段", 0, 1, 3),
            EpisodeAt("upcoming-e", "牧神记", "第 105 集", 0, 1, 105),
            EpisodeAt("upcoming-f", "学生会也有洞！", "《太好了呢》《泳池大扫除的泳装回》", 0, 1, 3),
            EpisodeAt("upcoming-g", "凡人修仙传", "慕兰之战20", 1, 1, 196),
            EpisodeAt("upcoming-h", "未来节目", "新的一集", 2, 1, 1)
        ];
    }
}
