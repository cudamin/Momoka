using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>
/// 每态固定一支材质画刷：现有底色走精确实色回退，亚克力走原生材质。文字、图标、边线与弹层沿用原有状态。
/// 框架 2.3.9 的 DefaultButtonStyle/DefaultToggleButtonStyle 在三种状态分别读取这些资源键，
/// 所以必须提供整组三态，不能只给 Background 填一支刷子。
/// <para>
/// 两种材质各有一套参数（2026-11-07 用户令「背景图和按钮新的亚克力改为三项可调，去掉高斯模糊和颜色遮罩」）：
/// 半透明叠层只有底色和它的不透明度；亚克力就是 <c>AcrylicBrush</c> 自己的三项 —— TintColor、TintOpacity、
/// TintLuminosityOpacity。不再有第四格「整体浓度」：那一个是整支画笔的 <c>Opacity</c>，不属于亚克力参数。
/// </para>
/// <para>
/// 详情按钮与闭合选择框共用这套材质；主页轮播那颗「详情」按钮仍按原来的底色浓度走，不吃这三项。
/// </para>
/// <para>
/// 第三条路是 <see cref="ApplyFixedSurface"/>：简洁模式不铺背景图、底是系统云母，按钮与闭合选择框改吃
/// 一支固定的半透明面色（四态各一个定值浓度），不参与上面两种材质的任何设置（2026-11-11 用户令）。
/// </para>
/// </summary>
internal sealed class ControlBackgroundBrushes
{
    // Extra 是这一态在底色／染色浓度上多推的份额：悬停 6 个百分点、按下 12，禁用不推。
    private readonly List<(SolidColorBrush Source, SolidColorBrush Target, AcrylicBrush Copy, bool Feedback, double Extra)> _brushes = [];
    private readonly bool _preserveDefault;
    private readonly bool _lightSurface;
    internal Control Control { get; }
    private readonly Dictionary<string, SolidColorBrush> _foregrounds = [];

    internal void ApplyDefaultForeground(Windows.UI.Color? forced = null)
    {
        var color = forced ?? Resource("EgDetailActionTextBrush").Color;
        var prefix = Control is ComboBox ? "ComboBox" : Control is ToggleButton ? "ToggleButton" : "Button";
        foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
            ForegroundBrush(prefix + "Foreground" + suffix).Color = color;
        foreach (var ink in _foregrounds.Values) ink.Color = color;
        Control.Foreground = _foregrounds[prefix + "Foreground"];
    }

    internal void ApplyForeground(ThemeColor background, bool materialFallback, Windows.UI.Color? forced = null)
    {
        var prefix = Control is ComboBox ? "ComboBox" : Control is ToggleButton ? "ToggleButton" : "Button";
        foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
        {
            var key = prefix + "Foreground" + suffix;
            var ink = ForegroundBrush(key);
            var fill = (AcrylicBrush)Control.Resources[prefix + "Background" + suffix];
            var color = fill.FallbackColor;
            var amount = !fill.AlwaysUseFallback && materialFallback ? 1
                : fill.AlwaysUseFallback ? fill.Opacity * color.A / 255d : fill.TintOpacity;
            var surface = background.Mix(ThemeColor.Rgb(color.R, color.G, color.B), amount);
            ink.Color = forced ?? ToColor(BackdropReading.Foreground(surface));
            if (suffix.Length == 0) Control.Foreground = ink;
            if (Control is DetailFilePicker)
                ForegroundBrush("DropDownButtonForegroundSecondary" + suffix).Color = ink.Color;
        }
        if (Control is ComboBox)
        {
            var ink = _foregrounds[prefix + "Foreground"];
            foreach (var (key, copy) in _foregrounds)
                if (key.Contains("Focused", StringComparison.Ordinal) || key.Contains("Glyph", StringComparison.Ordinal)) copy.Color = ink.Color;
        }
    }

    private SolidColorBrush ForegroundBrush(string key)
    {
        if (_foregrounds.TryGetValue(key, out var ink)) return ink;
        var initial = (Control.Foreground as SolidColorBrush)?.Color ?? ToColor(BackdropReading.Foreground(ThemeColor.Rgb(0, 0, 0)));
        ink = new SolidColorBrush { Color = initial };
        _foregrounds.Add(key, ink);
        Control.Resources[key] = ink;
        return ink;
    }

    private static Windows.UI.Color ToColor(ThemeColor color) => new() { A = color.A, R = color.R, G = color.G, B = color.B };

