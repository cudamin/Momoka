using System.Net;
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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeOrderAsync()
    {
        var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        var transport = new HomeOrderTransport(server);
        using var fixture = CreateFixture(transport);
        await fixture.SelectAsync();
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
                Require(list.Items.Count == 6 && Descendants(list).OfType<CheckBox>()
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
            choices[0].DownCommand.Execute(null);
            var hidden = HomeLayout.Save(HomeLayout.Plan(fixture.Settings.Ui.HomeRows, null));
            await LayoutAsync(settings);
            await SaveProbeScreenAsync("settings-home-order-offline.png");
            var hiddenList = Descendants(settings).OfType<ListView>().Single(view => ReferenceEquals(view.ItemsSource, choices));
            Require(hiddenList.Items.Count == 4 && Math.Abs(hiddenList.ActualHeight - table.ListHeight) < 1,
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
            await UntilAsync(() => choices.Count == 6);
            Require(ReferenceEquals(choices, table.Rows) && !choices.Single(row => row.Key == HomeLayout.MoviePilotMovies).Visible
                && HomeLayout.Same(hidden, HomeLayout.Plan(fixture.Settings.Ui.HomeRows, null)), "恢复连接丢失原次序或勾选");
            settings.ReleaseHosted();
            BeginSettingsFixtureVisit(settings);
            settings.WatchMoviePilot(service);
            await UntilAsync(() => choices.Count == 6);
            Require(ReferenceEquals(table, settings.ViewModel.HomeRows)
                && ReferenceEquals(choices, table.Rows) && choices.Count == 6,
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
                Require(fixture.Settings.Ui.HomeRows.Where(row => row.Visible).Select(row => row.Key).SequenceEqual(keys), "主页未采用排序表次序");
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
            await UntilAsync(() => choices.Count == 4);
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
            await UntilAsync(() => choices.Count == 6);
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

    private sealed class HomeOrderTransport(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal bool Offline { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Offline ? throw new HttpRequestException("离线夹具断线") : base.SendAsync(request, cancellationToken);
    }
}
