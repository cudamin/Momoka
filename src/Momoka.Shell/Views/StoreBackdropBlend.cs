using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>静图透明度与外部视频叠图共用同一条曲线；视频不经过VisualSurface。</summary>
internal static class StoreBackdropBlend
{
    internal const int Side = 128;
    internal const int FoundationWidth = 512;
    internal const int FoundationHeight = 256;
    internal const double FoundationTail = 120;
    private static readonly (double Position, double Alpha)[] PictureAcross =
        [(0, 0), (0.18, 0.38), (0.42, 0.96), (0.7, 1), (1, 0)];
    // 2026-11-15 用户令「加高右边封面图下方的渐变」：下方渐隐的起点从 0.42 提到 0.30，过渡带更长更缓。
    private static readonly (double Position, double Alpha)[] PictureDown =
        [(0, 1), (0.3, 1), (0.62, 0.7), (1, 0)];
    // 2026-11-15 用户令「详情页/订阅详情页这个左边别留这个黑边」：环境底左端不再起坡，从内容区左沿
    // （侧栏右沿）就是满色，右端照旧收在画布上。左端上坡的那一版在侧栏旁边留下一条从窗口底色升到
    // 环境色的暗带，屏上读成一道黑边。
    private static readonly (double Position, double Alpha)[] AmbientAcross =
        [(0, 1), (0.72, 1), (1, 0)];
    // 2026-11-16 用户令「在左上角加一层跟右侧一样的渐变」：环境底铺满窗口后，左沿也按右沿同一条曲线
    // 收边（左右各 28%），左上角因此是一道从窗口底色平滑升上来的坡。2026-11-18 用户令「改回主树的
    // 渐变样式」：左上方那团暗角退回这条左右对称的直线坡 —— 径向暗角那套铺得太散、浓淡也不再是
    // 媒体详情页的样子。订阅详情页同样用这一支。
    private static readonly (double Position, double Alpha)[] AmbientSymmetricAcross =
        [(0, 0), (0.28, 1), (0.72, 1), (1, 0)];
    private static readonly (double Position, double Alpha)[] AmbientDown =
        [(0, 1), (0.42, 1), (0.7, 0.68), (1, 0)];

    /// <summary>
    /// 商店档左上角那道淡墨（<c>StoreShade</c>）横向那一道的色标，浅色主题用这一份。与 DetailPage.xaml、
    /// MoviePilotSubscriptionPage.xaml 里那支 <c>LinearGradientBrush</c> 逐档同值，
    /// <c>StoreDetailLayoutTests</c> 读那两份 XAML 核对。
    /// <para>
    /// 为什么要在这里再来一份：淡墨画在视频<em>下面</em>（2026-11-16 用户令「视频与它那排控制键不该
    /// 跟着变暗」），而视频那层叠图是拿颜色盖住画面边缘的 —— 端点的颜色必须与「压了淡墨之后」的环境底
    /// 同值，否则视频左沿露出一道竖直的接缝（2026-11-18 用户报的那条）。叠图算色的时候读不到 XAML，
    /// 所以同一份色标在两处，靠测试对齐。
    /// </para>
    /// </summary>
    internal static readonly (double Position, double Alpha)[] ShadeAcross =
        [(0, 0), (0.3, 0x24 / 255d), (0.58, 0)];

