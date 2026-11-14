using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Windows.UI;

namespace Momoka.Shell.Views;

/// <summary>沉浸与无背景卡面之间的画刷终值；窄窗保留自己的紧凑排版。</summary>
public sealed partial class DetailPage
{
    /// <summary>
    /// 正文那张纸的底色。这一份是这一页自己的，不是全局那支 <c>EgWindowBrush</c>：沉浸档要把它淡成尾部那道
    /// 半透明黑（<see cref="DetailHero.ScrimCeiling"/> 那一档），改全局画刷会把别的页面一起改掉。
    /// </summary>
    private readonly SolidColorBrush _sheetBrush = new();

    /// <summary>
    /// 商店模式 —— 设置里那条「显示模式」关掉沉浸之后这一页的样子：保留卡面排版，顶部装饰背景单独渐隐到外壳画布。
    /// <para>
    /// 名字里不再带 Mica：2026-11-12 用户令「把媒体详情页的简洁模式也改成跟主页一样的」之后，这一页不再挂自己的
    /// 局部材质（从前那块铺满整页的 Mica Alt 把外壳的「Base ＋ 60% 画布」盖掉了），剩下的判断只是「哪几类页面
    /// 走这一档」—— 电影、剧、季、单集，四类都听这一格。
    /// </para>
    /// </summary>
    private bool PlainMode => !ViewModel.AppearanceSettings.EnableImmersive
        && ViewModel.ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;

    private void ApplyPlainSurfaces()
    {
        var plain = PlainMode;
        // 商店顶部直接在渐隐剧照上排海报、标题与操作，不再用整块卡面覆盖背景。
        // 内部 HeroCard 判断仍负责旧的文件选项与简介排版，视觉框单独收起。
        var surface = plain ? null : Application.Current.Resources["EgSurfaceBrush"] as Brush;
        HeroCardTop.Background = surface;
        HeroCardBottom.Background = surface;
        PlainHeroSurface.Background = surface;
        PlainHeroSurface.Opacity = plain ? 0 : 1;
        HeroScrim.Opacity = plain ? 0 : 1;
        foreach (var panel in BodyPanels)
        {
            DetailSectionCard.Apply(panel, plain, panel == InfoPanel ? new Thickness(24) : ViewModel.BodyPanelPadding);
            panel.Background = plain ? StoreCardMaterialSource.Background : _panelFill;
            panel.BorderThickness = plain ? default : (Thickness)Application.Current.Resources["EgCardBorderThickness"];
        }
        // 资源回调的顺序不固定；信息卡那块的底（商店固定面色／纸面面板刷／沉浸无卡面）由那一支最后定。
        PaintBackdropReading();
    }

    /// <summary>五块正文面板的底 —— 沉浸档里淡到 0，纸面让开之后不再有「板里的板」那一层。</summary>
    private readonly AcrylicBrush _panelFill = new();

    /// <summary>五块正文面板的描边，同 <see cref="_panelFill"/>。</summary>
    private readonly SolidColorBrush _panelLine = new();

    /// <summary>宽高批量写入期间，背景渲染结果等待同一次布局结算。</summary>
    private bool _sizing;

    /// <summary>正文面板共享本页的底色与描边，沉浸开启时让开，无背景布局恢复卡面。</summary>
    private IEnumerable<StackPanel> BodyPanels =>
        BodySheet.Children.OfType<StackPanel>();

    /// <summary>正文里那四条卡片带 —— 沉浸档里它们是直接站在背景图上的，纸面档才站在纸上。</summary>
    private IEnumerable<ShelfStrip> BodyStrips => [EpisodeStrip, SeasonRow, CastRow, SimilarRow];

