using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        series.People = [new() { Id = "card-person", Name = "示例演员", Type = "Actor", PrimaryImageTag = "fixture" }];
        series.ImageTags["Primary"] = "fixture";
        series.BackdropImageTags.Add("fixture");
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) }
                : path.EndsWith("/Items/show", StringComparison.Ordinal) ? Json(series)
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
            foreach (var width in new[] { 1240, 760, 360, 1240 })
            {
                ResizeProbeClient(width, 900);
                await UntilAsync(() => Math.Abs(page.ActualWidth - width) < 2);
                await LayoutAsync(page);
                RequireStoreDetailCards(page, names);
                var cover = Get<Border>(page, "PosterStill");
                var panels = names.Select(name => Get<StackPanel>(page, name)).ToArray();
                CheckEdges(cover, panels, Get<Grid>(page, "StoreScrollContent"));
                RequireCardOrder(page, fixture.Settings.Ui.SeriesCards.Select(card => card.Key));
                panels.First(panel => panel.Visibility == Visibility.Visible).StartBringIntoView();
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"store-detail-cards-media-{width}.png");
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
            fixture.Settings.Ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(page);
            RequireStoreDetailCards(page, names);
            CheckFailure(page, names);
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
            reads.Add("媒体旧模式检查：1200/760 商店与沉浸往返、主体与分区身份、固定按钮材质均通过。");
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
                await UntilAsync(() => Math.Abs(mp.ActualWidth - width) < 2);
                await LayoutAsync(mp);
                RequireStoreDetailCards(mp, mpNames);
                CheckEdges(Get<Border>(mp, "DetailPoster"), mpNames.Select(name => Get<StackPanel>(mp, name)), Get<Grid>(mp, "StoreScrollContent"));
                Get<StackPanel>(mp, "SeasonPanel").StartBringIntoView();
                await LayoutAsync(mp);
                await SaveProbeScreenAsync($"store-detail-cards-subscription-{width}.png");
                reads.Add($"订阅 {width}: 卡片与封面左沿对齐、卡头48、内容24、两侧对称");
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
