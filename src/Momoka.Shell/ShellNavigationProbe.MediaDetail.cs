using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private static void RequireSubscriptionBodyLayout(MoviePilotSubscriptionPage page, bool side)
    {
        var hero = Get<StackPanel>(page, "HeroSection");
        var grid = Get<Grid>(page, "HeroGrid");
        var surface = Get<Border>(page, "HeroSurface");
        var primary = Get<StackPanel>(page, "BodyPrimary");
        var overview = Get<StackPanel>(page, "HeroOverview");
        var season = Get<StackPanel>(page, "SeasonPanel");
        var facts = Get<FrameworkElement>(page, "HeroFacts");
        static double Top(FrameworkElement node, UIElement root) => node.TransformToVisual(root).TransformPoint(default).Y;
        Require(!Descendants(grid).Contains(facts) && !Descendants(grid).Contains(overview)
            && surface.VerticalAlignment == VerticalAlignment.Top && hero.MinHeight <= 0.5,
            "资料卡、简介或背景图仍在撑高海报与操作头部");
        Require(primary.Children.Contains(overview) && primary.Children.Contains(season)
            && overview.VerticalAlignment == VerticalAlignment.Top,
            "简介与季没有共用顶对齐的正文左列");
        Require(Top(overview, page) >= Top(hero, page) + hero.ActualHeight - 1.5
            && Top(overview, page) - Top(hero, page) - hero.ActualHeight <= 10,
            "简介与头部之间仍有首屏占位或过大间距");
        if (season.Visibility == Visibility.Visible)
            Require(Top(season, page) - Top(overview, page) - overview.ActualHeight is >= 6 and <= 10,
                "简介与季之间没有收紧到正文间距");
        Require(side
            ? ReferenceEquals(facts.Parent, Get<Grid>(page, "BodyLayout")) && Grid.GetRow(facts) == 0
                && Math.Abs(Top(facts, page) - Top(Get<TextBlock>(page, "MediaTitle"), page)) < 1
                && Math.Abs(facts.ActualWidth - 320) < 1 && primary.ActualWidth < page.ActualWidth - 320
            : Grid.GetColumn(facts) == 0 && Grid.GetRow(facts) == 1
                && Top(facts, page) >= Top(primary, page) + primary.ActualHeight + 6,
            "资料卡没有按宽窗对齐片名上沿、窄窗叠放的规则布局");
        Require(double.IsNaN(facts.Height) && !Descendants(facts).OfType<ScrollViewer>().Any(),
            "上方资料卡仍限制高度或裁掉完整字段");
        var factsPosition = facts.TransformToVisual(page).TransformPoint(default);
        foreach (var button in Get<WrapRow>(page, "DetailActions").Children.OfType<Button>().Where(button => button.Visibility == Visibility.Visible))
        {
            var position = button.TransformToVisual(page).TransformPoint(default);
            Require(position.X + button.ActualWidth <= factsPosition.X || position.Y + button.ActualHeight <= factsPosition.Y
                || position.Y >= factsPosition.Y + facts.ActualHeight, "上移的资料卡盖住了操作按钮：" + button.Name);
        }
    }

    private async Task CheckSubscriptionAppearanceAsync(MoviePilotSubscriptionPage page, UiSettings ui)
    {
        var original = (ui.BackdropColor, ui.BackdropTintOpacity, ui.BackdropTintLuminosityOpacity,
            ui.ControlBackgroundColor, ui.ControlBackgroundOpacity, ui.UseAcrylicControls,
            ui.ControlTintOpacity, ui.ControlTintLuminosityOpacity);
        try
        {
            ui.BackdropColor = "#123456";
            ui.BackdropTintOpacity = 37;
            ui.BackdropTintLuminosityOpacity = 61;
            ui.ControlBackgroundColor = "#345678";
            ui.ControlBackgroundOpacity = 27;
            ui.UseAcrylicControls = true;
            ui.ControlTintOpacity = 43;
            ui.ControlTintLuminosityOpacity = 59;
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
            var scrim = Get<Border>(page, "DetailBackdropScrim");
            var contrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
            Require(contrast || scrim.Background is AcrylicBrush { AlwaysUseFallback: false, Opacity: 1 } glass
                && glass.TintColor.R == 0x12 && glass.TintColor.G == 0x34 && glass.TintColor.B == 0x56
                && Math.Abs(glass.TintOpacity - 0.37) < 0.000001
                && Math.Abs((glass.TintLuminosityOpacity ?? -1) - 0.61) < 0.000001,
                "MP 背景亚克力没有即时采用三项外观设置");
            foreach (var button in Get<WrapRow>(page, "DetailActions").Children.OfType<Button>())
            {
                Require(button.Resources["ButtonBackground"] is AcrylicBrush { Opacity: 1 } fill
                    && (contrast || !fill.AlwaysUseFallback && fill.TintColor.R == 0x34
                        && Math.Abs(fill.TintOpacity - 0.43) < 0.000001
                        && Math.Abs((fill.TintLuminosityOpacity ?? -1) - 0.59) < 0.000001),
                    button.Name + " 没有跟随按钮亚克力三项设置");
                Require(button.Foreground is SolidColorBrush, button.Name + " 没有独立的可访问前景画刷");
            }
            var buttonFill = Get<Button>(page, "MoreActions").Resources["ButtonBackground"];
            var backdropFill = scrim.Background;
            var scroll = Get<ScrollViewer>(page, "DetailScroll");
            var offset = scroll.VerticalOffset;
            scroll.ChangeView(null, 80, null, true);
            await LayoutAsync(page);
            Require(ReferenceEquals(buttonFill, Get<Button>(page, "MoreActions").Resources["ButtonBackground"])
                && ReferenceEquals(backdropFill, scrim.Background), "滚动刷新前景时重建或重置了按钮／背景材质");
            scroll.ChangeView(null, offset, null, true);
            await LayoutAsync(page);
            ui.UseAcrylicControls = false;
            ui.ControlBackgroundOpacity = 47;
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
            foreach (var button in Get<WrapRow>(page, "DetailActions").Children.OfType<Button>())
                Require(button.Resources["ButtonBackground"] is AcrylicBrush { AlwaysUseFallback: true } fill
                    && Math.Abs(fill.Opacity - (contrast ? 1 : 0.47)) < 0.000001,
                    button.Name + " 没有即时切换半透明叠层和底色不透明度");
            Require(contrast || scrim.Background is AcrylicBrush, "切换按钮材质改变了背景图亚克力");
        }
        finally
        {
            (ui.BackdropColor, ui.BackdropTintOpacity, ui.BackdropTintLuminosityOpacity,
                ui.ControlBackgroundColor, ui.ControlBackgroundOpacity, ui.UseAcrylicControls,
                ui.ControlTintOpacity, ui.ControlTintLuminosityOpacity) = original;
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
        }
    }

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
            // 纸面 + 图那一套只活在单列版式里（2026-11-09 起宽版式有图默认走沉浸档，图铺满视口、纸面让开）。
            // 900 宽的页面上内容区是 836，够不到资料卡并排的 960 —— 正是这一档。
            ResizeInspect(900, 1000);
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
                    Require(header.Background is AcrylicBrush headerBrush
                        && headerBrush.TintColor == ThemeHost.ToColor(theme.Colors.SurfaceAlt)
                        && expander.Background is AcrylicBrush contentBrush
                        && contentBrush.TintColor == headerBrush.TintColor, "季标题与展开区域没有使用当前主题亚克力");
                    foreach (var group in Descendants(seasons).OfType<Button>().Where(button => button.Name == "SwitchEpisodeGroup"))
                        Require(group.Background is AcrylicBrush && group.Resources["ButtonBackgroundDisabled"] is AcrylicBrush,
                            "剧集组选中或未选中卡片缺少亚克力");
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
    /// 订阅详情页的两种显示模式（2026-11-03 用户令「给 mp 订阅页面也增强大窗模式」；2026-11-09 用户令
    /// 「给 MP 订阅也新增沉浸模式还有简洁模式」）。
    /// <para>
    /// 宽版式、有图时：沉浸模式把正文纸面换成半透明洗色、面板的底与框淡到 0、头部不再撑第一屏、背景铺到
    /// 窗口下沿并抬到 0.96，图下沿的黑色渐变收起；设置里改成简洁模式后当场收起背景图、头部换成一张卡面
    /// （底色、内边距、简介不再压在卡底），纸面与面板回来。窗口收窄成单列后两种模式各看一遍：沉浸仍是
    /// 「图 + 纸面」，简洁仍然没有图。最后切回沉浸、拉宽，确认同一页不用重开就回到沉浸档。
    /// </para>
    /// <para>
    /// 从前那套「窗口铺开／图快被裁」的门槛与过渡动画随判据收进开关一起退役：翻面只有设置与宽窄两条来路，
    /// 两条都换整套版面，所以这里断言的是终值，不再数动画。
    /// </para>
    /// </summary>
    private async Task MoviePilotSubscriptionShowcaseAsync()
    {
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync(), Related = true };
        var (settings, service) = SubscriptionService(server);
        using var settingsService = new SettingsService(null!, settings);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        var strips = new List<TitleStrip>();
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        // 这一条自己把窗口形态摆干净：先还原（最大化时改尺寸会被忽略），再给一扇宽版式的窗口。
        if (_window!.IsMaximized) _window.ToggleMaximize();
        ResizeProbeClient(1240, 1000);
        page.Attach(new(service, subscription, Settings: settingsService, SetTitleStrip: strips.Add));
        var readings = new List<string>();
        try
        {
            var media = page.MediaViewModel;
            await UntilAsync(() => page.ViewModel.IsReady && media.IsReady && media.Backdrop is not null);
            await LayoutAsync(page);

            var sheet = Get<StackPanel>(page, "BodySheet");
            var backdrop = Get<Grid>(page, "DetailBackdropRoot");
            var image = Get<Border>(page, "DetailBackdropBand");
            var fade = Get<Border>(page, "DetailBackdropFade");
            var hero = Get<StackPanel>(page, "HeroSection");
            var surface = Get<Border>(page, "HeroSurface");
            var panel = Get<StackPanel>(page, "PeoplePanel");
            var facts = Get<Border>(page, "HeroFacts");
            var overview = Get<StackPanel>(page, "HeroOverview");

            // ---- 沉浸模式（出厂那一档）：宽版式一成立就铺图，不必先把窗口最大化 ----
            Require(media.Immersive && media.Showcase && media.WideLayout,
                $"宽版式下没有进入沉浸档：开关 {media.Immersive}、宽版式 {media.WideLayout}、沉浸 {media.Showcase}");
            Require(backdrop.Visibility == Visibility.Visible, "沉浸档没有画背景图");
            Require(sheet.Background is SolidColorBrush { Color.A: 0 },
                "沉浸档正文仍有独立底色，上下背景出现分层");
            Require(panel.Background is AcrylicBrush { Opacity: 0 }, "沉浸档的正文面板底没有淡到 0");
            Require(fade.Visibility == Visibility.Collapsed, "沉浸档图下沿的黑色渐变没有收起");
            Require(!media.ShowShelfLines, "沉浸档的货架标题仍然画着横线");
            Require(surface.Background is null && surface.Padding == default(Thickness),
                "沉浸档的头部卡面没有让开（底色或内边距还在）");
            Require(surface.VerticalAlignment == VerticalAlignment.Top,
                "沉浸档的头部没有按海报与操作内容收边");
            RequireSubscriptionBodyLayout(page, true);
            Require(ReferenceEquals(facts.Parent, Get<Grid>(page, "BodyLayout")), "资料卡没有留在正文右列完整展示");
            await CheckSubscriptionAppearanceAsync(page, settings.Ui);
            Require(hero.MinHeight <= 0.5, "沉浸档的头部仍在按背景图让开第一屏");
            Require(Math.Abs(image.Opacity - DetailHero.ShowcaseBackdropOpacity) < 0.001,
                $"沉浸档的背景不透明度不是 {DetailHero.ShowcaseBackdropOpacity}：{image.Opacity:0.###}");
            var expectedImage = DetailHero.ImmersivePictureHeight(media.Viewport, media.HeaderLift);
            Require(Math.Abs(image.ActualHeight - expectedImage) < 2,
                $"沉浸档的背景没有铺到窗口下沿：图 {image.ActualHeight:0}、该是 {expectedImage:0}");
            Require(Math.Abs(facts.ActualWidth - 320) < 1 && facts.HorizontalAlignment == HorizontalAlignment.Right, "沉浸档的资料卡没有并排在右端");
            readings.Add($"沉浸 1240：视口 {media.Viewport:0}、图 {image.ActualHeight:0}、头部 {hero.ActualHeight:0}、"
                + $"纸面透明度 {(sheet.Background as SolidColorBrush)?.Color.A}、面板 {panel.Background.Opacity:0.##}");
            await SaveProbeScreenAsync("mp-subscription-mode-immersive-1240.png");

            // ---- 设置里改成简洁模式：当场收图、头部换成卡面 ----
            settings.Ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(settings.Ui);
            await UntilAsync(() => media.Plain && !media.Showcase);
            await LayoutAsync(page);
            Require(backdrop.Visibility == Visibility.Collapsed, "简洁模式仍铺着背景图");
            Require(surface.Background is SolidColorBrush, "简洁模式的头部没有卡面底色");
            Require(surface.Padding.Left == 12 && surface.Padding.Top == 12, "简洁模式的卡面没有内边距：" + surface.Padding);
            Require(surface.VerticalAlignment == VerticalAlignment.Top,
                "简洁模式的卡面没有按自己的内容收边");
            RequireSubscriptionBodyLayout(page, true);
            Require(surface.BorderThickness == (Thickness)Application.Current.Resources["EgCardBorderThickness"],
                "简洁模式的卡面没有接主题那一条描边");
            Require(sheet.Background is SolidColorBrush { Color.A: 255 }, "简洁模式的正文纸不是不透明");
            Require(panel.Background is AcrylicBrush { Opacity: 1 }, "简洁模式的正文面板底没有回来");
            Require(fade.Visibility == Visibility.Collapsed, "简洁模式没有图，图下沿的渐变却还画着");
            Require(hero.MinHeight <= 0.5, "简洁模式仍按背景图让开第一屏");
            Require(overview.VerticalAlignment == VerticalAlignment.Top, "简洁模式的简介没有顶对齐");
            Require(strips.Count > 0 && strips[^1] == TitleStrip.Plain, "简洁模式的标题栏没有回到页面字色");
            readings.Add($"简洁 1240：卡面内边距 {surface.Padding}、纸面透明度 {(sheet.Background as SolidColorBrush)?.Color.A}、"
                + $"面板 {panel.Background.Opacity:0.##}、资料卡 {facts.ActualWidth:0}");
            await SaveProbeScreenAsync("mp-subscription-mode-plain-1240.png");

            // ---- 简洁模式收窄成单列：资料卡叠到正文左列下面，仍然不铺图 ----
            ResizeProbeClient(760, 1000);
            await UntilAsync(() => !media.WideLayout);
            await LayoutAsync(page);
            Require(media.Plain && !media.Showcase && backdrop.Visibility == Visibility.Collapsed,
                "简洁模式窄窗又铺回了背景图");
            RequireSubscriptionBodyLayout(page, false);
            await SaveProbeScreenAsync("mp-subscription-mode-plain-760.png");

            // ---- 窄窗切回沉浸：图回来，但仍是「图 + 纸面」（单列不参与沉浸档） ----
            settings.Ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(settings.Ui);
            await UntilAsync(() => media.Immersive && !media.Showcase);
            await LayoutAsync(page);
            Require(backdrop.Visibility == Visibility.Visible, "窄窗沉浸模式没有背景图");
            Require(surface.Background is null, "窄窗沉浸模式的头部仍扣着卡面");
            Require(sheet.Background is SolidColorBrush { Color.A: 255 }, "窄窗沉浸模式的正文纸不是不透明");
            Require(overview.VerticalAlignment == VerticalAlignment.Top, "窄窗沉浸模式的简介没有顶对齐");
            RequireSubscriptionBodyLayout(page, false);
            Require(Get<Border>(page, "DetailBackdropScrim").Background is SolidColorBrush,
                "窄窗背景仍在使用亚克力而非实色阅读罩");
            Require(strips.Count > 0 && strips[^1] == TitleStrip.OnScrim, "有图的窄窗没有把标题栏换成浅墨");
            readings.Add($"沉浸 760：页宽 {media.PageWidth:0}、图 {image.ActualHeight:0}、头部下限 {hero.MinHeight:0}");
            await SaveProbeScreenAsync("mp-subscription-mode-immersive-760.png");

            // ---- 拉宽回沉浸档：同一页、不重开 ----
            ResizeProbeClient(1240, 1000);
            await UntilAsync(() => media.WideLayout && media.Showcase);
            await LayoutAsync(page);
            Require(sheet.Background is SolidColorBrush { Color.A: 0 }
                && panel.Background is AcrylicBrush { Opacity: 0 } && surface.Background is null,
                "拉宽回沉浸档后纸面、面板或头部卡面没有跟着翻");
            RequireSubscriptionBodyLayout(page, true);
            await SaveProbeScreenAsync("mp-subscription-mode-immersive-wide.png");
            // 翻到长货架末尾再缩窗，第 0 项已虚拟化；翻页必须采用现存卡片的新尺寸。
            for (var index = media.Recommendations.Count; index < 20; index++)
                media.Recommendations.Add(new MoviePilotRelatedCard("推荐 " + index, "电影", () => { }));
            var strip = Get<ShelfStrip>(page, "MediaRecommendations");
            strip.StartBringIntoView();
            await LayoutAsync(page);
            strip.FocusOn(19);
            await LayoutAsync(page);
            var repeater = Descendants(strip).OfType<ItemsRepeater>().Single();
            Require(repeater.TryGetElement(0) is null, "长货架夹具没有虚拟化首项，未覆盖末尾缩窗");
            ResizeProbeClient(760, 1000);
            await UntilAsync(() => !media.WideLayout);
            await LayoutAsync(page);
            strip.StartBringIntoView();
            await LayoutAsync(page);
            strip.FocusOn(13);
            await LayoutAsync(page);
            var target = repeater.TryGetElement(13) as FrameworkElement;
            var scroller = Descendants(strip).OfType<ScrollView>().Single();
            Require(target is not null, "缩窗后定位推荐卡片没有实现目标");
            var position = target!.TransformToVisual(scroller).TransformPoint(default);
            Require(position.X >= -1 && position.X + target.ActualWidth <= scroller.ViewportWidth + 1,
                $"长货架缩窗后仍用旧卡宽定位：目标 {position.X:0.0}..{position.X + target.ActualWidth:0.0}、视口 {scroller.ViewportWidth:0.0}");
        }
        finally
        {
            if (_window?.IsMaximized == true) _window.ToggleMaximize();
            page.Release();
            _root.Children.Remove(page);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "mp-subscription-mode.txt"), readings);
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
