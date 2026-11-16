using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Diagnostics;
using Momoka.Shell.Interop;
using Momoka.Theming;
using Windows.Graphics;
using Windows.UI;

namespace Momoka.Shell.Views;

/// <summary>
/// 设置窗口 —— 需求 1 的第二颗按键，「点击后弹出设置窗口」.
/// <para>
/// 设置使用 framework Window，系统提供窗口和 DPI 行为。主窗口沿用 HWND＋XAML 岛；当前集成视频在页面的
/// Composition 树里，独占视频由 mpv 自建顶层窗口管理。搜索版本窗同样是 framework Window。
/// </para>
/// <para>
/// Which is also the hazard: a WinUI app exits when its last <see cref="Window"/> closes, and this app
/// normally has none. So the X on this caption must not close it. <see cref="OnClosing"/> cancels and
/// hides instead, and the only real close is <see cref="Close"/> from <c>ShellPage.Shutdown</c>, on the
/// way out of the process anyway.
/// </para>
/// <para>
/// Unowned on purpose. An owned window is always in front of its owner, which is what one would want for
/// a dialog — but this one is not modal, and the app spends its time playing video full screen. A
/// settings window pinned over a full-screen playback is the same class of complaint as the task bar
/// showing over it; a window that can be sent behind and brought back by pressing 设置 again is not.
/// </para>
/// <para>
/// 「按设置能拿回来」不是 Activate 白送的：WinUI 3 的 <c>DesktopWindowImpl::ActivateImpl</c> 只做
/// <c>ShowWindow(SW_SHOW)</c> 与 <c>SetActiveWindow</c>，对一扇已经看得见的窗口不改 z 序。窗口被主窗口
/// 盖住之后，再点设置只会把它「激活」在主窗口底下（2026-11-14 用户报「再点一次设置也不会跑到主窗口
/// 前面」）。因此每次 <see cref="Show"/> 都在 Activate 之后显式 <c>AppWindow.MoveInZOrderAtTop()</c>：
/// 主窗口不在 topmost 带里（全屏时由 <c>HostWindow.StandAsideForOwnWindow</c> 先让开）就一定会露出来。
/// </para>
/// <para>
/// 这一提还不够：Show 跑在主窗口那颗设置按钮的 Click 里，事件返回后 WinUI 会把前台与键盘焦点交还主窗口
/// 的 XAML 岛（对岛设焦点会连它的顶层窗口一起激活），刚提上来的窗口又被压回主窗口后面（2026-11-15 用户
/// 再报「点完设置自动跑到主窗口后面」）。所以 Show 还在下一拍派发一次同样的「激活 + 提到最前」，等这一下
/// 输入处理彻底走完再提回去；窗口若已收起就不再动（见 <see cref="Show"/>）。
/// </para>
/// </summary>
internal sealed partial class SettingsWindow
{
    private const string Category = "设置窗口";

    /// <summary>
    /// 画布压在 Mica 上的浓度，与主窗口的 <c>EgCanvasBrush</c> 是同一个数（见 <c>ThemeHost.CanvasAlpha</c>）。
    /// 两个窗口是同一套外观，底也不能一个 60%、一个别的。
    /// </summary>
    private const byte CanvasAlpha = 0x99;

    /// <summary>Logical pixels, scaled to the owner's DPI in <see cref="Place"/>.</summary>
    private const int LogicalWidth = 1100;

    private const int LogicalHeight = 760;

    private readonly Window _window;
    private readonly Frame _frame;

    /// <summary>换主题时重画标题栏。存下来是为了 <see cref="Close"/> 里退订。</summary>
    private readonly Action<Momoka.Theming.UiTheme> _repaint;

    /// <summary>Set only by <see cref="Close"/>, so <see cref="OnClosing"/> knows to let one through.</summary>
    private bool _closing;

    private bool _open;

