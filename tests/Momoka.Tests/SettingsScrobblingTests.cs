using System.Collections;
using System.Net;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.Scrobbling;

namespace Momoka.Tests;

/// <summary>Actual settings models with memory settings: no window, network or credential IO.</summary>
internal static class SettingsScrobblingTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string NotificationLabel = "打格子失败时发送系统通知";

    internal static void RegisterState(SettingsPageStateTests.ShellFixture fixture)
    {
        TestHarness.Test("打格子设置：默认两张卡各只有总开关，建卡不写配置", () =>
        {
            using var state = new State(fixture);
            Assert.Equal(3, state.Sections.Length);
            Assert.Equal("Bangumi 打格子", fixture.Get<string>(state.Sections[0], "Title"));
            Assert.Equal("豆瓣自动标记在看 / 已看", fixture.Get<string>(state.Sections[1], "Title"));
            foreach (var section in state.Sections.Take(2))
            {
                var rows = fixture.List(section, "Rows");
                Assert.Equal(1, rows.Count);
                Assert.Equal("SettingToggleRow", rows[0]!.GetType().Name);
                Assert.False(fixture.Get<bool>(rows[0]!, "IsOn"));
            }
            Assert.True(fixture.List(state.Vm, "Categories").Cast<object>()
                .Any(entry => fixture.Get<string>(entry, "Label") == "打格子"));
            fixture.Set(state.Vm, "SelectedCategory", "打格子");
            Assert.Equal("Collapsed", fixture.Get<object>(state.Sections[2], "SectionVisibility").ToString());
            Assert.Equal(0, fixture.List(state.Sections[2], "Rows").Count);
            Assert.Equal(0, state.Settings.TrySaveCalls);
        });
        TestHarness.Test("打格子设置：任一站开启时显示通知开关，拨动即时保存且不断开当前连接", () =>
        {
            using var state = new State(fixture);
            using var bangumi = new BangumiClient();
            using var service = new ScrobblingService(state.Settings.Settings, PassthroughSecretProtector.Instance,
                bangumi, new StatusDouban());
            var checks = 0;
            fixture.Invoke(state.Vm, "UseScrobbling", service, (Func<Task>)(() => Task.CompletedTask),
                (Func<Task>)(() => Task.CompletedTask), (Func<Task>)(() => { checks++; return Task.CompletedTask; }));
            var bangumiToggle = state.Row("启用 Bangumi 打格子");
            var doubanToggle = state.Row("启用豆瓣自动标记");
            fixture.Set(doubanToggle, "IsOn", true);
            service.CheckConnectionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            var row = state.Row(NotificationLabel);
            Assert.False(fixture.Get<bool>(row, "IsOn"));
            fixture.Set(row, "IsOn", true);
            Assert.True(state.Settings.Settings.Scrobbling.NotifyOnFailure);
            Assert.Equal(2, state.Settings.TrySaveCalls);
            Assert.Equal(1, checks);
            Assert.Equal(ScrobblingConnectionState.Connected, service.DoubanConnection);

            fixture.Set(doubanToggle, "IsOn", false);
            Assert.False(state.ContainsRow(NotificationLabel));
            fixture.Set(row, "IsOn", false);
            Assert.Equal(3, state.Settings.TrySaveCalls);
            Assert.True(state.Settings.Settings.Scrobbling.NotifyOnFailure);
            fixture.Set(bangumiToggle, "IsOn", true);
            Assert.True(fixture.Get<bool>(state.Row(NotificationLabel), "IsOn"));
            Assert.Equal(4, state.Settings.TrySaveCalls);
        });
        TestHarness.Test("打格子设置：开关原地收放，Bangumi 密钥草稿跨关闭和重新启用保留", () =>
        {
            using var state = new State(fixture);
            var cards = fixture.List(state.Vm, "Sections");
            var rows = fixture.List(state.Sections[0], "Rows");
            var toggle = rows[0]!;
            fixture.Set(toggle, "IsOn", true);
            var secret = state.Row("Bangumi Access Token");
            fixture.Set(secret, "Draft", "uncommitted-scrobbling-placeholder");
            fixture.Invoke(state.Vm, "RefreshScrobblingRows");
            Assert.True(ReferenceEquals(secret, state.Row("Bangumi Access Token")));
            fixture.Set(toggle, "IsOn", false);
            Assert.Equal(1, rows.Count);
            fixture.Command(secret, "CommitCommand").Execute(null);
            fixture.Command(secret, "ClearCommand").Execute(null);
            Assert.Equal("", state.Settings.Settings.Scrobbling.Bangumi.ProtectedAccessToken);
            fixture.Set(toggle, "IsOn", true);
            Assert.True(ReferenceEquals(secret, state.Row("Bangumi Access Token")));
            Assert.Equal("uncommitted-scrobbling-placeholder", fixture.Get<string>(secret, "Draft"));
            Assert.True(ReferenceEquals(cards, fixture.List(state.Vm, "Sections")));
            Assert.True(ReferenceEquals(rows, fixture.List(state.Sections[0], "Rows")));
            Assert.Equal(3, state.Settings.TrySaveCalls);
        });
        TestHarness.Test("打格子设置：自动匹配不显示手动关联入口或旧关联行", () =>
        {
            using var state = new State(fixture, bangumi: true);
            fixture.Invoke(state.Vm, "RefreshScrobblingRows");
            var rows = state.Rows();
            Assert.False(rows.Any(row => fixture.Get<string>(row, "Label") == "添加作品关联"));
            Assert.False(rows.Any(row => row.GetType().GetProperty("ActionLabel", Members) is { } property
                && property.GetValue(row) is string actionLabel && actionLabel is "添加关联" or "移除关联"));
        });
        TestHarness.Test("打格子设置：两站不显示媒体用户名，登录状态和重新验证始终可见", () =>
        {
            using var state = new State(fixture, bangumi: true);
            fixture.Set(state.Row("启用豆瓣自动标记"), "IsOn", true);
            Assert.False(state.Rows().Any(row => fixture.Get<string>(row, "Label") == "媒体服务器用户名"));
            foreach (var label in new[] { "Bangumi 连接状态", "豆瓣登录状态" })
            {
                var row = state.Row(label);
                Assert.Equal("尚未验证", fixture.Get<string>(row, "Value"));
                Assert.Equal("重新验证", fixture.Get<string>(row, "ActionLabel"));
            }
        });
        TestHarness.Test("打格子设置：重新 Attach 后密钥行只写入新设置对象", () =>
        {
            using var state = new State(fixture, bangumi: true);
            var old = state.Settings.Settings.Scrobbling.Bangumi;
            old.ProtectedAccessToken = "old-encrypted-placeholder";
            var next = new SettingsPageStateTests.FakeSettings();
            next.Settings.Scrobbling.Bangumi.Enabled = true;
            fixture.Attach(state.Vm, next, fixture.NewLauncher());
            state.Vm.GetType().GetField("_secretProtector", Members)!
                .SetValue(state.Vm, PassthroughSecretProtector.Instance);
            fixture.List(state.Vm, "Sections").Clear();
            foreach (var section in ((IEnumerable)fixture.Invoke(state.Vm, "ScrobblingSections")!).Cast<object>())
                fixture.List(state.Vm, "Sections").Add(section);
            var secret = state.Row("Bangumi Access Token");
            fixture.Set(secret, "Draft", "new-scrobbling-placeholder");
            fixture.Command(secret, "CommitCommand").Execute(null);
            Assert.Equal("old-encrypted-placeholder", old.ProtectedAccessToken);
            Assert.Equal("new-scrobbling-placeholder", next.Settings.Scrobbling.Bangumi.ProtectedAccessToken);
        });
        TestHarness.Test("打格子设置：连接状态显示真实检查结果，重验和保存密钥触发自动检查", () =>
        {
            using var state = new State(fixture, bangumi: true);
            fixture.Set(state.Row("启用豆瓣自动标记"), "IsOn", true);
            using var bangumi = new BangumiClient();
            using var service = new ScrobblingService(state.Settings.Settings, PassthroughSecretProtector.Instance, bangumi, new StatusDouban());
            var checks = 0;
            fixture.Invoke(state.Vm, "UseScrobbling", service, (Func<Task>)(() => Task.CompletedTask),
                (Func<Task>)(() => Task.CompletedTask), (Func<Task>)(() => { checks++; return Task.CompletedTask; }));
            service.CheckConnectionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            fixture.Invoke(state.Vm, "RefreshScrobblingRows");
            Assert.Equal("未配置密钥", fixture.Get<string>(state.Row("Bangumi 连接状态"), "Value"));
            Assert.Equal("已登录", fixture.Get<string>(state.Row("豆瓣登录状态"), "Value"));
            fixture.Command(state.Row("豆瓣登录状态"), "RunCommand").Execute(null);
            Assert.Equal(1, checks);
            state.Vm.GetType().GetField("_secretProtector", Members)!.SetValue(state.Vm, PassthroughSecretProtector.Instance);
            var secret = state.Row("Bangumi Access Token");
            fixture.Set(secret, "Draft", "synthetic-check-placeholder");
            fixture.Command(secret, "CommitCommand").Execute(null);
            Assert.Equal(2, checks);
        });
        TestHarness.Test("打格子设置：两张站点卡的查看记录按钮分别带出站点", () =>
        {
            using var state = new State(fixture, bangumi: true);
            fixture.Set(state.Row("启用豆瓣自动标记"), "IsOn", true);
            using var bangumi = new BangumiClient();
            using var service = new ScrobblingService(state.Settings.Settings, PassthroughSecretProtector.Instance,
                bangumi, new StatusDouban());
            var viewed = new List<ScrobblingProvider>();
            fixture.Invoke(state.Vm, "UseScrobbling", service, (Func<Task>)(() => Task.CompletedTask),
                (Func<Task>)(() => Task.CompletedTask), (Func<Task>)(() => Task.CompletedTask));
            fixture.Invoke(state.Vm, "UseScrobblingRecords", (Action<ScrobblingProvider>)(provider => viewed.Add(provider)));
            fixture.Invoke(state.Vm, "RefreshScrobblingRows");
            foreach (var label in new[] { "Bangumi 同步记录", "豆瓣同步记录" })
            {
                Assert.Equal("查看记录", fixture.Get<string>(state.Row(label), "ActionLabel"));
                Assert.Contains(label, fixture.Get<string>(state.Row(label), "ActionAutomationName"));
            }
            fixture.Command(state.Row("Bangumi 同步记录"), "RunCommand").Execute(null);
            fixture.Command(state.Row("豆瓣同步记录"), "RunCommand").Execute(null);
            Assert.Equal("Bangumi,Douban", string.Join(",", viewed));
        });
        TestHarness.Test("打格子设置：只显示本站最近结果，关闭站点后仍可查看已有记录", () =>
        {
            using var state = new State(fixture, bangumi: true);
            fixture.Set(state.Row("启用豆瓣自动标记"), "IsOn", true);
            using var playback = new PlaybackLifecycleTests.Fixture();
            playback.Transport.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            using var bangumi = new BangumiClient();
            using var service = new ScrobblingService(state.Settings.Settings, PassthroughSecretProtector.Instance,
                bangumi, new StatusDouban());
            var viewed = new List<ScrobblingProvider>();
            fixture.Invoke(state.Vm, "UseScrobbling", service, (Func<Task>)(() => Task.CompletedTask),
                (Func<Task>)(() => Task.CompletedTask), (Func<Task>)(() => Task.CompletedTask));
            fixture.Invoke(state.Vm, "UseScrobblingRecords", (Action<ScrobblingProvider>)(provider => viewed.Add(provider)));

            var scope = playback.Session.Capture();
            var episode = PlaybackLifecycleTests.Ticket("episode");
            episode.Item.Type = EmbyItemType.Episode;
            episode.Item.IndexNumber = 1;
            episode.Item.ParentIndexNumber = 1;
            episode.Item.SeriesId = "series";
            scope.Bind(episode.Item);
            var attempt = service.CreateAttempt(episode, scope, new PlaybackRequest
            {
                Title = "匿名剧集",
                MediaUrl = new Uri("https://media.invalid/episode"),
                ItemId = episode.Item.Id
            });
            Assert.NotNull(attempt);
            attempt!.FinishAsync(5, true).GetAwaiter().GetResult();
            fixture.Invoke(state.Vm, "RefreshScrobblingRows");

            var bangumiRecord = state.Row("Bangumi 同步记录");
            var doubanRecord = state.Row("豆瓣同步记录");
            Assert.Equal("尚未同步观看记录", fixture.Get<string>(bangumiRecord, "Value"));
            Assert.Equal("自动匹配读取媒体库信息失败，已跳过同步", fixture.Get<string>(doubanRecord, "Value"));
            fixture.Set(state.Row("启用豆瓣自动标记"), "IsOn", false);
            Assert.True(ReferenceEquals(doubanRecord, state.Row("豆瓣同步记录")));
            Assert.False(state.ContainsRow("豆瓣登录状态"));
            fixture.Command(doubanRecord, "RunCommand").Execute(null);
            fixture.Invoke(state.Vm, "Cancel");
            fixture.Command(doubanRecord, "RunCommand").Execute(null);
            Assert.Equal(ScrobblingProvider.Douban, viewed.Single());
        });
        TestHarness.Test("打格子设置：验证时和登录期间拒绝复点，旧登录及退出命令复查当前状态", () =>
        {
            using var state = new State(fixture);
            fixture.Set(state.Row("启用豆瓣自动标记"), "IsOn", true);
            using var bangumi = new BangumiClient();
            var douban = new StatusDouban { LoggedIn = false };
            using var service = new ScrobblingService(state.Settings.Settings, PassthroughSecretProtector.Instance, bangumi, douban);
            var logins = 0;
            var logouts = 0;
            var checks = 0;
            var pending = new TaskCompletionSource();
            fixture.Invoke(state.Vm, "UseScrobbling", service,
                (Func<Task>)(() => { logins++; return pending.Task; }),
                (Func<Task>)(() => { logouts++; return Task.CompletedTask; }),
                (Func<Task>)(() => { checks++; return Task.CompletedTask; }));
            service.CheckConnectionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            fixture.Invoke(state.Vm, "RefreshScrobblingRows");
            var login = fixture.Command(state.Row("登录豆瓣"), "RunCommand");
            var check = fixture.Command(state.Row("豆瓣登录状态"), "RunCommand");

            using (var cancel = new CancellationTokenSource())
            {
                douban.LoginCheck = token => new TaskCompletionSource<bool>().Task.WaitAsync(token);
                var verifying = service.CheckConnectionsAsync(cancel.Token);
                Assert.Equal(ScrobblingConnectionState.Checking, service.DoubanConnection);
                login.Execute(null);
                check.Execute(null);
                Assert.Equal(0, logins);
                Assert.Equal(0, checks);
                cancel.Cancel();
                verifying.GetAwaiter().GetResult();
            }

            login.Execute(null);
            Assert.Equal(1, logins);
            Assert.False(state.ContainsRow("登录豆瓣"));
            Assert.Equal("正在登录豆瓣…", fixture.Get<string>(state.Row("豆瓣登录状态"), "Value"));
            Assert.Contains("当前操作完成后可重新验证。", fixture.Get<string>(state.Row("豆瓣登录状态"), "Note"));
            login.Execute(null);
            check.Execute(null);
            Assert.Equal(1, logins);
            Assert.Equal(0, checks);
            pending.SetResult();

            douban.LoginCheck = _ => Task.FromResult(true);
            service.CheckConnectionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            fixture.Invoke(state.Vm, "RefreshScrobblingRows");
            var logout = fixture.Command(state.Row("退出豆瓣"), "RunCommand");
            login.Execute(null);
            Assert.Equal(1, logins);
            douban.LoginCheck = _ => Task.FromResult(false);
            service.CheckConnectionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            logout.Execute(null);
            Assert.Equal(0, logouts);
        });
    }

    internal static void RegisterSearch(SettingsPageStateTests.ShellFixture fixture)
    {
        TestHarness.Test("打格子搜索：中英文分类命中同一个原始导航键", () =>
        {
            using var state = new State(fixture);
            var language = LanguageOf(state);
            var original = language.GetValue(null);
            try
            {
                language.SetValue(null, UiLanguages.Chinese);
                fixture.Set(state.Vm, "SettingsSearchQuery", "打格子");
                var chinese = state.Results().Single(result => fixture.Get<string>(result, "Label") == "打格子");
                Assert.Equal("打格子", fixture.Get<string>(chinese, "CategoryKey"));
                language.SetValue(null, UiLanguages.English);
                fixture.Set(state.Vm, "SettingsSearchQuery", "Scrobbling");
                var english = state.Results().Single(result => fixture.Get<string>(result, "Label") == "打格子");
                Assert.Equal("Scrobbling", fixture.Get<string>(english, "DisplayLabel"));
                Assert.Equal("打格子", fixture.Get<string>(english, "CategoryKey"));
                Assert.True((bool)fixture.Invoke(state.Vm, "SelectSettingsSearchResult", english)!);
                Assert.Equal("打格子", fixture.Get<string>(state.Vm, "SelectedCategory"));
            }
            finally { language.SetValue(null, original); }
        });
        TestHarness.Test("打格子搜索：中英文命中通知开关，停用全部站点后撤销旧结果", () =>
        {
            using var state = new State(fixture, bangumi: true);
            var language = LanguageOf(state);
            var original = language.GetValue(null);
            try
            {
                language.SetValue(null, UiLanguages.Chinese);
                fixture.Set(state.Vm, "SettingsSearchQuery", NotificationLabel);
                Assert.Equal(NotificationLabel, fixture.Get<string>(state.Results().Single(), "DisplayLabel"));
                language.SetValue(null, UiLanguages.English);
                fixture.Set(state.Vm, "SettingsSearchQuery", "Windows notification");
                var result = state.Results().Single(entry => fixture.Get<string>(entry, "Label") == NotificationLabel);
                Assert.Equal("Show a Windows notification when scrobbling fails", fixture.Get<string>(result, "DisplayLabel"));
                Assert.True((bool)fixture.Invoke(state.Vm, "SelectSettingsSearchResult", result)!);
                Assert.Equal("打格子", fixture.Get<string>(state.Vm, "SelectedCategory"));
                fixture.Set(state.Row("启用 Bangumi 打格子"), "IsOn", false);
                Assert.False(state.Results().Any(entry => fixture.Get<string>(entry, "Label") == NotificationLabel));
                Assert.False((bool)fixture.Invoke(state.Vm, "SelectSettingsSearchResult", result)!);
                fixture.Set(state.Row("启用豆瓣自动标记"), "IsOn", true);
                Assert.True(state.Results().Any(entry => fixture.Get<string>(entry, "Label") == NotificationLabel));
            }
            finally { language.SetValue(null, original); }
        });
        TestHarness.Test("打格子搜索：凭据草稿不索引，开关收起撤销旧密钥建议", () =>
        {
            using var state = new State(fixture, bangumi: true);
            state.Settings.Settings.Scrobbling.Bangumi.ProtectedAccessToken = "private-scrobbling-ciphertext";
            var secret = state.Row("Bangumi Access Token");
            fixture.Set(secret, "Draft", "private-scrobbling-draft");
            foreach (var text in new[] { "private-scrobbling-ciphertext", "private-scrobbling-draft" })
            {
                fixture.Set(state.Vm, "SettingsSearchQuery", text);
                Assert.Equal(0, state.Results().Count());
            }
            fixture.Set(state.Vm, "SettingsSearchQuery", "Bangumi Access Token");
            var suggestion = state.Results().Single(result => ReferenceEquals(secret, fixture.Get<object>(result, "Row")));
            fixture.Set(state.Row("启用 Bangumi 打格子"), "IsOn", false);
            Assert.Equal(0, state.Results().Count());
            Assert.False((bool)fixture.Invoke(state.Vm, "SelectSettingsSearchResult", suggestion)!);
            fixture.Set(state.Row("启用 Bangumi 打格子"), "IsOn", true);
            Assert.True(state.Results().Any(result => ReferenceEquals(secret, fixture.Get<object>(result, "Row"))));
            Assert.Equal("private-scrobbling-draft", fixture.Get<string>(secret, "Draft"));
        });
    }

    private static PropertyInfo LanguageOf(State state) => state.Vm.GetType().Assembly
        .GetType("Momoka.Shell.Localization.UiText", true)!.GetProperty("Language", BindingFlags.Static | BindingFlags.Public)!;

    private sealed class State : IDisposable
    {
        private readonly SettingsPageStateTests.ShellFixture _fixture;
        internal object Vm { get; }
        internal SettingsPageStateTests.FakeSettings Settings { get; } = new();
        internal object[] Sections { get; }
        internal State(SettingsPageStateTests.ShellFixture fixture, bool bangumi = false)
        {
            _fixture = fixture;
            Settings.Settings.Scrobbling.Bangumi.Enabled = bangumi;
            Vm = fixture.NewViewModel();
            fixture.Attach(Vm, Settings, fixture.NewLauncher());
            Sections = ((IEnumerable)fixture.Invoke(Vm, "ScrobblingSections")!).Cast<object>().ToArray();
            foreach (var section in Sections) fixture.List(Vm, "Sections").Add(section);
        }
        internal object Row(string label) => _fixture.List(Vm, "Sections").Cast<object>()
            .SelectMany(section => _fixture.List(section, "Rows").Cast<object>())
            .Single(row => _fixture.Get<string>(row, "Label") == label);
        internal object[] Rows() => _fixture.List(Vm, "Sections").Cast<object>()
            .SelectMany(section => _fixture.List(section, "Rows").Cast<object>())
            .ToArray();
        internal bool ContainsRow(string label) => Rows().Any(row => _fixture.Get<string>(row, "Label") == label);
        internal IEnumerable<object> Results() => _fixture.List(Vm, "SettingsSearchResults").Cast<object>();
        public void Dispose() => ((IDisposable)Vm).Dispose();
    }

    private sealed class StatusDouban : IDoubanScrobblingClient
    {
        public bool LoggedIn { get; set; } = true;
        public Func<CancellationToken, Task<bool>>? LoginCheck { get; set; }
        public Task<bool> CheckLoginAsync(CancellationToken cancellationToken) => LoginCheck?.Invoke(cancellationToken) ?? Task.FromResult(LoggedIn);
        public Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string query, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<ScrobblingSubject> SubjectAsync(long id, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task MarkAsync(long id, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
