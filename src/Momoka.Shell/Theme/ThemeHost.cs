using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Theming;
using Windows.UI;

namespace Momoka.Shell;

/// <summary>
/// 把一套主题真的画到界面上。<see cref="UiThemes"/> 说「这套主题是什么颜色」，这里说「怎么让已经在
/// 屏幕上的那几百个控件换成它」。
/// <para>
/// 手法是改画刷自己的 <see cref="SolidColorBrush.Color"/>，不是换字典条目。这一条是整个文件的关键：
/// 换条目（<c>dictionary["EgAccentBrush"] = 新画刷</c>）对已经绑上去的控件没有任何作用 —— 它们握着的是
/// 旧那个对象；而改颜色是改它们握着的那个对象，一次赋值，全屏幕当场跟着变，不用重建页面、不用重启。
/// </para>
/// <para>
/// 两个字典都写：<c>Application.RequestedTheme</c> 在 App.xaml 里钉死在 Dark（它只能在内容加载前设，
/// 之后改不了），而浅色主题靠的是把元素树的 <c>ElementTheme</c> 翻成 Light。翻过去之后那棵树按 Light
/// 字典解析，而 <c>DiagnosticsViewModel.Resolve</c> 和 <c>PlayerPage.BrushFor</c> 两处故意按应用级
/// 解析（也就是 Dark 字典）。两边写成同一套颜色，这个分歧就不存在了。HighContrast 不碰 —— 那份接的是
/// 系统自己的颜色，Windows 开了高对比度时盖掉它正是最不该做的事。
/// </para>
/// </summary>
public static class ThemeHost
{
    // ContentDialog lives in a separate Popup tree. Shared styles register it too, so native controls
    // change with the palette while it is open. Playback-only surfaces can locally opt out.
    public static readonly DependencyProperty FollowChangesProperty = DependencyProperty.RegisterAttached(
        "FollowChanges", typeof(bool), typeof(ThemeHost), new PropertyMetadata(false, OnFollowChangesChanged));

    public static void SetFollowChanges(DependencyObject target, bool value) => target.SetValue(FollowChangesProperty, value);

    public static bool GetFollowChanges(DependencyObject target) => (bool)target.GetValue(FollowChangesProperty);

