using System.Collections.ObjectModel;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // 模板与显隐是受控样本，不代表真实指针输入；只读假服务不启动播放。
    private async Task AcrylicControlsAsync()
    {
        if (_window!.IsMaximized) _window.ToggleMaximize();
        using var fixture = CreateFixture();
        var reads = new List<string>();
        var savedTheme = ThemeHost.Current;
        var savedMotion = HomeMotion.PreferenceEnabled;
        var savedBackground = _root.Background;
        var picture = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync(1280, 720);
        var series = Series();
        series.ImageTags["Primary"] = "fixture";
        series.BackdropImageTags.Add("fixture");
        var episode = Episode("a");
        episode.RunTimeTicks = TimeSpan.FromMinutes(45).Ticks;
        episode.UserData = new() { PlaybackPositionTicks = TimeSpan.FromMinutes(20).Ticks };
        episode.MediaSources = [new() { Id = "offline", Name = "离线版本", Container = "mkv" }];
        var movies = Enumerable.Range(0, 7).Select(index => new EmbyItem
        {
            Id = "material-" + index,
            Name = $"材质电影 {index + 1}",
            Type = EmbyItemType.Movie,
            RunTimeTicks = TimeSpan.FromMinutes(90).Ticks,
            ProductionYear = 2026
        }).ToArray();
        foreach (var item in movies) item.ImageTags["Primary"] = "fixture";
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get || request.Method == HttpMethod.Head,
                "材质检查拒绝服务器写入：" + request.Method);
            var path = request.RequestUri!.AbsolutePath;
            var result = path.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                        path.Contains("/Images/Backdrop", StringComparison.Ordinal) ? backdrop : picture)
                }
                : path.EndsWith("/Items/show", StringComparison.Ordinal) ? Json(series)
                : path.EndsWith("/Items/ep-a", StringComparison.Ordinal) ? Json(episode)
                : path.EndsWith("/Episodes", StringComparison.Ordinal) ? Json(new { Items = new[] { episode } })
                : path.EndsWith("/Notifications/Types", StringComparison.Ordinal) ? Json(new[] {
                    new NotificationCategoryInfo { Id = "playback", Name = "播放", Events = [
                        new NotificationTypeInfo { Id = "playback.stop", Name = "播放停止" }] } })
                : path.EndsWith("/Notifications/Services/Configured", StringComparison.Ordinal) ? Json(new[] {
                    new UserNotificationInfo { Id = "offline", FriendlyName = "离线通知示例",
                        ServiceName = "Webhooks", Enabled = true, EventIds = ["playback.stop"] } })
                : FakeTransport.Standard(request);
            return Task.FromResult(result);
        };
        await fixture.SelectAsync();
        await fixture.SelectAsync("b.invalid", "b");
        var palette = Application.Current.Resources.MergedDictionaries.Single(dictionary =>
            dictionary.Source?.ToString().EndsWith("Palette.xaml", StringComparison.OrdinalIgnoreCase) == true);
        var dark = (ResourceDictionary)palette.ThemeDictionaries["Dark"];
        var keys = new[] { "EgActionGlassBrush", "EgActionGlassHoverBrush", "EgActionGlassPressedBrush" };
        var frame = new Frame();
        var holder = new Grid { Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Top };
        MoviePilotSearchProgressView? progressCard = null;
        MoviePilotResourceBrowser? moviePilotCards = null;
        AniBtResourceBrowser? aniBtProgress = null;
        AniBtResourceBrowser? aniBtResources = null;
        string? builtCardStage = null;
        var cards = new ObservableCollection<CardItem>(movies.Select(item => new CardItem(item, fixture.Images, 210)));
        var strip = new ShelfStrip { ItemsSource = cards, ItemSpacing = 16 };
        var fallback = keys.Append("CardBackgroundFillColorDefaultBrush").Select(key =>
            (Brush: (AcrylicBrush)dark[key], Previous: ((AcrylicBrush)dark[key]).AlwaysUseFallback)).ToArray();
        HomeMotion.ApplyPreference(false);
        _root.Children.Add(frame);
        ThemeHost.Register(frame);
        ThemeHost.Register(holder);
        try
        {
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, series)), "详情页导航失败");
            var detail = (DetailPage)frame.Content;
            await UntilAsync(() => detail.IsReady && detail.ViewModel.HeroImage is not null
                && detail.ViewModel.PlayTarget?.HasResumePosition == true);
            strip.ItemTemplate = (DataTemplate)detail.Resources["CardTemplate"];
            holder.Children.Add(strip);
            foreach (var stage in new[] { "detail", "shelf", "servers", "notifications", "library", "mpcards", "anibtcards" })
            {
                if (stage == "shelf") { _root.Children.Remove(frame); _root.Children.Add(holder); }
                if (stage == "servers")
                {
                    _root.Children.Remove(holder);
                    _root.Children.Add(frame);
                    Require(frame.Navigate(typeof(ServersPage), new ServerRequest(fixture.Services)), "服务器页导航失败");
                    await UntilAsync(() => ((ServersPage)frame.Content).IsReady);
                }
                if (stage == "notifications")
                {
                    Require(frame.Navigate(typeof(NotificationsPage), new NotificationsRequest(fixture.Services)), "通知页导航失败");
                    await UntilAsync(() => ((NotificationsPage)frame.Content).IsReady
                        && ((NotificationsPage)frame.Content).ViewModel.Entries.Count == 1);
                }
                if (stage == "library")
                {
                    _root.Children.Remove(holder);
                    _root.Children.Remove(frame);
                    _root.Children.Add(frame);
                    Require(frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services)), "媒体库搜索页导航失败");
                    await UntilAsync(() => ((LibraryPage)frame.Content).IsReady);
                }
                if (stage is "mpcards" or "anibtcards")
                {
                    _root.Children.Remove(frame);
                    _root.Children.Remove(holder);
                    if (builtCardStage != stage)
                    {
                        holder.Children.Clear();
                        holder.Children.Add(stage == "mpcards" ? BuildMoviePilotCards() : BuildAniBtCards());
                        builtCardStage = stage;
                    }
                    _root.Children.Add(holder);
                }
                foreach (var theme in UiThemes.All)
                    foreach (var width in new[] { 1280, 760 })
                    {
                        ThemeHost.Apply(theme);
                        _root.Background = new SolidColorBrush(ThemeHost.ToColor(theme.Colors.Window));
                        ResizeProbeClient(width, stage == "mpcards" ? 920 : 700);
                        var page = stage is "shelf" or "mpcards" or "anibtcards"
                            ? (FrameworkElement)holder : (FrameworkElement)frame.Content;
                        await LayoutAsync(page);
                        // 窗口尺寸变更由系统异步落实；等目标宽度到位再量，不把遮挡或渲染节流当成产品尺寸不同。
                        await WaitForLayoutAsync(() =>
                        {
                            _root.UpdateLayout();
                            return Math.Abs(_root.ActualWidth - width) < 1;
                        }, "客户区未在时限内到达目标宽度");
                        Require(Math.Abs(_root.ActualWidth - width) < 1, "客户区未到达目标宽度");
                        CheckRoles(theme);
                        var samples = theme.Id == UiThemes.DefaultId && width == 1280;
                        if (stage == "detail")
                        {
                            await ScrollProbeAsync(Get<ScrollView>(detail, "Body"), 0);
                            var group = Get<StackPanel>(detail, width == 1280 ? "ColumnActions" : "CompactBlock");
                            await WaitForLayoutAsync(() => group.Visibility == Visibility.Visible
                                && Descendants(group).OfType<Control>().Count(control =>
                                    (control is Button or ToggleButton) && control.Visibility == Visibility.Visible) == 6,
                                "详情操作排未在时限内出现");
                            var controls = Descendants(group).OfType<Control>().Where(control =>
                                (control is Button or ToggleButton) && control.Visibility == Visibility.Visible).ToArray();
                            Require(controls.Length == 6, "详情操作排缺少按钮或连播");
                            foreach (var control in controls)
                            {
                                await CheckStatesAsync(control, false, samples && (control is ToggleButton
                                    || AutomationProperties.GetAutomationId(control) == "ColumnPlayButton"));
                            }
                        }
                        else if (stage == "shelf")
                        {
                            strip.JumpTo(0);
                            await UntilAsync(() => strip.Offset < 1 && Descendants(strip).OfType<PosterCard>()
                                .Any(card => card.Card?.Poster is not null));
                            var poster = Descendants(strip).OfType<PosterCard>()
                                .FirstOrDefault(card => card.Card?.Item.Type == EmbyItemType.Movie);
                            Require(poster is not null, "货架没有可播放卡片");
                            var hover = poster!.ProbeHover();
                            Require(hover.ActionGlass && hover.PlayGlassRest,
                                "海报悬停播放键或右下操作组未使用按钮亚克力");
                            reads.Add($"{theme.Id}/{width}/海报卡：操作组三态玻璃={hover.ActionGlass}，"
                                + $"播放键常态玻璃={hover.PlayGlassRest}，指针强调色={hover.PlayHover}");
                            if (samples)
                            {
                                // 受控样本：把悬停层亮一帧拍照，不代表真实指针手势。
                                var layer = Get<Grid>(poster, "Hover");
                                var shade = Get<Border>(poster, "HoverShade");
                                var previousVisibility = layer.Visibility;
                                var previousOpacity = layer.Opacity;
                                var previousShade = shade.Opacity;
                                try
                                {
                                    layer.Visibility = Visibility.Visible;
                                    layer.Opacity = 1;
                                    shade.Opacity = 0.78;
                                    await LayoutAsync(poster);
                                    await SaveProbeScreenAsync("acrylic-poster-actions-screen.png");
                                }
                                finally
                                {
                                    layer.Visibility = previousVisibility;
                                    layer.Opacity = previousOpacity;
                                    shade.Opacity = previousShade;
                                }
                            }
                            Field(strip, "_hover", true); Call(strip, "SyncArrows");
                            Require(!strip.PrevShown && strip.NextShown, "货架开头箭头显隐不正确");
                            var next = Get<Button>(strip, "Next");
                            await CheckStatesAsync(next, true, samples);
                            Call(strip, "OnNextClicked", next, new RoutedEventArgs());
                            await UntilAsync(() => strip.Offset > 1 && strip.PrevShown);
                            await CheckStatesAsync(Get<Button>(strip, "Prev"), true, false);
                            strip.JumpTo(0); await UntilAsync(() => strip.Offset < 1);
                            Field(strip, "_hover", false); Call(strip, "SyncArrows");
                            Require(!strip.PrevShown && !strip.NextShown, "离开货架后箭头仍命中");
                            Field(strip, "_hover", true); Call(strip, "SyncArrows");
                            Require(strip.NextShown, "恢复悬停后翻页入口未恢复");
                            if (samples)
                            {
                                try
                                {
                                    foreach (var item in fallback) item.Brush.AlwaysUseFallback = true;
                                    await LayoutAsync(holder);
                                    Require(fallback.All(item => item.Brush.AlwaysUseFallback), "未进入受控回退");
                                    await SaveProbeScreenAsync("acrylic-shelf-fallback-screen.png");
                                }
                                finally { foreach (var item in fallback) item.Brush.AlwaysUseFallback = item.Previous; }
                            }
                        }
                        else if (stage == "servers")
                        {
                            var servers = (ServersPage)page;
                            var accounts = Descendants(servers).OfType<Border>().Where(border => border.Tag is AccountRow).ToArray();
                            Require(accounts.Length == 2 && servers.Realised.Accounts == 2, "账号卡未实绘");
                            foreach (var card in accounts) CheckCard(card);
                            CheckCard(Descendants(servers).OfType<Border>().Single(border =>
                                AutomationProperties.GetAutomationId(border) == "ServersAggregateCard"));
                        }
                        else if (stage == "library")
                            CheckLibraryMaterials((LibraryPage)page, theme);
                        else if (stage == "mpcards")
                            await CheckMoviePilotCardsAsync(theme);
                        else if (stage == "anibtcards")
                            await CheckAniBtCardsAsync(theme);
                        else if (stage == "notifications")
                        {
                            var notifications = (NotificationsPage)page;
                            var toggle = Get<ToggleSwitch>(notifications, "StopReportToggle");
                            CheckCard(EnclosingCard(toggle));
                            var list = Descendants(notifications).OfType<ListView>().Single(view =>
                                AutomationProperties.GetAutomationId(view) == "NotificationsEntryList");
                            await UntilAsync(() => list.ContainerFromIndex(0) is ListViewItem);
                            var row = (ListViewItem)list.ContainerFromIndex(0);
                            var action = Descendants(row).OfType<Button>().First(button => button.CommandParameter is NotificationRow);
                            CheckCard(EnclosingCard(action));
                            Require(notifications.StopReport.Bound && notifications.NoteState.Follows, "通知卡原有绑定失效");
                        }
                        if (samples && stage is "library" or "mpcards" or "anibtcards")
                        {
                            var cardGlass = (AcrylicBrush)dark["CardBackgroundFillColorDefaultBrush"];
                            var previous = cardGlass.AlwaysUseFallback;
                            try
                            {
                                cardGlass.AlwaysUseFallback = true;
                                await LayoutAsync((FrameworkElement)page);
                                await SaveProbeScreenAsync($"acrylic-{stage}-fallback-card-screen.png");
                            }
                            finally { cardGlass.AlwaysUseFallback = previous; }
                        }
                        reads.Add($"{stage}/{theme.Id}/{width}: 客户区 {_root.ActualWidth:0}×{_root.ActualHeight:0}; 模板与角色通过");
                        await SaveProbeScreenAsync($"acrylic-{stage}-{theme.Id}-{width}-screen.png");
                    }
            }
            var highContrast = (ResourceDictionary)palette.ThemeDictionaries["HighContrast"];
            foreach (var key in keys) Require(highContrast[key] is SolidColorBrush, "高对比度角色不是实色：" + key);
            var aliases = new ResourceDictionary { Source = new Uri("ms-appx:///Theme/ActionButtons.xaml") };
            var contrastAliases = (ResourceDictionary)aliases.ThemeDictionaries["HighContrast"];
            foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
                "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed" })
                Require(contrastAliases[key] is SolidColorBrush, "高对比度模板别名不是实色：" + key);
            Require(fixture.Actions.Plays == 0, "材质检查请求了播放");
            Require(fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)
                || request.StartsWith("HEAD ", StringComparison.Ordinal)), "材质检查发送了写入");
            reads.Add("高对比度仅核对资源字典；模板状态为模拟；没有更改 OS 设置、注入鼠标或启动播放。");
        }
        finally
        {
            foreach (var item in fallback) item.Brush.AlwaysUseFallback = item.Previous;
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame); _root.Children.Remove(holder);
            HomeMotion.ApplyPreference(savedMotion); ThemeHost.Apply(savedTheme); _root.Background = savedBackground;
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "acrylic-controls.txt"), reads);
        }

        void CheckRoles(UiTheme theme)
        {
            var colors = new[] { theme.Colors.SurfaceElevated, theme.Colors.SurfaceHover, theme.Colors.SurfaceAlt };
            foreach (var dictionary in new[] { "Dark", "Light" })
                for (var index = 0; index < keys.Length; index++)
                {
                    var brush = (ResourceDictionary)palette.ThemeDictionaries[dictionary];
                    Require(brush[keys[index]] is AcrylicBrush acrylic
                        && acrylic.TintColor == ThemeHost.ToColor(colors[index])
                        && acrylic.FallbackColor == ThemeHost.ToColor(colors[index])
                        && ThemeHost.ColorOf(dictionary, keys[index]) == ThemeHost.ToColor(colors[index]),
                        $"{theme.Id}/{dictionary}/{keys[index]} tint 或回退未随主题变化");
                }

            // 低优先级卡片复用设置卡片那支亚克力：tint 与实色回退同样要跟着主题走，回退必须整块不透明。
            foreach (var dictionary in new[] { "Dark", "Light" })
            {
                var entries = (ResourceDictionary)palette.ThemeDictionaries[dictionary];
                Require(entries["CardBackgroundFillColorDefaultBrush"] is AcrylicBrush card
                    && card.TintColor == ThemeHost.ToColor(theme.Colors.SurfaceHover)
                    && card.FallbackColor == ThemeHost.ToColor(theme.Colors.SurfaceHover)
                    && card.FallbackColor.A == 0xFF
                    && ThemeHost.ColorOf(dictionary, "CardBackgroundFillColorDefaultBrush")
                        == ThemeHost.ToColor(theme.Colors.SurfaceHover),
                    $"{theme.Id}/{dictionary}/CardBackgroundFillColorDefaultBrush 未随主题变化或回退仍半透明");
            }
        }
        void CheckLibraryMaterials(LibraryPage page, UiTheme theme)
        {
            CheckCardMaterial(Get<Border>(page, "LibrarySectionsSurface"), "媒体库分类胶囊", theme);
            CheckCardMaterial(Get<Border>(page, "FnNasFilterPanel"), "飞牛筛选面板", theme);
            CheckCardMaterial(Get<SearchSourceTabs>(page, "SourceTabs"), "搜索来源栏", theme);
        }
        async Task CheckMoviePilotCardsAsync(UiTheme theme)
        {
            await WaitForLayoutAsync(() => progressCard is not null && moviePilotCards is not null
                && Descendants(moviePilotCards).OfType<Grid>()
                    .Any(grid => grid.Name == "ResourceCard" && grid.ActualWidth > 100), "MoviePilot 资源卡未在时限内实绘");
            CheckCardMaterial(Get<Border>(progressCard!, "ProgressCard"), "MoviePilot 搜索进度卡", theme);
            CheckCardMaterial(Descendants(moviePilotCards!).OfType<Grid>().First(grid =>
                grid.Name == "ResourceCard" && grid.ActualWidth > 100), "MoviePilot 资源卡", theme);
        }
        async Task CheckAniBtCardsAsync(UiTheme theme)
        {
            await WaitForLayoutAsync(() => aniBtProgress is not null && aniBtResources is not null
                && Descendants(aniBtProgress).OfType<Border>()
                    .Any(border => border.Name == "ProgressCard" && border.ActualWidth > 100)
                && Descendants(aniBtResources).OfType<Expander>()
                    .Any(expander => expander.Name == "ReleaseGroup" && expander.ActualWidth > 100), "AniBT 卡片未在时限内实绘");
            CheckCardMaterial(Descendants(aniBtProgress!).OfType<Border>().First(border =>
                border.Name == "ProgressCard" && border.ActualWidth > 100), "AniBT 进度卡", theme);
            CheckCardMaterial(Descendants(aniBtResources!).OfType<Expander>().First(expander =>
                expander.Name == "ReleaseGroup" && expander.ActualWidth > 100), "AniBT 资源卡", theme);
        }
        async Task WaitForLayoutAsync(Func<bool> ready, string message)
        {
            // 发布件冷启动、主题切换与 ItemsView 回收都比开发件慢一拍；仍按同一组断言，只把等待上限放到 8 秒。
            for (var attempt = 0; attempt < 320; attempt++)
            {
                if (ready()) return;
                await Task.Delay(25);
            }
            throw new TimeoutException(message);
        }
        void CheckCardMaterial(FrameworkElement element, string what, UiTheme theme)
        {
            bool Correct()
            {
                var background = Background();
                return ReferenceEquals(background, dark["CardBackgroundFillColorDefaultBrush"])
                    && background is AcrylicBrush acrylic
                    && acrylic.TintColor == ThemeHost.ToColor(theme.Colors.SurfaceHover)
                    && acrylic.FallbackColor == ThemeHost.ToColor(theme.Colors.SurfaceHover);
            }
            Brush? Background() => element switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                Control control => control.Background,
                _ => null
            };
            void Set(Brush? brush)
            {
                switch (element)
                {
                    case Border border: border.Background = brush; break;
                    case Panel panel: panel.Background = brush; break;
                    case Control control: control.Background = brush; break;
                }
            }

            Require(Correct(), what + "未使用设置卡片亚克力或未随主题更新");
            var original = Background();
            try
            {
                Set((Brush)dark["EgSurfaceBrush"]);
                Require(!Correct(), what + " 检查未拒绝实色反例");
            }
            finally { Set(original); }
            Require(Correct(), what + " 反例恢复失败");
            reads.Add($"{ThemeHost.Current.Id}/{what}: 卡片亚克力 tint={((AcrylicBrush)dark["CardBackgroundFillColorDefaultBrush"]).TintColor}，实色反例已拒绝");
        }
        FrameworkElement BuildMoviePilotCards()
        {
            progressCard = new MoviePilotSearchProgressView();
            progressCard.Attach(new MoviePilotSearchProgressViewModel
            {
                IsActive = true,
                IsIndeterminate = false,
                Value = 54,
                PercentText = "54%",
                Status = "已完成 9 / 15 个搜索来源",
                TitleText = "离线电影：用于材质探针的长标题",
                YearText = "年份：2026"
            });
            moviePilotCards = new MoviePilotResourceBrowser { Height = 620 };
            var browser = new MoviePilotResourceBrowserViewModel();
            browser.SetResults([new MoviePilotResourceRow(new MoviePilotResource
            {
                Title = "离线资源 S01E01 2026 2160p WEB-DL HDR10 HEVC AAC 中字-离线组",
                SiteName = "离线站点",
                Description = "用于材质探针的长文本资源描述，标题、标签和资源信息应保持可读。",
                Size = 8L * 1024 * 1024 * 1024,
                Seeders = 88,
                Peers = 3,
                Resolution = "2160p",
                VideoCodec = "HEVC",
                Edition = "WEB-DL",
                ReleaseGroup = "离线组",
                ResourceType = "WEB-DL",
                ResourceEffect = "HDR10",
                Labels = ["中字"],
                PublishedAt = DateTimeOffset.Now,
                PageUrl = "https://moviepilot.invalid/details",
                TorrentInfo = System.Text.Json.JsonSerializer.SerializeToElement(new { title = "fixture" })
            }, _ => Task.CompletedTask, _ => { })], autoEpisode: null);
            moviePilotCards.Attach(browser);
            return new StackPanel { Spacing = 12, Children = { progressCard, moviePilotCards } };
        }
        FrameworkElement BuildAniBtCards()
        {
            var release = new AniBtRelease("rel-1", "离线番剧 第01集 1080p WEB-DL 简中",
                new Uri("https://anibt.invalid/download/rel-1"),
                new Uri("https://anibt.invalid/release/rel-1"),
                Size: 1_500_000_000,
                PublishedAt: DateTimeOffset.Now.AddHours(-2),
                GroupName: "离线字幕组",
                EpisodeKey: "1",
                Resolution: "1080p",
                Format: "WEB-DL",
                Languages: ["简中"],
                Subtitle: "内嵌",
                Tags: ["合集"]);
            var resource = new MoviePilotResource
            {
                Title = release.Title,
                Size = 1_500_000_000,
                SiteName = "AniBT",
                ReleaseGroup = "离线字幕组",
                Resolution = "1080p",
                PageUrl = "https://anibt.invalid/release/rel-1",
                TorrentInfo = System.Text.Json.JsonSerializer.SerializeToElement(new { title = "fixture" })
            };
            var row = new AniBtResourceRow(new AniBtResource(release, resource), _ => Task.CompletedTask, _ => { });
            var source = new AniBtReleaseGroup("group-1", "离线字幕组", "offline", [1, 2, 3, 4], 4, [release]);
            var anime = new AniBtAnime("anime-1", 1, "离线番剧", EpisodeCount: 12);
            AniBtResourceBrowser Build(int view)
            {
                var model = new AniBtBrowserViewModel();
                model.SetResults([row], "", [source], anime);
                model.ViewIndex = view;
                var browser = new AniBtResourceBrowser { Height = 300 };
                browser.Attach(model);
                return browser;
            }
            aniBtProgress = Build(0);
            aniBtResources = Build(1);
            return new StackPanel { Spacing = 12, Children = { aniBtProgress, aniBtResources } };
        }
        void CheckCard(Border card)
        {
            bool Correct() => card.ActualWidth > 100
                && ReferenceEquals(card.Background, dark["CardBackgroundFillColorDefaultBrush"])
                && card.Background is AcrylicBrush;
            Require(Correct(), "手写卡片未使用设置卡片亚克力");
            var original = card.Background;
            try
            {
                card.Background = (Brush)dark["EgSurfaceBrush"];
                Require(!Correct(), "卡片检查未拒绝实色反例");
            }
            finally { card.Background = original; }
            Require(Correct(), "卡片反例恢复失败");
        }
        Border EnclosingCard(DependencyObject child)
        {
            for (var node = VisualTreeHelper.GetParent(child); node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is Border border && border.ActualWidth > 150
                    && border.CornerRadius == (CornerRadius)Application.Current.Resources["EgCardCornerRadius"]) return border;
            throw new InvalidOperationException("未找到通知条目或接管卡的实际外层 Border");
        }
        async Task CheckStatesAsync(Control control, bool circle, bool shots)
        {
            control.ApplyTemplate();
            var handle = AutomationProperties.GetAutomationId(control);
            if (string.IsNullOrEmpty(handle)) handle = control.Name;
            var states = control is ToggleButton
                ? new[] { "Normal", "PointerOver", "Pressed", "Checked", "CheckedPointerOver", "CheckedPressed" }
                : new[] { "Normal", "PointerOver", "Pressed" };
            var restore = control is ToggleButton { IsChecked: true } ? "Checked" : "Normal";
            try
            {
                for (var index = 0; index < states.Length; index++)
                {
                    Require(VisualStateManager.GoToState(control, states[index], false), "模板状态不存在：" + states[index]);
                    var expected = dark[keys[index % 3]];
                    await UntilAsync(() => ReferenceEquals(Fill(), expected));
                    Require(Fill() is AcrylicBrush, "模板实际绘制的背景不是亚克力");
                    Require(!ReferenceEquals(Fill(), dark["EgSurfaceBrush"]), "反例未拒绝实色按钮背景");
                    if (circle)
                    {
                        var ellipse = Descendants(control).OfType<Ellipse>().Single(shape => shape.Name == "Circle");
                        Require(control.Width == 48 && control.Height == 48 && ellipse.StrokeThickness == 2,
                            "翻页圆钮尺寸或描边改变");
                    }
                    reads.Add($"{ThemeHost.Current.Id}/{control.Name}/{AutomationProperties.GetAutomationId(control)}/{states[index]}: 实际刷子={keys[index % 3]}");
                    if (shots) await SaveProbeScreenAsync($"acrylic-state-{handle}-{states[index]}-screen.png");
                }
            }
            finally { VisualStateManager.GoToState(control, restore, false); }
            Brush? Fill() => circle
                ? Descendants(control).OfType<Ellipse>().Single(shape => shape.Name == "Circle").Fill
                : Descendants(control).OfType<ContentPresenter>().First(presenter => presenter.Name == "ContentPresenter").Background;
        }
    }
}
