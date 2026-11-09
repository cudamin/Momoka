using System.Reflection;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class LibraryAnchorTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("empty-after-window-change-clears-page-owner", EmptyWindow),
            ("empty-recovery-does-not-cancel-new-anchor-owner", EmptyThenNewOwner),
            ("query-replacement-clears-only-old-owner", QueryReplacement),
            ("new-request-zero-offset-does-not-reuse-old-scroll", ZeroOffset),
            ("layout-unload-invalidates-queued-scroll-correction", LayoutUnload),
            ("restore-keeps-requested-anchor-until-scrolling-settles", KeepRequestedAnchor)
        };
        var failures = 0;
        foreach (var (name, run) in cases)
        {
            var fixture = new CardImageFixture(artwork: false);
            try { _images = fixture.Images; _scope = fixture.Scope; run(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
            finally { CardImageContext.Run(() => fixture.DisposeAsync().AsTask()); }
        }
        Console.WriteLine($"TOTAL {cases.Length}; PASS {cases.Length - failures}; FAIL {failures}; SKIP 0");
        return failures == 0 ? 0 : 1;
    }

    private static Momoka.Emby.EmbyImageStore? _images;
    private static Momoka.Emby.EmbySessionScope? _scope;

    private static void EmptyWindow()
    {
        var page = Page("old");
        Call(page, "BeforeWindowChange");
        Check(Get<bool>(page, "_restoringAnchor"), "WindowChanging must own a pending restore");
        page.ViewModel.Cards.Clear();
        page.CardWall.Children.Clear();
        page.ViewModel.Request.AnchorIdentityKey = null;
        Call(page, "AfterWindowChange");
        Check(!Get<bool>(page, "_restoringAnchor"), "empty window must release page restore flag");
        Check(Get<object?>(page, "_pendingAnchor") is null, "empty window must release page pending anchor");
        Check(Get<int>(page, "_anchorAttempts") == 0, "empty window must reset attempts");

        Add(page, "new");
        Check(!Get<bool>(page, "_restoringAnchor") && Get<object?>(page, "_pendingAnchor") is null,
            "later rows must remain eligible for ordinary paging");
        Call(page, "BeforeWindowChange");
        Call(page, "AfterWindowChange");
        page.DispatcherQueue.Drain();
        Check(!Get<bool>(page, "_restoringAnchor") && page.CardWall.BringCalls > 0,
            "nonempty follow-up anchor should restore and release normally");
    }

    private static void EmptyThenNewOwner()
    {
        var page = Page("old");
        Call(page, "BeforeWindowChange");
        Call(page, "RestorePendingAnchor");
        var oldVersion = Get<int>(page, "_anchorVersion");
        Check(page.DispatcherQueue.Pending.Count > 0, "old completion must be queued");
        page.ViewModel.Cards.Clear();
        page.CardWall.Children.Clear();
        Call(page, "AfterWindowChange");
        Check(Get<int>(page, "_anchorVersion") > oldVersion, "empty release must invalidate queued old callbacks");
        Add(page, "new");
        Call(page, "BeforeWindowChange");
        Call(page, "RestorePendingAnchor");
        var newVersion = Get<int>(page, "_anchorVersion");
        page.DispatcherQueue.Pending.Dequeue()();
        Check(Get<bool>(page, "_restoringAnchor") && Get<int>(page, "_anchorVersion") == newVersion,
            "queued old completion must not release the newer nonempty owner");
        page.DispatcherQueue.Drain();
        Check(!Get<bool>(page, "_restoringAnchor"), "new owner must complete");
    }

    private static void QueryReplacement()
    {
        var page = Page("old");
        Call(page, "BeforeWindowChange");
        Call(page, "RestorePendingAnchor");
        page.ViewModel.ChangeQuery();
        page.DispatcherQueue.Drain();
        Check(!Get<bool>(page, "_restoringAnchor") && Get<object?>(page, "_pendingAnchor") is null,
            "old query completion must release only its own state");
    }

    private static void ZeroOffset()
    {
        var page = Page("old");
        var scroll = Get<Microsoft.UI.Xaml.Controls.ScrollView>(page, "_scroll");
        scroll.VerticalOffset = 800;
        typeof(LibraryPage).GetField("_pendingScroll", Members)!.SetValue(page, 0d);
        Call(page, "ApplyPendingScroll");
        Check(scroll.VerticalOffset == 0, "new zero-offset request must return an already-used scroller to its top");
        Check(Get<double?>(page, "_pendingScroll") is null, "the zero-offset request must be consumed");
    }

    private static void LayoutUnload()
    {
        var page = Page("old");
        Call(page, "BeforeWindowChange");
        Call(page, "RestorePendingAnchor");
        var scroll = Get<Microsoft.UI.Xaml.Controls.ScrollView>(page, "_scroll");
        scroll.VerticalOffset = 100;
        page.CardWall.LayoutAction = () => typeof(LibraryPage).GetField("_scroll", Members)!.SetValue(page, null);
        page.DispatcherQueue.Drain();
        Check(scroll.VerticalOffset == 100, "layout-unloaded scroller must not receive queued correction");
        Check(Get<object?>(page, "_pendingAnchor") is not null, "new Loaded must retain the requested anchor");
    }

    private static void KeepRequestedAnchor()
    {
        var page = Page("old");
        Call(page, "BeforeWindowChange");
        var y = page.ViewModel.Request.AnchorY;
        Call(page, "RestorePendingAnchor");
        ((PosterCard)page.CardWall.Children[0]).Y = 0;
        page.DispatcherQueue.Drain();
        Check(page.ViewModel.Request.AnchorY == y,
            "a dispatcher callback is not scroll completion and must not overwrite the saved anchor with an intermediate position");
    }

    private static LibraryPage Page(string identity)
    {
        var page = new LibraryPage();
        Add(page, identity);
        return page;
    }
    private static void Add(LibraryPage page, string identity)
    {
        var card = new CardItem(_scope!.Bind(new Momoka.Emby.EmbyItem { Id = identity, Name = identity, Type = Momoka.Emby.EmbyItemType.Movie }), _images!, 170);
        page.ViewModel.Cards.Add(card);
        page.CardWall.Children.Add(new PosterCard { Card = card, Y = -17 });
    }
    private static object? Call(LibraryPage page, string method) => typeof(LibraryPage).GetMethod(method, Members)!.Invoke(page, null);
    private static T Get<T>(LibraryPage page, string name) => (T)typeof(LibraryPage).GetField(name, Members)!.GetValue(page)!;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
