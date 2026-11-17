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
    private async Task WaitDetailPageSizeAsync(DetailPage page, int width, string stage)
    {
        var body = Get<ScrollView>(page, "Body");
        for (var attempt = 0; attempt < 80; attempt++)
        {
            page.UpdateLayout();
            var clientWidth = _window!.ClientSize.Width / _root.XamlRoot.RasterizationScale;
            if (StoreDetailProbeEvidence.PageSizeMatches(width, clientWidth, page.ActualWidth, body.ActualWidth, page.ViewModel.PageWidth))
                return;
            await Task.Delay(25);
        }
        var content = Get<Grid>(page, "StoreScrollContent");
        throw new TimeoutException($"{stage}：目标{width}，客户区{_window!.ClientSize.Width}，页{page.ActualWidth:0.##}，"
            + $"Body{body.ActualWidth:0.##}/视口{body.ViewportWidth:0.##}，VM{page.ViewModel.PageWidth:0.##}，"
            + $"内容{content.ActualWidth:0.##}/Width{content.Width:0.##}，商店{page.ViewModel.StoreMode}，缩放{_root.XamlRoot.RasterizationScale:0.##}");
    }

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
                    // 集页 2026-11-09 起没有「更多类似」（用户令），设置页那一行只剩 集列表／其他信息／演职人员。
                    ("单集页面卡片", EmbyItemType.Episode, 3),
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

            // 「详情页内容」那张新卡（用户令 2026-11-09）：演职人员开关默认关，拨一下写回设置、再拨回来。
            var contentCard = page.ViewModel.Sections.Single(section => section.Title == "详情页内容");
            var castRow = contentCard.Rows.OfType<SettingToggleRow>().Single(row => row.Label == "隐藏没有封面的演职人员");
            var castHost = Descendants(page).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, castRow));
            castHost.StartBringIntoView();
            await LayoutAsync(castHost);
            var castToggle = Descendants(castHost).OfType<ToggleSwitch>().Single();
            Require(!castToggle.IsOn && !fixture.Settings.Ui.HideCastWithoutPortrait, "隐藏没有封面的演职人员不是默认关");
            castToggle.IsOn = true;
            await UntilAsync(() => fixture.Settings.Ui.HideCastWithoutPortrait);
            castToggle.IsOn = false;
            await UntilAsync(() => !fixture.Settings.Ui.HideCastWithoutPortrait);
            readings.Add("详情页内容：隐藏没有封面的演职人员默认关，开与关都写回设置");

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
            // 2026-11-08 出厂材质改成亚克力（「把当前的系统设置设为默认」）：先切回半透明叠层，才量得到
            // 「底色不透明度」那格的精确默认值；这一下切换本身也是下面「两种材质各自只有一套参数」的入口。
            Require(fixture.Settings.Ui.UseAcrylicControls, "出厂材质不是亚克力");
            await Choose("按钮与选择框背景", "半透明叠层");
            await UntilAsync(() => appearance.Rows.OfType<SettingSliderRow>()
                .Any(row => row.Label == "按钮与选择框底色不透明度（%）"));
            var defaultRow = appearance.Rows.OfType<SettingSliderRow>()
                .Single(row => row.Label == "按钮与选择框底色不透明度（%）");
            var defaultHost = Descendants(page).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, defaultRow));
            defaultHost.StartBringIntoView();
            await LayoutAsync(defaultHost);
            Require(Math.Abs(defaultRow.Value - UiSettings.DefaultControlBackgroundOpacity) < 0.000001
                && Math.Abs(fixture.Settings.Ui.ControlBackgroundOpacity - UiSettings.DefaultControlBackgroundOpacity) < 0.000001,
                "打开设置把精确默认不透明度改成了吸附值");

            // 按钮：两种材质各自只有一套参数。切到亚克力时三行染色参数当场出现、叠层那格收起；
            // 切回叠层时反过来（2026-11-07 用户令「三项可调」）。
            await Choose("按钮与选择框背景", "亚克力");
            await UntilAsync(() => appearance.Rows.OfType<SettingSliderRow>()
                .Any(row => row.Label == "按钮与选择框染色不透明度（%）"));
            Require(fixture.Settings.Ui.UseAcrylicControls, "材质选择没有写回设置");
            Require(appearance.Rows.OfType<SettingSliderRow>().All(row => row.Label != "按钮与选择框底色不透明度（%）"),
                "亚克力模式仍留着半透明叠层的浓度行");
            await Set("按钮与选择框染色不透明度（%）", 37);
            await Set("按钮与选择框明暗不透明度（%）", 45);
            Require(fixture.Settings.Ui.ControlTintOpacity == 37 && fixture.Settings.Ui.ControlTintLuminosityOpacity == 45,
                "按钮染色参数没有写回设置");
            await SaveProbeScreenAsync("settings-acrylic-controls-screen.png");
            await Choose("按钮与选择框背景", "半透明叠层");
            await UntilAsync(() => appearance.Rows.OfType<SettingSliderRow>()
                .Any(row => row.Label == "按钮与选择框底色不透明度（%）"));
            Require(!fixture.Settings.Ui.UseAcrylicControls
                && appearance.Rows.OfType<SettingSliderRow>().All(row => row.Label != "按钮与选择框染色不透明度（%）"),
                "切回半透明叠层没有恢复底色浓度行");
            await Set("按钮与选择框底色不透明度（%）", 37);
            Require(fixture.Settings.Ui.ControlBackgroundOpacity == 37, "底色浓度滑块没有写回设置");

            // 背景：效果选择与遮罩颜色退役，只剩色调与两档浓度。
            Require(appearance.Rows.OfType<SettingChoiceRow>().All(row => row.Label != "背景图效果")
                && appearance.Rows.OfType<SettingColorRow>().Any(row => row.Label == "背景亚克力色调")
                && appearance.Rows.OfType<SettingSliderRow>().All(row => row.Label != "背景图不透明度（%）"),
                "背景仍然留着效果选择、遮罩颜色或浓度行");
            await Set("背景染色不透明度（%）", 42);
            await Set("背景明暗不透明度（%）", 51);
            Require(fixture.Settings.Ui.BackdropTintOpacity == 42 && fixture.Settings.Ui.BackdropTintLuminosityOpacity == 51,
                "背景染色参数没有写回设置");
            // 显示模式排在最前（2026-11-11 用户令），商店模式把按钮与背景两组整块收起，只留它自己。
            // 行是整组重建的：切完重新从 appearance.Rows 里拿那一行，不拿切换前那个实例。
            var modeRow = appearance.Rows.OfType<SettingChoiceRow>().Single(row => row.Label == "显示模式");
            Require(ReferenceEquals(appearance.Rows[0], modeRow), "显示模式没有排在详情页外观的第一行");
            modeRow.Selected = modeRow.Choices.Single(choice => choice.Label == "商店模式");
            Require(appearance.Rows.Count == 1 && appearance.Rows[0].Label == "显示模式"
                && appearance.Rows[0] is SettingChoiceRow { Selected.Label: "商店模式" },
                "商店模式没有收起按钮与背景两组：" + string.Join('|', appearance.Rows.Select(row => row.Label)));
            Require(appearance.Rows.All(row => row.Label is not ("按钮与选择框背景" or "按钮与选择框颜色"
                or "按钮与选择框底色不透明度（%）" or "背景亚克力色调" or "背景染色不透明度（%）" or "背景明暗不透明度（%）")),
                "商店模式仍留着按钮或背景的设置行");
            // 截图取景对着卡头：只 bring 行的话卡片标题会被卷到视口上面，看不出这一行属于哪张卡。
            var appearanceTitle = Descendants(page).OfType<TextBlock>().First(item => item.Text == "详情页外观");
            appearanceTitle.StartBringIntoView();
            await LayoutAsync(appearanceTitle);
            await SaveProbeScreenAsync("settings-detail-appearance-plain-screen.png");
            var restoredMode = appearance.Rows.OfType<SettingChoiceRow>().Single(row => row.Label == "显示模式");
            restoredMode.Selected = restoredMode.Choices.Single(choice => choice.Label == "沉浸模式");
            await UntilAsync(() => fixture.Settings.Ui.EnableImmersive);
            Require(appearance.Rows.OfType<SettingColorRow>().Any(row => row.Label == "背景亚克力色调")
                && appearance.Rows.OfType<SettingColorRow>().Any(row => row.Label == "按钮与选择框颜色")
                && fixture.Settings.Ui.EnableImmersive, "显示模式选择没有同步沉浸状态或恢复按钮与背景行");
            Require(appearance.Rows[0].Label == "显示模式", "切回沉浸模式后显示模式没有留在第一行");
            var immersiveHost = Descendants(page).OfType<ContentPresenter>().First(item => ReferenceEquals(item.Content, appearance.Rows[0]));
            immersiveHost.StartBringIntoView();
            await LayoutAsync(immersiveHost);
            appearanceTitle.StartBringIntoView();
            await LayoutAsync(appearanceTitle);
            await SaveProbeScreenAsync("settings-detail-appearance-immersive-screen.png");
            Require(fixture.Actions.Plays == 0, "设置自定义探针意外请求播放");

            async Task Choose(string label, string choice)
            {
                var row = appearance.Rows.OfType<SettingChoiceRow>().Single(item => item.Label == label);
                var host = Descendants(page).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, row));
                host.StartBringIntoView();
                await LayoutAsync(host);
                Descendants(host).OfType<ComboBox>().Single().SelectedItem = row.Choices.Single(item => item.Label == choice);
                await UntilAsync(() => row.Selected?.Label == choice);
            }

            async Task Set(string label, double value)
            {
                var row = appearance.Rows.OfType<SettingSliderRow>().Single(item => item.Label == label);
                var host = Descendants(page).OfType<ContentPresenter>().Single(item => ReferenceEquals(item.Content, row));
                host.StartBringIntoView();
                await LayoutAsync(host);
                Descendants(host).OfType<Slider>().Single().Value = value;
                await UntilAsync(() => Math.Abs(row.Value - value) < 0.01);
            }

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
    private async Task DetailCustomizationPagesAsync(string itemType)
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
            foreach (var item in DetailCustomizationProbeCases.Select([series, episode, movie], itemType))
            {
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item)), "自定义详情导航失败");
                var page = (DetailPage)frame.Content;
                page.AttachWindow(_window);
                await UntilAsync(() => page.IsReady && page.ViewModel.HeroImage is not null);
                await LayoutAsync(page);
                var readingNames = new[] { "ScoreText", "CriticScoreText", "HeroFacts", "Overview", "DetailVideoText", "GenreLine" };
                try
                {
                    await UntilAsync(() => DetailCustomizationProbeCases.Ready(Get<bool>(page, "_readingQueued"),
                        Get<bool>(page, "_appearanceObserved"), page.ViewModel.HeroImage is not null,
                        readingNames.Select(name => Get<TextBlock>(page, name))
                            .Where(node => node.Visibility == Visibility.Visible && node.ActualWidth > 0 && node.ActualHeight > 0)
                            .Select(node =>
                            {
                                var expected = DefaultDetailForeground();
                                return node.Foreground is SolidColorBrush brush && brush.Color == expected;
                            })));
                }
                catch (TimeoutException)
                {
                    File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "detail-reading-ready.txt"),
                        $"{itemType}: queued={Get<bool>(page, "_readingQueued")}, observed={Get<bool>(page, "_appearanceObserved")}, imageReady={page.ViewModel.HeroImage is not null}\n"
                        + string.Join("\n", readingNames.Select(name =>
                        {
                            var node = Get<TextBlock>(page, name);
                            return $"{name}: actual={(node.Foreground as SolidColorBrush)?.Color}, background={Call(page, "ReadingBackground", node)}, {node.ActualWidth:0.###}x{node.ActualHeight:0.###}";
                        })) + "\n");
                    throw;
                }
                foreach (var name in readingNames)
                {
                    var node = Get<TextBlock>(page, name);
                    if (node.Visibility != Visibility.Visible || node.ActualWidth <= 0 || node.ActualHeight <= 0) continue;
                    var expectedInk = DefaultDetailForeground();
                    Require(node.Foreground is SolidColorBrush text && text.Color == expectedInk,
                        name + $" 没有恢复默认文字颜色：实际{(node.Foreground as SolidColorBrush)?.Color}，预期{expectedInk}");
                }
                var sheet = Get<StackPanel>(page, "BodySheet");
                var originalPanels = sheet.Children.OfType<StackPanel>().ToArray();
                CheckDetailPanelMaterials(page, "初次呈现");
                var materialSource = Get<Border>(page, "CardMaterialSource");
                var originalMaterial = materialSource.Background;
                var privateMaterial = Get<AcrylicBrush>(page, "_panelFill");
                try
                {
                    materialSource.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
                    Require(privateMaterial.AlwaysUseFallback && privateMaterial.FallbackColor == Microsoft.UI.Colors.White,
                        "卡片没有随系统资源切换到实色");
                    RequireDetailPanelSource(privateMaterial, materialSource.Background);
                    // 信息卡自己那一支也跟着系统资源走 —— 2026-11-15 起它是沉浸档唯一还铺卡面的正文板。
                    var infoMaterial = Get<AcrylicBrush>(page, "_infoCardFill");
                    Require(infoMaterial.AlwaysUseFallback && infoMaterial.FallbackColor == Microsoft.UI.Colors.White,
                        "信息卡的卡片底没有随系统资源切换到实色");
                    RequireDetailPanelMaterials(page, "系统实色回退");
                }
                finally { materialSource.Background = originalMaterial; }
                RequireDetailPanelSource(privateMaterial, originalMaterial);
                RequireDetailPanelMaterials(page, "系统资源恢复");
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

                // 「隐藏没有封面的演职人员」（用户令 2026-11-09）：夹具里一个带头像、一个没有。开着时那一排只剩
                // 有头像的那位，关掉后两个人回来 —— 当场生效，不用重进页面。
                var cast = page.ViewModel.CastShelf!;
                var castPanel = Get<StackPanel>(page, "CastPanel");
                Require(cast.Cards.Count == 2 && cast.Cards.Select(card => card.Item.Id).SequenceEqual(["person-1", "person-2"]),
                    $"{item.Type} 演职人员不是夹具里那两个人：{string.Join(',', cast.Cards.Select(card => card.Item.Id))}");
                fixture.Settings.Ui.HideCastWithoutPortrait = true;
                ShellPrefs.ApplyDetailCast(fixture.Settings.Ui);
                await UntilAsync(() => cast.Cards.Count == 1);
                Require(castPanel.Visibility == Visibility.Visible && castPanel.ActualHeight > 0,
                    $"{item.Type} 只剩一张有头像的脸时演职人员卡被收起");
                Require(cast.Cards[0].Item.Id == "person-1" && cast.Cards[0].Item.PrimaryImageTag == "fixture",
                    $"{item.Type} 隐藏没有封面时留下了没有头像的人：{cast.Cards[0].Item.Id}");
                fixture.Settings.Ui.HideCastWithoutPortrait = false;
                ShellPrefs.ApplyDetailCast(fixture.Settings.Ui);
                await UntilAsync(() => cast.Cards.Count == 2);
                Require(castPanel.Visibility == Visibility.Visible, $"{item.Type} 关掉隐藏后演职人员卡没有回来");

                foreach (var width in new[] { 1100, 760 })
                {
                    await ResizeResponsiveAsync(page, width, 820);
                    await WaitDetailPageSizeAsync(page, width, "自定义卡片缩放");
                    await LayoutAsync(page);
                    CheckDetailPanelMaterials(page, "自定义卡片缩放");
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
                await ResizeResponsiveAsync(page, 1100, 1120);
                await WaitDetailPageSizeAsync(page, 1100, "背景染色前");
                await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
                // 背景固定亚克力：三项参数直接落到那支画笔上，不再有浓度档位，也不再另跑一遍 CPU 高斯。
                fixture.Settings.Ui.EnableImmersive = true;
                fixture.Settings.Ui.BackdropTintOpacity = 45;
                fixture.Settings.Ui.BackdropTintLuminosityOpacity = 45;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(page.ViewModel.Showcase && page.ViewModel.BackdropShown && !page.ViewModel.HeroCardShown,
                    "开启时剧、电影或单集宽窗没有进入沉浸布局");
                RequireImmersiveSurface(page);
                var picture = Get<Border>(page, "BackdropPicture");
                var masked = page.ViewModel.BackdropShown;
                if (masked)
                {
                    RequireBackdropAcrylic(page, 0.45, 0.45);
                    Require(picture.Opacity == 1, "背景图本身被调了透明度（该调的是图上那层材料）");
                }
                await CheckControlBackgroundOpacityAsync(page, fixture.Settings.Ui);
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(page.ViewModel.Showcase, "开启时有图宽窗没有保持沉浸布局");
                if (masked) RequireBackdropAcrylic(page, 0.45, 0.45);
                var band = Get<Grid>(page, "HeroBand");
                var heroHeight = band.ActualHeight;
                // 0 档是「这块材料什么都不盖」，不是「把图收走」：画面照旧站在那儿。
                fixture.Settings.Ui.BackdropTintOpacity = 0;
                fixture.Settings.Ui.BackdropTintLuminosityOpacity = 0;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                if (masked)
                {
                    RequireBackdropAcrylic(page, 0, 0);
                    Require(picture.Opacity == 1, "0 档把背景图本身调透明了");
                }
                Require(Math.Abs(band.ActualHeight - heroHeight) < 0.5, "改动染色参数改变了头图那一格的高");
                await SaveProbeScreenAsync($"detail-customization-{item.Type}-backdrop-clear.png");
                fixture.Settings.Ui.BackdropTintOpacity = 100;
                fixture.Settings.Ui.BackdropTintLuminosityOpacity = 100;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                if (masked) RequireBackdropAcrylic(page, 1, 1);
                await SaveProbeScreenAsync($"detail-customization-{item.Type}-backdrop-solid.png");
                fixture.Settings.Ui.BackdropTintOpacity = 45;
                fixture.Settings.Ui.BackdropTintLuminosityOpacity = 45;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                // 染色参数只重画材质，不重新请求图片：三条滑块来回拖也不该多走一次网络。
                Require(reads > 0 && page.ViewModel.HeroImage is not null, "染色参数改动丢了背景图");
                await ResizeResponsiveAsync(page, 1100, 1120);
                await WaitDetailPageSizeAsync(page, 1100, "切换主题前");
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                foreach (var theme in UiThemes.All)
                {
                    ThemeHost.Apply(theme);
                    await LayoutAsync(page);
                    Require(readingNames.Select(name => Get<TextBlock>(page, name))
                        .Where(node => node.Visibility == Visibility.Visible)
                        .All(node => node.Foreground is SolidColorBrush ink && ink.Color == DefaultDetailForeground()),
                        $"{item.Type}/{theme.Id} 切主题后文字没有保持默认角色颜色");
                    await SaveProbeScreenAsync($"detail-customization-{item.Type}-{theme.Id}-screen.png");
                    if (item.Type == EmbyItemType.Movie)
                        await CheckDetailMenuAcrylicAsync(page, fixture, theme.Id);
                }
                await ResizeResponsiveAsync(page, 760, 820);
                await WaitDetailPageSizeAsync(page, 760, "窄窗自定义检查");
                Require(!page.ViewModel.Showcase, "沉浸模式挤掉了窄窗紧凑布局");
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(!page.ViewModel.Showcase && page.ViewModel.IsCompact && page.ViewModel.BackdropShown, "开启时窄窗未保持紧凑排版与背景图");
                // 2026-11-08 用户令「窄窗口的背景图不要加亚克力」：这一档只有固定 40% 实色罩子，四类页面一样。
                Require(Get<Border>(page, "BackdropFade").Visibility == Visibility.Collapsed,
                    "窄窗背景图上仍画着亚克力层");
                if (masked)
                    Require(ScrimAlphaOf(page) == AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, AppearanceOpacity.ReadingVeil),
                        "窄窗背景图的实色阅读罩浓度不对");
                await CheckImmersiveSwitchAsync(page, fixture.Settings.Ui);
                readings.Add($"{item.Type}：{string.Join(',', expected.Select(card => card.Key))}，隐藏恢复同实例，背景染色0/45/100，窄窗保留紧凑布局");
            }
            if (DetailCustomizationProbeCases.IncludesShared(itemType))
            {
                // 季页使用原有顺序；剧与单集的自定义不应连带改掉它。
                ResizeProbeClient(1100, 1120);
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, seasons[0])), "季页导航失败");
                var seasonPage = (DetailPage)frame.Content;
                await UntilAsync(() => seasonPage.IsReady);
                await ResizeResponsiveAsync(seasonPage, 1100, 1120);
                await WaitDetailPageSizeAsync(seasonPage, 1100, "季页自定义检查");
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
            }
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
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, $"detail-customization-pages-{itemType}.txt"), readings);
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
                await ResizeResponsiveAsync(page, width, 900);
                await WaitDetailPageSizeAsync(page, width, "调整客户区后");
                ui.EnableImmersive = false;
                ShellPrefs.ApplyAppearance(ui);
                await LayoutAsync(page);
                var card = Get<Border>(page, "HeroCardTop");
                var wide = width == 1200;
                const bool oldEpisodeBackdrop = false;
                Require(page.ViewModel.BackdropShown == oldEpisodeBackdrop && !page.ViewModel.Showcase
                    && Get<Grid>(page, "Backdrop").Visibility == (oldEpisodeBackdrop ? Visibility.Visible : Visibility.Collapsed),
                    "关闭沉浸后背景未恢复该类型原有宽窄规则");
                var expectedCard = subject?.Type != EmbyItemType.Episode || wide;
                Require(page.ViewModel.HeroCardShown == expectedCard && card.Visibility == Visibility.Collapsed,
                    "商店模式顶部仍显示大卡框或改变文件选项布局");
                // 沉浸关掉之后卡片带回到主题那套墨、信息卡回到正文面板共享的那支画刷。
                RequireCardInk(page, onScrim: false);
                RequireStoreDetailCards(page, ["EpisodePanel", "InfoPanel", "SeasonPanel", "CastPanel", "SimilarPanel"]);
                CheckDetailPanelMaterials(page, "关闭沉浸");
                Require(Get<StackPanel>(page, "ColumnPickers").Visibility == page.ViewModel.ColumnPickersVisibility,
                    "同页切换后视频信息父容器没有同步显隐");
                RequireDisabledStoreTail(page);
                Require(ReferenceEquals(target, page.ViewModel.PlayTarget), "外观切换改变播放目标");
                Require(page.ViewModel.Title == title && page.ViewModel.Subline == subline
                    && ReferenceEquals(subject, page.ViewModel.CurrentItem), "外观切换改变详情主体或标题");
                if (wide) await CheckSimpleControlMaterialAsync(page, ui);
                await ScrollProbeAsync(Get<ScrollView>(page, "Body"), 0);
                await WaitDetailPageSizeAsync(page, width, "关闭沉浸截图前");
                await SaveProbeScreenAsync($"detail-immersive-disabled-{page.ViewModel.ItemType}-{width}-screen.png");
                ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(ui);
                // 不能仅凭VM上一拍的宽度放行，实际客户区、页面和ScrollView必须同时匹配。
                await WaitDetailPageSizeAsync(page, width, "开启沉浸后");
                await LayoutAsync(page);
                Require(page.ViewModel.HeroCardShown == (wide && subject?.Type == EmbyItemType.Episode && !page.ViewModel.BackdropShown),
                    "重新启用后有图沉浸或无图卡面没有恢复");
                Require(page.ViewModel.Showcase == (wide && page.ViewModel.BackdropShown)
                    && page.ViewModel.IsCompact == !wide,
                    $"重新启用沉浸后未恢复宽窗沉浸或窄窗紧凑布局：目标{width}、实际{page.ViewModel.PageWidth:0.##}、"
                        + $"沉浸{page.ViewModel.Showcase}、背景{page.ViewModel.BackdropShown}、紧凑{page.ViewModel.IsCompact}");
                Require((Get<Grid>(page, "Backdrop").Visibility == Visibility.Visible) == page.ViewModel.BackdropShown,
                    "重新启用后背景显隐未跟随视图模型");
                Require(panels.SequenceEqual(Get<StackPanel>(page, "BodySheet").Children), "外观切换重建或移动了正文实例");
                if (page.ViewModel.Showcase) RequireImmersiveSurface(page);
                CheckDetailPanelMaterials(page, "重新启用沉浸");
                if (subject?.Type == EmbyItemType.Episode) RequireEpisodeSubject(page, subject);
                await WaitDetailPageSizeAsync(page, width, "开启沉浸截图前");
                await SaveProbeScreenAsync($"detail-immersive-enabled-{page.ViewModel.ItemType}-{width}-screen.png");
            }
        }
        finally
        {
            ui.EnableImmersive = enabled;
            ShellPrefs.ApplyAppearance(ui);
        }
    }

    private static void RequireDetailPanelSource(AcrylicBrush actual, Brush source)
    {
        var matches = source switch
        {
            AcrylicBrush expected => actual.AlwaysUseFallback == expected.AlwaysUseFallback
                && actual.TintColor == expected.TintColor && actual.FallbackColor == expected.FallbackColor
                && actual.TintOpacity == expected.TintOpacity
                && actual.TintLuminosityOpacity == expected.TintLuminosityOpacity,
            SolidColorBrush expected => actual.AlwaysUseFallback && actual.FallbackColor == expected.Color,
            _ => false
        };
        Require(matches, "卡片资源恢复后未同步原画刷的材质参数");
    }

    // 正式三种表面分别验收：简洁实色、沉浸信息卡自带卡片底、普通纸面共享材料。
    // 不以亚克力对象存在代替当前模式的实测背景归属。
    private static void RequireDetailPanelMaterials(DetailPage page, string stage)
    {
        var panels = Get<StackPanel>(page, "BodySheet").Children.OfType<StackPanel>().ToArray();
        var fill = Get<AcrylicBrush>(page, "_panelFill");
        var line = Get<SolidColorBrush>(page, "_panelLine");
        var infoFill = Get<AcrylicBrush>(page, "_infoCardFill");
        var store = Get<Border>(page, "StoreCardMaterialSource").Background;
        foreach (var panel in panels)
        {
            // 2026-11-15 用户令「给沉浸模式下的更多信息卡片加背景卡片」：沉浸档只有信息卡还带卡面，
            // 用的是它自己那支 _infoCardFill（其余四块共用的那支在这一档是 0）。
            var expected = page.ViewModel.StoreMode ? store
                : page.ViewModel.Showcase && panel.Name == "InfoPanel" ? infoFill : fill;
            Require(ReferenceEquals(panel.Background, expected),
                $"{stage}/{page.ViewModel.ItemType}/{panel.Name} 背景归属不符：简洁{page.ViewModel.StoreMode}、"
                    + $"沉浸{page.ViewModel.Showcase}、紧凑{page.ViewModel.IsCompact}、宽{page.ActualWidth:0.##}/VM{page.ViewModel.PageWidth:0.##}、"
                    + $"实际{panel.Background?.GetType().Name ?? "null"}、预期{expected?.GetType().Name ?? "null"}");
        }
        if (page.ViewModel.StoreMode)
        {
            Require(store is SolidColorBrush { Color.A: 255, Opacity: 1 }, "简洁卡片没有不透明实色源");
            RequireStoreDetailCards(page, panels.Select(panel => panel.Name).ToArray());
        }
        else
        {
            var opacity = page.ViewModel.Showcase ? 0 : 1;
            Require(fill.Opacity == opacity && line.Opacity == opacity,
                $"{stage} 背景或描边未跟随模式：实际{fill.Opacity}/{line.Opacity}、预期{opacity}");
            if (page.ViewModel.Showcase) RequireImmersiveSurface(page);
        }
    }

    private static void CheckDetailPanelMaterials(DetailPage page, string stage)
    {
        RequireDetailPanelMaterials(page, stage);
        var info = Get<StackPanel>(page, "InfoPanel");
        var original = info.Background;
        try
        {
            info.Background = original is null ? new SolidColorBrush(Microsoft.UI.Colors.White) : null;
            var rejected = false;
            try { RequireDetailPanelMaterials(page, stage + "错误背景反例"); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "详情卡片材料检查没有拒绝错误信息卡背景");
        }
        finally { info.Background = original; }
        RequireDetailPanelMaterials(page, stage + "反例恢复");
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
        // 2026-11-15 用户令「给沉浸模式下的更多信息卡片加背景卡片」：这一块重新铺上卡片底
        // （2026-11-14 曾按「改为去掉其他信息的背景卡片」整块让开；其余四块正文板仍让开）。
        // 卡面垫在底下之后，表里的字照旧吃压图那套浅墨 —— 那支比主题暗字亮，压在深色卡上读得出来。
        var info = Get<StackPanel>(page, "InfoPanel");
        Require(ReferenceEquals(info.Background, Get<AcrylicBrush>(page, "_infoCardFill"))
            && info.Background is AcrylicBrush { Opacity: 1 },
            "沉浸信息卡没有铺自己那块卡片底，或那块底被淡掉了");
        RequireCardInk(page, onScrim: true);
        Require(!page.ViewModel.ShowShelfLines && page.ViewModel.TailMinHeight == 0
            && Get<Border>(page, "CardTailGap").ActualHeight < 0.01,
            "沉浸仍保留货架线条或卡面收尾留白");
    }

    /// <summary>
    /// 正文那四条卡片带的副行现在用的是哪一套墨。沉浸档里它们没有纸面接着，站的正是背景图，
    /// 所以必须是压在图上那支浅灰白；纸面档回到主题自己的暗字。卡片是回收着建的，另抓已建出来的那张读颜色。
    /// 一张卡都没建出来的页面（没有演职人员、没有相似条目的电影）能核的只有带自己那一档。
    /// </summary>
    private static void RequireCardInk(DetailPage page, bool onScrim)
    {
        var expected = ((SolidColorBrush)Application.Current.Resources[
            onScrim ? "EgOnScrimDimBrush" : "EgTextDimBrush"]).Color;
        foreach (var name in new[] { "EpisodeStrip", "SeasonRow", "CastRow", "SimilarRow" })
        {
            var strip = Get<ShelfStrip>(page, name);
            Require(strip.OnScrim == onScrim,
                (onScrim ? "沉浸卡片带没有换成压图浅墨：" : "纸面卡片带仍用压图浅墨：") + name);
            var repeater = Get<ItemsRepeater>(strip, "Repeater");
            // ItemsRepeater 的回收池仍留在可视子树；只读当前已实现的容器，避免把屏外旧副本当成当前文字。
            for (var index = 0; index < (repeater.ItemsSourceView?.Count ?? 0); index++)
            {
                if (repeater.TryGetElement(index) is not DependencyObject container) continue;
                foreach (var card in CardsIn(container).Where(card => card.Visibility == Visibility.Visible && card.ActualWidth > 0 && card.ActualHeight > 0))
                {
                    Require(card.OnScrim == onScrim, $"{name} 当前第 {index} 张卡没有同步文字模式");
                    var subline = Get<TextBlock>(card, "SubLine");
                    if (subline.Visibility != Visibility.Visible) continue;
                    Require(subline.Foreground is SolidColorBrush ink && ink.Color == expected,
                        $"{name} 副行颜色是{(subline.Foreground as SolidColorBrush)?.Color}，该是{expected}");
                }
            }
        }
    }

    private static IEnumerable<PosterCard> CardsIn(DependencyObject node)
    {
        if (node is PosterCard own) { yield return own; yield break; }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            if (child is PosterCard card) yield return card;
            foreach (var nested in CardsIn(child)) yield return nested;
        }
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

    /// <summary>
    /// 背景那层亚克力的三项参数是不是照设置落到了画笔上 —— 探针比的是画笔自己的读数，不另算一遍公式。
    /// </summary>
    private static void RequireBackdropAcrylic(DetailPage page, double tintOpacity, double luminosityOpacity)
    {
        var fade = Get<Border>(page, "BackdropFade");
        var ink = ShellPrefs.AppearanceColor(true, page.ViewModel.AppearanceSettings);
        var tint = ThemeHost.ToColor(ink);
        tint.A = 255;
        Require(fade.Background is AcrylicBrush acrylic
            && fade.Visibility == Visibility.Visible
            && acrylic.TintColor == tint
            && acrylic.FallbackColor == tint
            && Math.Abs(acrylic.TintOpacity - tintOpacity) < 0.001
            && acrylic.TintLuminosityOpacity is { } luminosity
            && Math.Abs(luminosity - luminosityOpacity) < 0.001
            && acrylic.Opacity == 1
            && !acrylic.AlwaysUseFallback,
            $"背景亚克力三项参数不对：{fade.Background}、tint={tintOpacity:0.###}/{luminosityOpacity:0.###}、"
            + $"可见={fade.Visibility}");
    }

    private static Windows.UI.Color DefaultDetailForeground(string key = "EgOnScrimBrush") =>
        new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : ((SolidColorBrush)Application.Current.Resources[key]).Color;

    /// <summary>「连播 / 已观看」那支固定的绿（#52B54B）；高对比度让给系统前景。</summary>
    private static Windows.UI.Color ActiveDetailInk() =>
        new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : Windows.UI.Color.FromArgb(0xFF, 0x52, 0xB5, 0x4B);

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
        // 出厂材质 2026-11-08 19:26 起是亚克力（a9e2418「把当前的系统设置设为默认」）。这一段前半量的是
        // 半透明叠层的浓度读数，所以显式切到叠层，不靠上一个检查留下的状态；亚克力那一支在循环后半段
        // 自己切过去（0 与 100 两档是同一块材料，滑块只管叠层）。library 分区自那次出厂值改动之后没跑过，
        // 这条前提到 2026-11-08 21:10 才重新被量到 —— 红的是检查的前提，不是产品。
        ui.UseAcrylicControls = false;
        ShellPrefs.ApplyAppearance(ui);
        // 半透明叠层那一路的底色是**用户选的按钮色**（a9e2418 起 Tint 由调用方传入，出厂 #808080），
        // 不是从前那支白。下面「薄白零档」和「不透明底色」两句按这一支色比，不再写死白色。
        var ink = ShellPrefs.AppearanceColor(false, ui);
        var expectedInk = Windows.UI.Color.FromArgb(255, ink.R, ink.G, ink.B);
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
                    Require(zero is AcrylicBrush { AlwaysUseFallback: true },
                        "实际控件底色不是可调画刷：" + (control.Name.Length > 0 ? control.Name : control.GetType().Name)
                            + "/" + state + " 拿到 " + (zero is null ? "null" : zero.GetType().Name));
                    var color = ((AcrylicBrush)zero!).FallbackColor;
                    var opacity = zero!.Opacity;
                    var baseline = opacity * color.A / 255d;
                    var feedback = state is "PointerOver" or "Pressed" or "CheckedPointerOver" or "CheckedPressed";
                    if (controls.Contains(control) || control is EgPicker)
                    {
                        var expectedOpacity = state.Contains("Pressed", StringComparison.Ordinal) ? 8d / 255 : feedback ? 21d / 255 : 0;
                        Require(color == expectedInk && Math.Abs(opacity - expectedOpacity) < 0.001,
                            control.Name + "/" + state + " 薄白零档或反馈不正确："
                                + $"色 {color}（该 {expectedInk}）、不透明度 {opacity:0.###}（该 {expectedOpacity:0.###}）");
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
                    // 100 档的不透明底色就是用户选的按钮色本身（从前写死「按钮浅、其余深」两档，那是白底时代的分法）。
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: true, FallbackColor: var opaqueInk }
                            && opaqueInk == expectedInk,
                        control.Name + "/" + state + " 100 档不透明底色不是用户选的按钮色："
                            + (Fill() is AcrylicBrush brush ? brush.FallbackColor.ToString() : "非亚克力"));
                    var owned = Fill();
                    ui.ControlBackgroundOpacity = 0;
                    ui.UseAcrylicControls = true;
                    ShellPrefs.ApplyAppearance(ui);
                    await LayoutAsync(control);
                    var tint = ui.ControlTintOpacity / 100 + (state is "PointerOver" or "CheckedPointerOver" ? 0.06
                        : state is "Pressed" or "CheckedPressed" ? 0.12 : 0);
                    Require(ReferenceEquals(Fill(), owned) && Fill() is AcrylicBrush glass
                        && !glass.AlwaysUseFallback && glass.Opacity == 1
                        && Math.Abs(glass.TintOpacity - tint) < 0.001
                        && glass.TintLuminosityOpacity is { } luminosity
                        && Math.Abs(luminosity - ui.ControlTintLuminosityOpacity / 100) < 0.001
                        && glass.FallbackColor.A == 255,
                        control.Name + "/" + state + " 亚克力的三项参数或回退不对");
                    // 「整体浓度」已经退役：亚克力在 0 与 100 两档是同一块材料，滑块只管半透明叠层那一条路。
                    ui.ControlBackgroundOpacity = 37;
                    ShellPrefs.ApplyAppearance(ui);
                    Require(Fill() is AcrylicBrush sameTint && Math.Abs(sameTint.TintOpacity - tint) < 0.000001,
                        "亚克力仍被已退役的浓度滑块影响");
                    ui.ControlBackgroundOpacity = 100;
                    ShellPrefs.ApplyAppearance(ui);
                    await LayoutAsync(control);
                    Require(Fill() is AcrylicBrush { AlwaysUseFallback: false, Opacity: 1 } denser
                        && Math.Abs(denser.TintOpacity - tint) < 0.000001,
                        "亚克力100档被当成实底");
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

    /// <summary>
    /// 商店模式（2026-11-11 用户令）：按钮与闭合选择框吃那一支固定面色，按钮材质那四项设置怎么改都不动它。
    /// 与 <see cref="CheckControlBackgroundOpacityAsync"/> 的沉浸档相对 —— 那一档量的是四条滑块落到画笔上，
    /// 这一档量的是「不读它们」。
    /// </summary>
    private async Task CheckSimpleControlMaterialAsync(DetailPage page, UiSettings ui)
    {
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return;
        var controls = Descendants(Get<StackPanel>(page, "ColumnActions")).OfType<Control>()
            .Concat(Descendants(Get<Grid>(page, "StoreCoverActions")).OfType<Control>())
            .Concat(Descendants(Get<Grid>(page, "StorePlaySlot")).OfType<Control>())
            .Where(control => control is Button or ToggleButton && control.Visibility == Visibility.Visible)
            .Concat(Descendants(page).OfType<EgPicker>().Where(picker => picker.Visibility == Visibility.Visible).Cast<Control>())
            .ToArray();
        // 用户新要求的三颗图标键与闭合选择框同底色同圆角，主播放键走主题色面；其余操作与闭合选择框保持原固定面色。
        var coverControls = controls.Where(page.IsStoreCoverAction).ToArray();
        var plays = controls.Where(control => !page.IsStoreCoverAction(control) && (control.Name is "ColumnPlayButton" or "CompactPlayButton")).ToArray();
        var fixedControls = controls.Except(plays).Except(coverControls).ToArray();
        Require(fixedControls.Length > 0, "商店模式夹具没有操作按钮或选择框");
        RequireFixedSurface(fixedControls);
        RequireAccentSurface(plays);
        RequireStoreCoverActionAppearance(page);
        var snapshot = (ui.UseAcrylicControls, ui.ControlTintOpacity, ui.ControlTintLuminosityOpacity,
            ui.ControlBackgroundOpacity, ui.ControlBackgroundColor);
        try
        {
            ui.UseAcrylicControls = false;
            ui.ControlBackgroundOpacity = 100;
            ui.ControlBackgroundColor = "#FF00FF";
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
            RequireFixedSurface(fixedControls);
            RequireAccentSurface(plays);
            RequireStoreCoverActionAppearance(page);
            ui.UseAcrylicControls = true;
            ui.ControlTintOpacity = 88;
            ui.ControlTintLuminosityOpacity = 12;
            ui.ControlBackgroundColor = "#00FF00";
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
            RequireFixedSurface(fixedControls);
            RequireAccentSurface(plays);
            RequireStoreCoverActionAppearance(page);
        }
        finally
        {
            (ui.UseAcrylicControls, ui.ControlTintOpacity, ui.ControlTintLuminosityOpacity,
                ui.ControlBackgroundOpacity, ui.ControlBackgroundColor) = snapshot;
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
        }
    }

    /// <summary>
    /// 这组控件的四态底色是不是那支浅色固定面 —— 比画笔自己的读数：颜色取 <c>EgStoreControlBrush</c> 的白，
    /// 浓度取 <see cref="ControlBackgroundBrushes"/> 上那四个定值。
    /// </summary>
    private static void RequireFixedSurface(IEnumerable<Control> controls)
    {
        var surface = ((SolidColorBrush)Application.Current.Resources["EgStoreControlBrush"]).Color;
        var levels = new[]
        {
            ControlBackgroundBrushes.FixedRest, ControlBackgroundBrushes.FixedHover,
            ControlBackgroundBrushes.FixedPressed, ControlBackgroundBrushes.FixedDisabled
        };
        foreach (var control in controls)
        {
            var prefix = control is ComboBox ? "ComboBox" : control is ToggleButton ? "ToggleButton" : "Button";
            for (var index = 0; index < 4; index++)
            {
                var suffix = new[] { "", "PointerOver", "Pressed", "Disabled" }[index];
                Require(control.Resources[prefix + "Background" + suffix] is AcrylicBrush { AlwaysUseFallback: true } copy
                    && copy.FallbackColor == surface
                    && Math.Abs(copy.Opacity - levels[index]) < 0.000001,
                    $"{control.Name}/{suffix} 商店模式没有用固定面色："
                        + (control.Resources[prefix + "Background" + suffix] is AcrylicBrush actual
                            ? $"实际 {actual.FallbackColor}、{actual.Opacity:0.###}、回退 {actual.AlwaysUseFallback}"
                            : "不是亚克力画刷"));
            }
            if (control is ComboBox)
                Require(ReferenceEquals(control.Resources["ComboBoxBackground"], control.Resources["ComboBoxBackgroundFocused"])
                    && ReferenceEquals(control.Resources["ComboBoxBackground"], control.Resources["ComboBoxBackgroundUnfocused"]),
                    control.Name + " 商店模式的聚焦底色没有沿用固定面色");
        }
    }

    /// <summary>
    /// 商店模式主播放键的主题色面（2026-11-14 用户令「播放按钮改成主题色」）：四态取主题里已经定好的
    /// 强调、悬停、按下和弱化档，字和图标取实心强调键的对比色。按钮材质那四项设置改到哪儿都不动它。
    /// </summary>
    private static void RequireAccentSurface(IEnumerable<Control> controls)
    {
        var levels = new[] { "EgAccentBrush", "EgAccentHoverBrush", "EgAccentPressedBrush", "EgAccentMutedBrush" };
        var ink = ((SolidColorBrush)Application.Current.Resources["EgTextOnAccentBrush"]).Color;
        foreach (var control in controls)
        {
            for (var index = 0; index < 4; index++)
            {
                var suffix = new[] { "", "PointerOver", "Pressed", "Disabled" }[index];
                var expected = ((SolidColorBrush)Application.Current.Resources[levels[index]]).Color;
                Require(control.Resources["ButtonBackground" + suffix] is AcrylicBrush { AlwaysUseFallback: true } copy
                    && copy.FallbackColor == expected && Math.Abs(copy.Opacity - 1) < 0.000001,
                    $"{control.Name}/{suffix} 播放键没有用主题色："
                        + (control.Resources["ButtonBackground" + suffix] is AcrylicBrush actual
                            ? $"实际 {actual.FallbackColor}、{actual.Opacity:0.###}，该是 {expected}"
                            : "不是亚克力画刷"));
            }
            Require(control.Foreground is SolidColorBrush { Color: var foreground } && foreground == ink,
                control.Name + " 播放键的文字和图标没有用强调面对比色");
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
            // 菜单是 Flyout：前台被别的窗口抢走、或能力重排把它收起时，浮层会连同模板实例一起消失，
            // 这时再找子菜单只会等到超时（2026-11-17 实测五套主题里第三套被收起）。最多重开三次，
            // 真缺子菜单仍然报红，不把「没长出来」和「没有这一项」混成同一句。
            MenuFlyoutPresenter[] presenters = [];
            MenuFlyoutSubItem? submenu = null;
            for (var attempt = 0; attempt < 3 && submenu is null; attempt++)
            {
                if (Presenters().Length == 0)
                {
                    Call(page, "OnMoreClicked", anchor, new RoutedEventArgs());
                    await LayoutAsync(page);
                }
                presenters = Presenters();
                if (presenters.Length != 1) continue;
                submenu = await WaitSubmenuAsync(presenters[0]);
                if (submenu is not null) break;
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).ToArray()) popup.IsOpen = false;
                await Task.Delay(150);
            }
            presenters = Presenters();
            Require(presenters.Length == 1 && submenu is not null, "详情更多菜单被收起或没有子菜单");
            Check(presenters[0]);
            var material = (DetailMenuBackdrop)presenters[0].SystemBackdrop;
            // 自动走查可能在用户操作其他窗口时运行。记录实际状态，并核对回退仍为卡片色；
            // Active 透底由独立检查入口和前台屏幕证据验证，不能把 Fallback 当作透明成功。
            Require(material.UsesCardColor, "菜单染色或回退色没有跟随当前卡片色：" + material.MaterialState);
            File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "detail-menu-material.txt"),
                $"{theme}: {material.MaterialState}\n");
            await SaveProbeScreenAsync($"detail-menu-acrylic-{theme}-screen.png");
            // 菜单项是模板实例化出来的，弹层对象先到、子菜单项晚一拍；上面已经等到它真的进树。
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

        async Task<MenuFlyoutSubItem?> WaitSubmenuAsync(MenuFlyoutPresenter presenter)
        {
            for (var attempt = 0; attempt < 160; attempt++)
            {
                var found = Descendants(presenter).OfType<MenuFlyoutSubItem>().FirstOrDefault();
                if (found is not null) return found;
                await Task.Delay(25);
            }
            return null;
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
            // 同 CheckControlBackgroundOpacityAsync：出厂材质已是亚克力，这一段量的是半透明叠层那条路
            // （浓度就是整支画笔的不透明度），所以显式切回叠层。
            ui.UseAcrylicControls = false;
            ShellPrefs.ApplyAppearance(ui);
            var button = Get<Button>(banner, "OpenButton");
            var baselineReads = requestCount();
            foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
            {
                ui.ControlBackgroundOpacity = 0;
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
            // 详情那三项染色参数不归首页管：改了它们，轮播的图与那三道遮罩都不该跟着动。
            ui.BackdropTintOpacity = 12;
            ui.BackdropTintLuminosityOpacity = 78;
            ShellPrefs.ApplyAppearance(ui);
            Require(ReferenceEquals(homePicture, banner.FrontPicture), "背景染色参数改动了首页轮播图片");
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(banner);
                await UntilAsync(() => Math.Abs(banner.FrontLayer.Opacity - 1) < 0.001);
                Require(Get<Border>(banner, "Top").Visibility == Visibility.Visible
                    && Get<Border>(banner, "Fade").Visibility == Visibility.Visible
                    && Get<Border>(banner, "RightShade").Visibility == Visibility.Visible,
                    "详情背景参数撤掉了主页轮播原遮罩");
                RequireHomeWithoutBottomFade(banner);
                await SaveProbeScreenAsync($"home-backdrop-tint-{theme.Id}-screen.png");
            }
            ui.BackdropTintOpacity = 55;
            ui.BackdropTintLuminosityOpacity = 55;
            ui.UseAcrylicControls = false;
            ShellPrefs.ApplyAppearance(ui);
            Require(Get<Border>(banner, "Fade").Visibility == Visibility.Visible, "主页切回后遮罩没有恢复");
            RequireHomeWithoutBottomFade(banner);
            Require(requestCount() == baselineReads, "主页外观切换重新请求了图片或媒体");
        }
        finally { _root.Children.Remove(banner); }
    }
}