    private ControlBackgroundBrushes(Control control, string prefix,
        SolidColorBrush normal, SolidColorBrush hover, SolidColorBrush pressed, SolidColorBrush disabled,
        bool preserveDefault = false, bool lightSurface = false)
    {
        Control = control;
        _preserveDefault = preserveDefault;
        _lightSurface = lightSurface;
        var rest = Copy(normal, lightSurface ? "EgDetailActionBrush" : "EgSurfaceElevatedBrush", false, 0);
        var over = Copy(hover, lightSurface ? "EgDetailActionHoverBrush" : "EgSurfaceHoverBrush", true, 0.06);
        var down = Copy(pressed, lightSurface ? "EgDetailActionPressedBrush" : "EgSurfaceAltBrush", true, 0.12);
        var off = Copy(disabled, "EgSurfaceElevatedBrush", false, 0);
        // XAML 不声明下面这些键；此处仅在构造时新增一次，以后只修改画刷。
        // WinUI 的别名重写按对象移除，可能报 0x800F0902；资源枚举也不能作为别名复制依据。
        // 保留原字典与所有前景资源，避免重写、移除或枚举已经存在的别名。
        var resources = control.Resources;
        resources[prefix + "Background"] = rest;
        resources[prefix + "BackgroundPointerOver"] = over;
        resources[prefix + "BackgroundPressed"] = down;
        resources[prefix + "BackgroundDisabled"] = off;
        if (control is ComboBox)
        {
            resources[prefix + "BackgroundFocused"] = rest;
            resources[prefix + "BackgroundUnfocused"] = rest;
            // The ComboBox template captures these ThemeResource brushes before Loaded. Inserting
            // them during ApplyForeground is too late for AnimatedIcon/FallbackIconSource; seed the
            // independent objects here and only change their colors later. Popup rows keep their keys.
            foreach (var key in new[] { "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundPressed",
                "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxDropDownGlyphForeground",
                "ComboBoxDropDownGlyphForegroundFocused", "ComboBoxDropDownGlyphForegroundFocusedPressed" })
                ForegroundBrush(key);
        }
        // WinUI 2.3.9 DefaultDropDownButtonStyle 的 ChevronIcon 独立读取次要前景。
        // 模板套用前准备对象，之后与每个 ButtonForeground 状态同步改色。
        if (control is DetailFilePicker)
            foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
                ForegroundBrush("DropDownButtonForegroundSecondary" + suffix);
        if (control is ToggleButton)
        {
            resources[prefix + "BackgroundChecked"] = rest;
            resources[prefix + "BackgroundCheckedPointerOver"] = over;
            resources[prefix + "BackgroundCheckedPressed"] = down;
            resources[prefix + "BackgroundCheckedDisabled"] = off;
        }
        control.Background = rest;
    }

    public static ControlBackgroundBrushes ForDetail(Control control, bool preserveDefault = false, bool lightSurface = false)
    {
        var prefix = control is ComboBox ? "ComboBox" : control is ToggleButton ? "ToggleButton" : "Button";
        var source = control is ToggleButton ? "ToggleButton" : "Button";
        return new(control, prefix, Resource(lightSurface ? "EgDetailActionBrush" : source + "Background"),
            Resource(lightSurface ? "EgDetailActionHoverBrush" : source + "BackgroundPointerOver"),
            Resource(lightSurface ? "EgDetailActionPressedBrush" : source + "BackgroundPressed"),
            Resource(source + "BackgroundDisabled"), preserveDefault, lightSurface);
    }

    public static ControlBackgroundBrushes ForBanner(Button button, ResourceDictionary resources) =>
        new(button, "Button", (SolidColorBrush)resources["EgBannerGhostRestBrush"],
            (SolidColorBrush)resources["EgBannerGhostHoverBrush"], (SolidColorBrush)resources["EgBannerGhostPressBrush"],
            Resource("ButtonBackgroundDisabled"));

