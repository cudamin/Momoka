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
        BannerCycleClockTests.Register(Case);
        Cases.Add(("poster/demand-waits-for-tree-and-captures-own-gate", () =>
        {
            var poster = new PosterDemandFixture();
            BrowsePresentation.Gate = new TaskCompletionSource().Task;
            poster.Request();
            Equal(0, poster.Card.Requests.Count, "off-tree binding cannot bypass the owner gate");
            poster.XamlRoot = new object();
            poster.Request();
            Check(ReferenceEquals(poster, BrowsePresentation.LastOwner), "resolve this control's frame");
            Check(ReferenceEquals(BrowsePresentation.Gate, poster.Card.Requests.Single()), "capture exact gate");
            poster.Request();
            Equal(2, poster.Card.Requests.Count, "still attached recycled control remains eligible");
        }
        ));
        Cases.Add(("banner/prefetch-waits-for-tree-and-captures-own-gate", () =>
        {
            var banner = new BannerDemandFixture();
            BrowsePresentation.Gate = new TaskCompletionSource().Task;
            banner.Request();
            Check(banner.Slides.All(slide => slide.Requests.Count == 0), "off-tree Show waits for Loaded");
            banner.XamlRoot = new object();
            banner.Request();
            Check(ReferenceEquals(banner, BrowsePresentation.LastOwner), "resolve this banner's frame");
            Check(banner.Slides.All(slide => ReferenceEquals(BrowsePresentation.Gate, slide.Requests.Single())),
                "visible slide and neighbors share their own navigation gate");
        }
        ));
        Cases.Add(("episode/demand-waits-for-tree-and-captures-own-gate", () =>
        {
            var row = new EpisodeDemandFixture();
            BrowsePresentation.Gate = new TaskCompletionSource().Task;
            row.Request();
            Equal(0, row.Card.Requests.Count, "off-tree list row cannot bypass gate");
            row.XamlRoot = new object();
            row.Request();
            Check(ReferenceEquals(row, BrowsePresentation.LastOwner), "list row resolves its own frame");
            Check(ReferenceEquals(BrowsePresentation.Gate, row.Card.Requests.Single()), "list row keeps exact gate");
        }
        ));
        Case("banner/stacked-store-recommendations-prefetch-all-visible-cards", () =>
        {
            var banner = new BannerDemandFixture(10) { XamlRoot = new object() };
            BrowsePresentation.Gate = new TaskCompletionSource().Task;
            Check(!HomeCarousel.ShowsRail(CarouselLayout.Store, 848, banner.Slides.Count),
                "fixture is below the side-rail threshold");
            banner.SetLayout(CarouselLayout.Store, 848);
            banner.Request();
            var decoded = banner.Slides.Select((slide, index) => (slide, index))
                .Where(entry => entry.slide.PictureRetained).Select(entry => entry.index);
            Check(decoded.SequenceEqual(new[] { 0, 1, 2, 3, 9 }),
                "stacked cards retain the current, previous and all three following pictures");
            Check(banner.Slides.Where(slide => slide.PictureRetained)
                .All(slide => ReferenceEquals(BrowsePresentation.Gate, slide.Requests.Single())),
                "all stacked-card requests capture their own owner's navigation gate");
        });
        Case("banner/hiding-store-recommendations-releases-extra-decoded-pictures", () =>
        {
            var banner = new BannerDemandFixture(10) { XamlRoot = new object() };
            banner.SetLayout(CarouselLayout.Store, 848);
            banner.Request();
            Check(banner.Slides[2].PictureRetained && banner.Slides[3].PictureRetained,
                "stacked recommendations were decoded before shrinking");
            var releases = banner.Slides[2].Releases;
            banner.SetLayout(CarouselLayout.Store, HomeCarousel.RecommendationsMinWidth - 1);
            banner.Request();
            var decoded = banner.Slides.Select((slide, index) => (slide, index))
                .Where(entry => entry.slide.PictureRetained).Select(entry => entry.index);
            Check(decoded.SequenceEqual(new[] { 0, 1, 9 }),
                "hiding recommendations leaves only the main-card neighbor cache");
            Equal(releases + 1, banner.Slides[2].Releases, "newly hidden next-next picture is released");
            Check(!banner.Slides[3].PictureRetained, "third recommendation releases its decoded picture");
            banner.SetLayout(CarouselLayout.Store, 848);
            banner.Request();
            Check(banner.Slides[2].PictureRetained && banner.Slides[3].PictureRetained,
                "restoring stacked recommendations demands released images again");
        });
        Case("banner/recommendation-prefetch-stays-bounded-through-wraparound", () =>
        {
            var banner = new BannerDemandFixture(15) { XamlRoot = new object() };
            banner.SetLayout(CarouselLayout.Store, 848);
            for (var index = 0; index < banner.Slides.Count; index++)
            {
                banner.SetCurrent(index);
                banner.Request();
                Equal(5, banner.Slides.Count(slide => slide.PictureRetained),
                    "paging keeps exactly the main card, previous card and three recommendations");
                Check(banner.Slides[index].PictureRetained, "the current main picture is never released");
            }
            var decoded = banner.Slides.Select((slide, index) => (slide, index))
                .Where(entry => entry.slide.PictureRetained).Select(entry => entry.index);
            Check(decoded.SequenceEqual(new[] { 0, 1, 2, 13, 14 }),
                "last slide wraps recommendations to the start and releases old middle slides");
        });
        Cases.Add(("banner/warm-loaded-keeps-cached-art-but-fades-late-art", () =>
        {
            var banner = new BannerReturnFixture();
            banner.Loaded();
            Equal(1, banner.Rises, "first visit retains entry");
            banner.DuringShow = () => { banner.RestoreImage(); banner.RestoreLogo(); };
            banner.Loaded();
            Equal(1, banner.Rises, "warm Loaded must not replay text entry");
            Equal(0, banner.ImageFades, "restored image must not fade in again");
            Equal(0, banner.LogoFades, "restored logo must not fade in again");
            Equal(1d, banner.PictureOpacity, "restored picture is fully visible");
            Equal(1d, banner.LogoOpacity, "restored logo is fully visible");
            banner.DuringShow = null;
            banner.RestoreImage();
            banner.RestoreLogo();
            Equal(1, banner.ImageFades, "late image after warm entry must fade");
            Equal(1, banner.LogoFades, "late logo after warm entry must fade");
            banner.Advance();
            banner.RestoreImage();
            Equal(2, banner.Rises, "later slide keeps entry decoration");
            Equal(2, banner.ImageFades, "later slide keeps crossfade");
        }
        ));
        Cases.Add(("R06-008 carousel-single-to-many-clock", () =>
        {
            var clock = new BannerClockFixture();
            var first = new BannerSlide("first");
            var second = new BannerSlide("second");
            clock.Apply([first]);
            Check(!clock.Running, "a single slide must stop the clock");
            clock.Apply([first, second]);
            Check(clock.Running, "adding a second slide must start automatic paging");
            var started = clock.Starts;
            clock.Apply([first, second, new BannerSlide("third")]);
            Equal(started, clock.Starts, "ordinary nonempty refresh preserves the cycle");
            clock.Apply([first]);
            Check(!clock.Running, "returning to one slide stops the clock");
            clock.SetActive(false);
            clock.Apply([first, second]);
            Check(!clock.Running, "off-tree refresh cannot start the clock");
            clock.SetActive(true);
            Check(clock.Running, "reattaching multiple slides resumes automatic paging");
        }
        ));
        Cases.Add(("banner/empty-list-clears-clock", () =>
        {
            var clock = new BannerClockFixture();
            clock.Apply([new BannerSlide("first"), new BannerSlide("second")]);
            Check(clock.Running, "nonempty carousel runs");
            clock.Apply([]);
            Check(!clock.Running, "empty carousel cannot retain paging timer");
        }
        ));
        Cases.Add(("banner/loading-placeholder-updates-before-same-null-shortcut", () =>
        {
            var banner = new BannerReturnFixture();
            banner.SetLoadingPicture();
            Check(banner.ShimmerActive, "first null picture still enables loading placeholder before same-null return");
            banner.RestoreImage();
            Check(!banner.ShimmerActive, "decoded picture disables loading sweep");
            Equal(1, banner.ShimmerStops, "arrival stops the previously active placeholder");
            banner.SetLoadingPicture();
            Check(banner.ShimmerActive, "next missing picture enables the placeholder again");
            banner.RestoreImage();
            Equal(2, banner.ShimmerStops, "second arrival stops only the current placeholder");
        }
        ));
        Cases.Add(("banner/empty-list-stops-loading-placeholder", () =>
        {
            var clock = new BannerClockFixture();
            clock.Apply([new BannerSlide("first"), new BannerSlide("second")]);
            clock.SetLoadingPlaceholder();
            Check(clock.ShimmerActive, "fixture has an active pending picture");
            clock.Apply([]);
            Check(!clock.ShimmerActive, "production empty Apply cannot retain the loading placeholder");
            Equal(1, clock.ShimmerStops, "empty Apply stops the current loading placeholder");
        }
        ));
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
        foreach (var preference in new[] { HomeLibraryPlacement.Auto, HomeLibraryPlacement.OnCarousel })
            foreach (var width in new[] { 848d, 1120d })
                Case($"page/store-keeps-library-below/{preference}/{width}", () =>
                {
                    var scene = new Scene();
                    scene.Page.ViewModel.LibraryPlacement = preference;
                    scene.Page.Banner.Layout = CarouselLayout.Store;
                    scene.Page.Banner.ActualWidth = width;
                    foreach (var height in new[] { 560d, 900d, 300d })
                    {
                        scene.Page.Scroller.ActualHeight = height;
                        scene.Page.UpdateFold();
                        Check(!scene.Page.ViewModel.LibraryOnBanner, "store ignores saved auto/on-carousel preference");
                        scene.AssertFlowOnly();
                    }
                    Equal(preference, scene.Page.ViewModel.LibraryPlacement, "store does not overwrite saved placement");
                    Equal(0, Storyboard.Created.Count, "store cannot start a fold trip");
                });
        Case("page/store-switch-cancels-current-fold-and-retains-full-preference", () =>
        {
            var scene = new Scene();
            var old = scene.Start(true);
            var captured = Board(old).CaptureCompletion();
            Check(scene.Page.ViewModel.LibraryOnBanner, "full layout is on the carousel before switch");
            scene.Page.Banner.Layout = CarouselLayout.Store;
            scene.Page.UpdateFold();
            Check(!scene.Page.ViewModel.LibraryOnBanner, "store switch settles before fold throttle expires");
            Check(scene.Page.MainMotion is null && scene.Page.LateMotion is null, "store cancels both fold owners");
            scene.AssertFlowOnly();
            scene.Drain();
            captured();
            scene.AssertFlowOnly();
            Equal(HomeLibraryPlacement.OnCarousel, scene.Page.ViewModel.LibraryPlacement, "saved preference survives store");
            scene.Page.Banner.Layout = CarouselLayout.Full;
            scene.Page.UpdateFold();
            Check(scene.Page.ViewModel.LibraryOnBanner, "full layout restores the saved on-carousel preference");
        });
        Case("page/store-and-disabled-banner-clear-overlay-without-live-layout-or-images", () =>
        {
            foreach (var store in new[] { false, true })
            {
                var scene = new Scene(initialOn: true);
                scene.Page.ViewModel.LibraryPlacement = HomeLibraryPlacement.OnCarousel;
                scene.Page.Banner.Layout = store ? CarouselLayout.Store : CarouselLayout.Full;
                scene.Page.Ui.ShowHomeBanner = store;
                scene.Page.Banner.Visibility = Visibility.Collapsed;
                scene.Page.ViewModel.Slides.Clear();
                scene.Page.XamlRoot = null;
                scene.Page.SetPlayerLayer(true);
                scene.Page.UpdateFold();
                Check(!scene.Page.ViewModel.LibraryOnBanner, "mode change does not wait for player or layout readiness");
                scene.AssertFlowOnly();
                Equal(HomeLibraryPlacement.OnCarousel, scene.Page.ViewModel.LibraryPlacement, "hidden carousel does not erase preference");
            }
        });
        Case("page/header-mode-layout-anchoring-is-not-user-down-scroll", () =>
        {
            var page = new HomePage();
            page.ScrollHeader(240);
            page.ScrollHeader(228);
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "up scroll reveals before mode change");
            page.Ui.EnableImmersive = false;
            page.TestLayout = () => page.ScrollHeader(118);
            page.NotifyAppearance(page.Ui);
            page.ScrollHeader(118);
            Equal(HomeHeaderMode.Top, page.HeaderMode, "store remains visible after layout anchoring");
            page.Ui.EnableImmersive = true;
            var layoutCalls = 0;
            page.TestLayout = () =>
            {
                layoutCalls++;
                page.ScrollHeader(228);
                Equal(HomeHeaderMode.Revealed, page.HeaderMode, "layout ViewChanged must not hide header transiently");
            };
            page.NotifyAppearance(page.Ui);
            if (layoutCalls == 0) page.TestLayout();
            Equal(228d, page.Scroller.VerticalOffset, "native layout restores original offset");
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "mode return is immediately usable at depth");
            Equal(1, layoutCalls, "one mode layout establishes the final anchor");
            page.ScrollHeader(239);
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "real down jitter remains below 12 DIP");
            page.ScrollHeader(240);
            Equal(HomeHeaderMode.Hidden, page.HeaderMode, "real 12 DIP down movement still hides");
            page.ScrollHeader(228);
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "real reverse movement still restores");
        });
        Case("page/header-mode-layout-failure-does-not-freeze-future-scroll", () =>
        {
            var page = new HomePage();
            page.Scroller.VerticalOffset = 118;
            page.Ui.EnableImmersive = false;
            page.NotifyAppearance(page.Ui);
            page.Ui.EnableImmersive = true;
            var expected = new InvalidOperationException("mode layout failed");
            page.TestLayout = () =>
            {
                page.ScrollHeader(228);
                throw expected;
            };
            Exception? observed = null;
            try { page.NotifyAppearance(page.Ui); }
            catch (Exception error) { observed = error; }
            Check(ReferenceEquals(expected, observed), "layout failure must remain a failure");
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "failure still rebases current offset");
            page.ScrollHeader(240);
            Equal(HomeHeaderMode.Hidden, page.HeaderMode, "finally clears reflow guard");
        });
        Case("page/header-mode-layout-release-and-foreign-notification-cannot-revive-page", () =>
        {
            var page = new HomePage();
            page.Ui.EnableImmersive = false;
            page.NotifyAppearance(page.Ui);
            page.Ui.EnableImmersive = true;
            var layoutCalls = 0;
            page.TestLayout = () => layoutCalls++;
            page.NotifyAppearance(new() { EnableImmersive = true });
            Equal(0, layoutCalls, "foreign settings cannot reflow this page");
            page.TestLayout = page.Release;
            page.NotifyAppearance(page.Ui);
            Equal(HomeHeaderMode.Top, page.HeaderMode, "release wins over mode completion");
            var refreshes = page.HeaderRefreshes;
            page.ScrollHeader(300);
            Equal(refreshes, page.HeaderRefreshes, "released layout callback cannot publish header");
        });
        Case("page/header-real-events-preserve-direction-through-appearance-and-release", () =>
        {
            var page = new HomePage();
            page.Ui.EnableImmersive = true;
            page.NotifyAppearance(page.Ui);
            page.ScrollHeader(200);
            Equal(HomeHeaderMode.Hidden, page.HeaderMode, "down scroll hides");
            page.TestLayout = () => throw new InvalidOperationException("same-mode notification forced a layout");
            page.NotifyAppearance(page.Ui);
            Equal(HomeHeaderMode.Hidden, page.HeaderMode, "same-mode appearance preserves direction");
            page.TestLayout = null;
            page.ScrollHeader(180);
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "up scroll restores");
            page.ScrollHeader(220);
            page.Ui.EnableImmersive = false;
            page.NotifyAppearance(page.Ui);
            Equal(HomeHeaderMode.Top, page.HeaderMode, "store mode resets");
            page.Ui.EnableImmersive = true;
            page.NotifyAppearance(page.Ui);
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "enter immersive at depth remains usable");
            page.ScrollHeader(250);
            var releasedUi = page.Ui;
            page.Release();
            Equal(HomeHeaderMode.Top, page.HeaderMode, "release clears hidden state");
            var refreshes = page.HeaderRefreshes;
            page.ScrollHeader(400);
            page.NotifyAppearance(releasedUi);
            Equal(refreshes, page.HeaderRefreshes, "late events cannot publish to shell");
        });
        Case("page/header-restore-correlations-ignore-old-completion-and-resume-user-scroll", () =>
        {
            var page = new HomePage();
            page.Ui.EnableImmersive = true;
            page.SetPendingHeaderRestore(500);
            page.ScrollHeader(100);
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "pending restore does not hide");
            page.SetPendingHeaderRestore(null);
            page.BeginHeaderRestore(500);
            page.ScrollHeader(500);
            page.CompleteHeaderRestore(0);
            Check(page.RestoringHeader, "old completion does not end this restore");
            page.ScrollHeader(550);
            Equal(HomeHeaderMode.Revealed, page.HeaderMode, "restore still visible");
            page.CompleteHeaderRestore(1);
            Check(page.RestoringHeader, "completion alone does not end the layout period");
            page.ExpireHeaderRestore();
            Check(!page.RestoringHeader, "finite layout period ends restore");
            page.ScrollHeader(580);
            Equal(HomeHeaderMode.Hidden, page.HeaderMode, "new user scroll hides");
            page.FocusHeaderScroll();
            Check(page.Scroller.FocusedSelf && !page.Scroller.IsTabStop, "viewport takes focus and restores tab order");
            Equal(580d, page.Scroller.VerticalOffset, "focus helper issues no scrolling");
            page.ScrollHeader(0);
            Equal(HomeHeaderMode.Top, page.HeaderMode, "top is transparent");
        });
        Case("page/header-return-completion-at-wrong-offset-retries-with-original-target", () =>
        {
            var page = new HomePage();
            page.BeginHeaderRestore(300);
            Equal(300d, page.Scroller.Requests.Single().Offset, "initial requested position");
            page.CompleteHeaderRestore(1);
            Equal(2, page.Scroller.Requests.Count, "completed request at zero is repaired");
            Equal(300d, page.Scroller.Requests.Last().Offset, "original target survives completion");
            page.ScrollHeader(300);
            page.CompleteHeaderRestore(2);
            page.ScrollHeader(0);
            Equal(3, page.Scroller.Requests.Count, "late focus movement is repaired during layout");
            page.ScrollHeader(300);
            page.CompleteHeaderRestore(3);
            page.ExpireHeaderRestore();
            Equal(0, page.Scroller.HandlerCount, "layout completion releases user-input handlers");
            page.ScrollHeader(120);
            Equal(3, page.Scroller.Requests.Count, "user remains free after layout period");
        });
        Case("page/header-return-keeps-target-through-clamped-extent-and-ignores-sync-old-completion", () =>
        {
            var page = new HomePage();
            page.Scroller.ScrollableHeight = 100;
            page.BeginHeaderRestore(300);
            Equal(100d, page.Scroller.Requests.Single().Offset, "initial request clamps to current extent");
            page.ScrollHeader(100);
            page.CompleteHeaderRestore(1);
            page.Scroller.BeforeScrollRequest = () => page.CompleteHeaderRestore(1);
            page.Scroller.ScrollableHeight = 1000;
            page.TickHeaderRestore();
            Equal(2, page.Scroller.Requests.Count, "extent recovery submits one request despite old completion");
            Equal(300d, page.Scroller.Requests.Last().Offset, "extent recovery uses original position");
            page.Release();
            Equal(0, page.Scroller.HandlerCount, "leave detaches input handlers");
            page.TickHeaderRestore();
            page.CompleteHeaderRestore(2);
            Equal(2, page.Scroller.Requests.Count, "late callbacks after leave cannot scroll");
        });
        Case("page/header-return-releases-for-pointer-navigation-key-and-native-interaction", () =>
        {
            foreach (var kind in new[] { "wheel", "pointer", "key", "interaction" })
            {
                var page = new HomePage();
                page.BeginHeaderRestore(300);
                if (kind == "wheel" || kind == "pointer")
                    page.Scroller.Raise(kind == "wheel" ? UIElement.PointerWheelChangedEvent : UIElement.PointerPressedEvent,
                        new Microsoft.UI.Xaml.Input.PointerRoutedEventArgs());
                else if (kind == "key")
                    page.Scroller.Raise(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyRoutedEventArgs
                    { Key = Windows.System.VirtualKey.Down });
                else
                {
                    page.Scroller.State = Microsoft.UI.Xaml.Controls.ScrollingInteractionState.Interaction;
                    page.ScrollHeader(120);
                }
                Check(!page.RestoringHeader, kind + " immediately takes over");
                Equal(0, page.Scroller.HandlerCount, kind + " detaches input handlers");
                page.TickHeaderRestore();
                page.CompleteHeaderRestore(1);
                Equal(1, page.Scroller.Requests.Count, kind + " receives no late restoration");
            }
        });
        Case("page/carousel-mode-restores-full-bleed-and-store-insets-without-replacing-slides", () =>
        {
            var page = new HomePage();
            var slides = page.ViewModel.Slides;
            page.Ui.EnableImmersive = false;
            page.ApplyCarousel();
            Equal(new Thickness(36, 20, 20, 0), page.BannerZone.Margin, "store insets");
            Equal(36d, page.Overlay.Padding.Left, "store shelf left inset");
            Equal(20d, page.Overlay.Padding.Right, "store shelf right inset");
            page.ActualWidth = 2560;
            page.ApplyCarousel();
            Equal(new Thickness(350, 20, 350, 0), page.BannerZone.Margin, "store caps the carousel at 1860");
            Equal(new Thickness(350, 0, 350, 0), page.Overlay.Padding, "store caps the shelves at 1860");
            page.ActualWidth = 1422;
            page.ApplyCarousel();
            Equal(new Thickness(36, 20, 20, 0), page.BannerZone.Margin, "narrow store keeps the spine");
            Equal(new CornerRadius(16), page.BannerCard.CornerRadius, "store corners");
            Equal(0d, page.Scroller.Margin.Top, "store leaves caption room");
            Equal(24d, page.LibraryOverlay.Margin.Left, "store overlay inset");
            Equal(TitleStrip.Plain, page.CurrentTitle, "store caption ink");
            page.Ui.EnableImmersive = true;
            page.NotifyAppearance(page.Ui);
            Equal(new Thickness(0), page.BannerZone.Margin, "immersive spans width");
            Equal(36d, page.Overlay.Padding.Left, "immersive shelves keep their left inset");
            Equal(20d, page.Overlay.Padding.Right, "immersive shelves keep their right inset");
            Equal(new CornerRadius(0), page.BannerCard.CornerRadius, "immersive square corners");
            Equal(-48d, page.Scroller.Margin.Top, "immersive extends through caption");
            Equal(36d, page.LibraryOverlay.Margin.Left, "immersive overlay follows shelf spine");
            Equal(TitleStrip.OnScrim, page.CurrentTitle, "immersive caption ink");
            page.Banner.Visibility = Visibility.Collapsed;
            page.ApplyCarousel();
            Equal(0d, page.Scroller.Margin.Top, "empty carousel restores page start");
            Equal(TitleStrip.Plain, page.CurrentTitle, "empty carousel restores plain ink");
            page.Banner.Visibility = Visibility.Visible;
            page.ApplyCarousel();
            Equal(-48d, page.Scroller.Margin.Top, "carousel return restores full bleed");
            page.Ui.EnableImmersive = false;
            page.NotifyAppearance(new Momoka.Configuration.UiSettings());
            Equal(-48d, page.Scroller.Margin.Top, "foreign settings cannot reflow page");
            page.NotifyAppearance(page.Ui);
            Equal(0d, page.Scroller.Margin.Top, "same page returns to store");
            Check(ReferenceEquals(slides, page.ViewModel.Slides), "mode does not replace carousel data");
            page.Release();
            page.NotifyAppearance(new() { EnableImmersive = true });
            Equal(0d, page.Scroller.Margin.Top, "released page rejects late appearance");
            page.BannerZone.Margin = new Thickness(9);
            page.ApplyCarousel();
            Equal(new Thickness(9), page.BannerZone.Margin, "released page ignores late banner layout callbacks");
        });
        Case("page/auto-return-entrance-does-not-move-library-onto-carousel", () =>
        {
            var scene = new Scene();
            scene.Page.ViewModel.LibraryPlacement = HomeLibraryPlacement.Auto;
            var stable = scene.Page.ReadCoversLine()!.Value.Line;
            scene.Page.Scroller.ActualHeight = stable + 10;
            var template = new FrameworkElement { RenderTransform = new TranslateTransform() };
            scene.LibraryRow.TestChildren.Add(template);
            foreach (var child in scene.LibraryRow.TestChildren.Where(child => !ReferenceEquals(child, template)))
                child.TestParent = template;
            template.TestParent = scene.LibraryRow;

            foreach (var shift in new[] { 20d, 10d, 5d, 0d })
            {
                ((TranslateTransform)template.RenderTransform!).Y = shift;
                Equal(stable, scene.Page.ReadCoversLine()!.Value.Line, "entrance is rendering, not a different layout threshold");
                scene.Page.UpdateFold();
                Check(!scene.Page.ViewModel.LibraryOnBanner, "return must keep the library below the carousel");
                scene.AssertFlowOnly();
                Equal(0, Storyboard.Created.Count, "return cannot invent an up/down fold trip");
            }
        });
        Case("page/auto-threshold-ignores-row-and-ancestor-motion", () =>
        {
            var scene = new Scene();
            var stable = scene.Page.ReadCoversLine()!.Value.Line;
            var ancestor = new FrameworkElement { RenderTransform = new TranslateTransform { Y = -80 } };
            scene.LibraryRow.TestParent = ancestor;
            ((TranslateTransform)scene.LibraryRow.RenderTransform!).Y = 240;
            Equal(stable, scene.Page.ReadCoversLine()!.Value.Line, "row and parent motion leave the threshold unchanged");
        });
        Case("page/library-viewport-reserves-and-restores-carousel-status-room", () =>
        {
            var scene = new Scene(initialOn: true);
            scene.Page.LibraryOverlay.Margin = new Thickness(36, 0, 36, 4);
            scene.Page.Banner.LibraryOverlayBottomInset = 4;
            scene.Page.Banner.LibraryOverlayRightInset = 244;
            scene.Page.Apply();
            Equal(new Thickness(36, 0, 244, 4), scene.Page.LibraryOverlay.Margin,
                "status room only changes the overlay's right margin");
            scene.Page.Apply();
            scene.AssertOverlayOnly();
            Check(scene.Page.MainMotion is null, "same placement does not start another fold");
            scene.Page.Banner.LibraryOverlayRightInset = 36;
            scene.Page.Apply();
            Equal(new Thickness(36, 0, 36, 4), scene.Page.LibraryOverlay.Margin,
                "hidden status restores the original viewport width");
        });
        Case("page/store-overlay-stays-on-main-card-across-wide-stacked-and-full-layouts", () =>
        {
            var scene = new Scene(initialOn: true);
            scene.Page.Ui.EnableImmersive = false;
            scene.Page.LibraryOverlay.Margin = new Thickness(36, 7, 36, 0);
            scene.Page.Banner.RailShown = true;
            scene.Page.Banner.MainColumnWidth = 670;
            scene.Page.Banner.LibraryOverlayBottomInset = 28;
            scene.Page.ApplyCarousel();
            Equal(new Thickness(24, 7, 36, 28), scene.Page.LibraryOverlay.Margin,
                "wide store leaves pagination below the main-card overlay");
            Equal(634d, scene.Page.LibraryOverlay.MaxWidth, "wide overlay stays inside the main column");

            scene.Page.Banner.RailShown = false;
            scene.Page.Banner.MainColumnWidth = 848;
            scene.Page.Banner.LibraryOverlayBottomInset = 357;
            scene.Page.ApplyCarousel();
            Equal(new Thickness(24, 7, 36, 357), scene.Page.LibraryOverlay.Margin,
                "stacked recommendations and pagination remain below the overlay");
            Equal(660d, scene.Page.LibraryOverlay.MaxWidth, "stacked layout restores the normal library viewport");
            scene.AssertOverlayOnly();
            Check(scene.Page.MainMotion is null, "moving recommendations does not invent a fold trip");

            scene.Page.Banner.RailShown = true;
            scene.Page.Banner.MainColumnWidth = 670;
            scene.Page.Banner.LibraryOverlayBottomInset = 28;
            scene.Page.ApplyCarousel();
            Equal(28d, scene.Page.LibraryOverlay.Margin.Bottom, "widening removes the old recommendation-row offset");
            Equal(634d, scene.Page.LibraryOverlay.MaxWidth, "widening limits the overlay to the main card again");

            scene.Page.Banner.RailShown = false;
            scene.Page.Banner.LibraryOverlayBottomInset = 0;
            scene.Page.Ui.EnableImmersive = true;
            scene.Page.ApplyCarousel();
            Equal(new Thickness(36, 7, 36, 0), scene.Page.LibraryOverlay.Margin,
                "full layout clears store offsets and retains the independent top inset");
            Equal(660d, scene.Page.LibraryOverlay.MaxWidth, "full layout restores the normal viewport");
            scene.AssertOverlayOnly();
        });
        Case("page/store-overlay-tail-change-updates-layout-once-without-rewriting-banner", () =>
        {
            var scene = new Scene(initialOn: true);
            var layouts = scene.Page.ForcedLayouts;
            var overlayWrites = scene.Page.Banner.SetLibraryOverlayCalls;
            scene.Page.Banner.LibraryOverlayBottomInset = 340;
            scene.Page.Apply();
            Equal(340d, scene.Page.LibraryOverlay.Margin.Bottom, "new recommendation height changes the bottom anchor");
            Equal(layouts + 1, scene.Page.ForcedLayouts, "changed anchor requires one layout");
            Equal(overlayWrites, scene.Page.Banner.SetLibraryOverlayCalls, "anchor change does not rewrite the unchanged banner overlay");
            scene.Page.Apply();
            Equal(layouts + 1, scene.Page.ForcedLayouts, "same anchor does not force another layout");
            scene.Page.Banner.LibraryOverlayBottomInset = 0;
            scene.Page.Apply();
            Equal(0d, scene.Page.LibraryOverlay.Margin.Bottom, "collapsing recommendations clears the stale anchor");
            Equal(layouts + 2, scene.Page.ForcedLayouts, "clearing the old anchor requires one layout");
            scene.AssertOverlayOnly();
        });
        Case("page/auto-single-following-row-keeps-scroll-and-live-transform", () =>
        {
            var scene = new Scene();
            scene.Page.HomeRows.RemoveAt(2);
            var stable = scene.Page.ReadCoversLine()!.Value.Line;
            scene.LibraryRow.TestTop -= 120;
            scene.Page.Scroller.VerticalOffset = 120;
            var drift = (TranslateTransform)scene.LibraryRow.RenderTransform!;
            drift.Y = -90;
            Equal(stable, scene.Page.ReadCoversLine()!.Value.Line, "scroll restoration and animation preserve the single-row threshold");
            Equal(-90d, drift.Y, "measurement cannot stop or reset the running visual animation");
        });
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
            scene.Page.ResumeVisit();
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
            scene.Page.UseHomeLibraryCoverPreferences();
            Equal(1, ShellPrefs.HomeLibraryCoverSubscribers, "cover preference notice belongs to this visit");
            scene.Page.Release();
            Check(scene.Page.MainMotion is null && scene.Page.LateMotion is null, "release clears current owners");
            Equal(0, Board(motion).HandlerCount, "release detaches main Completed");
            Equal(0, Board(late).HandlerCount, "release detaches late Completed");
            Equal(0, ShellPrefs.HomeLibraryCoverSubscribers, "release detaches the cover preference notice");
            Equal(1, scene.Page.ViewModel.CancelCalls, "actual Release calls VM boundary Cancel once");
        });
        Case("page/release-detaches-local-library-event-and-owner", () =>
        {
            var scene = new Scene();
            var source = new TestLocalLibraries();
            scene.Page.UseLocalLibraries(source);
            Equal(1, source.Subscribers, "event-only source belongs to this visit");
            scene.Page.Release();
            Equal(0, source.Subscribers, "actual Release must detach local library notification");
            Check(!scene.Page.HasLocalLibraries, "actual Release clears the optional source");
            source.Notify();
            scene.Page.Release();
            Equal(0, source.Subscribers, "repeat Release remains detached");
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
        Case("page/schedule-overlay-coalesces-size-and-slide-wakeups", () =>
        {
            var scene = new Scene();
            scene.Page.QueueOverlay();
            scene.Page.QueueOverlay();
            scene.Page.EndPlayerLayer();
            Equal(1, scene.Page.DispatcherQueue!.PendingCount, "size/slide/fold wakeups coalesce");
            scene.Drain();
            Equal(0, scene.Page.DispatcherQueue.PendingCount, "drained overlay does not leave a wakeup");
        });
        Case("page/unchanged-chrome-skips-writes-and-forced-layout", () =>
        {
            var scene = new Scene();
            var layouts = scene.Page.ForcedLayouts;
            var overlayWrites = scene.Page.Banner.SetLibraryOverlayCalls;
            scene.Page.Apply();
            Equal(layouts, scene.Page.ForcedLayouts, "unchanged chrome must not force layout");
            Equal(overlayWrites, scene.Page.Banner.SetLibraryOverlayCalls, "unchanged chrome must not rewrite banner overlay");
            Equal(1, scene.Page.SkippedChromeWrites, "unchanged chrome records a skip");
        });
        Case("page/fold-still-writes-host-and-banner-once", () =>
        {
            var scene = new Scene();
            var layouts = scene.Page.ForcedLayouts;
            var overlayWrites = scene.Page.Banner.SetLibraryOverlayCalls;
            scene.Page.ViewModel.LibraryOnBanner = true;
            scene.Page.Apply();
            Equal(Visibility.Visible, scene.Page.LibraryOverlay.Visibility, "overlay carrier is visible");
            Check(ReferenceEquals(scene.Library, scene.Page.LibraryHost.Content), "host carries the library");
            Equal(overlayWrites + 1, scene.Page.Banner.SetLibraryOverlayCalls, "banner overlay written once");
            Check(scene.Page.ForcedLayouts > layouts, "content change forces layout");
        });
        Case("page/overlay-reentry-schedules-one-follow-up", () =>
        {
            var scene = new Scene();
            var reentries = 0;
            scene.Page.TestLayout = () =>
            {
                if (reentries++ == 0) scene.Page.QueueOverlay();
            };
            try
            {
                scene.Page.UpdateFold();
                Equal(1, scene.Page.OverlayPasses, "direct pass counted once");
                Equal(1, scene.Page.DispatcherQueue!.PendingCount, "reentry queues exactly one follow-up");
                scene.Drain();
                Equal(2, scene.Page.OverlayPasses, "follow-up pass counted");
                Equal(0, scene.Page.DispatcherQueue.PendingCount, "stable follow-up does not feed itself");
            }
            finally
            {
                scene.Page.TestLayout = null;
            }
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
        internal FrameworkElement LibraryRow { get; } = Row(520);
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
