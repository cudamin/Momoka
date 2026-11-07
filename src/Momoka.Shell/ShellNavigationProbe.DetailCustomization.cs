using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
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
                    ("单集页面卡片", EmbyItemType.Episode, 5),
                    ("电影页面卡片", EmbyItemType.Movie, 3)
                })
                {
                    var section = page.ViewModel.Sections.Single(card => card.Title == title);
                    var row = section.Rows.OfType<SettingHomeLayoutRow>().Single();
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
            foreach (var (label, choice) in new[]
            {
                ("按钮与选择框背景", "亚克力"), ("背景图效果", "高斯模糊")
            })
            {
                var row = appearance.Rows.OfType<SettingChoiceRow>().Single(item => item.Label == label);
                Require(row.Choices.Count == (label == "背景图效果" ? 3 : 2), label + " 缺少背景效果选择");
                var host = Descendants(page).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, row));
                host.StartBringIntoView();
                await LayoutAsync(host);
                var picker = Descendants(host).OfType<ComboBox>().Single();
                picker.SelectedItem = row.Choices.Single(item => item.Label == choice);
                await UntilAsync(() => row.Selected?.Label == choice);
                await SaveProbeScreenAsync(label == "背景图效果" ? "settings-gaussian-choice-screen.png" : "settings-acrylic-choice-screen.png");
            }
            Require(fixture.Settings.Ui.UseAcrylicControls && fixture.Settings.Ui.UseGaussianBackdrop,
                "材质选择没有写回设置");
            var effectRow = appearance.Rows.OfType<SettingChoiceRow>().Single(row => row.Label == "背景图效果");
            effectRow.Selected = effectRow.Choices.Single(choice => choice.Label == "亚克力");
            Require(fixture.Settings.Ui.UseAcrylicBackdrop && !fixture.Settings.Ui.UseGaussianBackdrop,
                "亚克力选择未保存或与高斯同时开启");
            await LayoutAsync(page);
            await SaveProbeScreenAsync("settings-backdrop-acrylic-choice-screen.png");
            effectRow.Selected = effectRow.Choices.Single(choice => choice.Label == "高斯模糊");
            Require(!fixture.Settings.Ui.UseAcrylicBackdrop && fixture.Settings.Ui.UseGaussianBackdrop,
                "切回高斯未关闭亚克力");
            foreach (var (label, value) in new[]
            {
                ("按钮与选择框底色不透明度（%）", 37d), ("背景图不透明度（%）", 45d)
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
            var modeRow = appearance.Rows.OfType<SettingChoiceRow>().Single(row => row.Label == "显示模式");
            modeRow.Selected = modeRow.Choices.Single(choice => choice.Label == "简洁模式");
            Require(!appearance.Rows.Any(row => row.Label is "背景图效果" or "背景遮罩颜色" or "背景图不透明度（%）"),
                "简洁模式没有隐藏完整背景设置组");
            modeRow.Selected = modeRow.Choices.Single(choice => choice.Label == "沉浸模式");
            await UntilAsync(() => fixture.Settings.Ui.EnableImmersive);
            Require(fixture.Settings.Ui.EnableImmersive, "显示模式选择没有同步沉浸状态");
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
                foreach (var name in new[] { "ScoreText", "ScoreSourceText", "HeroFacts", "Overview", "DetailVideoText", "GenreLine" })
                {
                    var node = Get<TextBlock>(page, name);
                    if (node.Visibility != Visibility.Visible || node.ActualWidth <= 0 || node.ActualHeight <= 0) continue;
                    var background = (ThemeColor)Call(page, "ReadingBackground", node)!;
                    var expectedInk = page.ViewModel.BackdropShown && !page.ViewModel.HeroCardShown
                        ? BackdropReading.Foreground(background) : ThemeColor.Rgb(255, 255, 255);
                    Require(node.Foreground is SolidColorBrush text && text.Color == ThemeHost.ToColor(expectedInk),
                        name + $" 没有根据实际背景更新阅读颜色：实际{(node.Foreground as SolidColorBrush)?.Color}，预期{ThemeHost.ToColor(expectedInk)}，尺寸{node.ActualWidth:0.#}×{node.ActualHeight:0.#}");
                }
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
                // 设置页那一段刚把「背景图效果」选成高斯模糊，这里测的是暗色遮罩那一支，先切回来再比浓度。
                fixture.Settings.Ui.UseGaussianBackdrop = false;
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
                    Require(ScrimAlphaOf(page) == 0, "0 档还留着遮罩");
                    // 0 档是「关掉遮罩」，不是「把图收走」：画面照旧站在那儿，只是不再压那一层墨。
                    Require(picture.Opacity == 1, "0 档把背景图本身调透明了");
                }
                // 关掉遮罩只收那层墨，版式不动：海报、片名和那排键还在原位。
                Require(Math.Abs(band.ActualHeight - heroHeight) < 0.5, "关掉遮罩改变了头图那一格的高");
                await SaveProbeScreenAsync($"detail-customization-{item.Type}-backdrop-clear.png");
                fixture.Settings.Ui.BackdropOpacity = 45;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await CheckGaussianBackdropAsync(page, fixture.Settings.Ui, () => reads);
                ResizeProbeClient(1100, 1120);
                await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                foreach (var theme in UiThemes.All)
                {
                    ThemeHost.Apply(theme);
                    await LayoutAsync(page);
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
                readings.Add($"{item.Type}：{string.Join(',', expected.Select(card => card.Key))}，隐藏恢复同实例，背景遮罩0/45/100，窄窗保留紧凑布局");
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
        var gaussian = ui.UseGaussianBackdrop;
        var acrylic = ui.UseAcrylicBackdrop;
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
                Require(ReferenceEquals(target, page.ViewModel.PlayTarget)
                    && ui.UseGaussianBackdrop == gaussian && ui.UseAcrylicBackdrop == acrylic,
                    "外观切换改变播放目标或清空背景效果偏好");
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
        if (page.ViewModel.ItemType == EmbyItemType.Episode)
            Require(Get<StackPanel>(page, "InfoPanel").Background is AcrylicBrush { FallbackColor.A: 255, Opacity: 1 },
                "单集沉浸信息表没有保留可读亚克力卡面");
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
            "单集清单丢失当前集");
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

    private async Task CheckGaussianBackdropAsync(DetailPage page, UiSettings ui, Func<int> requestCount)
    {
        ui.EnableImmersive = true;
        await ResizeResponsiveAsync(page, 760, 820);
        await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
        ui.UseGaussianBackdrop = false;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !page.ViewModel.GaussianBackdropApplied);
        var normal = page.ViewModel.HeroImage;
        var originalPixels = await ReadBackdropPixelsAsync(normal!);
        var baselineReads = requestCount();

        // 强度就是模糊程度：100 拉满、45 比它清楚、0 回到原图（2026-11-06 用户令）。
        ui.BackdropOpacity = 100;
        ui.UseGaussianBackdrop = true;
        ui.UseAcrylicControls = true;
        ui.ControlBackgroundOpacity = 25;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !page.ViewModel.GaussianBackdropApplied);
        Require(ReferenceEquals(normal, page.ViewModel.HeroImage), "窄窗紧凑背景被高斯偏好重新处理");
        ResizeProbeClient(1100, 1120);
        await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
        ui.EnableImmersive = true;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady
            && !ReferenceEquals(normal, page.ViewModel.HeroImage));
        Require(page.ViewModel.Showcase && !page.ViewModel.IsCompact, "开启高斯后的宽窗未保持沉浸布局");
        Require(!ReferenceEquals(normal, page.ViewModel.HeroImage), "高斯背景没有渲染新的图片");
        var full = page.ViewModel.HeroImage;
        var fullPixels = await ReadBackdropPixelsAsync(full!);
        Require(originalPixels.Width == fullPixels.Width && originalPixels.Height == fullPixels.Height,
            "高斯改变了图片尺寸");
        var original = PictureVariation(originalPixels.Pixels, originalPixels.Width);
        var fullBlur = PictureVariation(fullPixels.Pixels, fullPixels.Width);
        Require(fullBlur.Variation < original.Variation * 0.9 && Math.Abs(fullBlur.Mean - original.Mean) < 2,
            $"高斯拉满没有平滑图像边界或错误压暗：纹理{original.Variation:0.###}→{fullBlur.Variation:0.###}，均值{original.Mean:0.###}→{fullBlur.Mean:0.###}");

        ui.BackdropOpacity = 45;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !ReferenceEquals(full, page.ViewModel.HeroImage));
        var halfPixels = await ReadBackdropPixelsAsync(page.ViewModel.HeroImage!);
        var half = PictureVariation(halfPixels.Pixels, halfPixels.Width);
        // 夹具那张图本身很平，纹理差是零点几的量级：判据取「相对更大 5% 且至少多 0.01」，不写死一个绝对门槛。
        Require(half.Variation > fullBlur.Variation + Math.Max(0.01, fullBlur.Variation * 0.05),
            $"强度减半没有变清楚：拉满{fullBlur.Variation:0.###}→45%{half.Variation:0.###}");

        var halfImage = page.ViewModel.HeroImage;
        ui.BackdropOpacity = 0;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !ReferenceEquals(halfImage, page.ViewModel.HeroImage));
        var clearPixels = await ReadBackdropPixelsAsync(page.ViewModel.HeroImage!);
        var clear = PictureVariation(clearPixels.Pixels, clearPixels.Width);
        Require(Math.Abs(clear.Variation - original.Variation) < 0.01 && Math.Abs(clear.Mean - original.Mean) < 0.01,
            $"0 档没有关掉模糊：纹理{original.Variation:0.###}→{clear.Variation:0.###}，均值{original.Mean:0.###}→{clear.Mean:0.###}");

        var clearImage = page.ViewModel.HeroImage;
        ui.BackdropOpacity = 45;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !ReferenceEquals(clearImage, page.ViewModel.HeroImage));
        var settled = page.ViewModel.HeroImage;
        var settledReading = GaussianReading(page);

        foreach (var theme in UiThemes.All)
        {
            ThemeHost.Apply(theme);
            await LayoutAsync(page);
            var play = Descendants(Get<StackPanel>(page, "CompactBlock")).OfType<Button>().First();
            var action = Descendants(play).OfType<ContentPresenter>().Single(part => part.Name == "ContentPresenter").Background;
            Require(action is AcrylicBrush { AlwaysUseFallback: false } acrylic
                && acrylic.TintColor == Windows.UI.Color.FromArgb(255, 255, 255, 255)
                && acrylic.FallbackColor == Windows.UI.Color.FromArgb(255, 255, 255, 255),
                "操作亚克力没有保留当前白色染色与回退");
            Require(Get<TextBlock>(page, "Overview").Foreground is SolidColorBrush text
                && (text.Color == Microsoft.UI.Colors.White || text.Color == Windows.UI.Color.FromArgb(255, 22, 22, 22)),
                "高斯简介文字没有选取可读的深浅色");
            var themeReading = GaussianReading(page);
            File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "detail-gaussian-generation.txt"),
                $"{page.ViewModel.ItemType}/{theme.Id}: before({settledReading}), after({themeReading}), imageSame={ReferenceEquals(settled, page.ViewModel.HeroImage)}\n");
            Require(ReferenceEquals(settled, page.ViewModel.HeroImage),
                $"切主题重新处理高斯图片：{theme.Id}, before({settledReading}), after({themeReading})");
            Require(Get<FrameworkElement>(page, "HeroBand") is Panel { Background: null }
                or Panel { Background: SolidColorBrush { Color.A: 0 } }, "高斯头部重复叠暗");
            var tail = Get<StackPanel>(page, "HeroTail");
            Require(tail.Background is null or SolidColorBrush { Color.A: 0 }, "高斯尾部重复叠暗");
            Require(Get<Border>(page, "TitleWash").Background is null or SolidColorBrush { Color.A: 0 },
                "高斯标题栏重复叠暗");
            Require(ScrimAlphaOf(page) == ScrimExpected(page) && ScrimAlphaOf(page) > 0,
                "高斯背景没有随强度变化的轻罩");
            RequireDetailBackdropFade(page);
            await CheckBackdropFadePixelsAsync(page);
            await SaveProbeScreenAsync($"detail-gaussian-{page.ViewModel.ItemType}-{theme.Id}-screen.png");
        }
        {
            ResizeProbeClient(1100, 1120);
            await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
            ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(ui);
            await UntilAsync(() => page.ViewModel.GaussianBackdropReady);
            await LayoutAsync(page);
            Require(page.ViewModel.Showcase && page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady,
                "宽窗沉浸没有保留高斯图片");
            RequireDetailBackdropFade(page);
            var sheet = Get<StackPanel>(page, "BodySheet");
            Require(sheet.Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<ShelfHead>()).All(head => head.Ink),
                "高斯沉浸货架标题缺少字形保护");
            Require(Get<StackPanel>(page, "InfoPanel").Background is AcrylicBrush { FallbackColor.A: 255 },
                "高斯沉浸信息表没有保持可读深色亚克力卡面");
            await SaveProbeScreenAsync($"detail-gaussian-immersive-{page.ViewModel.ItemType}-screen.png");
            ui.EnableImmersive = true;
            ResizeProbeClient(760, 820);
            await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 760) < 1; });
            ShellPrefs.ApplyAppearance(ui);
            await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !page.ViewModel.GaussianBackdropApplied);
            Require(ui.EnableImmersive && ui.UseGaussianBackdrop && page.ViewModel.IsCompact && page.ViewModel.BackdropShown,
                "切到窄窗清空高斯偏好或丢失紧凑背景");
            var compactPixels = await ReadBackdropPixelsAsync(page.ViewModel.HeroImage!);
            Require(compactPixels.Width == originalPixels.Width && compactPixels.Height == originalPixels.Height
                && compactPixels.Pixels.AsSpan().SequenceEqual(originalPixels.Pixels), "窄窗未恢复原图像素");
            await LayoutAsync(page);
        }
        ui.UseGaussianBackdrop = false;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => !page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady);
        Require(page.ViewModel.HeroImage is not null, "切回遮罩模式丢失背景图");
        RequireDetailBackdropFade(page);
        Require(page.ViewModel.ItemType == EmbyItemType.Episode
            ? Get<Grid>(page, "HeroBand").Background is SolidColorBrush { Color.A: > 0 }
            : ScrimAlphaOf(page) > 0, "切回后暗色遮罩未恢复");
        // 快速来回切换：晚到的旧模式不能覆盖当前高斯结果。
        ResizeProbeClient(1100, 1120);
        await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
        ui.EnableImmersive = true;
        ui.UseGaussianBackdrop = true;
        ShellPrefs.ApplyAppearance(ui);
        ui.UseGaussianBackdrop = false;
        ShellPrefs.ApplyAppearance(ui);
        ui.UseGaussianBackdrop = true;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady);
        Require(requestCount() == baselineReads, "背景效果切换重新请求媒体或图片");
        ui.UseGaussianBackdrop = false;
        ui.UseAcrylicControls = false;
        ui.EnableImmersive = true;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => !page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady);
    }

    private static GaussianProbeReading GaussianReading(DetailPage page) => new(
        Get<int>(page.ViewModel, "_heroBlurRadius"), Get<int>(page.ViewModel, "_heroBlurGeneration"),
        Get<bool>(page.ViewModel, "_heroRequestedGaussian"), Get<bool>(page.ViewModel, "_heroRendering"),
        page.ViewModel.PageWidth, page.ViewModel.IsCompact, page.ViewModel.Showcase,
        page.ViewModel.GaussianBackdropReady, page.ViewModel.GaussianBackdropApplied);

    /// <summary>这一页按设置该画出来的遮罩 alpha —— 探针比的是这个数，不自己另算一遍公式。</summary>
    private static byte ScrimExpected(DetailPage page) =>
        AppearanceOpacity.ReadingShadeAlpha(page.ViewModel.BackdropStrength, page.ViewModel.GaussianBackdropApplied);

    /// <summary>
    /// 屏上实际阅读遮罩的 alpha；有图详情读取整块背景遮罩，无图时尾部读数为 0。
    /// </summary>
    private static byte ScrimAlphaOf(DetailPage page)
    {
        var fade = Get<Border>(page, "BackdropFade");
        if (fade.Visibility == Visibility.Visible && fade.Background is SolidColorBrush brush)
            return brush.Color.A;
        return Get<StackPanel>(page, "HeroTail").Background is SolidColorBrush tail ? tail.Color.A : (byte)0;
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

    private static (double Variation, double Mean) PictureVariation(byte[] pixels, int width)
    {
        double variation = 0;
        double total = 0;
        var samples = 0;
        for (var at = 0; at + 7 < pixels.Length; at += 4)
        {
            if (at / 4 % width == width - 1) continue;
            for (var channel = 0; channel < 3; channel++)
            {
                total += pixels[at + channel];
                variation += Math.Abs(pixels[at + channel] - pixels[at + 4 + channel]);
                samples++;
            }
        }
        return (variation / samples, total / samples);
    }

    private static async Task CheckControlBackgroundOpacityAsync(DetailPage page, UiSettings ui)
    {
        var controls = Descendants(Get<StackPanel>(page, "ColumnActions")).OfType<Control>()
            .Where(control => (control is Button or ToggleButton) && control.Visibility == Visibility.Visible
                && control.ActualWidth > 0 && control.ActualHeight > 0).ToArray();
        var pickers = Descendants(page).OfType<EgPicker>()
            .Where(picker => picker.ActualWidth > 0 && picker.ActualHeight > 0).ToArray();
        Require(controls.Length > 0 && pickers.Length > 0, "外观夹具没有操作按钮或选择框");
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
                    ui.ControlBackgroundOpacity = 0;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(VisualStateManager.GoToState(control, state, false), "控件缺少状态：" + state);
                    await LayoutAsync(control);
                    var zero = Fill();
                    Require(zero is AcrylicBrush { AlwaysUseFallback: true }, "实际控件底色不是可调画刷");
                    var color = ((AcrylicBrush)zero!).FallbackColor;
                    var opacity = zero!.Opacity;
                    var baseline = opacity * color.A / 255d;
                    var feedback = state is "PointerOver" or "Pressed" or "CheckedPointerOver" or "CheckedPressed";
                    if (controls.Contains(control) || control is EgPicker)
                    {
                        var expectedOpacity = state.Contains("Pressed", StringComparison.Ordinal) ? 8d / 255 : feedback ? 21d / 255 : 0;
                        Require(color == Windows.UI.Color.FromArgb(255, 255, 255, 255) && Math.Abs(opacity - expectedOpacity) < 0.001,
                            control.Name + "/" + state + " 薄白零档或反馈不正确");
                    }
                    else
                        Require(feedback ? baseline > 0 : baseline == 0 && color.A == 0,
                            control.Name + "/" + state + (feedback ? " 零档丢了悬停的薄白" : " 零档还画着底色"));
                    var foreground = control.Foreground;
                    ui.ControlBackgroundOpacity = 37;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(VisualStateManager.GoToState(control, state, false), "外观调整后控件丢失状态：" + state);
                    await LayoutAsync(control);
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: true } adjusted
                            && Math.Abs(adjusted.Opacity * adjusted.FallbackColor.A / 255d - ExpectedStrength()) < 0.001,
                        control.Name + "/" + state + " 底色浓度不是滑块读数");
                    Require(control.Opacity == 1 && ReferenceEquals(control.Foreground, foreground),
                        "底色调节改变了整个控件或文字画刷");
                    ui.ControlBackgroundOpacity = 100;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: true } opaque && opaque.FallbackColor.A == 255 && opaque.Opacity == 1,
                        "底色100档未完全不透明");
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: true } readable && (controls.Contains(control) || control is EgPicker
                        ? readable.FallbackColor.R > 128 && readable.FallbackColor.G > 128 && readable.FallbackColor.B > 128
                        : readable.FallbackColor.R < 128 && readable.FallbackColor.G < 128 && readable.FallbackColor.B < 128),
                        "操作按钮或选择框的不透明底色不符合各自配色");
                    var owned = Fill();
                    ui.ControlBackgroundOpacity = 0;
                    ui.UseAcrylicControls = true;
                    ShellPrefs.ApplyAppearance(ui);
                    await LayoutAsync(control);
                    Require(ReferenceEquals(Fill(), owned) && Fill() is AcrylicBrush glass
                        && (feedback ? !glass.AlwaysUseFallback && Math.Abs(glass.TintOpacity - baseline) < 0.001
                            : glass.AlwaysUseFallback && glass.Opacity == 0) && glass.FallbackColor.A == 255,
                        control.Name + "/" + state + " 亚克力零档不是完全透明或丢了主题回退");
                    ui.ControlBackgroundOpacity = 37;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: false } thicker
                        && Math.Abs(thicker.TintOpacity - ExpectedStrength()) < 0.001,
                        "亚克力没有随不透明度增加");
                    ui.ControlBackgroundOpacity = 99.9;
                    ShellPrefs.ApplyAppearance(ui);
                    await LayoutAsync(control);
                    ui.ControlBackgroundOpacity = 100;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: true, Opacity: 1, FallbackColor.A: 255 },
                        "亚克力100档仍透出背景");
                    ui.UseAcrylicControls = false;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(control.Opacity == 1 && ReferenceEquals(control.Foreground, foreground),
                        "材质切换改变文字或整个按钮的不透明度");

                    double ExpectedStrength() => controls.Contains(control) || control is EgPicker
                        ? !feedback ? 0.37 : baseline + (1 - baseline) * (0.37 - 15d / 255) / (1 - 15d / 255)
                        : Math.Min(1, 0.37 + baseline);
                }
            }
            finally { VisualStateManager.GoToState(control, restore, false); }

            Brush? Fill() => control is EgPicker
                ? Descendants(control).OfType<Border>().Single(border => border.Name == "Background").Background
                : Descendants(control).OfType<ContentPresenter>().Single(presenter => presenter.Name == "ContentPresenter").Background;
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
        _root.Children.Add(banner);
        try
        {
            banner.Apply([new BannerSlide(item, fixture.Images)]);
            await UntilAsync(() => banner.FrontPicture is not null);
            await LayoutAsync(banner);
            var button = Get<Button>(banner, "OpenButton");
            var baselineReads = requestCount();
            foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
            {
                ui.ControlBackgroundOpacity = 0;
                ui.BackdropOpacity = 0;
                ShellPrefs.ApplyAppearance(ui);
                Require(VisualStateManager.GoToState(button, state, false), "首页详情按钮缺少状态：" + state);
                await LayoutAsync(banner);
                var presenter = Descendants(button).OfType<ContentPresenter>().Single(part => part.Name == "ContentPresenter");
                var surface = Descendants(button).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>().Single(part => part.Name == "Surface");
                Require(surface.Fill is AcrylicBrush { AlwaysUseFallback: true }, "首页详情按钮没有实际底色");
                var baseline = (AcrylicBrush)surface.Fill;
                var color = baseline.FallbackColor;
                var opacity = baseline.Opacity;
                var foreground = presenter.Foreground;
                ui.ControlBackgroundOpacity = 37;
                ui.BackdropOpacity = 45;
                ShellPrefs.ApplyAppearance(ui);
                Require(VisualStateManager.GoToState(button, state, false), "首页详情按钮外观调整后丢失状态：" + state);
                await LayoutAsync(banner);
                Require(surface.Fill is AcrylicBrush { AlwaysUseFallback: true } adjusted
                        && Math.Abs(adjusted.Opacity * adjusted.FallbackColor.A / 255d
                            - Math.Max(0.37, opacity * color.A / 255d)) < 0.001,
                    "首页详情按钮底色浓度不是滑块读数");
                Require(button.Opacity == 1 && ReferenceEquals(presenter.Foreground, foreground), "首页详情文字跟着底色变透明");
                foreach (var layer in new[] { Get<Grid>(banner, "LayerA"), Get<Grid>(banner, "LayerB") })
                    Require(layer.Background is ImageBrush picture && Math.Abs(picture.Opacity - 1) < 0.001,
                        "主页原本不透明的背景图变淡，或改动了淡入淡出层");
                ui.ControlBackgroundOpacity = 100;
                ShellPrefs.ApplyAppearance(ui);
                Require(VisualStateManager.GoToState(button, state, false), "首页实底状态切换失败");
                await LayoutAsync(banner);
                Require(surface.Fill is AcrylicBrush { AlwaysUseFallback: true } opaque && opaque.FallbackColor.A == 255 && opaque.Opacity == 1
                    && presenter.Foreground is SolidColorBrush ink
                    && ThemeColor.Contrast(ThemeColor.Rgb(ink.Color.R, ink.Color.G, ink.Color.B),
                        ThemeColor.Rgb(opaque.FallbackColor.R, opaque.FallbackColor.G, opaque.FallbackColor.B)) >= 4.5,
                    "主页详情按钮的100档底色没有完全不透明，或文字对比不足");
                var owned = surface.Fill;
                ui.ControlBackgroundOpacity = 37;
                ui.UseAcrylicControls = true;
                ShellPrefs.ApplyAppearance(ui);
                await LayoutAsync(banner);
                Require(ReferenceEquals(owned, surface.Fill)
                    && surface.Fill is AcrylicBrush { AlwaysUseFallback: false, Opacity: 1 },
                    "主页详情按钮没有即时切换亚克力");
                ui.ControlBackgroundOpacity = 0;
                ShellPrefs.ApplyAppearance(ui);
                await LayoutAsync(banner);
                Require(ReferenceEquals(owned, surface.Fill)
                    && surface.Fill is AcrylicBrush { AlwaysUseFallback: true } clear
                    && (state == "Normal" ? clear.Opacity == 0 : clear.Opacity * clear.FallbackColor.A / 255d > 0),
                    "主页亚克力零档没有关闭常态底色或丢失反馈");
                Require(ReferenceEquals(presenter.Foreground, foreground), "主页材质切换修改了文字");
                ui.ControlBackgroundOpacity = 100;
                ShellPrefs.ApplyAppearance(ui);
                Require(surface.Fill is AcrylicBrush { AlwaysUseFallback: true, Opacity: 1, FallbackColor.A: 255 },
                    "主页亚克力100档未回到实底");
                ui.UseAcrylicControls = false;
                ShellPrefs.ApplyAppearance(ui);
            }
            Require(requestCount() == baselineReads, "仅修改首页外观重新请求了图片或媒体");
            VisualStateManager.GoToState(button, "Normal", false);
            await SaveProbeScreenAsync("home-detail-opacity-screen.png");
            ui.ControlBackgroundOpacity = 0;
            ui.UseAcrylicControls = true;
            var homePicture = banner.FrontPicture;
            ui.UseGaussianBackdrop = true;
            ShellPrefs.ApplyAppearance(ui);
            await UntilAsync(() => !banner.GaussianBackdropApplied && banner.GaussianBackdropReady);
            Require(ReferenceEquals(homePicture, banner.FrontPicture), "高斯偏好改动了首页轮播图片");
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(banner);
                await UntilAsync(() => Math.Abs(banner.FrontLayer.Opacity - 1) < 0.001);
                Require(Get<Border>(banner, "Top").Visibility == Visibility.Visible
                    && Get<Border>(banner, "Fade").Visibility == Visibility.Visible
                    && Get<Border>(banner, "RightShade").Visibility == Visibility.Visible,
                    "详情高斯偏好撤掉了主页轮播原遮罩");
                RequireHomeWithoutBottomFade(banner);
                await SaveProbeScreenAsync($"home-gaussian-{theme.Id}-screen.png");
            }
            ui.UseGaussianBackdrop = false;
            ui.UseAcrylicControls = false;
            ShellPrefs.ApplyAppearance(ui);
            await UntilAsync(() => !banner.GaussianBackdropApplied && banner.GaussianBackdropReady);
            Require(Get<Border>(banner, "Fade").Visibility == Visibility.Visible, "主页切回后遮罩没有恢复");
            RequireHomeWithoutBottomFade(banner);
            Require(requestCount() == baselineReads, "主页高斯切换重新请求了图片或媒体");
        }
        finally { _root.Children.Remove(banner); }
    }
}