    private SettingsWindow(Window window, Frame frame, SolidColorBrush canvas, bool mica)
    {
        _window = window;
        _frame = frame;
        _canvas = canvas;
        _mica = false;
        _micaSupported = mica;

        // 自绘空白标题栏和系统关闭按钮一起随主题更新；画布的颜色也在同一趟里改。
        _repaint = _ =>
        {
            // 「透明效果」关掉时把 Mica 摘掉，再按新的系统状态重画画布。
            ApplyBackdrop();
            InvalidateSystemAppearance();
        };
        ThemeHost.Changed += _repaint;

        window.AppWindow.Closing += OnClosing;
        StartSystemAppearanceWatch();
    }

    /// <summary>
    /// 整扇窗唯一的那层画布，挂在根 Grid 上：材质那一档是 60% 的窗口色，回退那一档是不透明窗口色。
    /// 改这一个对象的颜色，已经画上去的那块当场跟着变（同 ThemeHost 的手法）。标题栏和正文共用它，
    /// 所以窗口从上沿到下沿是一块，不是「标题一条、内容一块」。
    /// <para>
    /// 写在构造函数下面，不在上面那一组字段里：<c>Momoka.SettingsLifetimeTests</c> 按「<c>Category</c>
    /// 常量 → 构造函数」切一段源码进那个没有 WinUI 引用的测试项目，<see cref="SolidColorBrush"/> 字段留在
    /// 上面会把那段编炸。位置不影响含义。
    /// </para>
    /// </summary>
    private readonly SolidColorBrush _canvas;

    /// <summary>
    /// Mica 的支持检查/属性赋值是否成功。不是 controller Active 状态；系统回退不等于赋值失败。
    /// </summary>
    private bool _mica;

    /// <summary>
    /// 这台机器上支不支持 Mica，与「这一档要不要用」分开：设置页的「透明效果」关掉时支持仍在、底材不挂。
    /// </summary>
    private readonly bool _micaSupported;

    /// <summary>
    /// Whether the window is on screen. Tracked rather than asked of the OS, because 「shown」 and
    /// 「hidden」 are the two states this class puts it in and nothing else moves it between them.
    /// </summary>
    internal bool IsOpen => _open;

    /// <summary>Outer size in physical pixels. Read by the self-check.</summary>
    internal SizeInt32 Size => _window.AppWindow.Size;

    /// <summary>
    /// 自检用：窗口级底材是不是 Mica、那层画布多浓。两样一起报，是因为它们是一条链上的两节 —— 窗口没有
    /// 材质时画布必须落回不透明，材质在而画布还实着则等于白开。
    /// </summary>
    internal (bool Mica, byte CanvasAlpha) BackdropState => (_mica, _canvas.Color.A);

    /// <summary>
    /// 这扇窗的 HWND。自检量「设置窗口真的盖在画面之上」要用它 —— 那句问的是屏幕坐标上谁在最前面，
    /// 只有句柄答得了。
    /// </summary>
    internal IntPtr Handle => Win32Interop.GetWindowFromWindowId(_window.AppWindow.Id);

    /// <summary>
    /// 收摊了（X 或 <see cref="Hide"/>）。外壳凭它把画面放下的置顶拿回来 —— 全屏播放时那扇主窗是 topmost 的，
    /// 开设置之前先让开，收了摊就得还原（见 <c>HostWindow.StandAsideForOwnWindow</c> 与
    /// <c>ShellPage.ShowSettings</c>）。按「真的收起了才喊」：<see cref="Hide"/> 本就有幂等闸，
    /// <see cref="OnClosing"/> 那一支是用户按 X 的路。
    /// </summary>
    internal event Action? Hidden;

    /// <summary>
    /// What is on screen inside this window: the hosted 服务器/诊断/通知/网页控制台 page when the settings
    /// page has one showing, otherwise the settings page itself. The self-check reads both trees through this.
    /// </summary>
    internal object? CurrentPage => (_frame.Content as SettingsPage)?.CurrentPage ?? _frame.Content;

    /// <summary>
    /// The settings page itself, whatever it happens to be showing inside. The self-check needs both: this
    /// one to drive its walk through the cards, <see cref="CurrentPage"/> to read the tree that is really
    /// on screen — which for the hosted categories is a different page altogether.
    /// </summary>
    internal SettingsPage? Page => _frame.Content as SettingsPage;