    private static void OnFollowChangesChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement root) return;
        if (args.NewValue is true) Register(root);
        else
            Roots.RemoveAll(reference => !reference.TryGetTarget(out var item) || ReferenceEquals(item, root));
    }

    /// <summary>
    /// Palette.xaml 里那两份跟着主题走的字典。第三份 HighContrast 故意不在内。
    /// <para>
    /// 本项目从 Default 改为显式 Dark；Default 是 WinUI 支持的兜底键，显式三主题字典是项目组织选择。
    /// </para>
    /// </summary>
    private static readonly string[] Painted = ["Dark", "Light"];

    private static readonly ThemeColor White = ThemeColor.Rgb(0xFF, 0xFF, 0xFF);
    private static readonly ThemeColor Black = ThemeColor.Rgb(0, 0, 0);

    /// <summary>
    /// 主窗口画布压在 Mica 上的浓度：0x99 ≈ 60%。Palette.xaml 里 <c>EgCanvasBrush</c> 的首帧字面值是同一个数，
    /// 两处必须一起改 —— 前者决定第一帧，后者决定换主题之后。
    /// <para>
    /// 为什么是 60% 而不是「干脆全透」：深色 Mica 本身就是一张被壁纸染过、又压暗过的底，全透时壁纸的亮块会
    /// 从正文的字缝里钻出来；60% 把它压到只剩明暗结构，海报和正文的对比度与实色时代一个量级。
    /// </para>
    /// </summary>
    private const byte CanvasAlpha = 0x99;

    /// <summary>
    /// 侧边栏那一层（<c>LayerFillColorAltBrush</c>）的浓度：0x80 ≈ 50%，比画布再透一档。
    /// <para>
    /// 2026-11-12 晚起侧边栏不再铺这一层（用户令「侧边栏和标题栏采用云母 Alt」，栏直接露出窗口材质），
    /// 这颗键留在表里是给框架模板与以后可能再用到它的表面 —— 它与 Palette 的字面值仍要成对，见
    /// <c>MicaMaterialTests</c> 那一条。
    /// </para>
    /// </summary>
    private const byte SidebarLayerAlpha = 0x80;

    /// <summary>
    /// 侧边栏自己那层底（<c>EgSidebarBrush</c>）的浓度：0xD9 ≈ 85%，比内容画布（窗口色 60%）更实一档。
    /// <para>
    /// 2026-11-14 用户令「侧边栏与内容区之间对比度不够高，侧栏加深一层面色」：栏作为一整列铺一层更实的
    /// 面色（<see cref="UiTheme.Colors"/> 的 Surface），和右侧页面之间因此分得开；15% 的 Mica Alt 仍从
    /// 底下透上来。首帧字面值写在 Palette.xaml 的 <c>EgSidebarBrush</c> 上，两处必须一起改 ——
    /// <c>MicaMaterialTests</c> 盯着这对数。
    /// </para>
    /// </summary>
    private const byte SidebarSurfaceAlpha = 0xD9;

    /// <summary>
    /// 设置页卡片的官方卡片层（<c>EgSettingsCardBrush</c>，2026-11-28）：深色 5% 白、浅色 70% 白，
    /// 与 WinUI 主题字典里 <c>CardBackgroundFillColorDefaultBrush</c> 同档。
    /// </summary>
    private const byte CardVeilDarkAlpha = 0x0D;

    private const byte CardVeilLightAlpha = 0xB3;

    /// <summary>
    /// 每个画刷键 → 这套主题里的哪个角色。前二十条是我们自己的角色名，后面那些是框架控件真正读的键。
    /// <para>
    /// 后半截必须在这里再写一遍，不能只覆盖 <c>SystemAccentColor</c>：框架在解析它自己那份字典时就用
    /// StaticResource 把每个派生画刷算完了，那时我们的字典还没并进来。所以一个按钮的底色、一个开关的
    /// 圆点、一条进度条，都得按键逐个换掉。
    /// </para>
    /// </summary>
    private static readonly (string Key, Func<UiTheme, ThemeColor> Pick)[] Brushes =
    [
        ("EgWindowBrush", theme => theme.Colors.Window),
        ("EgSurfaceBrush", theme => theme.Colors.Surface),
        ("EgSurfaceAltBrush", theme => theme.Colors.SurfaceAlt),
        ("EgSurfaceElevatedBrush", theme => theme.Colors.SurfaceElevated),
        ("EgSurfaceHoverBrush", theme => theme.Colors.SurfaceHover),
        ("EgBorderBrush", theme => theme.Colors.Border),
        ("EgBorderStrongBrush", theme => theme.Colors.BorderStrong),
        // 商店模式固定面色：深色下白底高透明，浅色下黑底半透明，确保浅底上仍能辨识按钮边界。
        ("EgStoreControlBrush", theme => theme.IsDark ? White : Black),
        // Dark/Light 下保持卡片和弹窗无边框观感；HighContrast 由 Palette 映射到系统文字色显出 1px 描边。
        ("EgCardStrokeBrush", _ => Black.WithAlpha(0)),
        ("EgTextBrush", theme => theme.Colors.Text),
        ("EgTextDimBrush", theme => theme.Colors.TextDim),
        ("EgTextFaintBrush", theme => theme.Colors.TextFaint),
        // 主题别名仍共享原画刷；登记同一角色使首帧和运行覆盖清单一致，不漏掉卡片文字。
        ("EgCardCaptionForegroundBrush", theme => theme.Colors.Text),
        ("EgCardCaptionSecondaryBrush", theme => theme.Colors.Text),
        ("EgMediaCaptionForegroundBrush", _ => White),
        ("EgMediaCaptionSecondaryBrush", _ => ThemeColor.Rgb(0xC9, 0xD0, 0xD9)),
        ("EgTextOnAccentBrush", theme => theme.Colors.TextOnAccent),
        ("EgAccentBrush", theme => theme.Colors.Accent),
        ("EgAccentHoverBrush", theme => theme.Colors.AccentHover),
        ("EgAccentPressedBrush", theme => theme.Colors.AccentPressed),
        ("EgAccentSoftBrush", theme => theme.Colors.AccentSoft),
        ("EgAccentMutedBrush", theme => theme.Colors.AccentMuted),
        ("EgDangerBrush", theme => theme.Colors.Danger),
        ("EgWarningBrush", theme => theme.Colors.Warning),
        ("EgInfoBrush", theme => theme.Colors.Info),
        ("EgScrimBrush", theme => theme.Colors.Scrim),

        // 主窗口画布（2026-11-12 用户令「改为Mica」）：窗口角色加上浓度，压在本窗口的 Mica 上。
        // 只有 ShellPage.ContentHost 画它；各页面根背景都是 Transparent，整页因此只有一道底。
        ("EgCanvasBrush", theme => theme.Colors.Window.WithAlpha(CanvasAlpha)),

        // 侧边栏那一列自己的底（2026-11-14）：比画布更实一档的面色，栏与页面之间因此分得开。
        ("EgSidebarBrush", theme => theme.Colors.Surface.WithAlpha(SidebarSurfaceAlpha)),

        // 详情页正文那张亚克力（见 Apply 里的亚克力分支）。取窗口底色 ——
        // 它是这一页正文本来的底，亚克力只是让背后那张剧照透上来一点点，不是换一种颜色。
        ("EgFrostBrush", theme => theme.Colors.Window),

        // 详情操作和货架翻页的三态玻璃；tint 与实色回退一起更新，已经打开的模板握住同一个对象。
        ("EgActionGlassBrush", theme => theme.Colors.SurfaceElevated),
        ("EgActionGlassHoverBrush", theme => theme.Colors.SurfaceHover),
        ("EgActionGlassPressedBrush", theme => theme.Colors.SurfaceAlt),

        // 封面播放键的三态玻璃（海报卡与详情页封面）。静止档取窗口底 —— 它是一块压在剧照上的深色玻璃，
        // 不是主题的抬升面；悬停与按下换成强调色，字同时翻成深墨，和实心强调键同一套对比。
        ("EgPlayGlassBrush", theme => theme.Colors.Window),
        ("EgPlayGlassHoverBrush", theme => theme.Colors.Accent),
        ("EgPlayGlassPressedBrush", theme => theme.Colors.AccentPressed),

        // 悬停/按下的叠加层。不取自主题的任何一个角色 —— 它按深浅在白和黑之间翻，因为它要压在
        // *下面那层是什么* 上面，而不是配合某个色相。数字和 HostWindow.PaintCaption 给系统窗口按钮
        // 设的那组一致，标题栏两头才是同一种手感。
        ("EgOverlayHoverBrush", theme => theme.IsDark ? White.WithAlpha(0x23) : Black.WithAlpha(0x19)),
        ("EgOverlayPressedBrush", theme => theme.IsDark ? White.WithAlpha(0x37) : Black.WithAlpha(0x2D)),

        // 标题栏那颗搜索框（2026-11-12）：压在 Mica Alt 上的一层半透明，深浅两边翻白/翻黑，理由同上一对。
        ("EgFieldFillBrush", theme => theme.IsDark ? White.WithAlpha(0x14) : Black.WithAlpha(0x0F)),
        ("EgFieldFillHoverBrush", theme => theme.IsDark ? White.WithAlpha(0x24) : Black.WithAlpha(0x1A)),
        ("EgFieldStrokeBrush", theme => theme.IsDark ? White.WithAlpha(0x1F) : Black.WithAlpha(0x24)),

        // 官方控件面（2026-11-28）：设置页卡片区的输入框与选择框改吃 WinUI 的 ControlFillColor* /
        // ControlStrokeColor* 那一族 —— 半透明的控件表面，压在 Mica 上跟卡片一样会透。
        // 数值就是框架主题字典里那几档：深色 6%/8%/3%/4% 白、7%/9% 白描边；浅色 70%/50%/30% 白、
        // 6%/16% 黑（次级那两档的底色是 #F9F9F9，不是纯白）。
        ("EgControlFillBrush", theme => theme.IsDark ? White.WithAlpha(0x0F) : White.WithAlpha(0xB3)),
        ("EgControlFillSecondaryBrush", theme => theme.IsDark ? White.WithAlpha(0x15) : ThemeColor.Rgb(0xF9, 0xF9, 0xF9).WithAlpha(0x80)),
        ("EgControlFillTertiaryBrush", theme => theme.IsDark ? White.WithAlpha(0x08) : ThemeColor.Rgb(0xF9, 0xF9, 0xF9).WithAlpha(0x4D)),
        ("EgControlFillDisabledBrush", theme => theme.IsDark ? White.WithAlpha(0x0B) : ThemeColor.Rgb(0xF9, 0xF9, 0xF9).WithAlpha(0x4D)),
        ("EgControlStrokeBrush", theme => theme.IsDark ? White.WithAlpha(0x12) : Black.WithAlpha(0x0F)),
        ("EgControlStrokeSecondaryBrush", theme => theme.IsDark ? White.WithAlpha(0x18) : Black.WithAlpha(0x29)),

        // 框架的强调色一族：按钮、开关、滑块、评分、导航栏那条选中指示。
        ("AccentFillColorDefaultBrush", theme => theme.Colors.Accent),
        ("AccentFillColorSecondaryBrush", theme => theme.Colors.Accent.WithAlpha(0xE8)),
        ("AccentFillColorTertiaryBrush", theme => theme.Colors.Accent.WithAlpha(0xC4)),
        ("AccentFillColorDisabledBrush", theme => theme.Colors.Surface.Mix(theme.Colors.Accent, 0.28)),

        // 强调色当字用的时候取悬停那一档：深色主题里它更亮、浅色主题里更暗，两边都是压在面上更读得出来
        // 的那一头。这也是浅色主题下 #52B54B 当链接色只有 2.4:1 那个老问题的答案。
        ("AccentTextFillColorPrimaryBrush", theme => theme.Colors.AccentHover),
        ("AccentTextFillColorSecondaryBrush", theme => theme.Colors.AccentHover),
        ("AccentTextFillColorTertiaryBrush", theme => theme.Colors.Accent),
        ("TextOnAccentFillColorPrimaryBrush", theme => theme.Colors.TextOnAccent),
        ("TextOnAccentFillColorSecondaryBrush", theme => theme.Colors.TextOnAccent.WithAlpha(0xB4)),
        ("TextOnAccentFillColorSelectedTextBrush", theme => theme.Colors.TextOnAccent),

        ("TextFillColorPrimaryBrush", theme => theme.Colors.Text),
        ("TextFillColorSecondaryBrush", theme => theme.Colors.TextDim),
        ("TextFillColorTertiaryBrush", theme => theme.Colors.TextFaint),

        // 侧边栏里的字与两条分区标题（2026-10-13 晚）：都取主题正文色，和标题栏那一排同一支墨。框架那几支
        // 键（条目是写死的白、分区标题是 Secondary 那档暗字、选中态另有一支白）解析得比我们早，覆盖不到，
        // 只能按键逐个换掉。
        ("NavigationViewItemForeground", theme => theme.Colors.Text),
        ("NavigationViewItemForegroundPointerOver", theme => theme.Colors.Text),
        ("NavigationViewItemForegroundPressed", theme => theme.Colors.Text),
        ("NavigationViewItemForegroundSelected", theme => theme.Colors.Text),
        ("NavigationViewItemForegroundSelectedPointerOver", theme => theme.Colors.Text),
        ("NavigationViewItemForegroundSelectedPressed", theme => theme.Colors.Text),
        ("NavigationViewItemHeaderForeground", theme => theme.Colors.Text),

        // 外壳画布/侧栏保留项目半透明层，弹窗使用实色，卡片保留应用内 Acrylic。
        // 浮层仍由 EgFrostBrush 提供材质，底层画布与卡片各用自己的主题角色。
        // 设置卡片（2026-10-29 用户令「所有的卡片也要用亚克力」）：深色取抬两档的悬停面色，
        // 浅色取白面 —— 两边的浓度都归 Palette 的两份字典，这里只管「这套主题该用哪个色」。
        ("LayerFillColorDefaultBrush", theme => theme.Colors.Window),
        ("LayerFillColorAltBrush", theme => theme.Colors.Surface.WithAlpha(SidebarLayerAlpha)),
        ("CardBackgroundFillColorDefaultBrush", theme => theme.Colors.SurfaceAlt),
        ("CardBackgroundFillColorSecondaryBrush", theme => theme.Colors.SurfaceAlt),
        // 设置页卡片取官方 Fluent 卡片层的那两档白：深色 5%、浅色 70%（同 WinUI 的
        // CardBackgroundFillColorDefaultBrush）。它是半透明面，压在 Mica 上能透出壁纸；
        // 「透明效果」关掉时落回同主题的实心面色 —— 与设置页那颗开关的承诺一致。
        ("EgSettingsCardBrush", theme => TransparencyEnabled
            ? White.WithAlpha(theme.IsDark ? CardVeilDarkAlpha : CardVeilLightAlpha)
            : theme.Colors.SurfaceAlt),
        // 全站内容卡的卡片层（2026-11-28：媒体详情页与订阅详情页的正文卡也接这一支）。
        ("EgCardFillBrush", theme => TransparencyEnabled
            ? White.WithAlpha(theme.IsDark ? CardVeilDarkAlpha : CardVeilLightAlpha)
            : theme.Colors.SurfaceAlt),
        ("CardStrokeColorDefaultBrush", theme => theme.Colors.Border),
        ("ControlStrokeColorDefaultBrush", theme => theme.Colors.Border),
        ("ControlStrokeColorSecondaryBrush", theme => theme.Colors.BorderStrong),

        ("SolidBackgroundFillColorBaseBrush", theme => theme.Colors.Window),
        ("SolidBackgroundFillColorSecondaryBrush", theme => theme.Colors.Surface),
        ("SolidBackgroundFillColorTertiaryBrush", theme => theme.Colors.SurfaceAlt),

        // 下拉框浮层的底和边（2026-09-25「复刻 QQ 的选择框」）。底那支是亚克力：TintColor/FallbackColor
        // 按主题的抬升面改画，两个 Opacity 是「透多少」，归 Palette 两份字典按深浅分档 —— 跟 EgFrostBrush
        // 同一个分工。这键不加进表里的话，自检「主题角色覆盖」那条红：调色板里有了，ThemeHost 却不涂。
        ("ComboBoxDropDownBackground", theme => theme.Colors.SurfaceElevated),
        ("ComboBoxDropDownBorderBrush", theme => theme.Colors.Border),

        // 详情页三颗文件选项下拉**闭合态**的底（2026-10-09）。tint 与上面那支同取抬升面，两个 Opacity
        // 归 Palette 两份字典 —— 比浮层那支松一档，闭合态才透得出底下那张压暗的剧照。这键不加进表里
        // 的话，自检「主题角色覆盖」那条红：调色板里有了，ThemeHost 却不涂。
        ("EgPickerGlassBrush", theme => theme.Colors.SurfaceElevated),

        // 与 Palette.xaml 的首帧资源配对；独立表供离线测试编译链接真实选色规则。
        .. InputControlTheme.Brushes
    ];

    /// <summary>
    /// 跟着换的 Color 条目。只有系统强调色这一族 —— 我们自己那些 <c>Eg*Color</c> 只在 Palette.xaml 内部
    /// 被 StaticResource 取过一次，取完画刷就归上面那张表管了。
    /// </summary>
    private static readonly (string Key, Func<UiTheme, ThemeColor> Pick)[] Colors =
    [
        ("SystemAccentColor", theme => theme.Colors.Accent),
        ("SystemAccentColorLight1", theme => theme.Colors.Accent.Mix(White, 0.14)),
        ("SystemAccentColorLight2", theme => theme.Colors.Accent.Mix(White, 0.28)),
        ("SystemAccentColorLight3", theme => theme.Colors.Accent.Mix(White, 0.42)),
        ("SystemAccentColorDark1", theme => theme.Colors.Accent.Mix(Black, 0.14)),
        ("SystemAccentColorDark2", theme => theme.Colors.Accent.Mix(Black, 0.28)),
        ("SystemAccentColorDark3", theme => theme.Colors.Accent.Mix(Black, 0.42))
    ];

    private static readonly List<WeakReference<FrameworkElement>> Roots = [];

    private static SystemThemeWatcher? _systemTheme;

    /// <summary>
    /// 最近一次按设置文件应用的那一份外观。只有它非空时，跟随系统才算「模式跟着系统」——
    /// 探针与 <c>--theme</c> 那种临时换一套色板的调用会把它清空，退出时不该继续替用户跟随。
    /// </summary>
    private static UiSettings? _appearance;

    /// <summary>当前生效的那套。启动时 <see cref="Apply(string?)"/> 会按设置文件把它换掉。</summary>
    public static UiTheme Current { get; private set; } = UiThemes.Default;

    /// <summary>
    /// 透明效果当前是否开着。关掉时整窗画布与侧栏落成实色、亚克力表面走回退色，窗口的 Mica 也一并撤掉。
    /// 窗口那头在 <see cref="Changed"/> 里读这一位重新决定要不要挂底材。
    /// </summary>
    public static bool TransparencyEnabled { get; private set; } = true;

    /// <summary>
    /// 换主题时通知一次。给两个窗口的标题栏用：那两处的颜色是 Win32 的非客户区，画刷改不到它们，
    /// 只能收到消息之后自己再设一遍。
    /// </summary>
    public static event Action<UiTheme>? Changed;

    /// <summary>
    /// 设置文件里存的 id → 生效。认不出来的 id 由 <see cref="UiThemes.Resolve"/> 拨回默认那套。
    /// </summary>
    public static void Apply(string? id) => Apply(UiThemes.Resolve(id));

    /// <summary>
    /// 一套固定的主题（探针、<c>--theme</c>、按 id 恢复）：透明效果沿用当前这一档。
    /// </summary>
    public static void Apply(UiTheme theme) => Apply(theme, TransparencyEnabled);

    /// <summary>
    /// 一套固定的主题，并指定这一次的透明效果（<c>--theme</c> 启动时跟着设置走）。
    /// 会清掉「按设置文件应用」的那一份 —— 临时换肤不该留下继续跟随系统的尾巴。
    /// </summary>
    public static void Apply(UiTheme theme, bool transparencyEnabled)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _appearance = null;
        TransparencyEnabled = transparencyEnabled;
        ApplyCore(theme, theme.FollowsSystem);
    }

    /// <summary>
    /// 设置文件里的整份外观：色板、选择模式与透明效果一起合成。只有选择模式是「跟随系统」时才建立系统观察；
    /// 固定深色或浅色时停掉它。
    /// </summary>
    public static void Apply(UiSettings ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _appearance = ui;

        var mode = UiThemes.NormalizeMode(ui.AppearanceMode);
        var watchSystem = string.Equals(mode, UiThemes.ModeSystem, StringComparison.Ordinal);

        var systemDark = true;
        if (watchSystem)
        {
            _systemTheme ??= new SystemThemeWatcher(UpdateSystemTheme);
            systemDark = _systemTheme.IsDark;
        }

        TransparencyEnabled = ui.TransparencyEffects;
        ApplyCore(UiThemes.Compose(ui.Theme, mode, systemDark), watchSystem);
    }

    private static void ApplyCore(UiTheme theme, bool watchSystem)
    {
        if (watchSystem)
        {
            _systemTheme ??= new SystemThemeWatcher(UpdateSystemTheme);
            // 目录里的「跟随系统」只是一份深色预览；真正要画的深浅在观察器里读。
            if (theme.FollowsSystem)
                theme = UiThemes.System(_systemTheme.IsDark);
        }
        else
            StopSystemWatch();

        ApplyResolved(theme);
    }

    public static void StopSystemWatch()
    {
        _systemTheme?.Dispose();
        _systemTheme = null;
    }

    /// <summary>播放信息保留深色局部角色，不能只固定 ElementTheme 后仍引用被全局涂浅的画刷。</summary>
    internal static void KeepDark(FrameworkElement root)
    {
        SetFollowChanges(root, false);
        root.RequestedTheme = ElementTheme.Dark;
        var theme = Current.IsDark
            ? Current
            : UiThemes.Compose(UiThemes.DefaultId, UiThemes.ModeDark, true);
        var template = Dictionary("Dark");
        var entries = new ResourceDictionary();
        foreach (var (key, pick) in Brushes)
        {
            var color = pick(theme);
            if (template is not null && template.TryGetValue(key, out var found) && found is AcrylicBrush source)
            {
                var tint = ToColor(color);
                entries[key] = new AcrylicBrush
                {
                    TintColor = tint,
                    FallbackColor = tint,
                    TintOpacity = source.TintOpacity,
                    TintLuminosityOpacity = source.TintLuminosityOpacity,
                    Opacity = source.Opacity,
                    AlwaysUseFallback = !TransparencyEnabled
                };
                continue;
            }
            if (!TransparencyEnabled && key is "EgCanvasBrush" or "EgSidebarBrush" or "LayerFillColorAltBrush")
                color = color.WithAlpha(0xFF);
            entries[key] = new SolidColorBrush(ToColor(color));
        }
        foreach (var (key, pick) in Colors)
            entries[key] = ToColor(pick(theme));
        // Only Dark is locally overridden; Windows HighContrast continues to resolve system resources.
        root.Resources.ThemeDictionaries["Dark"] = entries;
    }

    private static void UpdateSystemTheme(bool dark)
    {
        if (_appearance is { } ui)
        {
            // 按设置文件应用时，只有「选择模式」是跟随系统才重算；固定深浅不理会 Windows 的变化。
            if (string.Equals(UiThemes.NormalizeMode(ui.AppearanceMode), UiThemes.ModeSystem, StringComparison.Ordinal))
                ApplyResolved(UiThemes.Compose(ui.Theme, UiThemes.ModeSystem, dark));
            return;
        }

        if (Current.FollowsSystem && Current.IsDark != dark)
            ApplyResolved(UiThemes.System(dark));
    }

    private static void ApplyResolved(UiTheme theme)
    {
        Current = theme;

        foreach (var name in Painted)
        {
            var entries = Dictionary(name);
            if (entries is null) continue;

            foreach (var (key, pick) in Brushes)
            {
                if (!entries.TryGetValue(key, out var found)) continue;

                switch (found)
                {
                    case SolidColorBrush brush:
                        var color = pick(theme);
                        // 透明效果关掉时，整窗画布、侧栏与框架那层栏底直接落成实色；浓度仍写在上面那张表里。
                        if (!TransparencyEnabled && key is "EgCanvasBrush" or "EgSidebarBrush" or "LayerFillColorAltBrush")
                            color = color.WithAlpha(0xFF);
                        brush.Color = ToColor(color);
                        break;

                    // 亚克力（EgFrostBrush）：改的是它的两个颜色，两个 Opacity 留给调色板 —— 那两个数是
                    // 「透多少」，按深浅分两档写在两份字典里，和这套主题是哪个色相无关。FallbackColor 也得
                    // 改：关掉透明效果时屏上就剩它，停在上一套主题的窗口底色上会是一整块不合的颜色。
                    // AlwaysUseFallback 跟着设置页的「透明效果」走：关掉时每一支亚克力都直接画回退色。
                    case AcrylicBrush acrylic:
                        acrylic.TintColor = ToColor(pick(theme));
                        acrylic.FallbackColor = ToColor(pick(theme));
                        acrylic.AlwaysUseFallback = !TransparencyEnabled;
                        break;
                }
            }

            // Color 条目没法就地改（结构体），只能换掉。改这些不为了当场刷新 —— 界面上那些引用早在解析
            // 时就取过值了 —— 而是为了之后任何一次重新解析（比如 ThemeResource 在树翻主题时重算）拿到的
            // 是这套主题的绿，而不是上一套留下的。
            foreach (var (key, pick) in Colors)
                entries[key] = ToColor(pick(theme));
        }

        var element = theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        for (var index = Roots.Count - 1; index >= 0; index--)
        {
            if (Roots[index].TryGetTarget(out var root))
                root.RequestedTheme = element;
            else
                Roots.RemoveAt(index);   // 页面早关了，弱引用是为了不把它们钉在内存里
        }

        Changed?.Invoke(theme);
    }

    /// <summary>
    /// 让一棵元素树跟着主题的深浅翻过去。框架自己那几百个没被 Palette.xaml 覆盖的刷子只认
    /// <c>ElementTheme</c>，所以浅色主题下没登记过的树会是「浅色的面配框架的深色控件」。
    /// </summary>
    public static void Register(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var existing in Roots)
            if (existing.TryGetTarget(out var already) && ReferenceEquals(already, root))
                return;

        Roots.Add(new WeakReference<FrameworkElement>(root));
        root.RequestedTheme = Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
    }

    public static Color ToColor(ThemeColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>自检用：这张表覆盖了哪些键。少覆盖一个，界面上就会留一块换不掉的颜色。</summary>
    public static IReadOnlyList<string> BrushKeys { get; } = Brushes.Select(entry => entry.Key).ToArray();

    /// <summary>
    /// 此别名的 Color 三主题都用 ThemeResource 接系统角色，不能由 Apply 改成项目色。
    /// 只核明确声明且实际三字典仍有的原生画刷；XAML 动态表达式由对应源码契约验证。
    /// </summary>
    internal static bool IsSystemManagedBrush(string key) => SystemManagedThemeRole.IsManaged(key, (theme, name) =>
        Dictionary(theme) is { } entries && entries.TryGetValue(name, out var value) && value is SolidColorBrush);

    /// <summary>
    /// 自检用：读回某个字典里某个键现在是什么颜色。亚克力那一支报的是它的 <c>TintColor</c> —— 一张亚克力
    /// 没有「一个颜色」，而这里问的是「<see cref="Apply"/> 有没有把这套主题的色写进去」，答的就是那一个。
    /// </summary>
    public static Color? ColorOf(string theme, string key) =>
        Dictionary(theme) is { } entries && entries.TryGetValue(key, out var found)
            ? found switch
            {
                SolidColorBrush brush => brush.Color,
                AcrylicBrush acrylic => acrylic.TintColor,
                _ => null
            }
            : null;

    private static ResourceDictionary? Dictionary(string theme)
    {
        if (Application.Current is null) return null;
        foreach (var merged in Application.Current.Resources.MergedDictionaries)
        {
            // 按 Source 认自己那份：XamlControlsResources 就并在旁边，它也带 Dark 和 Light。
            if (merged.Source is null ||
                !merged.Source.ToString().EndsWith("Palette.xaml", StringComparison.OrdinalIgnoreCase))
                continue;

            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary entries)
                return entries;
        }

        return null;
    }
}
