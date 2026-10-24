using System.ComponentModel;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterSynchronousEntryTests()
    {
        foreach (var notification in new[] { "Busy", "IsReady", "NoticeOpen", "Caption" })
        {
            foreach (var edit in new[] { "Username", "SelectedUser", "SelectedUser无密码" })
            {
                Case($"R008 同步入口/{notification}/{edit}撤销未发送A且保B草稿", async () =>
                {
                    using var f = await Fixture.CreateAsync(passwordlessB: edit == "SelectedUser无密码");
                    f.UserB.Name = " A ";
                    f.Vm.NoticeOpen = true;
                    var seen = false;
                    PropertyChangedEventHandler handler = (_, e) =>
                    {
                        if (seen || e.PropertyName != notification) return;
                        if (notification == "Busy" && !f.Vm.Busy) return;
                        seen = true;
                        if (edit == "Username") f.Vm.Username = "b";
                        else f.Vm.SelectedUser = f.UserB;
                        if (edit != "SelectedUser无密码") f.Vm.Password = DraftB;
                        f.Vm.Remember = false;
                    };
                    f.Vm.PropertyChanged += handler;
                    var submit = f.Submit();
                    f.Vm.PropertyChanged -= handler;
                    var attempts = f.Session.Attempts.ToArray();
                    var busyAfterOuter = f.Vm.Busy;
                    var draftAfterOuter = f.Vm.Password;
                    foreach (var attempt in attempts) attempt.Confirm();
                    await submit;
                    await NextTurnAsync();
                    Assert.True(seen, "必须确实经过真实生产VM的同步属性通知");
                    if (edit == "SelectedUser无密码")
                    {
                        Assert.Equal(1, attempts.Length, "只发送B的新票，尚未发送的A零端点调用");
                        Assert.True(busyAfterOuter, "旧A入口返回不能结束新B的Busy");
                        Assert.Equal("", attempts[0].Snapshot.Password);
                        Assert.False(attempts[0].Token.IsCancellationRequested);
                        CheckCurrentSuccess(f, attempts[0]);
                    }
                    else
                    {
                        Assert.Equal(0, attempts.Length, "通知内已撤销的A必须在端点调用前止步");
                        Assert.Equal(DraftB, draftAfterOuter);
                        Assert.Equal(DraftB, f.Vm.Password, "A不能清B后来草稿");
                        Assert.False(f.Vm.Busy);
                        Assert.Equal(0, f.SignedInUsers.Count);
                        Assert.True(f.Vm.SignInCommand.CanExecute(null));
                        var next = f.Submit();
                        var b = f.Session.Attempts.Single();
                        b.Confirm();
                        await next;
                        CheckCurrentSuccess(f, b);
                    }
                });
            }

            Case($"R008 同步入口/{notification}/等价用户名不取消且请求仍用点击快照", async () =>
            {
                using var f = await Fixture.CreateAsync();
                f.Vm.NoticeOpen = true;
                var seen = false;
                PropertyChangedEventHandler handler = (_, e) =>
                {
                    if (seen || e.PropertyName != notification) return;
                    if (notification == "Busy" && !f.Vm.Busy) return;
                    seen = true;
                    f.Vm.Username = " A ";
                    f.Vm.Password = DraftB;
                    f.Vm.Remember = true;
                };
                f.Vm.PropertyChanged += handler;
                var submit = f.Submit();
                f.Vm.PropertyChanged -= handler;
                var attempts = f.Session.Attempts.ToArray();
                foreach (var attempt in attempts) attempt.Confirm();
                await submit;
                Assert.True(seen);
                Assert.Equal(1, attempts.Length);
                CheckSnapshot(attempts[0], f, "a", DraftA, false);
                CheckCurrentSuccess(f, attempts[0]);
            });

            foreach (var boundary in new[] { "Cancel", "Detach", "Dispose", "新票" })
            {
                Case($"R008 同步入口/{notification}/{boundary}旧入口不污染后继", async () =>
                {
                    using var f = await Fixture.CreateAsync();
                    f.Vm.NoticeOpen = true;
                    var seen = false;
                    Task? newer = null;
                    PropertyChangedEventHandler handler = (_, e) =>
                    {
                        if (seen || e.PropertyName != notification) return;
                        if (notification == "Busy" && !f.Vm.Busy) return;
                        seen = true;
                        switch (boundary)
                        {
                            case "Cancel": f.Vm.Cancel(); break;
                            case "Detach": f.Vm.Detach(); break;
                            case "Dispose": f.Vm.Dispose(); break;
                            default:
                                f.Vm.Username = "b";
                                f.Vm.Password = DraftB;
                                newer = f.Vm.SignInCommand.ExecuteAsync(null);
                                break;
                        }
                    };
                    f.Vm.PropertyChanged += handler;
                    var submit = f.Submit();
                    f.Vm.PropertyChanged -= handler;
                    var attempts = f.Session.Attempts.ToArray();
                    var busyAfterOuter = f.Vm.Busy;
                    foreach (var attempt in attempts) attempt.Confirm();
                    await submit;
                    if (newer is not null) await newer;
                    await NextTurnAsync();
                    Assert.True(seen);
                    if (boundary == "新票")
                    {
                        Assert.Equal(1, attempts.Length);
                        Assert.True(busyAfterOuter);
                        CheckSnapshot(attempts[0], f, "b", DraftB, false);
                        CheckCurrentSuccess(f, attempts[0]);
                    }
                    else
                    {
                        Assert.Equal(0, attempts.Length, "同步离页在发送前撤销入口");
                        Assert.Equal(0, f.SignedInUsers.Count);
                        Assert.False(f.Vm.Busy);
                    }
                });
            }
        }

        Case("R008 EndLoad同步Busy结束开启B，旧A不把新B的IsReady改成完成", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var submit = f.Submit();
            var a = f.Session.Attempts.Single();
            var seen = false;
            Task? newer = null;
            PropertyChangedEventHandler handler = (_, e) =>
            {
                if (seen || e.PropertyName != nameof(PageViewModel.Busy) || f.Vm.Busy) return;
                seen = true;
                f.Vm.Username = "b";
                f.Vm.Password = DraftB;
                newer = f.Vm.SignInCommand.ExecuteAsync(null);
            };
            f.Vm.PropertyChanged += handler;
            a.Confirm();
            await submit;
            f.Vm.PropertyChanged -= handler;
            var b = f.Session.Attempts.Last();
            Assert.True(seen);
            Assert.Equal(2, f.Session.Attempts.Count);
            Assert.True(f.Vm.Busy);
            Assert.False(f.Vm.IsReady, "旧A的EndLoad只结束A，新B还在等待确认");
            Assert.Equal(DraftB, f.Vm.Password);
            Assert.False(b.Token.IsCancellationRequested);
            b.Confirm();
            await newer!;
            Assert.Equal(2, f.SignedInUsers.Count);
            Assert.False(f.Vm.Busy);
            Assert.True(f.Vm.IsReady);
        });

        Case("R008 同步入口/同ID复制及null本身不取消，DTO不覆盖点击快照", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var seen = false;
            PropertyChangedEventHandler handler = (_, e) =>
            {
                if (seen || e.PropertyName != nameof(PageViewModel.Busy) || !f.Vm.Busy) return;
                seen = true;
                f.Vm.SelectedUser = null;
                f.Vm.SelectedUser = new() { Id = f.UserA.Id, Name = " A ", HasPassword = true };
            };
            f.Vm.PropertyChanged += handler;
            var submit = f.Submit();
            f.Vm.PropertyChanged -= handler;
            var attempt = f.Session.Attempts.Single();
            attempt.Confirm();
            await submit;
            Assert.True(seen);
            CheckSnapshot(attempt, f, "a", DraftA, false);
            CheckCurrentSuccess(f, attempt);
        });

        Case("R008 同步入口/_filling换选中ID不取消，但后续仍按原点击ID撤销", async () =>
        {
            using var f = await Fixture.CreateAsync();
            var seen = false;
            PropertyChangedEventHandler handler = (_, e) =>
            {
                if (seen || e.PropertyName != nameof(PageViewModel.Busy) || !f.Vm.Busy) return;
                seen = true;
                FillActualCredentialStep(f, [f.UserB]);
            };
            f.Vm.PropertyChanged += handler;
            var submit = f.Submit();
            f.Vm.PropertyChanged -= handler;
            var attempt = f.Session.Attempts.Single();
            var notCancelledByFilling = !attempt.Token.IsCancellationRequested;
            f.UserB.Name = " A ";
            f.Vm.SelectedUser = null;
            f.Vm.SelectedUser = f.UserB;
            f.Vm.Password = DraftB;
            var cancelled = attempt.Token.IsCancellationRequested;
            attempt.Confirm();
            await submit;
            Assert.True(seen);
            Assert.True(notCancelledByFilling);
            CheckSnapshot(attempt, f, "a", DraftA, false);
            CheckAbandonedA(f, attempt, cancelled);
            Assert.Equal(DraftB, f.Vm.Password);
        });
    }
}
