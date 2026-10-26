using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string FirstPassword = new('1', 6);
    private static readonly string LaterPassword = new('2', 6);
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];
    private static string _root = "";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is not [var root, var output] || !Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(output))
            throw new ArgumentException("Supply this tree root and a new in-tree evidence file.");
        _root = Path.GetFullPath(root);
        Assert.True(typeof(Program).Assembly.Location.StartsWith(Path.Combine(_root, "tests", "Momoka.CurrentPermissionSnapshotTests", "AccountCompatibility") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.True(Path.GetFullPath(output).StartsWith(Path.Combine(_root, "artifacts", "r011-current-permission") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(output), "Do not overwrite evidence");
        Assert.True(typeof(FnNasPasswordViewModel).Assembly == typeof(EmbySession).Assembly, "All session types must be narrow test boundaries, not production Core");
        Assert.False(AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is "Momoka.Core" or "Momoka"), "No production authentication/persistence assembly may be loaded");
        Register();
        var results = new List<object>();
        var failed = 0;
        foreach (var (name, scenario) in Cases)
        {
            try
            {
                using var queue = new UiQueue();
                queue.Run(scenario);
                Console.WriteLine("通过 " + name);
                results.Add(new { name, outcome = "passed" });
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("失败 " + name + " —— " + error.Message);
                results.Add(new { name, outcome = "failed", message = error.Message });
            }
        }
        // The main tree's current VM already has R010. Label the actual source selector,
        // not merely the compile-time interface switch used by both variants.
        var mode = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AccountPasswordSource"))
            ? "current-tree-full-password-and-preferences-with-local-save"
            : "explicit-committed-full-password-and-current-preferences-with-local-save";
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new
        {
            mode,
            executed = Cases.Count,
            passed = Cases.Count - failed,
            failed,
            skipped = 0,
            cases = results,
            notExecuted = new[] { "production authentication/restore/password/session/vault/DPAPI", "settings Store/file IO", "real network/window/native/playback" }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        Console.WriteLine($"帐号兼容 {Cases.Count}项，通过{Cases.Count - failed}，失败{failed}，跳过0；证据{output}");
        return failed == 0 ? 0 : 1;
    }

    private static void Case(string name, Func<Task> run) => Cases.Add((name, run));
    private static Task Complete(Action action) { action(); return Task.CompletedTask; }
    private static void Draft(FnNasPasswordViewModel vm, string password)
    {
        vm.NewPassword = vm.ConfirmPassword = password;
    }
    private static void SameDraft(FnNasPasswordViewModel vm, string password)
    {
        Assert.True(vm.NewPassword == password && vm.ConfirmPassword == password, "Anonymous draft preserved, never print its value");
    }
    private static void AttachPassword(FnNasPasswordViewModel vm, EmbySession session, AnonymousLocalSave local, string? key = null)
    {
#if R010_COMPATIBILITY
        vm.Attach(session, local, action => action(), key);
#else
        vm.Attach(session, key);
#endif
    }
    private static bool Tracks(FnNasSessionViewModel vm) => (bool)typeof(FnNasSessionViewModel).GetProperty("TracksPermissions", Members)!.GetValue(vm)!;

    private static void Register()
    {
        foreach (var explicitSource in new[] { false, true })
        {
            var name = explicitSource ? "显式B" : "默认A";
            Case($"完整password {name}权限降升/来源通知不取消确认或清草稿", async () =>
            {
                var session = new EmbySession();
                var source = session.Sources[explicitSource ? 1 : 0];
                var local = new AnonymousLocalSave();
                using var vm = new FnNasPasswordViewModel();
                AttachPassword(vm, session, local, explicitSource ? source.Key : null);
                Draft(vm, FirstPassword);
                Assert.False(Tracks(vm));
                var pending = vm.SaveCommand.ExecuteAsync(null);
                Assert.Equal(1, source.Endpoint.Submissions.Count);
                var submission = source.Endpoint.Submissions[0];
                Assert.True(submission.Password == FirstPassword, "Only anonymous click-time value passed to fake endpoint");
                session.Permission(source, false);
                session.NotifySources();
                session.Permission(source, true);
                SameDraft(vm, FirstPassword);
                Assert.True(vm.Busy && vm.ContentVisibility == Visibility.Visible);
                Assert.False(submission.Token.IsCancellationRequested, "Admin permission cannot cancel an account acknowledgement");
                Assert.Equal(0, session.PermissionAdds);
                Assert.Equal(0, session.SourceAdds);
                submission.Acknowledged.SetResult();
                await pending;
                SameDraft(vm, "");
                Assert.True(vm.CanUse && vm.NoticeSeverity == InfoBarSeverity.Success);
                Assert.Equal(1, source.Endpoint.Confirmed);
            });
            Case($"完整preferences {name}权限降升不取消在途提交，不自动重发或清新选择", async () =>
            {
                var session = new EmbySession();
                var source = session.Sources[explicitSource ? 1 : 0];
                using var vm = new FnNasPreferencesViewModel();
                vm.Attach(session, explicitSource ? source.Key : null);
                await vm.ReloadAsync();
                var languages = vm.Languages;
                Assert.True(vm.Loaded && vm.CanSave);
                Assert.False(Tracks(vm));
                vm.SubtitleLanguage = "ja-JP";
                vm.AudioLanguage = "ko-KR";
                var pending = vm.SaveCommand.ExecuteAsync(null);
                Assert.Equal(1, source.Endpoint.Submissions.Count);
                var submission = source.Endpoint.Submissions[0];
                session.Permission(source, false);
                session.NotifySources();
                session.Permission(source, true);
                Assert.False(submission.Token.IsCancellationRequested);
                vm.SubtitleLanguage = "zh-CN";
                vm.AudioLanguage = "en-US";
                submission.Acknowledged.SetResult();
                await pending;
                Assert.True(vm.SubtitleLanguage == "zh-CN" && vm.AudioLanguage == "en-US");
                Assert.Equal("ja-JP", submission.Body!["lan"]!.ToString());
                Assert.True(ReferenceEquals(languages, vm.Languages));
                Assert.True(vm.CanUse && vm.NoticeSeverity == InfoBarSeverity.Warning);
                Assert.Equal(1, source.Endpoint.Confirmed);
                Assert.Equal(0, session.PermissionAdds);
                Assert.Equal(0, session.SourceAdds);
            });
        }
        Case("完整帐号VM权限通知不误退订其它页面/本地保存监听者", () => Complete(() =>
        {
            var session = new EmbySession();
            var local = new AnonymousLocalSave();
            var permissionEvents = 0;
            var sourceEvents = 0;
            var saveEvents = 0;
            session.PermissionsChanged += (_, _) => permissionEvents++;
            session.SourcesChanged += (_, _) => sourceEvents++;
            local.SaveStateChanged += (_, _) => saveEvents++;
            using var password = new FnNasPasswordViewModel();
            using var preferences = new FnNasPreferencesViewModel();
            AttachPassword(password, session, local);
            preferences.Attach(session);
            var sourceRemoves = session.SourceRemoves;
            var permissionRemoves = session.PermissionRemoves;
            var saveSubscribers = local.Subscribers;
            session.Permission(session.Default, false);
            session.NotifySources();
            session.Permission(session.Default, true);
            local.ObserveResult(false);
            Assert.Equal(2, permissionEvents);
            Assert.Equal(1, sourceEvents);
            Assert.Equal(1, saveEvents);
            Assert.Equal(sourceRemoves, session.SourceRemoves);
            Assert.Equal(permissionRemoves, session.PermissionRemoves);
            Assert.Equal(saveSubscribers, local.Subscribers);
            Assert.Equal(Visibility.Visible, password.ContentVisibility);
            Assert.Equal(Visibility.Visible, preferences.ContentVisibility);
        }));
        Case("完整password等待中编辑后权限更新，旧确认不清新草稿", async () =>
        {
            var session = new EmbySession();
            var local = new AnonymousLocalSave();
            using var vm = new FnNasPasswordViewModel();
            AttachPassword(vm, session, local);
            Draft(vm, FirstPassword);
            var pending = vm.SaveCommand.ExecuteAsync(null);
            Draft(vm, LaterPassword);
            session.Permission(session.Default, false);
            session.Permission(session.Default, true);
            session.Default.Endpoint.Submissions[0].Acknowledged.SetResult();
            await pending;
            SameDraft(vm, LaterPassword);
            Assert.True(vm.NoticeSeverity == InfoBarSeverity.Warning && vm.CanSave);
            Assert.Equal(1, session.Default.Endpoint.Confirmed);
        });
        Case("完整password离页取消仍隔离迟到确认，权限通知不借重附加新草稿", async () =>
        {
            var session = new EmbySession();
            var local = new AnonymousLocalSave();
            using var vm = new FnNasPasswordViewModel();
            AttachPassword(vm, session, local);
            Draft(vm, FirstPassword);
            var pending = vm.SaveCommand.ExecuteAsync(null);
            var old = session.Default.Endpoint.Submissions[0];
            vm.Cancel();
            Assert.True(old.Token.IsCancellationRequested, "Actual navigation cancellation must still work");
            AttachPassword(vm, session, local);
            Draft(vm, LaterPassword);
            session.Permission(session.Default, false);
            session.Permission(session.Default, true);
            old.Acknowledged.SetResult();
            await pending;
            SameDraft(vm, LaterPassword);
            Assert.Equal(1, session.Default.Endpoint.Confirmed);
        });
        Case("完整preferences离页/重入仍取消旧读取票，原任务确认不替新页报成功", async () =>
        {
            var session = new EmbySession();
            using var vm = new FnNasPreferencesViewModel();
            vm.Attach(session);
            await vm.ReloadAsync();
            var pending = vm.SaveCommand.ExecuteAsync(null);
            var old = session.Default.Endpoint.Submissions[0];
            vm.Cancel();
            Assert.True(old.Token.IsCancellationRequested);
            vm.Attach(session);
            await vm.ReloadAsync();
            vm.SubtitleLanguage = "ja-JP";
            old.Acknowledged.SetResult();
            await pending;
            Assert.Equal("ja-JP", vm.SubtitleLanguage);
            Assert.False(vm.NoticeOpen);
        });
#if R010_COMPATIBILITY
        RegisterLocalSave();
#endif
        Case("完整帐号编译边界不含production Session/Store/Auth实现", () => Complete(() =>
        {
            Assert.False(AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name is "Momoka.Core" or "Momoka"));
            Assert.True(typeof(FnNasPasswordViewModel).Assembly == typeof(EmbySession).Assembly);
            Assert.True(typeof(FnNasPreferencesViewModel).Assembly == typeof(EmbySession).Assembly);
        }));
    }

#if R010_COMPATIBILITY
    private static void RegisterLocalSave()
    {
        Case("R010完整password本地欠项订阅遇权限降升不中断，恢复只本地保存一次", async () =>
        {
            var session = new EmbySession();
            var local = new AnonymousLocalSave();
            using var vm = new FnNasPasswordViewModel();
            AttachPassword(vm, session, local);
            session.Default.Endpoint.OnConfirmed = () => local.ObserveResult(false);
            Draft(vm, FirstPassword);
            var pending = vm.SaveCommand.ExecuteAsync(null);
            session.Default.Endpoint.Submissions[0].Acknowledged.SetResult();
            await pending;
            Assert.True(vm.HasUnsavedChanges && vm.CanRetrySaveLocal);
            var subscribers = local.Subscribers;
            session.Permission(session.Default, false);
            session.NotifySources();
            session.Permission(session.Default, true);
            Assert.Equal(subscribers, local.Subscribers);
            Draft(vm, LaterPassword);
            vm.RetrySaveLocalCommand.Execute(null);
            Assert.Equal(1, local.Attempts);
            Assert.Equal(1, session.Default.Endpoint.Submissions.Count);
            Assert.False(vm.HasUnsavedChanges);
            SameDraft(vm, LaterPassword);
        });
        Case("R010完整password后台保存状态通知在权限变更后仍交当前订阅", async () =>
        {
            var session = new EmbySession();
            var local = new AnonymousLocalSave();
            using var vm = new FnNasPasswordViewModel();
            var context = SynchronizationContext.Current!;
            // The fixture deliberately uses its own nonblocking SynchronizationContext queue,
            // not a native UI or JTF. This is the dispatch boundary under test, with no Send/wait.
#pragma warning disable VSTHRD001
            vm.Attach(session, local, action => context.Post(_ => action(), null));
#pragma warning restore VSTHRD001
            await Task.Run(() => local.ObserveResult(false));
            session.Permission(session.Default, false);
            session.Permission(session.Default, true);
            await Task.Yield();
            Assert.True(vm.HasUnsavedChanges);
            await Task.Run(() => local.ObserveResult(true));
            await Task.Yield();
            Assert.False(vm.HasUnsavedChanges);
            Assert.Equal(1, local.Subscribers);
        });
        Case("R010完整password本地重试失败保欠项与新草稿，无再次确认调用", () => Complete(() =>
        {
            var session = new EmbySession();
            var local = new AnonymousLocalSave { NextSuccess = false };
            local.ObserveResult(false);
            using var vm = new FnNasPasswordViewModel();
            AttachPassword(vm, session, local);
            Draft(vm, LaterPassword);
            session.Permission(session.Default, false);
            vm.RetrySaveLocalCommand.Execute(null);
            Assert.True(vm.HasUnsavedChanges);
            Assert.Equal(1, local.Attempts);
            Assert.Equal(0, session.Default.Endpoint.Submissions.Count);
            SameDraft(vm, LaterPassword);
        }));
        Case("R010完整password Cancel/重入只换自己的保存订阅，不删外部监听", () => Complete(() =>
        {
            var session = new EmbySession();
            var first = new AnonymousLocalSave();
            var second = new AnonymousLocalSave();
            var observed = 0;
            first.SaveStateChanged += (_, _) => observed++;
            using var vm = new FnNasPasswordViewModel();
            AttachPassword(vm, session, first);
            Assert.Equal(2, first.Subscribers);
            vm.Cancel();
            Assert.Equal(1, first.Subscribers);
            AttachPassword(vm, session, second);
            Assert.Equal(1, second.Subscribers);
            first.ObserveResult(false);
            Assert.Equal(1, observed);
            Assert.False(vm.HasUnsavedChanges);
            second.ObserveResult(false);
            Assert.True(vm.HasUnsavedChanges);
        }));
        Case("R010完整password权限通知不取消RetrySaveLocal的当前票，实际本地写与状态成功", () => Complete(() =>
        {
            var session = new EmbySession();
            var local = new AnonymousLocalSave();
            local.ObserveResult(false);
            using var vm = new FnNasPasswordViewModel();
            AttachPassword(vm, session, local);
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.Busy) && vm.Busy)
                {
                    session.Permission(session.Default, false);
                    session.NotifySources();
                    session.Permission(session.Default, true);
                }
            };
            vm.RetrySaveLocalCommand.Execute(null);
            Assert.Equal(1, local.Attempts);
            Assert.False(vm.HasUnsavedChanges);
            Assert.Equal(0, session.Default.Endpoint.Submissions.Count);
        }));
    }
#endif
}
