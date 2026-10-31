using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Tests
{
    internal static class SubscriptionNavigationTests
    {
        internal static void Register(List<(string Name, Func<Task> Run)> cases)
        {
            void Test(string name, Func<Task> run) => cases.Add(("订阅成功导航 " + name, run));
            Test("电影新增后跳转一次，取消订阅只刷新", async () =>
            {
                var page = new MoviePilotSubscriptionPage();
                page.Setup("电影"); await page.Choose();
                Check.Equal(1, page.Navigations); Check.Equal(0, page.Refreshes);
                page.Setup("电影", existing: true); await page.Choose();
                Check.Equal(0, page.Navigations); Check.Equal(1, page.Refreshes);
            });
            Test("电视剧真正新增后跳转，取消弹窗与删除季不跳", async () =>
            {
                var page = new MoviePilotSubscriptionPage(); page.Setup("电视剧");
                page.Service.Pick = _ => { page.Service.Items = [page.Subscription(8)]; return Task.FromResult(true); };
                await page.Choose(); Check.Equal(1, page.Navigations);
                page.Setup("电视剧"); page.Service.Pick = _ => Task.FromResult(false);
                await page.Choose(); Check.Equal(0, page.Navigations); Check.Equal(1, page.Refreshes);
                page.Setup("电视剧", existing: true);
                page.Service.Pick = _ => { page.Service.Items = []; return Task.FromResult(true); };
                await page.Choose(); Check.Equal(0, page.Navigations); Check.Equal(1, page.Refreshes);
            });
            Test("写入失败与页面离开不误跳转", async () =>
            {
                var page = new MoviePilotSubscriptionPage(); page.Setup("电影");
                page.Service.Apply = _ => Task.FromException(new InvalidOperationException("匿名拒绝"));
                await page.Choose(); Check.Equal(0, page.Navigations); Check.Equal(1, page.Failures);
                page.Setup("电影"); var hold = new TaskCompletionSource();
                page.Service.Apply = _ => hold.Task;
                var choosing = page.Choose(); page.Leave(); hold.SetResult(); await choosing;
                Check.Equal(0, page.Navigations); Check.Equal(0, page.Refreshes);
            });
            Test("连接替换与重复点击不产生错误跳转或第二次提交", async () =>
            {
                var page = new MoviePilotSubscriptionPage(); page.Setup("电影");
                var hold = new TaskCompletionSource(); page.Service.Apply = _ => hold.Task;
                var choosing = page.Choose(); await page.Choose(); Check.Equal(1, page.Service.Applies);
                page.Service.CurrentConnection = false; hold.SetResult(); await choosing;
                Check.Equal(0, page.Navigations);
            });
        }
    }
}

namespace Momoka.Shell.Views
{
    // UI and IO endpoint boundaries. ChooseSeasonsAsync is extracted verbatim from production at build time.
    public sealed partial class MoviePilotSubscriptionPage
    {
        private SubscriptionPageRequest? _request;
        private CancellationTokenSource? _pageLifetime;
        internal SubscriptionPageFixtureService Service { get; private set; } = new();
        internal SubscriptionMediaBoundary MediaViewModel { get; } = new();
        internal int Navigations { get; private set; }
        internal int Refreshes { get; private set; }
        internal int Failures => MediaViewModel.Failures;
        internal void Setup(string type, bool existing = false)
        {
            _pageLifetime?.Dispose(); _pageLifetime = new();
            Service = new(); Navigations = Refreshes = 0; MediaViewModel.Failures = 0;
            MediaViewModel.Detail = new(new MoviePilotMedia { Title = "匿名", Type = type, MediaSource = "themoviedb", MediaId = "42", ConnectionStamp = "fixture" });
            Service.Items = existing ? [Subscription(7)] : [];
            MediaViewModel.Seasons = Service.Items.Select(item => new SubscriptionSeasonBoundary(item)).ToArray();
            _request = new(Service, _ => Navigations++);
        }
        internal MoviePilotSubscription Subscription(int id) => new()
        {
            Id = id,
            Media = MediaViewModel.Detail!.Media,
            ConnectionStamp = "fixture",
            Data = System.Text.Json.JsonSerializer.SerializeToElement(new { id })
        };
        internal Task Choose() => ChooseSeasonsAsync(null);
        internal void Leave() { _request = null; _pageLifetime?.Cancel(); }
        private Task RefreshSubscriptionStateAsync() { Refreshes++; return Task.CompletedTask; }
    }
    internal sealed record SubscriptionPageRequest(SubscriptionPageFixtureService Service, Action<MoviePilotMedia>? Subscribed);
    public sealed class SubscriptionPageFixtureService
    {
        public bool Available { get; set; } = true;
        public bool CurrentConnection { get; set; } = true;
        public IReadOnlyList<MoviePilotSubscription> Items { get; set; } = [];
        public Func<CancellationToken, Task> Apply { get; set; } = _ => Task.CompletedTask;
        public Func<CancellationToken, Task<bool>> Pick { get; set; } = _ => Task.FromResult(false);
        public int Applies { get; private set; }
        public bool IsCurrentConnection(string stamp) => CurrentConnection && stamp == "fixture";
        public Task<IReadOnlyList<MoviePilotSubscription>> SubscriptionsAsync(CancellationToken token) => Task.FromResult(Items);
        public Task ApplyMovieSubscriptionAsync(MoviePilotMedia media, MoviePilotSubscription? existing, CancellationToken token)
        { Applies++; return Apply(token); }
    }
    internal sealed record SubscriptionMediaDetailBoundary(MoviePilotMedia Media);
    internal sealed record SubscriptionSeasonBoundary(MoviePilotSubscription? CurrentSubscription);
    internal sealed class SubscriptionMediaBoundary
    {
        public bool CanInteract { get; set; } = true;
        public SubscriptionMediaDetailBoundary? Detail { get; set; }
        public IReadOnlyList<SubscriptionSeasonBoundary> Seasons { get; set; } = [];
        public string GroupId => "";
        public int Failures { get; set; }
        public void OperationFailed(Exception error) => Failures++;
    }
}