    /// <summary>
    /// Builds the window, or returns null having said why. Null is not a failure the caller has to
    /// handle loudly — <c>ShellPage.ShowSettings</c> falls back to opening the settings page in the main
    /// window's frame, which is where it lived before this class existed.
    /// </summary>
    internal static SettingsWindow? TryCreate(Windowing.HostWindow? owner)
    {
        try
        {
            // 这扇窗和主窗口一样上 Mica（Base）。主窗口是在 HWND 上直接问 DWM，这里走框架的窗口级
            // SystemBackdrop：两者的档位与画布浓度必须成对，见 ApplyBackdrop 与 Palette 的 EgCanvasBrush。
            // 设置页的「透明效果」关掉时不挂底材，画布直接落成不透明窗口色。
            var mica = MicaController.IsSupported();
            var useMica = mica && ThemeHost.TransparencyEnabled;
            var canvas = new SolidColorBrush(useMica ? CanvasColor(ThemeHost.Current) : WindowColor(ThemeHost.Current));

            // 画布挂在根 Grid 上，Frame 让空：两层都画就是 60% 叠 60%，Mica 白开。
            var frame = new Frame
            {
                // 需求 9 的同一条：没有过渡。也让第一次导航不闪。
                Transitions = null,
                Background = new SolidColorBrush(Colors.Transparent)
            };

            // 深浅跟着当前主题走，而不是写死 Dark：这个窗口里全是框架自己的控件（Expander、NumberBox、
            // ComboBox），它们只认 ElementTheme。
            ThemeHost.Register(frame);

            // Top-level in Palette.xaml, so unlike the brushes it is safe to index; see WindowFill.
            if (Application.Current.Resources.TryGetValue("EgUiFontFamily", out var font) &&
                font is FontFamily family)
                frame.FontFamily = family;

            // 标题必须显式设成空串（用户 2026-09-13「把设置页面左上角的标题栏的图标和设置字样去掉」）。
            // 「不设」不等于空 —— Window.Title 的官方文档原话是「SetWindowText 的封装」，而不设时标题栏
            // 兜底显示「WinUI Desktop」（当天截图为证）。写一个空串进去，左上角才是真的没有字。图标那
            // 一半在 <see cref="PaintCaption"/>。任务栏上那一格照旧有字有图（进程名和应用图标兜着），
            // 只有标题栏是空的。
            var window = CreateCaptionedWindow(frame, canvas);

            Place(window, owner);
            var created = new SettingsWindow(window, frame, canvas, mica);
            created.ApplyBackdrop();
            // The constructor already cached system appearance. Backdrop assignment may have
            // changed the canvas/assignment result; failed repaint must leave the cache invalid.
            created.InvalidateSystemAppearance();

            Log.Info(Category, $"设置窗口已创建（底材：{(useMica ? "Mica" : "纯色")}）");
            return created;
        }
        catch (Exception error)
        {
            Log.Warn(Category, "创建设置窗口失败，改用主窗口内的设置页", error);
            return null;
        }
    }

    /// <summary>空白标题栏独占顶部一行；设置正文始终在下一行，系统只保留关闭按钮。</summary>
    /// <param name="content">标题栏下面那一整块。</param>
    /// <param name="fill">
    /// 这扇窗的底。传进来的那一支由调用方持有、换主题时改它的颜色，标题栏和正文因此始终同一块；
    /// 不传就是一块不透明窗口色（探针建的窗口走这条默认路）。
    /// </param>
    internal static Window CreateCaptionedWindow(FrameworkElement content, Brush? fill = null)
    {
        var root = new Grid { Background = fill ?? new SolidColorBrush(WindowColor(ThemeHost.Current)) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var caption = new Border { Name = "SettingsWindowTitleBar", Background = new SolidColorBrush(Colors.Transparent) };
        root.Children.Add(caption);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        ThemeHost.Register(root);
        var window = new Window { ExtendsContentIntoTitleBar = true, Content = root, Title = string.Empty };
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }
        window.SetTitleBar(caption);
        return window;
    }

