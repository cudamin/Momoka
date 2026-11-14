using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SidebarMotionTests
{
    internal static void Register()
    {
        Test("齿轮只在Loaded活动期绑定动画，重复Loaded不增加观察", () => With(shell =>
        {
            Assert.Null(shell.IconSource);
            shell.AttachMotion();
            var source = shell.IconSource;
            Assert.NotNull(source);
            var subscriptions = UISettings.Created.Sum(settings => settings.SubscriptionCount);
            shell.AttachMotion();
            Assert.True(ReferenceEquals(source, shell.IconSource));
            Assert.Equal(subscriptions, UISettings.Created.Sum(settings => settings.SubscriptionCount));
            shell.DetachMotion();
            Assert.Null(shell.IconSource);
            Assert.Equal(0, UISettings.Created.Sum(settings => settings.SubscriptionCount));
        }));
        foreach (var system in new[] { false, true })
            Test($"关闭{(system ? "系统" : "应用")}动画立即切静态齿轮，恢复读当前偏好", () => With(shell =>
            {
                shell.AttachMotion();
                var source = shell.IconSource;
                if (system) UISettings.SetAnimationsEnabled(false);
                else HomeMotion.ApplyPreference(false);
                Assert.Null(shell.IconSource);
                if (system) UISettings.SetAnimationsEnabled(true);
                else HomeMotion.ApplyPreference(true);
                Assert.True(ReferenceEquals(source, shell.IconSource));
            }));
        Test("齿轮两个动画开关独立，任一关闭都保持静态", () => With(shell =>
        {
            shell.AttachMotion();
            HomeMotion.ApplyPreference(false);
            UISettings.SetAnimationsEnabled(false);
            HomeMotion.ApplyPreference(true);
            Assert.Null(shell.IconSource);
            UISettings.SetAnimationsEnabled(true);
            Assert.NotNull(shell.IconSource);
        }));
        Test("齿轮离页后旧系统事件和排队通知不能重绑动画", () => With(shell =>
        {
            shell.AttachMotion();
            var posted = UISettings.Created.Last().CapturePostedEvent();
            shell.DispatcherQueue.HasThreadAccess = false;
            UISettings.SetAnimationsEnabled(false);
            Assert.Equal(1, shell.DispatcherQueue.PendingCount);
            shell.DetachMotion();
            UISettings.SetAnimationsEnabled(true);
            var writes = shell.IconSourceWrites;
            shell.DispatcherQueue.Drain();
            posted();
            Assert.Null(shell.IconSource);
            Assert.Equal(writes, shell.IconSourceWrites);
        }));
        Test("齿轮重入后旧生命周期通知不改新Source或追加工作", () => With(shell =>
        {
            shell.AttachMotion();
            var posted = UISettings.Created.Last().CapturePostedEvent();
            shell.DispatcherQueue.HasThreadAccess = false;
            UISettings.SetAnimationsEnabled(false);
            shell.DetachMotion();
            UISettings.SetAnimationsEnabled(true);
            shell.AttachMotion();
            var source = shell.IconSource;
            var writes = shell.IconSourceWrites;
            var queued = shell.DispatcherQueue.PendingCount;
            posted();
            Assert.Equal(queued, shell.DispatcherQueue.PendingCount);
            shell.DispatcherQueue.Drain();
            Assert.True(ReferenceEquals(source, shell.IconSource));
            Assert.Equal(writes, shell.IconSourceWrites);
        }));
        foreach (var after in new[] { false, true })
            Test($"齿轮原生观察{(after ? "注册后" : "注册前")}异常不阻断Loaded和清理", () => With(shell =>
            {
                UISettings.ThrowOnAdd = !after;
                UISettings.ThrowAfterAdd = after;
                shell.AttachMotion();
                Assert.Null(shell.IconSource);
                UISettings.ThrowOnAdd = UISettings.ThrowAfterAdd = false;
                shell.DetachMotion();
                Assert.Null(shell.IconSource);
                Assert.Equal(0, UISettings.Created.Sum(settings => settings.SubscriptionCount));
            }));
        Test("齿轮退订失败也先撤销生命周期，旧通知不能留动画源", () =>
        {
            Reset();
            using var shell = new ShellPage();
            shell.AttachMotion();
            var posted = UISettings.Created.Last().CapturePostedEvent();
            UISettings.ThrowOnRemove = true;
            shell.DetachMotion();
            Assert.Null(shell.IconSource);
            var writes = shell.IconSourceWrites;
            posted();
            Assert.Equal(writes, shell.IconSourceWrites);
            Assert.Equal(1, UISettings.Created.Sum(settings => settings.SubscriptionCount));
            UISettings.EndFaultFixture();
        });
    }

    private static void With(Action<ShellPage> run)
    {
        Reset();
        using var shell = new ShellPage();
        run(shell);
    }

    private static void Reset()
    {
        Storyboard.Reset();
        UISettings.Reset();
        HomeMotion.ApplyPreference(true);
    }
}
