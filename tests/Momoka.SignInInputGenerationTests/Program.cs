using System.CodeDom.Compiler;
using System.Reflection;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class Program
{
    private const string DraftA = "anonymous-draft-a";
    private const string DraftB = "anonymous-draft-b";

    private static int Main()
    {
        RegisterContracts();
        RegisterInputChanges();
        RegisterTicketOwnership();
        RegisterCurrentness();
        RegisterNullSelectionTests();
        RegisterSynchronousEntryTests();
        RegisterSynchronousTailTests();
        return Run();
    }

    private static void RegisterInputChanges()
    {
        foreach (var selection in new[] { false, true })
        {
            var input = selection ? "实际SelectedUser B" : "实际Username B";
            Case($"R008 {input}撤销迟到A且保草稿并可再次提交B", async () =>
            {
                using var f = await Fixture.CreateAsync();
                var aTask = f.Submit();
                var a = f.Session.Attempts.Single();
                f.ChangeToB(selection);
                var cancellationWasSynchronous = a.Token.IsCancellationRequested && a.CancellationObserved;
                a.Confirm();
                await aTask;
                CheckAbandonedA(f, a, cancellationWasSynchronous);
                CheckBDraft(f);
                Assert.True(f.Vm.SignInCommand.CanExecute(null), "取消完成后B仍必须可提交");
                var bTask = f.Submit();
                var b = f.Session.Attempts.Last();
                Assert.False(a.Token == b.Token, "B必须有自己的新票");
                CheckSnapshot(b, f, "b", DraftB, false);
                b.Confirm();
                await bTask;
                Assert.Equal("b", f.Session.Adopted.Single().Username);
                Assert.Equal("b", f.SignedInUsers.Single());
                Assert.Equal(1, f.Settings.Saves);
                Assert.False(f.Vm.Busy);
                Assert.True(f.Vm.IsReady);
            });

            Case($"R008 {input}不让迟到失败覆盖B提示或焦点", async () =>
            {
                using var f = await Fixture.CreateAsync();
                var aTask = f.Submit();
                var a = f.Session.Attempts.Single();
                f.ChangeToB(selection);
                var focusCount = f.Focus.Count;
                a.Reject("anonymous delayed failure");
                await aTask;
                Assert.False(f.Vm.NoticeOpen, "A的迟到失败不是B的错误");
                Assert.Equal(focusCount, f.Focus.Count, "旧错误不能聚焦/选择B的新密码");
                Assert.True(a.CancellationObserved);
                Assert.Equal(true, a.CancelledAtConfirmation);
                CheckBDraft(f);
                Assert.False(f.Vm.Busy);
                Assert.Equal(0, f.Settings.Saves);
                Assert.Equal(0, f.SignedInUsers.Count);
            });

            Case($"R008 {input}即使端点仅返回成功也拒绝旧VM完成通知", async () =>
            {
                using var f = await Fixture.CreateAsync();
                var aTask = f.Submit();
                var a = f.Session.Attempts.Single();
                f.ChangeToB(selection);
                a.ReturnWithoutCommit();
                await aTask;
                Assert.Equal(0, f.SignedInUsers.Count, "旧VM回调不能SignedIn");
                Assert.Equal(0, f.Settings.Saves, "旧VM回调不能Save");
                Assert.Equal(0, f.Session.Adopted.Count, "此故障注入端点根本没有commit能力");
                Assert.True(a.CancellationObserved);
                CheckBDraft(f);
                Assert.False(f.Vm.Busy);
            });

            foreach (var newFirst in new[] { false, true })
            {
                Case($"R008 {input}在A未返回时按钮可提B/{(newFirst ? "B先A后" : "A先B后")}", async () =>
                {
                    using var f = await Fixture.CreateAsync();
                    var aTask = f.Submit();
                    var a = f.Session.Attempts.Single();
                    f.ChangeToB(selection);
                    var availableWhileAWaits = f.Vm.SignInCommand.CanExecute(null);
                    // Execute the real generated command even on the red implementation, so its
                    // original Busy guard (rather than a fixture gate) decides whether B is sent.
                    var bTask = f.Vm.SignInCommand.ExecuteAsync(null);
                    var b = f.Session.Attempts.Skip(1).SingleOrDefault();
                    var bCountBeforeAnyConfirmation = f.Session.Attempts.Count;
                    if (newFirst) b?.Confirm();
                    if (newFirst) await bTask;
                    a.Confirm();
                    await aTask;
                    var busyAfterOld = f.Vm.Busy;
                    var captionAfterOld = f.Vm.Caption;
                    if (!newFirst) b?.Confirm();
                    await bTask;
                    Assert.Equal(2, bCountBeforeAnyConfirmation, "不等待A的远端响应才能提交B");
                    Assert.True(availableWhileAWaits, "真实Toolkit命令不能因A旧ExecutionTask继续禁B");
                    Assert.NotNull(b);
                    CheckAbandonedA(f, a, a.CancellationObserved);
                    Assert.False(a.Token == b!.Token);
                    Assert.False(b.Token.IsCancellationRequested, "A的finally不能取消B");
                    Assert.Equal(!newFirst, busyAfterOld, "Busy必须归仍在飞的B所有");
                    if (!newFirst) Assert.Equal("正在登录…", captionAfterOld);
                    Assert.Equal("b", f.Session.Adopted.Single().Username);
                    Assert.Equal("b", f.SignedInUsers.Single());
                    Assert.Equal(1, f.Settings.Saves);
                    Assert.False(f.Vm.Busy);
                    Assert.True(f.Vm.IsReady);
                });
            }
        }

        foreach (var newFirst in new[] { false, true })
        {
            Case($"R008 无密码B自动用新票/{(newFirst ? "B先A后" : "A先B后")}", async () =>
            {
                using var f = await Fixture.CreateAsync(passwordlessB: true);
                var aTask = f.Submit();
                var a = f.Session.Attempts.Single();
                f.Vm.SelectedUser = f.UserB;
                var b = f.Session.Attempts.Skip(1).SingleOrDefault();
                var attemptCountBeforeConfirmation = f.Session.Attempts.Count;
                if (newFirst) b?.Confirm();
                if (newFirst) await NextTurnAsync();
                a.Confirm();
                await aTask;
                var busyAfterOld = f.Vm.Busy;
                if (!newFirst) b?.Confirm();
                await NextTurnAsync();
                Assert.Equal(2, attemptCountBeforeConfirmation, "选无密码B要正常自动提交，不等A也不复用A票");
                Assert.NotNull(b);
                Assert.Equal("b", b!.Snapshot.Username);
                Assert.Equal(f.AccountB.Id, b.Snapshot.AccountId);
                Assert.Equal("", b.Snapshot.Password);
                Assert.True(b.Snapshot.Remember);
                Assert.False(a.Token == b.Token);
                CheckAbandonedA(f, a, a.CancellationObserved);
                Assert.Equal(!newFirst, busyAfterOld, "旧A完成不能结束B的Busy");
                Assert.Equal("b", f.SignedInUsers.Single());
                Assert.Equal("b", f.Session.Adopted.Single().Username);
                Assert.Equal(1, f.Settings.Saves);
                Assert.False(f.Vm.Busy);
            });
        }

        Case("R008 不同用户ID即使名称等价也同步撤销A", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var aTask = f.Submit();
            var a = f.Session.Attempts.Single();
            f.Vm.SelectedUser = new() { Id = "different-user", Name = " A ", HasPassword = true };
            f.Vm.Password = DraftB;
            var cancelled = a.Token.IsCancellationRequested;
            a.Confirm();
            await aTask;
            CheckAbandonedA(f, a, cancelled);
            Assert.Equal(" A ", f.Vm.Username);
            Assert.Equal(DraftB, f.Vm.Password);
            Assert.Equal("different-user", f.Vm.SelectedUser!.Id);
            Assert.True(f.Vm.SignInCommand.CanExecute(null));
        });

        Case("R008 用户名A到B再到A不复活原票", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var aTask = f.Submit();
            var a = f.Session.Attempts.Single();
            f.ChangeToB(false);
            f.Vm.Username = "a";
            f.Vm.Password = "anonymous-a-again";
            var canSubmit = f.Vm.SignInCommand.CanExecute(null);
            var againTask = f.Vm.SignInCommand.ExecuteAsync(null);
            var again = f.Session.Attempts.Skip(1).SingleOrDefault();
            again?.Confirm();
            await againTask;
            a.Confirm();
            await aTask;
            Assert.True(canSubmit, "ABA也要让新请求可提交");
            Assert.NotNull(again);
            CheckAbandonedA(f, a, a.CancellationObserved);
            Assert.False(a.Token == again!.Token);
            Assert.Equal("anonymous-a-again", f.Session.Adopted.Single().Password);
            Assert.Equal(1, f.SignedInUsers.Count);
            Assert.False(f.Vm.Busy);
        });

        Case("R008 已完成B之后A迟到不能清第三份未提交草稿", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var aTask = f.Submit();
            var a = f.Session.Attempts.Single();
            f.ChangeToB(false);
            var bTask = f.Vm.SignInCommand.ExecuteAsync(null);
            var b = f.Session.Attempts.Skip(1).SingleOrDefault();
            b?.Confirm();
            await bTask;
            f.Vm.Username = "b";
            f.Vm.Password = "anonymous-third-draft";
            f.Vm.Remember = true;
            a.Confirm();
            await aTask;
            Assert.NotNull(b, "必须已在A等待期间提交B");
            CheckAbandonedA(f, a, a.CancellationObserved);
            Assert.Equal("b", f.Vm.Username);
            Assert.Equal("anonymous-third-draft", f.Vm.Password);
            Assert.True(f.Vm.Remember);
            Assert.Equal(1, f.SignedInUsers.Count);
            Assert.Equal("b", f.SignedInUsers.Single());
            Assert.False(f.Vm.Busy);
        });

        Case("R008 未进入凭据步的显式提交也按实际用户名撤销", async () =>
        {
            using var f = new Fixture();
            var aTask = f.Submit();
            var a = f.Session.Attempts.Single();
            f.ChangeToB(false);
            a.Confirm();
            await aTask;
            CheckAbandonedA(f, a, a.CancellationObserved);
            Assert.Equal(SignInStep.Address, f.Vm.Step);
            Assert.Equal(DraftB, f.Vm.Password);
            Assert.False(f.Vm.Busy);
            Assert.True(f.Vm.SignInCommand.CanExecute(null));
        });
    }

    private static void RegisterTicketOwnership()
    {
        foreach (var passwordless in new[] { false, true })
        {
            Case($"R008 A先收尾后仍能用B当前票撤销{(passwordless ? "自动" : "手动")}B", async () =>
            {
                using var f = await Fixture.CreateAsync(passwordlessB: passwordless);
                var aTask = f.Submit();
                var a = f.Session.Attempts.Single();
                f.Vm.SelectedUser = f.UserB;
                Task bTask = Task.CompletedTask;
                if (!passwordless)
                {
                    f.Vm.Password = DraftB;
                    bTask = f.Vm.SignInCommand.ExecuteAsync(null);
                }
                var b = f.Session.Attempts.Skip(1).SingleOrDefault();
                a.Confirm();
                await aTask;
                var busyBeforeSecondEdit = f.Vm.Busy;
                f.Vm.Username = "a";
                f.Vm.Password = "anonymous-third-draft";
                var bCancelledSynchronously = b?.Token.IsCancellationRequested == true && b.CancellationObserved;
                b?.Confirm();
                await bTask;
                await NextTurnAsync();
                Assert.NotNull(b, "B必须在A返回前已经提交");
                Assert.True(busyBeforeSecondEdit, "A的finally不能结束B");
                Assert.True(bCancelledSynchronously, "A的finally不能清掉B的当前票，下一次编辑仍可撤销B");
                CheckAbandonedA(f, a, a.CancellationObserved);
                Assert.Equal(true, b!.CancelledAtConfirmation);
                Assert.Equal(0, f.Session.Adopted.Count);
                Assert.Equal(0, f.SignedInUsers.Count);
                Assert.Equal(0, f.Settings.Saves);
                Assert.Equal("a", f.Vm.Username);
                Assert.Equal("anonymous-third-draft", f.Vm.Password);
                Assert.False(f.Vm.Busy);
                Assert.True(f.Vm.SignInCommand.CanExecute(null));
            });
        }

        Case("R008 选有密码B保原保存密码默认Remember及密码焦点", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var task = f.Submit();
            var a = f.Session.Attempts.Single();
            f.Vm.SelectedUser = f.UserB;
            var before = f.Vm.Password;
            a.Confirm();
            await task;
            CheckAbandonedA(f, a, a.CancellationObserved);
            Assert.Equal("anonymous-saved-b", before);
            Assert.Equal(before, f.Vm.Password, "取消旧A不能擦除原选B行为填好的草稿");
            Assert.True(f.Vm.Remember);
            Assert.Equal(SignInFocus.Password, f.Focus.Last());
            Assert.Equal(SignInStep.Credential, f.Vm.Step);
            Assert.False(f.Vm.Busy);
        });

        Case("R008 B当前失败后A旧成功不覆盖B的失败提示和草稿", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var aTask = f.Submit();
            var a = f.Session.Attempts.Single();
            f.ChangeToB(false);
            var bTask = f.Vm.SignInCommand.ExecuteAsync(null);
            var b = f.Session.Attempts.Skip(1).SingleOrDefault();
            b?.Reject("anonymous B current failure");
            await bTask;
            var focusCount = f.Focus.Count;
            a.Confirm();
            await aTask;
            Assert.NotNull(b);
            CheckAbandonedA(f, a, a.CancellationObserved);
            Assert.True(f.Vm.NoticeOpen);
            Assert.Equal("anonymous B current failure", f.Vm.NoticeMessage);
            Assert.Equal(focusCount, f.Focus.Count);
            CheckBDraft(f);
            Assert.False(f.Vm.Busy);
            Assert.True(f.Vm.IsReady);
            Assert.True(f.Vm.SignInCommand.CanExecute(null));
            Assert.Equal(0, f.Session.Adopted.Count);
            Assert.Equal(0, f.Settings.Saves);
            Assert.Equal(0, f.SignedInUsers.Count);
        });
    }
    private static void RegisterCurrentness()
    {
        Case("R008 等价用户名编辑保当前票与提交快照", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var task = f.Submit();
            var attempt = f.Session.Attempts.Single();
            f.Vm.Username = " A ";
            Assert.Equal(DraftA, f.Vm.Password);
            Assert.False(attempt.Token.IsCancellationRequested);
            Assert.False(attempt.CancellationObserved);
            Assert.True(f.Vm.Busy);
            CheckSnapshot(attempt, f, "a", DraftA, false);
            attempt.Confirm();
            await task;
            CheckCurrentSuccess(f, attempt);
        });

        Case("R008 同ID用户对象复制与等价名称不撤销当前票", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var task = f.Submit();
            var attempt = f.Session.Attempts.Single();
            f.Vm.SelectedUser = new() { Id = f.UserA.Id, Name = " A ", HasPassword = true };
            Assert.False(attempt.Token.IsCancellationRequested);
            Assert.True(f.Vm.Busy);
            Assert.Equal(1, f.Session.Attempts.Count);
            CheckSnapshot(attempt, f, "a", DraftA, false);
            attempt.Confirm();
            await task;
            CheckCurrentSuccess(f, attempt);
        });

        Case("R008 正常列表空选择不等于换账号", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var task = f.Submit();
            var attempt = f.Session.Attempts.Single();
            f.Vm.SelectedUser = null;
            Assert.False(attempt.Token.IsCancellationRequested);
            Assert.Equal("a", f.Vm.Username);
            Assert.Equal(DraftA, f.Vm.Password);
            attempt.Confirm();
            await task;
            CheckCurrentSuccess(f, attempt);
        });

        Case("R008 原用户名重复提交仍被真实Busy与命令门控阻挡", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var task = f.Submit();
            var attempt = f.Session.Attempts.Single();
            Assert.False(f.Vm.SignInCommand.CanExecute(null));
            await f.Vm.SignInCommand.ExecuteAsync(null);
            await f.Vm.SignInCommand.ExecuteAsync(null);
            Assert.Equal(1, f.Session.Attempts.Count);
            Assert.False(attempt.Token.IsCancellationRequested);
            Assert.True(f.Vm.Busy);
            attempt.Confirm();
            await task;
            CheckCurrentSuccess(f, attempt);
            Assert.True(f.Vm.SignInCommand.CanExecute(null));
        });

        Case("R008 _filling默认无密码账号只填表不自动登录或取消connect", async () =>
        {
            using var f = await Fixture.CreateAsync(passwordlessA: true);
            Assert.Equal(f.UserA, f.Vm.SelectedUser);
            Assert.Equal(0, f.Session.Attempts.Count);
            Assert.False(f.Session.Gateway.DetectTokens.Single().IsCancellationRequested);
            Assert.False(f.Vm.Busy);
            Assert.True(f.Vm.IsReady);
            Assert.Equal(SignInStep.Credential, f.Vm.Step);
            Assert.Equal(SignInFocus.Password, f.Focus.Last());
        });

        Case("R008 connect等待时编辑用户名与默认填B不取消非登录载入", async () =>
        {
            using var f = new Fixture(passwordlessB: true);
            f.Session.Gateway.HeldDetection = new();
            var task = f.Vm.ConnectCommand.ExecuteAsync(null);
            var token = f.Session.Gateway.DetectTokens.Single();
            f.Vm.Username = "b";
            Assert.False(token.IsCancellationRequested, "仅身份编辑只撤销在飞登录，不破坏公开用户步骤");
            Assert.True(f.Vm.Busy);
            f.Session.Gateway.HeldDetection.SetResult(f.Session.Gateway.Info);
            await task;
            Assert.False(token.IsCancellationRequested);
            Assert.Equal(f.UserB, f.Vm.SelectedUser);
            Assert.Equal("b", f.Vm.Username);
            Assert.Equal("", f.Vm.Password);
            Assert.Equal(0, f.Session.Attempts.Count, "_filling预选无密码B不能自动登录");
            Assert.False(f.Vm.Busy);
            Assert.True(f.Vm.IsReady);
        });

        Case("R008 实际ShowCredentialStep的_filling不误撤销在飞票", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var task = f.Submit();
            var attempt = f.Session.Attempts.Single();
            var fill = typeof(SignInViewModel).GetMethod("ShowCredentialStep", BindingFlags.Instance | BindingFlags.NonPublic)!;
            fill.Invoke(f.Vm, [new List<EmbyUser> { new() { Id = "refilled-a", Name = "a", HasPassword = false }, f.UserB }]);
            Assert.False(attempt.Token.IsCancellationRequested);
            Assert.Equal(1, f.Session.Attempts.Count, "实际填默认不能新发登录");
            Assert.Equal(DraftA, f.Vm.Password);
            attempt.Confirm();
            await task;
            CheckCurrentSuccess(f, attempt);
        });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
        {
            Case($"R008 {kind}等价地址编辑不撤销当前票", async () =>
            {
                using var f = await Fixture.CreateAsync(kind: kind);
                var task = f.Submit();
                var attempt = f.Session.Attempts.Single();
                f.Vm.Address = " " + f.Session.Gateway.Info.ApiBase!.AbsoluteUri + " ";
                Assert.False(attempt.Token.IsCancellationRequested);
                Assert.Equal(DraftA, f.Vm.Password);
                Assert.Equal(SignInStep.Credential, f.Vm.Step);
                attempt.Confirm();
                await task;
                CheckCurrentSuccess(f, attempt);
            });

            Case($"R008 {kind}真实地址改变保原回退与取消", async () =>
            {
                using var f = await Fixture.CreateAsync(kind: kind);
                var task = f.Submit();
                var attempt = f.Session.Attempts.Single();
                f.Vm.Address = "https://other.invalid/media";
                Assert.True(attempt.Token.IsCancellationRequested);
                Assert.Equal(SignInStep.Address, f.Vm.Step);
                Assert.Equal("", f.Vm.Password, "地址变化原本就清旧密码，不能沿用旧服务器草稿");
                Assert.Null(f.Vm.SelectedUser);
                Assert.Equal(0, f.Vm.Users.Count);
                Assert.False(f.Vm.Busy);
                attempt.Confirm();
                await task;
                CheckAbandonedA(f, attempt, attempt.CancellationObserved);
                Assert.Equal(SignInStep.Address, f.Vm.Step);
                Assert.False(f.Vm.Busy);
            });
        }

        foreach (var stop in new[] { "返回地址", "Cancel", "Detach" })
        {
            Case($"R008 {stop}取消旧票且不清新草稿", async () =>
            {
                using var f = await Fixture.CreateAsync();
                var task = f.Submit();
                var attempt = f.Session.Attempts.Single();
                if (stop == "返回地址") f.Vm.BackToAddressCommand.Execute(null);
                else if (stop == "Cancel") f.Vm.Cancel();
                else f.Vm.Detach();
                Assert.True(attempt.Token.IsCancellationRequested);
                Assert.False(f.Vm.Busy);
                f.Vm.Username = "b";
                f.Vm.Password = DraftB;
                f.Vm.Remember = false;
                attempt.Confirm();
                await task;
                CheckAbandonedA(f, attempt, attempt.CancellationObserved);
                Assert.Equal(DraftB, f.Vm.Password);
                Assert.Equal("b", f.Vm.Username);
                Assert.False(f.Vm.Busy);
                Assert.True(f.Vm.SignInCommand.CanExecute(null));
                if (stop == "返回地址")
                {
                    Assert.Equal(SignInStep.Address, f.Vm.Step);
                    Assert.Equal(SignInFocus.Address, f.Focus.Last());
                }
            });
        }

        Case("R008 Dispose使迟到旧完成失效", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var task = f.Submit();
            var attempt = f.Session.Attempts.Single();
            f.Vm.Dispose();
            attempt.Confirm();
            await task;
            CheckAbandonedA(f, attempt, attempt.CancellationObserved);
        });

        Case("R008 当前真实VM失败反馈与重提保持", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var task = f.Submit();
            var attempt = f.Session.Attempts.Single();
            attempt.Reject("anonymous current failure");
            await task;
            Assert.False(attempt.Token.IsCancellationRequested);
            Assert.True(f.Vm.NoticeOpen);
            Assert.Equal("登录失败", f.Vm.NoticeTitle);
            Assert.Equal("anonymous current failure", f.Vm.NoticeMessage);
            Assert.Equal(SignInFocus.RejectedPassword, f.Focus.Last());
            Assert.Equal(DraftA, f.Vm.Password);
            Assert.False(f.Vm.Busy);
            Assert.True(f.Vm.IsReady);
            Assert.True(f.Vm.SignInCommand.CanExecute(null));
            var retryTask = f.Submit();
            var retry = f.Session.Attempts.Last();
            Assert.False(attempt.Token == retry.Token);
            Assert.True(attempt.Token.IsCancellationRequested, "真正的新请求仍由LoadGeneration替代旧票");
            retry.Confirm();
            await retryTask;
            Assert.Equal(1, f.SignedInUsers.Count);
        });

        Case("R008 Busy变化通知实际文案与命令且集合身份稳定", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var users = f.Vm.Users;
            var discovered = f.Vm.Discovered;
            var saved = f.Vm.SavedServers;
            var textNotifications = 0;
            var commandNotifications = 0;
            f.Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SignInViewModel.SignInText)) textNotifications++; };
            f.Vm.SignInCommand.CanExecuteChanged += (_, _) => commandNotifications++;
            var task = f.Submit();
            var attempt = f.Session.Attempts.Single();
            Assert.Equal("请稍候…", f.Vm.SignInText);
            f.ChangeToB(false);
            var afterEdit = f.Vm.SignInText;
            attempt.Confirm();
            await task;
            Assert.Equal("登录", afterEdit, "取消身份请求同步结束请稍候状态");
            Assert.True(textNotifications >= 2);
            Assert.True(commandNotifications >= 2);
            Assert.True(ReferenceEquals(users, f.Vm.Users));
            Assert.True(ReferenceEquals(discovered, f.Vm.Discovered));
            Assert.True(ReferenceEquals(saved, f.Vm.SavedServers));
            CheckBDraft(f);
        });
    }

    private static void RegisterContracts()
    {
        Test("R008 安全边界只装载source-linked VM与真实固定生成器", () =>
        {
            var assembly = typeof(Program).Assembly;
            Assert.Equal(assembly, typeof(SignInViewModel).Assembly);
            Assert.Equal(assembly, typeof(PageViewModel).Assembly);
            Assert.Equal(assembly, typeof(Momoka.Infrastructure.LoadGeneration).Assembly);
            Assert.Equal(assembly, typeof(EmbySession).Assembly);
            Assert.False(assembly.GetReferencedAssemblies().Any(name => name.Name is "Momoka" or "Momoka.Core"));
            var command = typeof(SignInViewModel).GetProperty(nameof(SignInViewModel.SignInCommand))!
                .GetCustomAttribute<GeneratedCodeAttribute>();
            Assert.NotNull(command);
            Assert.Contains("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", command!.Tool);
            var busy = typeof(PageViewModel).GetProperty(nameof(PageViewModel.Busy))!
                .GetCustomAttribute<GeneratedCodeAttribute>();
            Assert.NotNull(busy);
            Assert.Contains("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", busy!.Tool);
        });

        Test("R008 实际XAML用户名地址密码绑定逐输入双向回写", () =>
        {
            var doc = XDocument.Parse(Resource("Production.SignInPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var item in new[] { ("ServerBox", "Text", "Address"), ("UserBox", "Text", "Username"), ("PasswordInput", "Password", "Password") })
            {
                var node = doc.Descendants().Single(element => (string?)element.Attribute(x + "Name") == item.Item1);
                var binding = (string?)node.Attribute(item.Item2);
                Assert.Contains("ViewModel." + item.Item3, binding);
                Assert.Contains("Mode=TwoWay", binding);
                Assert.Contains("UpdateSourceTrigger=PropertyChanged", binding);
                Assert.Null(node.Attribute("IsEnabled"), "本修复不冻结/改输入行为");
            }
        });

        Test("R008 实际XAML选账号与按钮和页Detach事件仍接原VM", () =>
        {
            var doc = XDocument.Parse(Resource("Production.SignInPage.xaml"));
            var users = doc.Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SignInUserList");
            Assert.Equal("{x:Bind ViewModel.SelectedUser, Mode=TwoWay}", (string?)users.Attribute("SelectedItem"));
            Assert.Equal("{x:Bind ViewModel.Users, Mode=OneWay}", (string?)users.Attribute("ItemsSource"));
            Assert.Null(users.Attribute("IsEnabled"));
            var submit = doc.Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SignInSubmitButton");
            Assert.Equal("{x:Bind ViewModel.SignInCommand}", (string?)submit.Attribute("Command"));
            var code = Resource("Production.SignInPage.xaml.cs");
            Assert.Contains("ViewModel.SignInCommand.Execute(null)", code);
            Assert.Contains("internal void Detach() => ViewModel.Detach();", code);
            Assert.Contains("ViewModel.SignedIn += (_, e) => SignedIn?.Invoke(this, e);", code);
            Assert.Contains("ViewModel.FocusRequested += FocusOn;", code);
        });
    }

    private static void Case(string name, Func<Task> scenario) => Test(name, () =>
    {
        using var queue = new UiQueue();
        queue.Run(scenario);
    });

    private static async Task NextTurnAsync() => await Task.Yield();

    private static void CheckAbandonedA(Fixture f, FakeSignInAttempt a, bool synchronousCancellation)
    {
        Assert.False(f.Session.Adopted.Any(snapshot => ReferenceEquals(snapshot, a.Snapshot)),
            $"旧A不得Adopt；实际Username={f.Vm.Username}，取消={a.CancelledAtConfirmation}，Adopt数={f.Session.Adopted.Count}");
        Assert.False(f.SignedInSnapshots.Any(snapshot => ReferenceEquals(snapshot, a.Snapshot)), "旧A不得SignedIn");
        Assert.True(synchronousCancellation, "用户setter返回之前端点应已观察token取消");
        Assert.True(a.CancellationObserved, "fake确认端点必须看到调用方取消，不自己推断身份");
        Assert.Equal(true, a.CancelledAtConfirmation, "迟到确认在任何fake Adopt前看到取消");
        Assert.True(a.Completed);
    }

    private static void CheckBDraft(Fixture f)
    {
        Assert.Equal("b", f.Vm.Username);
        Assert.Equal(DraftB, f.Vm.Password, "取消与A旧回调都不能清B草稿");
        Assert.False(f.Vm.Remember);
        Assert.Equal(SignInStep.Credential, f.Vm.Step, "身份编辑与取消不应跳回地址步");
    }

    private static void CheckSnapshot(FakeSignInAttempt attempt, Fixture f, string username, string password, bool remember)
    {
        Assert.Equal(f.Server.Id, attempt.Snapshot.ServerId);
        Assert.Equal(f.Server.Url, attempt.Snapshot.Address);
        Assert.Equal(f.Server.ServerKind, attempt.Snapshot.Kind);
        Assert.Equal(username == "a" ? f.AccountA.Id : f.AccountB.Id, attempt.Snapshot.AccountId);
        Assert.Equal(username, attempt.Snapshot.AccountUsername);
        Assert.Equal(username, attempt.Snapshot.Username);
        Assert.Equal(password, attempt.Snapshot.Password);
        Assert.Equal(remember, attempt.Snapshot.Remember);
    }

    private static void CheckCurrentSuccess(Fixture f, FakeSignInAttempt attempt)
    {
        Assert.False(attempt.CancellationObserved);
        Assert.Equal(false, attempt.CancelledAtConfirmation);
        Assert.True(attempt.Completed);
        Assert.Equal(attempt.Snapshot, f.Session.Adopted.Single());
        Assert.Equal(attempt.Snapshot, f.SignedInSnapshots.Single());
        Assert.Equal(1, f.Settings.Saves);
        Assert.Equal("", f.Vm.Password);
        Assert.False(f.Vm.Busy);
        Assert.True(f.Vm.IsReady);
    }

    private static string Resource(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture(bool passwordlessA = false, bool passwordlessB = false, MediaServerKind kind = MediaServerKind.Emby)
        {
            Server = new() { Id = "server", Url = "https://signin.invalid/media", Name = "anonymous server", ServerKind = kind };
            AccountA = new() { Id = "account-a", Username = "a", HasSavedPassword = true };
            AccountB = new() { Id = "account-b", Username = "b", HasSavedPassword = true };
            Server.Accounts.AddRange([AccountA, AccountB]);
            Settings.Settings.Servers.Add(Server);
            Vault.Values.Add(AccountA.Id, "anonymous-saved-a");
            Vault.Values.Add(AccountB.Id, "anonymous-saved-b");
            UserA = new() { Id = "user-a", Name = "a", HasPassword = !passwordlessA };
            UserB = new() { Id = "user-b", Name = "b", HasPassword = !passwordlessB };
            Session.Gateway.Users.AddRange([UserA, UserB]);
            Session.Gateway.Info = new()
            {
                ApiBase = EmbyServerAddress.Normalize(Server.Url, kind),
                ServerKind = kind,
                ServerName = "anonymous server",
                Version = "fixture",
                Id = "server"
            };
            Vm.FocusRequested += Focus.Add;
            Vm.SignedIn += (_, _) =>
            {
                SignedInUsers.Add(Session.Adopted.LastOrDefault()?.Username ?? "no-commit");
                if (Session.Adopted.LastOrDefault() is { } snapshot) SignedInSnapshots.Add(snapshot);
            };
            Vm.Attach(Settings, Session, Vault);
            Vm.Password = DraftA;
            Vm.Remember = false;
        }

        internal MemorySettings Settings { get; } = new();
        internal EmbySession Session { get; } = new();
        internal CredentialVault Vault { get; } = new();
        internal SignInViewModel Vm { get; } = new();
        internal ServerProfile Server { get; }
        internal AccountProfile AccountA { get; }
        internal AccountProfile AccountB { get; }
        internal EmbyUser UserA { get; }
        internal EmbyUser UserB { get; }
        internal List<SignInFocus> Focus { get; } = [];
        internal List<string> SignedInUsers { get; } = [];
        internal List<FakeSignInSnapshot> SignedInSnapshots { get; } = [];

        internal static async Task<Fixture> CreateAsync(bool passwordlessA = false, bool passwordlessB = false,
            MediaServerKind kind = MediaServerKind.Emby)
        {
            var f = new Fixture(passwordlessA, passwordlessB, kind);
            await f.Vm.ConnectCommand.ExecuteAsync(null);
            return f;
        }

        internal Task Submit()
        {
            Assert.True(Vm.SignInCommand.CanExecute(null), "必须经当前真实生成命令提交");
            return Vm.SignInCommand.ExecuteAsync(null);
        }

        internal void ChangeToB(bool selection)
        {
            if (selection) Vm.SelectedUser = UserB;
            else Vm.Username = "b";
            Vm.Password = DraftB;
            Vm.Remember = false;
        }

        public void Dispose()
        {
            Vm.Detach();
            foreach (var attempt in Session.Attempts) attempt.Confirm();
            Vm.Dispose();
        }
    }
}
