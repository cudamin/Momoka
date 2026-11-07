using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实设置模板的排序、显隐、两阶段提交及重入；仅操作本次隔离夹具。</summary>
    private async Task DetailCustomizationSettingsAsync()
    {
        using var fixture = CreateFixture();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "detail-customization-settings"));
        paths.EnsureCreated();
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        var motionPreference = HomeMotion.PreferenceEnabled;
        var readings = new List<string>();
        _root.Children.Add(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            await page.ViewModel.ReloadAsync();
            Require(page.ViewModel.Sections.Where(section => section.Category == "关于")
                    .SelectMany(section => section.Rows).All(row => !row.Label.Contains("弹弹play", StringComparison.Ordinal)),
                "关于仍有弹弹play来源行");
            page.SelectedCategory = "界面";
            HomeMotion.ApplyPreference(true);
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                foreach (var (title, type, count) in new[]
                {
                    ("剧页面卡片", EmbyItemType.Series, 5),
                    ("单集页面卡片", EmbyItemType.Episode, 4),
                    ("电影页面卡片", EmbyItemType.Movie, 3)
                })
                {
                    var section = page.ViewModel.Sections.Single(card => card.Title == title);
                    var row = section.Rows.OfType<SettingHomeLayoutRow>().Single();
                    if (type == EmbyItemType.Episode)
                        Require(row.Rows.All(card => card.Key != "seasons"), "单集设置仍显示全部剧季卡片选项");
                    var list = Descendants(page).OfType<ListView>()
                        .Single(control => ReferenceEquals(control.ItemsSource, row.Rows));
                    Require(row.EnableSpring && row.Rows.Count == count && list.CanDragItems && list.CanReorderItems
                        && list.AllowDrop && ReferenceEquals(list.Tag, row), title + " 未完整接入拖拽排序");
                    list.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3 });
                    await LayoutAsync(list);
                    Require(list.ActualHeight >= row.ListHeight - 1 && list.ActualWidth > 0,
                        title + " 排序表没有展开完整行高");
                    var first = row.Rows[0];
                    var firstContainer = (ListViewItem)list.ContainerFromItem(first);
                    var down = Descendants(firstContainer).OfType<Button>().Single(button => button.IsEnabled);
                    ((IInvokeProvider)new ButtonAutomationPeer(down).GetPattern(PatternInterface.Invoke)).Invoke();
                    if (HomeMotion.AnimationsEnabled) await UntilAsync(() => page.HasPrioritySpring(list));
                    await UntilAsync(() => !page.HasPrioritySpring(list));
                    Require(row.Rows[1] == first && Stored(type).Select(card => card.Key)
                        .SequenceEqual(row.Rows.Select(card => card.Key)), title + " 箭头没有保存实际次序");
                    var check = Descendants((ListViewItem)list.ContainerFromItem(first)).OfType<CheckBox>().Single();
                    check.IsChecked = false;
                    await UntilAsync(() => !first.Visible);
                    Require(!Stored(type).Single(card => card.Key == first.Key).Visible, title + " 复选框没有保存隐藏");

                    var before = Stored(type).Select(card => (card.Key, card.Visible)).ToArray();
                    var motion = Get<Dictionary<ListView, SettingsPriorityMotion>>(page, "_priorityMotions")[list];
                    motion.BeginDrag();
                    var dragged = row.Rows[0];
                    row.Rows.RemoveAt(0);
                    Require(Stored(type).Select(card => (card.Key, card.Visible)).SequenceEqual(before),
                        title + " 拖拽中间把缺项快照写入设置");
                    row.Rows.Add(dragged);
                    motion.EndDrag();
                    await UntilAsync(() => !page.HasPrioritySpring(list));
                    Require(Stored(type).Select(card => (card.Key, card.Visible))
                        .SequenceEqual(row.Rows.Select(card => (card.Key, card.Visible))), title + " 拖拽完成未保存最终次序");
                    var restored = row.Rows.ToArray();
                    motion.BeginDrag();
                    row.Rows.RemoveAt(0);
                    motion.EndDrag();
                    Require(row.Rows.SequenceEqual(restored), title + " 中断拖拽没有还原完整集合");
                    motion.BeginDrag();
                    motion.EndDrag();
                    Require(row.Rows.SequenceEqual(restored), title + " 取消拖拽改变了顺序");
                    // 原生完成事件若漏发，下一次独立编辑也必须结束旧事务并写回。
                    row.BeginReorder();
                    first.Visible = true;
                    Require(Stored(type).Single(card => card.Key == first.Key).Visible,
                        title + " 拖拽完成事件漏发后勾选不能保存");
                    row.BeginReorder();
                    row.Rows[0].DownCommand.Execute(null);
                    Require(Stored(type).Select(card => card.Key).SequenceEqual(row.Rows.Select(card => card.Key)),
                        title + " 拖拽完成事件漏发后箭头不能保存");

                    HomeMotion.ApplyPreference(false);
                    row.Rows[0].DownCommand.Execute(null);
                    await LayoutAsync(list);
                    Require(!page.HasPrioritySpring(list) && Stored(type).Select(card => card.Key)
                        .SequenceEqual(row.Rows.Select(card => card.Key)), title + " 关闭动画后无法保存换位");
                    Require(Descendants(list).OfType<FrameworkElement>().Where(element => element.Name == "CardOrderRoot")
                        .All(element => element.RenderTransform is TranslateTransform { Y: 0 }), title + " 关闭动画遗留位移");
                    first.Visible = true;
                    HomeMotion.ApplyPreference(true);
                    await LayoutAsync(list);
                    await SaveProbeScreenAsync($"settings-detail-cards-{type}-{width}.png");
                    readings.Add($"{title}/{width}：{string.Join(',', row.Rows.Select(card => card.Key))}，拖中完整、完成保存、取消恢复");
                }
            }
            var tables = page.ViewModel.DetailCards.ToArray();
            page.ReleaseHosted();
            BeginSettingsFixtureVisit(page);
            page.SelectedCategory = "界面";
            await LayoutAsync(page);
            Require(tables.SequenceEqual(page.ViewModel.DetailCards), "缓存设置页重入重建卡片排序表");
            foreach (var (title, type) in new[] { ("剧页面卡片", EmbyItemType.Series), ("单集页面卡片", EmbyItemType.Episode), ("电影页面卡片", EmbyItemType.Movie) })
            {
                var row = page.ViewModel.Sections.Single(card => card.Title == title).Rows.OfType<SettingHomeLayoutRow>().Single();
                Require(Stored(type).Select(card => (card.Key, card.Visible))
                    .SequenceEqual(row.Rows.Select(card => (card.Key, card.Visible))), title + " 重入丢失偏好");
            }
            var appearance = page.ViewModel.Sections.Single(section => section.Title == "详情页外观");
            var defaultRow = appearance.Rows.OfType<SettingSliderRow>()
                .Single(row => row.Label == "按钮与选择框底色不透明度（%）");
            var defaultHost = Descendants(page).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, defaultRow));
            defaultHost.StartBringIntoView();
            await LayoutAsync(defaultHost);
            Require(Math.Abs(defaultRow.Value - UiSettings.DefaultControlBackgroundOpacity) < 0.000001
                && Math.Abs(fixture.Settings.Ui.ControlBackgroundOpacity - UiSettings.DefaultControlBackgroundOpacity) < 0.000001,
                "打开设置把精确默认不透明度改成了吸附值");
            Require(page.ViewModel.DetailCards.Count == 3, "剧、单集和电影没有保留独立卡片设置");
            Require(!appearance.Rows.Any(row => row.Label is "按钮与选择框背景" or "背景图效果" or "背景遮罩颜色"),
                "固定亚克力后仍显示已退役材质或背景颜色选项");
            var backdropDefault = appearance.Rows.OfType<SettingSliderRow>()
                .Single(row => row.Label == "背景图不透明度（%）");
            Require(backdropDefault.Value == 20 && fixture.Settings.Ui.BackdropOpacity == 20,
                "背景亚克力默认不透明度不是20%");
            foreach (var label in new[] { "按钮与选择框色调不透明度（%）", "按钮与选择框亮度不透明度（%）",
                "背景色调不透明度（%）", "背景亮度不透明度（%）" })
                Require(appearance.Rows.OfType<SettingSliderRow>().Single(row => row.Label == label).Value == 55,
                    label + " 默认值不是55%");
            Require(fixture.Settings.Ui.BackdropTintFromArtwork
                && !appearance.Rows.Any(row => row.Label == "背景染色颜色"), "默认主图染色仍显示自定义颜色行");
            await SaveProbeScreenAsync("settings-fixed-acrylic-screen.png");
            foreach (var (label, value) in new[]
            {
                ("按钮与选择框底色不透明度（%）", 37d), ("背景图不透明度（%）", 45d),
                ("按钮与选择框色调不透明度（%）", 20d), ("按钮与选择框亮度不透明度（%）", 80d),
                ("背景色调不透明度（%）", 80d), ("背景亮度不透明度（%）", 20d)
            })
            {
                var row = appearance.Rows.OfType<SettingSliderRow>().Single(item => item.Label == label);
                var host = Descendants(page).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, row));
                host.StartBringIntoView();
                await LayoutAsync(host);
                Descendants(host).OfType<Slider>().Single().Value = value;
                await UntilAsync(() => Math.Abs(row.Value - value) < 0.01);
            }
            Require(fixture.Settings.Ui.ControlBackgroundOpacity == 37 && fixture.Settings.Ui.BackdropOpacity == 45,
                "不透明度滑块没有写回设置");
            Require(fixture.Settings.Ui.ControlTintOpacity == 20 && fixture.Settings.Ui.ControlTintLuminosityOpacity == 80
                && fixture.Settings.Ui.BackdropTintOpacity == 80 && fixture.Settings.Ui.BackdropTintLuminosityOpacity == 20,
                "色调或亮度不透明度滑块没有保存独立偏好");
            var sourceRow = appearance.Rows.OfType<SettingChoiceRow>().Single(row => row.Label == "背景染色来源");
            sourceRow.Selected = sourceRow.Choices.Single(choice => choice.Label == "自定义颜色");
            await LayoutAsync(page);
            var customColor = appearance.Rows.OfType<SettingColorRow>().Single(row => row.Label == "背景染色颜色");
            customColor.BeginEdit();
            customColor.Color = "#245A8E";
            Require(customColor.CommitEdit() && fixture.Settings.Ui.BackdropColor == "#245A8E",
                "背景自定义染色没有通过确认保存");
            await SaveProbeScreenAsync("settings-acrylic-custom-tint-screen.png");
            sourceRow = appearance.Rows.OfType<SettingChoiceRow>().Single(row => row.Label == "背景染色来源");
            sourceRow.Selected = sourceRow.Choices.Single(choice => choice.Label == "当前主图");
            await LayoutAsync(page);
            Require(fixture.Settings.Ui.BackdropTintFromArtwork && fixture.Settings.Ui.BackdropColor == "#245A8E"
                && !appearance.Rows.Any(row => row.Label == "背景染色颜色"),
                "恢复主图染色没有收起自定义行或清空了颜色偏好");
            await SaveProbeScreenAsync("settings-acrylic-artwork-tint-restored-screen.png");
            var modeRow = appearance.Rows.OfType<SettingChoiceRow>().Single(row => row.Label == "显示模式");
            modeRow.Selected = modeRow.Choices.Single(choice => choice.Label == "简洁模式");
            Require(!appearance.Rows.Any(row => row.Label is "背景图不透明度（%）" or "背景染色来源"
                or "背景染色颜色" or "背景色调不透明度（%）" or "背景亮度不透明度（%）"),
                "简洁模式没有隐藏完整背景设置组");
            modeRow.Selected = modeRow.Choices.Single(choice => choice.Label == "沉浸模式");
            await UntilAsync(() => fixture.Settings.Ui.EnableImmersive);
            Require(fixture.Settings.Ui.EnableImmersive, "显示模式选择没有同步沉浸状态");
            Require(appearance.Rows.OfType<SettingSliderRow>().Single(row => row.Label == "背景色调不透明度（%）").Value == 80
                && appearance.Rows.OfType<SettingSliderRow>().Single(row => row.Label == "背景亮度不透明度（%）").Value == 20,
                "恢复沉浸丢失背景色调或亮度不透明度偏好");
            Require(appearance.Rows.OfType<SettingSliderRow>().Single(row => row.Label == "背景图不透明度（%）").Value == 45,
                "恢复沉浸丢失背景不透明度偏好");
            Require(!appearance.Rows.Any(row => row.Label is "按钮与选择框背景" or "背景图效果" or "背景遮罩颜色"),
                "恢复沉浸重新显示退役选项");
            Require(fixture.Actions.Plays == 0, "设置自定义探针意外请求播放");

            IReadOnlyList<DetailCardSetting> Stored(string type) => type switch
            {
                EmbyItemType.Series => fixture.Settings.Ui.SeriesCards,
                EmbyItemType.Episode => fixture.Settings.Ui.EpisodeCards,
                _ => fixture.Settings.Ui.MovieCards
            };
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            HomeMotion.ApplyPreference(motionPreference);
            ResizeInspect(1420, 980);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-customization-settings.txt"), readings);
        }
    }

    /// <summary>三种详情页的实际卡片顺序、隐藏恢复、即时外观及大屏模式；媒体与图片全部是假传输。</summary>
    private async Task DetailCustomizationPagesAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        if (_window!.IsMaximized) _window.ToggleMaximize();
        var image = await FixtureImageAsync(1600, 900);
        var series = SeriesWithInfo("custom-show", "卡片自定义剧集", ["剧情"], "离线工作室", 2);
        series.Overview = "用于核对卡片顺序、隐藏恢复与背景透明度的离线页面。";
        series.BackdropImageTags.Add("fixture");
        series.ImageTags["Primary"] = "fixture";
        var seasons = new[] { SeasonOf(series, "custom-s1", 1), SeasonOf(series, "custom-s2", 2) };
        var episodes = seasons.ToDictionary(season => season.Id, season => EpisodesOf(series, season));
        var episode = EpisodeDetail(episodes[seasons[0].Id][0]);
        episode.People = series.People;
        episode.BackdropImageTags.Add("fixture");
        episode.ImageTags["Primary"] = "fixture";
        var movie = new EmbyItem
        {
            Id = "custom-movie",
            Name = "卡片自定义电影",
            Type = EmbyItemType.Movie,
            Overview = series.Overview,
            People = series.People,
            MediaSources = episode.MediaSources,
            BackdropImageTags = ["fixture"],
            ImageTags = new() { ["Primary"] = "fixture" },
            Genres = ["剧情"],
            RunTimeTicks = TimeSpan.FromMinutes(90).Ticks
        };
        var known = new[] { series, movie, episode }.Concat(seasons)
            .Concat(episodes.Values.SelectMany(items => items).Where(item => item.Id != episode.Id).Select(EpisodeDetail))
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var reads = 0;
        fixture.Transport.Reply = (request, _) =>
        {
            reads++;
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (path.EndsWith("/Similar", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = new[] { movie }, TotalRecordCount = 1 }));
            var index = path.IndexOf("/Items/", StringComparison.Ordinal);
            if (index >= 0 && known.TryGetValue(path[(index + "/Items/".Length)..], out var item))
                return Task.FromResult(Json(item));
            if (path.Contains("/Shows/" + series.Id + "/", StringComparison.Ordinal))
                return Task.FromResult(SeriesPart(path, request.RequestUri!.Query, seasons, episodes));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        var readings = new List<string>();
        var savedTheme = ThemeHost.Current;
        var motionPreference = HomeMotion.PreferenceEnabled;
        _root.Children.Add(frame);
        HomeMotion.ApplyPreference(false);
        try
        {
            foreach (var item in new[] { series, episode, movie })
            {
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item)), "自定义详情导航失败");
                var page = (DetailPage)frame.Content;
                page.AttachWindow(_window);
                await UntilAsync(() => page.IsReady && page.ViewModel.HeroImage is not null);
                await LayoutAsync(page);
                RequireFixedDetailForeground(page);
                var otherInfo = Get<TextBlock>(page, "OtherInfoLine");
                Require(otherInfo.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>()
                    .All(run => run.Foreground is SolidColorBrush ink && ink.Color == MediaTextColors.Current),
                    "其他信息的标签和值绕过页面固定媒体字色");
                var sheet = Get<StackPanel>(page, "BodySheet");
                var originalPanels = sheet.Children.OfType<StackPanel>().ToArray();
                foreach (var panel in originalPanels)
                    Require(panel.Background is AcrylicBrush, "详情卡片没有使用亚克力：" + panel.Name);
                var materialSource = Get<Border>(page, "CardMaterialSource");
                var originalMaterial = materialSource.Background;
                var privateMaterial = Get<AcrylicBrush>(page, "_panelFill");
                try
                {
                    materialSource.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
                    Require(privateMaterial.AlwaysUseFallback && privateMaterial.FallbackColor == Microsoft.UI.Colors.White,
                        "卡片没有随系统资源切换到实色");
                }
                finally { materialSource.Background = originalMaterial; }
                Require(!privateMaterial.AlwaysUseFallback && privateMaterial.TintColor == ((AcrylicBrush)originalMaterial).TintColor,
                    "卡片资源恢复后仍锁在实色");
                var expected = DetailLayout.For(fixture.Settings.Ui, item.Type).Reverse()
                    .Select(card => new DetailCardSetting { Key = card.Key, Visible = card.Visible }).ToList();
                // 信息卡置于中间，隐藏后用后继卡片的位置验证不占位，也覆盖电影页。
                var infoSetting = expected.Single(card => card.Key == "info");
                expected.Remove(infoSetting);
                expected.Insert(1, infoSetting);
                SetCards(item.Type, expected);
                ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
                await LayoutAsync(page);
                RequireCardOrder(page, expected.Select(card => card.Key));
                Require(originalPanels.All(panel => sheet.Children.Contains(panel)), "排序重建了原有卡片实例");
                Require(sheet.Children[sheet.Children.Count - 1] == Get<UIElement>(page, "FooterBanner"), "卡片排序移动了尾图");
                foreach (var width in new[] { 1100, 760 })
                {
                    ResizeProbeClient(width, 820);
                    await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - width) < 1; });
                    await LayoutAsync(page);
                    RequireCardOrder(page, expected.Select(card => card.Key));
                    var info = Get<StackPanel>(page, "InfoPanel");
                    Require(info.Visibility == Visibility.Visible && info.ActualHeight > 0, "信息卡未呈现夹具内容");
                    var infoIndex = sheet.Children.IndexOf(info);
                    var following = sheet.Children.Skip(infoIndex + 1).OfType<FrameworkElement>()
                        .FirstOrDefault(element => element.Visibility == Visibility.Visible);
                    var previousTop = following?.TransformToVisual(sheet).TransformPoint(default).Y;
                    var previousHeight = info.ActualHeight;
                    var before = expected.Select(card => card.Key).ToArray();
                    expected.Single(card => card.Key == "info").Visible = false;
                    ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
                    await LayoutAsync(page);
                    Require(info.Visibility == Visibility.Collapsed, "信息卡未隐藏");
                    // Collapsed 的 ActualHeight 可能保留旧 RenderSize；量下一可见兄弟的真实位置，证明空位也收走了。
                    if (following is not null && previousTop is { } top)
                    {
                        var nextTop = following.TransformToVisual(sheet).TransformPoint(default).Y;
                        Require(nextTop <= top - previousHeight + 1, "隐藏信息卡后后续卡片没有上移");
                    }
                    RequireCardOrder(page, before);
                    await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
                    await SaveProbeScreenAsync($"detail-customization-{item.Type}-{width}-hidden.png");
                    expected.Single(card => card.Key == "info").Visible = true;
                    ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
                    await LayoutAsync(page);
                    Require(info.Visibility == Visibility.Visible && info.ActualHeight > 0, "恢复信息卡未原位出现");
                    if (following is not null && previousTop is { } restoredTop)
                        Require(Math.Abs(following.TransformToVisual(sheet).TransformPoint(default).Y - restoredTop) < 1,
                            "恢复信息卡后后继卡片没有返回原位置");
                    RequireCardOrder(page, before);
                    var scroll = Get<ScrollView>(page, "Body");
                    var first = sheet.Children.OfType<FrameworkElement>().First(element => element.Visibility == Visibility.Visible);
                    var content = (UIElement)scroll.Content;
                    await ScrollProbeAsync(scroll, first.TransformToVisual(content).TransformPoint(default).Y);
                    await SaveProbeScreenAsync($"detail-customization-{item.Type}-{width}-restored.png");
                }
                ResizeProbeClient(1100, 1120);
                await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
                await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
                // 不透明度只改变固定亚克力，原图、文字与布局都保持稳定。
                fixture.Settings.Ui.BackdropOpacity = 45;
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !page.ViewModel.GaussianBackdropApplied);
                await LayoutAsync(page);
                Require(page.ViewModel.Showcase && page.ViewModel.BackdropShown && !page.ViewModel.HeroCardShown,
                    "开启时剧、电影或单集宽窗没有进入沉浸布局");
                RequireImmersiveSurface(page);
                var picture = Get<Border>(page, "BackdropPicture");
                var expectedScrim = AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, 0.45);
                var masked = page.ViewModel.BackdropShown;
                if (masked)
                {
                    Require(ScrimAlphaOf(page) == expectedScrim,
                        $"背景图遮罩没有立即按 45% 落下（{ScrimAlphaOf(page)}，该是 {expectedScrim}）；"
                        + $"fade={Get<Border>(page, "BackdropFade").Visibility}、"
                        + $"tail={(Get<StackPanel>(page, "HeroTail").Background as SolidColorBrush)?.Color}、"
                        + $"有图={page.ViewModel.BackdropShown}、沉浸={page.ViewModel.Showcase}、"
                        + $"高斯={page.ViewModel.GaussianBackdropApplied}、强度={page.ViewModel.BackdropStrength:0.###}");
                    Require(picture.Opacity == 1, "背景图本身被调了透明度（该调的是图上那层遮罩）");
                }
                await CheckControlBackgroundOpacityAsync(page, fixture.Settings.Ui);
                await CheckAcrylicTintPreferencesAsync(page, fixture.Settings.Ui);
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(page.ViewModel.Showcase, "开启时有图宽窗没有保持沉浸布局");
                if (masked)
                    Require(ScrimAlphaOf(page) == expectedScrim, "沉浸版式把背景图遮罩浓度改掉了");
                var band = Get<Grid>(page, "HeroBand");
                var heroHeight = band.ActualHeight;
                fixture.Settings.Ui.BackdropOpacity = 0;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                if (masked)
                {
                    Require(ScrimAlphaOf(page) == 0, "0 档还留着亚克力");
                    // 0 档是「关掉亚克力」，不是「把图收走」：画面照旧站在那儿，只是不再压那一层墨。
                    Require(picture.Opacity == 1, "0 档把背景图本身调透明了");
                }
                // 关掉遮罩只收那层墨，版式不动：海报、片名和那排键还在原位。
                Require(Math.Abs(band.ActualHeight - heroHeight) < 0.5, "关掉亚克力改变了头图那一格的高");
                await SaveProbeScreenAsync($"detail-customization-{item.Type}-backdrop-clear.png");
                fixture.Settings.Ui.BackdropOpacity = 45;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await CheckFixedAcrylicBackdropAsync(page, fixture.Settings.Ui, () => reads);
                ResizeProbeClient(1100, 1120);
                await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                foreach (var theme in UiThemes.All)
                {
                    ThemeHost.Apply(theme);
                    await LayoutAsync(page);
                    RequireFixedDetailForeground(page);
                    await SaveProbeScreenAsync($"detail-customization-{item.Type}-{theme.Id}-screen.png");
                    if (item.Type == EmbyItemType.Movie)
                        await CheckDetailMenuAcrylicAsync(page, fixture, theme.Id);
                }
                ResizeProbeClient(760, 820);
                await UntilAsync(() => { page.UpdateLayout(); return page.ViewModel.IsCompact; });
                Require(!page.ViewModel.Showcase, "沉浸模式挤掉了窄窗紧凑布局");
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(!page.ViewModel.Showcase && page.ViewModel.IsCompact && page.ViewModel.BackdropShown, "开启时窄窗未保持紧凑排版与背景图");
                fixture.Settings.Ui.BackdropOpacity = 100;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                if (masked) Require(ScrimAlphaOf(page) == byte.MaxValue, "100 档遮罩没有完全不透明");
                await CheckImmersiveSwitchAsync(page, fixture.Settings.Ui);
                readings.Add($"{item.Type}：{string.Join(',', expected.Select(card => card.Key))}，隐藏恢复同实例，背景亚克力0/20/45/100，窄窗保留紧凑布局");
            }
            // 季页使用原有顺序；剧与单集的自定义不应连带改掉它。
            ResizeProbeClient(1100, 1120);
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, seasons[0])), "季页导航失败");
            var seasonPage = (DetailPage)frame.Content;
            await UntilAsync(() => seasonPage.IsReady);
            RequireCardOrder(seasonPage, new[] { "episodes", "info", "seasons", "cast", "similar" });
            fixture.Settings.Ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(seasonPage);
            Require(!seasonPage.ViewModel.Showcase, "没有背景图的季页被强制沉浸");
            await CheckImmersiveSwitchAsync(seasonPage, fixture.Settings.Ui);
            // 恢复默认与导入同样走通用通知，页面应当场重排，不需要再次导航。
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, series)), "重入剧页导航失败");
            var reopened = (DetailPage)frame.Content;
            await UntilAsync(() => reopened.IsReady);
            RequireCardOrder(reopened, fixture.Settings.Ui.SeriesCards.Select(card => card.Key));
            SettingsPreferences.Apply(fixture.Settings, new AppSettings());
            ShellPrefs.Apply(fixture.Settings.Ui);
            await LayoutAsync(reopened);
            RequireCardOrder(reopened, new[] { "episodes", "info", "seasons", "cast", "similar" });
            reopened.Release();
            _root.Children.Remove(frame);
            await CheckHomeBackgroundOpacityAsync(fixture, movie, () => reads);
            Require(fixture.Actions.Plays == 0, "详情自定义探针意外请求播放");

            void SetCards(string type, List<DetailCardSetting> cards)
            {
                if (type == EmbyItemType.Series) fixture.Settings.Ui.SeriesCards = cards;
                else if (type == EmbyItemType.Episode) fixture.Settings.Ui.EpisodeCards = cards;
                else fixture.Settings.Ui.MovieCards = cards;
            }
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ThemeHost.Apply(savedTheme);
            HomeMotion.ApplyPreference(motionPreference);
            ResizeInspect(1420, 980);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-customization-pages.txt"), readings);
        }
    }

    private async Task CheckImmersiveSwitchAsync(DetailPage page, UiSettings ui)
    {
        var enabled = ui.EnableImmersive;
        var target = page.ViewModel.PlayTarget;
        var subject = page.ViewModel.CurrentItem;
        var title = page.ViewModel.Title;
        var subline = page.ViewModel.Subline;
        var panels = Get<StackPanel>(page, "BodySheet").Children.ToArray();
        try
        {
            foreach (var width in new[] { 1200, 760 })
            {
                ResizeProbeClient(width, 900);
                await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - width) < 1; });
                ui.EnableImmersive = false;
                ShellPrefs.ApplyAppearance(ui);
                await LayoutAsync(page);
                var card = Get<Border>(page, "HeroCardTop");
                var wide = width == 1200;
                var oldEpisodeBackdrop = !wide && subject?.Type == EmbyItemType.Episode
                    && ItemArtwork.Hero(subject).Count > 0;
                Require(page.ViewModel.BackdropShown == oldEpisodeBackdrop && !page.ViewModel.Showcase
                    && Get<Grid>(page, "Backdrop").Visibility == (oldEpisodeBackdrop ? Visibility.Visible : Visibility.Collapsed),
                    "关闭沉浸后背景未恢复该类型原有宽窄规则");
                var expectedCard = subject?.Type != EmbyItemType.Episode || wide;
                Require(page.ViewModel.HeroCardShown == expectedCard && card.Visibility == (expectedCard ? Visibility.Visible : Visibility.Collapsed),
                    "简洁三类详情始终保留卡面，单集恢复原宽窗卡片布局");
                Require(Get<StackPanel>(page, "ColumnPickers").Visibility == page.ViewModel.ColumnPickersVisibility,
                    "同页切换后视频信息父容器没有同步显隐");
                if (!oldEpisodeBackdrop) Require(page.ViewModel.TailMinHeight == 0, "无背景仍保留了背景留白");
                Require(ReferenceEquals(target, page.ViewModel.PlayTarget), "外观切换改变播放目标");
                Require(page.ViewModel.Title == title && page.ViewModel.Subline == subline
                    && ReferenceEquals(subject, page.ViewModel.CurrentItem), "外观切换改变详情主体或标题");
                await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
                await SaveProbeScreenAsync($"detail-immersive-disabled-{page.ViewModel.ItemType}-{width}-screen.png");
                ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(ui);
                await LayoutAsync(page);
                Require(page.ViewModel.HeroCardShown == (wide && subject?.Type == EmbyItemType.Episode && !page.ViewModel.BackdropShown),
                    "重新启用后有图沉浸或无图卡面没有恢复");
                Require(page.ViewModel.Showcase == (wide && page.ViewModel.BackdropShown)
                    && page.ViewModel.IsCompact == !wide,
                    "重新启用沉浸后未恢复宽窗沉浸或窄窗紧凑布局");
                Require((Get<Grid>(page, "Backdrop").Visibility == Visibility.Visible) == page.ViewModel.BackdropShown,
                    "重新启用后背景显隐未跟随视图模型");
                Require(panels.SequenceEqual(Get<StackPanel>(page, "BodySheet").Children), "外观切换重建或移动了正文实例");
                if (page.ViewModel.Showcase) RequireImmersiveSurface(page);
                if (subject?.Type == EmbyItemType.Episode) RequireEpisodeSubject(page, subject);
                await SaveProbeScreenAsync($"detail-immersive-enabled-{page.ViewModel.ItemType}-{width}-screen.png");
            }
        }
        finally
        {
            ui.EnableImmersive = enabled;
            ShellPrefs.ApplyAppearance(ui);
        }
    }

    private static void RequireImmersiveSurface(DetailPage page)
    {
        Require(page.ViewModel.Showcase && !page.ViewModel.HeroCardShown,
            "有图沉浸仍保留头部拼接卡面");
        Require(Get<Border>(page, "HeroCardTop").Visibility == Visibility.Collapsed
            && Get<Border>(page, "HeroCardBottom").Visibility == Visibility.Collapsed,
            "沉浸头部卡面没有完整收起");
        Require(Get<AcrylicBrush>(page, "_panelFill").Opacity == 0
            && Get<SolidColorBrush>(page, "_panelLine").Opacity == 0,
            "沉浸正文仍保留卡片底色或描边");
        Require(Get<Border>(page, "BodyPaper").Background is SolidColorBrush { Color.A: 0 },
            "沉浸正文纸面仍遮住固定背景");
        Require(Get<StackPanel>(page, "InfoPanel").Background is AcrylicBrush { FallbackColor.A: 255, Opacity: 1 },
            "沉浸信息表没有保留统一亚克力卡面");
        Require(!page.ViewModel.ShowShelfLines && page.ViewModel.TailMinHeight == 0
            && Get<Border>(page, "CardTailGap").ActualHeight < 0.01,
            "沉浸仍保留货架线条或卡面收尾留白");
    }

    private static void RequireEpisodeSubject(DetailPage page, EmbyItem episode)
    {
        var view = page.ViewModel;
        Require(view.CurrentItem?.Id == episode.Id && view.PlayTarget?.Id == episode.Id,
            "单集外观切换把详情主体或播放目标改成剧集");
        Require(view.Title == ItemDetail.Title(episode) && view.Subline == ItemDetail.Subline(episode),
            "单集沉浸丢失剧名、集号或本集标题");
        Require(view.TitleLink?.Id == episode.SeriesId && view.TitleLink?.Type == EmbyItemType.Series,
            "单集剧名导航不再指向所属剧");
        Require(view.Episodes.Count == 0 || view.Episodes.Any(item => item.Id == episode.Id),
            "单集完整播放上下文丢失当前集");
        Require(Get<StackPanel>(page, "SeasonPanel").Visibility == Visibility.Collapsed,
            "集页仍显示全部剧季卡片");
        if (view.EpisodeShelf is { Cards.Count: > 0 })
            Require(Get<ItemsRepeater>(page, "EpisodeList").Visibility == Visibility.Visible
                && Get<ShelfStrip>(page, "EpisodeStrip").Visibility == Visibility.Collapsed,
                "集页没有使用当前季度纵向集列表");
        Require(Get<StackPanel>(page, "ColumnPickers").Visibility == view.ColumnPickersVisibility
            && Get<StackPanel>(page, "PickerPanel").Visibility == view.PickersVisibility,
            "单集切换后文件选项容器未恢复");
    }

    private static void RequireCardOrder(DetailPage page, IEnumerable<string> expected)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EpisodePanel"] = "episodes",
            ["InfoPanel"] = "info",
            ["SeasonPanel"] = "seasons",
            ["CastPanel"] = "cast",
            ["SimilarPanel"] = "similar"
        };
        var wanted = expected.ToArray();
        var panels = Get<StackPanel>(page, "BodySheet").Children.OfType<FrameworkElement>()
            .Where(element => names.ContainsKey(element.Name)).ToArray();
        var order = panels.Select(element => names[element.Name]).Where(wanted.Contains).ToArray();
        Require(order.SequenceEqual(wanted), "实际卡片顺序与配置不同：" + string.Join(',', order));
        var visible = panels.Where(element => element.Visibility == Visibility.Visible && element.ActualHeight > 0).ToArray();
        for (var index = 1; index < visible.Length; index++)
        {
            var previous = visible[index - 1];
            var previousTop = previous.TransformToVisual(page).TransformPoint(default).Y;
            var currentTop = visible[index].TransformToVisual(page).TransformPoint(default).Y;
            Require(currentTop >= previousTop + previous.ActualHeight - 1, "重排后的卡片发生重叠");
        }
    }

    private async Task CheckFixedAcrylicBackdropAsync(DetailPage page, UiSettings ui, Func<int> requestCount)
    {
        ui.EnableImmersive = true;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.HeroImage is not null && page.ViewModel.GaussianBackdropReady);
        var original = page.ViewModel.HeroImage;
        var originalPixels = await ReadBackdropPixelsAsync(original!);
        var baselineReads = requestCount();
        var savedStrength = ui.BackdropOpacity;
        try
        {
            foreach (var width in new[] { 760, 1100 })
                foreach (var strength in new[] { 0d, 20, 45, 100 })
                {
                    await ResizeResponsiveAsync(page, width, 1120);
                    ui.BackdropOpacity = strength;
                    ShellPrefs.ApplyAppearance(ui);
                    await LayoutAsync(page);
                    Require(page.ViewModel.GaussianBackdropReady && !page.ViewModel.GaussianBackdropApplied
                        && !page.ViewModel.UseGaussianBackdrop, "固定亚克力仍运行CPU高斯处理");
                    Require(ReferenceEquals(original, page.ViewModel.HeroImage), "背景不透明度或窗口变化重新生成了主图");
                    var current = await ReadBackdropPixelsAsync(page.ViewModel.HeroImage!);
                    Require(current.Width == originalPixels.Width && current.Height == originalPixels.Height
                        && current.Pixels.AsSpan().SequenceEqual(originalPixels.Pixels), "亚克力设置改变了原图像素");
                    RequireDetailBackdropFade(page);
                    RequireFixedDetailForeground(page);
                    await CheckBackdropFadePixelsAsync(page);
                    await SaveProbeScreenAsync($"detail-fixed-acrylic-{page.ViewModel.ItemType}-{width}-{strength}-screen.png");
                }
            // 快速更新不能恢复被删除的模式，也不能排队重新请求图片。
            foreach (var strength in new[] { 100d, 0, 20 })
            {
                ui.BackdropOpacity = strength;
                ShellPrefs.ApplyAppearance(ui);
            }
            await LayoutAsync(page);
            Require(page.ViewModel.BackdropStrength == 0.2 && ReferenceEquals(original, page.ViewModel.HeroImage),
                "快速调整背景后晚到结果覆盖当前20%设置");
            RequireDetailBackdropFade(page);
            Require(requestCount() == baselineReads, "外观调整重新请求媒体或图片");
        }
        finally
        {
            ui.BackdropOpacity = savedStrength;
            ShellPrefs.ApplyAppearance(ui);
        }
    }

    private static void RequireFixedDetailForeground(DetailPage page)
    {
        var expected = (SolidColorBrush)Application.Current.Resources["EgOnScrimBrush"];
        foreach (var name in new[] { "TitleText", "ScoreText", "ScoreSourceText", "HeroFacts", "Overview", "DetailVideoText", "GenreLine" })
        {
            var node = Get<TextBlock>(page, name);
            if (node.Visibility != Visibility.Visible || node.ActualWidth <= 0 || node.ActualHeight <= 0) continue;
            Require(node.Foreground is SolidColorBrush text && text.Color == expected.Color,
                name + " 没有保持固定文字角色颜色");
        }
    }

    private static byte ScrimExpected(DetailPage page) =>
        (byte)Math.Round(Math.Clamp(page.ViewModel.BackdropStrength, 0, 1) * byte.MaxValue);

    private static byte ScrimAlphaOf(DetailPage page)
    {
        var fade = Get<Border>(page, "BackdropFade");
        return fade.Visibility == Visibility.Visible && fade.Background is AcrylicBrush acrylic
            ? (byte)Math.Round(acrylic.Opacity * byte.MaxValue) : (byte)0;
    }

    private async Task<(byte[] Pixels, int Width, int Height)> ReadBackdropPixelsAsync(ImageSource picture)
    {
        var image = new Image { Source = picture, Width = 640, Height = 360, Stretch = Stretch.Fill };
        _root.Children.Add(image);
        try
        {
            await LayoutAsync(image);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(image, 640, 360);
            return ((await bitmap.GetPixelsAsync()).ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
        }
        finally { _root.Children.Remove(image); }
    }

    private static async Task CheckControlBackgroundOpacityAsync(DetailPage page, UiSettings ui)
    {
        var controls = Descendants(Get<StackPanel>(page, "ColumnActions")).OfType<Control>()
            .Where(control => (control is Button or ToggleButton) && control.Visibility == Visibility.Visible
                && control.ActualWidth > 0 && control.ActualHeight > 0).ToArray();
        var pickers = Descendants(page).OfType<EgPicker>()
            .Where(picker => picker.ActualWidth > 0 && picker.ActualHeight > 0).ToArray();
        Require(controls.Length > 0 && pickers.Length > 0, "外观夹具没有操作按钮或选择框");
        var savedStrength = ui.ControlBackgroundOpacity;
        try
        {
            foreach (var control in controls.Concat(pickers.Cast<Control>()))
            {
                control.ApplyTemplate();
                var states = control is ToggleButton
                    ? new[] { "Normal", "PointerOver", "Pressed", "Checked", "CheckedPointerOver", "CheckedPressed" }
                    : new[] { "Normal", "PointerOver", "Pressed" };
                var restore = control is ToggleButton { IsChecked: true } ? "Checked" : "Normal";
                try
                {
                    foreach (var state in states)
                    {
                        var foreground = control.Foreground;
                        Require(VisualStateManager.GoToState(control, state, false), "控件缺少状态：" + state);
                        AcrylicBrush? owned = null;
                        foreach (var strength in new[] { 0d, 37, 99.9, 100 })
                        {
                            ui.ControlBackgroundOpacity = strength;
                            ShellPrefs.ApplyAppearance(ui);
                            await LayoutAsync(control);
                            var fill = Fill();
                            Require(fill is AcrylicBrush, control.Name + "/" + state + " 没有固定使用亚克力");
                            var glass = (AcrylicBrush)fill!;
                            owned ??= glass;
                            Require(ReferenceEquals(owned, glass), "调整底色重建实际控件画刷");
                            Require(glass.FallbackColor.A == 255 && control.Opacity == 1
                                && ReferenceEquals(control.Foreground, foreground), "底色调整改变文字或整个控件的不透明度");
                            var feedback = state is "PointerOver" or "Pressed" or "CheckedPointerOver" or "CheckedPressed";
                            if (strength == 0 && !feedback)
                                Require(glass.Opacity == 0, "亚克力零档常态没有完全透明");
                            else if (strength == 100)
                                Require(glass.AlwaysUseFallback && glass.Opacity == 1, "亚克力100档没有回到实底");
                            else
                            {
                                var normal = 15d / 255;
                                var floor = state.Contains("Pressed", StringComparison.Ordinal) ? 8d / 255 : 21d / 255;
                                var amount = strength / 100;
                                var expected = !feedback ? amount : amount <= normal ? floor
                                    : floor + (1 - floor) * (amount - normal) / (1 - normal);
                                Require(!glass.AlwaysUseFallback && Math.Abs(glass.Opacity - expected) < 0.001
                                    && Math.Abs(glass.TintOpacity - ui.ControlTintOpacity / 100) < 0.001
                                    && Math.Abs((glass.TintLuminosityOpacity ?? -1) - ui.ControlTintLuminosityOpacity / 100) < 0.001,
                                    "亚克力中间档没有保留材质或浓度反馈");
                            }
                        }
                    }
                }
                finally { VisualStateManager.GoToState(control, restore, false); }

                Brush? Fill() => control is EgPicker
                    ? Descendants(control).OfType<Border>().Single(border => border.Name == "Background").Background
                    : Descendants(control).OfType<ContentPresenter>().Single(presenter => presenter.Name == "ContentPresenter").Background;
            }
        }
        finally
        {
            ui.ControlBackgroundOpacity = savedStrength;
            ShellPrefs.ApplyAppearance(ui);
        }
    }

    private async Task CheckAcrylicTintPreferencesAsync(DetailPage page, UiSettings ui)
    {
        var saved = (ui.ControlTintOpacity, ui.ControlTintLuminosityOpacity, ui.BackdropTintOpacity,
            ui.BackdropTintLuminosityOpacity, ui.BackdropTintFromArtwork, ui.BackdropColor);
        var picture = page.ViewModel.HeroImage;
        var foreground = Get<TextBlock>(page, "Overview").Foreground;
        try
        {
            foreach (var (tint, luminosity) in new[] { (20d, 80d), (80d, 20d) })
            {
                ui.ControlTintOpacity = tint;
                ui.ControlTintLuminosityOpacity = luminosity;
                ui.BackdropTintOpacity = luminosity;
                ui.BackdropTintLuminosityOpacity = tint;
                ui.BackdropTintFromArtwork = false;
                ui.BackdropColor = "#245A8E";
                ShellPrefs.ApplyAppearance(ui);
                await LayoutAsync(page);
                RequireDetailBackdropFade(page);
                var play = Descendants(page).OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == "ColumnPlayButton");
                var control = (AcrylicBrush)play.Resources["ButtonBackground"];
                Require(Math.Abs(control.TintOpacity - tint / 100) < 0.001 && Math.Abs((control.TintLuminosityOpacity ?? -1) - luminosity / 100) < 0.001
                    && control.FallbackColor == control.TintColor, "按钮色调/亮度属性没有实际下发到亚克力");
                var backdrop = (AcrylicBrush)Get<Border>(page, "BackdropFade").Background;
                Require(backdrop.TintColor == Windows.UI.Color.FromArgb(255, 36, 90, 142)
                    && backdrop.FallbackColor == backdrop.TintColor,
                    "背景没有采用自定义染色或自动回退色");
                Require(ReferenceEquals(picture, page.ViewModel.HeroImage)
                    && ReferenceEquals(foreground, Get<TextBlock>(page, "Overview").Foreground),
                    "亚克力参数调整重新处理原图或改掉字体画刷");
                await SaveProbeScreenAsync($"detail-acrylic-tint-custom-{page.ViewModel.ItemType}-{tint}-{luminosity}.png");
                ui.BackdropTintFromArtwork = true;
                ShellPrefs.ApplyAppearance(ui);
                await LayoutAsync(page);
                RequireDetailBackdropFade(page);
                Require(ui.BackdropColor == "#245A8E", "恢复主图染色清空了自定义颜色");
                await SaveProbeScreenAsync($"detail-acrylic-tint-artwork-{page.ViewModel.ItemType}-{tint}-{luminosity}.png");
            }
        }
        finally
        {
            (ui.ControlTintOpacity, ui.ControlTintLuminosityOpacity, ui.BackdropTintOpacity,
                ui.BackdropTintLuminosityOpacity, ui.BackdropTintFromArtwork, ui.BackdropColor) = saved;
            ShellPrefs.ApplyAppearance(ui);
        }
    }

    private async Task CheckDetailMenuAcrylicAsync(DetailPage page, Fixture fixture, string theme)
    {
        var originalTopMost = _window!.TopMost;
        var enabled = fixture.Actions.MoviePilotEnabled;
        try
        {
            _window.TopMost = true;
            _window.Activate();
            fixture.Actions.MoviePilotEnabled = true;
            var anchor = Get<Button>(page, "ColumnMoreButton");
            Call(page, "OnMoreClicked", anchor, new RoutedEventArgs());
            await LayoutAsync(page);
            var presenters = Presenters();
            Require(presenters.Length == 1, "详情更多菜单没有打开");
            Check(presenters[0]);
            var material = (DetailMenuBackdrop)presenters[0].SystemBackdrop;
            // 自动走查可能在用户操作其他窗口时运行。记录实际状态，并核对回退仍为卡片色；
            // Active 透底由独立检查入口和前台屏幕证据验证，不能把 Fallback 当作透明成功。
            Require(material.UsesCardColor, "菜单染色或回退色没有跟随当前卡片色：" + material.MaterialState);
            File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "detail-menu-material.txt"),
                $"{theme}: {material.MaterialState}\n");
            await SaveProbeScreenAsync($"detail-menu-acrylic-{theme}-screen.png");
            var submenu = Descendants(presenters[0]).OfType<MenuFlyoutSubItem>().First();
            ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(submenu)
                .GetPattern(PatternInterface.ExpandCollapse)).Expand();
            await UntilAsync(() => Presenters().Length == 2);
            foreach (var presenter in Presenters()) Check(presenter);
            await SaveProbeScreenAsync($"detail-submenu-acrylic-{theme}-screen.png");
        }
        finally
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).ToArray()) popup.IsOpen = false;
            fixture.Actions.MoviePilotEnabled = enabled;
            _window.TopMost = originalTopMost;
        }

        MenuFlyoutPresenter[] Presenters() => VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().ToArray();
        static void Check(MenuFlyoutPresenter presenter)
        {
            Require(ReferenceEquals(presenter.Style, Application.Current.Resources["EgDetailMenuPresenterStyle"]),
                "详情菜单或子菜单丢失局部材质样式");
            Require(presenter.Background is SolidColorBrush { Color.A: 0 }, "详情菜单实色背景遮住了系统亚克力");
            Require(presenter.SystemBackdrop is DetailMenuBackdrop,
                "详情菜单未使用轻透系统亚克力背景");
        }
    }

    private async Task CheckHomeBackgroundOpacityAsync(Fixture fixture, EmbyItem item, Func<int> requestCount)
    {
        fixture.Session.Capture().Bind(item);
        var banner = new HomeBanner();
        var ui = fixture.Settings.Ui;
        var savedStrength = ui.ControlBackgroundOpacity;
        _root.Children.Add(banner);
        try
        {
            banner.Apply([new BannerSlide(item, fixture.Images)]);
            await UntilAsync(() => banner.FrontPicture is not null);
            await LayoutAsync(banner);
            var button = Get<Button>(banner, "OpenButton");
            var baselineReads = requestCount();
            var homePicture = banner.FrontPicture;
            foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
            {
                Require(VisualStateManager.GoToState(button, state, false), "首页详情按钮缺少状态：" + state);
                await LayoutAsync(banner);
                var presenter = Descendants(button).OfType<ContentPresenter>().Single(part => part.Name == "ContentPresenter");
                var surface = Descendants(button).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>().Single(part => part.Name == "Surface");
                var foreground = presenter.Foreground;
                AcrylicBrush? owned = null;
                foreach (var strength in new[] { 0d, 37, 100 })
                {
                    ui.ControlBackgroundOpacity = strength;
                    ui.BackdropOpacity = strength;
                    ShellPrefs.ApplyAppearance(ui);
                    await LayoutAsync(banner);
                    Require(surface.Fill is AcrylicBrush, "首页详情按钮没有固定使用亚克力");
                    var glass = (AcrylicBrush)surface.Fill;
                    owned ??= glass;
                    Require(ReferenceEquals(owned, glass) && glass.FallbackColor.A == 255,
                        "首页底色调整替换画刷或丢失回退色");
                    Require(button.Opacity == 1 && ReferenceEquals(presenter.Foreground, foreground),
                        "首页详情文字跟着底色改变");
                    if (strength == 100)
                        Require(glass.AlwaysUseFallback && glass.Opacity == 1, "首页亚克力100档未回到实底");
                    else if (strength > 0)
                        Require(!glass.AlwaysUseFallback && Math.Abs(glass.Opacity - strength / 100) < 0.001
                            && Math.Abs(glass.TintOpacity - ui.ControlTintOpacity / 100) < 0.001
                                    && Math.Abs((glass.TintLuminosityOpacity ?? -1) - ui.ControlTintLuminosityOpacity / 100) < 0.001,
                            "首页中间档没有按滑块调整亚克力透明度");
                    foreach (var layer in new[] { Get<Grid>(banner, "LayerA"), Get<Grid>(banner, "LayerB") })
                        Require(layer.Background is ImageBrush picture && Math.Abs(picture.Opacity - 1) < 0.001,
                            "详情背景设置改变了主页图片的不透明度");
                    Require(ReferenceEquals(homePicture, banner.FrontPicture), "详情外观改动重新处理了首页轮播图片");
                }
            }
            VisualStateManager.GoToState(button, "Normal", false);
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(banner);
                await UntilAsync(() => Math.Abs(banner.FrontLayer.Opacity - 1) < 0.001);
                Require(Get<Border>(banner, "Top").Visibility == Visibility.Visible
                    && Get<Border>(banner, "Fade").Visibility == Visibility.Visible
                    && Get<Border>(banner, "RightShade").Visibility == Visibility.Visible,
                    "详情设置撤掉了主页轮播原遮罩");
                RequireHomeWithoutBottomFade(banner);
                await SaveProbeScreenAsync($"home-fixed-acrylic-{theme.Id}-screen.png");
            }
            Require(requestCount() == baselineReads, "仅修改首页外观重新请求了图片或媒体");
        }
        finally
        {
            ui.ControlBackgroundOpacity = savedStrength;
            ShellPrefs.ApplyAppearance(ui);
            _root.Children.Remove(banner);
        }
    }
}