    /// <summary>第一帧之前准备本页的正文画刷。</summary>
    private void PrepareShowcase()
    {
        _storeBackdropFade = new StoreBackdropFade(StoreBackdropContent, StoreBackdrop, StoreBackdropMask);
        _sheetBrush.Color = PaperColor;
        BodyPaper.Background = _sheetBrush;
        BodySheet.Background = null;

        // 背景整层不调不透明度：设置里那一档落在图上那道遮罩（PaintScrim／PaintBackdropFade），
        // 2026-11-06 起普通与沉浸共用同一个数，翻面不再改图本身。

        // 监听本页的 ThemeResource 替换，覆盖系统高对比度；不订阅需要 CoreWindow 的系统事件。
        CardMaterialSource.RegisterPropertyChangedCallback(Border.BackgroundProperty, (_, _) =>
        {
            RefreshPanelBrushes();
            PaintBackdropReading();
        });
        StoreCardMaterialSource.RegisterPropertyChangedCallback(Border.BackgroundProperty, (_, _) => ApplyPlainSurfaces());
        MicaLayerSource.RegisterPropertyChangedCallback(Border.BackgroundProperty, (_, _) => ApplyPlainSurfaces());
        HeroContentLayerSource.RegisterPropertyChangedCallback(Border.BackgroundProperty, (_, _) => ApplyPlainSurfaces());
        RefreshPanelBrushes();
        foreach (var panel in BodyPanels)
        {
            panel.Background = _panelFill;
            panel.BorderBrush = _panelLine;
        }
    }

    /// <summary>
    /// 换了主题：面板那两档颜色跟着换（纸面那一支在还原档也要换；沉浸档它一直是尾部那道半透明黑，与主题无关）。
    /// 主题现在全是深色，这一句在屏上多数时候看不出来 —— 但把颜色抄进画刷的那一刻就欠下了这笔账。
    /// </summary>
    private void RefreshShowcaseBrushes()
    {
        RefreshPanelBrushes();
        if (!ViewModel.Showcase) _sheetBrush.Color = PlainMode ? Microsoft.UI.Colors.Transparent : PaperColor;
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

    private static Color BrushColor(string key) => Application.Current.Resources[key] is SolidColorBrush brush
        ? brush.Color
        : key switch
        {
            "EgWindowBrush" => Color.FromArgb(255, 0x0C, 0x11, 0x16),
            "EgSurfaceAltBrush" => Color.FromArgb(255, 0x1C, 0x25, 0x2E),
            _ => Color.FromArgb(255, 0x30, 0x39, 0x41)
        };

    /// <summary>沉浸档的纸面与阅读遮罩同色。</summary>
    private Color WashColor => UsesBackdropFade
        ? Color.FromArgb(0, DetailHero.ScrimInk.R, DetailHero.ScrimInk.G, DetailHero.ScrimInk.B)
        : ReadingShadeColor;

    private void OnShowcaseChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(DetailViewModel.Showcase) || _sizing) return;
        ViewModel.UpdateHeroBlur();
        SettleShowcase();
    }

    /// <summary>首次显示、模式切换和窄窗排版后统一应用纸面、卡面及尾部留白。</summary>
    private void SettleShowcase()
    {
        PaintStoreBackdrop();
        PaintScrim();
        var showcase = ViewModel.Showcase;
        _sheetBrush.Color = PlainMode ? Microsoft.UI.Colors.Transparent : showcase ? WashColor : PaperColor;
        ApplyPlainSurfaces();
        _panelFill.Opacity = showcase ? 0 : 1;
        _panelLine.Opacity = showcase ? 0 : 1;

        // 沉浸档里这四条带没有纸面接着，卡片底下那两行字直接压在剧照上：换成压在图上那一套浅墨
        // （标题近白、副行 #C9D0D9）。主题那支暗字（#9FA4A8）压在彩色画面上一比一点六，几乎读不出来
        // —— 用户 2026-11-08 截图点名的就是它。纸面档照旧用主题自己的正文与暗字。
        foreach (var strip in BodyStrips) strip.OnScrim = showcase;

        PaintBackdropReading();

        HeroTail.MinHeight = ViewModel.TailMinHeight;

        // 那一栏撑满封面的高之后，操作那一块要贴到封面下沿 —— 换模式时它自己的高度可能没变，
        // SizeChanged 不会再来，所以在这里主动排一次（见 DetailPage.LayoutHeroColumn）。
        LayoutHeroColumn();

        PaintWash(force: true);
    }
}
