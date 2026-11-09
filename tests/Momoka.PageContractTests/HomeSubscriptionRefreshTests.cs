using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests
{
    internal static class HomeSubscriptionRefreshTests
    {
        internal static void Register()
        {
            Test("订阅属主首次加载后停留三分钟不再自动读取", () =>
            {
                using var client = new MoviePilotClient();
                using var service = Service(client);
                var owner = new MoviePilotHomeSubscriptions();
                try
                {
                    owner.Attach(service, null);
                    Assert.Equal(1, owner.ViewModel.Reloads);
                    for (var minute = 0; minute < 3; minute++) DispatcherTimer.Advance(TimeSpan.FromMinutes(1));
                    Assert.Equal(1, owner.ViewModel.Reloads, "停留不应触发新的订阅读取");
                }
                finally { owner.Release(); }
            });
            Test("订阅属主重新进入仍加载一次且释放会取消读取", () =>
            {
                using var client = new MoviePilotClient();
                using var service = Service(client);
                var owner = new MoviePilotHomeSubscriptions();
                owner.Attach(service, null);
                owner.Release();
                var cancelled = owner.ViewModel.Cancels;
                owner.Attach(service, null);
                Assert.Equal(2, owner.ViewModel.Reloads);
                Assert.True(owner.ViewModel.Cancels > cancelled);
                owner.Release();
                DispatcherTimer.Advance(TimeSpan.FromMinutes(2));
                Assert.Equal(2, owner.ViewModel.Reloads);
            });
            Test("主页和独立订阅页的两个属主不会相互取消", () =>
            {
                using var client = new MoviePilotClient();
                using var service = Service(client);
                var home = new MoviePilotHomeSubscriptions();
                var page = new MoviePilotHomeSubscriptions();
                try
                {
                    home.Attach(service, null);
                    page.Attach(service, null);
                    var cancelled = page.ViewModel.Cancels;
                    home.Release();
                    Assert.Equal(cancelled, page.ViewModel.Cancels);
                    DispatcherTimer.Advance(TimeSpan.FromMinutes(2));
                    Assert.Equal(1, page.ViewModel.Reloads);
                }
                finally { home.Release(); page.Release(); }
            });
        }

        private static MoviePilotService Service(MoviePilotClient client) => new(client,
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), new AppSettings());
    }
}

namespace Microsoft.UI.Xaml
{
    // Inert scheduling boundary: production owns registration/start/stop, tests advance elapsed time.
    public sealed class DispatcherTimer
    {
        private static readonly List<DispatcherTimer> Active = [];
        public TimeSpan Interval { get; set; }
        public event EventHandler<object>? Tick;
        public void Start() { if (!Active.Contains(this)) Active.Add(this); }
        public void Stop() => Active.Remove(this);
        internal static void Advance(TimeSpan elapsed)
        {
            foreach (var timer in Active.ToArray())
                if (elapsed >= timer.Interval) timer.Tick?.Invoke(timer, new object());
        }
    }
}
