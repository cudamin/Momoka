using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SubscriptionFailureTests
{
    internal static void Register()
    {
        foreach (var kind in new[] { "Home", "Surface", "Glyph" })
            foreach (var after in new[] { false, true })
            {
                Test(kind + (after ? "注册后抛错" : "注册前抛错") + "落定且解除本地时钟，下一次仍可启动", () =>
                {
                    Reset();
                    var target = Target();
                    var surface = kind == "Surface" ? new SurfaceMotion(target) : null;
                    try
                    {
                        UISettings.ThrowOnAdd = !after;
                        UISettings.ThrowAfterAdd = after;
                        Start(kind, target, surface);
                        Assert.Equal(0, Storyboard.Count);
                        Assert.True(surface is null || !surface.IsAnimating);
                        Assert.Equal(1d, target.Opacity);
                        AssertResting(target);
                        Assert.Equal(0, UISettings.Created.Sum(settings => settings.SubscriptionCount));
                        UISettings.ThrowOnAdd = UISettings.ThrowAfterAdd = false;
                        Start(kind, target, surface);
                        // Surface already landed shown; a later exit can still own a fresh clock.
                        if (surface is not null) surface.Exit();
                        Assert.Equal(1, Storyboard.Count);
                        Storyboard.Advance(1000);
                        Assert.Equal(0, Storyboard.Count);
                        Assert.Equal(0, UISettings.Created.Sum(settings => settings.SubscriptionCount));
                    }
                    finally { EndFixture(); }
                });

                Test(kind + (after ? "退订后抛错" : "退订前抛错") + "不传播或中断落定，遗留回调不能复活旧时钟", () =>
                {
                    Reset();
                    var target = Target();
                    var surface = kind == "Surface" ? new SurfaceMotion(target) : null;
                    var landed = 0;
                    try
                    {
                        Start(kind, target, surface);
                        Storyboard.Advance(30);
                        if (surface is not null) surface.Exit(landed: () => landed++);
                        var stale = Storyboard.Active.Single().CapturePostedCompletion();
                        UISettings.ThrowOnRemove = !after;
                        UISettings.ThrowAfterRemove = after;
                        HomeMotion.ApplyPreference(false);
                        Assert.Equal(0, Storyboard.Count);
                        Assert.True(surface is null || !surface.IsAnimating);
                        Assert.Equal(surface is null ? 1d : 0d, target.Opacity);
                        Assert.Equal(surface is null ? 0 : 1, landed);
                        if (surface is null) AssertResting(target);
                        // A remove-before throw cannot prove native release; the fixture retains that handle.
                        Assert.Equal(after ? 0 : 1, UISettings.Created.Sum(settings => settings.SubscriptionCount));
                        UISettings.ThrowOnRemove = UISettings.ThrowAfterRemove = false;
                        HomeMotion.ApplyPreference(true);
                        Start(kind, target, surface);
                        stale();
                        Assert.Equal(1, Storyboard.Count);
                        UISettings.SetAnimationsEnabled(false);
                        Assert.Equal(0, Storyboard.Count);
                        Assert.Equal(1d, target.Opacity);
                        AssertResting(target);
                        Assert.Equal(surface is null ? 0 : 1, landed);
                        Assert.Equal(after ? 0 : 1, UISettings.Created.Sum(settings => settings.SubscriptionCount));
                    }
                    finally { EndFixture(); }
                });
            }
    }

    private static FrameworkElement Target() => new() { ActualWidth = 20, Height = 20 };
    private static void Start(string kind, FrameworkElement target, SurfaceMotion? surface)
    {
        if (kind == "Home") HomeMotion.Reveal(target, 0, 180, 12);
        else if (kind == "Glyph") GlyphPulse.Play(target);
        else surface!.Enter();
    }
    private static void AssertResting(FrameworkElement target)
    {
        if (target.RenderTransform is TranslateTransform drift) Assert.Equal(0d, drift.Y);
        if (target.RenderTransform is ScaleTransform scale) { Assert.Equal(1d, scale.ScaleX); Assert.Equal(1d, scale.ScaleY); }
    }
    private static void Reset()
    {
        Storyboard.Reset();
        UISettings.Reset();
        HomeMotion.ApplyPreference(true);
    }
    private static void EndFixture()
    {
        UISettings.ThrowOnRemove = UISettings.ThrowAfterRemove = false;
        HomeMotion.ApplyPreference(false);
        Storyboard.Advance(1000);
        // Unknown native handle release is deliberately reported above; end only this fake boundary.
        UISettings.EndFaultFixture();
        UISettings.SetAnimationsEnabled(true);
        HomeMotion.ApplyPreference(true);
    }
}