    /// <summary>
    /// 按当前设置重画三态（外加选中／禁用）的底色。<paramref name="percent"/> 是半透明叠层的底色不透明度；
    /// <paramref name="tintOpacity"/> 与 <paramref name="luminosityOpacity"/> 是亚克力的两项浓度（0–1），
    /// 只在 <paramref name="useAcrylic"/> 为真时生效。<paramref name="tint"/> 是那颗按钮的颜色，两种材质共用。
    /// </summary>
    public void Apply(double percent, bool useAcrylic = false, Windows.UI.Color? tint = null,
        double tintOpacity = 0.55, double luminosityOpacity = 0.55)
    {
        var amount = AppearanceOpacity.ControlBackground(percent);
        foreach (var (source, target, copy, feedback, extra) in _brushes)
        {
            if (_lightSurface && useAcrylic)
            {
                // 三项就是亚克力的全部可调项：色相来自 tint，染色与明暗各一档。指针反馈不另叠一层、
                // 也不动整支画笔的 Opacity，只把染色浓度往上推几个百分点 —— 材质的颜色和明暗仍是用户选的那一套。
                var acrylicInk = tint ?? target.Color;
                acrylicInk.A = byte.MaxValue;
                copy.TintColor = acrylicInk;
                copy.FallbackColor = acrylicInk;
                copy.TintOpacity = Math.Clamp(tintOpacity + extra, 0, 1);
                copy.TintLuminosityOpacity = Math.Clamp(luminosityOpacity, 0, 1);
                copy.Opacity = 1;
                // 系统关掉透明效果（省电、远程桌面）时平台自己落到上面那块不透明的 FallbackColor。
                copy.AlwaysUseFallback = false;
                continue;
            }
            if (_lightSurface)
            {
                // 常态是滑块直接读数；默认以下保留悬停/按下反馈，默认以上各态连续补到满档。
                // 不用「默认值相等」特判，邻近小数档、导入值与系统实底也走同一条曲线。
                var normal = Resource("EgDetailActionBrush").Color.A / 255d;
                var feedbackFloor = source.Color.A / 255d;
                var strength = !feedback ? amount : amount <= normal ? feedbackFloor
                    : feedbackFloor + (1 - feedbackFloor) * (amount - normal) / (1 - normal);
                var acrylicInk = tint ?? target.Color;
                acrylicInk.A = byte.MaxValue;
                copy.TintColor = acrylicInk;
                copy.FallbackColor = acrylicInk;
                copy.TintOpacity = strength;
                copy.TintLuminosityOpacity = 0;
                copy.Opacity = strength;
                copy.AlwaysUseFallback = true;
                continue;
            }
            if (amount == 0 && !_preserveDefault)
            {
                var clear = source.Color;
                clear.A = 0;
                copy.FallbackColor = feedback ? source.Color : clear;
                copy.Opacity = feedback ? source.Opacity : 0;
                copy.AlwaysUseFallback = true;
                continue;
            }
            if (useAcrylic && amount < 1)
            {
                // 主页那颗「详情」按钮保持 0.2.0 的算法：浓度就是整支画笔的不透明度，亚克力只换材质不改形状。
                // 2026-11-07 用户把三项染色参数限定在媒体详情页，这一支没有跟着换。
                var ink = tint ?? target.Color;
                ink.A = byte.MaxValue;
                copy.TintColor = ink;
                copy.FallbackColor = ink;
                copy.TintOpacity = amount;
                copy.TintLuminosityOpacity = amount;
                copy.Opacity = 1;
                copy.AlwaysUseFallback = false;
                continue;
            }
            var color = source.Color;
            if (tint is { } chosen)
            {
                chosen.A = color.A;
                color = chosen;
            }
            // 原底色那一层薄白的有效浓度 —— 悬停与按下在它以下时仍然是那层白，不是一块淡掉的深色板。
            var floor = feedback || _preserveDefault ? color.A / 255d * source.Opacity : 0;
            // 详情操作键恢复 v0.2.1 的平台半透明底；用户提高浓度时继续走现有表面色，亚克力仍由上方分支处理。
            if (_preserveDefault && amount <= floor)
            {
                copy.FallbackColor = color;
                copy.Opacity = source.Opacity;
                copy.AlwaysUseFallback = true;
                continue;
            }
            if (amount == 0)
            {
                // 0 档常态没有底色。悬停与按下的副本仍留着原来那层薄白：指到按钮上要看得见反应。
                var clear = color;
                clear.A = 0;
                copy.FallbackColor = feedback ? color : clear;
                copy.Opacity = feedback ? source.Opacity : 0;
                copy.AlwaysUseFallback = true;
                continue;
            }
            if (amount <= floor)
            {
                // 滑块还没爬过原底色那一档：颜色和浓度照旧，免得悬停手感在零档附近忽然断掉。
                copy.FallbackColor = color;
                copy.Opacity = source.Opacity;
                copy.AlwaysUseFallback = true;
                continue;
            }
            // 底色就是主题那一档表面色，浓度直接等于百分比。旧版把原底色的 alpha 当起点往上加，
            // 于是 1% 一上来就是一层薄白、读数说不清；2026-11-06 起读数就是屏上的浓度。
            var surface = tint ?? target.Color;
            surface.A = byte.MaxValue;
            copy.FallbackColor = surface;
            copy.Opacity = amount;
            copy.AlwaysUseFallback = true;
            if (amount == 1)
            {
                // 极近 100 的小数档可能已经被平台按 alpha 舍入成实底；先跨过不透明状态，
                // 再落到完全不透明，确保结束平台尚在执行的 fallback 过渡。
                copy.TintOpacity = 0;
                copy.TintColor = surface;
                copy.TintLuminosityOpacity = 1;
                copy.TintOpacity = 1;
            }
        }
    }

