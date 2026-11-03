using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;

namespace Momoka.Shell.Views;

/// <summary>
/// 压在剧照上的字底下那一层影子 —— **跟着字形走，不是一个方框的影子**。
/// <para>
/// 为什么要它：主页轮播那块字站在一张任意亮度的剧照上，而四条边缘渐变到三分之一处就散尽了（用户 2026-09-05
/// 「渐变弄淡一些，面积弄少一些」），所以字的右半截没有任何保底。量出来的账：一张粉色动画剧照上，简介那两行
/// 底下的画面亮度到 138–162（满 255），而那行字是 #C9D0D9（亮度 207）—— 对比度 1.9:1，读不出来；同一块字在
/// 一张深底剧照上是 4.6–6.8:1，所以这不是「哪张图不好」，是「亮图上没有底线」。
/// </para>
/// <para>
/// **详情页头图不需要这一层**，别照着加：那一页的罩子末档是七成二的黑（<c>DetailHero.ScrimCeiling</c>），所以
/// 就算剧照白得发光，字底下的亮度也封在 77 上、对比度不会低于 5:1 —— 有保底的那一半用渐变，没保底的这一半
/// 用影子。
/// </para>
/// <para>
/// 做法：<c>DropShadow</c> 的 <c>Mask</c> 收的是元素自己的 alpha（<c>TextBlock.GetAlphaMask()</c>），所以影子是
/// 那几个字的形状而不是它们外框的形状；影子画在一个空的宿主元素上（<c>SetElementChildVisual</c> 挂上去的视觉
/// 画在宿主自己的内容<b>之上</b>，所以宿主必须排在字的<b>前面</b>那一格里，见 HomeBanner.xaml 里那几个 Row）。
/// 不压暗画面一个像素：影子只在字形周围那几像素里。
/// </para>
/// </summary>
internal static class TextInk
{
    /// <summary>
    /// 影子的墨。同轮播那四条边缘渐变和详情页头图那道罩子（<c>#0C0E11</c>）：它压的是一张照片，不是主题色 ——
    /// 剧照不会因为用户挑了哪一套配色就变亮，所以这一支故意不跟主题走。
    /// </summary>
    private static readonly Windows.UI.Color Ink = Windows.UI.Color.FromArgb(0xFF, 0x0C, 0x0E, 0x11);

    /// <summary>正文和读数那几行的模糊半径。约等于笔画宽的三四倍：再小就成了一圈硬边，再大就糊成一团灰。</summary>
    internal const double SmallBlur = 6;

    /// <summary>片名那一档（44 号）的模糊半径。笔画粗一档，影子也得跟着散开一档，否则看着像描了一圈黑边。</summary>
    internal const double TitleBlur = 12;

    /// <summary>一张图（徽标）那一档。徽标多半是白字白线，和片名同一个量级。</summary>
    internal const double PlateBlur = 10;

    /// <summary>一行字的影子。<paramref name="host"/> 是同一格里排在它前面的那个空元素。</summary>
    internal static void Attach(FrameworkElement host, TextBlock text, double blur = SmallBlur) =>
        Paint(host, text, text.GetAlphaMask(), blur);

    /// <summary>一张图（徽标）的影子。同上，只是 alpha 来自图自己。</summary>
    internal static void Attach(FrameworkElement host, Image image, double blur = PlateBlur) =>
        Paint(host, image, image.GetAlphaMask(), blur);

    /// <summary>
    /// 一颗图标（<c>FontIcon</c>、<c>PathIcon</c> 或包着它的 <c>Viewbox</c>）的影子。与上面两支同一套：
    /// alpha 来自元素自己，所以影子是那几个笔画的形状。播放页压在画面上的那些按钮吃的是这一支
    /// （见 <c>PlayerPage.Ink.cs</c>）—— 白图标压在亮画面上时，唯一还认得出形状的就是它周围那圈暗。
    /// </summary>
    internal static void AttachGlyph(FrameworkElement host, FrameworkElement glyph, double blur)
    {
        // 蒙版只能从这三类身上取（<c>GetAlphaMask</c> 只长在它们身上）；FontIcon／PathIcon 自己那一层
        // 就是模板里的 TextBlock／Path，调用方已经找好了才进来。
        var mask = glyph switch
        {
            TextBlock text => text.GetAlphaMask(),
            Image image => image.GetAlphaMask(),
            Shape shape => shape.GetAlphaMask(),
            _ => null
        };

        if (mask is not null) Paint(host, glyph, mask, blur);
    }

    /// <summary>
    /// 取不到字形蒙版时的退路：一块按元素外框算的软影。蒙版是一整片白，于是影子是元素那一格大小的
    /// 模糊暗块而不是字形的形状 —— 压在画面上是一圈均匀的暗底，认得出「有影子」，但没有跟着笔画走的那种质感。
    /// <para>
    /// 它在播放页只用在一颗图标<b>里面找不到可采样那一层</b>的时候（见 <c>PlayerPage.Ink.cs</c>，
    /// 自检会把两种各几颗报出来）。有蒙版的路子（<see cref="AttachGlyph"/>）永远优先。
    /// </para>
    /// </summary>
    internal static void AttachHalo(FrameworkElement host, FrameworkElement source, double blur)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        Paint(host, source, compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), blur);
    }

    /// <summary>
    /// 影子挂上去。尺寸跟着源元素量出来的大小走（<c>SizeChanged</c>）—— alpha 蒙版是在源元素自己的坐标里，
    /// 精灵视觉小一圈的话影子就会被裁掉一截。两个元素**不要求同一个左上角**：播放页那些图标是居中摆在
    /// 按钮里的（宿主与图标同格但不同尺寸），所以每一拍都按 <c>TransformToVisual</c> 把精灵挪到字形那一格上
    /// —— 主页轮播那四处两者本来就同原点，算出来是 (0,0)，与从前逐像素相同。
    /// <para>
    /// 不解绑：这一层是控件自己的一部分，和控件同生共死，而订的是自己那个孩子的事件。
    /// </para>
    /// </summary>
    private static void Paint(FrameworkElement host, FrameworkElement source, CompositionBrush mask, double blur)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;

        var shadow = compositor.CreateDropShadow();
        shadow.Mask = mask;
        shadow.Color = Ink;
        shadow.BlurRadius = (float)blur;
        shadow.Opacity = 0.9f;

        // 往下偏一点点，不是四面均匀：字下面暗一档看着是「浮在图上」，四面一样就成了外发光。
        shadow.Offset = new Vector3(0, 1, 0);

        var sprite = compositor.CreateSpriteVisual();
        sprite.Shadow = shadow;

        ElementCompositionPreview.SetElementChildVisual(host, sprite);

        Place();
        source.SizeChanged += (_, _) => Place();

        void Place()
        {
            sprite.Size = new Vector2((float)source.ActualWidth, (float)source.ActualHeight);

            // 布局之前问不出坐标（两者还不在同一棵树里），SizeChanged 会再来一次。
            try
            {
                var origin = source.TransformToVisual(host).TransformPoint(new Windows.Foundation.Point(0, 0));
                sprite.Offset = new Vector3((float)origin.X, (float)origin.Y, 0);
            }
            catch (ArgumentException)
            {
            }
        }
    }
}
