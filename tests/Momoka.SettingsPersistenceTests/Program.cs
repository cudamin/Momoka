extern alias ConfigurationCore;

using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using AppPaths = ConfigurationCore::Momoka.Infrastructure.AppPaths;
using AppSettings = ConfigurationCore::Momoka.Configuration.AppSettings;
using SettingsStore = ConfigurationCore::Momoka.Configuration.SettingsStore;
using SettingsService = ConfigurationCore::Momoka.Services.SettingsService;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly string P1 = new('1', 6);
    private static readonly string P2 = new('2', 6);
    private static string _root = "";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args is not [var root]) throw new ArgumentException("需要显式本树隔离输出目录");
        _root = Path.GetFullPath(root);
        Assert.True(_root.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Directory.CreateDirectory(_root);
        StoreCases();
        PasswordCases();
        LifetimeCases();
        BindingCases();
        return TestHarness.Run();
    }

    private static void Case(string name, Func<Task> run) => TestHarness.Test(name, () =>
    {
        using var queue = new UiQueue();
        queue.Run(run);
    });

    private static Task Save(FnNasPasswordViewModel vm) => (Task)Method("SaveAsync").Invoke(vm, null)!;
    private static void Retry(FnNasPasswordViewModel vm) => Method("RetrySaveLocal").Invoke(vm, null);
    private static MethodInfo Method(string name) => typeof(FnNasPasswordViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("实际密码 VM 缺少 " + name);
    private static object Property(FnNasPasswordViewModel vm, string name) => typeof(FnNasPasswordViewModel).GetProperty(name)!.GetValue(vm)!;
    private static bool Unsaved(FnNasPasswordViewModel vm) => (bool)Property(vm, "HasUnsavedChanges");
    private static bool CanRetry(FnNasPasswordViewModel vm) => (bool)Property(vm, "CanRetrySaveLocal");
    private static void Draft(FnNasPasswordViewModel vm, string password) => vm.NewPassword = vm.ConfirmPassword = password;
    private static void AssertDraft(FnNasPasswordViewModel vm, string password)
    {
        Assert.True(string.Equals(password, vm.NewPassword, StringComparison.Ordinal), "匿名新草稿，值不打印");
        Assert.True(string.Equals(password, vm.ConfirmPassword, StringComparison.Ordinal), "匿名确认草稿，值不打印");
    }

    private static void StoreCases()
    {
        Case("共享保存：会话式直接 Store 失败传递全部 Service，释放锁只重试本地", () =>
        {
            var env = new LocalEnvironment(_root);
            var old = File.ReadAllBytes(env.Paths.SettingsFile);
            var backup = File.ReadAllBytes(env.Paths.SettingsBackupFile);
            env.Document.Playback.SubtitleFontSize = 72;
            using (env.Lock())
            {
                Assert.Throws<IOException>(() => env.Store.Save(env.Document));
                Assert.True(env.Service.HasUnsavedChanges);
                Assert.True(new SettingsService(env.Store, env.Document).HasUnsavedChanges, "晚创建 Service 也读已有失败");
                Assert.Equal(72, env.Document.Playback.SubtitleFontSize);
                Assert.False(env.Service.TrySave());
            }
            Assert.True(old.SequenceEqual(File.ReadAllBytes(env.Paths.SettingsFile)), "旧主文件完整");
            Assert.True(backup.SequenceEqual(File.ReadAllBytes(env.Paths.SettingsBackupFile)), "旧备份完整");
            Assert.Equal(0, Directory.GetFiles(env.Paths.Root, "*.tmp").Length);
            Assert.True(env.Service.TrySave());
            Assert.False(env.Service.HasUnsavedChanges);
            Assert.Equal(72, env.ReadFont());
            Assert.Equal(0, env.Protector.Calls);
            return Task.CompletedTask;
        });

        Case("共享保存：直接成功清除其它调用者旧失败，无 Service 私有残留", () =>
        {
            var env = new LocalEnvironment(_root);
            var peer = new SettingsService(env.Store, env.Document);
            using (env.Lock()) Assert.False(env.Service.TrySave());
            Assert.True(peer.HasUnsavedChanges);
            env.Document.Playback.SubtitleFontSize = 76;
            env.Store.Save(env.Document);
            Assert.False(peer.HasUnsavedChanges);
            Assert.False(env.Service.HasUnsavedChanges);
            Assert.Equal(76, env.ReadFont());
            return Task.CompletedTask;
        });

        Case("共享保存：失败/重复失败/成功每次通知完成状态，订阅与退订", () =>
        {
            var env = new LocalEnvironment(_root);
            var states = new List<bool>();
            var local = new PersistenceBridge(env.Service);
            EventHandler handler = (_, _) => states.Add(local.HasUnsavedChanges);
            local.SaveStateChanged += handler;
            using (env.Lock()) { Assert.False(env.Service.TrySave()); Assert.False(env.Service.TrySave()); }
            Assert.True(env.Service.TrySave());
            Assert.Equal("True,True,False", string.Join(',', states));
            local.SaveStateChanged -= handler;
            using (env.Lock()) Assert.False(env.Service.TrySave());
            Assert.Equal(3, states.Count);
            return Task.CompletedTask;
        });

        Case("共享保存：坏订阅者不改变落盘结果，不截断后续观察者", () =>
        {
            var env = new LocalEnvironment(_root);
            var local = new PersistenceBridge(env.Service);
            var good = 0;
            local.SaveStateChanged += (_, _) => throw new InvalidOperationException("匿名通知失败");
            local.SaveStateChanged += (_, _) => good++;
            env.Document.Playback.SubtitleFontSize = 74;
            Assert.True(env.Service.TrySave());
            Assert.False(local.HasUnsavedChanges);
            Assert.Equal(74, env.ReadFont());
            Assert.Equal(1, good);
            using (env.Lock()) Assert.False(env.Service.TrySave());
            Assert.Equal(2, good);
            Assert.True(local.HasUnsavedChanges);
            return Task.CompletedTask;
        });

        Case("共享保存：通知不持文件锁，旧失败 catch 不覆盖订阅者较新成功", () =>
        {
            var env = new LocalEnvironment(_root);
            var local = new PersistenceBridge(env.Service);
            var locked = env.Lock();
            var recovered = false;
            local.SaveStateChanged += (_, _) =>
            {
                if (recovered || !local.HasUnsavedChanges) return;
                recovered = true;
                locked.Dispose();
                env.Document.Playback.SubtitleFontSize = 73;
                env.Store.Save(env.Document);
            };
            Assert.False(env.Service.TrySave(), "原尝试失败，但更晚的一次直接保存已恢复");
            locked.Dispose();
            Assert.True(recovered);
            Assert.False(local.HasUnsavedChanges, "状态按 Store 真正执行顺序，不由旧 Service catch 覆盖");
            Assert.Equal(73, env.ReadFont());
            return Task.CompletedTask;
        });

        foreach (var recovery in new[] { false, true })
        {
            Case("共享保存：导出偏好不冒充当前文档保存 " + recovery, () =>
            {
                var env = new LocalEnvironment(_root);
                using (env.Lock()) Assert.False(env.Service.TrySave());
                if (recovery) env.Service.ExportPreferences(Path.Combine(env.Paths.Root, "preferences.json"));
                else Assert.Throws<Exception>(() => env.Service.ExportPreferences(Path.Combine(env.Paths.Root, "missing", "preferences.json")));
                Assert.True(env.Service.HasUnsavedChanges);
                return Task.CompletedTask;
            });
        }

        Case("共享保存：两独立 Store 不共享失败，匿名文档无凭据持久化", () =>
        {
            var a = new LocalEnvironment(_root);
            var b = new LocalEnvironment(_root);
            using (a.Lock()) Assert.False(a.Service.TrySave());
            Assert.True(a.Service.HasUnsavedChanges);
            Assert.False(b.Service.HasUnsavedChanges);
            Assert.True(b.Service.TrySave());
            Assert.True(a.Service.HasUnsavedChanges);
            Assert.Equal(0, a.Protector.Calls + b.Protector.Calls);
            Assert.Equal(0, a.Document.Servers.Count + b.Document.Servers.Count);
            return Task.CompletedTask;
        });
    }

    private static void PasswordCases()
    {
        foreach (var laterDraft in new[] { false, true })
        {
            Case("真实 VM：服务器确认后本地失败分离成功，重试仅本地且保留新草稿 " + laterDraft, async () =>
            {
                using var fixture = new PasswordFixture(_root);
                Draft(fixture.Vm, P1);
                var save = Save(fixture.Vm);
                if (laterDraft) Draft(fixture.Vm, P2);
                using (fixture.Local.Lock())
                {
                    fixture.Reply();
                    await save;
                    Assert.True(fixture.Local.Service.HasUnsavedChanges);
                    Assert.True(Unsaved(fixture.Vm));
                    Assert.True(CanRetry(fixture.Vm));
                    Assert.Equal(Visibility.Visible, (Visibility)Property(fixture.Vm, "UnsavedVisibility"));
                    Assert.Contains("本地尚未保存", fixture.Vm.NoticeTitle);
                    Assert.Equal(InfoBarSeverity.Warning, fixture.Vm.NoticeSeverity);
                    if (laterDraft) AssertDraft(fixture.Vm, P2);
                    else AssertDraft(fixture.Vm, "");
                    Retry(fixture.Vm);
                    Assert.True(Unsaved(fixture.Vm));
                    Assert.Equal(1, fixture.Endpoint.Writes.Count);
                    Assert.Equal(1, fixture.Endpoint.ConfirmedWrites);
                }
                Retry(fixture.Vm);
                Assert.False(Unsaved(fixture.Vm));
                Assert.False(CanRetry(fixture.Vm));
                Assert.Equal(Visibility.Collapsed, (Visibility)Property(fixture.Vm, "UnsavedVisibility"));
                Assert.Equal(72, fixture.Local.ReadFont());
                Assert.Equal(1, fixture.Endpoint.Writes.Count, "本地重试不发送第二次改密");
                Assert.Equal(2, fixture.Persistence.Retries);
                Assert.Equal(0, fixture.Local.Protector.Calls);
                if (laterDraft)
                {
                    AssertDraft(fixture.Vm, P2);
                    Assert.Contains("尚未提交", fixture.Vm.NoticeMessage);
                    var second = Save(fixture.Vm);
                    fixture.Reply(1);
                    await second;
                    Assert.Equal(2, fixture.Endpoint.Writes.Count, "只由显式保存新草稿追加请求");
                }
            });
        }

        Case("真实 VM：正常本地成功保原文案/清本次草稿，无本地重试", async () =>
        {
            using var fixture = new PasswordFixture(_root);
            Draft(fixture.Vm, P1);
            var save = Save(fixture.Vm);
            fixture.Reply();
            await save;
            AssertDraft(fixture.Vm, "");
            Assert.Equal("密码已修改", fixture.Vm.NoticeTitle);
            Assert.Equal(InfoBarSeverity.Success, fixture.Vm.NoticeSeverity);
            Assert.False(Unsaved(fixture.Vm));
            Retry(fixture.Vm);
            Assert.Equal(0, fixture.Persistence.Retries);
        });

        Case("真实 VM：正常确认有后来草稿，保留 R027 原未保存提示", async () =>
        {
            using var fixture = new PasswordFixture(_root);
            Draft(fixture.Vm, P1);
            var save = Save(fixture.Vm);
            Draft(fixture.Vm, P2);
            fixture.Reply();
            await save;
            AssertDraft(fixture.Vm, P2);
            Assert.Equal("本次密码已修改", fixture.Vm.NoticeTitle);
            Assert.Contains("当前新输入尚未保存", fixture.Vm.NoticeMessage);
            Assert.False(Unsaved(fixture.Vm));
        });

        Case("真实 VM：本地重试 Busy 通知中新编辑不提交不清除", () =>
        {
            using var fixture = new PasswordFixture(_root);
            using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
            fixture.Vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(fixture.Vm.Busy) && fixture.Vm.Busy) Draft(fixture.Vm, P2);
            };
            Retry(fixture.Vm);
            AssertDraft(fixture.Vm, P2);
            Assert.Equal(1, fixture.Persistence.Retries);
            Assert.Equal(0, fixture.Endpoint.Writes.Count);
            return Task.CompletedTask;
        });

        Case("真实 VM：挂起改密期间绕过 CanExecute 本地重试也是零保存", async () =>
        {
            using var fixture = new PasswordFixture(_root);
            using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
            Draft(fixture.Vm, P1);
            var save = Save(fixture.Vm);
            Assert.True(fixture.Vm.Busy);
            Assert.False(CanRetry(fixture.Vm));
            Retry(fixture.Vm);
            Assert.Equal(0, fixture.Persistence.Retries);
            fixture.Reply();
            await save;
            Assert.Equal(1, fixture.Endpoint.Writes.Count);
        });

        Case("真实 VM：挂起改密失败不伪造本地确认或清草稿", async () =>
        {
            using var fixture = new PasswordFixture(_root);
            Draft(fixture.Vm, P1);
            var save = Save(fixture.Vm);
            fixture.Endpoint.Writes[0].Fail();
            await save;
            AssertDraft(fixture.Vm, P1);
            Assert.Equal("修改密码失败", fixture.Vm.NoticeTitle);
            Assert.Equal(InfoBarSeverity.Error, fixture.Vm.NoticeSeverity);
            Assert.Equal(0, fixture.Endpoint.ConfirmedWrites);
            Assert.False(Unsaved(fixture.Vm));
            Assert.Equal(0, fixture.Persistence.Retries);
        });

        Case("真实 VM：其它页面成功保存后自动收起欠项，后来草稿仍需显式提交", async () =>
        {
            using var fixture = new PasswordFixture(_root);
            Draft(fixture.Vm, P1);
            var save = Save(fixture.Vm);
            using (fixture.Local.Lock()) { fixture.Reply(); await save; }
            Draft(fixture.Vm, P2);
            Assert.True(fixture.Local.Service.TrySave());
            Assert.False(Unsaved(fixture.Vm));
            AssertDraft(fixture.Vm, P2);
            Assert.Contains("尚未提交", fixture.Vm.NoticeMessage);
            Assert.Equal(1, fixture.Endpoint.Writes.Count);
        });

        Case("真实 VM：本地重试旧失败通知内较新保存成功，不冒仍未保存", () =>
        {
            using var fixture = new PasswordFixture(_root);
            var locked = fixture.Local.Lock();
            Assert.False(fixture.Local.Service.TrySave());
            var recovered = false;
            fixture.Persistence.SaveStateChanged += (_, _) =>
            {
                if (recovered || !fixture.Persistence.HasUnsavedChanges) return;
                recovered = true;
                locked.Dispose();
                fixture.Local.Store.Save(fixture.Local.Document);
            };
            Retry(fixture.Vm);
            locked.Dispose();
            Assert.True(recovered);
            Assert.False(Unsaved(fixture.Vm));
            Assert.Equal("本地设置已保存", fixture.Vm.NoticeTitle);
            Assert.Equal(0, fixture.Endpoint.Writes.Count);
            return Task.CompletedTask;
        });

        Case("真实 VM：生成 RetrySaveLocalCommand 实际执行及通知", () =>
        {
            using var fixture = new PasswordFixture(_root);
            var command = (CommunityToolkit.Mvvm.Input.IRelayCommand)Property(fixture.Vm, "RetrySaveLocalCommand");
            var changes = 0;
            command.CanExecuteChanged += (_, _) => changes++;
            Assert.False(command.CanExecute(null));
            using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
            Assert.True(command.CanExecute(null));
            command.Execute(null);
            Assert.False(command.CanExecute(null));
            Assert.True(changes >= 2);
            Assert.Equal(1, fixture.Persistence.Retries);
            Assert.Equal(0, fixture.Endpoint.Writes.Count);
            return Task.CompletedTask;
        });
    }

    private static void LifetimeCases()
    {
        foreach (var boundary in new[] { "未Attach", "只基础Attach", "未知来源", "非飞牛", "来源撤销", "默认切换", "Cancel", "Dispose" })
        {
            Case("真实 VM：本地重试守卫 " + boundary, () =>
            {
                using var fixture = new PasswordFixture(_root, attach: false);
                using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
                if (boundary == "只基础Attach") ((FnNasSessionViewModel)fixture.Vm).Attach(fixture.Session);
                else if (boundary != "未Attach") fixture.Attach(source: boundary == "未知来源" ? "missing" : null);
                switch (boundary)
                {
                    case "非飞牛": fixture.Session.Sources[0].Connection = new(false); break;
                    case "来源撤销": fixture.Session.Sources[0].IsAvailable = false; break;
                    case "默认切换": fixture.Session.DefaultSource = fixture.Session.Sources[1]; break;
                    case "Cancel": fixture.Vm.Cancel(); break;
                    case "Dispose": fixture.Vm.Dispose(); break;
                }
                Retry(fixture.Vm);
                Assert.Equal(0, fixture.Persistence.Retries);
                Assert.Equal(0, fixture.Endpoint.Writes.Count);
                return Task.CompletedTask;
            });
        }

        Case("真实 VM：显式非默认来源不受默认源切换限制，仅本地保存", () =>
        {
            using var fixture = new PasswordFixture(_root, attach: false);
            using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
            fixture.Attach(source: fixture.Session.Sources[1].Key);
            Assert.True(CanRetry(fixture.Vm));
            Retry(fixture.Vm);
            Assert.Equal(1, fixture.Persistence.Retries);
            Assert.Equal(0, fixture.Endpoint.Writes.Count);
            return Task.CompletedTask;
        });

        foreach (var exit in new[] { "Cancel", "Dispose", "重Attach" })
        {
            Case("真实 VM：离页旧确认仍留共享本地欠项但不回填 " + exit, async () =>
            {
                using var fixture = new PasswordFixture(_root);
                Draft(fixture.Vm, P1);
                var save = Save(fixture.Vm);
                if (exit == "Cancel") fixture.Vm.Cancel();
                else if (exit == "Dispose") fixture.Vm.Dispose();
                else fixture.Attach(source: fixture.Session.Sources[1].Key);
                using (fixture.Local.Lock()) { fixture.Reply(); await save; }
                Assert.True(fixture.Local.Service.HasUnsavedChanges, "已确认后的本地失败不能被页面取消丢掉");
                if (exit == "重Attach") Assert.True(Unsaved(fixture.Vm), "新活动页只通过共享通知看到欠项");
                else Assert.False(Unsaved(fixture.Vm));
                Assert.False(fixture.Vm.NoticeOpen);
                Assert.Equal(1, fixture.Endpoint.ConfirmedWrites);
                Assert.Equal(0, fixture.Persistence.Retries);
                var next = new FnNasPasswordViewModel();
                Attach(next, fixture.Session, fixture.Persistence, action => action());
                Assert.True(Unsaved(next));
                next.Cancel();
            });
        }

        foreach (var boundary in new[] { "Cancel", "重Attach", "默认切换" })
        {
            Case("真实 VM：本地重试 BeginLoad 同步边界后不保存旧票 " + boundary, () =>
            {
                using var fixture = new PasswordFixture(_root);
                using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
                var fired = false;
                fixture.Vm.PropertyChanged += (_, e) =>
                {
                    if (fired || e.PropertyName != nameof(fixture.Vm.Busy) || !fixture.Vm.Busy) return;
                    fired = true;
                    if (boundary == "Cancel") fixture.Vm.Cancel();
                    else if (boundary == "重Attach") fixture.Attach(source: fixture.Session.Sources[1].Key);
                    else fixture.Session.DefaultSource = fixture.Session.Sources[1];
                };
                Retry(fixture.Vm);
                Assert.True(fired);
                Assert.Equal(0, fixture.Persistence.Retries);
                Assert.Equal(0, fixture.Endpoint.Writes.Count);
                return Task.CompletedTask;
            });
        }

        Case("真实 VM：Cancel/同源重附加不接收旧队列保存通知", () =>
        {
            using var fixture = new PasswordFixture(_root, attach: false);
            var queued = new Queue<Action>();
            fixture.Attach(dispatch: queued.Enqueue);
            using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
            Assert.Equal(1, queued.Count);
            Assert.False(Unsaved(fixture.Vm));
            fixture.Vm.Cancel();
            queued.Dequeue()();
            Assert.False(Unsaved(fixture.Vm));
            fixture.Attach(dispatch: queued.Enqueue);
            Assert.True(Unsaved(fixture.Vm));
            using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
            var stale = queued.Dequeue();
            fixture.Attach(dispatch: queued.Enqueue);
            Assert.True(fixture.Local.Service.TrySave());
            Assert.True(Unsaved(fixture.Vm));
            stale();
            Assert.True(Unsaved(fixture.Vm), "旧订阅不能在同源重Attach后复活");
            queued.Dequeue()();
            Assert.False(Unsaved(fixture.Vm));
            return Task.CompletedTask;
        });

        Case("真实 VM：失败/成功乱序排队通知只读取最终共享状态", () =>
        {
            using var fixture = new PasswordFixture(_root, attach: false);
            var queued = new List<Action>();
            fixture.Attach(dispatch: queued.Add);
            using (fixture.Local.Lock()) Assert.False(fixture.Local.Service.TrySave());
            Assert.True(fixture.Local.Service.TrySave());
            queued[1]();
            Assert.False(Unsaved(fixture.Vm));
            queued[0]();
            Assert.False(Unsaved(fixture.Vm));
            Assert.Equal(0, fixture.Endpoint.Writes.Count);
            return Task.CompletedTask;
        });

        Case("真实 VM：离页退订/Dispose 后无监听泄漏", () =>
        {
            using var fixture = new PasswordFixture(_root);
            Assert.Equal(1, fixture.Persistence.Subscribers);
            fixture.Vm.Cancel();
            Assert.Equal(0, fixture.Persistence.Subscribers);
            fixture.Attach();
            Assert.Equal(1, fixture.Persistence.Subscribers);
            fixture.Vm.Dispose();
            Assert.Equal(0, fixture.Persistence.Subscribers);
            return Task.CompletedTask;
        });
    }

    private static void BindingCases()
    {
        Case("真实 XAML：完整未保存 InfoBar 和本地重试命令，有稳定定位且 OneWay", () =>
        {
            var xaml = XDocument.Parse(Resource("FnNasPasswordPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var notice = xaml.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "FnNasPasswordUnsavedNotice");
            Assert.Equal("False", notice.Attribute("IsClosable")!.Value);
            Assert.Contains("HasUnsavedChanges", notice.Attribute("IsOpen")!.Value);
            Assert.Contains("Mode=OneWay", notice.Attribute("IsOpen")!.Value);
            Assert.Contains("UnsavedVisibility", notice.Attribute("Visibility")!.Value);
            var button = notice.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "FnNasPasswordRetryLocalSave");
            Assert.Contains("RetrySaveLocalCommand", button.Attribute("Command")!.Value);
            Assert.Equal("重试本地保存", button.Attribute("Content")!.Value);
            return Task.CompletedTask;
        });

        Case("真实页面：窄保存依赖和 Dispatcher 接线，Settings 活动期订阅退订", () =>
        {
            var password = Resource("FnNasPasswordPage.xaml.cs");
            Assert.Contains("request.Services.GetRequiredService<ISettingsService>()", password);
            Assert.Contains("DispatcherQueue.TryEnqueue(() => action())", password);
            var settings = Resource("SettingsPage.xaml.cs");
            Assert.Contains("WatchSettingsSaveState(request.Services.GetRequiredService<ISettingsService>());", settings);
            Assert.Contains("settings.SaveStateChanged += _saveStateChanged;", settings);
            Assert.Contains("settings.SaveStateChanged -= handler;", settings);
            Assert.Contains("_activity.IsCurrent(activity) && ReferenceEquals(_saveSettings, settings)", settings);
            Assert.Equal(3, settings.Split("StopSettingsSaveState();", StringSplitOptions.None).Length - 1);
            return Task.CompletedTask;
        });

        Case("安全边界：真实 Session 仅静态 Store 保存接线，VM 确认是窄替身", () =>
        {
            Assert.Equal(typeof(Program).Assembly, typeof(EmbySession).Assembly);
            Assert.True(typeof(SettingsStore).Assembly != typeof(EmbySession).Assembly);
            Assert.Contains("try { _store.Save(_settings); }", Resource("EmbySession.cs"));
            Assert.DoesNotContain("ConfigurationCore", typeof(FnNasPasswordViewModel).Assembly.GetName().Name);
            Assert.False(AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "Momoka"));
            return Task.CompletedTask;
        });
    }

    private static string Resource(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Production." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Attach(FnNasPasswordViewModel vm, EmbySession session, PersistenceBridge persistence, Action<Action> dispatch, string? source = null)
    {
        // Exact actual VM method, including its lifecycle. Reflection permits compiling the SAME
        // behavior fixture against an older VM that lacks the new narrow recovery entry point.
        var method = typeof(FnNasPasswordViewModel).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(EmbySession), typeof(ISettingsPersistence), typeof(Action<Action>), typeof(string)], null);
        if (method is null) ((FnNasSessionViewModel)vm).Attach(session, source);
        else method.Invoke(vm, [session, persistence, dispatch, source]);
    }

    private sealed class PasswordFixture : IDisposable
    {
        internal PasswordFixture(string root, bool attach = true)
        {
            Local = new(root);
            Persistence = new(Local.Service);
            Session = new();
            Vm = new();
            Endpoint.PasswordConfirmed = () =>
            {
                // Deliberately a NONSECRET preference stands for changed in-memory settings. Never
                // call real ApplyPasswordChange, auth/restore, vault, DPAPI or a server HTTP endpoint.
                Local.Document.Playback.SubtitleFontSize = 72;
                try { Local.Store.Save(Local.Document); }
                catch (IOException) { }
            };
            if (attach) Attach();
        }
        internal LocalEnvironment Local { get; }
        internal PersistenceBridge Persistence { get; }
        internal EmbySession Session { get; }
        internal FnNasPasswordViewModel Vm { get; }
        internal FixtureEndpoint Endpoint => Session.Sources[0].Endpoint;
        internal void Reply(int index = 0) => Endpoint.Writes[index].Succeed();
        internal void Attach(string? source = null, Action<Action>? dispatch = null) => Program.Attach(Vm, Session, Persistence, dispatch ?? (action => action()), source);
        public void Dispose() => Vm.Cancel();
    }

    private sealed class LocalEnvironment
    {
        internal LocalEnvironment(string root)
        {
            Paths = new(Path.Combine(root, Guid.NewGuid().ToString("N")));
            Paths.EnsureCreated();
            Document = new();
            Document.Playback.SubtitleFontSize = 44;
            Protector = new();
            Store = new(Paths, Protector);
            Service = new(Store, Document);
            Store.Save(Document);
            Store.Save(Document);
        }
        internal AppPaths Paths { get; }
        internal AppSettings Document { get; }
        internal DenySecrets Protector { get; }
        internal SettingsStore Store { get; }
        internal SettingsService Service { get; }
        internal FileStream Lock() => new(Paths.SettingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        internal int ReadFont()
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Paths.SettingsFile));
            return json.RootElement.GetProperty("Playback").GetProperty("SubtitleFontSize").GetInt32();
        }
    }

    private sealed class DenySecrets : ConfigurationCore::Momoka.Configuration.ISecretProtector
    {
        internal int Calls { get; private set; }
        public string Protect(string value) { Calls++; throw new InvalidOperationException("夹具禁止凭据写入"); }
        public string Unprotect(string value) { Calls++; throw new InvalidOperationException("夹具禁止凭据读取"); }
    }

    private sealed class PersistenceBridge(SettingsService service) : ISettingsPersistence
    {
        internal int Retries { get; private set; }
        internal int Subscribers { get; private set; }
        public bool HasUnsavedChanges => service.HasUnsavedChanges;
        public bool TrySave() { Retries++; return service.TrySave(); }
        public event EventHandler? SaveStateChanged
        {
            add { Event()?.AddEventHandler(service, value); Subscribers++; }
            remove { Event()?.RemoveEventHandler(service, value); Subscribers--; }
        }
        private static EventInfo? Event() => typeof(SettingsService).GetEvent("SaveStateChanged");
    }
}
