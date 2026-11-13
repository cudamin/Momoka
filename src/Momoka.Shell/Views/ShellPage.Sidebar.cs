using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private const double SidebarWideThreshold = 1008;
    private readonly Dictionary<string, NavigationViewItem> _sidebarLibraries = new(StringComparer.Ordinal);
    private readonly AnimatedSettingsVisualSource _sidebarSettingsIconSource = new();
    private readonly UISettings _sidebarSettings = new();
    private TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? _sidebarSettingsChanged;
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
        InitializeLibraryPreview();
        SyncSidebarSettingsIcon();
        Loaded += (_, _) =>
        {
            ShellPrefs.SidebarChanged -= OnSidebarPreferencesChanged;
            ShellPrefs.SidebarChanged += OnSidebarPreferencesChanged;
            if (_settings is not null) OnSidebarPreferencesChanged(Settings.Ui);
            HomeMotion.PreferenceChanged -= OnSidebarMotionPreferenceChanged;
            HomeMotion.PreferenceChanged += OnSidebarMotionPreferenceChanged;
            _sidebarSettingsChanged ??= OnSidebarSettingsChanged;
            try { _sidebarSettings.AnimationsEnabledChanged -= _sidebarSettingsChanged; }
            catch (Exception) { /* 先重挂一个本地失效的旧回调；退订失败不阻断后续同步。 */ }
            _sidebarSettings.AnimationsEnabledChanged += _sidebarSettingsChanged;
            SyncSidebarSettingsIcon();
            UpdateSidebarWidth();
        };
        Unloaded += (_, _) =>
        {
            ShellPrefs.SidebarChanged -= OnSidebarPreferencesChanged;
            HomeMotion.PreferenceChanged -= OnSidebarMotionPreferenceChanged;
            if (_sidebarSettingsChanged is not null)
            {
                try { _sidebarSettings.AnimationsEnabledChanged -= _sidebarSettingsChanged; }
                catch (Exception) { /* 只确认本地回调失效，不能声称 native 已退订。 */ }
            }
            HideLibraryPreview();
        };
    }

    /// <summary>
    /// 设置那颗齿轮的动画只在应用与系统都允许时跑。关掉时把 Source 置空，AnimatedIcon 落到
    /// Gallery 样例里那颗 FontIconSource 静态回退，不为动画留住一帧。
    /// </summary>
    private void SyncSidebarSettingsIcon()
    {
        var source = HomeMotion.AnimationsEnabled ? _sidebarSettingsIconSource : null;
        if (!ReferenceEquals(SidebarSettingsIcon.Source, source)) SidebarSettingsIcon.Source = source;
    }

    private void OnSidebarMotionPreferenceChanged() => SyncSidebarSettingsIcon();

    private void OnSidebarSettingsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess) SyncSidebarSettingsIcon();
        else DispatcherQueue.TryEnqueue(SyncSidebarSettingsIcon);
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
        // （见 ApplySidebarLayout 的缩进），所以收藏/后退/前进跟在它右边。
        NavToggleButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        // 标题「Momoka」2026-11-13 按用户令整块删掉（那一格让给返回/折叠）。
        // 主页与收藏也在这条栏出现时让给栏里的入口（2026-11-12 晚）：那一档左边只剩返回 · 折叠，
        // 栏里第一项就是「主页」、收藏那条也在栏里 —— 同一页不出两个同样的入口。
        HomeButton.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        SettingsButton.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        TitleUtilities.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        // 搜索框（标题栏正中）与那一排里的放大镜：门槛与顶班规则在 SyncTitleSearch 里。
        SyncTitleSearch();
        // 主页、返回与前进每一档都在；设置、收藏在侧边栏那一档让给栏里的入口（见 SyncTitleActions）。
        // 搜索 2026-11-13 起只在标题栏正中那颗框里，栏里不再有第二份入口。
        SyncTitleActions();
        SidebarFavorites.Visibility = Favorites.Visibility;

        // MoviePilot 的两条订阅入口跟着服务可用性走：开关已开且本进程真的连得上才算可用。收起只收入口，
        // 不删订阅也不解除防重状态；条件恢复后它们照原样回来（同 momoka-moviepilot 的入口显隐）。
        // 那条「订阅」分区标题跟两条入口同进同退 —— 段里一条都不剩时留着一条光标题就是空带子。
        //
        // 它和「媒体库」那条标题用的是同一套机制（翻 Visibility），所以也带着同一个框架脾气：分区标题的
        // 内容可见性是 NavigationView 按面板模式在**实现那一刻**算的，标题在栏收着（56 宽）的时候翻成
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
        if (Sidebar.PaneDisplayMode != mode)
        {
            Sidebar.PaneDisplayMode = mode;
            // 换档会换掉 SplitView 里那支裁剪几何：旧的那支不再在树上，缓存着它只会一直去修一支没人用的变换，
            // 而新那支带着 −172 的位移把整条栏推到栏外 —— 屏上就是「只有标题、没有栏」。下一次 AlignSidebarPaneClip
            // 重新找一遍。
            _sidebarPaneClip = null;
        }
        AlignSidebarPaneClip();
        var shown = SidebarShown;
        if (!shown || Sidebar.IsPaneOpen) HideLibraryPreview();
        var width = shown ? Sidebar.IsPaneOpen ? Sidebar.OpenPaneLength : Sidebar.CompactPaneLength : 0;
        SidebarHost.Width = width;
        // 顶上那 40 是标题栏：2026-11-12 晚起画布让开它（见 ShellPage.xaml 里 ContentHost 那一段），
        // 所以这一份外边距是「让开侧边栏的宽 + 让开标题栏的高」两件事合起来的一个值。
        ContentHost.Margin = new Thickness(width, HomePage.ChromeHeight, 0, 0);
        PlaceMusicBar();

        // 标题栏那一排的缩进：侧边栏在屏上时是 8，其余时候是 18（标记里那个值）。
        // **2026-11-13 从 11 改成 8，并实测过两档**：栏 48→56 宽之后，框架给条目的图标格是「栏宽 − 8」= 48，
        // 而按钮左右各留 4 —— 方格与图标格同宽，字形因此居中落在栏的中线 28 上，展开与收成图标栏两档
        // 都是这个数（实体屏量过 32→28 那一版之后的读数）。这一排的托盘 40 宽、字形 28 居中，缩进 8 时
        // 字形中线同样是 28 —— 用户令「返回键出现时要和下面那一列对齐」，两颗（返回、折叠）都落在栏里
        // 那些图标的同一条竖线上，换档时也不挪。8 同时让开了窗口自己那 8 像素的缩放边框。
        // 上边那 4 像素由 ShellPage.xaml 的 TitleActions 定下，这一处只换左边的缩进。
        var titleMargin = new Thickness(shown ? 8 : 18, 4, 0, 0);
        if (TitleActions.Margin != titleMargin)
        {
            // Moving an unchanged row does not raise SizeChanged. Measure after layout,
            // otherwise the native title bar keeps the old clickable rectangle.
            _sidebarTitleLayoutPending = true;
            TitleActions.Margin = titleMargin;
        }

        ToolTipService.SetToolTip(NavToggleButton, Sidebar.IsPaneOpen ? "关闭导航" : "打开导航");
        ApplyAccountCluster(shown);

        // 标题栏那三个洞跟着窗宽走（正中那颗搜索框、右上角那组账号都是随窗宽落位的），而这条路径是
        // 「窗口尺寸变了」唯一一定会走到的地方 —— 不在这里补一次，拉宽之后点搜索框就变成拖窗口。
        // 坐标没变时 ReportTitleBarHoles 自己会跳过。
        ReportTitleBarHoles();
    }

    /// <summary>
    /// 账号那一颗此刻站在哪儿。**两种模式都在标题栏右上角、系统那三颗窗口按钮左边**（2026-11-13 用户令
    /// 「人像图标移到最小化左边，和搜索栏对齐」）：侧边栏在屏上时只留一颗 40×40 的人像（字形 20 居中），
    /// 中心落在 y=24，与正中那颗搜索框、左边那一排、系统那三颗（Tall 档 48 高）同一条中线；
    /// 不在侧边栏模式时仍是「名字 · 服务器」那一段文字。
    /// <para>
    /// 纯摆位，留在 code-behind：它读的全是「这一栏现在开着没有」「系统给那三颗按钮留了多宽」，没有业务判断。
    /// </para>
    /// </summary>
    private void ApplyAccountCluster(bool shown)
    {
        AccountCluster.HorizontalAlignment = HorizontalAlignment.Right;
        AccountCluster.VerticalAlignment = VerticalAlignment.Top;
        AccountCluster.Width = double.NaN;
        // 右端让开系统那三颗按钮、再留 8 的缝：这颗按钮的右沿因此贴在最小化左边。
        AccountCluster.Margin = new Thickness(0, 4, CaptionRoom() + 8, 0);
        // 菜单从这颗按钮底下落出来；两种模式都在标题栏里，朝下这一档就是唯一那一档。
        AccountMenu.Placement = FlyoutPlacementMode.BottomEdgeAlignedRight;

        if (shown)
        {
            // 侧边栏模式：只留一颗人像，按钮缩回 40 见方 —— 和栏里那些按钮、和标题栏那一排同一块方格。
            AccountButton.Width = 40;
            AccountButton.Height = 40;
            AccountButton.Padding = new Thickness(0);
            AccountButton.MaxWidth = 40;
            AccountButton.HorizontalContentAlignment = HorizontalAlignment.Center;
            AccountGlyph.Visibility = Visibility.Visible;
            AccountText.Visibility = Visibility.Collapsed;
        }
        else
        {
            AccountButton.Width = double.NaN;
            AccountButton.Height = double.NaN;
            AccountButton.Padding = new Thickness(10, 3, 10, 3);
            AccountButton.MaxWidth = 240;
            AccountButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            AccountGlyph.Visibility = Visibility.Collapsed;
            AccountText.Visibility = Visibility.Visible;
        }

        // 挪位不改尺寸，SizeChanged 不会再发一次：这一拍先报一遍（对齐刚落下去，位置可能还是旧的），
        // 再挂一拍 —— 新的落位排完时 LayoutUpdated 会按新位置重报一次，窗口那个洞才跟着走。
        _sidebarTitleLayoutPending = true;
        ReportTitleBarHoles();
    }

    /// <summary>
    /// 标题栏右端要给系统那三颗窗口按钮让出多宽（DIP）。框架报的 <c>RightInset</c> 是准数；拿不到时退回
    /// 三颗 48 宽的 Tall 值（同 <c>HostWindow</c> 的 <c>CaptionButtonWidth × 3</c>）—— 那时窗口还没建好，
    /// 之后 <see cref="ApplySidebarLayout"/> 会再走一遍。
    /// </summary>
    private double CaptionRoom() => _window is { } window && window.TitleBarMetrics.RightInset > 0
        ? window.TitleBarMetrics.RightInset
        : 144;

    /// <summary>导航开关：侧边栏的开与关。框架那颗自带开关收掉之后（<c>IsPaneToggleButtonVisible=False</c>），
    /// 开合只剩这一处入口 —— 它也把菜单项从栏顶那一格解放出来，让它们往上顶到标题栏下沿。</summary>
    private void OnNavToggleClicked(object sender, RoutedEventArgs e)
    {
        if (!SidebarShown) return;
        Sidebar.IsPaneOpen = !Sidebar.IsPaneOpen;
    }

    /// <summary>
    /// SplitView draws its compact pane as "full-width clip rectangle + offset": the clip stays
    /// <c>OpenPaneLength</c> wide and is shifted by (compact − expanded), so only the leftmost
    /// compact strip survives. When NavigationView collapses the pane it rewrites the pane's
    /// <c>OpenPaneLength</c> to the compact length (56), but the offset was already computed from the
    /// expanded width (56 − 256 = −200) and is not recomputed: the clip rectangle is 56 wide and yet
    /// slides 200 DIP left, which clips the entire icon rail off-screen and leaves only the ≡ button.
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
        HideLibraryPreview();
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
            item.PointerEntered += OnSidebarLibraryPointerEntered;
            item.PointerExited += OnSidebarLibraryPointerExited;
            Sidebar.MenuItems.Insert(at++, item);
            _sidebarLibraries.Add(tag, item);
        }
        SidebarLibrariesHeader.Visibility = _sidebarLibraries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncSidebar();
    }

    private void OnSidebarItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        HideLibraryPreview();
        if (!SidebarShown || args.InvokedItemContainer is not NavigationViewItem { Tag: string tag }) return;
        // 页脚那颗设置开的是另一个窗口，不改变这一页的选中目的地（SelectsOnInvoked="False" 已把选中挡在
        // 外面，这里连 GoTo 也不走）。
        if (tag == "settings")
        {
            ShowSettings();
            return;
        }
        // Invoking the selected home item must still retry a failed directory request.
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
        HideLibraryPreview();
        GoTo(tag);
    }
}
