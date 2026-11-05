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
            foreach (var (label, choice) in new[]
            {
                ("按钮与选择框背景", "亚克力"), ("背景图效果", "高斯模糊")
            })
            {
                var row = appearance.Rows.OfType<SettingChoiceRow>().Single(item => item.Label == label);
                Require(row.Choices.Count == 2, label + " 没有两项选择");
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
            var toggleRow = appearance.Rows.OfType<SettingToggleRow>().Single(row => row.Label == "固定使用沉浸模式（大屏模式）");
            var toggleHost = Descendants(page).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, toggleRow));
            toggleHost.StartBringIntoView();
            await LayoutAsync(toggleHost);
            var toggle = Descendants(toggleHost).OfType<ToggleSwitch>().Single();
            ((IToggleProvider)new ToggleSwitchAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
            await UntilAsync(() => fixture.Settings.Ui.AlwaysImmersive);
            Require(toggleRow.IsOn, "固定沉浸开关未跟随实际设置");
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
                var sheet = Get<StackPanel>(page, "BodySheet");
                var originalPanels = sheet.Children.OfType<StackPanel>().ToArray();
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
                ResizeProbeClient(1100, 820);
                await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
                await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
                fixture.Settings.Ui.BackdropOpacity = 45;
                fixture.Settings.Ui.AlwaysImmersive = false;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(!page.ViewModel.Showcase, "普通尺寸不应触发沉浸布局");
                var picture = Get<Border>(page, "BackdropPicture");
                Require(Math.Abs(picture.Opacity - 0.945) < 0.001, "背景图增量没有立即落到实际图片层");
                await CheckControlBackgroundOpacityAsync(page, fixture.Settings.Ui);
                fixture.Settings.Ui.AlwaysImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(page.ViewModel.Showcase == (item.Type != EmbyItemType.Episode),
                    "固定沉浸没有按剧/电影及单集适用边界生效");
                if (item.Type != EmbyItemType.Episode)
                    Require(Math.Abs(picture.Opacity - 0.978) < 0.001, "沉浸背景不透明度没有按同一增量计算");
                await CheckGaussianBackdropAsync(page, fixture.Settings.Ui, () => reads);
                ResizeProbeClient(1100, 820);
                await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
                fixture.Settings.Ui.AlwaysImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                foreach (var theme in UiThemes.All)
                {
                    ThemeHost.Apply(theme);
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"detail-customization-{item.Type}-{theme.Id}-screen.png");
                }
                ResizeProbeClient(760, 820);
                await UntilAsync(() => { page.UpdateLayout(); return page.ViewModel.IsCompact; });
                Require(!page.ViewModel.Showcase, "固定沉浸挤掉了窄窗紧凑布局");
                fixture.Settings.Ui.AlwaysImmersive = false;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(!page.ViewModel.Showcase, "关闭固定沉浸没有恢复自动模式");
                fixture.Settings.Ui.BackdropOpacity = 100;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                Require(Math.Abs(picture.Opacity - 1) < 0.001, "背景增量100未完全不透明");
                readings.Add($"{item.Type}：{string.Join(',', expected.Select(card => card.Key))}，隐藏恢复同实例，背景增量45/100，窄窗保留紧凑布局");
            }
            // 季页使用原有顺序；剧与单集的自定义不应连带改掉它。
            ResizeProbeClient(1100, 820);
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, seasons[0])), "季页导航失败");
            var seasonPage = (DetailPage)frame.Content;
            await UntilAsync(() => seasonPage.IsReady);
            RequireCardOrder(seasonPage, new[] { "episodes", "info", "seasons", "cast", "similar" });
            fixture.Settings.Ui.AlwaysImmersive = true;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            await LayoutAsync(seasonPage);
            Require(!seasonPage.ViewModel.Showcase, "没有背景图的季页被强制沉浸");
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
        ui.AlwaysImmersive = false;
        ResizeProbeClient(760, 820);
        await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 760) < 1; });
        await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
        ui.UseGaussianBackdrop = false;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropReady && !page.ViewModel.GaussianBackdropApplied);
        var normal = page.ViewModel.HeroImage;
        var originalPixels = await ReadBackdropPixelsAsync(normal!);
        var baselineReads = requestCount();
        ui.UseGaussianBackdrop = true;
        ui.UseAcrylicControls = true;
        ui.ControlBackgroundOpacity = 0;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady);
        Require(!ReferenceEquals(normal, page.ViewModel.HeroImage), "高斯背景没有渲染新的图片");
        var gaussian = page.ViewModel.HeroImage;
        var blurredPixels = await ReadBackdropPixelsAsync(gaussian!);
        Require(originalPixels.Width == blurredPixels.Width && originalPixels.Height == blurredPixels.Height,
            "高斯改变了图片尺寸");
        var original = PictureVariation(originalPixels.Pixels, originalPixels.Width);
        var blurred = PictureVariation(blurredPixels.Pixels, blurredPixels.Width);
        Require(blurred.Variation < original.Variation * 0.98 && Math.Abs(blurred.Mean - original.Mean) < 2,
            $"高斯没有平滑图像边界或错误压暗：纹理{original.Variation:0.###}→{blurred.Variation:0.###}，均值{original.Mean:0.###}→{blurred.Mean:0.###}");
        foreach (var theme in UiThemes.All)
        {
            ThemeHost.Apply(theme);
            await LayoutAsync(page);
            var play = Descendants(Get<StackPanel>(page, "CompactBlock")).OfType<Button>().First();
            var action = Descendants(play).OfType<ContentPresenter>().Single(part => part.Name == "ContentPresenter").Background;
            Require(action is AcrylicBrush { AlwaysUseFallback: false } acrylic
                && acrylic.TintColor == ThemeHost.ToColor(theme.Colors.SurfaceElevated)
                && acrylic.FallbackColor == ThemeHost.ToColor(theme.Colors.SurfaceElevated), "亚克力没有跟随当前主题");
            Require(ReferenceEquals(gaussian, page.ViewModel.HeroImage), "切主题重新处理高斯图片");
            Require(Get<FrameworkElement>(page, "HeroBand") is Panel { Background: null }
                or Panel { Background: SolidColorBrush { Color.A: 0 } }, "高斯模式仍在头图叠暗色遮罩");
            var tail = Get<StackPanel>(page, "HeroTail");
            Require(tail.Background is null or SolidColorBrush { Color.A: 0 }, "高斯模式尾部仍叠暗色遮罩");
            Require(Get<Border>(page, "TitleWash").Background is null or SolidColorBrush { Color.A: 0 },
                "高斯模式标题栏仍压暗图片");
            var fade = Get<Border>(page, "BackdropFade");
            Require(fade.Height == 40 && fade.Background is LinearGradientBrush, "背景收边渐变没有缩短");
            var target = ThemeHost.ToColor(theme.Colors.Window);
            var stops = ((LinearGradientBrush)fade.Background).GradientStops;
            Require(stops.Count > 1 && stops.All(stop => stop.Color.R == target.R
                    && stop.Color.G == target.G && stop.Color.B == target.B)
                && stops[^1].Color.A == 255, "背景收边渐变没有自然接到主题底色");
            Require(fade.Visibility == Visibility.Visible, "高斯紧凑背景没有显示收边渐变");
            var root = page.XamlRoot.Content;
            var picture = Get<Border>(page, "BackdropPicture");
            var fadeBottom = fade.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, fade.ActualHeight)).Y;
            var pictureBottom = picture.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, picture.ActualHeight)).Y;
            Require(Math.Abs(fadeBottom - pictureBottom) < 1, "收边渐变实际位置未覆盖背景图底部");
            await SaveProbeScreenAsync($"detail-gaussian-{page.ViewModel.ItemType}-{theme.Id}-screen.png");
        }
        if (page.ViewModel.ItemType != EmbyItemType.Episode)
        {
            ResizeProbeClient(1100, 820);
            await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 1100) < 1; });
            ui.AlwaysImmersive = true;
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
            Require(page.ViewModel.Showcase && page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady,
                "固定沉浸没有保留高斯图片");
            var sheet = Get<StackPanel>(page, "BodySheet");
            Require(sheet.Children.OfType<StackPanel>().SelectMany(panel => panel.Children.OfType<ShelfHead>()).All(head => head.Ink),
                "高斯沉浸货架标题缺少字形保护");
            Require(Get<StackPanel>(page, "InfoPanel").Background is SolidColorBrush { Color.A: 255 },
                "高斯沉浸信息表没有保持可读实体卡面");
            await SaveProbeScreenAsync($"detail-gaussian-immersive-{page.ViewModel.ItemType}-screen.png");
            ui.AlwaysImmersive = false;
            ResizeProbeClient(760, 820);
            await UntilAsync(() => { page.UpdateLayout(); return Math.Abs(page.ViewModel.PageWidth - 760) < 1; });
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
        }
        ui.UseGaussianBackdrop = false;
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => !page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady);
        Require(page.ViewModel.HeroImage is not null, "切回遮罩模式丢失背景图");
        Require(Get<Grid>(page, "HeroBand").Background is SolidColorBrush { Color.A: > 0 }, "切回后暗色遮罩未恢复");
        // 快速来回切换：晚到的旧模式不能覆盖当前高斯结果。
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
        ShellPrefs.ApplyAppearance(ui);
        await UntilAsync(() => !page.ViewModel.GaussianBackdropApplied && page.ViewModel.GaussianBackdropReady);
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
                    var baseline = Fill();
                    Require(baseline is AcrylicBrush { AlwaysUseFallback: true }, "实际控件底色不是可调画刷");
                    var color = ((AcrylicBrush)baseline!).FallbackColor;
                    var opacity = baseline!.Opacity;
                    var foreground = control.Foreground;
                    ui.ControlBackgroundOpacity = 37;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(VisualStateManager.GoToState(control, state, false), "外观调整后控件丢失状态：" + state);
                    await LayoutAsync(control);
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: true } adjusted && Math.Abs(adjusted.Opacity * adjusted.FallbackColor.A / 255d
                            - (opacity * color.A / 255d + (1 - opacity * color.A / 255d) * 0.37)) < 0.001,
                        control.Name + "/" + state + " 底色没有从原浓度增加不透明度");
                    Require(control.Opacity == 1 && ReferenceEquals(control.Foreground, foreground),
                        "底色调节改变了整个控件或文字画刷");
                    ui.ControlBackgroundOpacity = 100;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: true } opaque && opaque.FallbackColor.A == 255 && opaque.Opacity == 1,
                        "底色增量100未完全不透明");
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: true } readable && readable.FallbackColor.R < 128
                        && readable.FallbackColor.G < 128 && readable.FallbackColor.B < 128,
                        "不透明底色变成亮白，无法读取浅色文字");
                    var owned = Fill();
                    ui.ControlBackgroundOpacity = 0;
                    ui.UseAcrylicControls = true;
                    ShellPrefs.ApplyAppearance(ui);
                    await LayoutAsync(control);
                    Require(ReferenceEquals(Fill(), owned) && Fill() is AcrylicBrush { AlwaysUseFallback: false } glass
                        && Math.Abs(glass.TintOpacity - 0.55) < 0.001 && glass.FallbackColor.A == 255,
                        control.Name + "/" + state + " 亚克力没有使用固定画刷和主题回退");
                    ui.ControlBackgroundOpacity = 37;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: false } thicker
                        && Math.Abs(thicker.TintOpacity - (0.55 + 0.45 * 0.37)) < 0.001,
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
                }
            }
            finally { VisualStateManager.GoToState(control, restore, false); }

            Brush? Fill() => control is EgPicker
                ? Descendants(control).OfType<Border>().Single(border => border.Name == "Background").Background
                : Descendants(control).OfType<ContentPresenter>().Single(presenter => presenter.Name == "ContentPresenter").Background;
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
                Require(surface.Fill is AcrylicBrush { AlwaysUseFallback: true } adjusted && Math.Abs(adjusted.Opacity * adjusted.FallbackColor.A / 255d
                        - (opacity * color.A / 255d + (1 - opacity * color.A / 255d) * 0.37)) < 0.001,
                    "首页详情按钮底色没有从原浓度增加不透明度");
                Require(button.Opacity == 1 && ReferenceEquals(presenter.Foreground, foreground), "首页详情文字跟着底色变透明");
                foreach (var layer in new[] { Get<Grid>(banner, "LayerA"), Get<Grid>(banner, "LayerB") })
                    Require(layer.Background is ImageBrush picture && Math.Abs(picture.Opacity - 1) < 0.001,
                        "主页原本不透明的背景图变淡，或改动了淡入淡出层");
                ui.ControlBackgroundOpacity = 100;
                ShellPrefs.ApplyAppearance(ui);
                Require(surface.Fill is AcrylicBrush { AlwaysUseFallback: true } opaque && opaque.FallbackColor.A == 255 && opaque.Opacity == 1
                    && opaque.FallbackColor.R < 128 && opaque.FallbackColor.G < 128 && opaque.FallbackColor.B < 128,
                    "主页详情按钮的100档底色没有完全不透明，或亮到看不清文字");
                var owned = surface.Fill;
                ui.ControlBackgroundOpacity = 0;
                ui.UseAcrylicControls = true;
                ShellPrefs.ApplyAppearance(ui);
                await LayoutAsync(banner);
                Require(ReferenceEquals(owned, surface.Fill)
                    && surface.Fill is AcrylicBrush { AlwaysUseFallback: false, Opacity: 1 },
                    "主页详情按钮没有即时切换亚克力");
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
            ui.UseGaussianBackdrop = true;
            ShellPrefs.ApplyAppearance(ui);
            await UntilAsync(() => banner.GaussianBackdropApplied && banner.GaussianBackdropReady);
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(banner);
                await UntilAsync(() => Math.Abs(banner.FrontLayer.Opacity - 1) < 0.001);
                Require(Get<Border>(banner, "Top").Visibility == Visibility.Collapsed
                    && Get<Border>(banner, "Fade").Visibility == Visibility.Collapsed
                    && Get<Border>(banner, "RightShade").Visibility == Visibility.Collapsed,
                    "主页高斯仍保留图片暗色遮罩");
                var bottom = Get<Border>(banner, "BackdropBottomFade");
                var color = ThemeHost.ToColor(theme.Colors.Window);
                Require(bottom.Height == 40 && bottom.Background is LinearGradientBrush gradient
                    && gradient.GradientStops.All(stop => stop.Color.R == color.R && stop.Color.G == color.G && stop.Color.B == color.B)
                    && gradient.GradientStops[^1].Color.A == 255, "主页收边渐变没有缩短或没有衔接主题");
                await SaveProbeScreenAsync($"home-gaussian-{theme.Id}-screen.png");
            }
            ui.UseGaussianBackdrop = false;
            ui.UseAcrylicControls = false;
            ShellPrefs.ApplyAppearance(ui);
            await UntilAsync(() => !banner.GaussianBackdropApplied && banner.GaussianBackdropReady);
            Require(Get<Border>(banner, "Fade").Visibility == Visibility.Visible, "主页切回后遮罩没有恢复");
            Require(requestCount() == baselineReads, "主页高斯切换重新请求了图片或媒体");
        }
        finally { _root.Children.Remove(banner); }
    }
}
