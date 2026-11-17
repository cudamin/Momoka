using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private static void RequireStoreDetailCards(object page, string[] names)
    {
        var source = Get<Border>(page, "StoreCardMaterialSource");
        foreach (var name in names)
        {
            var panel = Get<StackPanel>(page, name);
            Require(ReferenceEquals(panel.Background, source.Background) && panel.Background is SolidColorBrush { Color.A: 255 },
                name + " 商店卡片缺少当前主题实色表面");
            Require(ReferenceEquals(panel.Style, Application.Current.Resources["EgStoreDetailCardStyle"])
                && panel.Padding == default && panel.Spacing == 0 && panel.CornerRadius == new CornerRadius(8),
                name + " 商店卡片格式不一致");
            var header = (Border)panel.Children[0];
            var content = (StackPanel)panel.Children[1];
            var head = (ShelfHead)header.Child;
            Require(header.Padding == new Thickness(24, 10, 24, 10)
                && header.BorderThickness == new Thickness(0, 0, 0, 1) && header.MinHeight == 48
                && content.Padding == new Thickness(24) && head.StoreCard && !head.Offer,
                name + " 标题、分隔或内容留白不一致");
            if (panel.Visibility == Visibility.Visible && panel.ActualHeight > 0)
            {
                Require(header.ActualWidth > 0 && Math.Abs(header.ActualWidth - panel.ActualWidth) < 1
                    && content.ActualWidth > 0, name + " 标题分隔未铺满卡片或内容未呈现");
                var title = Descendants(head).OfType<TextBlock>().Single(text => text.Name == "TitleText");
                Require(title.FontSize == 16 && Descendants(head).OfType<Border>()
                    .Where(border => border.Name is "Accent" or "Rule").All(border => border.Visibility == Visibility.Collapsed),
                    name + " 商店标题仍显示旧强调线或字号");
            }
        }
    }

    private async Task StoreDetailCardsAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var savedTheme = ThemeHost.Current.Id;
        var picture = await FixtureImageAsync(400, 600);
        var series = Series();
        series.Name = "商店分区卡片";
        series.Overview = "相同的标题栏、分隔线与内容留白。";
        series.Genres = ["动画"];
        // 2026-11-17 商店封面按右栏「徽标上沿到主播放键下沿」放大：夹具补上徽标、评分与日期/时长，
        // 右栏才高过 180×270 的兜底，封面放大与上/下沿对齐这两句在原生探针里才真的被量到。
        series.ImageTags["Logo"] = "fixture";
        series.CommunityRating = 8.4f;
        series.ProductionYear = 2024;
        series.RunTimeTicks = TimeSpan.FromMinutes(118).Ticks;
        series.People = [new() { Id = "card-person", Name = "示例演员", Type = "Actor", PrimaryImageTag = "fixture" }];
        series.ImageTags["Primary"] = "fixture";
        series.BackdropImageTags.Add("fixture");
        var episode = Episode("a");
        episode.MediaSources =
        [
            new()
            {
                Id = "source-a", Name = "离线版本 · 1080p", Container = "mkv",
                MediaStreams =
                [
                    new() { Index = 0, Type = "Video", Codec = "h264", Width = 1920, Height = 1080 },
                    new() { Index = 1, Type = "Audio", DisplayTitle = "国语", IsDefault = true },
                    new() { Index = 2, Type = "Audio", DisplayTitle = "日语" },
                    new() { Index = 3, Type = "Subtitle", DisplayTitle = "简体中文", IsDefault = true },
                    new() { Index = 4, Type = "Subtitle", DisplayTitle = "English" }
                ]
            },
            new()
            {
                Id = "source-b", Name = "离线版本 · 720p", Container = "mkv",
                MediaStreams = [new() { Index = 0, Type = "Video", Codec = "h264", Width = 1280, Height = 720 }]
            }
        ];
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) }
                : path.EndsWith("/Items/show", StringComparison.Ordinal) ? Json(series)
                : path.EndsWith("/Episodes", StringComparison.Ordinal) ? Json(new { Items = new[] { episode } })
                : path.EndsWith("/Items/" + episode.Id, StringComparison.Ordinal) ? Json(episode)
                : path.EndsWith("/Similar", StringComparison.Ordinal) ? Items(2, 2, "similar") : FakeTransport.Standard(request));
        };
        fixture.Settings.Ui.EnableImmersive = false;
        fixture.Settings.Ui.SeriesCards = [.. DetailLayout.Plan(EmbyItemType.Series, null)];
        ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
        var frame = new Frame();
        _root.Children.Add(frame);
        var reads = new List<string>();
        try
        {
            if (_window!.IsMaximized) _window.ToggleMaximize();
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, series)), "详情导航失败");
            var page = (DetailPage)frame.Content;
            page.AttachWindow(_window);
            await UntilAsync(() => page.IsReady && page.ViewModel.StillImage is not null && Get<bool>(page, "_appearanceObserved"));
            var names = new[] { "EpisodePanel", "InfoPanel", "SeasonPanel", "CastPanel", "SimilarPanel" };
            foreach (var width in new[] { 1240, 1025, 1024, 1023, 760, 360, 1240 })
            {
                ResizeProbeClient(width, 900);
                await WaitDetailPageSizeAsync(page, width, "媒体卡片缩放");
                await LayoutAsync(page);
                RequireStoreDetailCards(page, names);
                var cover = Get<Border>(page, "PosterStill");
                var panels = names.Select(name => Get<StackPanel>(page, name)).ToArray();
                CheckEdges(cover, panels, Get<Grid>(page, "StoreScrollContent"));
                RequireStoreHeroAlignment(page);
                if (width is 1240 or 760 or 360 && page.ViewModel.StoreMode)
                    await CheckStoreContinueAsync(page, reads);
                RequireCardOrder(page, fixture.Settings.Ui.SeriesCards.Select(card => card.Key));
                panels.First(panel => panel.Visibility == Visibility.Visible).StartBringIntoView();
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"store-detail-cards-media-{width}.png");
                await SaveProbeScreenAsync($"store-cover-actions-{width}-screen.png");
                reads.Add($"媒体 {width}: 左沿 {panels[0].TransformToVisual(page).TransformPoint(default).X:0.##}, 卡头48、内容24、两侧对称");
            }
            var info = Get<StackPanel>(page, "InfoPanel");
            var infoSetting = fixture.Settings.Ui.SeriesCards.Single(card => card.Key == "info");
            infoSetting.Visible = false;
            ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
            await LayoutAsync(page);
            Require(info.Visibility == Visibility.Collapsed && Get<Border>(page, "InfoPanelHeader").Parent == info,
                "隐藏信息卡没有连同标题一起收起");
            infoSetting.Visible = true;
            ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
            await LayoutAsync(page);
            Require(ReferenceEquals(info, Get<StackPanel>(page, "InfoPanel")) && info.Visibility == Visibility.Visible, "信息卡未原实例恢复");
            fixture.Settings.Ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(page);
            Require(!Get<ShelfHead>(page, "InfoHead").StoreCard && info.Padding == new Thickness(24), "沉浸未恢复原格式");
            // 2026-11-15 用户明确「这个按钮的修改包括沉浸模式」：已观看那颗保持固定的绿；连播那颗当天稍后
            // 改为跟主题色，由 AcrylicControlsAsync 的连播图标检查单独核对。
            var active = (SolidColorBrush)page.Resources["EgActionActiveBrush"];
            Require(active.Color == Windows.UI.Color.FromArgb(0xFF, 0x52, 0xB5, 0x4B),
                $"沉浸模式已观看图标不是绿色：{active.Color}");
            fixture.Settings.Ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(page);
            RequireStoreDetailCards(page, names);
            CheckFailure(page, names);
            var overview = Get<StackPanel>(page, "OverviewPanel");
            var overviewMargin = overview.Margin;
            try
            {
                overview.Margin = new Thickness(8, overviewMargin.Top, overviewMargin.Right, overviewMargin.Bottom);
                await LayoutAsync(page);
                var rejected = false;
                try { RequireStoreHeroAlignment(page); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "顶部对齐检查未拒绝简介左移位");
            }
            finally { overview.Margin = overviewMargin; }
            await LayoutAsync(page);
            RequireStoreHeroAlignment(page);
            ThemeHost.Apply(UiThemes.All.First(theme => theme.Id != savedTheme).Id);
            await LayoutAsync(page);
            RequireStoreDetailCards(page, names);
            var source = Get<Border>(page, "StoreCardMaterialSource");
            var savedBrush = source.Background;
            try
            {
                source.Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
                RequireStoreDetailCards(page, names);
            }
            finally { source.Background = savedBrush; }
            // 直接复用旧模式检查；单集入口的旧窄窗前件不能代替本次受影响路径的运行证据。
            await CheckImmersiveSwitchAsync(page, fixture.Settings.Ui);
            reads.Add("媒体旧模式检查：1200/760 商店与沉浸往返、主体与分区身份、操作材质均通过；简介、文件选项与操作左沿及移位反例通过。");
            page.Release();
            frame.Content = null;

            using var server = new SubscriptionProbeServer { Poster = picture, Related = true };
            var (settings, service) = SubscriptionService(server);
            using var mpService = service;
            using var settingsService = new Momoka.Services.SettingsService(null!, settings);
            settings.Ui.EnableImmersive = false;
            var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
            var mp = new MoviePilotSubscriptionPage();
            frame.Content = mp;
            mp.Attach(new(service, subscription, Settings: settingsService));
            await UntilAsync(() => mp.ViewModel.IsReady && mp.MediaViewModel.IsReady && Get<bool>(mp, "_appearanceObserved"));
            var mpNames = new[] { "SeasonPanel", "PeoplePanel", "RecommendationsPanel", "SimilarPanel" };
            foreach (var width in new[] { 1240, 760, 360, 1240 })
            {
                ResizeProbeClient(width, 900);
                try
                {
                    await UntilAsync(() =>
                    {
                        mp.UpdateLayout();
                        return StoreDetailProbeEvidence.PageSizeMatches(width,
                            _window!.ClientSize.Width / _root.XamlRoot.RasterizationScale, mp.ActualWidth,
                            Get<ScrollViewer>(mp, "DetailScroll").ActualWidth, mp.MediaViewModel.PageWidth);
                    });
                }
                catch (TimeoutException error)
                {
                    var scroll = Get<ScrollViewer>(mp, "DetailScroll");
                    var content = Get<Grid>(mp, "StoreScrollContent");
                    throw new TimeoutException($"订阅缩放：目标{width}、客户区{_window!.ClientSize.Width}、页{mp.ActualWidth:0.##}、"
                        + $"Scroll{scroll.ActualWidth:0.##}/视口{scroll.ViewportWidth:0.##}、VM{mp.MediaViewModel.PageWidth:0.##}、"
                        + $"内容{content.ActualWidth:0.##}/Width{content.Width:0.##}、缩放{_root.XamlRoot.RasterizationScale:0.##}", error);
                }
                await LayoutAsync(mp);
                RequireStoreDetailCards(mp, mpNames);
                CheckSubscriptionEdges(mp, mpNames);
                Get<StackPanel>(mp, "SeasonPanel").StartBringIntoView();
                await LayoutAsync(mp);
                await SaveProbeScreenAsync($"store-detail-cards-subscription-{width}.png");
                reads.Add($"订阅 {width}: 卡片与封面左沿对齐、卡头48、内容24、正文列与资料列实际边界核对");
            }
            CheckFailure(mp, mpNames);
            settings.Ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(settings.Ui);
            await LayoutAsync(mp);
            Require(Get<Border>(mp, "SeasonPanelHeader").Visibility == Visibility.Collapsed
                && !Get<ShelfHead>(mp, "SeasonHead").StoreCard, "订阅沉浸未恢复原标题");
            settings.Ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(settings.Ui);
            await LayoutAsync(mp);
            RequireStoreDetailCards(mp, mpNames);
            mp.Release();
            reads.Add("同页隐藏恢复、模式往返、主题与资源替换、错误背景和留白反例均通过；匿名假服务、未播放。");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ShellPrefs.ApplyAppearance(new());
            ShellPrefs.ApplyDetailLayout(new());
            ThemeHost.Apply(savedTheme);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "store-detail-cards.txt"), reads);
        }
    }

    /// <summary>
    /// 2026-11-16 用户参考图「把商店页面的连播开关改为这种」：商店档这颗是开关（<c>StoreContinueSwitch</c>），
    /// 挂在集卡右上角。先按指针路径确认它点得到，再核对开/关两档写回，并确认开态的轨道真的画成主题色。
    /// </summary>
    private async Task CheckStoreContinueAsync(DetailPage page, List<string> reads)
    {
        var toggle = Get<ToggleSwitch>(page, "StoreContinueSwitch");
        Require(toggle.Visibility == Visibility.Visible && toggle.IsEnabled
            && toggle.ActualWidth > 20 && toggle.ActualHeight > 20, "商店连播开关不可见或不可点");
        toggle.StartBringIntoView();
        await LayoutAsync(page);
        var centre = toggle.TransformToVisual(_root)
            .TransformPoint(new(toggle.ActualWidth / 2, toggle.ActualHeight / 2));
        var hits = VisualTreeHelper.FindElementsInHostCoordinates(centre, _root).ToList();
        Require(hits.Contains(toggle),
            $"商店连播开关点不到（中心 {centre.X:0.#},{centre.Y:0.#}）：{HitChain(hits)}");
        var accent = ((SolidColorBrush)Application.Current.Resources["EgAccentBrush"]).Color;
        var before = page.ViewModel.ContinuePlay;
        try
        {
            toggle.IsOn = false;
            await LayoutAsync(page);
            Require(!page.ViewModel.ContinuePlay, "商店连播开关关着时没有写回视图模型");
            toggle.IsOn = true;
            await LayoutAsync(page);
            Require(page.ViewModel.ContinuePlay, "商店连播开关打开后没有写回视图模型");
            // 属性对了不算数：把这颗开关自己渲染进位图，数实际画出来的主题色像素。
            var (accentPixels, white, total) = await CountPixelsAsync(toggle, accent);
            reads.Add($"商店连播开关：命中链 {HitChain(hits)}，开态主题色 {accent} 像素 {accentPixels}/{total}（白 {white}）");
            Require(accentPixels > 0,
                $"商店连播开关开着时轨道没有画出主题色（{accentPixels}/{total} 像素，主题色 {accent}）");
        }
        finally
        {
            toggle.IsOn = before;
            await LayoutAsync(page);
        }
        reads.Add($"商店连播开关：命中链 {HitChain(hits)}，按钮在 {centre.X:0.#},{centre.Y:0.#}；开/关写回通过");
    }

    private static string HitChain(IEnumerable<DependencyObject> hits) => string.Join(" > ", hits.Take(5).Select(node =>
        node is FrameworkElement element && element.Name.Length > 0
            ? node.GetType().Name + "/" + element.Name
            : node.GetType().Name));

    /// <summary>
    /// 把一个控件渲染进自己的位图，数目标色/白像素。预乘 BGRA：不透明像素的分量就是原值。
    /// </summary>
    private async Task<(int Target, int White, int Total)> CountPixelsAsync(
        FrameworkElement element, Windows.UI.Color want)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var hits = 0;
        var white = 0;
        for (var at = 0; at + 3 < pixels.Length; at += 4)
        {
            var blue = pixels[at];
            var greenChannel = pixels[at + 1];
            var red = pixels[at + 2];
            var alpha = pixels[at + 3];
            if (alpha < 160) continue;
            if (Math.Abs(red - want.R) <= 30 && Math.Abs(greenChannel - want.G) <= 30 && Math.Abs(blue - want.B) <= 30)
                hits++;
            else if (red > 225 && greenChannel > 225 && blue > 225)
                white++;
        }
        return (hits, white, pixels.Length / 4);
    }

    private static void RequireStoreHeroAlignment(DetailPage page)
    {
        // 2026-11-15 用户令：商店模式里「已观看」那颗图标是固定的绿，不跟主题强调色走
        // （连播那颗当天稍后改为跟主题色，见 AcrylicControlsAsync 的连播图标检查）。
        var active = (SolidColorBrush)page.Resources["EgActionActiveBrush"];
        Require(active.Color == Windows.UI.Color.FromArgb(0xFF, 0x52, 0xB5, 0x4B),
            $"商店模式已观看图标不是绿色：{active.Color}");
        var cover = Get<Border>(page, "PosterStill");
        var overview = Get<StackPanel>(page, "OverviewPanel");
        var canvas = Get<Grid>(page, "StoreScrollContent");
        var left = cover.TransformToVisual(canvas).TransformPoint(default).X;
        // 剧情移到封面下方，文字与封面、文件选项同一条左沿。
        Require(overview.Padding.Left == 0 && overview.Padding.Right == 0, "简介文字保留了额外左右缩进");
        if (overview.Visibility == Visibility.Visible)
            Require(Math.Abs(overview.TransformToVisual(canvas).TransformPoint(default).X
                    - left) < 1,
                "简介没有和封面同一条左沿");
        if (page.ViewModel.PageWidth >= 600)
            Require(Get<StackPanel>(page, "FileOptions").ActualHeight > 0
                && Get<EgPicker>(page, "ColumnSourcePicker").ActualWidth > 0,
                "常规宽度没有呈现可测量的文件选项");
        // 封面下方的剧情、文件选项、操作与卡片保持同一条左沿。
        foreach (var name in new[] { "StoreHeroDetails", "FileOptions", "ColumnActions" })
        {
            var node = Get<FrameworkElement>(page, name);
            if (node.Visibility != Visibility.Visible || node.ActualWidth <= 0 || node.ActualHeight <= 0) continue;
            Require(Math.Abs(node.TransformToVisual(canvas).TransformPoint(default).X - left) < 1,
                name + " 未与封面、下方卡片外沿对齐");
        }
        if (overview.Visibility == Visibility.Visible && cover.Visibility == Visibility.Visible)
        {
            var coverBox = Bounds(cover, canvas);
            var overviewBox = Bounds(overview, canvas);
            Require(overview.Parent == Get<StackPanel>(page, "StoreHeroDetails")
                && overviewBox.Top >= coverBox.Bottom + 14,
                "商店剧情没有排在封面下方并保留间距");
        }
        var play = Get<Button>(page, "ColumnPlayButton");
        var more = Get<Button>(page, "ColumnMoreButton");
        var favorite = Get<Button>(page, "ColumnFavoriteButton");
        var watched = Get<Button>(page, "ColumnWatchedButton");
        var continuePlay = Get<ToggleButton>(page, "ColumnContinueButton");
        var continueEntry = Get<StackPanel>(page, "StoreContinueEntry");
        var actionRow = Get<WrapRow>(page, "ColumnActionRow");
        var coverActions = Get<Grid>(page, "StoreCoverActions");
        var playSlot = Get<Grid>(page, "StorePlaySlot");
        var actionBlock = Get<StackPanel>(page, "StoreActionBlock");
        var factsRow = Get<StackPanel>(page, "StoreRatingRow");
        var episodeHead = Get<ShelfHead>(page, "EpisodeHead");
        if (page.ViewModel.StoreMode)
        {
            Require(ReferenceEquals(play.Parent, playSlot)
                && new[] { watched, favorite, more }.All(button => ReferenceEquals(button.Parent, coverActions))
                && ReferenceEquals(episodeHead.Trailing, continueEntry),
                "商店主播放键未在三颗图标下方、三颗未排在同一行，或连播开关离开了集卡右上角");
            Require(continueEntry.Children.OfType<TextBlock>().Any(text => text.Text == "连播")
                && continueEntry.Children.OfType<ToggleSwitch>().Count() == 1,
                "商店连播开关缺少左边的「连播」标签或开关本身");
            Require(ReferenceEquals(actionBlock.Parent, Get<StackPanel>(page, "HeroStack")),
                "商店操作块没有排在片名那一栏里");
            RequireStoreCoverActionAppearance(page);
            // 2026-11-17 用户令「把商店模式下媒体详情页左上角的封面放大一点，让它上方和徽标上方平行，
            // 下方和播放按钮下方平行」：封面盖住右栏从徽标上沿到主播放键下沿这一段。这一页的封面是 400×600
            // 的 2:3 海报，只在「高由实测给、没有被 260 的宽上限收短」的那几档才把下沿对齐写死；
            // 360 这类压力尺寸上正文折行把右栏撑得很高，宽上限先到，封面就不追那一段了。
            var coverRoom = page.ViewModel.StoreCoverRoom;
            var coverFit = DetailHero.StoreStillBox(400, 600, coverRoom);
            if (cover.Visibility == Visibility.Visible && play.Visibility == Visibility.Visible
                && playSlot.ActualHeight > 0 && coverRoom >= DetailHero.StoreStillHeight
                && Math.Abs(coverFit.Height - coverRoom) < 1.5)
            {
                var coverBox = Bounds(cover, canvas);
                var plateRow = Get<Grid>(page, "DetailPlateRow");
                if (plateRow.Visibility == Visibility.Visible)
                    Require(Math.Abs(coverBox.Top - Bounds(plateRow, canvas).Top) < 1.5,
                        $"商店封面没有和徽标同一条上沿：封面 {coverBox.Top:0.##}、徽标 {Bounds(plateRow, canvas).Top:0.##}");
                var playBottom = Bounds(play, canvas).Bottom;
                Require(Math.Abs(coverBox.Bottom - playBottom) < 1.5,
                    $"商店封面下沿没有和主播放键下沿平行：封面 {coverBox.Bottom:0.##}、播放键 {playBottom:0.##}");
            }
            var facts = Bounds(factsRow, canvas);
            var row = Bounds(coverActions, canvas);
            var buttons = new[] { watched, favorite, more };
            var boxes = buttons.Select(button => Bounds(button, canvas)).ToArray();
            var playBox = Bounds(play, canvas);
            StoreDetailProbeEvidence.RequireActionStack(facts.Bottom, row.Left, row.Right, row.Top,
                boxes.Select(box => box.Top).ToArray(),
                buttons.Select(button => button.ActualWidth).ToArray(), buttons.Select(button => button.ActualHeight).ToArray(),
                buttons.Select(button => button.Opacity).ToArray(), buttons.SelectMany(button =>
                    new[] { button.BorderThickness.Left, button.BorderThickness.Top, button.BorderThickness.Right, button.BorderThickness.Bottom }).ToArray(),
                playBox.Left, playBox.Top, playBox.Width, playBox.Height);
            Require(buttons.SequenceEqual(coverActions.Children.OfType<Button>()), "操作行按钮次序不是已看、收藏、更多");
            var panel = Get<StackPanel>(page, "EpisodePanel");
            if (panel.Visibility == Visibility.Visible && continueEntry.Visibility == Visibility.Visible)
            {
                var panelBox = Bounds(panel, canvas);
                var continueBox = Bounds(continueEntry, canvas);
                Require(continueBox.Right <= panelBox.Right + 1 && continueBox.Top <= panelBox.Top + 72
                    && continueBox.Left >= panelBox.Left + panelBox.Width / 2,
                    "连播开关没有落在集列表卡片的右上角");
            }
        }
        else
        {
            Require(ReferenceEquals(play.Parent, actionRow) && ReferenceEquals(more.Parent, actionRow)
                && ReferenceEquals(favorite.Parent, actionRow) && ReferenceEquals(watched.Parent, actionRow)
                && ReferenceEquals(continuePlay.Parent, actionRow) && episodeHead.Trailing is null
                && continueEntry.Visibility == Visibility.Collapsed,
                "退出商店模式后操作键没有回到ColumnActions，或连播开关没有收起");
        }
        // 操作行宽窄与右侧预告的1024断点相互独立，不用IsCompact冒充预告宽窗条件。
        var pictureCorner = Get<Grid>(page, "StoreBackdropContent").CornerRadius;
        var trailerCorner = Get<Grid>(page, "StoreTrailerContent").CornerRadius;
        var expectedCorner = (CornerRadius)Application.Current.Resources["EgCardCornerRadius"];
        StoreDetailProbeEvidence.RequirePreviewCorners(page.ViewModel.StoreMode, page.ViewModel.PageWidth,
            DetailHero.StoreWideFloor, expectedCorner.TopLeft,
            CornerValues(pictureCorner), CornerValues(trailerCorner));
    }

    private static double[] CornerValues(CornerRadius corner) =>
        [corner.TopLeft, corner.TopRight, corner.BottomRight, corner.BottomLeft];

    private static void RequireStoreCoverActionAppearance(DetailPage page)
    {
        if (!page.ViewModel.StoreMode) return;
        var row = Get<Grid>(page, "StoreCoverActions");
        var slot = Get<Grid>(page, "StorePlaySlot");
        Require(row.Visibility == Visibility.Visible && row.Opacity == 1 && row.IsHitTestVisible
            && slot.Visibility == Visibility.Visible && slot.Opacity == 1 && slot.IsHitTestVisible,
            "商店操作块不是常驻可命中状态");
        var icons = row.Children.OfType<Button>().ToArray();
        Require(icons.Length == 3, "商店图标键不是三颗");
        foreach (var button in icons)
        {
            Require(button.Style == (Style)page.Resources["EgStoreCoverActionButtonStyle"]
                && button.BorderThickness == default && button.Opacity == 1 && button.IsTabStop,
                "图标按钮丢失平台按钮样式、无边框、常驻或键盘入口");
            Require(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))
                && ToolTipService.GetToolTip(button) is not null, "图标按钮缺少名称或提示");
            button.ApplyTemplate();
            if (!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            {
                // 2026-11-17 用户令「这三个按钮的框改为鼠标移到按钮上的时候才显示」：常态不铺框，
                // 指针移上去之后才由 PointerOver／Pressed 读那支固定面色；圆角和闭合选择框同一档。
                Require(button.Background is SolidColorBrush { Color.A: 0 }, "图标键常态仍然铺着框");
                RequireFixedSurface([button], restHidden: true);
                Require(button.CornerRadius == (CornerRadius)page.Resources["EgDetailPickerCornerRadius"],
                    "图标键圆角没有跟下方选择框同一档");
            }
            foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
            {
                Require(VisualStateManager.GoToState(button, state, false), "图标按钮缺少平台状态 " + state);
                Require(button.Opacity == 1 && row.Opacity == 1, "图标按钮仍被悬停状态隐藏");
            }
            VisualStateManager.GoToState(button, "Normal", false);
        }
        // 主播放键回到带字的主题色药丸（2026-11-16 用户令「还原成之前的样式」）。
        var play = slot.Children.OfType<Button>().Single();
        var accent = ((SolidColorBrush)Application.Current.Resources["EgAccentBrush"]).Color;
        // 滚过阈值后顶部悬浮栏会接手主播放键：原键按设计收成透明、不可命中，顶栏那颗顶上。
        // 这时核顶栏那颗可用；页面在顶时仍核原键常驻。
        var stickyHolding = !play.IsHitTestVisible && Get<bool>(page, "_headerShown");
        Require(play.Style == (Style)page.Resources["EgPlayButtonStyle"]
            && (stickyHolding ? Math.Abs(play.Opacity) < 0.01 && !play.IsTabStop : play.Opacity == 1 && play.IsHitTestVisible),
            $"主播放键没有回到带字的药丸样式或常驻状态（样式 {(ReferenceEquals(play.Style, page.Resources["EgPlayButtonStyle"]) ? "药丸" : play.Style is null ? "空" : "其他")}、"
            + $"透明度 {play.Opacity:0.##}、命中 {play.IsHitTestVisible}、顶栏接手 {stickyHolding}）");
        if (stickyHolding)
        {
            var sticky = Get<Button>(page, "StickyPlayButton");
            Require(sticky.Visibility == Visibility.Visible && sticky.IsHitTestVisible && sticky.IsEnabled,
                "主播放键被顶栏接手后，顶栏那颗播放键不可用");
        }
        Require(play.Background is AcrylicBrush { AlwaysUseFallback: true } fill
            && fill.FallbackColor == accent && Math.Abs(fill.Opacity - 1) < 0.000001,
            "主播放键没有涂上主题色面");
        // 不移动用户鼠标，显式清封面悬停入口后仍须全部显示。
        Call(page, "HideCoverHover");
        Require(row.Opacity == 1 && icons.All(button => button.Opacity == 1),
            "离开封面后操作块被隐藏");
    }

    private static void CheckEdges(FrameworkElement cover, IEnumerable<StackPanel> panels, FrameworkElement canvas)
    {
        var coverLeft = cover.TransformToVisual(canvas).TransformPoint(default).X;
        foreach (var panel in panels.Where(panel => panel.Visibility == Visibility.Visible && panel.ActualHeight > 0))
        {
            var left = panel.TransformToVisual(canvas).TransformPoint(default).X;
            var right = canvas.ActualWidth - left - panel.ActualWidth;
            Require(Math.Abs(left - DetailHero.StorePageInset(canvas.ActualWidth)) < 1
                && Math.Abs(left - right) < 1 && Math.Abs(left - coverLeft) < 1,
                $"{panel.Name} 两侧留白或封面对齐错误：左 {left:0.##} 右 {right:0.##} 封面 {coverLeft:0.##}");
        }
    }

    private static void CheckSubscriptionEdges(MoviePilotSubscriptionPage page, string[] names)
    {
        var canvas = Get<Grid>(page, "StoreScrollContent");
        var cover = Bounds(Get<Border>(page, "DetailPoster"), canvas);
        var flow = Get<StackPanel>(page, "BodyFlow");
        var row = Get<Grid>(page, "BodyRow");
        var facts = Get<Border>(page, "HeroFacts");
        var flowBox = Bounds(flow, canvas);
        var factsBox = Bounds(facts, canvas);
        var inset = DetailHero.StorePageInset(canvas.ActualWidth);
        var side = Grid.GetColumn(facts) == 1;
        Require(side == (row.ActualWidth >= 960), "订阅正文列没有按实际宽度切换并排与堆叠");
        StoreDetailProbeEvidence.RequireSubscriptionColumns(canvas.ActualWidth, inset, cover.Left,
            flowBox.Left, flowBox.Right, factsBox.Left, factsBox.Right, row.ColumnSpacing, side);
        Require(side ? Math.Abs(facts.ActualWidth - 288) < 1 && Grid.GetRow(flow) == 0
            : Grid.GetRow(flow) == 1 && Grid.GetRow(facts) == 0
                && Math.Abs(flowBox.Top - factsBox.Bottom - row.RowSpacing - facts.Margin.Bottom - flow.Margin.Top) < 1
                && Math.Abs(facts.ActualWidth - Math.Min(520, row.ActualWidth)) < 1,
            "订阅资料列宽度或窄窗堆叠顺序与间距错误");
        foreach (var name in names)
        {
            var panel = Get<StackPanel>(page, name);
            if (panel.Visibility != Visibility.Visible || panel.ActualHeight <= 0) continue;
            var box = Bounds(panel, canvas);
            Require(Math.Abs(box.Left - flowBox.Left) < 1 && Math.Abs(box.Right - flowBox.Right) < 1,
                name + " 没有铺满订阅正文列或越过资料列边界");
        }
    }

    private static void CheckFailure(object page, string[] names)
    {
        var panel = Get<StackPanel>(page, names[0]);
        var background = panel.Background;
        try
        {
            panel.Background = null;
            var rejected = false;
            try { RequireStoreDetailCards(page, names); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "卡片检查未拒绝缺失背景");
        }
        finally { panel.Background = background; }
        var header = (Border)panel.Children[0];
        var padding = header.Padding;
        try
        {
            header.Padding = new Thickness(1);
            var rejected = false;
            try { RequireStoreDetailCards(page, names); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "卡片检查未拒绝错误标题留白");
        }
        finally { header.Padding = padding; }
    }
}
