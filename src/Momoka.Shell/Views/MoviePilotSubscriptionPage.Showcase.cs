using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Windows.UI;

namespace Momoka.Shell.Views;

/// <summary>
/// 订阅详情页的两种显示模式（2026-11-03 用户令「给 mp 订阅页面也增强大窗模式」；2026-11-09 用户令
/// 「给 MP 订阅也新增沉浸模式还有简洁模式」把它并进设置里那条显示模式）。
/// <para>
/// 沉浸档的判据与电影／剧／季详情页同一套（<see cref="DetailHero.SubscriptionImmersive"/>）：显示模式是沉浸、
/// 资料卡能并排的宽版式、背后有那张图。成立时正文那张不透明的纸换成尾部那道半透明洗色，面板的底与框淡到 0，
/// 头部不再撑第一屏，下方组件整体上移、直接压在固定的剧照上；背景图从页顶铺到窗口下沿并略微更不透明，图下沿
/// 的黑色渐变收起。关掉就是简洁模式：整张背景图收起，头部收成一张卡面（<c>ApplyHeroSurface</c>），正文纸面与
/// 那几块面板照旧。
/// </para>
/// <para>
/// 翻面只有两条来路 —— 设置里改显示模式，以及窗口宽度跨过宽窄断点 —— 两条都是「换了一整套版面」，所以当场落定，
/// 不插过渡：从前那套窗口几何（最大化／占工作区五分之四／图快被裁）连同它 180ms 的等待与 260ms 的 Storyboard
/// 一起退役了（判据收进开关之后没有第三条来路能翻面）。
/// </para>
/// </summary>
public sealed partial class MoviePilotSubscriptionPage
{
    /// <summary>正文那张纸的底色，构造函数里换成这一支（沉浸档要把它淡成半透明洗色，不能改全局画刷）。</summary>
    private readonly SolidColorBrush _sheetBrush = new();

    /// <summary>正文四块面板的底与框 —— 沉浸档里淡到 0。</summary>
    private readonly AcrylicBrush _panelFill = new();
    private readonly SolidColorBrush _panelLine = new();

    /// <summary>
    /// 四块正文面板，从上往下：季、演职员、相关推荐、相似作品。它们在标记里都挂着
    /// <c>EgBodyPanelStyle</c>，这里把底色和描边换成自己持有的画刷，好让透明度能跟着模式翻。
    /// </summary>
    private IEnumerable<StackPanel> BodyPanels => [SeasonPanel, PeoplePanel, RecommendationsPanel, SimilarPanel];

    /// <summary>
    /// 构造时准备这几支画刷，并给背景图定下常态那一档不透明度。都在 <c>InitializeComponent</c> 之后、上树之前
    /// 做完 —— 第一帧就是对的。
    /// </summary>
    private void PrepareShowcase()
    {
        _sheetBrush.Color = PaperColor;
        BodySheet.Background = _sheetBrush;
        DetailBackdropBand.Opacity = DetailHero.BackdropOpacity;

        // 监听本页的 ThemeResource 替换，覆盖系统高对比度；不订阅需要 CoreWindow 的系统事件。
        CardMaterialSource.RegisterPropertyChangedCallback(Border.BackgroundProperty, (_, _) => RefreshPanelBrushes());
        RefreshPanelBrushes();
        foreach (var panel in BodyPanels)
        {
            panel.Background = _panelFill;
            panel.BorderBrush = _panelLine;
        }
    }

    /// <summary>
    /// 这一页上树／离树时挂上与摘下主题订阅。与详情页同一支写法：页面实例可能被复用，挂一次、走时解掉；
    /// 先减再加，重复上树也只挂一支。
    /// </summary>
    private void WatchShowcaseTheme()
    {
        Loaded += (_, _) =>
        {
            ThemeHost.Changed -= OnShowcaseThemeChanged;
            ThemeHost.Changed += OnShowcaseThemeChanged;
        };
        Unloaded += (_, _) => ThemeHost.Changed -= OnShowcaseThemeChanged;
    }

    /// <summary>换了主题：面板那两档颜色跟着换；纸面在还原档也要换（沉浸档它一直是尾部那道半透明黑，与主题无关）。</summary>
    private void OnShowcaseThemeChanged(Momoka.Theming.UiTheme theme)
    {
        RefreshPanelBrushes();
        if (!MediaViewModel.Showcase) _sheetBrush.Color = PaperColor;
    }

    private void RefreshPanelBrushes()
    {
        if (CardMaterialSource.Background is AcrylicBrush template)
        {
            _panelFill.AlwaysUseFallback = template.AlwaysUseFallback;
            _panelFill.TintColor = template.TintColor;
            _panelFill.FallbackColor = template.FallbackColor;
            _panelFill.TintOpacity = template.TintOpacity;
            _panelFill.TintLuminosityOpacity = template.TintLuminosityOpacity;
        }
        else if (CardMaterialSource.Background is SolidColorBrush contrast)
        {
            _panelFill.AlwaysUseFallback = true;
            _panelFill.FallbackColor = contrast.Color;
        }
        _panelLine.Color = BrushColor("EgBorderBrush");
    }

    /// <summary>主题里那支窗口色 —— 正文那张纸原本的底。</summary>
    private static Color PaperColor => BrushColor("EgWindowBrush");

    private static Color BrushColor(string key) => Application.Current is { } app
        && app.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush
        ? brush.Color
        : key switch
        {
            "EgWindowBrush" => Color.FromArgb(255, 0x0C, 0x11, 0x16),
            "EgSurfaceAltBrush" => Color.FromArgb(255, 0x1C, 0x25, 0x2E),
            _ => Color.FromArgb(255, 0x30, 0x39, 0x41)
        };

    /// <summary>
    /// 沉浸档的纸面用它 —— 与图下沿那道罩子最浓的一档同色（<see cref="DetailHero.ScrimCeiling"/>），
    /// 正文和上方那一段因此接得上，接缝看不出来。
    /// </summary>
    private static Color WashColor => Color.FromArgb(
        DetailHero.ScrimCeiling, DetailHero.ScrimInk.R, DetailHero.ScrimInk.G, DetailHero.ScrimInk.B);

    /// <summary>
    /// 视图模型里的 <c>Showcase</c>／<c>Plain</c> 从任何一条来路翻面（设置开关、宽窄版式跨断点、背景图到手）：
    /// 当场落定。两种模式之间换的是整套版面，插过渡反而像一次卡顿。
    /// </summary>
    private void OnShowcaseChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MoviePilotMediaViewModel.Showcase) or nameof(MoviePilotMediaViewModel.Plain))
            SettleShowcase();
    }

    /// <summary>
    /// 落定：纸面色、面板透明度、背景图不透明度直接写终值。头部下限与背景盒高绑在视图模型上，自己会跟上。
    /// </summary>
    private void SettleShowcase()
    {
        var showcase = MediaViewModel.Showcase;
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        _panelFill.Opacity = showcase ? 0 : 1;
        _panelLine.Opacity = showcase ? 0 : 1;
        DetailBackdropBand.Opacity = showcase
            ? DetailHero.ShowcaseBackdropOpacity
            : DetailHero.BackdropOpacity;
    }
}