    /// <summary>
    /// 简洁模式的固定按钮材质（2026-11-11 用户令「重新设计简洁模式下按钮与选择框的颜色与材质样式，
    /// 使其为固定值，不再跟随沉浸模式的设置变化」；2026-11-15 用户令「把这些按钮换成浅色高透明背景」）。
    /// <para>
    /// 颜色取 <c>EgStoreControlBrush</c> 的白（浅色），四态各铺一个定值浓度（常态 12%、悬停 20%、按下 8%、
    /// 禁用 5%）：高透明，压在商店的取色环境底上是一层亮而看得见的薄片，不再是一块比底更暗的板。
    /// 换主题不改这一档 —— 浅色是固定的；按钮材质那四项设置也<b>一个字节都不读</b>。
    /// </para>
    /// <para>
    /// 这一档<b>故意不再用亚克力</b>：简洁模式的底是页面局部的系统云母（<c>MicaBackdrop</c>），那层东西不在
    /// XAML 树里，应用内亚克力采不到它 —— 同一支玻璃在云母上会落成一块比页面更暗的板。这里改用可预期的
    /// 半透明面色。
    /// </para>
    /// <para>
    /// 四条顺序就是构造时的顺序：常态、悬停、按下、禁用。
    /// </para>
    /// </summary>
    public void ApplyFixedSurface()
    {
        var surface = Resource("EgStoreControlBrush").Color;
        var levels = new[] { FixedRest, FixedHover, FixedPressed, FixedDisabled };
        for (var index = 0; index < _brushes.Count; index++)
        {
            var (_, _, copy, _, _) = _brushes[index];
            copy.TintColor = surface;
            copy.FallbackColor = surface;
            copy.TintOpacity = 0;
            copy.TintLuminosityOpacity = 0;
            copy.Opacity = levels[index];
            copy.AlwaysUseFallback = true;
        }
    }

    /// <summary>简洁模式固定材质的四态浓度：常态、悬停、按下、禁用。见 <see cref="ApplyFixedSurface"/>。</summary>
    public const double FixedRest = 0.12;

    public const double FixedHover = 0.20;

    public const double FixedPressed = 0.08;

    public const double FixedDisabled = 0.05;

    /// <summary>
    /// 商店模式那颗主播放键的主题色面（2026-11-14 用户令「播放按钮改成主题色」）：常态取强调色实心，
    /// 悬停、按下各取主题里已经定好的亮暗一档，禁用取弱化档。其余按钮仍走
    /// <see cref="ApplyFixedSurface"/> 的固定面色，只有播放键例外。
    /// <para>
    /// 前景不在这里：字和图标由调用方用 <see cref="ApplyDefaultForeground"/> 换成
    /// <c>EgTextOnAccentBrush</c>（实心强调面配的那支对比色），和主题里别的实心强调键同一套。
    /// </para>
    /// </summary>
    public void ApplyAccentSurface()
    {
        var levels = new[] { "EgAccentBrush", "EgAccentHoverBrush", "EgAccentPressedBrush", "EgAccentMutedBrush" };
        for (var index = 0; index < _brushes.Count; index++)
        {
            var (_, _, copy, _, _) = _brushes[index];
            var color = Resource(levels[index]).Color;
            copy.TintColor = color;
            copy.FallbackColor = color;
            copy.TintOpacity = 0;
            copy.TintLuminosityOpacity = 0;
            copy.Opacity = 1;
            copy.AlwaysUseFallback = true;
        }
    }

    private AcrylicBrush Copy(SolidColorBrush source, string targetKey, bool feedback, double extra)
    {
        var copy = new AcrylicBrush
        {
            FallbackColor = source.Color,
            Opacity = source.Opacity,
            AlwaysUseFallback = true,
            TintTransitionDuration = TimeSpan.Zero
        };
        _brushes.Add((source, Resource(targetKey), copy, feedback, extra));
        return copy;
    }

    private static SolidColorBrush Resource(string key) => (SolidColorBrush)Application.Current.Resources[key];

}
