namespace Momoka.Emby;

/// <summary>轮播卡片四周那圈光晕与左侧渐变的颜色。三个通道，没有 UI 类型 —— 页面自己包成画刷。</summary>
public readonly record struct CarouselInk(byte R, byte G, byte B)
{
    /// <summary>没有可借的颜色（没有像素、尺寸不对、整张图都是近黑）。页面那一档把光晕收掉。</summary>
    public static CarouselInk None => default;

    /// <summary>三通道全零就是 <see cref="None"/>。</summary>
    public bool IsNone => R == 0 && G == 0 && B == 0;
}

/// <summary>
/// 从当前剧照里挑一个能代表它的颜色，给主页轮播卡片画四周光晕与左侧渐变
/// （2026-11-12 用户令「参考上图给轮播图添加圆角，和四周染色的效果」；2026-11-16 用户再问
/// 「左边的渐变能不能根据不同的背景图颜色自动切换 / 背景图外面光晕效果能不能做」，重新接回商店版式）。
/// <para>
/// 取的不是整张图的平均色。平均色会把一张「雪原里一条红围巾」算成一块灰，而屏上要的是那条围巾的颜色 ——
/// 所以先按饱和度筛掉灰调，再按「饱和度平方 × 亮度离中间有多近」加权：太暗的色压在深色底上看不见，
/// 太亮的（字幕、白墙、天空）借过来就是一片发光。
/// </para>
/// <para>
/// 最后把峰值通道抬进 96–200 这一档：一串近黑的加权平均仍然是一片看不见的暗，而光晕的作用是「让人看出
/// 这张图是什么颜色」。抬的是三个通道的同一个系数，色相不变。
/// </para>
/// <para>
/// 纯函数，住在 Core：输入是 BGRA 字节，输出三个通道，不碰任何 UI 类型。于是它能被离线测试逐条钉住，
/// 不必起一个窗口去截图。
/// </para>
/// </summary>
public static class CarouselAmbient
{
    /// <summary>最多取这么多个样本。1280 宽的剧照是九十来万像素，全扫一遍没必要。</summary>
    private const int SampleBudget = 4096;

    /// <summary>饱和度低于这一档算灰：借过来的颜色和底分不开。</summary>
    private const double GrayShare = 0.18;

    /// <summary>近黑与近白的门槛（按该像素最亮/最暗的通道算）。</summary>
    private const int DarkFloor = 28;
    private const int BrightCeiling = 232;

    /// <summary>亮度加权时认为「最值钱」的那一档（0–255，人眼亮度）。</summary>
    private const double SweetSpot = 132;

    /// <summary>峰值通道的下限与上限：低于下限抬到它，高于上限压到它。</summary>
    private const double PeakFloor = 96;
    private const double PeakCeiling = 200;

    /// <summary>
    /// 采样一张 BGRA8 图（顶行在前，alpha 不参与计算）。尺寸与实际字节数对不上时返回
    /// <see cref="CarouselInk.None"/>，不抛异常 —— 这条路上的像素来自网络与解码，页面不该因为一张坏图崩掉。
    /// </summary>
    public static CarouselInk Sample(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0) return CarouselInk.None;

        var stride = (long)width * 4;
        if (bgra.Length < stride * height) return CarouselInk.None;

        var step = Math.Max(1, (int)Math.Sqrt((double)width * height / SampleBudget));

        double red = 0, green = 0, blue = 0, weight = 0;
        double plainRed = 0, plainGreen = 0, plainBlue = 0, plain = 0;

        for (var y = 0; y < height; y += step)
        {
            var row = y * (int)stride;
            for (var x = 0; x < width; x += step)
            {
                var at = row + x * 4;
                int b = bgra[at], g = bgra[at + 1], r = bgra[at + 2];

                plainRed += r;
                plainGreen += g;
                plainBlue += b;
                plain++;

                var high = Math.Max(r, Math.Max(g, b));
                var low = Math.Min(r, Math.Min(g, b));
                if (high < DarkFloor || low > BrightCeiling) continue;

                var saturation = (high - low) / (double)high;
                if (saturation < GrayShare) continue;

                var luma = (r * 299 + g * 587 + b * 114) / 1000.0;
                var reach = Math.Max(0.15, 1 - Math.Abs(luma - SweetSpot) / SweetSpot);
                var w = saturation * saturation * reach;

                red += r * w;
                green += g * w;
                blue += b * w;
                weight += w;
            }
        }

        if (weight < 0.5)
        {
            // 整张图没有可借的色相（纯灰、纯黑、全是字幕）：退回平均色 —— 一片中性灰的光晕比没有强，
            // 而且它至少让卡片的边沿和页面底色分开。
            if (plain < 1) return CarouselInk.None;
            red = plainRed;
            green = plainGreen;
            blue = plainBlue;
            weight = plain;
        }

        var peak = Math.Max(red, Math.Max(green, blue)) / weight;
        if (peak <= 0) return CarouselInk.None;

        var scale = peak < PeakFloor ? PeakFloor / peak : peak > PeakCeiling ? PeakCeiling / peak : 1;
        return new CarouselInk(
            Clamp(red / weight * scale),
            Clamp(green / weight * scale),
            Clamp(blue / weight * scale));
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
