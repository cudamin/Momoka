using System.Collections;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Scrobbling;

namespace Momoka.Tests;

/// <summary>Actual settings models with memory settings: no window, network or credential IO.</summary>
internal static class SettingsScrobblingTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void RegisterState(SettingsPageStateTests.ShellFixture fixture)
    {
        TestHarness.Test("打格子设置：默认两张卡各只有总开关，建卡不写配置", () =>
        {
            using var state = new State(fixture);
            Assert.Equal(2, state.Sections.Length);
            Assert.Equal("Bangumi 打格子", fixture.Get<string>(state.Sections[0], "Title"));
            Assert.Equal("豆瓣自动标记在看 / 已看", fixture.Get<string>(state.Sections[1], "Title"));
            foreach (var section in state.Sections)
            {
                var rows = fixture.List(section, "Rows");
                Assert.Equal(1, rows.Count);
                Assert.Equal("SettingToggleRow", rows[0]!.GetType().Name);
                Assert.False(fixture.Get<bool>(rows[0]!, "IsOn"));
            }
            Assert.True(fixture.List(state.Vm, "Categories").Cast<object>()
                .Any(entry => fixture.Get<string>(entry, "Label") == "打格子"));
            Assert.Equal(0, state.Settings.TrySaveCalls);
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
    }

    internal static void RegisterSearch(SettingsPageStateTests.ShellFixture fixture)
    {
        TestHarness.Test("打格子搜索：中英文分类命中同一个原始导航键", () =>
        {
            using var state = new State(fixture);
            var sample = fixture.NewRow("SettingChoice", "", (Action)(() => { }));
            var language = sample.GetType().Assembly.GetType("Momoka.Shell.Localization.UiText", true)!
                .GetProperty("Language", BindingFlags.Static | BindingFlags.Public)!;
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
        internal IEnumerable<object> Results() => _fixture.List(Vm, "SettingsSearchResults").Cast<object>();
        public void Dispose() => ((IDisposable)Vm).Dispose();
    }

    private sealed class StatusDouban : IDoubanScrobblingClient
    {
        public Task<bool> CheckLoginAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(string query, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<ScrobblingSubject> SubjectAsync(long id, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task MarkAsync(long id, bool completed, Func<bool> stillAllowed, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
