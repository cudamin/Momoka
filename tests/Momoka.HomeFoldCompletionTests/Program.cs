using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Action Run)> Cases = [];

    private static int Main()
    {
        RegisterMotion();
        RegisterPage();
        var passed = 0;
        var failed = 0;
        foreach (var test in Cases)
        {
            Storyboard.Created.Clear();
            Storyboard.FailNextBegin = false;
            HomeMotion.AnimationsEnabled = true;
            Momoka.Diagnostics.Log.UseSink(Momoka.Diagnostics.NullLogSink.Instance);
            try
            {
                test.Run();
                passed++;
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("FAIL " + test.Name + ": " + error.Message);
            }
        }
        Console.WriteLine($"TOTAL {Cases.Count}; PASS {passed}; FAIL {failed}; SKIP 0");
        return failed == 0 ? 0 : 1;
    }

    private static void Case(string name, Action run) => Cases.Add((name, run));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected={expected}; actual={actual}");
    private static Storyboard Board(HomeFoldMotion motion) => HomePage.BoardOf(motion);
    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException error) when (error.Message == "Controlled Storyboard.Begin failure") { return; }
        throw new InvalidOperationException("Expected controlled Begin failure");
    }

    private static void RegisterMotion()
    {
        Case("motion/slide-keeps-existing-260ms-ease-out", () =>
        {
            var motion = new HomeFoldMotion();
            var drift = new TranslateTransform();
            motion.Slide(drift, 240, 0);
            var animation = Board(motion).Children.Single();
            Equal(240d, drift.Y, "pre-Begin local value");
            Equal(240d, animation.From!.Value, "from");
            Equal(0d, animation.To!.Value, "to");
            Equal(260d, animation.Duration.TimeSpan.TotalMilliseconds, "duration");
            Equal(EasingMode.EaseOut, animation.EasingFunction!.EasingMode, "easing");
        });
        Case("motion/late-handoff-keeps-150-plus-110ms", () =>
        {
            var motion = new HomeFoldMotion();
            motion.FadeLate(new FrameworkElement());
            var animation = Board(motion).Children.Single();
            Equal(150d, animation.BeginTime!.Value.TotalMilliseconds, "handoff delay");
            Equal(110d, animation.Duration.TimeSpan.TotalMilliseconds, "handoff span");
        });
        Case("motion/landing-fade-keeps-150ms", () =>
        {
            var motion = new HomeFoldMotion();
            motion.FadeIn(new FrameworkElement());
            var animation = Board(motion).Children.Single();
            Equal(0d, animation.BeginTime!.Value.TotalMilliseconds, "fade-in delay");
            Equal(150d, animation.Duration.TimeSpan.TotalMilliseconds, "fade-in span");
        });
        Case("motion/completion-settles-after-framework-stop", () =>
        {
            var motion = new HomeFoldMotion();
            var drift = new TranslateTransform();
            var element = new FrameworkElement();
            motion.Slide(drift, 240, 90);
            motion.Fade(element, 0.25, 0);
            var landed = 0;
            motion.Play(() =>
            {
                Equal(0d, drift.Y, "landed drift must be local zero, not Stop's original 240");
                Equal(1d, element.Opacity, "landed opacity must be restored");
                landed++;
            });
            Board(motion).Complete();
            Equal(1, landed, "natural landed once");
        });
        Case("motion/duplicate-captured-completion-is-inert", () =>
        {
            var motion = new HomeFoldMotion();
            motion.Slide(new TranslateTransform(), 240, 0);
            var landed = 0;
            motion.Play(() => landed++);
            var completion = Board(motion).CaptureCompletion();
            completion();
            completion();
            Equal(1, landed, "captured Completed must land once");
            Equal(1, Board(motion).StopCount, "captured Completed must Stop once");
        });
        Case("motion/cancel-rejects-already-queued-completion", () =>
        {
            var motion = new HomeFoldMotion();
            var drift = new TranslateTransform();
            motion.Slide(drift, 240, 0);
            var landed = 0;
            motion.Play(() => landed++);
            var completion = Board(motion).CaptureCompletion();
            motion.Stop();
            drift.Y = 17;
            completion();
            Equal(0, landed, "cancelled trip must not hand back later");
            Equal(17d, drift.Y, "old completion must not reset a newer local value");
            Equal(1, Board(motion).StopCount, "old callback must not call Stop again");
        });
        Case("motion/repeated-cancel-does-not-stop-new-values", () =>
        {
            var motion = new HomeFoldMotion();
            var drift = new TranslateTransform();
            motion.Slide(drift, 240, 0);
            motion.Play();
            motion.Stop();
            drift.Y = 23;
            motion.Stop();
            Equal(23d, drift.Y, "repeat cancellation must be inert");
            Equal(1, Board(motion).StopCount, "one trip has one Stop");
        });
        Case("motion/cancel-before-play-cannot-restart-trip", () =>
        {
            var motion = new HomeFoldMotion();
            motion.Slide(new TranslateTransform(), 240, 0);
            motion.Stop();
            motion.Play();
            Equal(0, Board(motion).BeginCount, "cancelled plan must not Begin");
        });
        Case("motion/begin-failure-cleans-values-and-handler", () =>
        {
            var motion = new HomeFoldMotion();
            var drift = new TranslateTransform();
            var element = new FrameworkElement();
            motion.Slide(drift, 240, 0);
            motion.Fade(element, 0, 1);
            Board(motion).ThrowOnBegin = true;
            Throws(() => motion.Play());
            Equal(0d, drift.Y, "failed Begin must settle local drift");
            Equal(1d, element.Opacity, "failed Begin must restore local opacity");
            Equal(0, Board(motion).HandlerCount, "failed Begin must detach Completed");
            Equal(1, Board(motion).StopCount, "failed Begin must release framework values");
        });
        Case("motion/cancel-detaches-completed", () =>
        {
            var motion = new HomeFoldMotion();
            motion.Slide(new TranslateTransform(), 240, 0);
            motion.Play();
            motion.Stop();
            Equal(0, Board(motion).HandlerCount, "Stop must detach event owner");
        });
        Case("motion/natural-completion-detaches-completed", () =>
        {
            var motion = new HomeFoldMotion();
            motion.Slide(new TranslateTransform(), 240, 0);
            motion.Play();
            Board(motion).Complete();
            Equal(0, Board(motion).HandlerCount, "natural completion must detach event owner");
        });
        Case("motion/empty-trip-lands-without-framework-begin", () =>
        {
            var motion = new HomeFoldMotion();
            var landed = 0;
            motion.Play(() => landed++);
            Equal(1, landed, "empty plan lands synchronously");
            Equal(0, Board(motion).BeginCount, "empty plan has no animation clock");
        });
        Case("motion/cancelled-empty-trip-does-not-land", () =>
        {
            var motion = new HomeFoldMotion();
            motion.Stop();
            var landed = 0;
            motion.Play(() => landed++);
            Equal(0, landed, "cancelled empty plan must not run landed");
        });
        Case("motion/foreign-transform-not-replaced", () =>
        {
            var transform = new MatrixTransform();
            var element = new FrameworkElement { RenderTransform = transform };
            Check(HomeFoldMotion.DriftOf(element) is null, "foreign transform rejects motion");
            Check(ReferenceEquals(transform, element.RenderTransform), "foreign transform is preserved");
        });
        Case("motion/find-ignores-collapsed-carrier", () =>
        {
            var carrier = new FrameworkElement { Visibility = Visibility.Collapsed };
            carrier.TestChildren.Add(new ShelfStrip());
            Check(HomeFoldMotion.Find<ShelfStrip>(carrier) is null, "collapsed subtree is not a layout carrier");
        });
    }

    private static void RegisterPage()
    {
        Case("page/default-static-layout-has-only-flow-shelf", () =>
        {
            var scene = new Scene();
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "static layout has no trip");
        });
        Case("page/first-visible-on-layout-does-not-invent-flight", () =>
        {
            var scene = new Scene(initialOn: true);
            Check(scene.Page.MainMotion is null, "first real placement is static");
            scene.AssertOverlayOnly();
        });
        Case("page/ascending-completion-clears-current-main", () =>
        {
            var scene = new Scene();
            var motion = scene.Start(true);
            scene.Drain();
            Board(motion).Complete();
            Check(scene.Page.MainMotion is null, "completed climb is not an active fold marker");
            scene.AssertOverlayOnly();
        });
        Case("page/completed-climb-carousel-off-clears-old-host-once", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            var clears = scene.Page.LibraryHost.ClearCount;
            scene.Page.Banner.Visibility = Visibility.Collapsed;
            scene.Page.ExpireSettle();
            scene.Page.UpdateFold();
            scene.AssertFlowOnly();
            scene.Page.UpdateFold();
            Equal(clears + 1, scene.Page.LibraryHost.ClearCount, "host is handed back only once");
        });
        Case("page/completed-climb-empty-slides-clears-old-host", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            scene.Page.ViewModel.Slides.Clear();
            scene.Page.ViewModel.LibraryPlacement = HomeLibraryPlacement.BelowCarousel;
            scene.Page.ExpireSettle();
            scene.Page.UpdateFold();
            scene.AssertFlowOnly();
        });
        Case("page/return-completion-clears-main-and-hands-back-once", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            var motion = scene.Start(false);
            scene.Drain();
            var completion = Board(motion).CaptureCompletion();
            var clears = scene.Page.LibraryHost.ClearCount;
            completion();
            completion();
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "return completion clears active marker");
            Equal(clears + 1, scene.Page.LibraryHost.ClearCount, "return hand-back once");
        });
        Case("page/active-return-keeps-original-carrier-on-same-chrome", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            var motion = scene.Start(false);
            var carrier = scene.Page.LibraryHost.Content;
            scene.Page.Apply();
            Check(ReferenceEquals(motion, scene.Page.MainMotion), "active return remains owner");
            Check(ReferenceEquals(carrier, scene.Page.LibraryHost.Content), "do not remove moving carrier");
            Equal(Visibility.Visible, scene.Page.LibraryOverlay.Visibility, "carrier stays painted during return");
            Check(Board(motion).IsRunning, "same-chrome call must not Stop return");
        });
        Case("page/active-return-keeps-carrier-when-carousel-closes", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            var motion = scene.Start(false);
            scene.Drain();
            scene.Page.Banner.Visibility = Visibility.Collapsed;
            scene.Page.UpdateFold();
            Check(ReferenceEquals(scene.Library, scene.Page.LibraryHost.Content), "running return must retain carrier");
            Equal(Visibility.Visible, scene.Page.LibraryOverlay.Visibility, "running return remains visible");
            Board(motion).Complete();
            scene.AssertFlowOnly();
        });
        Case("page/late-completion-clears-only-current-late", () =>
        {
            var scene = new Scene();
            scene.Start(true);
            var late = scene.Drain()!;
            Board(late).Complete();
            Check(scene.Page.LateMotion is null, "completed late phase is no longer active");
        });
        Case("page/main-completes-before-queued-layout-late-still-runs", () =>
        {
            var scene = new Scene();
            var motion = scene.Start(true);
            Board(motion).Complete();
            var late = scene.Drain();
            Check(late is not null && Board(late).IsRunning, "completed owner still admits its queued late phase");
            Check(Board(late!).Children.Count >= 1, "real delayed row calculations executed");
        });
        Case("page/release-invalidates-queued-late", () =>
        {
            var scene = new Scene();
            var motion = scene.Start(true);
            scene.Page.Release();
            scene.Page.XamlRoot = null;
            scene.Drain();
            Equal(1, Storyboard.Created.Count, "leaving page must not Begin deferred phase");
            Check(!Board(motion).IsRunning && scene.Page.MainMotion is null, "release cancels current main");
        });
        Case("page/playback-return-invalidates-queued-late", () =>
        {
            var scene = new Scene();
            scene.Start(true);
            scene.Page.PreparePlaybackReturn();
            scene.Drain();
            Equal(1, Storyboard.Created.Count, "playback return must not Begin deferred phase");
            Check(scene.Page.EntranceSuppressed, "playback return remains static");
        });
        Case("page/old-return-completion-cannot-clear-new-climb", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            var old = scene.Start(false);
            var oldCompletion = Board(old).CaptureCompletion();
            var newer = scene.Start(true);
            var drift = (TranslateTransform)scene.Page.LibraryOverlay.RenderTransform!;
            var from = drift.Y;
            oldCompletion();
            Check(ReferenceEquals(newer, scene.Page.MainMotion), "old return cannot clear new owner");
            scene.AssertOverlayOnly();
            Equal(from, drift.Y, "old completion cannot reset new animation values");
        });
        Case("page/old-completion-after-release-cannot-clear-reentered-page", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            var old = scene.Start(false);
            var oldCompletion = Board(old).CaptureCompletion();
            scene.Page.Release();
            scene.Page.XamlRoot = new();
            scene.Page.SetReal(true);
            var newer = scene.Start(true);
            oldCompletion();
            Check(ReferenceEquals(newer, scene.Page.MainMotion), "old visit cannot clear new trip");
            scene.AssertOverlayOnly();
        });
        Case("page/old-late-completion-is-inert-after-new-flight", () =>
        {
            var scene = new Scene();
            scene.Start(true);
            var oldLate = scene.Drain()!;
            var oldCompletion = Board(oldLate).CaptureCompletion();
            scene.Start(false);
            var newer = scene.Drain()!;
            var stops = Board(oldLate).StopCount;
            oldCompletion();
            Check(ReferenceEquals(newer, scene.Page.LateMotion), "old late cannot clear newer late owner");
            Equal(stops, Board(oldLate).StopCount, "old late cannot Stop again");
        });
        Case("page/failed-return-plan-foreign-transform-is-static", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            scene.Page.LibraryOverlay.RenderTransform = new MatrixTransform();
            scene.Change(false);
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "foreign transform abandons whole plan");
        });
        Case("page/failed-return-plan-missing-strip-is-static", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            scene.FillerRow.TestChildren.Clear();
            scene.Change(false);
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "missing strip abandons whole plan");
        });
        Case("page/failed-return-plan-implausible-distance-is-static", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            scene.FillerStrip.TestTop = 12000;
            scene.Change(false);
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "implausible distance abandons whole plan");
        });
        Case("page/failed-plan-missing-shelf-does-not-leave-marker", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            scene.Page.ViewModel.LibraryShelf = null;
            scene.Page.SetBefore(620);
            scene.Page.Apply();
            Check(scene.Page.MainMotion is null, "missing shelf cannot leave a planned marker");
            Equal(Visibility.Collapsed, scene.Page.LibraryOverlay.Visibility, "missing shelf has no visible carrier");
            Check(scene.Page.LibraryHost.Content is null, "missing shelf leaves no host content");
        });
        Case("page/stopped-object-is-not-running-return", () =>
        {
            var scene = new Scene();
            var stopped = new HomeFoldMotion();
            stopped.Stop();
            scene.Page.ReplaceMain(stopped);
            scene.Page.LibraryHost.Content = scene.Library;
            scene.Page.LibraryOverlay.Visibility = Visibility.Visible;
            scene.Page.Apply();
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "retained stopped object is not an active flight");
        });
        Case("page/unstarted-object-is-not-running-return", () =>
        {
            var scene = new Scene();
            scene.Page.ReplaceMain(new HomeFoldMotion());
            scene.Page.LibraryHost.Content = scene.Library;
            scene.Page.LibraryOverlay.Visibility = Visibility.Visible;
            scene.Page.Apply();
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "unstarted plan is not an active flight");
        });
        Case("page/begin-failure-return-clears-carrier-and-owner", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            Storyboard.FailNextBegin = true;
            Throws(() => scene.Change(false));
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "failed Begin return has no active owner");
        });
        Case("page/begin-failure-climb-settles-current-static-layout", () =>
        {
            var scene = new Scene();
            Storyboard.FailNextBegin = true;
            Throws(() => scene.Change(true));
            scene.AssertOverlayOnly();
            Check(scene.Page.MainMotion is null, "failed Begin climb has no active owner");
            Equal(0d, ((TranslateTransform)scene.Page.LibraryOverlay.RenderTransform!).Y, "failed climb settles position");
        });
        Case("page/animations-off-after-completion-clears-host", () =>
        {
            var scene = new Scene();
            scene.CompleteClimb();
            HomeMotion.AnimationsEnabled = false;
            scene.Change(false);
            scene.AssertFlowOnly();
            Check(scene.Page.MainMotion is null, "reduced-motion layout does not inherit a completed marker");
        });
        Case("page/rapid-sequential-folds-use-new-owner-and-reject-old-queue", () =>
        {
            var scene = new Scene();
            var first = scene.Start(true);
            var second = scene.Start(false);
            var third = scene.Start(true);
            var late = scene.Drain()!;
            Equal(4, Storyboard.Created.Count, "only newest queued late phase may create a board");
            Check(!Board(first).IsRunning && !Board(second).IsRunning && Board(third).IsRunning, "only current main runs");
            Board(third).Complete();
            Board(late).Complete();
            Check(scene.Page.MainMotion is null && scene.Page.LateMotion is null, "newest completion clears both current markers");
            scene.AssertOverlayOnly();
        });
        Case("page/same-chrome-does-not-restart-active-climb", () =>
        {
            var scene = new Scene();
            var motion = scene.Start(true);
            var boards = Storyboard.Created.Count;
            scene.Page.Apply();
            Equal(boards, Storyboard.Created.Count, "same-chrome does not plan twice");
            Check(ReferenceEquals(motion, scene.Page.MainMotion) && Board(motion).IsRunning, "active climb remains untouched");
        });
        Case("page/release-stops-both-phases-and-detaches-events", () =>
        {
            var scene = new Scene();
            var motion = scene.Start(true);
            var late = scene.Drain()!;
            scene.Page.Release();
            Check(scene.Page.MainMotion is null && scene.Page.LateMotion is null, "release clears current owners");
            Equal(0, Board(motion).HandlerCount, "release detaches main Completed");
            Equal(0, Board(late).HandlerCount, "release detaches late Completed");
            Equal(1, scene.Page.ViewModel.CancelCalls, "actual Release calls VM boundary Cancel once");
        });
        Case("page/playback-return-does-not-reanimate-async-layout", () =>
        {
            var scene = new Scene();
            var motion = scene.Start(true);
            var completion = Board(motion).CaptureCompletion();
            scene.Drain();
            scene.Page.PreparePlaybackReturn();
            var boards = Storyboard.Created.Count;
            scene.Change(false);
            completion();
            scene.AssertFlowOnly();
            scene.Change(true);
            Equal(boards, Storyboard.Created.Count, "async playback-return layout stays static");
            Check(scene.Page.MainMotion is null && scene.Page.LateMotion is null, "playback return has no active phase");
        });
        Case("page/player-layer-keeps-existing-layout-and-host", () =>
        {
            var scene = new Scene(initialOn: true);
            scene.Page.SetPlayerLayer(true);
            scene.Page.Banner.Visibility = Visibility.Collapsed;
            scene.Page.UpdateFold();
            Check(scene.Page.ViewModel.LibraryOnBanner, "player geometry does not choose new fold");
            scene.AssertOverlayOnly();
        });
        Case("page/schedule-fold-coalesces-one-dispatch", () =>
        {
            var scene = new Scene();
            scene.Page.QueueFold();
            scene.Page.QueueFold();
            scene.Page.EndPlayerLayer();
            Equal(1, scene.Page.DispatcherQueue!.PendingCount, "all fold wakeups coalesce");
            scene.Drain();
            scene.Page.QueueFold();
            Equal(1, scene.Page.DispatcherQueue.PendingCount, "later visit can schedule next fold");
        });
        Case("page/settle-protection-retries-without-changing-animation-speed", () =>
        {
            var scene = new Scene();
            var motion = scene.Start(true);
            scene.Page.ViewModel.LibraryPlacement = HomeLibraryPlacement.BelowCarousel;
            scene.Page.UpdateFold();
            Check(scene.Page.ViewModel.LibraryOnBanner, "rapid decision cannot flip before settle");
            Check(ReferenceEquals(motion, scene.Page.MainMotion), "settle retains original trip");
            Check(scene.Page.Retry is { IsRunning: true }, "blocked decision has a retry");
            Check(scene.Page.Retry!.Interval <= TimeSpan.FromMilliseconds(520), "same two-phase settle span");
            Board(motion).Complete();
            scene.Page.ExpireSettle();
            scene.Page.Retry.Fire();
            Check(!scene.Page.ViewModel.LibraryOnBanner, "retry publishes the retained decision");
        });
        Case("page/rejected-late-dispatch-does-not-retain-completed-main", () =>
        {
            var scene = new Scene();
            scene.Page.DispatcherQueue!.AcceptEnqueue = false;
            var motion = scene.Start(true);
            Board(motion).Complete();
            Check(scene.Page.MainMotion is null, "queue rejection does not strand main active marker");
            Equal(1, Storyboard.Created.Count, "rejected late phase is not created");
        });
        Case("page/no-dispatcher-still-completes-current-main", () =>
        {
            var scene = new Scene();
            scene.Page.DispatcherQueue = null;
            var motion = scene.Start(true);
            Board(motion).Complete();
            Check(scene.Page.MainMotion is null, "no dispatcher does not strand completed main");
        });
        Case("page/cancel-clears-captured-main-before-framework-reentry", () =>
        {
            var scene = new Scene();
            var old = scene.Start(true);
            var newer = new HomeFoldMotion();
            newer.Slide(new TranslateTransform(), 50, 0);
            Board(old).OnStop = () =>
            {
                scene.Page.ReplaceMain(newer);
                newer.Play();
            };
            scene.Page.PreparePlaybackReturn();
            Check(ReferenceEquals(newer, scene.Page.MainMotion), "old cancellation cannot clear reentrant current main");
            Check(Board(newer).IsRunning, "new main remains active");
        });
        Case("page/cancel-clears-captured-late-before-framework-reentry", () =>
        {
            var scene = new Scene();
            scene.Start(true);
            var old = scene.Drain()!;
            var newer = new HomeFoldMotion();
            newer.Slide(new TranslateTransform(), 50, 0);
            Board(old).OnStop = () =>
            {
                scene.Page.ReplaceLate(newer);
                newer.Play();
            };
            scene.Page.Release();
            Check(ReferenceEquals(newer, scene.Page.LateMotion), "old cancellation cannot clear reentrant current late");
            Check(Board(newer).IsRunning, "new late remains active");
        });
        Case("page/empty-delayed-plan-does-not-leave-late-marker", () =>
        {
            var scene = new Scene();
            scene.Page.ViewModel.LibraryOnBanner = true;
            scene.Page.SetBefore(620);
            scene.Page.Apply();
            scene.Drain();
            Check(scene.Page.LateMotion is null, "empty late plan completes synchronously without a marker");
        });
    }

    private sealed class Scene
    {
        internal HomePage Page { get; } = new();
        internal CardShelf Library { get; } = new();
        private CardShelf Filler { get; } = new();
        private CardShelf Below { get; } = new();
        private FrameworkElement LibraryRow { get; } = Row(520);
        internal FrameworkElement FillerRow { get; } = Row(760);
        private FrameworkElement BelowRow { get; } = Row(1020);
        internal ShelfStrip FillerStrip => FillerRow.TestChildren.OfType<ShelfStrip>().Single();
        internal Scene(bool initialOn = false)
        {
            Page.ViewModel.LibraryShelf = Library;
            Page.ViewModel.Shelves.AddRange([Library, Filler, Below]);
            Page.ViewModel.LibraryOnBanner = initialOn;
            Page.ViewModel.PublishLayout = Publish;
            Publish(initialOn);
            Page.Apply();
            Page.ExpireSettle();
        }
        private static FrameworkElement Row(double top)
        {
            var row = new FrameworkElement { ActualHeight = 240, TestTop = top, RenderTransform = new TranslateTransform() };
            row.TestChildren.Add(new ShelfHead { ActualHeight = 36, TestTop = 0 });
            row.TestChildren.Add(new ShelfStrip { ActualHeight = 180, TestTop = top + 100 });
            return row;
        }
        // Precomputed framework layout snapshots, not a duplicate VM or fold planner.
        private void Publish(bool on)
        {
            Page.HomeRows.Clear();
            Page.ShelfRepeater.Elements.Clear();
            if (!on)
            {
                Page.HomeRows.Add(Library);
                Page.ShelfRepeater.Elements.Add(0, LibraryRow);
            }
            var index = on ? 0 : 1;
            Page.HomeRows.Add(Filler);
            Page.HomeRows.Add(Below);
            FillerRow.TestTop = on ? 520 : 760;
            BelowRow.TestTop = on ? 780 : 1020;
            Page.ShelfRepeater.Elements.Add(index, FillerRow);
            Page.ShelfRepeater.Elements.Add(index + 1, BelowRow);
        }
        internal void Change(bool on)
        {
            Page.ExpireSettle();
            Page.ViewModel.LibraryPlacement = on ? HomeLibraryPlacement.OnCarousel : HomeLibraryPlacement.BelowCarousel;
            Page.UpdateFold();
        }
        internal HomeFoldMotion Start(bool on)
        {
            Change(on);
            return Page.MainMotion ?? throw new InvalidOperationException("Expected real page to plan a motion");
        }
        internal HomeFoldMotion? Drain()
        {
            Page.DispatcherQueue?.Drain();
            return Page.LateMotion;
        }
        internal void CompleteClimb()
        {
            var motion = Start(true);
            var late = Drain();
            Board(motion).Complete();
            if (late is not null) Board(late).Complete();
        }
        internal void AssertFlowOnly()
        {
            Equal(Visibility.Collapsed, Page.LibraryOverlay.Visibility, "old overlay is collapsed");
            Check(Page.LibraryHost.Content is null, "old overlay host Content is null");
            Equal(1, Page.HomeRows.Count(row => ReferenceEquals(row, Library)), "current library exists once in flow");
        }
        internal void AssertOverlayOnly()
        {
            Equal(Visibility.Visible, Page.LibraryOverlay.Visibility, "overlay carrier is visible");
            Check(ReferenceEquals(Library, Page.LibraryHost.Content), "host carries current library");
            Equal(0, Page.HomeRows.Count(row => ReferenceEquals(row, Library)), "library is not duplicated in flow");
        }
    }
}
