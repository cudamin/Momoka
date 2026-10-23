using System.Reflection;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Tests;

internal static class Program
{
    // Valid-length anonymous placeholders, created only in memory. Never print their values.
    private static readonly string P1 = new('1', 6);
    private static readonly string P2 = new('2', 6);
    private static readonly string[] L1 = ["zh-CN", "en-US"];
    private static readonly string[] L2 = ["ja-JP", "ko-KR"];

    private static int Main()
    {
        PasswordCases();
        PreferencesCases();
        LifetimeCases();
        BindingAndGeneratorCases();
        ConfirmationQueueCases();
        return TestHarness.Run();
    }

    private static void Case(string name, Func<Task> run) => TestHarness.Test(name, () =>
    {
        using var queue = new UiQueue();
        queue.Run(run);
    });

    private static FnNasPasswordViewModel Password(EmbySession session, string? source = null)
    {
        var vm = new FnNasPasswordViewModel();
        vm.Attach(session, source);
        Draft(vm, P1);
        return vm;
    }

    private static async Task<FnNasPreferencesViewModel> PreferencesAsync(EmbySession session, string? source = null)
    {
        var vm = new FnNasPreferencesViewModel();
        vm.Attach(session, source);
        await vm.ReloadAsync();
        Assert.True(vm.Loaded, "执行实际 ReloadAsync 后已读回偏好");
        Draft(vm, L1);
        return vm;
    }