    /// <summary>
    /// 横向那一道在<em>深色主题</em>下的两段色标（2026-11-27 用户令「把详情页背景图左侧改成深色的，浅色主题
    /// 时候保持浅色」）。浅色那份是给左列的字垫一层淡墨；深色下环境底是剧照取的色，亮剧照会把左边
    /// 抬成一片灰，用户要左侧是一整片深色、剧照从暗里浮出来。所以按几何分两段：
    /// <list type="bullet">
    /// <item><see cref="ShadeDarkLeft"/>：左列（0 → 剧照左沿），从浓墨缓降，一整片深色。</item>
    /// <item><see cref="ShadeDarkFade"/>：剧照自己的渐隐带（剧照左沿 → <see cref="PictureFade"/> 处满色），
    /// 从左列末色分三段散尽，剧照满色之前退完 —— 剧照的中段与右侧一个色阶都不动。</item>
    /// </list>
    /// 两段都是各自区间的比例停点；<see cref="ShadeDarkStops"/> 把它们按本次几何换算成整页的比例停点，
    /// 画刷与 <see cref="ShadeDark"/> 共用这一份。宽屏上右沿空隙会涨到五百多，剧照左沿在整页里的位置
    /// 跟着变，所以不能写成整页的固定比例。
    /// </summary>
    internal static readonly (double Position, double Alpha)[] ShadeDarkLeft =
        [(0, 0xD9 / 255d), (1, 0xA6 / 255d)];

    /// <inheritdoc cref="ShadeDarkLeft"/>
    internal static readonly (double Position, double Alpha)[] ShadeDarkFade =
        [(0, 0xA6 / 255d), (0.35, 0x66 / 255d), (0.7, 0x26 / 255d), (1, 0)];

    /// <summary>剧照横向渐隐到满色的位置（<see cref="PictureAcross"/> 第 0.96 那一档）：深色淡墨在这一处散尽。</summary>
    internal static double PictureFade => PictureAcross[2].Position;

    /// <inheritdoc cref="ShadeAcross"/>
    internal static readonly (double Position, double Alpha)[] ShadeDown =
        [(0, 0x30 / 255d), (0.35, 0x10 / 255d), (1, 0)];

    /// <summary>淡墨的颜色（同一个 #0C0E11 一族，同主页大图）。</summary>
    internal static readonly ThemeColor ShadeInk = ThemeColor.Rgb(0x0C, 0x0E, 0x11);

    /// <summary>
    /// 一点上的淡墨不透明度（浅色那一份）：横向那道压左沿、竖向那道压顶上，两道各自线性过渡后叠起来
    /// （两层同色，合成一层等价）。<paramref name="width"/>／<paramref name="height"/> 是那道淡墨自己那一格
    /// 的尺寸 —— XAML 里取环境底与视口里大的那个。
    /// </summary>
    internal static double Shade(double x, double y, double width, double height)
    {
        if (!(width > 0) || !(height > 0) || !double.IsFinite(x) || !double.IsFinite(y)) return 0;
        var across = Linear(x / width, ShadeAcross);
        var down = Linear(y / height, ShadeDown);
        return 1 - (1 - across) * (1 - down);
    }

    /// <summary>
    /// 一点上的淡墨不透明度（深色那一份）：<paramref name="leftColumn"/> 是剧照左沿（左列的宽），
    /// <paramref name="fadeWidth"/> 是剧照渐隐带的宽，横向那道按 <see cref="ShadeDarkLeft"/>／
    /// <see cref="ShadeDarkFade"/> 两段取；竖向那道与浅色同一支。
    /// </summary>
    internal static double ShadeDark(double x, double y, double leftColumn, double fadeWidth, double height)
    {
        if (!(height > 0) || !double.IsFinite(x) || !double.IsFinite(y)) return 0;
        var left = Math.Max(0, leftColumn);
        double across;
        if (left > 0 && x < left) across = Linear(x / left, ShadeDarkLeft);
        else across = fadeWidth > 0 ? Linear((x - left) / fadeWidth, ShadeDarkFade) : 0;
        var down = Linear(y / height, ShadeDown);
        return 1 - (1 - across) * (1 - down);
    }