    /// <summary>
    /// Shows the window on a category, building or re-pointing the page inside it.
    /// <para>
    /// Navigated before it is shown, and every time: the page is cached in this frame, so the usual visit
    /// is a parameter change on a page that is already built — which is what keeps half-typed config text
    /// alive across trips to 设置, exactly as it did when the page lived in the main frame.
    /// </para>
    /// <para>
    /// 提到最前要做两次。第一次在这一拍里：窗口立刻可见、可点。第二次排到输入处理之后（<see cref="_open"/>
    /// 闸门），因为这次 Show 多半是从主窗口按钮的 Click 里进来的，事件返回后主窗口会把前台收回去，把第一次
    /// 提上来的窗口重新压到后面。两下都幂等，从别处（菜单、启动参数）开窗时第二次只是重复一次无害的激活。
    /// </para>
    /// </summary>
    internal void Show(SettingsRequest request)
    {
        _frame.Navigate(typeof(SettingsPage), request);

        // Nothing in this window goes back, and a stack that grows by one per visit is a page kept alive
        // per visit with it.
        _frame.BackStack.Clear();

        // WinAppSDK 2.5.1 的关闭按钮在 Hide 后可能保留旧悬停区域；隐藏期间以 HTNOWHERE
        // 发一次 NC move 触发该区域的 PointerExited。只发送本窗消息，不挪动用户光标。
        // Input::OnNcMouseMove_Callback → CTitleBar::OnPointerExited，见 microsoft-ui-xaml #12232。
        if (!_open && Native.GetCursorPos(out var cursor))
        {
            var coordinates = unchecked((cursor.Y << 16) | (cursor.X & 0xffff));
            Native.SendMessage(Handle, Native.WmNcMouseMove, IntPtr.Zero, (IntPtr)coordinates);
        }

        _open = true;

        // Activate 已执行 ShowWindow + UpdateWindow；避免在内容首帧前额外显窗。
        _window.Activate();

        // 已经看得见的窗口，Activate 只 SetActiveWindow、不动 z 序 —— 被主窗口盖住之后，再点设置
        // 仍然是盖住。每趟显现都把窗口提到 z 序最前，才与「按设置键就能拿回来」这句话对得上。
        _window.AppWindow.MoveInZOrderAtTop();

        // 这一拍还在主窗口按钮的输入处理里：Click 返回后主窗口的岛把前台与键盘焦点收回去，刚提上来的
        // 窗口会被重新压到后面。排到这一拍之后再提一次 —— 那时它才是真的在最前面。
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_open) return;
            _window.Activate();
            _window.AppWindow.MoveInZOrderAtTop();
        });

        ResumeSystemAppearance();
    }

    /// <summary>
    /// Off screen, still built. What the X does, and what signing out does.
    /// <para>
    /// The hosted page is released on the way out. It would otherwise go on doing whatever it was doing
    /// behind a window nobody can see, which for 需求 8's console means a live browser process — and for the
    /// server list and the log view means work whose result nothing will read. Reopening navigates the frame
    /// again, so the page comes back fresh.
    /// </para>
    /// </summary>
    internal void Hide()
    {
        if (!_open) return;

        _open = false;
        PauseSystemAppearance();
        Page?.ReleaseHosted();
        _window.AppWindow.Hide();
        Hidden?.Invoke();
    }

    /// <summary>
    /// The real close, from <c>ShellPage.Shutdown</c> only. Left to the framework the window would keep
    /// the app alive after the main window had gone: a hidden window is still an open one.
    /// </summary>
    internal void Close()
    {
        _closing = true;
        _open = false;
        StopSystemAppearanceWatch();
        Page?.ReleaseHosted();
        ThemeHost.Changed -= _repaint;

        try
        {
            _window.Close();
        }
        catch (Exception error)
        {
            Log.Warn(Category, "关闭设置窗口失败", error);
        }
    }

    /// <summary>
    /// Hides on the X instead of closing; see the note on the class for why that matters more here than
    /// it would in an ordinary app.
    /// </summary>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_closing) return;

        e.Cancel = true;
        Hide();
    }

    /// <summary>
    /// Centred on the owner's monitor at <see cref="LogicalWidth"/>×<see cref="LogicalHeight"/> logical
    /// pixels. <c>MoveAndResize</c> speaks physical ones, so the numbers are scaled by the owner's DPI —
    /// 760 logical is 1520 physical at 200%, which is taller than a 1080p monitor, hence the clamp to the
    /// work area rather than a straight multiply.
    /// </summary>
    private static void Place(Window window, Windowing.HostWindow? owner)
    {
        var self = Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        var reference = owner?.Handle is { } handle && handle != IntPtr.Zero ? handle : self;

        var dpi = Native.GetDpiForWindow(reference);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var area = DisplayArea.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(reference), DisplayAreaFallback.Nearest);
        var work = area.WorkArea;

        var width = Fit(LogicalWidth, 640, work.Width, scale);
        var height = Fit(LogicalHeight, 480, work.Height, scale);

        window.AppWindow.MoveAndResize(new RectInt32(
            work.X + ((work.Width - width) / 2),
            work.Y + ((work.Height - height) / 2),
            width,
            height));
    }

    /// <summary>
    /// One axis: the wanted size in physical pixels, short of filling the monitor, and never below what
    /// the page needs to lay out — a settings page 400 pixels wide is not a smaller settings page.
    /// </summary>
    private static int Fit(int logical, int minimum, int available, double scale) =>
        Momoka.Infrastructure.ScreenPlacement.FitAxis(logical, minimum, available, scale);

    /// <summary>
    /// 窗口级底材：Mica（Base），这是当前项目的档位选择。**整窗只有这一块底和一层画布**：标题栏、分类栏、正文压在同一个画布
    /// 对象上，没有「标题一条、侧栏一块」的分块（2026-11-12 用户令「不是，你为什么一定要给标题和侧边栏
    /// 加卡片」）。层次由设置卡自己的底色给出，不靠窗口再切几块。
    /// </summary>
    private void ApplyBackdrop()
    {
        // 不支持 Mica、或者设置页把「透明效果」关掉了：撤掉底材，画布落成不透明窗口色。
        if (!_micaSupported || !ThemeHost.TransparencyEnabled)
        {
            try { _window.SystemBackdrop = null; }
            catch (Exception error) { Log.Warn(Category, "撤除设置窗口 Mica 失败", error); }
            _mica = false;
            _canvas.Color = WindowColor(ThemeHost.Current);
            _lastSystemAppearance = null;
            return;
        }

        if (_mica) return;

        _lastSystemAppearance = null;

        try
        {
            _window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            _mica = true;
        }
        catch (Exception error)
        {
            // Mica is a nicety. 关了透明效果、远程桌面或 GPU 不支持时，这扇窗必须还是一扇读得清的实色窗
            // —— 画布那一支换成不透明窗口色就够了：标题栏和正文用的是同一个对象，一处改、整窗跟上。
            Log.Warn(Category, "设置 Mica 背景失败，改用纯色底", error);
            _mica = false;
            _canvas.Color = WindowColor(ThemeHost.Current);
        }
    }

    /// <summary>
    /// The same caption as the main window's, and for the same reason: the app has one appearance, and a
    /// caption left to the system is a pale strip over a window whose every other pixel is the theme's
    /// window colour. The inactive twins are set to their active partners because an unset twin does not
    /// inherit — see <c>HostWindow.ConfigureTitleBar</c>, which this mirrors.
    /// <para>
    /// **标题栏不画底，两种档位都一样。** 有材质时它底下是那块 Mica，没有材质时是不透明窗口色 —— 无论
    /// 哪种，底下都是根 Grid 上那支画布，标题栏再压一条自己的颜色就会把窗口切成两截。系统那三颗按钮的
    /// 四种底色同样给透明，失焦时也没有第二块默认底可露。
    /// </para>
    /// <para>
    /// 颜色取自当前主题，所以换主题时这里要再跑一遍（<see cref="_repaint"/>）。画布也在这里改颜色：
    /// 它和标题栏是同一支窗口角色，换主题时两处必须同一拍翻。悬停和按下那两档故意用半透明的白／黑而不是
    /// 主题里的某个面：它们压在画布上，靠透明度叠出来的那一档在每一套主题里都对。
    /// </para>
    /// <para>
    /// Its own try/catch, inside <see cref="TryCreate"/>'s: a caption colour that will not take is a
    /// cosmetic loss, and falling back to the in-frame settings page over one would be a worse trade.
    /// </para>
    /// </summary>
    private bool PaintCaption()
    {
        try
        {
            var theme = ThemeHost.Current;
            var systemAppearance = ReadSystemAppearance();
            // 画布先更新；非客户区API失败也不让整窗保留旧的高对比/透明状态。
            _canvas.Color = SelectCanvasColor(systemAppearance, WindowColor(theme), _mica);
            var handle = Win32Interop.GetWindowFromWindowId(_window.AppWindow.Id);

            var dark = theme.IsDark ? 1 : 0;
            Native.DwmSetWindowAttribute(handle, Native.DwmUseImmersiveDarkMode, ref dark, sizeof(int));

            var corners = Native.DwmCornerRound;
            Native.DwmSetWindowAttribute(handle, Native.DwmWindowCornerPreference, ref corners, sizeof(int));

            // 左上角那颗图标不要（用户 2026-09-13「把设置页面左上角的标题栏的图标和设置字样去掉」）。
            // 发空句柄摘不掉它 —— 系统的查找链「WM_SETICON 给的 → 窗口类图标 → exe 资源里的第一颗」
            // 会把图标原样补回来（当天发过空句柄，截图上照样有）。所以给标题栏画的那一档（小档）换成
            // <see cref="BlankCaptionIcon"/> 的全透明图标：链上每一环都在，只是画出来看不见。大档不动，
            // 任务栏和 Alt-Tab 照旧拿应用图标。字样那一半在 <see cref="TryCreate"/>：Title 显式设空串。
            Native.SendMessage(handle, Native.WmSetIcon, (IntPtr)0, BlankCaptionIcon.Handle);

            var titleBar = _window.AppWindow.TitleBar;
            if (titleBar is null) return false;

            // 画布：Mica 那一档是 60% 的窗口色，回退那一档是不透明窗口色。改颜色而不是换画刷 —— 根 Grid
            // 上挂的就是这个对象，标题栏和正文当场一起变。
            titleBar.ForegroundColor = systemAppearance.HighContrast
                ? systemAppearance.Foreground : ThemeHost.ToColor(theme.Colors.Text);
            titleBar.ButtonForegroundColor = titleBar.ForegroundColor;

            // 标题栏不画底，三颗系统按钮的四种底色也全透明：它们底下就是根 Grid 那块画布，整窗从上沿到
            // 下沿是同一块。失焦时也没有第二块默认底可露。
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            titleBar.ButtonHoverBackgroundColor = systemAppearance.HighContrast ? systemAppearance.Highlight : theme.IsDark
                ? Color.FromArgb(35, 255, 255, 255)
                : Color.FromArgb(25, 0, 0, 0);
            titleBar.ButtonPressedBackgroundColor = systemAppearance.HighContrast ? systemAppearance.Highlight : theme.IsDark
                ? Color.FromArgb(55, 255, 255, 255)
                : Color.FromArgb(45, 0, 0, 0);
            titleBar.ButtonHoverForegroundColor = systemAppearance.HighContrast
                ? systemAppearance.HighlightText : titleBar.ButtonForegroundColor;
            titleBar.ButtonPressedForegroundColor = titleBar.ButtonHoverForegroundColor;
            titleBar.InactiveForegroundColor = titleBar.ForegroundColor;
            titleBar.ButtonInactiveForegroundColor = titleBar.ButtonForegroundColor;
            return true;
        }
        catch (Exception error)
        {
            Log.Warn(Category, "设置窗口标题栏配色失败", error);
            return false;
        }
    }

    /// <summary>压在 Mica 上的那层画布：窗口角色加浓度。</summary>
    private static Color CanvasColor(UiTheme theme) =>
        ThemeHost.ToColor(theme.Colors.Window.WithAlpha(CanvasAlpha));

    /// <summary>没有材质时那一整块不透明底：就是窗口角色本身。</summary>
    private static Color WindowColor(UiTheme theme) => ThemeHost.ToColor(theme.Colors.Window);
}
