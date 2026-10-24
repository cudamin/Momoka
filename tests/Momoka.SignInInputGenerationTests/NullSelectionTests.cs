using System.Reflection;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterNullSelectionTests()
    {
        foreach (var viaNull in new[] { false, true })
        {
            foreach (var passwordless in new[] { false, true })
            {
                var route = viaNull ? "A经null到B" : "A直接到B";
                var form = passwordless ? "无密码" : "有密码";
                Case($"R008 补修同名不同ID/{route}/{form}草稿与新提交", async () =>
                {
                    using var f = await Fixture.CreateAsync(passwordlessB: passwordless);
                    var aTask = f.Submit();
                    var a = f.Session.Attempts.Single();
                    f.UserB.Name = " A ";
                    if (viaNull) ClearSelectionWithoutCancelling(f, a);
                    f.Vm.SelectedUser = f.UserB;
                    var cancelledAtPick = a.Token.IsCancellationRequested && a.CancellationObserved;
                    var countAtPick = f.Session.Attempts.Count;
                    Task bTask = Task.CompletedTask;
                    FakeSignInAttempt? b = null;
                    if (passwordless) b = f.Session.Attempts.Skip(1).SingleOrDefault();
                    else
                    {
                        f.Vm.Password = DraftB;
                        f.Vm.Remember = false;
                    }
                    a.Confirm();
                    await aTask;
                    var draftAfterA = f.Vm.Password;
                    var busyAfterA = f.Vm.Busy;
                    var notificationsAfterA = f.SignedInUsers.Count;
                    b?.Confirm();
                    await NextTurnAsync();
                    CheckAbandonedA(f, a, cancelledAtPick);
                    Assert.Equal(f.UserB, f.Vm.SelectedUser);
                    Assert.Equal(" A ", f.Vm.Username);
                    Assert.Equal(0, notificationsAfterA, "经null不应让旧A成功导航");
                    if (passwordless)
                    {
                        Assert.Equal(2, countAtPick, "同名无密码B也必须自己拿新票自动提交");
                        Assert.NotNull(b);
                        Assert.True(busyAfterA, "A的旧finally不结束B");
                        Assert.Equal("", b!.Snapshot.Password);
                    }
                    else
                    {
                        Assert.Equal(1, countAtPick, "有密码B只保原填表行为，不自动提交");
                        Assert.Equal(DraftB, draftAfterA, "经null改选后的B草稿不能被A清空");
                        Assert.False(busyAfterA);
                        Assert.True(f.Vm.SignInCommand.CanExecute(null));
                        bTask = f.Submit();
                        b = f.Session.Attempts.Last();
                        Assert.Equal(DraftB, b.Snapshot.Password);
                        b.Confirm();
                        await bTask;
                    }
                    Assert.NotNull(b);
                    Assert.False(a.Token == b!.Token);
                    Assert.Equal("A", b.Snapshot.Username);
                    // Both advertised names resolve through the unchanged production pure helper.
                    // The fake endpoint does not infer B from a user ID; distinct receipt/token proves
                    // the VM requested B's new attempt, even when the normalized username is equal.
                    Assert.Equal(f.AccountA.Id, b.Snapshot.AccountId);
                    CheckCurrentSuccess(f, b);
                });

                foreach (var newFirst in new[] { false, true })
                {
                    Case($"R008 补修同名不同ID/{route}/{form}/{(newFirst ? "B先A后" : "A先B后")}", async () =>
                    {
                        using var f = await Fixture.CreateAsync(passwordlessB: passwordless);
                        var aTask = f.Submit();
                        var a = f.Session.Attempts.Single();
                        f.UserB.Name = " A ";
                        if (viaNull) ClearSelectionWithoutCancelling(f, a);
                        f.Vm.SelectedUser = f.UserB;
                        var availableAfterPick = f.Vm.SignInCommand.CanExecute(null);
                        Task bTask = Task.CompletedTask;
                        if (!passwordless)
                        {
                            f.Vm.Password = DraftB;
                            f.Vm.Remember = false;
                            bTask = f.Vm.SignInCommand.ExecuteAsync(null);
                        }
                        var b = f.Session.Attempts.Skip(1).SingleOrDefault();
                        var countBeforeConfirm = f.Session.Attempts.Count;
                        if (newFirst) b?.Confirm();
                        if (newFirst) { await bTask; await NextTurnAsync(); }
                        a.Confirm();
                        await aTask;
                        var busyAfterOld = f.Vm.Busy;
                        var draftAfterOld = f.Vm.Password;
                        if (!newFirst) b?.Confirm();
                        await bTask;
                        await NextTurnAsync();
                        Assert.Equal(2, countBeforeConfirm, "经null切同名身份也不等待A才能起B新票");
                        Assert.NotNull(b);
                        CheckAbandonedA(f, a, a.CancellationObserved);
                        Assert.False(a.Token == b!.Token);
                        Assert.Equal(!newFirst, busyAfterOld, "只有仍在飞的B拥有Busy");
                        if (!passwordless)
                        {
                            Assert.True(availableAfterPick);
                            if (!newFirst) Assert.Equal(DraftB, draftAfterOld);
                        }
                        Assert.Equal(f.UserB, f.Vm.SelectedUser);
                        CheckCurrentSuccess(f, b);
                    });
                }

                Case($"R008 补修{route}/{form}/A收尾后B快照仍可撤销", async () =>
                {
                    using var f = await Fixture.CreateAsync(passwordlessB: passwordless);
                    var aTask = f.Submit();
                    var a = f.Session.Attempts.Single();
                    f.UserB.Name = " A ";
                    if (viaNull) ClearSelectionWithoutCancelling(f, a);
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
                    var busyBeforeThirdEdit = f.Vm.Busy;
                    if (viaNull) f.Vm.SelectedUser = null;
                    f.Vm.SelectedUser = new() { Id = f.UserA.Id, Name = " A ", HasPassword = true };
                    f.Vm.Password = "anonymous-third-draft";
                    var cancelledB = b?.Token.IsCancellationRequested == true && b.CancellationObserved;
                    b?.Confirm();
                    await bTask;
                    await NextTurnAsync();
                    Assert.NotNull(b);
                    Assert.True(busyBeforeThirdEdit);
                    Assert.True(cancelledB, "旧A finally不能清B捕获ID，第三次同名改选仍须取消B");
                    CheckAbandonedA(f, a, a.CancellationObserved);
                    Assert.Equal(true, b!.CancelledAtConfirmation);
                    Assert.Equal(0, f.Session.Adopted.Count);
                    Assert.Equal(0, f.SignedInUsers.Count);
                    Assert.Equal(0, f.Settings.Saves);
                    Assert.Equal("anonymous-third-draft", f.Vm.Password);
                    Assert.False(f.Vm.Busy);
                    Assert.True(f.Vm.SignInCommand.CanExecute(null));
                });

                Case($"R008 补修{route}/{form}/A收尾不清B快照且同ID复制免取消", async () =>
                {
                    using var f = await Fixture.CreateAsync(passwordlessB: passwordless);
                    var aTask = f.Submit();
                    var a = f.Session.Attempts.Single();
                    f.UserB.Name = " A ";
                    if (viaNull) ClearSelectionWithoutCancelling(f, a);
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
                    var busyBeforeCopy = f.Vm.Busy;
                    if (viaNull) f.Vm.SelectedUser = null;
                    f.Vm.SelectedUser = new() { Id = f.UserB.Id, Name = " A ", HasPassword = !passwordless };
                    var bCancelledOnCopy = b?.Token.IsCancellationRequested == true;
                    var attemptsAfterCopy = f.Session.Attempts.Count;
                    b?.Confirm();
                    await bTask;
                    await NextTurnAsync();
                    Assert.NotNull(b, "经null必须已经提交B新票");
                    CheckAbandonedA(f, a, a.CancellationObserved);
                    Assert.True(busyBeforeCopy);
                    Assert.False(bCancelledOnCopy, "A旧finally不能清B捕获ID并误把same ID复制当新身份");
                    Assert.Equal(2, attemptsAfterCopy, "复制B不能开第三票");
                    Assert.Equal(f.UserB.Id, f.Vm.SelectedUser!.Id);
                    CheckCurrentSuccess(f, b!);
                });
                Case($"R008 补修同ID等价行/{route}/{form}不取消不发新票", async () =>
                {
                    using var f = await Fixture.CreateAsync();
                    var aTask = f.Submit();
                    var a = f.Session.Attempts.Single();
                    if (viaNull) ClearSelectionWithoutCancelling(f, a);
                    f.Vm.SelectedUser = new() { Id = f.UserA.Id, Name = " A ", HasPassword = !passwordless };
                    var cancelled = a.Token.IsCancellationRequested;
                    var attempts = f.Session.Attempts.Count;
                    a.Confirm();
                    await aTask;
                    Assert.False(cancelled, "相同提交ID的复制行不是新身份");
                    Assert.Equal(1, attempts, "same ID无密码行仍被当前Busy挡重复提交");
                    CheckCurrentSuccess(f, a);
                });

                Case($"R008 补修_filling换默认ID后/{route}/{form}仍比较提交快照", async () =>
                {
                    using var f = await Fixture.CreateAsync(passwordlessB: passwordless);
                    var aTask = f.Submit();
                    var a = f.Session.Attempts.Single();
                    f.UserB.Name = "a";
                    FillActualCredentialStep(f, [f.UserB]);
                    Assert.Equal(f.UserB, f.Vm.SelectedUser);
                    Assert.False(a.Token.IsCancellationRequested, "_filling自己不取消也不覆盖提交ID");
                    Assert.Equal(1, f.Session.Attempts.Count);
                    if (viaNull) ClearSelectionWithoutCancelling(f, a);
                    f.Vm.SelectedUser = new() { Id = f.UserB.Id, Name = " A ", HasPassword = !passwordless };
                    var cancelled = a.Token.IsCancellationRequested && a.CancellationObserved;
                    var b = passwordless ? f.Session.Attempts.Skip(1).SingleOrDefault() : null;
                    var countAfterPick = f.Session.Attempts.Count;
                    if (!passwordless) f.Vm.Password = DraftB;
                    a.Confirm();
                    await aTask;
                    var draftAfterOld = f.Vm.Password;
                    b?.Confirm();
                    await NextTurnAsync();
                    CheckAbandonedA(f, a, cancelled);
                    if (passwordless)
                    {
                        Assert.Equal(2, countAfterPick, "默认填表不发请求，用户随后真正选B才用新票");
                        Assert.NotNull(b);
                        CheckCurrentSuccess(f, b!);
                    }
                    else
                    {
                        Assert.Equal(DraftB, draftAfterOld);
                        Assert.Equal(0, f.SignedInUsers.Count);
                        Assert.False(f.Vm.Busy);
                    }
                });
            }

            Case($"R008 补修提交ID为不可变值/{(viaNull ? "经null" : "直接")}/DTO修改不迁移旧票", async () =>
            {
                using var f = await Fixture.CreateAsync();
                var aTask = f.Submit();
                var a = f.Session.Attempts.Single();
                f.UserA.Id = "mutated-row-id";
                if (viaNull) ClearSelectionWithoutCancelling(f, a);
                f.Vm.SelectedUser = new() { Id = "mutated-row-id", Name = " A ", HasPassword = true };
                f.Vm.Password = DraftB;
                var cancelled = a.Token.IsCancellationRequested && a.CancellationObserved;
                a.Confirm();
                await aTask;
                CheckAbandonedA(f, a, cancelled);
                Assert.Equal(DraftB, f.Vm.Password);
                Assert.False(f.Vm.Busy);
            });
        }

        foreach (var passwordless in new[] { false, true })
        {
            Case($"R008 补修提交未选ID到非空同名行/{(passwordless ? "无密码" : "有密码")}", async () =>
            {
                using var f = await Fixture.CreateAsync();
                f.Vm.SelectedUser = null;
                var aTask = f.Submit();
                var a = f.Session.Attempts.Single();
                f.Vm.SelectedUser = new() { Id = f.UserA.Id, Name = " A ", HasPassword = !passwordless };
                var cancelled = a.Token.IsCancellationRequested && a.CancellationObserved;
                var b = passwordless ? f.Session.Attempts.Skip(1).SingleOrDefault() : null;
                if (!passwordless) f.Vm.Password = DraftB;
                a.Confirm();
                await aTask;
                b?.Confirm();
                await NextTurnAsync();
                CheckAbandonedA(f, a, cancelled);
                if (passwordless)
                {
                    Assert.NotNull(b);
                    CheckCurrentSuccess(f, b!);
                }
                else
                {
                    Assert.Equal(DraftB, f.Vm.Password);
                    Assert.False(f.Vm.Busy);
                    Assert.True(f.Vm.SignInCommand.CanExecute(null));
                }
            });
        }

        Case("R008 补修null后等价用户名编辑不取消提交票", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var aTask = f.Submit();
            var a = f.Session.Attempts.Single();
            ClearSelectionWithoutCancelling(f, a);
            f.Vm.Username = " A ";
            Assert.False(a.Token.IsCancellationRequested);
            Assert.Equal(DraftA, f.Vm.Password);
            a.Confirm();
            await aTask;
            CheckCurrentSuccess(f, a);
        });
    }

    private static void ClearSelectionWithoutCancelling(Fixture f, FakeSignInAttempt attempt)
    {
        var draft = f.Vm.Password;
        f.Vm.SelectedUser = null;
        Assert.False(attempt.Token.IsCancellationRequested, "null自身只为正常列表回写，不取消请求");
        Assert.True(f.Vm.Busy);
        Assert.Equal(draft, f.Vm.Password);
    }

    private static void FillActualCredentialStep(Fixture f, List<EmbyUser> users) =>
        typeof(SignInViewModel).GetMethod("ShowCredentialStep", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.Vm, [users]);
}
