using System.Collections;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>执行当前 Shell 的真实目录重建；匿名内存来源，不创建窗口、不读取账号或发送网络请求。</summary>
internal static class SettingsNotificationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Register(SettingsPageStateTests.ShellFixture fixture)
    {
        Test("通知排序探针：真实检查规则接受诊断前，拒绝错位与缺失", async () =>
        {
            var capabilities = new Capabilities { ServerKind = MediaServerKind.Emby };
            var vm = Create(fixture, new SettingsPageStateTests.FakeSettings(), capabilities);
            using var lifetime = (IDisposable)vm;
            await Refresh(fixture, vm);
            var check = vm.GetType().Assembly.GetType("Momoka.Shell.ShellNavigationProbe", throwOnError: true)!
                .GetMethod("SitsBeforeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic)!;
            object MomokaGroup() => ((IEnumerable)fixture.Get<object>(vm, "CategoryGroups")).Cast<object>().First();
            Assert.True((bool)check.Invoke(null, [MomokaGroup(), new[] { "通知" }])!);
            Assert.False((bool)check.Invoke(null, [MomokaGroup(), new[] { "服务器" }])!);
            Assert.False((bool)check.Invoke(null, [MomokaGroup(), new[] { "通知", "服务器" }])!);
            capabilities.IsConnected = false;
            await Refresh(fixture, vm);
            Assert.False((bool)check.Invoke(null, [MomokaGroup(), new[] { "通知" }])!);
        });

        foreach (var kind in Enum.GetValues<MediaServerKind>())
            Test($"通知目录：{kind} 单服显隐、诊断前排序及断线恢复", async () =>
            {
                var settings = new SettingsPageStateTests.FakeSettings();
                settings.Settings.Playback.StopReportEnabled = true;
                var capabilities = new Capabilities { ServerKind = kind };
                var vm = Create(fixture, settings, capabilities);
                using var lifetime = (IDisposable)vm;
                await Refresh(fixture, vm);
                AssertNotification(fixture, vm, kind == MediaServerKind.Emby ? 1 : 0);
                Assert.Equal(kind == MediaServerKind.Emby, (bool)fixture.Invoke(vm, "CanOpenServerCategory", "通知")!);
                if (kind == MediaServerKind.Emby) fixture.Set(vm, "SelectedCategory", "通知");
                capabilities.IsConnected = false;
                await Refresh(fixture, vm);
                AssertNotification(fixture, vm, 0);
                Assert.False((bool)fixture.Invoke(vm, "CanOpenServerCategory", "通知")!);
                Assert.Equal("主页", fixture.Get<string>(vm, "SelectedCategory"));
                capabilities.IsConnected = true;
                await Refresh(fixture, vm);
                AssertNotification(fixture, vm, kind == MediaServerKind.Emby ? 1 : 0);
                Assert.True(settings.Settings.Playback.StopReportEnabled, "隐藏不清空通知偏好");
                Assert.Equal(0, settings.TrySaveCalls);
            });

        Test("通知目录：多服移至诊断前，保来源、搜索与下拉名称，降权退出立即撤销", async () =>
        {
            var settings = new SettingsPageStateTests.FakeSettings();
            using var handler = new RejectNetwork();
            var protector = PassthroughSecretProtector.Instance;
            using var session = new EmbySession(settings.Settings, new SettingsStore(fixture.Paths, protector),
                new CredentialVault(protector), new("fixture", "fixture", "fixture", "1"), handler);
            using var http = new EmbyHttp(handler);
            var emby = Source(session, http, MediaServerKind.Emby, "a", "account");
            var other = Source(session, http, MediaServerKind.Jellyfin, "b", "account");
            var second = Source(session, http, MediaServerKind.Emby, "c", "account");
            var ordinary = Source(session, http, MediaServerKind.Emby, "a", "ordinary", administrator: false);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(session, other.Scope);
            var capabilities = new Capabilities { ServerKind = MediaServerKind.Jellyfin, Sources = [ordinary, emby, other, second] };
            var vm = Create(fixture, settings, capabilities);
            using var lifetime = (IDisposable)vm;
            await Refresh(fixture, vm);
            AssertNotification(fixture, vm, 2);
            var notifications = Notifications(fixture, vm);
            Assert.True(notifications.Select(entry => fixture.Get<string>(entry, "SourceKey")).SequenceEqual(new[] { emby.Key, second.Key }));
            foreach (var entry in notifications)
            {
                Assert.True(fixture.Get<string>(entry, "DisplayLabel").StartsWith("通知 · ", StringComparison.Ordinal));
                Assert.Equal(fixture.Get<string>(entry, "DisplayLabel"), fixture.Get<string>(entry, "PickerLabel"));
            }
            Assert.Equal(2, notifications.Select(entry => fixture.Get<string>(entry, "DisplayLabel")).Distinct().Count());
            var firstKey = fixture.Get<string>(notifications[0], "Key");
            fixture.Set(vm, "SelectedCategory", firstKey);
            Assert.Equal(emby.Key, fixture.Get<string>(vm, "SelectedSourceKey"));
            Assert.True(fixture.Get<bool>(vm, "ShowsHosted"));
            Assert.True((bool)fixture.Invoke(vm, "CanOpenServerCategory", firstKey)!);
            fixture.Set(vm, "SettingsSearchQuery", "通知");
            var results = fixture.List(vm, "SettingsSearchResults").Cast<object>().ToArray();
            Assert.Equal(2, results.Select(result => fixture.Get<string>(result, "Label")).Distinct().Count());

            settings.Settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            fixture.Invoke(vm, "RefreshDanmakuAvailability");
            Assert.True(Notifications(fixture, vm).Select(entry => fixture.Get<string>(entry, "DisplayLabel"))
                .SequenceEqual(notifications.Select(entry => fixture.Get<string>(entry, "DisplayLabel"))), "弹幕目录重建必须保留来源标题");

            capabilities.Sources = [ordinary, emby with { IsAdministrator = false }, other, second];
            await Refresh(fixture, vm);
            AssertNotification(fixture, vm, 1);
            Assert.False((bool)fixture.Invoke(vm, "CanOpenServerCategory", firstKey)!);
            Assert.Equal("主页", fixture.Get<string>(vm, "SelectedCategory"));
            Assert.Equal("通知", fixture.Get<string>(Notifications(fixture, vm)[0], "DisplayLabel"));
            capabilities.Sources = [other];
            await Refresh(fixture, vm);
            AssertNotification(fixture, vm, 0);
            capabilities.Sources = [emby, other, second];
            await Refresh(fixture, vm);
            AssertNotification(fixture, vm, 2);
            Assert.Equal(0, handler.Requests);
        });
    }

    private static object Create(SettingsPageStateTests.ShellFixture fixture, SettingsPageStateTests.FakeSettings settings, Capabilities capabilities)
    {
        var vm = fixture.NewViewModel();
        fixture.Attach(vm, settings, fixture.NewLauncher());
        vm.GetType().GetField("_capabilities", Members)!.SetValue(vm, capabilities);
        fixture.Invoke(vm, "Activate");
        return vm;
    }

    private static Task Refresh(SettingsPageStateTests.ShellFixture fixture, object vm) =>
        (Task)fixture.Invoke(vm, "RefreshServerAdministrationAsync")!;

    private static object[] Notifications(SettingsPageStateTests.ShellFixture fixture, object vm) =>
        fixture.List(vm, "Categories").Cast<object>().Where(entry => fixture.Get<string>(entry, "Label") == "通知").ToArray();

    private static void AssertNotification(SettingsPageStateTests.ShellFixture fixture, object vm, int count)
    {
        var groups = ((IEnumerable)fixture.Get<object>(vm, "CategoryGroups")).Cast<object>().ToArray();
        var entries = ((IEnumerable)fixture.Get<object>(groups[0], "Entries")).Cast<object>().ToArray();
        var labels = entries.Select(entry => fixture.Get<string>(entry, "Label")).ToArray();
        Assert.Equal(count, Notifications(fixture, vm).Length);
        Assert.Equal(count, labels.Count(label => label == "通知"));
        if (count > 0)
        {
            var diagnostic = Array.IndexOf(labels, "诊断");
            Assert.True(labels.Skip(diagnostic - count).Take(count).All(label => label == "通知"));
        }
    }

    private static ServerSettingsSource Source(EmbySession session, EmbyHttp http, MediaServerKind kind, string serverId, string accountId, bool administrator = true)
    {
        var connection = new EmbyConnection(new Uri("https://notifications.invalid/emby/"), "", accountId, "fixture", serverId,
            new("fixture", "fixture", "fixture", "1"))
        { ServerKind = kind, IsAdministrator = administrator };
        var scope = new EmbySessionScope(session, new(new EmbyClient(http, connection), new ServerProfile { Id = serverId, Name = serverId },
            new AccountProfile { Id = accountId }));
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(session)!).Add(scope.Key, scope);
        return new(scope, kind, administrator);
    }

    private sealed class Capabilities : IServerCapabilities
    {
        public MediaServerKind ServerKind { get; set; }
        public bool IsConnected { get; set; } = true;
        public bool? IsAdministrator => true;
        public Version? ServerVersion => null;
        public IReadOnlyList<ServerSettingsSource> Sources { get; set; } = [];
        public ServerInterfaceFlavor InterfaceFlavor => Sources.Select(source => source.ServerProfileId).Distinct().Count() > 1
            ? ServerInterfaceFlavor.Generic : ServerAccess.InterfaceFor(ServerKind);
        public Task ProbeAsync(CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new InvalidOperationException("通知目录测试禁止网络");
        }
    }
}
