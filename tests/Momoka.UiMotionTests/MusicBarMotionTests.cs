using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MusicBarMotionTests
{
    internal static void Register()
    {
        foreach (var musicPage in new[] { false, true })
            Test($"音乐栏{(musicPage ? "音乐墙" : "普通页")}退出中重开，旧完成不收起新栏或归还避让", () => With(fixture =>
            {
                if (musicPage) fixture.UseMusicPage();
                fixture.Show(true);
                Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
                fixture.Show(false);
                Assert.False(fixture.Bar.IsHitTestVisible);
                var stale = Storyboard.Active.Single().CapturePostedCompletion();
                Storyboard.Advance(60);
                fixture.Show(true);
                stale();
                Assert.Equal(Visibility.Visible, fixture.Bar.Visibility);
                Assert.True(fixture.Bar.IsHitTestVisible);
                Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
                Assert.Equal(1d, fixture.Bar.Opacity);
                Assert.Equal(musicPage ? 0d : 98d, fixture.PageMargin.Bottom);
                if (musicPage) Assert.Equal(102d, fixture.Page!.PlayerClearance);
            }));
        Test("音乐栏退出期间尺寸变化保留占位，落定才归还", () => With(fixture =>
        {
            fixture.Show(true);
            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
            fixture.Show(false);
            fixture.SizeChanged(80);
            Assert.Equal(Visibility.Visible, fixture.Bar.Visibility);
            Assert.Equal(98d, fixture.PageMargin.Bottom);
            Storyboard.Advance(SurfaceMotion.ExitMilliseconds);
            Assert.Equal(Visibility.Collapsed, fixture.Bar.Visibility);
            Assert.Equal(0d, fixture.PageMargin.Bottom);
        }));
        foreach (var system in new[] { false, true })
            Test($"音乐栏退出中关{(system ? "系统" : "应用")}动画即时归还音乐墙避让", () => With(fixture =>
            {
                fixture.UseMusicPage();
                fixture.Show(true);
                Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
                fixture.Show(false);
                var stale = Storyboard.Active.Single().CapturePostedCompletion();
                if (system) UISettings.SetAnimationsEnabled(false);
                else HomeMotion.ApplyPreference(false);
                Assert.Equal(Visibility.Collapsed, fixture.Bar.Visibility);
                Assert.False(fixture.Bar.IsHitTestVisible);
                Assert.Equal(0d, fixture.Page!.PlayerClearance);
                stale();
                Assert.Equal(0d, fixture.Page.PlayerClearance);
                Assert.Equal(0, Storyboard.Count);
            }));
        Test("音乐栏显示中重复布局不重播，音乐墙接管末行避让", () => With(fixture =>
        {
            fixture.Show(true);
            var boards = Storyboard.Begun.Count;
            fixture.SizeChanged(90);
            fixture.UseMusicPage();
            Assert.Equal(boards, Storyboard.Begun.Count);
            Assert.Equal(0d, fixture.PageMargin.Bottom);
            Assert.Equal(122d, fixture.Page!.PlayerClearance);
            Assert.True(fixture.Bar.IsHitTestVisible);
        }));
        Test("整页播放时播放栏收起并归还避让，收起页面后恢复", () => With(fixture =>
        {
            fixture.UseMusicPage();
            fixture.Show(true);
            Storyboard.Advance(SurfaceMotion.EnterMilliseconds);
            Assert.Equal(Visibility.Visible, fixture.Bar.Visibility);
            Assert.Equal(102d, fixture.Page!.PlayerClearance);

            fixture.ShowPage(true);
            Assert.Equal(Visibility.Collapsed, fixture.Bar.Visibility);
            Assert.False(fixture.Bar.IsHitTestVisible);
            Assert.Equal(0d, fixture.Page.PlayerClearance);

            fixture.ShowPage(false);
            Assert.Equal(Visibility.Visible, fixture.Bar.Visibility);
            Assert.True(fixture.Bar.IsHitTestVisible);
            Assert.Equal(102d, fixture.Page.PlayerClearance);
        }));
        Test("音乐栏原生动画启动拒绝仍兑现显示与退出避让", () => With(fixture =>
        {
            fixture.UseMusicPage();
            Storyboard.ThrowOnBegin = true;
            fixture.Show(true);
            Assert.Equal(1d, fixture.Bar.Opacity);
            Assert.Equal(Visibility.Visible, fixture.Bar.Visibility);
            Assert.True(fixture.Bar.IsHitTestVisible);
            fixture.Show(false);
            Assert.Equal(Visibility.Collapsed, fixture.Bar.Visibility);
            Assert.False(fixture.Bar.IsHitTestVisible);
            Assert.Equal(0d, fixture.Page!.PlayerClearance);
            Assert.Equal(0, Storyboard.Count);
            Assert.Equal(0, UISettings.Created.Sum(settings => settings.SubscriptionCount));
        }));
    }

    private static void With(Action<MusicBarMotionFixture> run)
    {
        Storyboard.Reset();
        UISettings.Reset();
        HomeMotion.ApplyPreference(true);
        var fixture = new MusicBarMotionFixture();
        try { run(fixture); }
        finally { fixture.Stop(); }
    }
}
