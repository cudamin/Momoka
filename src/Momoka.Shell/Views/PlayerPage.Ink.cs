using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Momoka.Shell.Views;

/// <summary>
/// 压在同一片画面上的按钮图标，各带一层跟着字形走的影子。
/// <para>
/// 2026-11-03 用户令第四条先要的是「按钮改为白色实心并且加上黑边」，追问后又改成「算了，在原按钮的基础上
/// 加阴影就可以」。所以按钮的底、边框、悬停那两档**一个像素没动**，加的是图标底下那层影子 —— 白图标压在
/// 一格亮得发白的画面上时，唯一还认得出形状的就是它周围那圈暗。载体是项目现成的
/// <see cref="TextInk"/>（<c>DropShadow</c> 收元素自己的 alpha 蒙版，影子跟着字形而不是跟着方框），
/// 主页轮播那四行字用的是同一套。
/// </para>
/// <para>
/// **谁有、谁没有**（用户令里点名的那两颗）：控制条上方那一排、右上那四颗窗口命令都有（它们直接压在画面
/// 上）；**返回键没有**（它坐在左上角那块常驻玻璃上）、**静音键没有**（它坐在音量条那块牌子上）—— 这两颗
/// 本来就有自己的底，用户说的「这些有边框的」就是它们。「跳过」那颗同理：它自己铺着 Raised 底、带一圈
/// 1px 描边，不在这条令的范围里。
/// </para>
/// <para>
/// 做法只能是把图标包进一格：影子宿主必须是图标<b>前面</b>的兄弟（<c>SetElementChildVisual</c> 挂上去的
/// 视觉画在宿主自己的内容之上，排在后面就压住字形了）。包完之后按钮内容从 <c>FontIcon</c> 变成 <c>Grid</c>，
/// 所以读「这颗按钮是哪颗图标」的地方统一走 <see cref="GlyphOf"/>，别再直接 <c>Content is FontIcon</c>。
/// </para>
/// <para>
/// 影子采的是**图标里面画笔画的那一层**，不是图标控件自己：<c>GetAlphaMask</c> 只长在 <see cref="TextBlock"/>、
/// <see cref="Image"/> 和 <see cref="Shape"/> 上（<c>FontIcon</c>／<c>PathIcon</c> 没有这个方法），而那一层
/// 在图标自己的模板里 —— 模板套上之前树里没有它，所以第一次找不到就等图标的 <c>Loaded</c> 再来一次。
/// </para>
/// </summary>
public sealed partial class PlayerPage
{
    /// <summary>
    /// 图标影子的模糊半径。16.5～22 号字形的笔画约 1.5 像素，<see cref="TextInk.SmallBlur"/>（6）是给
    /// 13 号正文调的，压在这么细的笔画上会散成一团灰；5 正好是「认得出是影子、又不糊边」那一档。
    /// </summary>
    private const double GlyphInkBlur = 5;

    /// <summary>
    /// 已经挂上影子的那几颗按钮（自检读它，见 <c>ProbeGlyphInk</c>）。挂不上的那颗在屏上就是「这页别处都有
    /// 影子、它一颗没有」，而那种少一颗的样子没人报得出来 —— 所以这里留一份名单而不是静默。
    /// </summary>
    private readonly List<Button> _inked = [];

    /// <summary>同上，取不到字形蒙版、退回按外框那一块软影的那几颗（自检分开报，见 <see cref="ProbeGlyphInk"/>）。</summary>
    private readonly List<Button> _haloed = [];

    /// <summary>
    /// 给这一页压在画面上的那颗图标挂影子。构造时跑一次；字形换（播放/暂停）、字号换（全屏那一档）都不必
    /// 重来 —— 影子量的是那一层实际大小，<see cref="TextInk"/> 自己订着 <c>SizeChanged</c>。
    /// </summary>
    private void WireGlyphInk()
    {
        foreach (var button in new Button[]
                 {
                     // 控制条上方那一排（左下到右下，次序同 XAML）
                     PreviousButton, PlayButton, NextButton, TransportStatsButton, DanmakuButton,
                     ChapterButton, EpisodeButton, VersionButton, PreviousChapterButton, NextChapterButton,
                     SubtitleButton, AudioButton, FullscreenButton,

                     // 右上那四颗窗口命令（置顶那颗是 PathIcon，同一格、同一套）
                     PinButton, MinimizeButton, MaximizeButton, CloseButton
                 })
        {
            InkContent(button);
        }
    }