    /// <summary>
    /// 深色横向那道的整页比例停点：<see cref="ShadeDarkLeft"/> 铺在 0 → <paramref name="leftColumn"/>，
    /// <see cref="ShadeDarkFade"/> 铺在 <paramref name="leftColumn"/> → <paramref name="leftColumn"/> +
    /// <paramref name="fadeWidth"/>，两段接缝处同色（0xA6），屏上没有折痕。页宽没量到时给空表（画刷不画）。
    /// </summary>
    internal static (double Position, double Alpha)[] ShadeDarkStops(double pageWidth, double leftColumn, double fadeWidth)
    {
        if (!(pageWidth > 0) || !double.IsFinite(pageWidth)) return [];
        var left = Math.Clamp(leftColumn, 0, pageWidth);
        var fade = Math.Clamp(fadeWidth, 0, pageWidth - left);
        var stops = new List<(double Position, double Alpha)>(ShadeDarkFade.Length + 1);
        if (left > 0) stops.Add((0, ShadeDarkLeft[0].Alpha));
        foreach (var (position, alpha) in ShadeDarkFade)
            stops.Add(((left + position * fade) / pageWidth, alpha));
        return stops.ToArray();
    }

    internal static double Alpha(double x, double y, bool ambient) => ambient
        ? Interpolate(x, AmbientAcross) * Interpolate(y, AmbientDown)
        : Interpolate(x, PictureAcross) * Interpolate(y, PictureDown);

