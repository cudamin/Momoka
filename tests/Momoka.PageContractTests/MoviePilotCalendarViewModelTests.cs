using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotCalendarViewModelTests
{
    internal static void Register()
    {
        Test("MP 日历视图模型：跨年翻月、返回今天和日期选择使用当前月", () => Run(async () =>
        {
            using var fixture = await Fixture.Create();
            using var model = new MoviePilotCalendarViewModel();
            model.Attach(fixture.Service, fixture.Settings.MoviePilot);
            var original = model.Month;
            var delta = (2026 - original.Year) * 12 + 12 - original.Month;
            model.MoveMonth(delta);
            Assert.Equal(new DateOnly(2026, 12, 1), model.Month);
            Assert.Equal(model.Month, model.SelectedDate);
            model.MoveMonth(1);
            Assert.Equal(new DateOnly(2027, 1, 1), model.Month);
            model.SelectDate(new DateOnly(2027, 1, 20));
            Assert.Equal(new DateOnly(2027, 1, 20), model.SelectedDate);
            model.Today();
            var today = DateOnly.FromDateTime(DateTime.Today);
            Assert.Equal(today, model.SelectedDate);
            Assert.Equal(new DateOnly(today.Year, today.Month, 1), model.Month);
            Assert.True(fixture.Transport.CalendarReads >= 4);
        }));

        Test("MP 日历视图模型：旧月份迟到不能覆盖新月份", () => Run(async () =>
        {
            using var fixture = await Fixture.Create();
            using var model = new MoviePilotCalendarViewModel();
            model.Attach(fixture.Service, fixture.Settings.MoviePilot);
            var oldMonth = model.Month;
            fixture.Transport.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = model.ReloadAsync();
            Assert.True(model.Busy);
            Assert.False(old.IsCompleted);
            fixture.Transport.Hold = null;
            model.MoveMonth(1);
            await model.ReloadAsync();
            var current = model.Entries.Single();
            Assert.True(current.Date >= oldMonth && current.Date < oldMonth.AddMonths(2));
            Assert.Equal(oldMonth.AddMonths(1), model.Month);
            fixture.Transport.ReleaseHeld();
            await old;
            Assert.True(ReferenceEquals(current, model.Entries.Single()), "旧月份回话改写了新窗口");
            Assert.False(model.Busy);
        }));

        Test("MP 日历视图模型：离页取消和关闭入口拒绝迟到回填", () => Run(async () =>
        {
            foreach (var closePreference in new[] { false, true })
            {
                using var fixture = await Fixture.Create();
                using var model = new MoviePilotCalendarViewModel();
                model.Attach(fixture.Service, fixture.Settings.MoviePilot);
                fixture.Transport.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var old = model.ReloadAsync();
                Assert.False(old.IsCompleted);
                if (closePreference) fixture.Settings.MoviePilot.CalendarEnabled = false;
                else model.Release();
                fixture.Transport.ReleaseHeld();
                await old;
                Assert.Equal(0, model.Entries.Count);
                Assert.False(model.Busy);
                Assert.False(model.NoticeOpen, "取消不应报读取失败");
            }
        }));

        Test("MP 日历视图模型：配置 A-B-A 后空响应也不能归属旧加载", () => Run(async () =>
        {
            using var fixture = await Fixture.Create();
            using var model = new MoviePilotCalendarViewModel();
            model.Attach(fixture.Service, fixture.Settings.MoviePilot);
            fixture.Transport.Empty = true;
            fixture.Transport.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = model.ReloadAsync();
            Assert.False(old.IsCompleted);
            var original = fixture.Settings.MoviePilot.Username;
            fixture.Settings.MoviePilot.Username = "replacement";
            fixture.Settings.MoviePilot.Username = original;
            fixture.Transport.Hold = null;
            Assert.True(await fixture.Service.RefreshAvailabilityAsync());
            fixture.Transport.ReleaseHeld();
            await old;
            Assert.Equal(0, model.Entries.Count);
            Assert.False(model.NoticeOpen, "连接交接不应把旧读数当作失败");
            model.Release();
            var reads = fixture.Transport.CalendarReads;
            fixture.Settings.MoviePilot.CalendarEnabled = false;
            fixture.Settings.MoviePilot.CalendarEnabled = true;
            await model.ReloadAsync();
            Assert.Equal(reads, fixture.Transport.CalendarReads, "释放后失去服务归属，不应自行重启读取");
            model.Attach(fixture.Service, fixture.Settings.MoviePilot);
            await model.ReloadAsync();
            Assert.True(model.IsReady);
            Assert.True(fixture.Transport.CalendarReads > reads, "重新进入应重新获取归属");
        }));

        Test("MP 日历视图模型：先显示本地缓存，再用网络结果替换", () => Run(async () =>
        {
            var directory = Directory.CreateTempSubdirectory("momoka-calendar-vm-cache").FullName;
            try
            {
                using var model = new MoviePilotCalendarViewModel();
                var (start, end) = MoviePilotCalendarLayout.Window(model.Month);
                var cache = new MoviePilotCalendarCache(Path.Combine(directory, "calendar.json"));
                cache.Write("https://fixture.invalid:3001/\nfixture", start, end,
                    [new MoviePilotCalendarEntry { Date = start, SubscriptionId = 7, Title = "本地缓存电影", Type = "电影" }]);

                using var fixture = await Fixture.Create(cache);
                fixture.Transport.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                model.Attach(fixture.Service, fixture.Settings.MoviePilot);
                Assert.Equal("本地缓存电影", model.Entries.Single().Title);
                Assert.True(model.Busy, "缓存上屏后仍在读取最新安排");

                fixture.Transport.ReleaseHeld();
                Assert.True(SpinWait.SpinUntil(() => !model.Busy, TimeSpan.FromSeconds(15)), "最新安排未落屏");
                Assert.Equal("匿名电影", model.Entries.Single().Title);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }));

        Test("MP 日历视图模型：可读空态与失败分开，重新加载恢复", () => Run(async () =>
        {
            using var fixture = await Fixture.Create();
            using var model = new MoviePilotCalendarViewModel();
            fixture.Transport.CalendarStatus = HttpStatusCode.Forbidden;
            model.Attach(fixture.Service, fixture.Settings.MoviePilot);
            Assert.True(model.IsReady);
            Assert.True(model.NoticeOpen);
            Assert.Contains("权限", model.NoticeMessage);
            fixture.Transport.CalendarStatus = HttpStatusCode.OK;
            fixture.Transport.Empty = true;
            await model.ReloadAsync();
            Assert.True(model.IsReady);
            Assert.False(model.NoticeOpen);
            Assert.Equal(0, model.Entries.Count);
            fixture.Transport.Empty = false;
            await model.ReloadAsync();
            Assert.Equal(1, model.Entries.Count);
            model.MediaType = 2;
            Assert.Equal(0, model.Entries.Count);
            model.MediaType = 0;
            Assert.Equal(1, model.Entries.Count);
        }));
    }

    private static void Run(Func<Task> test) => test().GetAwaiter().GetResult();

    private sealed class Fixture : IDisposable
    {
        internal AppSettings Settings { get; } = new();
        internal CalendarTransport Transport { get; } = new();
        private MoviePilotClient Client { get; }
        internal MoviePilotService Service { get; }

        private Fixture(MoviePilotCalendarCache? cache)
        {
            Settings.MoviePilot.Enabled = true;
            Settings.MoviePilot.Url = "https://fixture.invalid";
            Settings.MoviePilot.Username = "fixture";
            var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
            credentials.SetPassword(Settings.MoviePilot, "fixture");
            Client = new MoviePilotClient(Transport);
            Service = new MoviePilotService(Client, credentials, Settings, calendarCache: cache);
        }

        internal static async Task<Fixture> Create(MoviePilotCalendarCache? cache = null)
        {
            var fixture = new Fixture(cache);
            Assert.True(await fixture.Service.RefreshAvailabilityAsync());
            return fixture;
        }

        public void Dispose()
        {
            Service.Dispose();
            Client.Dispose();
            Transport.Dispose();
        }
    }

    // Only the HTTP response boundary is replaced; production client, service and VM own cancellation.
    private sealed class CalendarTransport : HttpMessageHandler
    {
        internal TaskCompletionSource? Hold { get; set; }
        private TaskCompletionSource? _held;
        internal int CalendarReads { get; private set; }
        internal bool Empty { get; set; }
        internal HttpStatusCode CalendarStatus { get; set; } = HttpStatusCode.OK;
        internal void ReleaseHeld() => _held?.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            string body;
            var status = HttpStatusCode.OK;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) body = """{"access_token":"fixture-token"}""";
            else if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) body = "{}";
            else if (path.EndsWith("subscribe/calendar", StringComparison.Ordinal))
            {
                CalendarReads++;
                status = CalendarStatus;
                var start = uri.Query.TrimStart('?').Split('&').Single(value => value.StartsWith("start=", StringComparison.Ordinal))[6..];
                body = Empty ? """{"total":0,"items":[]}""" : JsonSerializer.Serialize(new
                {
                    total = 1,
                    items = new[] { new { date = start, subscribe = new { id = 42, name = "匿名电影", type = "电影" }, episodes = Array.Empty<object>() } },
                });
                if (Hold is { } hold)
                {
                    _held = hold;
                    await hold.Task; // Deliberately ignores token to exercise a late response.
                }
            }
            else throw new AssertionException("未预期的匿名接口 " + path);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
