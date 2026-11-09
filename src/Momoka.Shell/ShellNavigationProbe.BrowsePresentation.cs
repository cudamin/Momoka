using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task PerformanceBrowsePresentationAsync()
    {
        var samples = new List<object>();
        var visit = Guid.NewGuid().ToString("N");
        foreach (var width in new[] { 1280, 760 })
        {
            using var fixture = CreateFixture();
            await fixture.SelectAsync();
            fixture.Session.TakeRestoredViews();
            fixture.Settings.Ui.ShowHomeBanner = true;
            fixture.Settings.Ui.CarouselCount = 1;
            var picture = await FixtureImageAsync(1280, 720);
            var library = fixture.Session.Capture().Bind(new EmbyItem
            {
                Id = "presentation-library-" + width + "-" + visit,
                Name = "匿名首帧媒体库",
                Type = EmbyItemType.CollectionFolder,
                CollectionType = "movies"
            });
            // 两种媒体库容器都走生产入口；窄窗直接以列表偏好导航，不能先让Poster预取再换模板。
            var listView = width == 760;
            if (listView) fixture.Settings.Ui.Display[BrowsingPreferences.LibraryKey(fixture.Session.Capture().Connection, library.Id)]
                = new LibraryDisplay { View = LibraryView.List };
            fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, [library], true));
            foreach (var row in fixture.Settings.Ui.HomeRows) row.Visible = row.Key == HomeLayout.Libraries;
            var film = fixture.Session.Capture().Bind(new EmbyItem
            {
                Id = "presentation-film-" + width + "-" + visit,
                Name = "首帧文字先显示",
                Type = EmbyItemType.Movie,
                Overview = "图片尚未返回时，这段文字已经可以阅读。",
                ImageTags = new() { [EmbyImageStore.Primary] = "poster" },
                BackdropImageTags = ["backdrop"]
            });
            var libraryFilm = fixture.Session.Capture().Bind(new EmbyItem
            {
                Id = "presentation-library-film-" + width + "-" + visit,
                Name = "库页晚到海报",
                Type = EmbyItemType.Movie,
                ImageTags = new() { [EmbyImageStore.Primary] = "library-poster" }
            });
            var detailFilm = fixture.Session.Capture().Bind(new EmbyItem
            {
                Id = "presentation-detail-film-" + width + "-" + visit,
                Name = "详情晚到背景",
                Type = EmbyItemType.Movie,
                Overview = "详情文字先显示，图片在页面动画开始后再进入。",
                ImageTags = new() { [EmbyImageStore.Primary] = "detail-poster" },
                BackdropImageTags = ["detail-backdrop"]
            });
            var holds = new List<TaskCompletionSource<bool>>();
            var imageGate = Pending<bool>();
            holds.Add(imageGate);
            var imageCalls = 0;
            var requestObservations = new List<object>();
            var prematureRequests = 0;
            var observingRequests = true;
            var activeImageRun = "";
            var sink = new BrowseTimingSink();
            fixture.Transport.Reply = async (request, token) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/Images/", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref imageCalls);
                    var run = activeImageRun;
                    var released = sink.Has(run, "browse-images-released");
                    if (observingRequests && !released) Interlocked.Increment(ref prematureRequests);
                    lock (requestObservations) requestObservations.Add(new
                    {
                        stage = "image-request",
                        run,
                        gateReleased = released,
                        item = path.Split('/').SkipWhile(part => part != "Items").Skip(1).FirstOrDefault(),
                        type = path.Split('/').LastOrDefault(),
                        at = DateTimeOffset.UtcNow
                    });
                    var release = imageGate;
                    await release.Task.WaitAsync(token);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) };
                }
                if (path.EndsWith("/Views", StringComparison.Ordinal)) return Json(new ItemsResult { Items = [library], TotalRecordCount = 1 });
                if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Json(new[] { film });
                if (path.EndsWith("/Items/" + film.Id, StringComparison.Ordinal)) return Json(film);
                if (path.EndsWith("/Items/" + detailFilm.Id, StringComparison.Ordinal)) return Json(detailFilm);
                if (path.EndsWith("/Items/presentation-stale-" + width, StringComparison.Ordinal)) return Json(new EmbyItem
                {
                    Id = "presentation-stale-" + width + "-" + visit,
                    Name = "离页图片仍属于旧请求",
                    Type = EmbyItemType.Movie,
                    BackdropImageTags = ["stale-backdrop"],
                    ImageTags = new() { [EmbyImageStore.Primary] = "stale-poster" }
                });
                if (path.EndsWith("/Items", StringComparison.Ordinal)) return Json(new ItemsResult { Items = [libraryFilm], TotalRecordCount = 1 });
                return FakeTransport.Standard(request);
            };
            var shell = new ShellPage();
            shell.Attach(fixture.Services);
            _root.Children.Add(shell);
            var observations = new List<object>();
            var observedAtLoaded = "Home Loaded尚未发生";
            var completed = false;
            NavigationTiming.Start(new NavigationTimingRecorder(sink));
            var entrance = HomeMotion.PreferenceEnabled;
            try
            {
                ResizeProbeClient(width, 900);
                HomeMotion.ApplyPreference(true);
                await LayoutAsync(shell);
                var frame = shell.Pages;
                var pendingCheck = Pending<bool>();
                var observed = false;
                void Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
                {
                    if (observed || frame.Content is not HomePage page) return;
                    observed = true;
                    void Check()
                    {
                        var presenter = Get<object>(shell, "_browsePresentation");
                        var images = Get<TaskCompletionSource?>(presenter, "_images");
                        var gatePending = images is { Task.IsCompleted: false };
                        var requests = Volatile.Read(ref imageCalls);
                        var banner = Get<HomeBanner>(page, "Banner");
                        var title = banner.Current?.Title;
                        var slides = page.ViewModel.Slides.Count;
                        observedAtLoaded = $"gatePresent={images is not null};gatePending={gatePending};imageCalls={requests};"
                            + $"title={title ?? "<empty>"};slides={slides};isReady={page.IsReady};pageLoaded={page.IsLoaded}";
                        observations.Add(new
                        {
                            stage = "home-loaded",
                            gatePresent = images is not null,
                            gatePending,
                            imageCalls = requests,
                            title,
                            slides,
                            isReady = page.IsReady,
                            pageLoaded = page.IsLoaded
                        });
                        pendingCheck.TrySetResult(gatePending && requests == 0);
                    }
                    if (page.IsLoaded) Check();
                    else
                    {
                        RoutedEventHandler? loaded = null;
                        loaded = (_, _) => { page.Loaded -= loaded; Check(); };
                        page.Loaded += loaded;
                    }
                }
                frame.Navigated += Navigated;
                var homeRun = NavigationTiming.Click("probe-browse-home")!;
                activeImageRun = homeRun.RunId;
                try
                {
                    await (Task)Call(shell, "EnterShellAsync")!;
                    Require(await pendingCheck.Task.WaitAsync(TimeSpan.FromSeconds(3)),
                        "真实Home Loaded时首帧门控或图片请求前置不成立：" + observedAtLoaded);
                }
                finally { frame.Navigated -= Navigated; }
                var home = (HomePage)frame.Content;
                await UntilAsync(() => sink.Has(homeRun.RunId, "browse-images-released") && Volatile.Read(ref imageCalls) > 0);
                AssertPresentationStages(homeRun.RunId, enabled: true);
                Require(Volatile.Read(ref prematureRequests) == 0, "主页图片请求早于首帧放行");
                await UntilAsync(() => home.IsLoaded && Get<HomeBanner>(home, "Banner").Current?.Title == film.Name);
                observations.Add(new
                {
                    stage = "home-before-response-release",
                    title = Get<HomeBanner>(home, "Banner").Current?.Title,
                    slides = home.ViewModel.Slides.Count,
                    isReady = home.IsReady,
                    imageCalls = Volatile.Read(ref imageCalls),
                    responseHeld = !imageGate.Task.IsCompleted
                });
                Require(Get<HomeBanner>(home, "Banner").FrontPicture is null, "匿名图片响应挂起时已有背景图");
                await SaveProbeScreenAsync($"browse-home-text-before-images-{width}-screen.png");
                imageGate.TrySetResult(true);
                await UntilAsync(() => Get<HomeBanner>(home, "Banner").FrontPicture is not null
                    && Property<bool>(Get<HomeBanner>(home, "Banner"), "ImageArrivalRunning"));
                await UntilAsync(() => !Property<bool>(Get<HomeBanner>(home, "Banner"), "ImageArrivalRunning"));
                await SaveProbeScreenAsync($"browse-home-images-settled-{width}-screen.png");

                imageGate = Pending<bool>();
                holds.Add(imageGate);
                var libraryRun = NavigationTiming.Click("probe-browse-library")!;
                activeImageRun = libraryRun.RunId;
                shell.OpenChild(new LibraryRequest
                {
                    Services = fixture.Services,
                    Source = fixture.Session.Capture(),
                    ParentId = library.Id,
                    CollectionType = "movies",
                    Title = library.Name
                });
                await UntilAsync(() => frame.Content is LibraryPage page && page.IsReady
                    && sink.Has(libraryRun.RunId, "browse-images-released"));
                AssertPresentationStages(libraryRun.RunId, enabled: true);
                Require(Volatile.Read(ref prematureRequests) == 0, "存在图片请求早于所属页面首帧放行");
                var libraryPage = (LibraryPage)frame.Content;
                await LayoutAsync(libraryPage);
                Require(libraryPage.ViewModel.Cards.Single().Poster is null, "匿名海报响应挂起时已有新图");
                imageGate.TrySetResult(true);
                await UntilAsync(() => listView
                    ? Descendants(libraryPage).OfType<EpisodeRow>().Any(row => row.Card?.Item.Id == libraryFilm.Id && row.PosterArrivalRunning)
                    : Descendants(libraryPage).OfType<PosterCard>().Any(card => card.Card?.Item.Id == libraryFilm.Id && card.PosterArrivalRunning));
                await UntilAsync(() => listView
                    ? Descendants(libraryPage).OfType<EpisodeRow>().Where(row => row.Card?.Item.Id == libraryFilm.Id).All(row => !row.PosterArrivalRunning)
                    : Descendants(libraryPage).OfType<PosterCard>().Where(card => card.Card?.Item.Id == libraryFilm.Id).All(card => !card.PosterArrivalRunning));
                await SaveProbeScreenAsync($"browse-library-images-settled-{width}-screen.png");

                imageGate = Pending<bool>();
                holds.Add(imageGate);
                var detailRun = NavigationTiming.Click("probe-browse-detail")!;
                activeImageRun = detailRun.RunId;
                shell.OpenDetail(DetailRequest.For(fixture.Services, detailFilm));
                await UntilAsync(() => frame.Content is DetailPage page && page.IsReady
                    && sink.Has(detailRun.RunId, "browse-images-released"));
                AssertPresentationStages(detailRun.RunId, enabled: true);
                Require(Volatile.Read(ref prematureRequests) == 0, "详情图片请求早于首帧放行");
                var detail = (DetailPage)frame.Content;
                Require(detail.ViewModel.Title == detailFilm.Name, "详情文字没有先上屏");
                imageGate.TrySetResult(true);
                await UntilAsync(() => Property<bool>(detail, "ArtworkArrivalRunning"));
                await UntilAsync(() => !Property<bool>(detail, "ArtworkArrivalRunning"));

                // A held old image flow must remain cancelled after quick Back/Forward and a new gate.
                observingRequests = false;
                var stale = fixture.Session.Capture().Bind(new EmbyItem
                {
                    Id = "presentation-stale-" + width,
                    Name = "离页图片仍属于旧请求",
                    Type = EmbyItemType.Movie,
                    BackdropImageTags = ["stale-backdrop"],
                    ImageTags = new() { [EmbyImageStore.Primary] = "stale-poster" }
                });
                imageGate = Pending<bool>();
                holds.Add(imageGate);
                activeImageRun = NavigationTiming.Click("probe-browse-stale")!.RunId;
                shell.OpenDetail(DetailRequest.For(fixture.Services, stale));
                var old = (DetailPage)frame.Content;
                Call(shell, "GoBack");
                await UntilAsync(() => !old.IsLoaded);
                imageGate.TrySetResult(true);
                Require(!Property<bool>(old, "ArtworkArrivalRunning"), "离页旧图片流重新开始淡入");
                Call(shell, "GoForward");
                await UntilAsync(() => frame.Content is DetailPage);
                Call(shell, "GoBack");
                HomeMotion.ApplyPreference(false);
                var offRun = NavigationTiming.Click("probe-browse-off")!;
                activeImageRun = offRun.RunId;
                shell.OpenChild(new LibraryRequest
                {
                    Services = fixture.Services,
                    Source = fixture.Session.Capture(),
                    ParentId = library.Id,
                    CollectionType = "movies",
                    Title = "关闭动画仍呈现内容"
                });
                await UntilAsync(() => sink.Has(offRun.RunId, "browse-images-released"));
                AssertPresentationStages(offRun.RunId, enabled: false);
                Require(frame.Opacity == 1, "关闭动画后页面仍透明");
                Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)),
                    "首帧匿名检查发生播放或服务写入");
                samples.Add(new
                {
                    width,
                    homeRun = homeRun.RunId,
                    libraryRun = libraryRun.RunId,
                    detailRun = detailRun.RunId,
                    imageCalls,
                    listView,
                    textBeforeImages = true,
                    lateImageFade = true,
                    events = sink.Snapshot(),
                    realNetwork = false,
                    playback = false
                });
                completed = true;

                void AssertPresentationStages(string run, bool enabled)
                {
                    var stages = sink.Snapshot().Where(entry => entry.RunId == run).Select(entry => entry.Stage).ToArray();
                    var first = Array.IndexOf(stages, "browse-first-frame");
                    var released = Array.IndexOf(stages, "browse-images-released");
                    Require(first >= 0 && released > first, "图片放行早于真实首帧：" + string.Join(',', stages));
                    var motion = Array.IndexOf(stages, "browse-motion-start");
                    Require(enabled ? motion >= 0 && motion < first : motion < 0,
                        "页面动画没有按本次偏好在首帧前开始：" + string.Join(',', stages));
                    var waiting = Array.IndexOf(stages, "browse-images-wait");
                    Require(waiting < 0 || waiting < released,
                        "图片等待事件落在放行之后：" + string.Join(',', stages));
                }
            }
            finally
            {
                try
                {
                    await WritePerformanceEvidenceAsync($"browse-presentation-observations-{width}.json", new
                    {
                        width,
                        completed,
                        observedAtLoaded,
                        imageCalls = Volatile.Read(ref imageCalls),
                        prematureRequests = Volatile.Read(ref prematureRequests),
                        observations,
                        requestObservations = RequestsSnapshot(),
                        events = sink.Snapshot(),
                        realNetwork = false,
                        playback = false
                    });
                }
                finally
                {
                    foreach (var release in holds) release.TrySetResult(true);
                    Call(shell, "ReleaseContent");
                    shell.SignInRoot.Detach();
                    _root.Children.Remove(shell);
                    NavigationTiming.Stop();
                    HomeMotion.ApplyPreference(entrance);
                }
            }
            object[] RequestsSnapshot() { lock (requestObservations) return requestObservations.ToArray(); }
        }
        await WritePerformanceEvidenceAsync("browse-presentation-runtime.json", samples);
        static T Property<T>(object target, string name) => (T)target.GetType()
            .GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;
    }

    private sealed class BrowseTimingSink : INavigationTimingSink
    {
        private readonly List<NavigationTimingEvent> _events = [];
        public void Write(NavigationTimingEvent entry) { lock (_events) _events.Add(entry); }
        internal NavigationTimingEvent[] Snapshot() { lock (_events) return _events.ToArray(); }
        internal bool Has(string run, string stage) => Snapshot().Any(entry => entry.RunId == run && entry.Stage == stage);
    }
}
