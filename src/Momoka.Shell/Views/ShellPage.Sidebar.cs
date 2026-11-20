using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private const double SidebarWideThreshold = 1008;
    private readonly Dictionary<string, NavigationViewItem> _sidebarLibraries = new(StringComparer.Ordinal);
    private CompositeTransform? _sidebarPaneClip;
    private bool _sidebarMode;
    private bool _sidebarPlaying;
    private bool _sidebarTitleLayoutPending;
    private bool _syncingSidebar;

    /// <summary>
    /// 进侧边栏模式时还没量到宽度（<c>Loaded</c> 那一刻 ActualWidth 是 0），而且用户还没有存过开合选择时，
    /// 初值等窗宽稳定再定；存过的那一档直接还原，不走这一拍。
    /// </summary>
    private bool _sidebarPanePending;

    /// <summary>等窗宽稳定的那一拍：启动时外壳会先按上一位宽排一次，随后才收到真正的窗口尺寸。</summary>
    private DispatcherQueueTimer? _sidebarPaneTimer;

    /// <summary>侧栏右沿拖动条那一趟的起点；<see cref="_resizingSidebar"/> 为假时都不看。</summary>
    private bool _resizingSidebar;
    private double _sidebarResizeStartX;
    private double _sidebarResizeStartWidth;
    private bool _sidebarResizePointerOver;

    /// <summary>拖动条的光标（左右箭头）。第一次指针进入时创建，离树时释放。</summary>
    private InputSystemCursor? _sidebarResizeCursor;

    // Loaded/Unloaded also cover the temporary shells used by offline verification.
    // Reattaching reads the setting again, so an event missed while unloaded is harmless.
    private void InitializeSidebar()
    {
        InitializeSidebarIcons();
        Sidebar.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => ApplySidebarLayout());
        _sidebarPaneTimer = DispatcherQueue.CreateTimer();
        _sidebarPaneTimer.Interval = TimeSpan.FromMilliseconds(120);
        _sidebarPaneTimer.IsRepeating = false;
        _sidebarPaneTimer.Tick += (_, _) => ResolvePendingSidebarMode();
        SizeChanged += (_, _) => UpdateSidebarWidth();
        SidebarResizeGrip.PointerEntered += OnSidebarResizePointerEntered;
        SidebarResizeGrip.PointerExited += OnSidebarResizePointerExited;
        SidebarResizeGrip.PointerPressed += OnSidebarResizePointerPressed;
        SidebarResizeGrip.PointerMoved += OnSidebarResizePointerMoved;
        SidebarResizeGrip.PointerReleased += OnSidebarResizePointerReleased;
        SidebarResizeGrip.PointerCaptureLost += OnSidebarResizePointerCaptureLost;
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
            AttachSidebarMotion();
            UpdateSidebarWidth();
        };
        Unloaded += (_, _) =>
        {
            ShellPrefs.SidebarChanged -= OnSidebarPreferencesChanged;
            DetachSidebarMotion();
            _sidebarPaneTimer?.Stop();
            // 拖动到一半离树：不写回设置，只把状态和光标收干净（离树后框架也不会再送指针事件）。
            EndSidebarResize(persist: false);
            _sidebarResizePointerOver = false;
            SidebarResizeGrip.Cursor = null;
            _sidebarResizeCursor?.Dispose();
            _sidebarResizeCursor = null;
            HideLibraryPreview();
        };
    }

    private void OnSidebarPreferencesChanged(UiSettings ui)
    {
        // All open windows receive ShellPrefs; unrelated preferences must not reflow this shell.
        if (_settings is null || !ReferenceEquals(ui, Settings.Ui)) return;
        // 宽度偏好也可能单独改过（导入设置）。先钉回 NavigationView；真变了才重排，没变就让原来的路径收尾。
        var before = Sidebar.OpenPaneLength;
        ApplyPreferredSidebarWidth();
        // 主页版面（媒体库顺序）也决定栏里那一段的次序（2026-11-18 用户令「订阅按照媒体库顺序排序」）：
        // 设置页拖完一行，栏跟着重排。次序没变时 Rebuild 自己会退出来，不会白闪一下。
        RebuildSidebarLibraries();
        if (_sidebarMode == !ui.EnableImmersive)
        {
            if (Math.Abs(before - Sidebar.OpenPaneLength) > 0.01) ApplySidebarLayout();
            return;
        }
        _sidebarMode = !ui.EnableImmersive;
        // 进侧边栏模式时还原用户上次选的那一档；还没选过（缺键的旧设置文件）才按当时的窗宽定一次初值。
        // 之后窗宽怎么变都不再改写开合（2026-11-16 用户令「拉动窗口的时候侧边栏不要自动展开或者收起」）。
        _sidebarPanePending = false;
        _sidebarPaneTimer?.Stop();
        if (_sidebarMode)
        {
            if (ui.SidebarExpanded is { } expanded)
            {
                // 2026-11-17 用户令「持久化保存侧边栏为展开或者折叠，不要每次打开都默认展开」：
                // 存着哪一档就还原哪一档，不等窗宽，也不按这一跑的窗宽改写它。
                Sidebar.IsPaneOpen = expanded;
            }
            // Loaded 那一刻 ActualWidth 还是 0：初值等窗宽跳停之后再定，见 ScheduleSidebarPaneDecision。
            else if (double.IsFinite(ActualWidth) && ActualWidth > 0)
            {
                Sidebar.IsPaneOpen = ActualWidth >= SidebarWideThreshold;
            }
            else
            {
                _sidebarPanePending = true;
            }
        }
        SyncSidebar();
    }

    /// <summary>
    /// 窗宽变了只重排，不碰开合（2026-11-16 用户令「拉动窗口的时候侧边栏不要自动展开或者收起」）：
    /// 展开与收成图标栏只归标题栏那颗折叠键，窗口拉宽拉窄都把用户选的那一档原样留着。
    /// </summary>
    private void UpdateSidebarWidth()
    {
        if (_sidebarPanePending) ScheduleSidebarPaneDecision();
        // 窗宽变了，能放下多宽的栏也变了：按当前窗宽重夹一次，再排按钮那一排。
        // 只动屏上的宽度，不写设置 —— 窗口拉回来时用户原来的宽度跟着回来。
        ApplyPreferredSidebarWidth();
        SyncTitleSearch();
        ApplySidebarLayout();
    }

    /// <summary>
    /// 进侧边栏模式那一刻宽度还没量出来（启动时 Loaded 的 ActualWidth 是 0）：等窗宽跳停 120ms
    /// 再按那时的真实窗宽定这一次初值，定完就清账。它是模式入口的初值，不是「窗口尺寸变就跟着换档」的钩子。
    /// 启动时外壳会先按上一位宽排一次（1404／760 之类的中间值），所以不能在第一下 SizeChanged 上就吃账。
    /// </summary>
    private void ScheduleSidebarPaneDecision()
    {
        if (_sidebarPaneTimer is not { } timer) return;
        timer.Stop();
        timer.Start();
    }

    private void ResolvePendingSidebarMode()
    {
        if (!_sidebarPanePending || !double.IsFinite(ActualWidth) || ActualWidth <= 0) return;
        _sidebarPanePending = false;
        Sidebar.IsPaneOpen = ActualWidth >= SidebarWideThreshold;
    }

    /// <summary>设置里存下来的展开宽度。还没拿到设置文档（构造到 Attach 之间）时就是标记里的出厂值。</summary>
    private double PreferredSidebarWidth => _settings?.Settings.Ui.SidebarWidth ?? UiSettings.DefaultSidebarWidth;

    /// <summary>
    /// 把偏好钉到 NavigationView 上。窗宽变了也要再走一遍：可用宽度变小时栏跟着收一档，
    /// 窗口拉回来后按偏好复原 —— 这一路上的夹取不写回设置。
    /// </summary>
    private void ApplyPreferredSidebarWidth()
    {
        // 拖动途中不覆盖用户正在拖的那一档；松手、窗口变化之后才回到偏好与窗宽的夹取。
        if (_resizingSidebar) return;
        var width = ClampSidebarWidth(PreferredSidebarWidth);
        if (Math.Abs(Sidebar.OpenPaneLength - width) > 0.01) Sidebar.OpenPaneLength = width;
    }

    /// <summary>
    /// 侧栏宽度的夹取。偏好自己的上下限在 <see cref="UiSettings"/> 上（180～480）；窗口窄时再给内容区
    /// 留出 320 DIP，免得拖宽的栏把页面挤没。窗口还没有尺寸（第一帧之前）时不按窗宽收。
    /// </summary>
    private double ClampSidebarWidth(double width)
    {
        var max = UiSettings.MaxSidebarWidth;
        if (double.IsFinite(ActualWidth) && ActualWidth > 0)
            max = Math.Max(UiSettings.MinSidebarWidth, Math.Min(max, ActualWidth - 320));
        return double.IsFinite(width)
            ? Math.Clamp(width, UiSettings.MinSidebarWidth, max)
            : UiSettings.DefaultSidebarWidth;
    }

    /// <summary>把侧栏换成指定宽度并当场重排（拖动每一拍都走这里；离线探针也直接调它复核夹取）。</summary>
    private void ResizeSidebar(double width)
    {
        width = ClampSidebarWidth(width);
        if (Math.Abs(Sidebar.OpenPaneLength - width) < 0.5) return;
        Sidebar.OpenPaneLength = width;
        ApplySidebarLayout();
    }

    private InputSystemCursor SidebarResizeCursor() =>
        _sidebarResizeCursor ??= InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    private void OnSidebarResizePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _sidebarResizePointerOver = true;
        SidebarResizeGrip.Cursor = SidebarResizeCursor();
    }

    private void OnSidebarResizePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _sidebarResizePointerOver = false;
        if (!_resizingSidebar) SidebarResizeGrip.Cursor = null;
    }

    private void OnSidebarResizePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 只有展开档能拖；收成图标栏时这条拖动条根本不在屏上（见 ApplySidebarLayout）。
        if (!SidebarShown || !Sidebar.IsPaneOpen) return;
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) return;
        _resizingSidebar = true;
        _sidebarResizeStartX = point.Position.X;
        _sidebarResizeStartWidth = Sidebar.OpenPaneLength;
        // 捕获之后指针移出这条 7 宽的条件带也继续收到事件；松手或捕获丢失时收尾。
        SidebarResizeGrip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnSidebarResizePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingSidebar) return;
        var x = e.GetCurrentPoint(Root).Position.X;
        ResizeSidebar(_sidebarResizeStartWidth + (x - _sidebarResizeStartX));
        e.Handled = true;
    }

    private void OnSidebarResizePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingSidebar) return;
        SidebarResizeGrip.ReleasePointerCapture(e.Pointer);
        EndSidebarResize();
        e.Handled = true;
    }

    private void OnSidebarResizePointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndSidebarResize();

    /// <summary>
    /// 一趟拖动收尾：把这一趟拖出来的宽度写回设置文件（下次启动照这个宽度展开）。捕获丢失、离树与
    /// 栏收起这三条路也走这里，但只有真的拖过才写；不写的那几档只清状态。
    /// </summary>
    private void EndSidebarResize(bool persist = true)
    {
        if (!_resizingSidebar) return;
        _resizingSidebar = false;
        if (!_sidebarResizePointerOver) SidebarResizeGrip.Cursor = null;
        if (!persist || _settings is null) return;
        var width = ClampSidebarWidth(Sidebar.OpenPaneLength);
        if (Math.Abs(Settings.Ui.SidebarWidth - width) < 0.01) return;
        Settings.Ui.SidebarWidth = width;
        _settings.Save();
    }

    private bool SidebarShown => _sidebarMode && !_sidebarPlaying && _session?.IsSignedIn == true
        && ContentHost.Visibility == Visibility.Visible;

    private void SyncSidebar()
    {
        var shown = SidebarShown;
        SidebarHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        // 标题栏那一条与栏同色（2026-11-14 用户令「标题栏的颜色要和侧边栏一样」）：栏在屏上时整条铺
        // EgSidebarBrush，栏顶那一格也归它画；栏收起（沉浸模式、登录页、播放）时整条撤下，让标题栏回到透明。
        TitleBarBackground.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        AppTitleLabel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        // 折叠开关只在商店侧栏出现时显示，主页位于最左，其他浏览页跟在返回右侧。
        NavToggleButton.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        // 应用名只在商店侧栏浏览态显示，独立于按钮行，位于导航按钮右侧；沉浸整块收起。
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
        // 2026-11-18 用户令「去掉侧边栏的订阅分区，订阅按照媒体库顺序排序」：订阅不再单独成段，两条入口
        // 跟库项排在同一张名单里（见 RebuildSidebarLibraries），这里只翻它们自己的显隐 —— 没有分区标题要跟着收。
        var moviePilot = _moviePilotService?.Available == true;
        SidebarMoviePilotCalendar.Visibility = MoviePilotCalendarAvailable ? Visibility.Visible : Visibility.Collapsed;
        SidebarMoviePilotMovies.Visibility = moviePilot ? Visibility.Visible : Visibility.Collapsed;
        SidebarMoviePilotSeries.Visibility = moviePilot ? Visibility.Visible : Visibility.Collapsed;

        // 账号那一组现在是 Root 的孩子（它要能落在左下角，也要能回标题栏右上角），播放时 Chrome 收着而它
        // 不会跟着收 —— 所以这里按外壳在不在屏上把它一起收掉。
        AccountCluster.Visibility = Chrome.Visibility == Visibility.Visible && AccountButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;

        // A detail page has no destination tag; its first breadcrumb still identifies its library/search.
        // 从主页下钻时那一节是「主页」，不是详情所属的媒体库 —— 例外规则见 ResolveSidebarTag。
        var tag = ResolveSidebarTag(_current, _trail.FirstOrDefault()?.Tag);
        var selected = tag switch
        {
            "home" => SidebarHome,
            MoviePilotCalendarPage.Destination when MoviePilotCalendarAvailable => SidebarMoviePilotCalendar,
            LibraryRequest.FavoritesTag when Favorites.HasItems => SidebarFavorites,
            HomeLayout.MoviePilotMovies when moviePilot => SidebarMoviePilotMovies,
            HomeLayout.MoviePilotSeries when moviePilot => SidebarMoviePilotSeries,
            _ => tag is not null && _sidebarLibraries.TryGetValue(tag, out var item) ? item : null
        };
        _syncingSidebar = true;
        try { Sidebar.SelectedItem = selected; }
        finally { _syncingSidebar = false; }
        SyncSidebarIcons(selected);
        ApplySidebarLayout();
        SyncHomeHeaderChrome();
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
        // 拖动条只跟着展开档走：收成 56 的图标栏没有可拖宽的一档，开合仍归标题栏那颗折叠键。
        // 它整个站在内容区一侧（左沿 = 栏的右沿），所以栏里的条目与滚动条照旧拿得到命中。
        var resizable = shown && Sidebar.IsPaneOpen;
        if (!resizable && _resizingSidebar) EndSidebarResize(persist: false);
        SidebarResizeGrip.Visibility = resizable ? Visibility.Visible : Visibility.Collapsed;
        if (resizable) SidebarResizeGrip.Margin = new Thickness(width, HomePage.ChromeHeight, 0, 0);
        // 商店模式让画布与页面一起避开标题栏，让标题栏那一条（与栏同色的 TitleBarBackground）整条露出；
        // 沉浸模式恢复旧版画布铺入标题栏、页内用 Padding 让位的排法。两档页面起点相同，切换不把顶端控件压进标题栏。
        ContentHost.Margin = new Thickness(width, _sidebarMode ? HomePage.ChromeHeight : 0, 0, 0);
        ContentHost.Padding = new Thickness(0, _sidebarMode ? 0 : HomePage.ChromeHeight, 0, 0);
        PlaceMusicBar();

        // 标题栏那一排的缩进：侧边栏在屏上时是 8，其余时候是 18（标记里那个值）。
        // **2026-11-13 从 11 改成 8，并实测过两档**：栏 48→56 宽之后，框架给条目的图标格是「栏宽 − 8」= 48，
        // 而按钮左右各留 4 —— 方格与图标格同宽，字形因此居中落在栏的中线 28 上，展开与收成图标栏两档
        // 都是这个数（实体屏量过 32→28 那一版之后的读数）。这一排的托盘 40 宽、字形 20 居中，缩进 8 时
        // 字形中线同样是 28 —— 用户令「返回键出现时要和下面那一列对齐」，两颗（返回、折叠）都落在栏里
        // 那些图标的同一条竖线上，换档时也不挪。8 同时让开了窗口自己那 8 像素的缩放边框。
        // 上边那 4 像素由 ShellPage.xaml 的 TitleActions 定下，这一处只换左边的缩进。
        var titleMargin = new Thickness(shown ? 8 : 18, _sidebarMode ? 4 : 6, 0, 0);
        if (TitleActions.Margin != titleMargin)
        {
            // Moving an unchanged row does not raise SizeChanged. Measure after layout,
            // otherwise the native title bar keeps the old clickable rectangle.
            _sidebarTitleLayoutPending = true;
            TitleActions.Margin = titleMargin;
        }

        ToolTipService.SetToolTip(NavToggleButton, Sidebar.IsPaneOpen ? "关闭导航" : "打开导航");
        NavPaneExpandedIcon.Visibility = Sidebar.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        NavPaneCollapsedIcon.Visibility = Sidebar.IsPaneOpen ? Visibility.Collapsed : Visibility.Visible;
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
        // 右端让开系统那三颗按钮、再留 16 的缝（2026-11-14 用户令「用户按钮往左移一点」：
        // 原先是 8，贴在最小化左边；现在再让开 8，按钮与系统三颗之间留出一点空）。
        AccountCluster.Margin = new Thickness(0, 4, CaptionRoom() + 16, 0);
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
        // 用户自己按了开关：还没落定的启动初值作废，免得那一拍回来把用户刚选的档改掉。
        _sidebarPanePending = false;
        _sidebarPaneTimer?.Stop();
        Sidebar.IsPaneOpen = !Sidebar.IsPaneOpen;
        PersistSidebarExpanded();
    }

    /// <summary>
    /// 用户自己按折叠键选了新的一档：写回设置文件，下次启动照这一档还原
    /// （2026-11-17 用户令「持久化保存侧边栏为展开或者折叠，不要每次打开都默认展开」）。
    /// 程序自己定初值的那几条路不写 —— 只有用户的选择才算数。
    /// </summary>
    private void PersistSidebarExpanded()
    {
        if (_settings is null || Settings.Ui.SidebarExpanded == Sidebar.IsPaneOpen) return;
        Settings.Ui.SidebarExpanded = Sidebar.IsPaneOpen;
        _settings.Save();
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
    /// 两个及以上在线账号时，栏里的库项要写账号用户名（用户令 2026-11-16：聚合模式下侧边栏为
    /// 「用户名 · 库名」）。只有一个账号时不写 —— 多出来的用户名不区分任何东西。
    /// </summary>
    private bool ShowsAccountNames => _session is { IsSignedIn: true } session && session.Sources.Count > 1;

    /// <summary>
    /// 把媒体库那几项和两条订阅填进栏里，**插在「媒体库」那条分区标题之后**。次序取
    /// <see cref="SidebarRowOrder"/> —— 就是设置 → 主页版面 →「媒体库顺序」那一份。
    /// <para>
    /// 2026-11-18 用户令「去掉侧边栏的订阅分区，订阅按照媒体库顺序排序」：从前订阅两条是栏尾单独一段，
    /// 栏里因此有「媒体库 → 订阅」两条标题；现在只有库项与订阅同排的一张名单，订阅在哪一行由 <c>HomeRows</c>
    /// 的顺序说了算。两条订阅控件仍是 XAML 里那两颗，这里每次先摘下来再跟着库项插回去，图标、AutomationId 与
    /// 选中高亮都不必重建。
    /// </para>
    /// </summary>
    private void RebuildSidebarLibraries()
    {
        // Both surfaces read the same directory. Refresh choices even when only the account-qualified title changed.
        (_settingsWindow?.Page ?? ContentFrame.Content as SettingsPage)?.RefreshHomeLibraryDirectory();
        var order = SidebarRowOrder();

        // 名单、次序和屏上那句库名都没变时不动：设置页拖一下排序也会走到这里，而重插是一记 Remove/Insert，
        // 正看着的那一项会闪一下。
        if (SidebarRowsMatch(order)) return;

        HideLibraryPreview();
        _previewDwells.Clear();
        // 订阅那两条不重建，但静止计时表刚被清空：登记回来，悬停预览才继续认它们。
        TrackPreviewItem(SidebarMoviePilotMovies);
        TrackPreviewItem(SidebarMoviePilotSeries);
        foreach (var item in _sidebarLibraries.Values)
        {
            Sidebar.MenuItems.Remove(item);
            _sidebarIcons.Remove(item);
        }
        _sidebarLibraries.Clear();
        Sidebar.MenuItems.Remove(SidebarMoviePilotMovies);
        Sidebar.MenuItems.Remove(SidebarMoviePilotSeries);

        var at = Sidebar.MenuItems.IndexOf(SidebarLibrariesHeader) + 1;
        foreach (var key in order)
        {
            if (HomeLayout.IsMoviePilot(key))
            {
                Sidebar.MenuItems.Insert(at++, key == HomeLayout.MoviePilotMovies
                    ? SidebarMoviePilotMovies : SidebarMoviePilotSeries);
                continue;
            }
            if (!_libraries.TryGetValue(key, out var library)) continue;

            var label = SidebarLibraryLabel(library);
            var item = new NavigationViewItem
            {
                Content = label,
                Tag = key
            };
            RegisterSidebarLibraryIcon(item, library.CollectionType);
            // 库名提示与三只悬停指针事件和订阅入口共用同一套接线（见 AttachSidebarPreviewItem）。
            AttachSidebarPreviewItem(item);
            AutomationProperties.SetAutomationId(item, "SidebarLibrary_" + library.ParentId);
            AutomationProperties.SetName(item, label);
            TrackPreviewItem(item);
            Sidebar.MenuItems.Insert(at++, item);
            _sidebarLibraries.Add(key, item);
        }
        SidebarLibrariesHeader.Visibility = _sidebarLibraries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncSidebar();
    }

    /// <summary>
    /// 栏里「媒体库 + 订阅」这一段的次序（见 <see cref="HomeLayout.SidebarOrder"/>）：与设置 → 主页版面 →
    /// 「媒体库顺序」同一份名单，新库照旧补在末尾。
    /// </summary>
    private IReadOnlyList<string> SidebarRowOrder() => HomeLayout.SidebarOrder(
        HomeLayout.Plan(_settings?.Settings.Ui.HomeRows, _libraryViews),
        _libraries.Keys);

    /// <summary>栏里那一行库写什么字：聚合多账号时带账号用户名（见 <see cref="SourceCaption.Prefix"/>）。</summary>
    private string SidebarLibraryLabel(LibraryRequest library) =>
        SourceCaption.Prefix(library.Title, library.Source, ShowsAccountNames);

    /// <summary>
    /// 栏里那一段此刻是不是已经是 <paramref name="order"/> 这个样子 —— 连屏上那句库名一起比。
    /// 是就不重插，省掉看得见的一闪（见 <see cref="RebuildSidebarLibraries"/>）。
    /// </summary>
    private bool SidebarRowsMatch(IReadOnlyList<string> order)
    {
        var existing = Sidebar.MenuItems.OfType<NavigationViewItem>()
            .Where(item => item.Tag is string tag && (HomeLayout.IsMoviePilot(tag) || _libraries.ContainsKey(tag)))
            .ToArray();
        if (existing.Length != order.Count || _sidebarLibraries.Count != _libraries.Count) return false;

        for (var index = 0; index < order.Count; index++)
        {
            if (!string.Equals(existing[index].Tag as string, order[index], StringComparison.Ordinal)) return false;
            if (_libraries.TryGetValue(order[index], out var library)
                && !Equals(existing[index].Content, SidebarLibraryLabel(library))) return false;
        }
        return true;
    }

    private void OnSidebarItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        SuppressLibraryPreview(args.InvokedItemContainer as NavigationViewItem);
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
        SuppressLibraryPreview(args.SelectedItem as NavigationViewItem);
        GoTo(tag);
    }
}
