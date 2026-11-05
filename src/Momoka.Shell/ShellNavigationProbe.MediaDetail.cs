using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotMediaBrowsingAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        using var fixture = CreateFixture();
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        page.Attach(new(service, subscription, Launcher: fixture.Launcher));
        try
        {
            var vm = page.MediaViewModel;
            await UntilAsync(() => vm.IsReady && page.ViewModel.IsReady);
            Require(vm.Detail is not null && vm.Groups.Count == 3 && vm.Seasons.Count == 2, "媒体详情、剧集组或默认季没有载入：" + vm.NoticeMessage);
            Require(vm.Facts.Any(fact => fact.Value == "The Long Journey"), "没有展示真实媒体元数据");
            var groups = vm.Groups;
            var seasons = vm.Seasons;
            await vm.SelectGroupAsync(vm.Groups.Single(row => row.Group.Id == "grp-9"));
            Require(vm.Seasons.Single().Season.Number == 3 && ReferenceEquals(groups, vm.Groups) && ReferenceEquals(seasons, vm.Seasons), "切组没有保留实际季号或集合实例");
            vm.Seasons[0].Expanded = true;
            await UntilAsync(() => vm.Seasons[0].Episodes.Count == 4);
            Require(server.Writes.Count == 0, "浏览剧集组或展开季修改了订阅");
            foreach (var width in new[] { 1240, 760, 360 })
            {
                ResizeInspect(width, 1000);
                await LayoutAsync(page);
                Get<MoviePilotMediaSeasonsView>(page, "MediaSeasons").StartBringIntoView();
                await LayoutAsync(page);
                await SaveSettledFrameAsync(page, $"mp-media-seasons-{width}.png");
            }
            var resources = Get<MoviePilotResourcePanel>(page, "MediaResources");
            var search = Descendants(page).OfType<Button>().First(button => button.Name == "SearchMediaEpisode");
            InvokeSubscriptionButton(search);
            Require(resources.Visibility == Visibility.Visible && Get<ScrollViewer>(page, "DetailScroll").Visibility == Visibility.Collapsed,
                "集搜索没有打开真实资源面板或底层页面仍可交互");
            Require(server.Writes.Count == 0, "打开资源面板触发订阅或下载");
        }
        finally { page.Release(); _root.Children.Remove(page); ResizeInspect(1420, 980); }
    }

    private async Task MoviePilotSeasonSelectionAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var dialog = new MoviePilotSeasonPicker { XamlRoot = _root.XamlRoot };
        dialog.ViewModel.Attach(service, subscription.Media, picker: true);
        ResizeInspect(760, 1000);
        try
        {
            await dialog.ViewModel.ReloadAsync();
            var showing = dialog.ShowAsync();
            await LayoutAsync(dialog);
            var list = Get<ItemsRepeater>(dialog, "SubscribeSeasons");
            var first = (FrameworkElement)list.GetOrCreateElement(0);
            var second = (FrameworkElement)list.GetOrCreateElement(1);
            Descendants(first).OfType<CommunityToolkit.WinUI.Controls.Segmented>().Single().SelectedIndex = 2;
            Descendants(second).OfType<ToggleSwitch>().Single().IsOn = true;
            // 模式区域显示并初始化后才是用户能够选择的状态，先等这一帧完成。
            await LayoutAsync(dialog);
            Descendants(second).OfType<CommunityToolkit.WinUI.Controls.Segmented>().Single().SelectedIndex = 1;
            await LayoutAsync(dialog);
            Require(dialog.ViewModel.Seasons[1].ModeIndex == 1, "第二季模式选择没有写回草稿");
            Require(dialog.PrimaryButtonText == "提交订阅" && dialog.IsPrimaryButtonEnabled, "选中季后提交按钮没有恢复");
            Require(dialog.ViewModel.Plan().Count == 2 && server.Writes.Count == 0, "季选择或模式草稿提前写入服务器");
            var pickerBody = Get<StackPanel>(dialog, "PickerBody");
            var modeControls = Descendants(dialog).OfType<CommunityToolkit.WinUI.Controls.Segmented>().ToArray();
            Require(modeControls.Length == 2 && modeControls.All(mode => mode.ActualWidth <= pickerBody.ActualWidth), "订阅方式超出季卡片宽度");
            var originalTheme = ThemeHost.Current.Id;
            try
            {
                foreach (var theme in Momoka.Theming.UiThemes.All)
                {
                    ThemeHost.Apply(theme.Id);
                    await LayoutAsync(dialog);
                    await SaveProbeScreenAsync($"mp-subscribe-season-picker-{theme.Id}-760.png");
                }
            }
            finally { ThemeHost.Apply(originalTheme); }
            await SaveProbeScreenAsync("mp-subscribe-season-picker-760.png");
            InvokeSubscriptionButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            await showing;
            Require(server.Writes.Count == 2 && server.Items[4]["best_version_full"]!.GetValue<int>() == 1, "已有季没有改为全集洗版");
            var added = server.Items.Values.Single(item => item["id"]!.GetValue<int>() > 5);
            Require(added["season"]!.GetValue<int>() == 2 && added["best_version"]!.GetValue<int>() == 1 && added["best_version_full"]!.GetValue<int>() == 0, "新增季没有使用分集洗版");
            Require(server.WriteBodies[0].Count == 3, "模式变更覆盖了无关订阅字段");
            dialog.ViewModel.Cancel();
            var cancelled = new MoviePilotSeasonPicker { XamlRoot = _root.XamlRoot };
            cancelled.ViewModel.Attach(service, subscription.Media, picker: true);
            await cancelled.ViewModel.ReloadAsync();
            showing = cancelled.ShowAsync();
            await LayoutAsync(cancelled);
            cancelled.ViewModel.Seasons[0].Selected = false;
            ResizeInspect(360, 900); await LayoutAsync(cancelled);
            var narrowBody = Get<StackPanel>(cancelled, "PickerBody");
            Require(Descendants(cancelled).OfType<CommunityToolkit.WinUI.Controls.Segmented>().All(mode => mode.ActualWidth <= narrowBody.ActualWidth), "窄窗订阅方式被裁切");
            await SaveProbeScreenAsync("mp-subscribe-season-picker-360.png");
            // 关闭按钮的文案会随产品改（2026-10-25 起是「取消」，之前是「关闭」），探针不钉文案：
            // 只按 ContentDialog 模板里的 CloseButton 名字定位，再与对话框自己的 CloseButtonText 核对一次，
            // 确认挑中的是这颗按钮而不是别处的同名控件。
            var close = Descendants(cancelled).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, cancelled.CloseButtonText));
            InvokeSubscriptionButton(close);
            await showing;
            cancelled.ViewModel.Cancel();
            Require(server.Writes.Count == 2 && server.Items.ContainsKey(4), "关闭弹窗仍取消了订阅");
            var emptyMedia = subscription.Media with { MediaId = "9", Season = null };
            var empty = new MoviePilotSeasonPicker { XamlRoot = _root.XamlRoot };
            empty.ViewModel.Attach(service, emptyMedia, picker: true);
            await empty.ViewModel.ReloadAsync();
            ResizeInspect(760, 1000);
            showing = empty.ShowAsync();
            await LayoutAsync(empty);
            var emptyModeRows = Descendants(empty).OfType<CommunityToolkit.WinUI.Controls.Segmented>().ToArray();
            Require(emptyModeRows.Length == 2 && emptyModeRows.All(panel => panel.Visibility == Visibility.Collapsed), "未选季仍显示模式选择");
            Require(!empty.IsPrimaryButtonEnabled && empty.PrimaryButtonText == "请选择订阅季", "未选季时提交按钮没有禁用或没有改为「请选择订阅季」");
            await SaveProbeScreenAsync("mp-subscribe-season-picker-none-760.png");
            empty.Hide(); await showing; empty.ViewModel.Cancel();
            server.FailExists = true;
            var failed = new MoviePilotSeasonPicker { XamlRoot = _root.XamlRoot };
            failed.ViewModel.Attach(service, emptyMedia, picker: true);
            await failed.ViewModel.ReloadAsync();
            showing = failed.ShowAsync();
            await LayoutAsync(failed);
            var retry = Get<Button>(failed, "RetrySeasonStates");
            Require(retry.Visibility == Visibility.Visible, "入库状态失败没有重试入口");
            var failedList = Get<ItemsRepeater>(failed, "SubscribeSeasons");
            Descendants((FrameworkElement)failedList.GetOrCreateElement(0)).OfType<ToggleSwitch>().Single().IsOn = true;
            await LayoutAsync(failed);
            Require(!failed.ViewModel.CanSubmit, "入库状态未知且未显式选择模式时仍可提交");
            failed.ViewModel.Seasons[0].EpisodesMode = true;
            failed.ViewModel.Seasons[0].MarkModePicked();
            await LayoutAsync(failed);
            Require(failed.ViewModel.CanSubmit, "显式选择模式后仍不能提交");
            Require(failed.IsPrimaryButtonEnabled, "显式选择模式后提交按钮仍禁用");
            await SaveProbeScreenAsync("mp-subscribe-season-picker-failed-760.png");
            server.FailExists = false;
            InvokeSubscriptionButton(retry);
            await UntilAsync(() => failed.ViewModel.Seasons[0].Status != "入库状态未知");
            Require(failed.ViewModel.Seasons[0].Status == "部分入库 · 1 / 4 集", "重试没有恢复入库状态");
            Require(failed.ViewModel.SeasonStateRetryVisibility == Visibility.Collapsed, "重试成功后仍保留重试入口");
            failed.Hide(); await showing; failed.ViewModel.Cancel();
        }
        finally { dialog.Hide(); dialog.ViewModel.Cancel(); ResizeInspect(1420, 980); }
    }

    private async Task MoviePilotMediaReadRacesAsync()
    {
        using var server = new SubscriptionProbeServer();
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var vm = new MoviePilotMediaViewModel();
        vm.Attach(service, subscription.Media);
        try
        {
            var exists = Pending<System.Net.Http.HttpResponseMessage>();
            server.PendingExists = exists;
            var loading = vm.ReloadAsync();
            await UntilAsync(() => vm.Seasons.Count == 2);
            var row = vm.Seasons[0];
            await vm.LoadEpisodesAsync(row);
            Require(row.Episodes[0].Exists is null, "入库统计未返回就显示了确定状态");
            exists.SetResult(Json(new { success = true, data = new Dictionary<string, int[]> { ["1"] = [1] } }));
            await loading;
            Require(row.Episodes[0].Exists == true && row.Episodes[1].Exists == false, "入库统计迟到没有更新已展开集");
            server.PendingExists = null;
            var group = Pending<System.Net.Http.HttpResponseMessage>();
            server.PendingGroup = group;
            var old = vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "grp-9"));
            await vm.SelectGroupAsync(vm.Groups.Single(item => item.Group.Id == "grp-10"));
            group.SetResult(Json(new[] { new { season_number = 99, episode_count = 4 } }));
            await old;
            Require(vm.GroupId == "grp-10" && vm.Seasons.Single().Season.Number == 7, "旧剧集组迟到覆盖当前组");
            vm.Cancel();
            Require(vm.Seasons.Count == 0 && !vm.CanInteract && server.Writes.Count == 0, "离页后仍保留可操作的旧数据");
        }
        finally { vm.Cancel(); }
    }

    private async Task MoviePilotMediaBackgroundAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var page = new MoviePilotSubscriptionPage();
        var originalTheme = ThemeHost.Current.Id;
        _root.Children.Add(page);
        page.Attach(new(service, subscription));
        static Windows.Foundation.Rect Box(UIElement anchor, FrameworkElement element) => element
            .TransformToVisual(anchor)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        try
        {
            ResizeInspect(1240, 1000);
            await UntilAsync(() => page.ViewModel.IsReady && page.MediaViewModel.IsReady && page.MediaViewModel.Backdrop is not null);
            await LayoutAsync(page);

            // 背景这一层照电影详情页那一套（用户令 2026-10-13「参考电影页面的背景图修改订阅页面的背景图」
            // 「下方组件背景也参考电影页面做下滑遮罩」）：图按页宽等比、顶对齐铺在页面顶部；罩子只盖住图那一屏；
            // 图下沿一道黑色渐变（下滑遮罩）；下方组件整段站在不透明的主题纸面上。
            // 这一条此前钉的是「图撑满整页、Height 是 NaN、没有圆角边框」—— 那个前提随本轮改动作废：图现在有定高
            // （DetailHero.PictureHeight），而「按页宽等比」和「盖住整页」本来就是两回事。保留的覆盖是三条读数：
            // 图的高与 Core 那支纯函数逐像素一致、罩子正好盖住图、纸面接在头部下沿上。
            var scroll = Get<ScrollViewer>(page, "DetailScroll");
            var image = Get<Border>(page, "DetailBackdropBand");
            var scrim = Get<Border>(page, "DetailBackdropScrim");
            var fade = Get<Border>(page, "DetailBackdropFade");
            var sheet = Get<StackPanel>(page, "BodySheet");
            var hero = Get<StackPanel>(page, "HeroSection");
            var expected = DetailHero.PictureHeight(scroll.ActualWidth, page.MediaViewModel.BackdropRatio,
                scroll.ActualHeight, page.MediaViewModel.HeroBandHeight);
            var imageBox = Box(page, image);
            var scrimBox = Box(page, scrim);
            var fadeBox = Box(page, fade);
            var sheetBox = Box(page, sheet);
            var heroBox = Box(page, hero);
            Require(Math.Abs(image.ActualWidth - page.ActualWidth) < 2
                && image.VerticalAlignment == VerticalAlignment.Top
                && image.CornerRadius.TopLeft == 0 && image.BorderThickness.Left == 0
                && Math.Abs(image.ActualHeight - expected) < 2
                && image.ActualHeight <= scroll.ActualHeight + 2,
                $"背景图没有按页宽等比铺在页面顶部：图 {image.ActualWidth:0}×{image.ActualHeight:0}、"
                + $"页面 {page.ActualWidth:0}×{page.ActualHeight:0}、该是 {expected:0}");
            Require(Math.Abs(scrimBox.Top - imageBox.Top) < 1.5 && Math.Abs(scrimBox.Height - imageBox.Height) < 1.5,
                $"罩子没有正好盖住图那一屏：罩子 {scrimBox.Top:0}–{scrimBox.Bottom:0}、图 {imageBox.Top:0}–{imageBox.Bottom:0}");
            Require(fadeBox.Height > 0 && Math.Abs(fadeBox.Bottom - imageBox.Bottom) < 1.5
                && fade.Background is LinearGradientBrush { GradientStops.Count: 4 },
                $"图下沿那道下滑遮罩没钉在画面的下沿上：渐变 {fadeBox.Top:0}–{fadeBox.Bottom:0}、图下沿 {imageBox.Bottom:0}");
            Require(sheet.Background is SolidColorBrush { Color.A: 255 }
                && Math.Abs(sheet.ActualWidth - page.ActualWidth) < 2
                && Math.Abs(sheetBox.Top - heroBox.Bottom) < 1.5
                && Descendants(sheet).OfType<MoviePilotMediaSeasonsView>().Any(),
                $"下方组件没有站在不透明的纸面上：纸 {sheetBox.Top:0}–{sheetBox.Bottom:0}、头部下沿 {heroBox.Bottom:0}");

            var before = image.TransformToVisual(page).TransformPoint(default);
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id); await LayoutAsync(page);
                await SaveSettledFrameAsync(page, $"mp-media-background-{theme.Id}.png");
            }
            var seasons = Get<MoviePilotMediaSeasonsView>(page, "MediaSeasons");
            seasons.StartBringIntoView();
            await LayoutAsync(page);
            var season = page.MediaViewModel.Seasons.Single(row => row.Files is not null);
            var expander = Descendants(seasons).OfType<Expander>().Single(control => control.IsExpanded);
            var header = Descendants(expander).OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>()
                .Single(control => control.Name == "ExpanderHeader");
            var summary = Descendants((Grid)expander.Header).OfType<TextBlock>().Single(text => text.Text == page.ViewModel.FileSummary);
            var originalSummary = page.ViewModel.FileSummary;
            foreach (var status in new[] { "正在读取文件统计…", "文件统计读取失败", originalSummary })
            {
                page.ViewModel.FileSummary = status;
                Require(summary.Text == status, "季标题没有同步统计的加载、失败或恢复状态");
            }
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                foreach (var expanded in new[] { false, true })
                {
                    // 操作真实头部控件，验证模板到视图模型的双向绑定；不移动用户的鼠标。
                    header.IsChecked = expanded;
                    await LayoutAsync(page);
                    Require(expander.IsExpanded == expanded && season.Expanded == expanded, "季折叠状态未同步");
                    Require(summary.Text == originalSummary, "折叠切换丢失了入库摘要");
                    Require(header.Background is Microsoft.UI.Xaml.Media.SolidColorBrush headerBrush
                        && headerBrush.Color == ThemeHost.ToColor(theme.Colors.SurfaceAlt)
                        && expander.Background is Microsoft.UI.Xaml.Media.SolidColorBrush contentBrush
                        && contentBrush.Color == headerBrush.Color, "季标题与展开区域没有跟随当前主题");
                    var offset = seasons.TransformToVisual((UIElement)scroll.Content).TransformPoint(default).Y;
                    scroll.ChangeView(null, offset - 16, null, true);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"mp-season-{theme.Id}-{(expanded ? "expanded" : "collapsed")}-screen.png");
                }
            }
            scroll.ChangeView(null, 300, null, true);
            await LayoutAsync(page);
            var after = image.TransformToVisual(page).TransformPoint(default);
            Require(before.Y == after.Y && before.X == after.X, "背景跟着正文滚走");
        }
        finally { page.Release(); _root.Children.Remove(page); ThemeHost.Apply(originalTheme); ResizeInspect(1420, 980); }
    }

    /// <summary>
    /// 订阅详情页的大窗模式（2026-11-03 用户令「给 mp 订阅页面也增强大窗模式」，同日追加「拉长窗口导致背景图
    /// 快要被裁切时也要自动切换」）：窗口最大化后，正文纸面换成半透明洗色、面板的底与框淡到 0、头部不再撑
    /// 第一屏、背景铺到窗口下沿并抬到 0.96，图下沿的黑色渐变收起；还原窗口后全部回到纸面版式。另在宽版式里
    /// 把窗口压成一扇「图快被裁掉」的横窗（窗口没铺开），确认只因裁切也进大窗模式，拉高退线后回到纸面。
    /// 每一次翻面都要走过渡动画（以 <c>ShowcaseAnimations</c> 计数证明）。
    /// </summary>
    private async Task MoviePilotSubscriptionShowcaseAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync(), Related = true };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        page.Attach(new(service, subscription));
        var readings = new List<string>();
        try
        {
            // 一条用例自己把窗口形态摆干净：先还原（最大化时 Resize 会被忽略），再给一扇宽版式、
            // 但图还够不着视口的窗口（1200×800 DIP 下 16:10 的图约 750 高）。
            if (_window!.IsMaximized) _window.ToggleMaximize();
            ResizeProbeClient(1200, 800);
            page.AttachWindow(_window);
            await UntilAsync(() => page.ViewModel.IsReady && page.MediaViewModel.IsReady
                && page.MediaViewModel.Backdrop is not null);
            await LayoutAsync(page);

            var media = page.MediaViewModel;
            var sheet = Get<StackPanel>(page, "BodySheet");
            var image = Get<Border>(page, "DetailBackdropBand");
            var fade = Get<Border>(page, "DetailBackdropFade");
            var hero = Get<StackPanel>(page, "HeroSection");
            var panel = Get<StackPanel>(page, "PeoplePanel");

            Require(!media.Showcase && !media.Spacious && !media.NearCrop, "未铺开的窗口不该进大窗模式");
            Require(media.WideLayout, "起始窗口不在宽版式，测不出裁切那条来路");
            Require(sheet.Background is SolidColorBrush { Color.A: 255 }, "纸面版式的正文纸不是不透明");
            Require(fade.Visibility == Visibility.Visible, "纸面版式下图下沿的黑色渐变被收起");
            Require(Math.Abs(image.Opacity - DetailHero.BackdropOpacity) < 0.001,
                $"纸面版式的背景不透明度不是 {DetailHero.BackdropOpacity}：{image.Opacity:0.###}");

            var beforeEnter = page.ShowcaseAnimations;
            if (!_window.IsMaximized) _window.ToggleMaximize();
            await UntilAsync(() => media.Showcase && !page.ShowcaseAnimating
                && Math.Abs(image.Opacity - DetailHero.ShowcaseBackdropOpacity) < 0.001
                && hero.MinHeight <= 0.5);
            Require(media.Showcase && media.Spacious && media.WideLayout,
                $"最大化后没有进入大窗模式：铺开={media.Spacious}、宽版式={media.WideLayout}、大窗={media.Showcase}");
            if (HomeMotion.AnimationsEnabled)
                Require(page.ShowcaseAnimations > beforeEnter,
                    "最大化进入大窗时没有走过渡动画（ShowcaseAnimations 没有增加）");
            var sheetBrush = sheet.Background as SolidColorBrush;
            Require(sheetBrush is { } && sheetBrush.Color.A == DetailHero.ScrimCeiling,
                "大窗模式的正文纸不是尾部那道半透明洗色");
            Require(panel.Background is AcrylicBrush { Opacity: 0 }, "大窗模式的正文面板底没有淡到 0");
            Require(fade.Visibility == Visibility.Collapsed, "大窗模式下图下沿的黑色渐变没有收起");
            Require(!media.ShowShelfLines, "大窗模式的货架标题仍然画着横线");
            var expectedImage = DetailHero.ImmersivePictureHeight(media.Viewport, media.HeaderLift);
            Require(Math.Abs(image.ActualHeight - expectedImage) < 2,
                $"大窗模式的背景没有铺到窗口下沿：图 {image.ActualHeight:0}、该是 {expectedImage:0}");
            Require(Math.Abs(hero.ActualHeight - media.HeroBandHeight) < 2,
                $"大窗模式的头部没有收到内容高：头部 {hero.ActualHeight:0}、内容 {media.HeroBandHeight:0}");
            readings.Add($"大窗：视口 {media.Viewport:0}、抬升 {media.HeaderLift:0}、图 {image.ActualHeight:0}、"
                + $"头部 {hero.ActualHeight:0}、纸面透明度 {sheetBrush?.Color.A}、面板 {panel.Background.Opacity:0.##}、"
                + $"动画 {page.ShowcaseAnimations}");
            await SaveProbeScreenAsync("mp-subscription-showcase-maximized.png");

            var beforeExit = page.ShowcaseAnimations;
            _window.ToggleMaximize();
            await UntilAsync(() => !media.Showcase && !media.Spacious && !page.ShowcaseAnimating
                && Math.Abs(image.Opacity - DetailHero.BackdropOpacity) < 0.001
                && hero.MinHeight > 0.5);
            if (HomeMotion.AnimationsEnabled)
                Require(page.ShowcaseAnimations > beforeExit,
                    "从最大化还原时没有走过渡动画（ShowcaseAnimations 没有增加）");
            var restored = sheet.Background as SolidColorBrush;
            Require(restored is { } && restored.Color.A == 255, "还原后正文纸没有回到不透明");
            Require(panel.Background is AcrylicBrush { Opacity: 1 }, "还原后正文面板的底没有回来");
            Require(fade.Visibility == Visibility.Visible, "还原后图下沿的黑色渐变没有回来");
            Require(media.ShowShelfLines, "还原后货架标题的横线没有回来");
            readings.Add($"纸面：视口 {media.Viewport:0}、图 {image.ActualHeight:0}、头部下限 {hero.MinHeight:0}、"
                + $"纸面透明度 {restored?.Color.A}、面板 {panel.Background.Opacity:0.##}、动画 {page.ShowcaseAnimations}");
            await SaveProbeScreenAsync("mp-subscription-showcase-paper.png");

            // 裁切那条来路：窗口没有铺开，但按页宽铺开的图（1080 × 0.625 ≈ 675）已经高过视口 480 的 98%。
            var beforeCrop = page.ShowcaseAnimations;
            ResizeProbeClient(1080, 480);
            await UntilAsync(() => media.NearCrop && media.Showcase && !page.ShowcaseAnimating
                && Math.Abs(image.Opacity - DetailHero.ShowcaseBackdropOpacity) < 0.001);
            Require(!media.Spacious, "裁切用例的窗口已经铺开，测不出只因裁切才切换");
            Require(media.WideLayout, "裁切用例落进了窄版式，测不出宽版式下的大窗模式");
            if (HomeMotion.AnimationsEnabled)
                Require(page.ShowcaseAnimations > beforeCrop,
                    "拉宽触发裁切时没有走过渡动画（ShowcaseAnimations 没有增加）");
            var cropBrush = sheet.Background as SolidColorBrush;
            Require(cropBrush is { } && cropBrush.Color.A == DetailHero.ScrimCeiling,
                "裁切触发后正文纸没有换成半透明洗色");
            Require(panel.Background is AcrylicBrush { Opacity: 0 }, "裁切触发后正文面板底没有淡到 0");
            Require(fade.Visibility == Visibility.Collapsed, "裁切触发后图下沿的黑色渐变没有收起");
            readings.Add($"裁切：页宽 {media.PageWidth:0}、视口 {media.Viewport:0}、图比 {media.BackdropRatio:0.###}、"
                + $"纸面透明度 {cropBrush?.Color.A}、面板 {panel.Background.Opacity:0.##}、动画 {page.ShowcaseAnimations}");
            await SaveProbeScreenAsync("mp-subscription-showcase-crop.png");

            // 把窗口拉高一点、退回 98% 以下：应当回到纸面版式（仍在宽版式内，过渡计数继续增加）。
            var beforeUncrop = page.ShowcaseAnimations;
            ResizeProbeClient(1080, 700);
            await UntilAsync(() => !media.NearCrop && !media.Showcase && !page.ShowcaseAnimating
                && Math.Abs(image.Opacity - DetailHero.BackdropOpacity) < 0.001);
            Require(media.WideLayout, "退出裁切用例落进了窄版式，测不出同一档版面内的还原");
            if (HomeMotion.AnimationsEnabled)
                Require(page.ShowcaseAnimations > beforeUncrop, "收回裁切线时没有走过渡动画");
            var uncropBrush = sheet.Background as SolidColorBrush;
            Require(uncropBrush is { } && uncropBrush.Color.A == 255, "收回裁切线后正文纸没有回到不透明");
            Require(panel.Background is AcrylicBrush { Opacity: 1 }, "收回裁切线后正文面板的底没有回来");
            readings.Add($"收回：页宽 {media.PageWidth:0}、视口 {media.Viewport:0}、裁切 {media.NearCrop}、"
                + $"纸面透明度 {uncropBrush?.Color.A}、面板 {panel.Background.Opacity:0.##}、动画 {page.ShowcaseAnimations}");
            await SaveProbeScreenAsync("mp-subscription-showcase-uncropped.png");
        }
        finally
        {
            if (_window?.IsMaximized == true) _window.ToggleMaximize();
            page.Release();
            _root.Children.Remove(page);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "mp-subscription-showcase.txt"), readings);
            ResizeInspect(1420, 980);
        }
    }

    private async Task MoviePilotSeasonPartialSubmitAsync()
    {
        using var server = new SubscriptionProbeServer { FailSubscriptionCreate = true };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var vm = new MoviePilotMediaViewModel();
        vm.Attach(service, subscription.Media, picker: true);
        try
        {
            await vm.ReloadAsync();
            vm.Seasons[0].ModeIndex = 2;
            vm.Seasons[1].Selected = true;
            Require(!await vm.SubmitAsync(), "部分失败被当作整批成功");
            Require(server.Writes.Count == 2 && server.Items[4]["best_version_full"]!.GetValue<int>() == 1
                && vm.SubmitSummary.Contains("已提交", StringComparison.Ordinal) && vm.Submitted, "没有保留已经成功的季或锁住结果不明的季");
            Require(!await vm.SubmitAsync() && server.Writes.Count == 2, "部分失败后重放了整批");
            vm.ReviewConfirmed = true;
            await vm.ReviewAsync();
            Require(vm.Seasons[0].ModeIndex == 2 && !vm.Seasons[1].Selected && !vm.Submitted, "核对后没有按服务器现状重新读取");
            using var owner = new CancellationTokenSource();
            vm.Attach(service, subscription.Media, picker: true, lifetimeToken: owner.Token);
            await vm.ReloadAsync();
            vm.Seasons[1].Selected = true;
            owner.Cancel();
            Require(!await vm.SubmitAsync() && server.Writes.Count == 2, "所属页面已离开，旧季选择仍提交订阅");
        }
        finally { vm.Cancel(); }
    }

    private async Task MoviePilotMediaSubtitlesAsync()
    {
        using var server = new ResourceOptionsServer();
        using var fixture = CreateFixture(server);
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new Momoka.Configuration.MoviePilotCredentials(Momoka.Configuration.PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var panel = new MoviePilotResourcePanel();
        _root.Children.Add(panel);
        try
        {
            panel.Open(service, new MoviePilotMedia { Title = "示例剧", MediaSource = "themoviedb", MediaId = "42", Type = "电视剧", Season = 1, ConnectionStamp = service.ConnectionStamp }, fixture.Launcher, new(Episode: 1, Subtitles: true));
            await panel.ViewModel.SearchAsync();
            Require(panel.ViewModel.Subtitles.Count == 1 && panel.ViewModel.Resources.Count == 0, "字幕没有进入独立的真实结果列表");
            panel.ViewModel.ChooseSubtitlePath = (_, _, _) => Task.FromResult<string?>(null);
            await panel.ViewModel.Subtitles[0].DownloadCommand.ExecuteAsync(null);
            Require(server.SubtitleDownloads.Count == 0, "取消字幕保存仍提交下载");
            panel.ViewModel.ChooseSubtitlePath = (_, _, _) => Task.FromResult<string?>("/subtitles");
            await panel.ViewModel.Subtitles[0].DownloadCommand.ExecuteAsync(null);
            Require(server.SubtitleDownloads.Count == 1 && server.SubtitleDownloads[0].GetProperty("save_path").GetString() == "/subtitles", "字幕下载没有提交实际保存路径");
            await LayoutAsync(panel);
            await SaveSettledFrameAsync(panel, "mp-media-subtitles.png");
            // 改站点只作废下一次搜索的范围：已经列出来的字幕和资源一起留在屏幕上，并说明要重新搜索（2026-10-29）。
            panel.ViewModel.SearchSites.UseDefault = false;
            Require(panel.ViewModel.Subtitles.Count == 1 && panel.ViewModel.NoticeOpen,
                "改站点后字幕被清空或没有提示下一次搜索才应用");
        }
        finally { panel.Release(); _root.Children.Remove(panel); }
    }
}
