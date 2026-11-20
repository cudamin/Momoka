using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class BannerCycleClockTests
{
    internal static void Register(Action<string, Action> test)
    {
        RegisterProductionWiring(test);
        test("banner/cycle-running-time-uses-monotonic-milliseconds", () =>
        {
            var clock = new BannerCycleClock();
            clock.Restart(100_000_000_000);
            Equal(0, clock.ElapsedMilliseconds(100_000_000_000), "new cycle starts at zero");
            Equal(2_750, clock.ElapsedMilliseconds(100_000_002_750), "late UI tick catches up elapsed time");
            Check(!clock.IsPaused, "new cycle runs by default");
        });
        test("banner/cycle-hover-freezes-exact-progress-and-resumes-remainder", () =>
        {
            var clock = new BannerCycleClock();
            clock.Restart(1_000);
            clock.SetPaused(true, 3_750);
            Equal(2_750, clock.ElapsedMilliseconds(3_750), "hover captures the precise current progress");
            Equal(2_750, clock.ElapsedMilliseconds(603_750), "long hover cannot consume the remaining dwell");
            clock.SetPaused(false, 603_750);
            Equal(2_750, clock.ElapsedMilliseconds(603_750), "leaving hover has no progress jump");
            Equal(7_999, clock.ElapsedMilliseconds(608_999), "only 1 ms remains of an 8 s dwell");
            Equal(8_000, clock.ElapsedMilliseconds(609_000), "cycle completes after the actual remaining time");
        });
        test("banner/cycle-repeated-pointer-moves-and-exits-are-idempotent", () =>
        {
            var clock = new BannerCycleClock();
            clock.Restart(0);
            clock.SetPaused(false, 500);
            Equal(750, clock.ElapsedMilliseconds(750), "repeated resume cannot reset a running cycle");
            clock.SetPaused(true, 1_000);
            clock.SetPaused(true, 2_000);
            clock.SetPaused(true, 3_000);
            Equal(1_000, clock.ElapsedMilliseconds(10_000), "moves within a paused hero do not recapture time");
            clock.SetPaused(false, 10_000);
            clock.SetPaused(false, 10_500);
            Equal(2_000, clock.ElapsedMilliseconds(11_000), "duplicate leave cannot move the resume origin");
        });
        test("banner/cycle-multiple-hover-visits-retain-all-running-intervals", () =>
        {
            var clock = new BannerCycleClock();
            clock.Restart(100);
            clock.SetPaused(true, 600);
            clock.SetPaused(false, 2_600);
            clock.SetPaused(true, 3_850);
            Equal(1_750, clock.ElapsedMilliseconds(20_000), "second hover includes both prior running intervals");
            clock.SetPaused(false, 20_000);
            Equal(2_000, clock.ElapsedMilliseconds(20_250), "third running interval continues the same cycle");
        });
        test("banner/cycle-manual-slide-change-under-hover-stays-paused", () =>
        {
            var clock = new BannerCycleClock();
            clock.Restart(0);
            clock.SetPaused(true, 2_000);
            clock.Restart(4_000);
            Check(clock.IsPaused, "changing slides cannot defeat the pointer pause");
            Equal(0, clock.ElapsedMilliseconds(40_000), "replacement slide has not consumed dwell under hover");
            clock.SetPaused(false, 40_000);
            Equal(250, clock.ElapsedMilliseconds(40_250), "replacement starts its full dwell after pointer leaves");
        });
        test("banner/cycle-pause-at-deadline-freezes-the-deadline", () =>
        {
            var clock = new BannerCycleClock();
            clock.Restart(10_000);
            clock.SetPaused(true, 18_000);
            Check(clock.IsPaused, "tick handler can suppress automatic advance while paused at deadline");
            Equal(8_000, clock.ElapsedMilliseconds(99_000), "pause does not overrun or wrap the dwell boundary");
            clock.SetPaused(false, 99_000);
            Equal(8_000, clock.ElapsedMilliseconds(99_000), "resume preserves an already due deadline");
        });
        test("banner/cycle-mode-change-can-release-pause-without-progress-reset", () =>
        {
            var clock = new BannerCycleClock();
            clock.Restart(0);
            clock.SetPaused(true, 1_500);
            clock.SetPaused(false, 60_000);
            Check(!clock.IsPaused, "leaving store mode can release its pointer pause");
            Equal(2_000, clock.ElapsedMilliseconds(60_500), "mode change continues the existing slide's remainder");
        });
        test("banner/cycle-reentry-can-clear-old-hover-before-starting-fresh", () =>
        {
            var clock = new BannerCycleClock();
            clock.Restart(0);
            clock.SetPaused(true, 1_000);
            clock.SetPaused(false, 70_000);
            clock.Restart(70_000);
            Check(!clock.IsPaused, "navigation reentry must not retain stale pointer pause");
            Equal(500, clock.ElapsedMilliseconds(70_500), "fresh entry starts its own cycle");
        });
    }

    private static void RegisterProductionWiring(Action<string, Action> test)
    {
        test("banner/actual-cycle-store-hover-pauses-and-resumes-the-existing-remainder", () =>
        {
            var banner = CreateStoreBanner();
            banner.SetNow(3_000);
            banner.Tick();
            EqualProgress(0.375, banner.Progress, "actual tick paints elapsed dwell progress");
            EqualProgress(banner.Progress, banner.PaintedProgress, "pie and linear progress share the same reading");
            banner.SetHeroHover(true);
            Check(banner.Paused && !banner.Running, "hover stops the actual dispatcher timer");
            banner.SetNow(60_000);
            banner.SetHeroHover(true);
            banner.Tick();
            Equal(0, banner.Advances, "even an already queued late tick cannot change the hovered slide");
            EqualProgress(0.375, banner.Progress, "hover moves and late ticks preserve exact progress");
            banner.SetHeroHover(false);
            Check(!banner.Paused && banner.Running, "leaving the hero restarts timer delivery");
            EqualProgress(0.375, banner.PaintedProgress, "resuming does not erase the pie's partial progress");
            banner.SetNow(64_999);
            banner.Tick();
            Equal(0, banner.Advances, "actual tick waits for all 5 s of remaining time");
            banner.SetNow(65_000);
            banner.Tick();
            Equal(1, banner.Advances, "actual tick advances exactly when the preserved remainder expires");
            Equal(1, banner.CurrentIndex, "actual advance decision targets the following slide");
            EqualProgress(0, banner.PaintedProgress, "the next slide begins a fresh full pie");
        });
        test("banner/actual-cycle-full-layout-ignores-hero-hover-and-advances", () =>
        {
            var banner = CreateStoreBanner();
            banner.SetStore(false);
            banner.SetNow(2_000);
            banner.SetHeroHover(true);
            Check(!banner.Paused && banner.Running, "full layout retains the existing continuous-clock behavior");
            banner.SetNow(8_000);
            banner.Tick();
            Equal(1, banner.Advances, "full layout still advances despite pointer hover");
        });
        test("banner/actual-cycle-store-mode-switch-releases-pause-without-restarting", () =>
        {
            var banner = CreateStoreBanner();
            banner.SetNow(1_000);
            banner.SetHeroHover(true);
            banner.SetNow(50_000);
            banner.SetStore(false);
            Check(!banner.Paused && banner.Running, "switching to full mode releases store-only pause");
            EqualProgress(0.125, banner.Progress, "switching mode preserves already consumed time");
            banner.SetNow(57_000);
            banner.Tick();
            Equal(1, banner.Advances, "mode switch resumes exactly the remaining 7 s");
        });
        test("banner/actual-cycle-period-reset-under-hover-remains-paused", () =>
        {
            var banner = CreateStoreBanner();
            banner.SetNow(1_000);
            banner.SetHeroHover(true);
            banner.SetNow(20_000);
            banner.SetActive(true);
            Check(banner.Paused && !banner.Running, "production SyncTimer honors the current hero hover");
            EqualProgress(0, banner.Progress, "explicit new cycle starts at zero consumed time");
            banner.SetNow(100_000);
            banner.Tick();
            Equal(0, banner.Advances, "new cycle cannot run underneath pointer hover");
            banner.SetHeroHover(false);
            banner.SetNow(108_000);
            banner.Tick();
            Equal(1, banner.Advances, "new cycle receives the complete dwell after the pointer leaves");
        });
        test("banner/actual-cycle-paused-deadline-does-not-advance-until-resume", () =>
        {
            var banner = CreateStoreBanner();
            banner.SetNow(8_000);
            banner.SetHeroHover(true);
            banner.Tick();
            Equal(0, banner.Advances, "pause at the exact deadline suppresses the queued timer callback");
            EqualProgress(1, banner.PaintedProgress, "pause keeps the truthful due progress");
            banner.SetNow(80_000);
            banner.SetHeroHover(false);
            banner.Tick();
            Equal(1, banner.Advances, "resuming an already due cycle advances on its next tick");
        });
        test("banner/actual-cycle-inactive-and-single-slide-stay-stopped-after-hover", () =>
        {
            var banner = CreateStoreBanner();
            banner.SetActive(false);
            banner.SetHeroHover(true);
            banner.SetNow(30_000);
            banner.SetHeroHover(false);
            Check(!banner.Running, "releasing hover cannot restart an off-tree banner");
            banner.Tick();
            Equal(0, banner.Advances, "inactive callback has no automatic navigation");
            banner.SetActive(true);
            banner.Apply([new BannerSlide("only")]);
            banner.SetHeroHover(true);
            banner.SetHeroHover(false);
            Check(!banner.Running, "single-slide banner remains stopped after hover ends");
            banner.Tick();
            Equal(0, banner.Advances, "single-slide callback cannot manufacture a next slide");
        });
    }

    private static BannerClockFixture CreateStoreBanner()
    {
        var banner = new BannerClockFixture();
        banner.SetNow(0);
        banner.SetStore(true);
        banner.Apply([new BannerSlide("one"), new BannerSlide("two")]);
        return banner;
    }

    private static void EqualProgress(double expected, double actual, string detail)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"{detail}: expected {expected}, got {actual}");
    }

    private static void Equal(long expected, long actual, string detail)
    {
        if (expected != actual) throw new InvalidOperationException($"{detail}: expected {expected}, got {actual}");
    }

    private static void Check(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
