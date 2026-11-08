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
        if (!ViewModel.Showcase) _sheetBrush.Color = PaperColor;
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
        PaintScrim();
        var showcase = ViewModel.Showcase;
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        _panelFill.Opacity = showcase ? 0 : 1;
        _panelLine.Opacity = showcase ? 0 : 1;

        // 沉浸档里这四条带没有纸面接着，卡片底下那两行字直接压在剧照上：换成压在图上那一套浅墨
        // （标题近白、副行 #C9D0D9）。主题那支暗字（#9FA4A8）压在彩色画面上一比一点六，几乎读不出来
        // —— 用户 2026-11-08 截图点名的就是它。纸面档照旧用主题自己的正文与暗字。
        foreach (var strip in BodyStrips) strip.OnScrim = showcase;

        PaintBackdropReading();

        HeroTail.MinHeight = ViewModel.TailMinHeight;

        PaintWash(force: true);
    }
}
