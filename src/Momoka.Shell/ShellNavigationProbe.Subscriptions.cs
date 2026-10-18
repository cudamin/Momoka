using System.Net;
using System.Text.Json.Nodes;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotSubscriptionsAsync()
    {
        using var server = new SubscriptionProbeServer();
        server.Poster = await FixtureImageAsync();
        var (settings, service) = SubscriptionService(server);
        var view = new MoviePilotSubscriptionsView();
        var scroll = new ScrollViewer { Content = view, Padding = new Thickness(28) };
        var host = new Page { Content = scroll, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ThemeHost.ToColor(ThemeHost.Current.Colors.Window)) };
        _root.Children.Add(host);
        MoviePilotSubscription? opened = null;

        // 「开应用／返回主页闪一下空板块」那一条（2026-10-08）：整块板块只在真有订阅时才立着 ——
        // 从前它认的是「MoviePilot 开着」，Attach 和 ReloadAsync 都在数据到达之前就把它摆上屏，屏上先出两块
        // 「暂无…」的空牌子、等异步请求回来才填卡片。
        //
        // 这一条用一块单独的视图模型量，而且**把那一趟列表请求扣在手里**（PendingList）：响应没放行之前，屏上
        // 该是什么、放开之后又该是什么，两头都读得到，不靠「哪一拍先跑」的运气。控件的 x:Bind 要等上场才求值，
        // 所以这里量的是 Visible 本身（绑定源），控件外那层 StackPanel 只是它的落点。
        await SubscriptionSectionVisibilityAsync(server);

        view.Attach(service, subscription => opened = subscription);
        try
        {
            var vm = view.ViewModel;
            await UntilAsync(() => vm.IsReady);
            Require(vm.Movies.Count == 3 && vm.Series.Count == 2, "订阅没有分成电影和电视剧：" + vm.NoticeMessage);
            Require(((StackPanel)view.Content).Visibility == Visibility.Visible, "订阅读回来了，板块却没有出现");
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeInspect(width, 1000); await LayoutAsync(scroll);
                var covers = Descendants(view).OfType<Button>().Where(button => button.Name == "SubscriptionCover").ToArray();
                Require(covers.Length >= 2 && covers.All(button => button.Tag is MoviePilotSubscriptionCard), "模板封面没有绑定真实订阅或句柄");
                Require(Math.Abs(covers[0].ActualWidth - vm.Movies[0].CardWidth) < 1, "封面按钮未占满卡片宽度");
                await UntilAsync(() => vm.Movies[0].Poster is not null);
                InvokeSubscriptionButton(covers[0]);
                Require(opened?.Id == 1, "封面点击没有打开媒体详情");
                await SaveSettledFrameAsync(host, $"mp-subscriptions-home-{width}.png");
            }
            var poster = Descendants(view).OfType<MoviePilotSubscriptionPoster>().First();
            var more = Get<Button>(poster, "SubscriptionMore");
            poster.SetHovered(false);
            Require(more.Visibility == Visibility.Collapsed, "未悬停封面时订阅管理按钮仍然显示");
            poster.SetHovered(true); await LayoutAsync(poster);
            var morePosition = more.TransformToVisual(poster).TransformPoint(default);
            Require(more.Visibility == Visibility.Visible && more.ActualWidth == 32 && more.ActualHeight == 32
                && Math.Abs(poster.ActualWidth - morePosition.X - more.ActualWidth - 7) < 1
                && Math.Abs(poster.ActualHeight - morePosition.Y - more.ActualHeight - 5) < 1, "更多按钮没有与媒体库封面右下角对齐");
            var seriesPoster = Descendants(view).OfType<MoviePilotSubscriptionPoster>().First(item => item.Card?.Subscription.IsSeries == true);
            seriesPoster.SetHovered(true); await LayoutAsync(seriesPoster);
            var progress = Get<Border>(seriesPoster, "SubscriptionProgress");
            var progressPosition = progress.TransformToVisual(seriesPoster).TransformPoint(default);
            var seriesMore = Get<Button>(seriesPoster, "SubscriptionMore");
            Require(progress.Visibility == Visibility.Visible && progressPosition.X >= 0
                && progressPosition.Y >= seriesPoster.ActualHeight - progress.ActualHeight - 9
                && progressPosition.X + progress.ActualWidth < seriesMore.TransformToVisual(seriesPoster).TransformPoint(default).X,
                "订阅进度没有放在封面内部左下角，或与更多按钮重叠");
            Require(Descendants(progress).OfType<TextBlock>().Single().Text == seriesPoster.Card!.ProgressCount,
                "封面集数丢失或仍带有订阅进度标签");
            Require(!Descendants(view).OfType<TextBlock>().Any(text => text.Text is "订阅中" or "入库情况见文件统计" or "订阅进度"), "旧状态标签或说明仍然显示");
            Get<Button>(view, "RefreshSubscriptions").Focus(FocusState.Programmatic);
            poster.SetHovered(false); await Task.Delay(250);
            poster.SetHovered(true); await Task.Delay(80);
            if (HomeMotion.AnimationsEnabled) Require(poster.Zoom > 1 && poster.Zoom <= 1.045, "悬停未产生图片推近动画");
            await SaveSettledFrameAsync(host, "mp-subscription-hover.png");
            poster.SetHovered(false); await Task.Delay(250);
            Require(Math.Abs(poster.Zoom - 1) < 0.001 && more.Visibility == Visibility.Collapsed, "鼠标离开后缩放没有复原或管理按钮没有隐藏");
            seriesPoster.SetHovered(false);
            var first = vm.Movies[0];
            await vm.ReloadAsync();
            Require(ReferenceEquals(first, vm.Movies[0]), "刷新重建卡片而丢失行状态");
            vm.UseConfirm((_, _, _) => Task.FromResult(false));
            await vm.ManageAsync(first, MoviePilotSubscriptionAction.Delete);
            Require(server.Writes.Count == 0 && vm.Movies.Count == 3, "取消确认仍然取消订阅");
            vm.UseConfirm((_, _, _) => Task.FromResult(true));
            await vm.ManageAsync(first, MoviePilotSubscriptionAction.Pause);
            Require(first.Subscription.Paused, "暂停后状态没有刷新");
            await vm.ManageAsync(first, MoviePilotSubscriptionAction.Resume);
            Require(!first.Subscription.Paused, "暂停后不能恢复");
            await vm.ManageAsync(first, MoviePilotSubscriptionAction.Search);
            await vm.ManageAsync(first, MoviePilotSubscriptionAction.Reset);
            Require(server.Writes.Count == 4 && server.Writes.Any(write => write == "POST /api/v1/subscribe/search/1"), "菜单动作没有对应实际请求");
            await vm.ManageAsync(first, MoviePilotSubscriptionAction.Delete);
            Require(vm.Movies.Count == 2 && !vm.Owns(first), "取消订阅后旧卡片仍可操作");
            settings.MoviePilot.Enabled = false;
            await vm.ReloadAsync();
            Require(vm.Visible == Visibility.Collapsed && vm.Movies.Count == 0, "关闭集成后仍显示旧订阅");
        }
        finally { view.Release(); _root.Children.Remove(host); ResizeInspect(1420, 980); }
    }

    private async Task MoviePilotSubscriptionDetailAsync()
    {
        using var server = new SubscriptionProbeServer();
        server.Poster = await FixtureImageAsync();
        // 下方三段（演职员／相关推荐／相似作品）这一轮换了版式，夹具给它们内容 —— 默认那三条 tmdb 接口回的是
        // 空数组，三段都收着，屏上什么都量不到。
        server.Related = true;
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Transport.Reply = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/Episodes", StringComparison.Ordinal)
            ? Json(new { Items = new[] { new EmbyItem { Id = "play-1", Type = EmbyItemType.Episode, ParentIndexNumber = 1, IndexNumber = 1, SeriesId = "linked-show", Path = "/library/01.mkv" } } })
            : Json(new { Items = new[] { new EmbyItem { Id = "linked-show", Type = EmbyItemType.Series, ProviderIds = new() { ["Tmdb"] = "4" } } } }));
        var played = new List<MoviePilotPlaybackTarget>();
        var searched = new List<string>();
        // 下方三段的读数（板底、带高、内层滚动面）—— 断言只说出「不对」，这几行说出屏上到底是什么。
        var relatedReadings = new List<string>();
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        page.Attach(new(service, subscription, fixture.Session.Capture(),
            target => { played.Add(target); return Task.CompletedTask; }, keyword => searched.Add(keyword)));
        try
        {
            var vm = page.ViewModel;
            await UntilAsync(() => vm.IsReady && page.MediaViewModel.IsReady);
            Require(page.MediaViewModel.Detail is not null, "媒体详情读取失败：" + page.MediaViewModel.NoticeMessage);
            Require(vm.Episodes.Count == 4 && vm.Episodes.Count(episode => episode.InLibrary) == 1, "入库和下载被混为一谈：" + vm.NoticeMessage);
            Require(vm.Episodes[1].Status == "已下载 · 待入库", "待入库集状态丢失");
            Require(vm.Episodes[0].CanPlay && !vm.Episodes[1].CanPlay, "已入库没有播放入口或缺集允许播放");
            // 播出日期来自 TMDB 季集接口；「搜索这一集资源」只给已播出的集（用户令 2026-10-10）。
            Require(vm.Episodes[0].AirDateLabel == "播出 2026-01-01" && vm.Episodes[0].CanSearch,
                "已播出集没有显示播出日期或搜索入口：" + vm.Episodes[0].AirDateLabel);
            Require(vm.Episodes[2].AirDateLabel == "待播出 2999-01-01" && vm.Episodes[2].SearchVisibility == Visibility.Collapsed,
                "未播出集不该给搜索入口：" + vm.Episodes[2].AirDateLabel);
            Require(vm.Episodes[3].AirDateLabel.Length == 0 && vm.Episodes[3].SearchVisibility == Visibility.Collapsed,
                "没有播出日期的集不该显示日期或搜索入口");
            // 文件统计住进了订阅季的折叠栏（用户令 2026-10-12）：订阅季自动展开、电影用的独立面板收起、
            // 通用集列表让位。折叠栏里的块只有等行实现并展开之后才存在，先把季列表滚进视野让它自然实现，
            // 再按「唯一展开的那一个」找到订阅季 —— 不用 GetOrCreateElement，避免与实现时机赛跑。
            var seasonsView = Get<MoviePilotMediaSeasonsView>(page, "MediaSeasons");
            seasonsView.StartBringIntoView();
            await LayoutAsync(page);
            var host = Descendants(seasonsView).OfType<Expander>().First(expander => expander.Name == "ExpandMediaSeason" && expander.IsExpanded);
            // 电影那块独立的「入库与缺集」面板已按用户令（2026-10-12）整块删除：这一页上只该有折叠栏里
            // 这一份统计，页面上不再出现第二个「入库与缺集」（电影页上的显隐见 MoviePilotMovieSubscriptionDetailAsync）。
            Require(!Descendants(page).OfType<TextBlock>().Any(text => text.Text == "入库与缺集"), "页面上还立着独立的「入库与缺集」面板");
            Require(Descendants(host).OfType<ItemsRepeater>().Any(repeater => repeater.Name == "MediaEpisodeList" && repeater.Visibility == Visibility.Collapsed),
                "订阅季的通用集列表没有让位");
            Require(Descendants((Grid)host.Header).OfType<TextBlock>().Any(text => text.Text == vm.FileSummary)
                && Descendants(host).OfType<TextBlock>().Count(text => text.Text.Contains("已入库 1 / 4 项", StringComparison.Ordinal)) == 1,
                "入库摘要没有统一放在季标题，或展开后重复显示");
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeInspect(width, 1000);
                // 窗口尺寸是异步生效的，而自适应版式跟着窗口宽度走 —— 不等它落下来，下面读到的还是上一档。
                await UntilAsync(() => page.ActualWidth <= width + 2);
                await LayoutAsync(page);
                // 页顶那一帧：头部行（海报、片名、操作，右端是唯一那张资料卡）。下面几步会把页面滚到季列表，
                // 所以版面这一眼先在这儿拍 —— 「媒体信息」与「订阅信息」同在这一张卡里，卡底是亚克力
                //（用户令 2026-10-12）。
                Get<ScrollViewer>(page, "DetailScroll").ChangeView(null, 0, null, true);
                await LayoutAsync(page);
                await SaveSettledFrameAsync(page, $"mp-subscription-header-{width}.png");
                // 窗口一变，季列表按新视口重新实现；把季列表重新滚进视野，折叠栏里的逐集行才有得读。
                seasonsView.StartBringIntoView();
                await LayoutAsync(page);
                host = Descendants(seasonsView).OfType<Expander>().First(expander => expander.Name == "ExpandMediaSeason" && expander.IsExpanded);
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"mp-subscription-season-{width}-screen.png");
                await UntilAsync(() => vm.Episodes[0].Artwork is not null);
                var picture = Descendants(page).OfType<Border>().First(border => border.Name == "EpisodeArtwork");
                var play = Descendants(page).OfType<Button>().First(button => button.Name == "PlaySubscriptionEpisode" && button.Visibility == Visibility.Visible);
                Require(picture.ActualWidth > picture.ActualHeight && picture.ActualHeight > 100, "集封面没有按横版显示");
                Require(play.Content is FontIcon && play.TransformToVisual(page).TransformPoint(default).X
                    > picture.TransformToVisual(page).TransformPoint(default).X + picture.ActualWidth, "播放图标不在封面和剧情右侧");
                var search = Descendants(page).OfType<Button>()
                    .First(button => button.Name == "SearchSubscriptionEpisode" && button.Visibility == Visibility.Visible);
                Require(search.Content is FontIcon
                    && search.TransformToVisual(page).TransformPoint(default).Y >= play.TransformToVisual(page).TransformPoint(default).Y,
                    "搜索按钮没有排在播放按钮下方");
                Require(!Descendants(page).OfType<TextBlock>().Any(text => text.Text.StartsWith("入库：", StringComparison.Ordinal)
                    || text.Text.StartsWith("下载：", StringComparison.Ordinal)), "剧情下方仍然显示文件路径");

                // 详情版式（参考 MoviePilot 的「订阅-详细」）：入库徽标、媒体信息、订阅信息都读到了内容，
                // 评分来自订阅快照。资料卡全页只有一张（用户令 2026-10-12「怎么变成两个卡片了」），
                // 卡上不再分「媒体信息」「订阅信息」两个小标题 —— 照网页端那张卡：评分星在最上面，下面一列
                // 「标签 / 值」（用户令 2026-10-12「为什么你弄了一个媒体信息一个订阅信息，只需要留这个」）。
                var facts = Get<StackPanel>(page, "HeroFacts");
                Require(Get<Border>(page, "LibraryBadge").Visibility == Visibility.Visible && vm.LibraryBadge == "部分入库",
                    "入库徽标没有按文件统计显示");
                Require(Get<ItemsRepeater>(page, "SubscriptionFacts").ItemsSource is IReadOnlyList<MoviePilotSubscriptionFact> { Count: > 0 },
                    "订阅信息栏没有内容");
                // 「订阅规则」那块卡已按用户令（2026-10-12「电影订阅界面去掉订阅规则板块」）整块删掉：
                // 页面上不该再有这块标题，也不该再有它的列表或那句「沿用 MoviePilot 当前配置」的说明。
                Require(!Descendants(page).OfType<TextBlock>().Any(text => text.Text == "订阅规则")
                    && Descendants(page).OfType<FrameworkElement>().All(element => element.Name is not ("RulePanel" or "SubscriptionRules")),
                    "页面上还立着「订阅规则」板块");
                Require(vm.DetailRatingText == "8.5 / 10"
                    && Get<ItemsRepeater>(page, "SubscriptionStars").ItemsSource is IReadOnlyList<string> { Count: 5 } stars
                    && stars.Count(glyph => glyph == "\uE735") == 4, "评分星没有按订阅快照折算");
                Require(Get<ItemsRepeater>(page, "MediaFacts").ItemsSource is IReadOnlyList<MoviePilotSubscriptionFact> { Count: > 0 },
                    "媒体信息栏没有内容");
                Require(!Descendants(facts).OfType<TextBlock>().Any(text => text.Text is "媒体信息" or "订阅信息"),
                    "资料卡里还留着「媒体信息」或「订阅信息」小标题");
                var card = facts.Children.OfType<FrameworkElement>().ToList();
                Require(card.Count == 2 && Descendants(card[0]).OfType<FrameworkElement>().Any(element => element.Name == "SubscriptionStars")
                    && card[1] is StackPanel { Spacing: 0 } list
                    && list.Children.OfType<FrameworkElement>().Select(child => child.Name).SequenceEqual(new[] { "MediaFacts", "SubscriptionFacts" }),
                    "资料卡不是「评分星 + 一列字段」两段：" + string.Join("、", card.Select(child => child.Name)));
                // 评分与简介都在头部（用户令 2026-10-12「参考电影页面简介和评分的位置进行修改」）：评分跟在媒体
                // 身份行下面（电影详情页的评分也在那一行）、资料卡里不再留第二份；简介与资料站链接贴头部下沿
                //（电影详情页的简介也在头部下沿），正文那张卡里不再有第二块简介。
                var rating = Get<TextBlock>(page, "MediaRating");
                var heroOverview = Get<StackPanel>(page, "HeroOverview");
                var heroGrid = Get<Grid>(page, "HeroGrid");
                Require(rating.Text == "★ 8.5 / 10" && Descendants(heroGrid).Contains(rating),
                    "头部没有按媒体详情显示评分：" + rating.Text);
                Require(!Descendants(facts).OfType<TextBlock>().Any(text => text.Text == rating.Text),
                    "资料卡里还留着第二份评分");
                Require(Descendants(heroOverview).OfType<TextBlock>().Count(text => text.Text == "简介") == 1
                    && Descendants(page).OfType<TextBlock>().Count(text => text.Text == "简介") == 1,
                    "简介没有跟着进头部，或屏上出现了两份简介");
                Require(Descendants(heroOverview).OfType<FrameworkElement>().Any(element => element.Name == "MediaSourceLinks"),
                    "资料站链接没有跟着简介进头部");
                Require(rating.TransformToVisual(page).TransformPoint(default).Y
                        < heroOverview.TransformToVisual(page).TransformPoint(default).Y,
                    "评分没有排在简介上面");
                // 简介压在这一格的下沿：末行是「*」，简介 Bottom 对齐，所以它的下缘就是头部的下缘。
                Require(Math.Abs(heroOverview.TransformToVisual(heroGrid).TransformPoint(default).Y
                        + heroOverview.ActualHeight - heroGrid.ActualHeight) < 1,
                    $"简介没有贴在头部下沿：简介底 {heroOverview.TransformToVisual(heroGrid).TransformPoint(default).Y + heroOverview.ActualHeight:0.0}，头部高 {heroGrid.ActualHeight:0.0}");
                // 一张卡：正文右栏那第二张（DetailFacts）已经取消，屏上只剩头部右侧这一张。
                Require(Descendants(page).OfType<FrameworkElement>().All(element => element.Name != "DetailFacts"),
                    "页面上还立着第二张资料卡");
                // 资料卡是半透明亚克力（压在固定的剧照上才透得出底下那张图）；简介那一块**不画底也不画外圈**
                //（用户令 2026-10-12「去掉这个背景」）：这几行字照电影详情页那样直接落在剧照上。
                Require(facts.Background is Microsoft.UI.Xaml.Media.AcrylicBrush, "资料卡的底不是半透明亚克力");
                Require(heroOverview.BorderThickness == default(Thickness)
                    && heroOverview.Background is not Microsoft.UI.Xaml.Media.AcrylicBrush
                    && (heroOverview.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush fill || fill.Color.A == 0),
                    "简介那一块还画着底色或外圈");
                Require(width >= 1080
                    ? Math.Abs(facts.ActualWidth - 320) < 1 && Grid.GetRow(facts) == 0 && Grid.GetColumn(facts) == 1
                    : facts.ActualWidth > 320 && Grid.GetRow(facts) == 1 && Grid.GetColumn(facts) == 0,
                    $"资料卡在 {width} 宽下没有按预期并排或叠起来：宽 {facts.ActualWidth}，行 {Grid.GetRow(facts)}，列 {Grid.GetColumn(facts)}，页宽 {page.ActualWidth}");
                // 这一页不画加载条（用户令 2026-10-12：「不要显示那个加载条」；此前是两份数据合并成的
                // 一条进度，再往前是订阅与媒体详情各一条并排）。那条线落在「简介」与「演职员」之间那块
                // 空地方，看着像那一块在加载东西。整页一条都不许再有 —— 进度改由头部与各段自己的说明交代。
                Require(Descendants(page).OfType<ProgressBar>().Count() == 0,
                    "页面上还留着加载条：" + string.Join("、", Descendants(page).OfType<ProgressBar>()
                        .Select(bar => string.IsNullOrEmpty(bar.Name) ? "（无名）" : bar.Name)));

                // 操作排（用户令 2026-10-12：「只留这四个按钮 + 一个编辑订阅按钮，其他的折叠到更多按钮里」
                // ＋「复刻电影页面的更多按钮」）：五颗带字的键按这个次序排着，右端一颗见方的圆角图标键。
                // 2026-10-19 起「在 AniBT 中搜索」也是一颗常驻在树上的键，只有日本动画才显示；这一条夹具不是动画。
                var actions = Descendants(page).OfType<WrapRow>()
                    .Single(row => Descendants(row).OfType<Button>().Any(button => button.Name == "MoreActions"));
                var rowButtons = Descendants(actions).OfType<Button>().ToArray();
                Require(rowButtons.Select(button => button.Name).SequenceEqual(
                        new[] { "SearchSubscription", "SearchAniBt", "SearchMediaSubtitles", "ChooseSubscribeSeasons", "PlayMediaLibraryItem", "EditSubscription", "MoreActions" }),
                    "操作排不是「搜索资源 + AniBT + 搜索字幕 + 播放 + 编辑 + 更多」：" + string.Join("、", rowButtons.Select(button => button.Name)));
                Require(rowButtons.Select(button => button.Content as string).SequenceEqual(
                        new[] { "搜索资源", "在 AniBT 中搜索", "搜索字幕", page.MediaViewModel.SubscribeLabel, "播放已入库", "编辑订阅", null }),
                    "操作排上的文案不对：" + string.Join("、", rowButtons.Select(button => button.Content?.ToString() ?? "（图标）")));
                Require(Get<Button>(page, "SearchAniBt").Visibility == Visibility.Collapsed,
                    "非日本动画的订阅显示了「在 AniBT 中搜索」");
                // 这一条同时钉住「没有片源时隐藏播放键」的另一半：夹具里有已入库且能播的集，这颗键该在。
                Require(Get<Button>(page, "PlayMediaLibraryItem").Visibility == Visibility.Visible,
                    "当前 Emby 里有可播放条目，「播放已入库」却没有出现");
                var more = Get<Button>(page, "MoreActions");
                Require(more.Content is FontIcon { Glyph: "\uE712" }, "「更多」那颗键的字形不是电影详情页那一颗（More）");
                Require(more.ActualWidth == 44 && more.ActualHeight == 44,
                    $"「更多」不是电影详情页那颗见方的圆角方块：{more.ActualWidth}×{more.ActualHeight}");
                // 收进菜单的那几颗：文案、次序与可用性都照它们原来在排上的样子（暂停那颗跟着订阅状态换说法）。
                // 菜单摊平成一层（用户令 2026-10-12：「订阅页面的更多按钮不要折叠，去掉重复的选项」）—— 原先
                // 「管理订阅」是个子菜单，摊开后里面还有一条「暂停」一条「重置」与外面那两条同动作。这里
                // 三条一起钉：不许再有第二层、项与次序一字不差、没有重名的项。
                var moreMenu = page.BuildMoreMenu();
                var moreItems = moreMenu.Items.OfType<MenuFlyoutItemBase>().ToArray();
                var moreLabels = moreItems.Select(item => item switch
                    { MenuFlyoutItem text => text.Text, MenuFlyoutSubItem sub => sub.Text, _ => "（分隔线）" }).ToArray();
                Require(moreItems.OfType<MenuFlyoutSubItem>().Count() == 0, "「更多」菜单里还留着一层折叠："
                    + string.Join("、", moreItems.OfType<MenuFlyoutSubItem>().Select(sub => sub.Text)));
                Require(moreLabels.SequenceEqual(new[] { "暂停订阅", "重置订阅", "编辑", "搜索", "文件统计", "刷新", "（分隔线）", "取消订阅" }),
                    "「更多」菜单里的项不对：" + string.Join("、", moreLabels));
                Require(moreLabels.Distinct().Count() == moreLabels.Length,
                    "「更多」菜单里有重复的项：" + string.Join("、", moreLabels));
                Require(moreItems.Take(4).All(item => item.IsEnabled == vm.CanManage) && moreItems[7].IsEnabled == vm.CanManage
                    && moreItems.Skip(4).Take(2).All(item => item.IsEnabled),
                    "「更多」菜单里的可用性与原来那几颗键不一致");
                await SaveSettledFrameAsync(page, $"mp-subscription-detail-{width}.png");

                // 集列表在折叠线以下，单独滚到它再拍一张：播出日期行和播放键下方那颗搜索键得看得见（2026-10-10）。
                // 用 ChangeView 定位而不是 StartBringIntoView —— 后者只保证「进入视野」，这里要的是它真的滚上来。
                var detailScroll = Get<ScrollViewer>(page, "DetailScroll");
                var episodeList = Descendants(host).OfType<ItemsRepeater>().First(repeater => repeater.Name == "EpisodeStatistics");
                var firstRow = Descendants(episodeList).OfType<Border>().First(border => border.Name == "EpisodeArtwork");
                var rowOffset = firstRow.TransformToVisual((UIElement)detailScroll.Content).TransformPoint(default).Y;
                detailScroll.ChangeView(null, rowOffset - 8, null, true);
                await LayoutAsync(page);
                await SaveSettledFrameAsync(page, $"mp-subscription-episodes-{width}.png");

                // 下方三段（演职员／相关推荐／相似作品）照电影详情页那三段（用户令 2026-10-12：「下方的演员和
                // 推荐啥的参考电影页面添加背景」）：每段一块 EgBodyPanelStyle 的板，板里是 ShelfHead 牌子接
                // ShelfStrip 货架带 —— 与 DetailPage 的「更多类似」「演职人员」两段同一种结构。
                // 这里同时钉住这一轮真正的那条因：原先这三段各套着一个内层 ScrollViewer，它会把竖向滚轮吃掉
                // ——指针停在哪条带上整页就滚不动（用户报「往下翻页之后不能往上翻」）。所以带子必须是
                // ShelfStrip（用的是 ScrollView），段里一个 ScrollViewer 都不许再有。
                var media = page.MediaViewModel;
                var sections = new (string Name, string Title, object Cards)[]
                {
                    ("MediaPeople", "演职员", media.People),
                    ("MediaRecommendations", "相关推荐", media.Recommendations),
                    ("MediaSimilar", "相似作品", media.Similar)
                };
                // ItemsRepeater 只在视口里实现卡片：不把这一段滚上来，读到的是一块空板。
                Get<ShelfStrip>(page, sections[0].Name).StartBringIntoView();
                await LayoutAsync(page);
                foreach (var (name, title, cards) in sections)
                {
                    var strip = Get<ShelfStrip>(page, name);
                    var section = strip.Parent as StackPanel;
                    var parts = section?.Children.OfType<FrameworkElement>().ToArray() ?? [];
                    var head = parts.Length == 2 ? parts[0] as ShelfHead : null;
                    Require(section is not null && head?.Title == title && ReferenceEquals(parts[1], strip),
                        $"{name} 那一段不是「板 + 牌子 + 货架带」：父级 {section?.GetType().Name ?? "空"}，子项 "
                            + string.Join("、", parts.Select(part => part.GetType().Name)));
                    var panel = section!;
                    var panelFill = panel.Background as Microsoft.UI.Xaml.Media.SolidColorBrush;
                    Require(panelFill is not null && panelFill.Color.A > 0 && panel.BorderThickness != default,
                        $"{name} 那一段没有板底或外圈（用户要的就是这个背景）");
                    Require(Descendants(strip).OfType<ScrollViewer>().Count() == 0 && Descendants(strip).OfType<ScrollView>().Any(),
                        $"{name} 里还有内层 ScrollViewer —— 指针停在带上时它会吃掉整页的竖向滚轮");
                    Require(strip.Visibility == Visibility.Visible && ReferenceEquals(strip.ItemsSource, cards),
                        $"{name} 没有装着夹具那六条内容（或整段收着）：{strip.Visibility}");
                    relatedReadings.Add($"{width} 宽 {name}：板底 {panelFill!.Color}、带高 {strip.ActualHeight:0.0}、"
                        + $"牌子「{head!.Title}」、内层滚动面 ScrollView（无 ScrollViewer）");
                }
                Require(media.People.Count == 6 && media.Recommendations.Count == 6 && media.Similar.Count == 6,
                    $"下方三段的内容没有读回来：{media.People.Count}／{media.Recommendations.Count}／{media.Similar.Count}");
                // 卡片上的图是异步抓的：不等它落下来，那张截图上的三段就是三排空板（断言不看图，只截图不好看）。
                await UntilAsync(() => media.People.All(card => card.Poster is not null)
                    && media.Recommendations.All(card => card.Poster is not null));
                await SaveSettledFrameAsync(page, $"mp-subscription-related-{width}.png");
            }
            var playButton = Descendants(page).OfType<Button>().First(button => button.Name == "PlaySubscriptionEpisode" && button.IsEnabled);
            InvokeSubscriptionButton(playButton);
            await UntilAsync(() => played.Count == 1);
            Require(played[0].Item.Id == "play-1" && played[0].Parent?.Id == "linked-show", "播放按钮没有送到正确的 Emby 剧集");
            // 集搜索按钮取第 1 集那一颗，不取「第一颗还亮着的」：集列表在折叠栏里按视口实现，宽度那两轮
            // 滚过之后第一集可能已经不在实现窗口里 —— 按「第一颗」取，实测跑到过第 2 集（那颗键送出的关键词
            // 本身没错，错的是这一行断言认错了行）。先把第一行叫回视野，再按读屏名认行：名是
            // 「搜索资源 · 第 1 集  新的开始」，后半截就是行自己的 RowTitle。
            var episodeRows = Descendants(host).OfType<ItemsRepeater>().First(repeater => repeater.Name == "EpisodeStatistics");
            episodeRows.GetOrCreateElement(0).StartBringIntoView();
            await LayoutAsync(page);
            var firstEpisode = "搜索资源 · " + vm.Episodes[0].RowTitle;
            var searchButton = Descendants(page).OfType<Button>().FirstOrDefault(button => button.Name == "SearchSubscriptionEpisode"
                && button.IsEnabled && AutomationProperties.GetName(button) == firstEpisode);
            Require(searchButton is not null, "页面上找不到第 1 集那一颗集搜索键（要找「" + firstEpisode + "」）：" + string.Join("、",
                Descendants(page).OfType<Button>().Where(button => button.Name == "SearchSubscriptionEpisode")
                    .Select(button => AutomationProperties.GetName(button) + (button.IsEnabled ? "" : "（不可用）"))));
            InvokeSubscriptionButton(searchButton!);
            Require(searched.Count == 1 && searched[0] == "漫长的旅途 S01 E01", "集搜索按钮没有送出正确的关键词：" + string.Join("、", searched));
            var filter = Descendants(page).OfType<ComboBox>().First(combo => combo.Name == "EpisodeFilter");
            filter.SelectedIndex = 1;
            Require(vm.Episodes.Count == 1, "已入库筛选没有驱动视图模型");
            filter.SelectedIndex = 2;
            Require(vm.Episodes.Count == 3, "缺集筛选错误");
            filter.SelectedIndex = 0;
            var editor = new MoviePilotSubscriptionEditor();
            editor.Set(subscription);
            _root.Children.Remove(page); _root.Children.Add(editor);
            try
            {
                await LayoutAsync(editor);
                Get<TextBox>(editor, "IncludeInput").Text = "WEB-DL";
                Get<NumberBox>(editor, "TotalInput").Value = 6;
                await SaveSettledFrameAsync(editor, "mp-subscription-editor.png");
                await vm.EditAsync(vm.Selected!, _ => Task.FromResult<MoviePilotSubscriptionEdit?>(editor.Read()));
                Require(server.Items[4]["include"]!.ToString() == "WEB-DL" && server.Items[4]["total_episode"]!.GetValue<int>() == 6, "编辑未保存输入字段");
                Require(server.Items[4]["sites"]![0]!.GetValue<int>() == 8, "编辑覆盖了未改动的站点");
                Require(vm.Episodes.Count == 6, "保存总集数后未刷新缺集");
            }
            finally { _root.Children.Remove(editor); _root.Children.Add(page); }
            vm.UseConfirm((_, _, _) => Task.FromResult(true));
            var oldEpisode = vm.Episodes[0];
            fixture.Session.SignOut();
            await oldEpisode.PlayCommand.ExecuteAsync(null);
            Require(played.Count == 1, "Emby 退出后旧播放按钮仍可调用");
            await vm.ManageAsync(vm.Selected!, MoviePilotSubscriptionAction.Delete);
            Require(vm.Deleted && !vm.CanManage, "取消订阅后详情仍允许写入");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "mp-subscription-related-layout.txt"), relatedReadings);
            page.Release(); _root.Children.Remove(page); ResizeInspect(1420, 980);
        }
    }

    /// <summary>
    /// 电影订阅详情（用户令 2026-10-12：「删掉红框框出来的地方，还有上方那条什么都没有、纯占位置的」）。
    /// 电影没有季：正文那张季卡整块跟着收起，屏上不再留一条空底；原先给电影留的独立「入库与缺集」面板
    /// 整块删除，页面上不再有第二个落点。入库情况仍由头部徽标交代 —— 所以文件统计本身还要照读。
    /// </summary>
    private async Task MoviePilotMovieSubscriptionDetailAsync()
    {
        using var server = new SubscriptionProbeServer();
        server.Poster = await FixtureImageAsync();
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => !item.IsSeries);
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        // 这一条不接播放范围：只看版式与入库徽标，不碰 Emby 条目匹配，更不起播。
        page.Attach(new(service, subscription));
        try
        {
            var vm = page.ViewModel;
            await UntilAsync(() => vm.IsReady && page.MediaViewModel.Detail is not null);
            // 季视图的显隐是详情读完之后才发的通知（IsReady 先立起来），等它真的落下来再读版面。
            await UntilAsync(() => Get<StackPanel>(page, "SeasonPanel").Visibility == Visibility.Collapsed);
            // 宽窄两档都读：资料卡在宽窗并排、窄窗落到头部下面，这两档的版面差别最大，收起与否都得成立。
            foreach (var width in new[] { 1420, 760 })
            {
                ResizeInspect(width, 1000);
                await UntilAsync(() => page.ActualWidth <= width + 2);
                await LayoutAsync(page);

                // ① 季卡：电影没有季，整块收起 —— 既不画空底，也不再占位置（用户说的就是「纯占位置的」那一条）。
                //    高度按季视图自己的读数算：收起的子树不参与布局，量到 0 才算真的没占。
                Require(Get<StackPanel>(page, "SeasonPanel").Visibility == Visibility.Collapsed, "电影页那条空的季面板没有收起");
                var seasonsView = Get<MoviePilotMediaSeasonsView>(page, "MediaSeasons");
                Require(seasonsView.ActualHeight == 0, $"电影页上的季视图仍占着 {seasonsView.ActualHeight:0.0} 的位置");

                // ② 红框那块独立面板：整块删除，标题与控件都不该再出现在页面上。
                Require(!Descendants(page).OfType<TextBlock>().Any(text => text.Text == "入库与缺集"), "电影页上还立着独立的「入库与缺集」面板");
                Require(Descendants(page).OfType<FrameworkElement>().All(element => element.Name is not ("SubscriptionFilesPanel" or "FilesHeading"
                    or "MovieEpisodeStatistics" or "MovieEpisodeFilter")), "电影页上还留着入库面板的控件");

                // ③ 入库情况改由头部徽标交代，所以文件统计本身还要读：徽标有结论、摘要不是失败文案。
                Require(Get<Border>(page, "LibraryBadge").Visibility == Visibility.Visible && vm.LibraryBadge == "已入库",
                    "头部入库徽标没有按电影的文件统计显示：" + vm.LibraryBadge);
                Require(vm.FileSummary == "电影已入库" && vm.Episodes.Count == 1, "电影的文件统计没有读回来：" + vm.FileSummary);

                // ④ 这一页正是用户圈出「订阅规则」那块卡的那一页（用户令 2026-10-12）：整块删掉之后，
                //    页面上不该再有那块标题、规则列表或那句「沿用 MoviePilot 当前配置」的说明。
                Require(!Descendants(page).OfType<TextBlock>().Any(text => text.Text == "订阅规则"
                    || text.Text.Contains("沿用 MoviePilot 当前配置", StringComparison.Ordinal)),
                    "电影页上还立着「订阅规则」板块");
                Require(Descendants(page).OfType<FrameworkElement>().All(element => element.Name is not ("RulePanel" or "SubscriptionRules")),
                    "电影页上还留着「订阅规则」的控件");

                // ⑤ 没有片源就不给播放入口（用户令 2026-10-12：「没有片源时隐藏在线播放按钮」）：这一条
                //    故意不接播放范围，也就没有可播放条目 —— 那颗键该收着。接上范围、真的匹配到条目的那一半
                //    在 MoviePilotSubscriptionDetailAsync 里钉（那里断言它是 Visible）。
                Require(Get<Button>(page, "PlayMediaLibraryItem").Visibility == Visibility.Collapsed,
                    "没有可播放片源，「播放已入库」却立着");
                await SaveSettledFrameAsync(page, $"mp-movie-subscription-detail-{width}.png");
            }

            // ⑥ 「更多」菜单在电影上的那一份：摊平的一层里没有「文件统计」（电影没有季折叠栏可滚），也没有
            //    「媒体详情」（这一页本来就是媒体详情，它原先与「文件统计」同去一处，用户令 2026-10-12
            //    「去掉重复的选项」把这一对合成落点准确的「文件统计」）。
            var movieMenu = page.BuildMoreMenu();
            var movieLabels = movieMenu.Items.OfType<MenuFlyoutItemBase>().Select(item => item switch
                { MenuFlyoutItem text => text.Text, MenuFlyoutSubItem sub => sub.Text, _ => "（分隔线）" }).ToArray();
            Require(movieLabels.SequenceEqual(new[] { "暂停订阅", "重置订阅", "编辑", "搜索", "刷新", "（分隔线）", "取消订阅" }),
                "电影页「更多」菜单里的项不对：" + string.Join("、", movieLabels));
        }
        finally { page.Release(); _root.Children.Remove(page); ResizeInspect(1420, 980); }
    }

    private async Task MoviePilotSubscriptionIdentityAsync()
    {
        using var server = new SubscriptionProbeServer();
        var (settings, service) = SubscriptionService(server);
        var vm = new MoviePilotSubscriptionsViewModel();
        vm.Attach(service);
        await vm.ReloadAsync();
        var card = vm.Movies[0];
        var confirm = Pending<bool>();
        vm.UseConfirm((_, _, _) => confirm.Task);
        var changing = vm.ManageAsync(card, MoviePilotSubscriptionAction.Delete);
        settings.MoviePilot.Username = "another-user";
        confirm.SetResult(true);
        await changing;
        Require(server.Writes.Count == 0, "切账号后旧确认仍发送写请求");
        await vm.ReloadAsync();
        Require(!vm.Owns(card), "切账号重载后沿用旧卡片");
        var late = Pending<HttpResponseMessage>();
        server.PendingList = late;
        var loading = vm.ReloadAsync();
        vm.Cancel();
        late.SetResult(Json(new { success = true, data = server.Items.Values.ToArray() }));
        await loading;
        Require(vm.Movies.Count == 0 && !vm.CanManage, "离页后迟到请求复活列表");
    }

    /// <summary>
    /// 订阅板块的显隐判据（2026-10-08「开应用／返回主页会闪一下空板块」）：把列表那一趟扣在手里，读「数据还没
    /// 回来」和「数据回来了」两头。判据是 <see cref="MoviePilotSubscriptionsViewModel.Visible"/>，不掺时序：
    /// 假传输同步完成与否都不影响结论。
    /// </summary>
    private async Task SubscriptionSectionVisibilityAsync(SubscriptionProbeServer server)
    {
        var (_, service) = SubscriptionService(server);
        var vm = new MoviePilotSubscriptionsViewModel();

        // 1. MoviePilot 开着、卡片一张都没有：整块收着。从前这里就是 Visible —— Attach 一上来凭 Enabled 把
        //    空板块摆上屏，正是开应用／返回主页看到的那一帧。
        vm.Attach(service);
        Require(vm.Visible == Visibility.Collapsed, "一条订阅也没有，空板块却要立起来（开应用／返回主页会闪一下）");

        // 2. **列表还扣在手里**（真实的开应用那一拍）：整块仍须收着，等数据真回来才和内容一起出现。
        var gate = Pending<HttpResponseMessage>();
        server.PendingList = gate;
        var loading = vm.ReloadAsync();
        Require(vm.Visible == Visibility.Collapsed, "列表还扣在手里，板块就已经立起来了");
        gate.SetResult(Json(new { success = true, data = server.Items.Values.ToArray() }));
        await loading;
        Require(vm.Movies.Count == 3 && vm.Visible == Visibility.Visible, "订阅读回来了，板块却没有立起来");

        // 3. 一条订阅都没有的账号：读完了也不该出现这块（从前只看「开着 MoviePilot」就会摆出来）。
        var empty = Pending<HttpResponseMessage>();
        server.PendingList = empty;
        var reloading = vm.ReloadAsync();
        empty.SetResult(Json(new { success = true, data = Array.Empty<object>() }));
        await reloading;
        Require(vm.Movies.Count == 0 && vm.Series.Count == 0 && vm.Visible == Visibility.Collapsed,
            "一条订阅都没有，空板块还是立着");

        server.PendingList = null;
        vm.Cancel();
    }

    /// <summary>
    /// 日本动画订阅详情上的那颗「在 AniBT 中搜索」：只验显示条件、点击接线与宽度布局，不打开第二个窗口，
    /// 也不请求 AniBT —— 外壳那一头由回调捕获，只读地去核对交出去的是订阅标题。
    /// </summary>
    private async Task MoviePilotAnimeSubscriptionDetailAsync()
    {
        using var server = new SubscriptionProbeServer { Anime = true };
        server.Poster = await FixtureImageAsync();
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        AniBtSearchQuery? query = null;
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        page.Attach(new(service, subscription, SearchAniBt: value => query = value));
        try
        {
            await UntilAsync(() => page.ViewModel.IsReady && page.MediaViewModel.IsReady);
            await UntilAsync(() => page.MediaViewModel.AniBtVisibility == Visibility.Visible);
            foreach (var width in new[] { 1420, 760 })
            {
                ResizeInspect(width, 1000);
                await LayoutAsync(page);
                var button = Get<Button>(page, "SearchAniBt");
                Require(button.Visibility == Visibility.Visible && button.IsEnabled && Equals(button.Content, "在 AniBT 中搜索"),
                    "日本动画订阅没有显示可用的「在 AniBT 中搜索」");
                await SaveSettledFrameAsync(page, $"mp-subscription-anibt-{width}.png");
            }
            InvokeSubscriptionButton(Get<Button>(page, "SearchAniBt"));
            Require(query == new AniBtSearchQuery("漫长的旅途", null),
                "AniBT 按钮没有把订阅标题交给搜索：" + query);
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }
    }

    private static void InvokeSubscriptionButton(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private async Task SeasonTrailingPlayAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var image = await FixtureImageAsync();
        var season = Season("a");
        season.Name = "第一季";
        season.ImageTags["Primary"] = "fixture";
        var episode = Episode("a");
        episode.ImageTags["Primary"] = "fixture";
        episode.ParentIndexNumber = 1;
        episode.Name = "新的旅程";
        episode.Overview = "一次突如其来的重逢，让熟悉的生活有了新的方向。";
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (path.EndsWith("/Items/a", StringComparison.Ordinal)) return Task.FromResult(Json(season));
            if (path.EndsWith("/Episodes", StringComparison.Ordinal)) return Task.FromResult(Json(new { Items = new[] { episode } }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var played = new List<EmbyItem>();
        IReadOnlyList<EmbyItem>? siblings = null;
        fixture.Actions.CapturePlay = (item, episodes) => { played.Add(item); siblings = episodes; };
        var frame = new Frame();
        _root.Children.Add(frame);
        frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, season));
        try
        {
            var page = (DetailPage)frame.Content;
            await UntilAsync(() => page.IsReady);
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeInspect(width, 1000); await LayoutAsync(page);
                var list = Get<ItemsRepeater>(page, "EpisodeList");
                var container = (FrameworkElement)list.GetOrCreateElement(0);
                container.StartBringIntoView();
                await LayoutAsync(page);
                var row = Descendants(container).OfType<EpisodeRow>().Single();
                var play = Get<Button>(row, "TrailingPlay");
                var picture = Get<Border>(row, "Art");
                Require(row.ShowTrailingPlay && play.Visibility == Visibility.Visible && play.Content is FontIcon, "季页面没有显示右侧播放图标");
                Require(play.TransformToVisual(row).TransformPoint(default).X >= picture.ActualWidth + 10, "季页面播放图标位置错误");
                await UntilAsync(() => row.Card?.Poster is not null);
                await SaveSettledFrameAsync(page, $"season-trailing-play-{width}.png");
                var opened = fixture.Actions.Opened;
                InvokeSubscriptionButton(play);
                Require(played.LastOrDefault()?.Id == episode.Id && siblings?.Any(item => item.Id == episode.Id) == true, "季页面播放图标没有携带正确的集和剧集列表");
                Require(fixture.Actions.Opened == opened, "播放图标同时触发行打开");
            }
        }
        finally { (frame.Content as IShellContent)?.Release(); _root.Children.Remove(frame); ResizeInspect(1420, 980); }
    }

    private static (AppSettings Settings, MoviePilotService Service) SubscriptionService(SubscriptionProbeServer server)
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true; settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
        credentials.SetPassword(settings.MoviePilot, "fixture-password");
        return (settings, new MoviePilotService(new MoviePilotClient(server), credentials, settings));
    }

    private sealed class SubscriptionProbeServer : HttpMessageHandler
    {
        internal Dictionary<int, JsonObject> Items { get; } = Enumerable.Range(1, 5).ToDictionary(id => id, id => new JsonObject
        {
            ["id"] = id,
            ["name"] = id < 4 ? new[] { "星际远航", "夏日来信", "海岸线" }[id - 1] : "漫长的旅途",
            ["year"] = "2026",
            ["type"] = id < 4 ? "电影" : "电视剧",
            ["season"] = id < 4 ? null : id - 3,
            ["media_source"] = "themoviedb",
            ["media_id"] = id.ToString(),
            ["state"] = "R",
            ["poster"] = "https://poster.invalid/" + id + ".png",
            ["backdrop"] = "https://poster.invalid/backdrop-" + id + ".png",
            ["vote"] = 8.5,
            ["total_episode"] = id < 4 ? 0 : 4,
            ["lack_episode"] = 2,
            ["start_episode"] = 1,
            ["description"] = "一段关于相遇、离别与重逢的故事。订阅详情可查看媒体简介、管理订阅，并核对每集的入库情况。",
            ["sites"] = new JsonArray(8),
            ["keyword"] = "星际远航",
            ["quality"] = "BluRay",
            ["resolution"] = "1080p",
            ["downloader"] = "qbittorrent",
            ["save_path"] = "/媒体库/电视剧",
            ["search_interval"] = 6,
            ["username"] = "fixture",
            ["date"] = "2026-07-26T10:00:00",
            ["last_update"] = "2026-07-27T11:30:00",
            ["last_search"] = "2026-07-27T11:30:00",
            ["include"] = ""
        });
        internal List<string> Writes { get; } = [];
        internal List<JsonObject> WriteBodies { get; } = [];
        internal TaskCompletionSource<HttpResponseMessage>? PendingGroup { get; set; }
        internal TaskCompletionSource<HttpResponseMessage>? PendingExists { get; set; }
        internal bool FailSubscriptionCreate { get; set; }
        internal bool UncertainNextWrite { get; set; }
        internal string? FailedImageUrl { get; set; }
        internal List<string> ImageRequests { get; } = [];

        /// <summary>
        /// 演职员／相关推荐／相似作品这三段要不要给内容。默认关（返回空数组，那三段在页上收起）—— 只有
        /// 订阅详情那一条用例打开它：这三段正是「下方那几段的板底与货架带」要量的东西，而别的用例的断言
        /// 与截图都不看它们，凭空多三段只会让那些用例的版面跟着变。
        /// </summary>
        internal bool Related { get; set; }

        /// <summary>把电视剧那份媒体详情标成动画；只有「日本动画订阅的 AniBT 入口」那一条打开。</summary>
        internal bool Anime { get; set; }

        internal byte[] Poster { get; set; } = [];
        internal TaskCompletionSource<HttpResponseMessage>? PendingList { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "订阅探针拒绝真实网络");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v1/system/cache/image")
            {
                var url = Query(request, "url") ?? "";
                ImageRequests.Add(url);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(url == FailedImageUrl ? [] : Poster) };
            }
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture-token" });
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Json(new { version = "fixture" });
            if (path == "/api/v1/mediaserver/exists_remote") return PendingExists is { } exists ? await exists.Task : Json(new { success = true, data = new Dictionary<string, int[]> { ["1"] = [1] } });
            // 演职员／相关推荐／相似作品：默认空 —— 这三段只有订阅详情页会读，别的用例不必凭空多三段内容。
            // 要看这三段的用例把 Related 打开（订阅详情那一条就是这么做的）：夹具给六个人、推荐与相似各六部，
            // 每一条都带一张图（走 /system/cache/image 那张假图）。
            if (path.StartsWith("/api/v1/tmdb/credits/", StringComparison.Ordinal)) return Json(Related ? RelatedPeople() : Array.Empty<object>());
            if (path.StartsWith("/api/v1/tmdb/recommend/", StringComparison.Ordinal) || path.StartsWith("/api/v1/tmdb/similar/", StringComparison.Ordinal)) return Json(Related ? RelatedMedia() : Array.Empty<object>());
            if (path == "/api/v1/media/4") return Json(new
            {
                title = "漫长的旅途",
                year = "2026",
                type = "电视剧",
                media_source = "themoviedb",
                media_id = "4",
                tmdb_id = 4,
                original_name = "The Long Journey",
                status = "Returning Series",
                first_air_date = "2026-01-01",
                original_language = "ja",
                vote_average = 8.5,
                poster_path = "https://poster.invalid/4.png",
                backdrop_path = "https://poster.invalid/backdrop-4.png",
                overview = "一段关于相遇、离别与重逢的故事。",
                tagline = "下一次相遇，在旅途的尽头。",
                genres = Anime ? new[] { new { name = "动画" } } : Array.Empty<object>(),
                production_countries = new[] { new { name = "日本" } },
                production_companies = new[] { new { name = "Example Studio" } },
                season_info = new[] { new { season_number = 1, episode_count = 4, air_date = "2026-01-01", poster_path = "https://poster.invalid/4.png" }, new { season_number = 2, episode_count = 4, air_date = "2027-01-01", poster_path = "https://poster.invalid/4.png" } }
            });
            // 媒体详情另给电影那一部一份像样的回话（id 1）。订阅列表里的条目用 name，媒体详情用 title，
            // 只回订阅对象的话 MoviePilotMediaParser.ToMedia 认不出标题、整趟会被判成「身份不匹配」。
            if (path == "/api/v1/media/1") return Json(new
            {
                title = "星际远航",
                year = "2026",
                type = "电影",
                media_source = "themoviedb",
                media_id = "1",
                tmdb_id = 1,
                original_title = "Star Voyage",
                status = "Released",
                release_date = "2026-05-01",
                original_language = "en",
                vote_average = 8.5,
                runtime = 132,
                poster_path = "https://poster.invalid/1.png",
                backdrop_path = "https://poster.invalid/backdrop-1.png",
                overview = "一艘船、一群人，和一段没有回程的航程。",
                tagline = "出发就是答案。",
                production_countries = new[] { new { name = "美国" } },
                production_companies = new[] { new { name = "Example Pictures" } }
            });
            if (path.StartsWith("/api/v1/media/group/seasons/", StringComparison.Ordinal))
            {
                if (path.EndsWith("grp-9", StringComparison.Ordinal) && PendingGroup is { } group) return await group.Task;
                return Json(new[] { new { season_number = path.EndsWith("grp-10", StringComparison.Ordinal) ? 7 : 3, episode_count = 4, name = "分组季", air_date = "2026-01-01", poster_path = "https://poster.invalid/4.png" } });
            }
            if (request.Method != HttpMethod.Get)
            {
                Writes.Add(request.Method.Method + " " + path);
                if (path == "/api/v1/subscribe/" && request.Method == HttpMethod.Post)
                {
                    if (FailSubscriptionCreate) return Json(new { success = false, message = "模拟订阅结果未确认" });
                    var added = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                    WriteBodies.Add((JsonObject)added.DeepClone());
                    var addedId = Items.Keys.Max() + 1;
                    added["id"] = addedId; added["state"] = "R"; Items[addedId] = added;
                    return Json(new { success = true, data = new { id = addedId } });
                }
                if (path == "/api/v1/subscribe/" && request.Method == HttpMethod.Put)
                {
                    var patch = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                    WriteBodies.Add((JsonObject)patch.DeepClone());
                    var id = patch["id"]!.GetValue<int>();
                    foreach (var pair in patch.Where(pair => pair.Key != "id")) Items[id][pair.Key] = pair.Value?.DeepClone();
                }
                else if (request.Method == HttpMethod.Delete) Items.Remove(int.Parse(path.Split('/')[^1]));
                else if (path.Contains("/status/", StringComparison.Ordinal)) Items[int.Parse(path.Split('/')[^1])]["state"] = Query(request, "state");
                if (UncertainNextWrite) { UncertainNextWrite = false; return Json(new { accepted = true }); }
                return Json(new { success = true, data = (object?)null });
            }
            if (path == "/api/v1/subscribe/") return PendingList is { } pending ? await pending.Task : Json(new { success = true, data = Items.Values.ToArray() });
            // 剧集组那一节的两趟：组列表（media/groups）与默认排序的季列表（media/seasons，只用来算「默认」那一项
            // 的季数/集数）。两条都得排在下面那个 int.Parse 兜底之前 —— 它们的末段不是订阅编号。
            if (path.StartsWith("/api/v1/media/groups/", StringComparison.Ordinal)) return Json(new object[]
            {
                new { id = "grp-9", name = "Seasons", group_count = 4, episode_count = 110 },
                new { id = "grp-10", name = "Cours", group_count = 4, episode_count = 48 }
            });
            if (path == "/api/v1/media/seasons") return Json(new object[]
            {
                new { season_number = 1, episode_count = 4 },
                new { season_number = 2, episode_count = 4 }
            });
            // 集播出日期走 TMDB 季集接口（订阅的文件统计不带它，见 MoviePilotEpisodeAirDates）。四条各占一种形态：
            // 已播出、已播出、还没播、没有日期 —— 屏上就该是「播出 / 播出 / 待播出 / 不显示」，搜索入口只给前两条。
            if (path.Contains("/tmdb/", StringComparison.Ordinal)) return Json(new object[]
            {
                new { episode_number = 1, air_date = "2026-01-01", name = "新的开始", overview = "一场意外的重逢，让旅途有了新的方向。", still_path = "https://poster.invalid/episode-1.png" },
                new { episode_number = 2, air_date = "2026-01-08", name = "重逢", overview = "熟悉的城市里，故事才刚刚开始。", still_path = "https://poster.invalid/episode-2.png" },
                new { episode_number = 3, air_date = "2999-01-01", name = "远方", overview = "", still_path = "https://poster.invalid/episode-3.png" },
                new { episode_number = 4, air_date = "", name = "归途", overview = "", still_path = "https://poster.invalid/episode-4.png" }
            });
            var itemId = int.Parse(path.Split('/')[^1]);
            if (!Items.TryGetValue(itemId, out var item)) return new(HttpStatusCode.NotFound);
            // 文件统计：电视剧按集号给三条（已入库 / 已下载待入库 / 缺集）；电影按真实回话只给零号那一条
            //（见 MoviePilotSubscriptionFiles.Parse —— 电影的非零号条目一律跳过）。电影这一条给入库文件，
            // 好让入库徽标有结论可读。
            if (path.Contains("/files/", StringComparison.Ordinal)) return Json(new
            {
                success = true,
                data = new
                {
                    subscribe = item,
                    episodes = item["type"]!.GetValue<string>() == "电影"
                        ? new Dictionary<string, object>
                        {
                            ["0"] = new { title = item["name"]!.GetValue<string>(), description = "", backdrop = "https://poster.invalid/backdrop-1.png", library = new[] { new { file_path = "/媒体库/电影/星际远航.mkv" } }, download = Array.Empty<object>() }
                        }
                        : new Dictionary<string, object>
                        {
                            ["1"] = new { title = "新的开始", description = "第一集", backdrop = "https://poster.invalid/episode-1.png", library = new[] { new { file_path = "/媒体库/漫长的旅途/S01E01.mkv" } }, download = Array.Empty<object>() },
                            ["2"] = new { title = "重逢", description = "第二集", backdrop = "https://poster.invalid/episode-2.png", library = Array.Empty<object>(), download = new[] { new { file_path = "/下载/S01E02.mkv" } } },
                            ["3"] = new { title = "远方", description = "第三集", backdrop = "https://poster.invalid/episode-3.png", library = Array.Empty<object>(), download = Array.Empty<object>() }
                        }
                }
            });
            return Json(new { success = true, data = item });
        }

        /// <summary>演职员那一段的夹具：六个人，各有一张头像。名字与角色都带序号，好按名字认行。</summary>
        private static object[] RelatedPeople() => Enumerable.Range(1, 6).Select(index => (object)new
        {
            id = "person-" + index,
            name = "演员" + index,
            character = "角色 " + index,
            profile_path = "https://poster.invalid/person-" + index + ".png"
        }).ToArray();

        /// <summary>相关推荐与相似作品那两段的夹具：六部作品，各有一张竖版海报。</summary>
        private static object[] RelatedMedia() => Enumerable.Range(1, 6).Select(index => (object)new
        {
            title = "相关作品" + index,
            type = index % 2 == 0 ? "电影" : "电视剧",
            media_source = "themoviedb",
            media_id = 100 + index,
            year = "2025",
            overview = "一部相关的作品。",
            poster_path = "https://poster.invalid/related-" + index + ".png"
        }).ToArray();
    }
}