    /// <summary>
    /// 把一颗按钮的内容包进「影子宿主 ＋ 图标」那一格，然后让 <see cref="TextInk"/> 把影子画上去。
    /// <para>
    /// 那一格贴着图标自己的大小（两者都是内容尺寸，按钮的 ContentPresenter 居中摆它），所以影子与字形
    /// 同一个原点；宿主的尺寸跟着图标变（<see cref="TextInk"/> 里那条 <c>SizeChanged</c>）。
    /// </para>
    /// </summary>
    private void InkContent(Button button)
    {
        if (button.Content is not FrameworkElement glyph || glyph is Grid) return;

        var host = new Border { IsHitTestVisible = false };
        var cell = new Grid();

        button.Content = null;
        cell.Children.Add(host);
        cell.Children.Add(glyph);
        button.Content = cell;

        if (Ink(button, glyph, host)) return;

        // 图标的模板还没套：那一层还不存在。**Loaded 也不一定够** —— 实测（2026-11-03 的自检树）
        // FontIcon 里面是 `Grid → TextBlock`，而 Loaded 那一拍这层还没建出来，于是那一次必然落空。
        // 量到尺寸就是「里面的东西已经建好了」：这时再找一次，还找不到才退回按外框那一块软影。
        void Once(object sender, RoutedEventArgs args)
        {
            glyph.Loaded -= Once;
            if (Ink(button, glyph, host)) return;

            glyph.SizeChanged += Measured;

            void Measured(object sizeSender, SizeChangedEventArgs change)
            {
                if (change.NewSize.Width <= 0 || change.NewSize.Height <= 0) return;
                glyph.SizeChanged -= Measured;
                if (Ink(button, glyph, host)) return;

                TextInk.AttachHalo(host, glyph, GlyphInkBlur);
                _haloed.Add(button);
            }
        }

        glyph.Loaded += Once;
    }

    /// <summary>
    /// 图标里那层能取蒙版的东西找到了就按字形挂影子（返回 true）；还没建出来返回 false，由调用方过一会儿再试。
    /// 退回按外框那一块软影是**最后一步**，由调用方在试过之后决定（见 <see cref="InkContent"/>）——
    /// 两种都算「挂上了」，自检把「几颗按字形、几颗按外框」报出来，好知道那一层到底找不找得到。
    /// </summary>
    private bool Ink(Button button, FrameworkElement glyph, Border host)
    {
        if (InkSource(glyph) is not { } source) return false;

        TextInk.AttachGlyph(host, source, GlyphInkBlur);
        _inked.Add(button);
        return true;
    }

    /// <summary>
    /// 图标里能取 alpha 蒙版的那一层。直接是 <see cref="TextBlock"/>／<see cref="Image"/>／<see cref="Shape"/>
    /// 就用它自己（<c>FontIcon</c>／<c>PathIcon</c> 的模板里那一个），否则往树里找第一个这样的后代。
    /// </summary>
    private static FrameworkElement? InkSource(FrameworkElement glyph)
    {
        if (glyph is TextBlock or Image or Shape) return glyph;
        return Descendant(glyph);
    }

    private static FrameworkElement? Descendant(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock or Image or Shape) return (FrameworkElement)child;
            if (Descendant(child) is { } deeper) return deeper;
        }

        return null;
    }

    /// <summary>
    /// 自检：该有影子的那几颗都挂上了。少一颗在屏上就是「这页别处都有、它没有」，而那种少一颗的样子没人报得出来；
    /// 影子本身又是一层画在合成树上的东西，构建和单测都看不见它。
    /// <para>
    /// 判据取 <see cref="_inked"/> 与这张名单的差集，不写数字 —— 名单加一颗，这一关当天跟上。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail) ProbeGlyphInk()
    {
        (string Name, Button Button)[] wanted =
        [
            ("上一集", PreviousButton), ("播放/暂停", PlayButton), ("下一集", NextButton), ("统计", TransportStatsButton),
            ("弹幕", DanmakuButton), ("章节", ChapterButton), ("剧集列表", EpisodeButton), ("版本", VersionButton),
            ("上一章节", PreviousChapterButton), ("下一章节", NextChapterButton), ("字幕", SubtitleButton),
            ("音轨", AudioButton), ("全屏", FullscreenButton),
            ("置顶", PinButton), ("最小化", MinimizeButton), ("最大化", MaximizeButton), ("关闭", CloseButton)
        ];

        var missing = wanted.Where(pair => !_inked.Contains(pair.Button) && !_haloed.Contains(pair.Button))
            .Select(pair => pair.Name).ToList();

        return (missing.Count == 0,
            missing.Count == 0
                ? $"{wanted.Length} 颗压在画面上的图标都挂上了影子（按字形 {_inked.Count} 颗、"
                  + $"按外框 {_haloed.Count} 颗；返回、静音、跳过按用户令不挂）"
                : $"{missing.Count} 颗没挂上：{string.Join('、', missing)}");
    }

    /// <summary>
    /// 按钮内容里那颗图标 —— 直接是它（还没挂影子的），或者包在影子那一格里（<see cref="InkContent"/> 之后）。
    /// 置顶那颗是 <c>Viewbox</c>／<c>PathIcon</c>，所以这里只答 <c>FontIcon</c>；它那一颗的颜色由
    /// <see cref="SetStripGlyphInk"/> 直接写字段，不走这份名单。
    /// </summary>
    private static FontIcon? GlyphOf(Button button) => button.Content switch
    {
        FontIcon direct => direct,
        Grid cell => cell.Children.OfType<FontIcon>().FirstOrDefault(),
        _ => null
    };
}
