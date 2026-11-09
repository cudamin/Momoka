using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Updates;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeOrderAsync()
    {
        var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var transport = new HomeOrderTransport(server);
        using var fixture = CreateFixture(transport);
        await fixture.SelectAsync();
        fixture.Capabilities.Sources = [new(fixture.Session.Capture(), MediaServerKind.Emby, true)];
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var library = new EmbyItem { Id = "library", Name = "离线电影库", Type = EmbyItemType.CollectionFolder };
        fixture.Session.Capture().Bind(library);
        var content = new EmbyItem
        {
            Id = "home-order-movie",
            Name = "离线影片",
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        };
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, [library]));
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(server.Poster) });
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(new[] { content }));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal) || path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = new[] { content } }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "home-order"));
        paths.EnsureCreated();
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var settings = new SettingsPage();
        settings.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
            service, new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            new UpdateClient(new RejectNetwork()), fixture.Capabilities, fixture.Images);
        await settings.ViewModel.ReloadAsync();
        var frame = new Frame();
        _root.Children.Add(settings);
        try
        {
            // Attach/Reload 不开启页面活动期；复用设置探针入口，不绕过正式监测的 lifetime 守卫。
            BeginSettingsFixtureVisit(settings);
            settings.WatchMoviePilot(service);
            settings.ViewModel.SelectedCategory = "主页";
            await LayoutAsync(settings);
            var table = settings.ViewModel.HomeRows!;
            var choices = table.Rows;
            Require(table.Label == "媒体库顺序" && choices.All(row => !HomeLayout.IsMoviePilot(row.Key)), "关闭时排序表仍有 MP 或标题未改");
            fixture.Settings.MoviePilot.Enabled = true;
            await LayoutAsync(settings);
            Require(choices.All(row => !HomeLayout.IsMoviePilot(row.Key)), "未连接时排序表显示 MP");
            await ConnectMoviePilotAsync(fixture);
            await UntilAsync(() => ReferenceEquals(table, settings.ViewModel.HomeRows)
                && choices.Count(row => HomeLayout.IsMoviePilot(row.Key)) == 2);
            var series = choices.Single(row => row.Key == HomeLayout.MoviePilotSeries);
            while (series.CanUp) series.UpCommand.Execute(null);
            var movies = choices.Single(row => row.Key == HomeLayout.MoviePilotMovies);
            while (choices.IndexOf(movies) > 2) movies.UpCommand.Execute(null);
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 980);
                await LayoutAsync(settings);
                var list = Descendants(settings).OfType<ListView>().Single(view => ReferenceEquals(view.ItemsSource, choices));
                list.StartBringIntoView();
                await LayoutAsync(settings);
                // 探针夹具默认开着聚合模式（见 Fixture 构造），2026-11-03 起那一档不排「接下来看」：
                // 六排固定/库/订阅的默认表因此是五排（继续观看、媒体库、库那一排、MoviePilot 两排）。
                Require(list.Items.Count == 5 && Descendants(list).OfType<CheckBox>()
                    .Any(check => AutomationProperties.GetName(check) == HomeLayout.FixedTitle(HomeLayout.MoviePilotSeries)), "排序表未实现订阅勾选控件");
                await SaveProbeScreenAsync($"settings-home-order-{width}.png");
            }
            // 真正的复选框写回偏好；断线期间编辑 Emby 排不能把隐藏项抹掉。
            var movieCheck = Descendants(settings).OfType<CheckBox>()
                .Single(check => AutomationProperties.GetName(check) == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies));
            movieCheck.IsChecked = false;
            await LayoutAsync(settings);
            Require(!fixture.Settings.Ui.HomeRows.Single(row => row.Key == HomeLayout.MoviePilotMovies).Visible, "电影订阅勾选未保存");
            transport.Offline = true;
            Require(!await service.RefreshAvailabilityAsync(), "假断线没有撤销可用性");
            await UntilAsync(() => choices.All(row => !HomeLayout.IsMoviePilot(row.Key)));
            // 轮播图开着时「媒体库」固定第一（2026-10-31 用户令），挪它一下不作数；这一下换了「继续观看」，
            // 验的还是「断线期间编辑 Emby 排不抹掉隐藏项」那件事。
            choices.Single(row => row.Key == HomeLayout.Resume).DownCommand.Execute(null);
            var hidden = HomeLayout.Save(HomeLayout.Plan(fixture.Settings.Ui.HomeRows, null));
            await LayoutAsync(settings);
            await SaveProbeScreenAsync("settings-home-order-offline.png");
            var hiddenList = Descendants(settings).OfType<ListView>().Single(view => ReferenceEquals(view.ItemsSource, choices));
            Require(hiddenList.Items.Count == 3 && Math.Abs(hiddenList.ActualHeight - table.ListHeight) < 1,
                "断线后排序表没有收起整行或仍有空白");
            // ListView 的回收池仍可保留旧视觉孩子；只检查当前四个有效容器，不把回收池当屏上控件。
            for (var index = 0; index < hiddenList.Items.Count; index++)
            {
                var container = (ListViewItem)hiddenList.ContainerFromIndex(index);
                Require(container.Content is HomeRowChoice choice && !HomeLayout.IsMoviePilot(choice.Key)
                    && Descendants(container).OfType<CheckBox>().All(check => AutomationProperties.GetName(check) == choice.Title),
                    "断线后有效列表项仍有订阅控件");
            }
            transport.Offline = false;
            Require(await service.RefreshAvailabilityAsync(), "假连接未恢复");
            await UntilAsync(() => choices.Count == 5);
            Require(ReferenceEquals(choices, table.Rows) && !choices.Single(row => row.Key == HomeLayout.MoviePilotMovies).Visible
                && HomeLayout.Same(hidden, HomeLayout.Plan(fixture.Settings.Ui.HomeRows, null)), "恢复连接丢失原次序或勾选");
            settings.ReleaseHosted();
            BeginSettingsFixtureVisit(settings);
            settings.WatchMoviePilot(service);
            await UntilAsync(() => choices.Count == 5);
            Require(ReferenceEquals(table, settings.ViewModel.HomeRows)
                && ReferenceEquals(choices, table.Rows) && choices.Count == 5,
                "缓存设置页重入未恢复订阅排序");
            await LayoutAsync(settings);
            await SaveProbeScreenAsync("settings-home-order-restored.png");
            choices.Single(row => row.Key == HomeLayout.MoviePilotMovies).Visible = true;

            settings.ReleaseHosted();
            _root.Children.Remove(settings);
            _root.Children.Add(frame);
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, [library], null));
            var home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.HomeRows.OfType<MoviePilotHomeRow>().Count() == 2);
            var repeater = Get<ItemsRepeater>(home, "ShelfRepeater");
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 980);
                await LayoutAsync(home);
                var keys = home.HomeRows.Select(row => row is CardShelf shelf ? shelf.HomeKey : ((MoviePilotHomeRow)row).Key).ToArray();
                // 聚合模式不排「接下来看」：存档里那一排还留着（关掉聚合会回来），主页采用的口径是设置表投影后的可见行。
                Require(choices.Where(row => row.Visible).Select(row => row.Key).SequenceEqual(keys), "主页未采用排序表次序");
                for (var index = 0; index < home.HomeRows.Count; index++)
                {
                    if (home.HomeRows[index] is not MoviePilotHomeRow subscription) continue;
                    var element = (FrameworkElement)repeater.GetOrCreateElement(index);
                    await LayoutAsync(element);
                    var head = HomeFoldMotion.Find<ShelfHead>(element);
                    var strip = HomeFoldMotion.Find<ShelfStrip>(element);
                    Require(head?.Title == subscription.Title && head.ActualHeight > 0 && strip?.ActualHeight > 0,
                        "混排订阅命中了隐藏的标题或条带");
                    Require(strip!.TransformToVisual(head!).TransformPoint(default).Y > head!.ActualHeight, "订阅标题与封面之间缺少间距");
                }
                Get<ScrollView>(home, "Scroller").ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                await LayoutAsync(home);
                await SaveProbeScreenAsync($"home-subscription-order-{width}.png");
                await HomeSubscriptionLoadingLayoutAsync(home, server, width);
                await HomeLibraryLoadingLayoutAsync(home, fixture, width);
            }

            // 媒体库后面两排为 MP / Emby：压上轮播前后的默认界线必须相同。
            var libraryChoice = choices.Single(row => row.Key == HomeLayout.Libraries);
            while (libraryChoice.CanUp) libraryChoice.UpCommand.Execute(null);
            var resume = choices.Single(row => row.Key == HomeLayout.Resume);
            while (choices.IndexOf(resume) > 2) resume.UpCommand.Execute(null);
            fixture.Settings.Ui.ShowHomeBanner = true;
            fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
            ShellPrefs.Apply(fixture.Settings.Ui);
            await UntilAsync(() => home.IsReady && Get<HomeBanner>(home, "Banner").Visibility == Visibility.Visible);
            ResizeInspect(1100, 980);
            await LayoutAsync(home);
            await Task.Delay(700);
            var gap = ((StackLayout)repeater.Layout).Spacing;
            var before = ((double Line, bool HasNext))Call(home, "MeasureCoversLine", gap)!;
            fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.OnCarousel;
            ShellPrefs.Apply(fixture.Settings.Ui);
            await UntilAsync(() => home.IsReady && home.ViewModel.LibraryOnBanner);
            await LayoutAsync(home);
            await Task.Delay(700);
            var after = ((double Line, bool HasNext))Call(home, "MeasureCoversLine", gap)!;
            Require(before.HasNext && after.HasNext && Math.Abs(before.Line - after.Line) < 2,
                $"订阅混排的轮播折叠阈值漂移：{before.Line:0.0} → {after.Line:0.0}");
            await SaveProbeScreenAsync("home-subscription-order-overlay.png");
            await HomeSubscriptionIdleAsync(home, server);
            fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
            ShellPrefs.Apply(fixture.Settings.Ui);
            await UntilAsync(() => home.IsReady && !home.ViewModel.LibraryOnBanner);

            choices.Single(row => row.Key == HomeLayout.MoviePilotSeries).Visible = false;
            await UntilAsync(() => home.HomeRows.OfType<MoviePilotHomeRow>().Count() == 1);
            Require(home.HomeRows.OfType<MoviePilotHomeRow>().Single().Key == HomeLayout.MoviePilotMovies, "取消电视剧勾选影响了电影排");
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => home.HomeRows.All(row => row is not MoviePilotHomeRow));
            await LayoutAsync(home);
            await SaveProbeScreenAsync("home-subscription-order-disabled.png");
            // 离树的缓存设置页已结束活动期，不等它的旧观察者更新；重开夹具活动期后核当前那张表。
            _root.Children.Add(settings);
            BeginSettingsFixtureVisit(settings);
            settings.WatchMoviePilot(service);
            await UntilAsync(() => choices.Count == 3);
            Require(ReferenceEquals(table, settings.ViewModel.HomeRows)
                && ReferenceEquals(choices, table.Rows), "关闭 MP 后重入替换了排序表");
            settings.ReleaseHosted();
            _root.Children.Remove(settings);
            fixture.Settings.MoviePilot.Enabled = true;
            Require(await service.RefreshAvailabilityAsync(), "关闭后连接未恢复");
            await UntilAsync(() => home.HomeRows.OfType<MoviePilotHomeRow>().Count() == 1);
            _root.Children.Add(settings);
            BeginSettingsFixtureVisit(settings);
            settings.WatchMoviePilot(service);
            await UntilAsync(() => choices.Count == 5);
            Require(ReferenceEquals(table, settings.ViewModel.HomeRows)
                && ReferenceEquals(choices, table.Rows)
                && !choices.Single(row => row.Key == HomeLayout.MoviePilotSeries).Visible, "关闭后恢复丢失电视剧勾选");
            Require(server.Writes.Count == 0, "主页排序触发了订阅写操作");
        }
        finally
        {
            settings.Release();
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(settings);
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }

    private async Task HomeSubscriptionIdleAsync(HomePage home, SubscriptionProbeServer server)
    {
        var vm = home.Subscriptions.ViewModel;
        var scroller = Get<ScrollView>(home, "Scroller");
        await UntilAsync(() => !vm.Busy);
        await LayoutAsync(home);
        await Task.Delay(600);
        var reads = server.SubscriptionReads;
        var rows = home.HomeRows.ToArray();
        var offset = scroller.VerticalOffset;
        var onBanner = home.ViewModel.LibraryOnBanner;
        var overlay = Get<Border>(home, "LibraryOverlay");
        var top = overlay.TransformToVisual(home).TransformPoint(default).Y;
        await SaveProbeScreenAsync("home-subscription-idle-before-screen.png");
        var began = Environment.TickCount64;
        while (Environment.TickCount64 - began < 65_000)
        {
            await Task.Delay(100);
            Require(!vm.Busy && server.SubscriptionReads == reads, "停留主页时仍触发订阅定时读取");
            Require(home.ViewModel.LibraryOnBanner == onBanner && home.HomeRows.SequenceEqual(rows)
                && Math.Abs(scroller.VerticalOffset - offset) < 1
                && Math.Abs(overlay.TransformToVisual(home).TransformPoint(default).Y - top) < 1,
                "停留主页时媒体库位置或滚动发生变化");
        }
        await SaveProbeScreenAsync("home-subscription-idle-after-screen.png");
        await vm.RefreshCommand.ExecuteAsync(null);
        Require(server.SubscriptionReads == reads + 1 && !vm.Busy, "取消定时刷新后手动刷新没有正常读取");
        File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "home-subscription-idle.txt"),
            $"idleMilliseconds={Environment.TickCount64 - began}; reads={reads}; afterManual={server.SubscriptionReads}; offset={offset:0.00}; top={top:0.00}");
    }

    private async Task HomeSubscriptionLoadingLayoutAsync(HomePage home, SubscriptionProbeServer server, int width)
    {
        var vm = home.Subscriptions.ViewModel;
        await UntilAsync(() => !vm.Busy);
        var views = Descendants(home).OfType<MoviePilotSubscriptionsView>().Where(view => view.Row is not null).ToArray();
        Require(views.Length == 2, "订阅加载布局检查需要电影和电视剧两排");
        var heights = views.Select(view => view.ActualHeight).ToArray();
        var strips = views.Select(view => HomeFoldMotion.Find<ShelfStrip>(view)!).ToArray();
        var tops = strips.Select(strip => strip.TransformToVisual(home).TransformPoint(default).Y).ToArray();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.PendingList = gate;
        var read = vm.RefreshCommand.ExecuteAsync(null);
        try
        {
            await UntilAsync(() => vm.Busy);
            await LayoutAsync(home);
            await SaveProbeScreenAsync($"home-subscription-loading-{width}-screen.png");
            for (var index = 0; index < views.Length; index++)
            {
                var height = views[index].ActualHeight;
                var top = strips[index].TransformToVisual(home).TransformPoint(default).Y;
                File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "home-subscription-loading.txt"),
                    $"width={width}; row={index}; height={heights[index]:0.00}->{height:0.00}; top={tops[index]:0.00}->{top:0.00}\n");
                Require(Math.Abs(height - heights[index]) < 1 && Math.Abs(top - tops[index]) < 1,
                    "订阅加载条改变列表高度或封面位置");
                Require(Descendants(views[index]).OfType<ProgressBar>().Any(bar =>
                    bar.Visibility == Visibility.Visible && bar.ActualHeight > 0), "订阅加载进度不可见");
            }
        }
        finally
        {
            server.PendingList = null;
            gate.TrySetResult(Json(new { success = true, data = server.Items.Values.ToArray() }));
            await read;
        }
        await LayoutAsync(home);
        Require(views.Select((view, index) => Math.Abs(view.ActualHeight - heights[index]) < 1).All(same => same),
            "订阅加载完成后列表高度没有保持");
    }

    private async Task HomeLibraryLoadingLayoutAsync(HomePage home, Fixture fixture, int width)
    {
        var reply = fixture.Transport.Reply;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repeater = Get<ItemsRepeater>(home, "ShelfRepeater");
        var before = repeater.TransformToVisual(home).TransformPoint(default).Y;
        fixture.Transport.Reply = async (request, token) =>
        {
            if (!request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
            {
                entered.TrySetResult();
                await gate.Task.WaitAsync(token);
            }
            return await (reply?.Invoke(request, token) ?? Task.FromResult(FakeTransport.Standard(request)));
        };
        var read = home.ViewModel.ReloadAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await LayoutAsync(home);
            var during = repeater.TransformToVisual(home).TransformPoint(default).Y;
            File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "home-library-loading.txt"),
                $"width={width}; top={before:0.00}->{during:0.00}\n");
            await SaveProbeScreenAsync($"home-library-loading-{width}-screen.png");
            Require(home.ViewModel.Busy && Math.Abs(during - before) < 1,
                "媒体库加载条改变主页列表位置");
            var overlay = Get<Grid>(home, "Overlay");
            var bar = overlay.Children.OfType<ProgressBar>().Single();
            var barTop = bar.TransformToVisual(overlay).TransformPoint(default).Y;
            Require(bar.Visibility == Visibility.Visible && bar.ActualHeight > 0
                && barTop >= 0 && barTop + bar.ActualHeight <= overlay.Padding.Top,
                "媒体库加载提示必须显示在既有顶部留白内");
        }
        finally
        {
            gate.TrySetResult();
            await read;
            fixture.Transport.Reply = reply;
        }
        await LayoutAsync(home);
        Require(Math.Abs(repeater.TransformToVisual(home).TransformPoint(default).Y - before) < 1,
            "媒体库刷新结束后主页列表发生回落");
    }

    private sealed class HomeOrderTransport(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal bool Offline { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Offline ? throw new HttpRequestException("离线夹具断线") : base.SendAsync(request, cancellationToken);
    }
}
