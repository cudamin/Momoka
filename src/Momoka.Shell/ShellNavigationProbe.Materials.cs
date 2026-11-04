using System.Collections.ObjectModel;
using System.Net;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
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
            foreach (var stage in new[] { "detail", "shelf", "servers", "notifications" })
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
                foreach (var theme in UiThemes.All)
                    foreach (var width in new[] { 1280, 760 })
                    {
                        ThemeHost.Apply(theme);
                        _root.Background = new SolidColorBrush(ThemeHost.ToColor(theme.Colors.Window));
                        ResizeProbeClient(width, 700);
                        var page = stage == "shelf" ? (FrameworkElement)holder : (FrameworkElement)frame.Content;
                        await LayoutAsync(page);
                        Require(Math.Abs(_root.ActualWidth - width) < 1, "客户区未到达目标宽度");
                        CheckRoles(theme);
                        var samples = theme.Id == UiThemes.DefaultId && width == 1280;
                        if (stage == "detail")
                        {
                            await ScrollProbeAsync(Get<ScrollView>(detail, "Body"), 0);
                            var group = Get<StackPanel>(detail, width == 1280 ? "ColumnActions" : "CompactBlock");
                            await UntilAsync(() => group.Visibility == Visibility.Visible
                                && Descendants(group).OfType<Control>().Count(control =>
                                    (control is Button or ToggleButton) && control.Visibility == Visibility.Visible) == 6);
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
                        else
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
