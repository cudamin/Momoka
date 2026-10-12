using Momoka.Configuration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private const double SidebarWideThreshold = 1008;
    private readonly Dictionary<string, NavigationViewItem> _sidebarLibraries = new(StringComparer.Ordinal);
    private CompositeTransform? _sidebarPaneClip;
    private bool _sidebarMode;
    private bool _sidebarWide;
    private bool _sidebarPlaying;
    private bool _sidebarTitleLayoutPending;
    private bool _syncingSidebar;

    // Loaded/Unloaded also cover the temporary shells used by offline verification.
    // Reattaching reads the setting again, so an event missed while unloaded is harmless.
    private void InitializeSidebar()
    {
        Sidebar.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => ApplySidebarLayout());
        SizeChanged += (_, _) => UpdateSidebarWidth();
        TitleActions.LayoutUpdated += (_, _) =>
        {
            if (!_sidebarTitleLayoutPending) return;
            _sidebarTitleLayoutPending = false;
            ReportTitleBarHoles();
        };
        Favorites.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Favorites.HasItems)) SyncSidebar();
        };
        Loaded += (_, _) =>
        {
            ShellPrefs.SidebarChanged -= OnSidebarPreferencesChanged;
            ShellPrefs.SidebarChanged += OnSidebarPreferencesChanged;
            if (_settings is not null) OnSidebarPreferencesChanged(Settings.Ui);
            UpdateSidebarWidth();
        };
        Unloaded += (_, _) => ShellPrefs.SidebarChanged -= OnSidebarPreferencesChanged;
    }

    private void OnSidebarPreferencesChanged(UiSettings ui)
    {
        // All open windows receive ShellPrefs; unrelated preferences must not reflow this shell.
        if (_settings is null || !ReferenceEquals(ui, Settings.Ui) || _sidebarMode == ui.SidebarMode) return;
        _sidebarMode = ui.SidebarMode;
        _sidebarWide = ActualWidth >= SidebarWideThreshold;
        if (_sidebarMode) Sidebar.IsPaneOpen = _sidebarWide;
        SyncSidebar();
    }

    private void UpdateSidebarWidth()
    {
        var wide = ActualWidth >= SidebarWideThreshold;
        if (_sidebarWide != wide)
        {
            _sidebarWide = wide;
            Sidebar.IsPaneOpen = wide;
        }
        ApplySidebarLayout();
    }

    private bool SidebarShown => _sidebarMode && !_sidebarPlaying && _session?.IsSignedIn == true
        && ContentHost.Visibility == Visibility.Visible;

    private void SyncSidebar()
    {
        var shown = SidebarShown;
        SidebarHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        HomeButton.Visibility = SettingsButton.Visibility = SearchButton.Visibility =
            shown ? Visibility.Collapsed : Visibility.Visible;
        FavoritesButton.Visibility = !shown && Favorites.HasItems ? Visibility.Visible : Visibility.Collapsed;
        SidebarFavorites.Visibility = Favorites.Visibility;

        // A detail page has no destination tag; its first breadcrumb still identifies its library/search.
        var tag = _current ?? _trail.FirstOrDefault()?.Tag;
        var selected = tag switch
        {
            "home" => SidebarHome,
            LibraryRequest.SearchTag => SidebarSearch,
            LibraryRequest.FavoritesTag when Favorites.HasItems => SidebarFavorites,
            _ => tag is not null && _sidebarLibraries.TryGetValue(tag, out var item) ? item : null
        };
        _syncingSidebar = true;
        try { Sidebar.SelectedItem = selected; }
        finally { _syncingSidebar = false; }
        ApplySidebarLayout();
    }

    private void ApplySidebarLayout()
    {
        // This control only hosts the menu. Explicitly select compact presentation
        // when its host shrinks, and retain persistent navigation while expanded.
        var mode = Sidebar.IsPaneOpen ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact;
        if (Sidebar.PaneDisplayMode != mode) Sidebar.PaneDisplayMode = mode;
        AlignSidebarPaneClip();
        var width = SidebarShown ? Sidebar.IsPaneOpen ? Sidebar.OpenPaneLength : Sidebar.CompactPaneLength : 0;
        SidebarHost.Width = width;
        ContentHost.Margin = new Thickness(width, 0, 0, 0);
        // The account shares the 32-DIP title strip. In a 600-DIP window with the
        // pane manually expanded, reserve space for both arrows before trimming its label.
        AccountButton.MaxWidth = SidebarShown ? Math.Clamp(ActualWidth - width - 276, 32, 240) : 240;
        // Keep the remaining title actions over the page, including in compact mode.
        var titleMargin = new Thickness(width + 18, 0, 0, 0);
        if (TitleActions.Margin != titleMargin)
        {
            // Moving an unchanged row does not raise SizeChanged. Measure after layout,
            // otherwise the native title bar keeps the old clickable rectangle.
            _sidebarTitleLayoutPending = true;
            TitleActions.Margin = titleMargin;
        }
    }

    /// <summary>
    /// SplitView draws its compact pane as "full-width clip rectangle + offset": the clip stays
    /// <c>OpenPaneLength</c> wide and is shifted by (compact − expanded), so only the leftmost
    /// compact strip survives. When NavigationView collapses the pane it rewrites the pane's
    /// <c>OpenPaneLength</c> to the compact length (48), but the offset was already computed from the
    /// expanded width (48 − 220 = −172) and is not recomputed: the clip rectangle is 48 wide and yet
    /// slides 172 DIP left, which clips the entire icon rail off-screen and leaves only the ≡ button.
    /// Our rail is always exactly as wide as the pane, so the correct offset is always 0; put it back
    /// whenever the visual state writes one.
    /// </summary>
    private void AlignSidebarPaneClip()
    {
        if (_sidebarPaneClip is null)
        {
            _sidebarPaneClip = FindSidebarPaneClip();
            if (_sidebarPaneClip is null) return;
            _sidebarPaneClip.RegisterPropertyChangedCallback(
                CompositeTransform.TranslateXProperty, (_, _) => AlignSidebarPaneClip());
        }
        if (_sidebarPaneClip.TranslateX != 0) _sidebarPaneClip.TranslateX = 0;
    }

    /// <summary>The clipped pane host inside the NavigationView's SplitView; it is the only
    /// template element carrying a rectangular clip with a transform.</summary>
    private CompositeTransform? FindSidebarPaneClip()
    {
        foreach (var split in Descendants(Sidebar).OfType<SplitView>())
            foreach (var part in Descendants(split).OfType<FrameworkElement>())
                if (part.Clip is RectangleGeometry { Transform: CompositeTransform transform }) return transform;
        return null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private void RebuildSidebarLibraries()
    {
        foreach (var item in _sidebarLibraries.Values) Sidebar.MenuItems.Remove(item);
        _sidebarLibraries.Clear();
        foreach (var (tag, library) in _libraries)
        {
            var item = new NavigationViewItem
            {
                Content = library.Title,
                Tag = tag,
                Icon = new FontIcon { Glyph = library.CollectionType == "tvshows" ? "\uE714" : "\uE8B2" }
            };
            AutomationProperties.SetAutomationId(item, "SidebarLibrary_" + library.ParentId);
            AutomationProperties.SetName(item, library.Title);
            Sidebar.MenuItems.Add(item);
            _sidebarLibraries.Add(tag, item);
        }
        SidebarLibrariesHeader.Visibility = _sidebarLibraries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncSidebar();
    }

    private void OnSidebarItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (!SidebarShown || args.InvokedItemContainer is not NavigationViewItem { Tag: string tag }) return;
        // Invoking the selected home item must still retry a failed directory request;
        // settings opens a separate window without changing this frame's selected destination.
        GoTo(tag);
        if (tag == "home")
        {
            _ = Favorites.RefreshAsync();
            if (_librariesFailed) _ = RetryLibrariesAsync();
        }
        SyncSidebar();
    }

    private void OnSidebarSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSidebar || !SidebarShown || args.SelectedItem is not NavigationViewItem { Tag: string tag }
            || tag == "settings") return;
        GoTo(tag);
    }
}
