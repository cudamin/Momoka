using System.ComponentModel;
using Momoka.Shell.ViewModels;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterSynchronousTailTests()
    {
        foreach (var selection in new[] { false, true })
        {
            foreach (var submitB in new[] { false, true })
            {
                Case($"R008 取消尾部/{(selection ? "选中B" : "用户名B")}/{(submitB ? "新B票" : "B新草稿")}不被外层清除", async () =>
                {
                    using var f = await Fixture.CreateAsync();
                    var first = f.Submit();
                    var a = f.Session.Attempts.Single();
                    var seen = false;
                    Task? newer = null;
                    PropertyChangedEventHandler handler = (_, e) =>
                    {
                        if (seen || e.PropertyName != nameof(PageViewModel.Busy) || f.Vm.Busy) return;
                        seen = true;
                        f.Vm.Username = "b";
                        f.Vm.Password = DraftB;
                        f.Vm.Remember = false;
                        if (submitB) newer = f.Vm.SignInCommand.ExecuteAsync(null);
                    };
                    f.Vm.PropertyChanged += handler;
                    if (selection) f.Vm.SelectedUser = f.UserB;
                    else f.Vm.Username = "b";
                    f.Vm.PropertyChanged -= handler;
                    var draft = f.Vm.Password;
                    var attempts = f.Session.Attempts.ToArray();
                    a.Confirm();
                    await first;
                    foreach (var attempt in attempts.Skip(1)) attempt.Confirm();
                    if (newer is not null) await newer;
                    Assert.True(seen);
                    Assert.True(a.Token.IsCancellationRequested);
                    Assert.Equal(DraftB, draft, "取消同步通知内后来草稿不能被旧输入hook尾部清空");
                    if (submitB)
                    {
                        Assert.Equal(2, attempts.Length);
                        CheckSnapshot(attempts[1], f, "b", DraftB, false);
                        CheckCurrentSuccess(f, attempts[1]);
                    }
                    else
                    {
                        Assert.Equal(1, attempts.Length);
                        Assert.Equal(DraftB, f.Vm.Password);
                        Assert.False(f.Vm.Busy);
                        Assert.Equal(0, f.SignedInUsers.Count);
                    }
                });
            }
        }

        foreach (var passwordless in new[] { false, true })
        {
            Case($"R008 选中填充/同ID但非等价名字/{(passwordless ? "无密码" : "有密码")}仍撤原用户名票", async () =>
            {
                using var f = await Fixture.CreateAsync();
                var first = f.Submit();
                var a = f.Session.Attempts.Single();
                f.Vm.SelectedUser = new() { Id = f.UserA.Id, Name = "b", HasPassword = !passwordless };
                var cancelled = a.Token.IsCancellationRequested;
                var b = f.Session.Attempts.Skip(1).SingleOrDefault();
                a.Confirm();
                await first;
                b?.Confirm();
                await NextTurnAsync();
                Assert.True(cancelled, "sameID只允许等价用户名，不能取消用户名变化的原保护");
                Assert.Equal("b", f.Vm.Username);
                if (passwordless)
                {
                    Assert.NotNull(b);
                    CheckCurrentSuccess(f, b!);
                }
                else
                {
                    Assert.Equal(1, f.Session.Attempts.Count);
                    Assert.Equal("anonymous-saved-b", f.Vm.Password);
                    Assert.Equal(0, f.SignedInUsers.Count);
                }
            });
        }

        foreach (var notification in new[] { "NoticeSeverity", "NoticeTitle", "NoticeMessage", "NoticeOpen", "Caption" })
        {
            foreach (var automatic in new[] { false, true })
            {
                Case($"R008 失败尾部/{notification}/{(automatic ? "自动B" : "手动B")}不盖B反馈焦点", async () =>
                {
                    using var f = await Fixture.CreateAsync(passwordlessB: automatic);
                    var first = f.Submit();
                    var a = f.Session.Attempts.Single();
                    var seen = false;
                    Task? newer = null;
                    PropertyChangedEventHandler handler = (_, e) =>
                    {
                        if (seen || e.PropertyName != notification) return;
                        seen = true;
                        if (automatic) f.Vm.SelectedUser = f.UserB;
                        else
                        {
                            f.Vm.Username = "b";
                            f.Vm.Password = DraftB;
                            newer = f.Vm.SignInCommand.ExecuteAsync(null);
                        }
                    };
                    f.Focus.Clear();
                    f.Vm.PropertyChanged += handler;
                    a.Reject("anonymous A failure");
                    await first;
                    f.Vm.PropertyChanged -= handler;
                    var b = f.Session.Attempts.Last();
                    var caption = f.Vm.Caption;
                    var notice = f.Vm.NoticeOpen;
                    var oldFocus = f.Focus.Contains(SignInFocus.RejectedPassword);
                    var count = f.Session.Attempts.Count;
                    b.Confirm();
                    if (newer is not null) await newer;
                    await NextTurnAsync();
                    Assert.True(seen);
                    Assert.Equal(2, count);
                    Assert.Equal("正在登录…", caption, "A不能把B的进行中文案改回初始文案");
                    Assert.False(notice, "B的新BeginLoad已清旧提示，A不能再打开错误栏");
                    Assert.False(oldFocus, "A失败不得再要求B重新输入密码");
                    CheckCurrentSuccess(f, b);
                });
            }
        }
    }
}