    internal static byte[] Mask(bool ambient, bool symmetric = false)
    {
        var across = ambient
            ? symmetric ? AmbientSymmetricAcross : AmbientAcross
            : PictureAcross;
        var down = ambient ? AmbientDown : PictureDown;
        var pixels = new byte[Side * Side * 4];
        for (var y = 0; y < Side; y++)
            for (var x = 0; x < Side; x++)
            {
                var alpha = Channel(255 * Interpolate(x / (double)(Side - 1), across)
                    * Interpolate(y / (double)(Side - 1), down));
                var at = (y * Side + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = alpha;
            }
        return pixels;
    }

    // 普通内容色层位于环境底与视频之下，不创建或替换系统Backdrop。
    // 视频范围内完全不透明，外侧收到透明，叠图端点因此与实际底色一致。
    internal static double FoundationAlpha(double x, double y, double pageWidth, double videoWidth,
        double videoHeight, double rightGap)
    {
        var left = Math.Max(0, pageWidth - rightGap - videoWidth);
        // 留两个采样像素的实色余量，双线性缩放不会在视频最外沿提前漏出Mica。
        var pad = Math.Max(2, pageWidth / FoundationWidth * 2);
        var across = Ramp(x, 0, Math.Max(1, left - pad))
            * (1 - Ramp(x, pageWidth - rightGap + pad, pageWidth));
        return across * (1 - Ramp(y, videoHeight + pad, videoHeight + FoundationTail));
    }

    internal static byte[] Foundation(ThemeColor window, double pageWidth, double videoWidth,
        double videoHeight, double rightGap)
    {
        var pixels = new byte[FoundationWidth * FoundationHeight * 4];
        if (!double.IsFinite(pageWidth) || pageWidth <= 0 || !double.IsFinite(videoWidth) || videoWidth <= 0
            || !double.IsFinite(videoHeight) || videoHeight <= 0 || !double.IsFinite(rightGap) || rightGap < 0) return pixels;
        for (var y = 0; y < FoundationHeight; y++)
            for (var x = 0; x < FoundationWidth; x++)
            {
                var alpha = FoundationAlpha(x * pageWidth / (FoundationWidth - 1),
                    y * (videoHeight + FoundationTail) / (FoundationHeight - 1),
                    pageWidth, videoWidth, videoHeight, rightGap);
                var at = (y * FoundationWidth + x) * 4;
                pixels[at] = Channel(window.B * alpha);
                pixels[at + 1] = Channel(window.G * alpha);
                pixels[at + 2] = Channel(window.R * alpha);
                pixels[at + 3] = Channel(255 * alpha);
            }
        return pixels;
    }

    /// <summary>
    /// 叠图RGB预乘透明度；其端点等于视频背后的环境底与窗口合成色，避免实色矩形。
    /// <para>
    /// <paramref name="shadeWidth"/>／<paramref name="shadeHeight"/> 是压在环境底上那道淡墨（<c>StoreShade</c>）
    /// 自己那一格的尺寸：淡墨在视频下面，叠图看不见它，端点的颜色就得自己把淡墨补进去 ——
    /// 少了这一步，视频左沿在屏上是一道竖直的接缝（2026-11-18 用户报的「预告片左边的边缘没遮住」）。
    /// <paramref name="dark"/> 跟着主题：深色下横向那道浓得多，补少了同样是一道接缝；深色的几何
    /// （左列宽、渐隐带宽）由视频自己的位置与宽给，与画刷那头同一套数。
    /// </para>
    /// </summary>
    internal static byte[] Video(byte[]? ambient, ThemeColor window, double pageWidth, double ambientHeight,
        double videoWidth, double videoHeight, double rightGap, double shadeWidth, double shadeHeight, bool dark = false)
    {
        var pixels = new byte[Side * Side * 4];
        var valid = ambient?.Length == ArtworkAmbient.Width * ArtworkAmbient.Height * 4
            && double.IsFinite(pageWidth) && pageWidth > 0 && double.IsFinite(ambientHeight) && ambientHeight > 0;
        var left = pageWidth - rightGap - videoWidth;
        for (var y = 0; y < Side; y++)
            for (var x = 0; x < Side; x++)
            {
                var nx = x / (double)(Side - 1);
                var ny = y / (double)(Side - 1);
                var amount = 1 - Alpha(nx, ny, ambient: false);
                var color = window;
                if (valid)
                {
                    var ax = Math.Clamp((left + nx * videoWidth) / pageWidth, 0, 1);
                    var ay = Math.Clamp(ny * videoHeight / ambientHeight, 0, 1);
                    var at = ((int)Math.Round(ay * (ArtworkAmbient.Height - 1)) * ArtworkAmbient.Width
                        + (int)Math.Round(ax * (ArtworkAmbient.Width - 1))) * 4;
                    color = window.Mix(ThemeColor.Rgb(ambient![at + 2], ambient[at + 1], ambient[at]), Alpha(ax, ay, ambient: true));
                }
                var shadeX = left + nx * videoWidth;
                var shade = dark
                    ? ShadeDark(shadeX, ny * videoHeight, left, PictureFade * videoWidth, shadeHeight)
                    : Shade(shadeX, ny * videoHeight, shadeWidth, shadeHeight);
                color = color.Mix(ShadeInk, shade);
                var offset = (y * Side + x) * 4;
                pixels[offset] = Channel(color.B * amount);
                pixels[offset + 1] = Channel(color.G * amount);
                pixels[offset + 2] = Channel(color.R * amount);
                pixels[offset + 3] = Channel(255 * amount);
            }
        return pixels;
    }

    private static double Interpolate(double position, (double Position, double Alpha)[] stops)
    {
        position = Math.Clamp(position, 0, 1);
        for (var i = 1; i < stops.Length; i++)
        {
            if (position > stops[i].Position) continue;
            var previous = stops[i - 1];
            var next = stops[i];
            var t = (position - previous.Position) / (next.Position - previous.Position);
            return previous.Alpha + (next.Alpha - previous.Alpha) * t * t * (3 - 2 * t);
        }
        return stops[^1].Alpha;
    }

    /// <summary>与 XAML 渐变同一条曲线：两道 GradientStop 之间就是直线，这里不套 smoothstep。</summary>
    private static double Linear(double position, (double Position, double Alpha)[] stops)
    {
        position = Math.Clamp(position, 0, 1);
        for (var i = 1; i < stops.Length; i++)
        {
            if (position > stops[i].Position) continue;
            var previous = stops[i - 1];
            var next = stops[i];
            var span = next.Position - previous.Position;
            var t = span > 0 ? (position - previous.Position) / span : 1;
            return previous.Alpha + (next.Alpha - previous.Alpha) * t;
        }
        return stops[^1].Alpha;
    }

    private static double Ramp(double value, double start, double end)
    {
        var t = Math.Clamp((value - start) / Math.Max(1, end - start), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}