    private static Task SaveDirect(PageViewModel vm)
    {
        var method = vm.GetType().GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method, "必须调用实际生产 SaveAsync，不能复制保存模型");
        return (Task)method!.Invoke(vm, null)!;
    }

    private static void Draft(FnNasPasswordViewModel vm, string password) => vm.NewPassword = vm.ConfirmPassword = password;
    private static void Draft(FnNasPreferencesViewModel vm, string[] languages)
    {
        vm.SubtitleLanguage = languages[0];
        vm.AudioLanguage = languages[1];
    }

    private static void PasswordDraft(FnNasPasswordViewModel vm, string password)
    {
        Assert.True(string.Equals(password, vm.NewPassword, StringComparison.Ordinal), "匿名新密码草稿保留，不打印值");
        Assert.True(string.Equals(password, vm.ConfirmPassword, StringComparison.Ordinal), "匿名确认草稿保留，不打印值");
    }

    private static void PreferenceDraft(FnNasPreferencesViewModel vm, string[] languages)
    {
        Assert.Equal(languages[0], vm.SubtitleLanguage);
        Assert.Equal(languages[1], vm.AudioLanguage);
    }

    private static void PasswordWrite(FixtureEndpoint endpoint, int index, string password)
    {
        Assert.Equal("password", endpoint.Writes[index].Kind);
        Assert.True(string.Equals(password, endpoint.Writes[index].Password, StringComparison.Ordinal), "假确认端点收到提交时的匿名密码快照");
        Assert.Null(endpoint.Writes[index].Body);
    }

    private static void PreferenceWrite(FixtureEndpoint endpoint, int index, string subtitle, string audio)
    {
        var request = endpoint.Writes[index];
        Assert.Equal("preferences", request.Kind);
        Assert.Null(request.Password);
        Assert.Equal(2, request.Body!.Count, "只发送 lan/audio_lan，不拼接其他当前字段");
        Assert.Equal(subtitle, request.Body["lan"]!.ToString());
        Assert.Equal(audio, request.Body["audio_lan"]!.ToString());
    }

    private static void Unsaved(PageViewModel vm)
    {
        Assert.True(vm.NoticeOpen, "新草稿必须有可见反馈");
        Assert.Equal(InfoBarSeverity.Warning, vm.NoticeSeverity, "已确认旧提交不能冒称当前草稿完整成功");
        Assert.Contains("尚未保存", vm.NoticeMessage);
        Assert.Contains("保存修改", vm.NoticeMessage, "明确可通过现有按钮显式重试");
    }

    private static void ConfirmedWithUnsaved(PageViewModel vm)
    {
        Unsaved(vm);
        Assert.Contains("已", vm.NoticeTitle);
        Assert.Contains("本次提交", vm.NoticeMessage, "仍承认确认的写已完成，不能把它说成未发送或失败");
    }

    private static void Saved(PageViewModel vm)
    {
        Assert.True(vm.NoticeOpen);
        Assert.Equal(InfoBarSeverity.Success, vm.NoticeSeverity);
        Assert.False(vm.Busy);
    }

    private static void PasswordCases()
    {
        foreach (var edit in new[] { "两个字段", "仅新密码", "仅确认", "改后返回原值" })
        {
            Case("密码：挂起确认期间" + edit + "保留草稿且不重发", async () =>
            {
                var session = new EmbySession();
                var endpoint = session.Sources[0].Endpoint;
                using var vm = Password(session);
                var save = vm.SaveCommand.ExecuteAsync(null);
                Assert.True(vm.Busy);
                Assert.False(vm.CanSave);
                Assert.Equal(Visibility.Visible, vm.ContentVisibility, "保存只限制提交，编辑区域仍存在");
                if (edit is "两个字段" or "改后返回原值") Draft(vm, P2);
                else if (edit == "仅新密码") vm.NewPassword = P2;
                else vm.ConfirmPassword = P2;
                if (edit == "改后返回原值") Draft(vm, P1);
                PasswordWrite(endpoint, 0, P1);
                endpoint.Writes[0].Succeed();
                await save;
                Assert.True(string.Equals(edit == "仅确认" || edit == "改后返回原值" ? P1 : P2, vm.NewPassword, StringComparison.Ordinal), "保留当前新密码，不打印值");
                Assert.True(string.Equals(edit == "仅新密码" || edit == "改后返回原值" ? P1 : P2, vm.ConfirmPassword, StringComparison.Ordinal), "保留当前确认，不打印值");
                ConfirmedWithUnsaved(vm);
                Assert.True(vm.CanSave);
                Assert.True(vm.SaveCommand.CanExecute(null));
                Assert.Equal(1, endpoint.Writes.Count);
                Assert.Equal(1, endpoint.ConfirmedWrites);
            });
        }

        Case("密码：正常成功只清本次未再编辑的草稿", async () =>
        {
            var session = new EmbySession();
            using var vm = Password(session);
            var save = vm.SaveCommand.ExecuteAsync(null);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            PasswordDraft(vm, "");
            Saved(vm);
            Assert.Equal("密码已修改", vm.NoticeTitle);
            Assert.False(vm.CanSave);
            Assert.False(vm.SaveCommand.CanExecute(null));
        });

        Case("密码：同值赋值不算新编辑，实际生成器只清正常提交", async () =>
        {
            var session = new EmbySession();
            using var vm = Password(session);
            var save = vm.SaveCommand.ExecuteAsync(null);
            Draft(vm, P1);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            PasswordDraft(vm, "");
            Saved(vm);
        });

        Case("密码：Busy通知触发的编辑不能改变已经点击的提交快照", async () =>
        {
            var session = new EmbySession();
            using var vm = Password(session);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Busy) && vm.Busy) Draft(vm, P2); };
            var save = vm.SaveCommand.ExecuteAsync(null);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            PasswordWrite(session.Sources[0].Endpoint, 0, P1);
            PasswordDraft(vm, P2);
            ConfirmedWithUnsaved(vm);
        });

        Case("密码：旧提交确认后仅显式重试才提交新草稿一次", async () =>
        {
            var session = new EmbySession();
            var endpoint = session.Sources[0].Endpoint;
            using var vm = Password(session);
            var first = vm.SaveCommand.ExecuteAsync(null);
            Draft(vm, P2);
            endpoint.Writes[0].Succeed();
            await first;
            PasswordDraft(vm, P2);
            ConfirmedWithUnsaved(vm);
            Assert.Equal(1, endpoint.Writes.Count);
            var retry = vm.SaveCommand.ExecuteAsync(null);
            PasswordWrite(endpoint, 1, P2);
            endpoint.Writes[1].Succeed();
            await retry;
            PasswordDraft(vm, "");
            Saved(vm);
            Assert.Equal(2, endpoint.Writes.Count);
            Assert.Equal(2, endpoint.ConfirmedWrites);
        });

        foreach (var edited in new[] { false, true })
        {
            Case("密码：失败保留" + (edited ? "后续编辑" : "原提交") + "且恢复保存命令", async () =>
            {
                var session = new EmbySession();
                var endpoint = session.Sources[0].Endpoint;
                using var vm = Password(session);
                var save = vm.SaveCommand.ExecuteAsync(null);
                if (edited) Draft(vm, P2);
                endpoint.Writes[0].Fail();
                await save;
                PasswordDraft(vm, edited ? P2 : P1);
                Assert.Equal(InfoBarSeverity.Error, vm.NoticeSeverity);
                Assert.Equal("修改密码失败", vm.NoticeTitle);
                Assert.True(vm.CanSave);
                Assert.True(vm.SaveCommand.CanExecute(null));
                Assert.Equal(1, endpoint.Writes.Count);
                Assert.Equal(0, endpoint.ConfirmedWrites);
            });
        }

        Case("密码：真实SaveAsync拒绝不一致输入且没有发送", async () =>
        {
            var session = new EmbySession();
            using var vm = Password(session);
            vm.ConfirmPassword = P2;
            await SaveDirect(vm);
            Assert.Equal(InfoBarSeverity.Error, vm.NoticeSeverity);
            Assert.Equal(0, session.Sources[0].Endpoint.Writes.Count);
            Assert.True(string.Equals(P1, vm.NewPassword, StringComparison.Ordinal));
            Assert.True(string.Equals(P2, vm.ConfirmPassword, StringComparison.Ordinal));
        });

        Case("密码：Busy时直接调用实际SaveAsync也不重复发送", async () =>
        {
            var session = new EmbySession();
            using var vm = Password(session);
            var save = vm.SaveCommand.ExecuteAsync(null);
            Draft(vm, P2);
            await SaveDirect(vm);
            Assert.Equal(1, session.Sources[0].Endpoint.Writes.Count);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            PasswordDraft(vm, P2);
        });
    }

    private static void ConfirmationQueueCases()
    {
        foreach (var kind in new[] { "密码", "语言" })
        {
            Case(kind + "：假服务已经确认但UI队列未收尾时编辑仍保留新草稿", async () =>
            {
                var session = new EmbySession();
                var endpoint = session.Sources[0].Endpoint;
                using PageViewModel vm = kind == "密码" ? Password(session) : await PreferencesAsync(session);
                var save = SaveDirect(vm);
                endpoint.Writes[0].Succeed();
                // This edit runs synchronously before the controlled UI continuation can resume.
                if (vm is FnNasPasswordViewModel password) Draft(password, P2);
                else Draft((FnNasPreferencesViewModel)vm, L2);
                await save;
                ConfirmedWithUnsaved(vm);
                if (vm is FnNasPasswordViewModel remaining) PasswordDraft(remaining, P2);
                else PreferenceDraft((FnNasPreferencesViewModel)vm, L2);
                Assert.Equal(1, endpoint.Writes.Count);
                Assert.Equal(1, endpoint.ConfirmedWrites);
            });

            Case(kind + "：显式非默认SourceKey未编辑时仍正常成功", async () =>
            {
                var session = new EmbySession();
                var source = session.Sources[1];
                using PageViewModel vm = kind == "密码" ? Password(session, source.Key) : await PreferencesAsync(session, source.Key);
                Assert.False(source.IsCurrent);
                Assert.Equal(source.Key, ((FnNasSessionViewModel)vm).SourceKey);
                var save = SaveDirect(vm);
                source.Endpoint.Writes[0].Succeed();
                await save;
                Saved(vm);
                Assert.Equal(1, source.Endpoint.ConfirmedWrites);
                Assert.Equal(0, session.Sources[0].Endpoint.Writes.Count);
                if (vm is FnNasPasswordViewModel password) PasswordDraft(password, "");
                else PreferenceDraft((FnNasPreferencesViewModel)vm, L1);
            });
        }

        Case("语言：调度前离页取消，延迟回调不得构建或发送任何写", async () =>
        {
            var session = new EmbySession();
            var scope = session.Sources[0];
            using var vm = await PreferencesAsync(session);
            scope.BeforeExecute = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var save = SaveDirect(vm);
            Draft(vm, L2);
            vm.Cancel();
            scope.BeforeExecute.SetResult();
            await save;
            PreferenceDraft(vm, L2);
            Assert.Equal(0, scope.Endpoint.Writes.Count);
            Assert.Equal(0, scope.Endpoint.ConfirmedWrites);
            Assert.False(vm.NoticeOpen);
        });

        Case("语言：失败反馈后继续编辑仍保留错误和显式重试", async () =>
        {
            var session = new EmbySession();
            using var vm = await PreferencesAsync(session);
            var save = SaveDirect(vm);
            session.Sources[0].Endpoint.Writes[0].Fail();
            await save;
            Draft(vm, L2);
            Assert.Equal(InfoBarSeverity.Error, vm.NoticeSeverity);
            Assert.Equal("保存播放偏好失败", vm.NoticeTitle);
            Assert.True(vm.SaveCommand.CanExecute(null));
            PreferenceDraft(vm, L2);
            Assert.Equal(1, session.Sources[0].Endpoint.Writes.Count);
        });

        Case("语言：保存成功后重Attach及Reload不制造未保存提示", async () =>
        {
            var session = new EmbySession();
            using var vm = await PreferencesAsync(session);
            var save = SaveDirect(vm);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            vm.Attach(session, session.Sources[1].Key);
            await vm.ReloadAsync();
            PreferenceDraft(vm, ["de-DE", "fr-FR"]);
            Assert.False(vm.NoticeOpen);
            Assert.True(vm.Loaded);
            Assert.True(vm.SaveCommand.CanExecute(null));
            Assert.Equal(0, session.Sources[1].Endpoint.Writes.Count);
        });
    }

    private static void PreferencesCases()
    {
        foreach (var edit in new[] { "两个字段", "仅字幕", "仅音频", "改后返回原值" })
        {
            Case("语言：挂起假POST期间" + edit + "不称完整已保存", async () =>
            {
                var session = new EmbySession();
                var endpoint = session.Sources[0].Endpoint;
                using var vm = await PreferencesAsync(session);
                var save = vm.SaveCommand.ExecuteAsync(null);
                Assert.True(vm.Busy);
                Assert.False(vm.CanSave);
                Assert.Equal(Visibility.Visible, vm.ContentVisibility);
                if (edit is "两个字段" or "改后返回原值") Draft(vm, L2);
                else if (edit == "仅字幕") vm.SubtitleLanguage = L2[0];
                else vm.AudioLanguage = L2[1];
                if (edit == "改后返回原值") Draft(vm, L1);
                PreferenceWrite(endpoint, 0, L1[0], L1[1]);
                endpoint.Writes[0].Succeed();
                await save;
                Assert.Equal(edit == "仅音频" || edit == "改后返回原值" ? L1[0] : L2[0], vm.SubtitleLanguage);
                Assert.Equal(edit == "仅字幕" || edit == "改后返回原值" ? L1[1] : L2[1], vm.AudioLanguage);
                ConfirmedWithUnsaved(vm);
                Assert.True(vm.CanSave);
                Assert.True(vm.SaveCommand.CanExecute(null));
                Assert.Equal(1, endpoint.Writes.Count);
                Assert.Equal(1, endpoint.ConfirmedWrites);
            });
        }

        Case("语言：正常保存保留选择并给出原完整成功", async () =>
        {
            var session = new EmbySession();
            using var vm = await PreferencesAsync(session);
            var collection = vm.Languages;
            var save = vm.SaveCommand.ExecuteAsync(null);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            PreferenceDraft(vm, L1);
            Saved(vm);
            Assert.Equal("已保存", vm.NoticeTitle);
            Assert.True(ReferenceEquals(collection, vm.Languages));
        });

        Case("语言：同值生成属性赋值不误报新草稿", async () =>
        {
            var session = new EmbySession();
            using var vm = await PreferencesAsync(session);
            var save = vm.SaveCommand.ExecuteAsync(null);
            Draft(vm, L1);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            Saved(vm);
        });

        Case("语言：Busy同步通知再编辑仍发送点击时L1而非当前L2", async () =>
        {
            var session = new EmbySession();
            using var vm = await PreferencesAsync(session);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Busy) && vm.Busy) Draft(vm, L2); };
            var save = vm.SaveCommand.ExecuteAsync(null);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            PreferenceWrite(session.Sources[0].Endpoint, 0, L1[0], L1[1]);
            PreferenceDraft(vm, L2);
            ConfirmedWithUnsaved(vm);
        });

        Case("语言：执行回调延迟也不能用当前草稿重建server write", async () =>
        {
            var session = new EmbySession();
            var scope = session.Sources[0];
            using var vm = await PreferencesAsync(session);
            scope.BeforeExecute = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var save = vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal(0, scope.Endpoint.Writes.Count);
            Draft(vm, L2);
            scope.BeforeExecute.SetResult();
            // Await a queue turn after the controlled ExecuteAsync gate, never sleep for timing.
            await Task.Yield();
            while (scope.Endpoint.Writes.Count == 0) await Task.Yield();
            PreferenceWrite(scope.Endpoint, 0, L1[0], L1[1]);
            scope.Endpoint.Writes[0].Succeed();
            await save;
            PreferenceDraft(vm, L2);
            ConfirmedWithUnsaved(vm);
            Assert.Equal(1, scope.Endpoint.Writes.Count);
        });

        Case("语言：any及null只归一化提交快照，不增加写字段", async () =>
        {
            var session = new EmbySession();
            using var vm = await PreferencesAsync(session);
            vm.SubtitleLanguage = "any";
            vm.AudioLanguage = null!;
            var save = vm.SaveCommand.ExecuteAsync(null);
            Draft(vm, L2);
            PreferenceWrite(session.Sources[0].Endpoint, 0, "", "");
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            PreferenceDraft(vm, L2);
            Unsaved(vm);
        });

        Case("语言：确认L1后L2未保存且显式重试才发第二次", async () =>
        {
            var session = new EmbySession();
            var endpoint = session.Sources[0].Endpoint;
            using var vm = await PreferencesAsync(session);
            var save = vm.SaveCommand.ExecuteAsync(null);
            Draft(vm, L2);
            endpoint.Writes[0].Succeed();
            await save;
            ConfirmedWithUnsaved(vm);
            Assert.Equal(1, endpoint.Writes.Count);
            Assert.True(vm.SaveCommand.CanExecute(null));
            var retry = vm.SaveCommand.ExecuteAsync(null);
            PreferenceWrite(endpoint, 1, L2[0], L2[1]);
            endpoint.Writes[1].Succeed();
            await retry;
            Saved(vm);
            PreferenceDraft(vm, L2);
            Assert.Equal(2, endpoint.Writes.Count);
            Assert.Equal(2, endpoint.ConfirmedWrites);
        });

        foreach (var edited in new[] { false, true })
        {
            Case("语言：假POST失败保留" + (edited ? "L2" : "L1") + "且不自动重发", async () =>
            {
                var session = new EmbySession();
                var endpoint = session.Sources[0].Endpoint;
                using var vm = await PreferencesAsync(session);
                var save = vm.SaveCommand.ExecuteAsync(null);
                if (edited) Draft(vm, L2);
                endpoint.Writes[0].Fail();
                await save;
                PreferenceDraft(vm, edited ? L2 : L1);
                Assert.Equal(InfoBarSeverity.Error, vm.NoticeSeverity);
                Assert.Equal("保存播放偏好失败", vm.NoticeTitle);
                Assert.True(vm.SaveCommand.CanExecute(null));
                Assert.Equal(1, endpoint.Writes.Count);
                Assert.Equal(0, endpoint.ConfirmedWrites);
            });
        }

        Case("语言：真实Reload先读user与config，集合身份及Loaded门保留", async () =>
        {
            var session = new EmbySession();
            using var vm = new FnNasPreferencesViewModel();
            var collection = vm.Languages;
            vm.Attach(session);
            Assert.False(vm.Loaded);
            Assert.False(vm.CanSave);
            await SaveDirect(vm);
            Assert.Equal(0, session.Sources[0].Endpoint.Writes.Count);
            await vm.ReloadAsync();
            Assert.True(vm.Loaded);
            Assert.True(vm.CanSave);
            Assert.Equal(1, session.Sources[0].Endpoint.UserReads);
            Assert.Equal(1, session.Sources[0].Endpoint.ConfigurationReads);
            Assert.True(ReferenceEquals(collection, vm.Languages));
            Assert.True(vm.Languages.Any(choice => choice.Id == "any"));
        });

        Case("语言：Busy时直接调用SaveAsync仍只有首次提交", async () =>
        {
            var session = new EmbySession();
            using var vm = await PreferencesAsync(session);
            var save = vm.SaveCommand.ExecuteAsync(null);
            Draft(vm, L2);
            await SaveDirect(vm);
            Assert.Equal(1, session.Sources[0].Endpoint.Writes.Count);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
        });
    }

    private static void LifetimeCases()
    {
        foreach (var kind in new[] { "密码", "语言" })
        {
            foreach (var boundary in new[] { "离页", "注销", "切默认来源", "同源重Attach", "异源重Attach", "非默认显式源", "显式源注销" })
            {
                Case(kind + "：" + boundary + "保留实际Current及确认边界", async () =>
                {
                    var session = new EmbySession();
                    var explicitSource = boundary is "非默认显式源" or "显式源注销";
                    var source = explicitSource ? session.Sources[1] : session.Sources[0];
                    using PageViewModel vm = kind == "密码"
                        ? Password(session, explicitSource ? source.Key : null)
                        : await PreferencesAsync(session, explicitSource ? source.Key : null);
                    var command = vm is FnNasPasswordViewModel password ? password.SaveCommand : ((FnNasPreferencesViewModel)vm).SaveCommand;
                    var save = command.ExecuteAsync(null);
                    if (vm is FnNasPasswordViewModel passwordDraft) Draft(passwordDraft, P2);
                    else Draft((FnNasPreferencesViewModel)vm, L2);
                    switch (boundary)
                    {
                        case "离页": vm.Cancel(); break;
                        case "注销":
                        case "显式源注销": source.IsAvailable = false; break;
                        case "切默认来源": session.DefaultSource = session.Sources[1]; break;
                        case "同源重Attach":
                        case "异源重Attach":
                            var next = boundary == "同源重Attach" ? source.Key : session.Sources[1].Key;
                            ((FnNasSessionViewModel)vm).Attach(session, next);
                            if (vm is FnNasPasswordViewModel reattachedPassword) Draft(reattachedPassword, P2);
                            else
                            {
                                await vm.ReloadAsync();
                                Draft((FnNasPreferencesViewModel)vm, L2);
                            }
                            break;
                        case "非默认显式源":
                            Assert.False(source.IsCurrent);
                            // Revoke/change the default, while the captured explicit source stays available.
                            session.Sources[0].IsAvailable = false;
                            session.DefaultSource = null;
                            break;
                    }
                    source.Endpoint.Writes[0].Succeed();
                    await save;
                    Assert.Equal(1, source.Endpoint.Writes.Count, "已发送的假写没有取消或重发");
                    Assert.Equal(1, source.Endpoint.ConfirmedWrites, "后续页面失效不把已确认写误当没发送");
                    if (boundary == "非默认显式源")
                    {
                        ConfirmedWithUnsaved(vm);
                        Assert.True(((FnNasSessionViewModel)vm).CanUse);
                    }
                    else Assert.False(vm.NoticeOpen, "旧响应不得把新页面或失效来源标为已保存");
                    if (vm is FnNasPasswordViewModel remainingPassword)
                        PasswordDraft(remainingPassword, boundary == "离页" ? "" : P2);
                    else PreferenceDraft((FnNasPreferencesViewModel)vm, L2);
                    Assert.Equal(0, session.Sources[explicitSource ? 0 : 1].Endpoint.Writes.Count, "旧响应不借用另一个source发送");
                });
            }

            Case(kind + "：异源重Attach的新请求不被旧finally结束", async () =>
            {
                var session = new EmbySession();
                using PageViewModel vm = kind == "密码" ? Password(session) : await PreferencesAsync(session);
                var first = SaveDirect(vm);
                ((FnNasSessionViewModel)vm).Attach(session, session.Sources[1].Key);
                if (vm is FnNasPasswordViewModel p) Draft(p, P2);
                else
                {
                    await vm.ReloadAsync();
                    Draft((FnNasPreferencesViewModel)vm, L2);
                }
                var second = SaveDirect(vm);
                session.Sources[0].Endpoint.Writes[0].Succeed();
                await first;
                Assert.True(vm.Busy, "旧EndLoad不能关闭新请求的busy");
                Assert.False(vm.NoticeOpen);
                session.Sources[1].Endpoint.Writes[0].Succeed();
                await second;
                Saved(vm);
                Assert.Equal(1, session.Sources[0].Endpoint.ConfirmedWrites);
                Assert.Equal(1, session.Sources[1].Endpoint.ConfirmedWrites);
                if (vm is FnNasPasswordViewModel password) PasswordDraft(password, "");
                else PreferenceDraft((FnNasPreferencesViewModel)vm, L2);
            });

            Case(kind + "：离页后旧失败不回填错误或恢复保存", async () =>
            {
                var session = new EmbySession();
                using PageViewModel vm = kind == "密码" ? Password(session) : await PreferencesAsync(session);
                var save = SaveDirect(vm);
                vm.Cancel();
                session.Sources[0].Endpoint.Writes[0].Fail();
                await save;
                Assert.False(vm.NoticeOpen);
                Assert.False(((FnNasSessionViewModel)vm).CanUse);
                Assert.Equal(1, session.Sources[0].Endpoint.Writes.Count);
                Assert.Equal(0, session.Sources[0].Endpoint.ConfirmedWrites);
            });

            foreach (var boundary in new[] { "未Attach", "空输入或未读", "未知SourceKey", "非飞牛来源", "来源已撤销", "已Cancel" })
            {
                Case(kind + "：" + boundary + "直接SaveAsync也零发送", async () =>
                {
                    var session = new EmbySession();
                    using PageViewModel vm = kind == "密码" ? new FnNasPasswordViewModel() : new FnNasPreferencesViewModel();
                    var attached = (FnNasSessionViewModel)vm;
                    if (boundary != "未Attach") attached.Attach(session, boundary == "未知SourceKey" ? "missing-source" : null);
                    if (boundary != "空输入或未读")
                    {
                        if (vm is FnNasPasswordViewModel password) Draft(password, P1);
                        else ((FnNasPreferencesViewModel)vm).Loaded = true;
                    }
                    if (boundary == "非飞牛来源") session.Sources[0].Connection = new(false);
                    if (boundary == "来源已撤销") session.Sources[0].IsAvailable = false;
                    if (boundary == "已Cancel") vm.Cancel();
                    await SaveDirect(vm);
                    Assert.False(attached.CanUse && (vm is FnNasPasswordViewModel p ? p.CanSave : ((FnNasPreferencesViewModel)vm).CanSave));
                    Assert.Equal(0, session.Sources.Sum(scope => scope.Endpoint.Writes.Count));
                });
            }
        }
    }

    private static void BindingAndGeneratorCases()
    {
        Case("生成器：真实密码属性编辑通知CanSave及SaveCommand", () =>
        {
            var session = new EmbySession();
            using var vm = new FnNasPasswordViewModel();
            vm.Attach(session);
            var properties = new List<string?>();
            var commandChanges = 0;
            vm.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
            vm.SaveCommand.CanExecuteChanged += (_, _) => commandChanges++;
            vm.NewPassword = P1;
            vm.ConfirmPassword = P1;
            Assert.Contains(nameof(vm.NewPassword), string.Join(',', properties));
            Assert.Contains(nameof(vm.ConfirmPassword), string.Join(',', properties));
            Assert.Contains(nameof(vm.CanSave), string.Join(',', properties));
            Assert.True(commandChanges >= 2);
            Assert.True(vm.SaveCommand.CanExecute(null));
            return Task.CompletedTask;
        });

        Case("生成器：语言成功后再编辑不能沿用完整已保存提示", async () =>
        {
            var session = new EmbySession();
            using var vm = await PreferencesAsync(session);
            var save = vm.SaveCommand.ExecuteAsync(null);
            session.Sources[0].Endpoint.Writes[0].Succeed();
            await save;
            Saved(vm);
            var properties = new List<string?>();
            vm.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
            Draft(vm, L2);
            Unsaved(vm);
            Assert.Contains(nameof(vm.SubtitleLanguage), string.Join(',', properties));
            Assert.Contains(nameof(vm.AudioLanguage), string.Join(',', properties));
            Assert.Contains(nameof(vm.NoticeSeverity), string.Join(',', properties));
            Assert.Contains(nameof(vm.NoticeMessage), string.Join(',', properties));
            Assert.True(vm.SaveCommand.CanExecute(null));
            Assert.Equal(1, session.Sources[0].Endpoint.Writes.Count);
        });

        foreach (var page in new[] { "FnNasPasswordPage", "FnNasPreferencesPage" })
        {
            Case("真实XAML：" + page + "编辑与反馈绑定和离页SourceKey接线", () =>
            {
                var xaml = XDocument.Parse(Resource(page + ".xaml"));
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var fields = page == "FnNasPasswordPage"
                    ? new[] { ("FnNasPasswordNew", "Password", "NewPassword"), ("FnNasPasswordConfirm", "Password", "ConfirmPassword") }
                    : new[] { ("FnNasSubtitleLanguage", "SelectedValue", "SubtitleLanguage"), ("FnNasAudioLanguage", "SelectedValue", "AudioLanguage") };
                foreach (var (name, property, member) in fields)
                {
                    var element = xaml.Descendants().Single(node => (string?)node.Attribute(x + "Name") == name);
                    var binding = element.Attribute(property)!.Value;
                    Assert.Contains("x:Bind ViewModel." + member, binding);
                    Assert.Contains("Mode=TwoWay", binding);
                    if (property == "Password") Assert.Contains("UpdateSourceTrigger=PropertyChanged", binding);
                    Assert.Null(element.Attribute("IsEnabled"), "本题不冻结输入；编辑版本必须处理实际双向输入");
                }
                var info = xaml.Descendants(presentation + "InfoBar").Single();
                foreach (var (property, member) in new[] { ("Title", "NoticeTitle"), ("Message", "NoticeMessage"), ("Severity", "NoticeSeverity"), ("IsOpen", "NoticeOpen"), ("Visibility", "NoticeVisibility") })
                {
                    Assert.Contains("x:Bind ViewModel." + member, info.Attribute(property)!.Value);
                    Assert.Contains("Mode=OneWay", info.Attribute(property)!.Value);
                }
                var button = xaml.Descendants(presentation + "Button").Single();
                Assert.Equal("{x:Bind ViewModel.SaveCommand}", button.Attribute("Command")!.Value);
                var code = Resource(page + ".xaml.cs");
                Assert.Contains("ViewModel.Attach(request.Services.GetRequiredService<EmbySession>(), request.SourceKey);", code);
                Assert.Contains("ViewModel.Cancel();", code);
                Assert.Contains("public string? SourceKey => ViewModel.SourceKey;", code);
                return Task.CompletedTask;
            });
        }

        Case("离线安全：无实际CoreShellSession认证凭据或WinUI程序集", () =>
        {
            var references = typeof(FnNasPasswordViewModel).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
            Assert.False(references.Any(name => name is "Momoka.Core" or "Momoka" || name!.StartsWith("Microsoft.UI.", StringComparison.Ordinal)));
            Assert.Equal(typeof(Program).Assembly, typeof(EmbySession).Assembly, "Session只是本fixture窄确认边界，不加载生产认证链");
            return Task.CompletedTask;
        });
    }

    private static string Resource(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("Production." + name);
        Assert.NotNull(stream, "XAML及页接线必须由本树真实文件在编译时嵌入");
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
