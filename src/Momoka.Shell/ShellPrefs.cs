using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Theming;

namespace Momoka.Shell;

/// <summary>
/// 界面那一组设置改完当场生效的那一根线：图片缓存上限、主页版面（次序和勾选）、主页轮播开关。
/// <para>
/// 侧边栏单独通知外壳，避免一次纯布局切换让主页重建货架、重新查询。导入和恢复默认仍通知所有订阅方。
/// </para>
/// 存在的理由是设置页在它自己那个窗口里（<c>SettingsWindow</c>）。它手上有设置文档，可是没有主窗口的 HWND，
/// 也没有外壳那一页 —— 而这些开关要动的正是那两样。<see cref="ThemeHost.Changed"/> 早就是这个形状了：
/// 一个静态事件，改设置的那一头喊一声，屏幕上那几头各自跟上。这里照抄，不新发明。
/// </para>
/// <para>
/// 事件带的是整份 <see cref="UiSettings"/> 而不是「哪个开关变了」：订阅方各读自己关心的那一条，于是加第四个
/// 开关不用改这个文件，也不用改别的订阅方。设置文档本来就是各处共用的那一份对象，传它不多一次拷贝。
/// </para>
/// <para>
/// 静态事件就要退订，订阅方各自负责：主页在 <c>Unloaded</c> 里退（每次导航都是新实例），外壳那一页和
/// <c>App</c> 不退，因为它们活得和进程一样久。
/// </para>
/// </summary>
public static class ShellPrefs
{
    /// <summary>界面那一组设置改了。参数是改完之后的那一份。</summary>
    public static event Action<UiSettings>? Changed;

    public static event Action<UiSettings>? SidebarChanged;

    public static void ApplySidebar(UiSettings ui) => SidebarChanged?.Invoke(ui);

    /// <summary>按钮底色、背景图和沉浸模式只更新画面，不触发主页重新查询。</summary>
    public static event Action<UiSettings>? AppearanceChanged;

    public static void ApplyAppearance(UiSettings ui) => AppearanceChanged?.Invoke(ui);

    // 草稿只属于打开拾色器的设置对象，不进入 AppSettings，也不会随别的设置保存。
    private static UiSettings? _controlPreviewOwner;
    private static string? _controlPreview;
    private static UiSettings? _backdropPreviewOwner;
    private static string? _backdropPreview;

    public static ThemeColor AppearanceColor(bool backdrop, UiSettings ui)
    {
        var preview = backdrop ? _backdropPreview : _controlPreview;
        var owner = backdrop ? _backdropPreviewOwner : _controlPreviewOwner;
        var value = ReferenceEquals(owner, ui) && preview is not null
            ? preview : backdrop ? ui.BackdropColor : ui.ControlBackgroundColor;
        return ThemeColor.Parse(HtmlColor.TryParse(value, out var rgb) ? HtmlColor.Format(rgb)
            : backdrop ? UiSettings.DefaultBackdropColor : UiSettings.DefaultControlBackgroundColor);
    }

    public static void SetAppearanceColorPreview(bool backdrop, string? color, UiSettings ui)
    {
        var value = color is not null && HtmlColor.TryParse(color, out var rgb) ? HtmlColor.Format(rgb) : null;
        if (backdrop)
        {
            if (value is null && !ReferenceEquals(_backdropPreviewOwner, ui)) return;
            _backdropPreviewOwner = value is null ? null : ui;
            _backdropPreview = value;
        }
        else
        {
            if (value is null && !ReferenceEquals(_controlPreviewOwner, ui)) return;
            _controlPreviewOwner = value is null ? null : ui;
            _controlPreview = value;
        }
        ApplyAppearance(ui);
    }


    public static event Action<UiSettings>? DetailLayoutChanged;

    public static void ApplyDetailLayout(UiSettings ui) => DetailLayoutChanged?.Invoke(ui);

    /// <summary>
    /// 「隐藏没有封面的演职人员」改了：详情页那一排要按新值重排，不重建整页、也不重新读条目。
    /// <para>
    /// 单独一根线而不是搭 <see cref="DetailLayoutChanged"/> 的车：那一根说的是「哪几块卡片、什么次序」，
    /// 订阅方收到就去重排版面；这一根只换演职人员那一排里的人。搭车也错不了（重排里带一句值没变就退出的
    /// 守卫），可下一处照着搭车的地方就分不清哪根线是干什么的了。
    /// </para>
    /// </summary>
    public static event Action<UiSettings>? DetailCastChanged;

    public static void ApplyDetailCast(UiSettings ui) => DetailCastChanged?.Invoke(ui);

    public static event Action? ShortcutsChanged;

    public static void ApplyShortcuts() => ShortcutsChanged?.Invoke();

    public static event Action? DanmakuChanged;

    public static void ApplyDanmaku() => DanmakuChanged?.Invoke();

    public static event Action? SubtitleTranslationChanged;

    public static void ApplySubtitleTranslation() => SubtitleTranslationChanged?.Invoke();

    /// <summary>由设置页那一头喊：这一份刚改过，请各位跟上。</summary>
    public static void Apply(UiSettings ui)
    {
        Views.HomeMotion.ApplyPreference(ui.AnimationsEnabled);
        ApplySidebar(ui);
        ApplyAppearance(ui);
        ApplyDetailLayout(ui);
        ApplyDetailCast(ui);
        Changed?.Invoke(ui);
    }
}
