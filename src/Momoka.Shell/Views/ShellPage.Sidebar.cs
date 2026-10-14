using Momoka.Configuration;
using Momoka.Emby;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

        // 导航开关（≡）只在这条栏出现时露脸：没有栏就没有可开可关的东西，而它站在标题栏那一排的最前面
        // （见 ApplySidebarLayout 的缩进），所以退/进两颗因此跟在它右边。
        NavToggleButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        HomeButton.Visibility = SettingsButton.Visibility = SearchButton.Visibility =
            shown ? Visibility.Collapsed : Visibility.Visible;
        FavoritesButton.Visibility = !shown && Favorites.HasItems ? Visibility.Visible : Visibility.Collapsed;
        SidebarFavorites.Visibility = Favorites.Visibility;
        SidebarSettingsButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        // MoviePilot 的两条订阅入口跟着服务可用性走：开关已开且本进程真的连得上才算可用。收起只收入口，
        // 不删订阅也不解除防重状态；条件恢复后它们照原样回来（同 momoka-moviepilot 的入口显隐）。
        // 那条「订阅」分区标题跟两条入口同进同退 —— 段里一条都不剩时留着一条光标题就是空带子。
        //
        // 它和「媒体库」那条标题用的是同一套机制（翻 Visibility），所以也带着同一个框架脾气：分区标题的
        // 内容可见性是 NavigationView 按面板模式在**实现那一刻**算的，标题在栏收着（48 宽）的时候翻成
        // Visible，之后把栏展开也不回头补 —— 标题占着位置、一个字不画（2026-10-13 晚实测）。应用里那条
        // 路是「窗宽那一拍先把栏开起来、MoviePilot 的可用性随后才到」，所以不踩；**窄窗启动（栏收着）
        // 再拉宽**那一档会踩，而「媒体库」那条标题自始就是这个脾气（它的内容一样是框架按模式收放的）。
        // 这一档记在 PROGRESS 里，没有为它单独加一层「展开时把标题重新实现一遍」的机制。
        var moviePilot = _moviePilotService?.Available == true;
        SidebarMoviePilotMovies.Visibility = moviePilot ? Visibility.Visible : Visibility.Collapsed;
        SidebarMoviePilotSeries.Visibility = moviePilot ? Visibility.Visible : Visibility.Collapsed;
        SidebarSubscriptionsHeader.Visibility = moviePilot ? Visibility.Visible : Visibility.Collapsed;

        // 账号那一组现在是 Root 的孩子（它要能落在左下角，也要能回标题栏右上角），播放时 Chrome 收着而它
        // 不会跟着收 —— 所以这里按外壳在不在屏上把它一起收掉。
        AccountCluster.Visibility = Chrome.Visibility == Visibility.Visible && AccountButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;

        // A detail page has no destination tag; its first breadcrumb still identifies its library/search.
        var tag = _current ?? _trail.FirstOrDefault()?.Tag;
        var selected = tag switch
        {
            "home" => SidebarHome,
            LibraryRequest.SearchTag => SidebarSearch,
            LibraryRequest.FavoritesTag when Favorites.HasItems => SidebarFavorites,
            HomeLayout.MoviePilotMovies when moviePilot => SidebarMoviePilotMovies,
            HomeLayout.MoviePilotSeries when moviePilot => SidebarMoviePilotSeries,
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
        var shown = SidebarShown;
        var width = shown ? Sidebar.IsPaneOpen ? Sidebar.OpenPaneLength : Sidebar.CompactPaneLength : 0;
        SidebarHost.Width = width;
        ContentHost.Margin = new Thickness(width, 0, 0, 0);

        // 标题栏那一排的缩进：侧边栏在屏上时是 12，其余时候是 18（标记里那个值）。12 是「导航开关的字形
        // 和栏里那几项对齐」算出来的 —— 开关靠左摆、字形宽 24，所以它的中线 = 缩进 + 12；栏里那几项的图标
        // 中线实测展开时 24（用户 2026-10-13：「关闭导航没和下面对齐」），收成 48 宽的图标栏时框架模板会把它
        // 再往右挪 2 像素（26）—— 一个数没法两档都精确，取展开那一档，另一档差 2 像素（探针按 3 像素判）。
        // 12 也把窗口自己那 8 像素缩放边框让开了。
        var titleMargin = new Thickness(shown ? 12 : 18, 0, 0, 0);
        if (TitleActions.Margin != titleMargin)
        {
            // Moving an unchanged row does not raise SizeChanged. Measure after layout,
            // otherwise the native title bar keeps the old clickable rectangle.
            _sidebarTitleLayoutPending = true;
            TitleActions.Margin = titleMargin;
        }

        ToolTipService.SetToolTip(NavToggleButton, Sidebar.IsPaneOpen ? "关闭导航" : "打开导航");
        ApplyAccountCluster(shown, width);
    }

    /// <summary>
    /// 账号那一组（账号 + 设置）此刻站在哪儿。侧边栏在屏上时它整组落在窗口左下角、宽度就是那条栏的宽：
    /// 展开时账号占满剩下的宽、设置并排在它右边；收成图标栏时两块上下叠、设置在账号下面。不在侧边栏模式里
    /// 时它回到标题栏右上角，设置那一颗收起（那一档设置是标题栏里那颗齿轮），账号只留文字那一格。
    /// <para>
    /// 纯摆位，留在 code-behind：它读的全是「这一栏现在开着没有、多宽」，没有业务判断。
    /// </para>
    /// </summary>
    private void ApplyAccountCluster(bool shown, double width)
    {
        var expanded = Sidebar.IsPaneOpen;
        if (shown)
        {
            AccountCluster.HorizontalAlignment = HorizontalAlignment.Left;
            AccountCluster.VerticalAlignment = VerticalAlignment.Bottom;
            AccountCluster.Margin = new Thickness(0);
            // 收成 48 宽的图标栏时左右只留 4：按钮因此是 40 宽，字形中线落在 24 上 —— 和栏里那几项图标同一条
            // 竖线（栏里那几项的高亮也是往里收 4）。留 6 的话按钮只剩 32 宽、去掉按钮自己那 10 的横向内边距后
            // 内容格只有 12 宽，装不下 16 宽的图标字形，右边会被裁掉 3 像素
            //（用户 2026-10-13：「收起侧边栏时候会被遮挡」）。
            AccountCluster.Padding = expanded ? new Thickness(6, 6, 6, 8) : new Thickness(4, 6, 4, 8);
            AccountCluster.ColumnSpacing = expanded ? 4 : 0;
            AccountCluster.RowSpacing = 4;
            AccountCluster.Width = width;

            // 展开时按钮自己留 10 的横向内边距（名字和服务器名要地方）；图标栏那一档按钮里就是那一颗字形，
            // 横向不留 —— 留下的横向内边距就是裁字形的刀。竖向：展开那一档要装两行字，取 5（两行 + 10 = 43）；
            // 图标栏那一档只有一颗字形，取 6 —— 那一档账号与设置一样高（28），而设置从前是 3、比账号矮 6 像素。
            AccountButton.Padding = expanded ? new Thickness(10, 5, 10, 5) : new Thickness(0, 6, 0, 6);
            AccountButton.MaxWidth = double.PositiveInfinity;
            AccountButton.HorizontalContentAlignment = expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            // 图标栏只有 48 宽：名字和服务器名都放不下，那一档留一颗人像字形。
            AccountGlyph.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            AccountText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            // 展开那一档两行：用户名在上、服务器名在下。图标栏那一档两格字都收着，排法无所谓，仍按一行落。
            ApplyAccountText(expanded);
            // 它贴着窗口下沿，菜单朝上落，不然那张菜单会落到屏幕外面去。
            AccountMenu.Placement = FlyoutPlacementMode.TopEdgeAlignedLeft;

            SidebarSettingsButton.Padding = expanded ? new Thickness(10, 3, 10, 3) : new Thickness(0, 6, 0, 6);
            SidebarSettingsButton.HorizontalContentAlignment = expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            SidebarSettingsText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetRow(SidebarSettingsButton, expanded ? 0 : 1);
            Grid.SetColumn(SidebarSettingsButton, expanded ? 1 : 0);

            AccountCluster.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            AccountCluster.ColumnDefinitions[1].Width = expanded ? GridLength.Auto : new GridLength(0);
            AccountCluster.RowDefinitions[1].Height = expanded ? new GridLength(0) : GridLength.Auto;
        }
        else
        {
            AccountCluster.HorizontalAlignment = HorizontalAlignment.Right;
            AccountCluster.VerticalAlignment = VerticalAlignment.Top;
            AccountCluster.Margin = new Thickness(0, 0, 160, 0);
            AccountCluster.Padding = new Thickness(0);
            AccountCluster.ColumnSpacing = 0;
            AccountCluster.RowSpacing = 0;
            AccountCluster.Width = double.NaN;

            AccountButton.Padding = new Thickness(10, 3, 10, 3);
            AccountButton.MaxWidth = 240;
            AccountButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            AccountGlyph.Visibility = Visibility.Collapsed;
            AccountText.Visibility = Visibility.Visible;
            // 标题栏那一档只有 32 像素高：13 号那行自己就 17，再加 12 号那行是 35 —— 装不下，仍旧一行。
            ApplyAccountText(false);
            AccountMenu.Placement = FlyoutPlacementMode.BottomEdgeAlignedRight;

            SidebarSettingsButton.Padding = new Thickness(10, 3, 10, 3);
            SidebarSettingsButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            Grid.SetRow(SidebarSettingsButton, 0);
            Grid.SetColumn(SidebarSettingsButton, 1);
            AccountCluster.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            AccountCluster.ColumnDefinitions[1].Width = GridLength.Auto;
            AccountCluster.RowDefinitions[1].Height = new GridLength(0);
        }

        // 挪位不改尺寸，SizeChanged 不会再发一次：这一拍先报一遍（对齐刚落下去，位置可能还是旧的），
        // 再挂一拍 —— 新的落位排完时 LayoutUpdated 会按新位置重报一次，窗口那个洞才跟着走。
        _sidebarTitleLayoutPending = true;
        ReportTitleBarHoles();
    }

    /// <summary>
    /// 账号那两格文字怎么排（2026-10-13 用户：「改成用户名在上服务器名在下」）。
    /// <para>
    /// <c>stacked</c>：用户名在上、服务器名在下，两行左对齐、中间那颗「·」收起、朝下的箭头跨两行竖着居中。
    /// 只有侧边栏展开那一档走这条 —— 那条栏 220 宽，名字和服务器各占一行才不会互相挤掉。格位这样落：
    /// 第 0 列换成 <c>*</c>（两格字都跨 0–2 列，因此都拿得到整行剩下的宽、长了截省略号而不是被按钮裁掉），
    /// 第 2 列退回 <c>Auto</c>（服务器名搬到第 1 行，那一列空了）。
    /// </para>
    /// <para>
    /// 一行那一档（标题栏右上角、图标栏）：名字、<c>·</c>、服务器名并排，第 2 列是 <c>*</c>、由服务器名截字 ——
    /// 标题栏那 32 像素装不下两行（13 号那行自己就 17）。列间距那一档要 8，两行那一档归 0：跨列的两格字
    /// 会把跨过的那两道间距也算进去，白占 16 像素，改由箭头自己留 8。
    /// </para>
    /// </summary>
    private void ApplyAccountText(bool stacked)
    {
        AccountDot.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
        AccountText.ColumnSpacing = stacked ? 0 : 8;
        AccountText.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        AccountText.ColumnDefinitions[2].Width = stacked ? GridLength.Auto : new GridLength(1, GridUnitType.Star);

        Grid.SetColumnSpan(AccountUser, stacked ? 3 : 1);
        Grid.SetRow(AccountServer, stacked ? 1 : 0);
        Grid.SetColumn(AccountServer, stacked ? 0 : 2);
        Grid.SetColumnSpan(AccountServer, stacked ? 3 : 1);
        Grid.SetRowSpan(AccountChevron, stacked ? 2 : 1);
        AccountChevron.Margin = stacked ? new Thickness(8, 0, 0, 0) : new Thickness(0);
    }

    /// <summary>导航开关：侧边栏的开与关。框架那颗自带开关收掉之后（<c>IsPaneToggleButtonVisible=False</c>），
    /// 开合只剩这一处入口 —— 它也把菜单项从栏顶那一格解放出来，让它们往上顶到标题栏下沿。</summary>
    private void OnNavToggleClicked(object sender, RoutedEventArgs e)
    {
        if (!SidebarShown) return;
        Sidebar.IsPaneOpen = !Sidebar.IsPaneOpen;
    }

    /// <summary>左下角那颗设置。它开的是设置窗口，不改变这一页的选中目的地（同原来那条 FooterMenuItems 的
    /// <c>SelectsOnInvoked=False</c>）。</summary>
    private void OnSidebarSettingsClicked(object sender, RoutedEventArgs e) => ShowSettings();

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

    /// <summary>
    /// 把当前账号的媒体库那几项填进栏里。**插在「媒体库」那条分区标题之后**，不是往栏尾加 ——
    /// 栏尾自 2026-10-13 起站着「订阅」那一段（两条 MoviePilot 入口），往尾上添会把媒体库塞进订阅段里，
    /// 而用户要的是两段各归各的（见 ShellPage.xaml 里那两条标题上方的注释）。声明顺序本来就是
    /// 「媒体库标题 → 订阅标题 → 两条订阅入口」，所以插在标题后一位、按库的次序往后排即可。
    /// </summary>
    private void RebuildSidebarLibraries()
    {
        foreach (var item in _sidebarLibraries.Values) Sidebar.MenuItems.Remove(item);
        _sidebarLibraries.Clear();
        var at = Sidebar.MenuItems.IndexOf(SidebarLibrariesHeader) + 1;
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
            Sidebar.MenuItems.Insert(at++, item);
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
