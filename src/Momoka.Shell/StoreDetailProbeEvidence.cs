namespace Momoka.Shell;

/// <summary>商店详情探针的实际布局、遮罩与首字屏幕读数判据；不产生界面或重写产品布局。</summary>
internal static class StoreDetailProbeEvidence
{
    internal static bool PageSizeMatches(double target, double client, double page, double body, double viewModel) =>
        new[] { client, page, body, viewModel }.All(value => double.IsFinite(value) && Math.Abs(value - target) < 1.5);

    internal static void RequireAmbient(byte[] pixels, int width, int height)
    {
        if (width < 8 || height < 4 || (long)width * height * 4 != pixels.Length)
            throw new InvalidOperationException("环境底像素尺寸不完整");
        var left = (height / 2 * width + width / 8) * 4;
        var right = (height / 2 * width + width * 7 / 8) * 4;
        // 蓝底红光团融合后右边仍可含蓝；检查相对色差，而不是误要求整个右边变成纯红。
        var leftWarmth = pixels[left + 2] - pixels[left];
        var rightWarmth = pixels[right + 2] - pixels[right];
        if (leftWarmth >= -20 || rightWarmth - leftWarmth < 35)
            throw new InvalidOperationException("实际环境底没有保留蓝底与红光团的相对色差");
    }

    internal static void Cleanup(params Action[] actions)
    {
        List<Exception>? failures = null;
        foreach (var action in actions)
        {
            try { action(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null) throw new AggregateException("商店诊断收尾失败，已继续还原其他状态", failures);
    }

    internal static void RequireSubscriptionColumns(double width, double inset, double coverLeft,
        double flowLeft, double flowRight, double factsLeft, double factsRight, double gap, bool side)
    {
        var values = new[] { width, inset, coverLeft, flowLeft, flowRight, factsLeft, factsRight, gap };
        if (values.Any(value => !double.IsFinite(value)) || width <= 0 || inset <= 0 || flowRight <= flowLeft
            || Math.Abs(coverLeft - inset) > 1 || Math.Abs(flowLeft - inset) > 1
            || (side ? Math.Abs(width - factsRight - inset) > 1 || Math.Abs(factsLeft - flowRight - gap) > 1 || gap != 24
                : Math.Abs(width - flowRight - inset) > 1 || Math.Abs(factsLeft - inset) > 1
                    || factsRight <= factsLeft || factsRight > width - inset + 1 || gap != 0))
            throw new InvalidOperationException("订阅正文列、资料列或两侧实际留白错误");
    }

    internal static void RequireViewport(double actualWidth, double pageWidth, double expectedWidth,
        double pictureWidth, double pictureGap, double acrylicGap, double trailerGap, double expectedGap)
    {
        var values = new[] { actualWidth, pageWidth, expectedWidth, pictureWidth, pictureGap, acrylicGap, trailerGap, expectedGap };
        if (values.Any(value => !double.IsFinite(value)) || actualWidth <= 0 || expectedWidth <= 0
            || Math.Abs(actualWidth - pageWidth) > 1.5 || Math.Abs(pictureWidth - expectedWidth) > 1.5
            || new[] { pictureGap, acrylicGap, trailerGap }.Any(gap => Math.Abs(gap - expectedGap) > 1.5))
            throw new InvalidOperationException("商店实际页宽、视口宽或右沿空隙不一致");
    }

    internal static void RequireClearance(double coverBottom, double detailsTop, double detailsBottom,
        double pictureBottom, double controlsBottom, double firstCardTop, double gap)
    {
        var values = new[] { coverBottom, detailsTop, detailsBottom, pictureBottom, controlsBottom, firstCardTop, gap };
        if (values.Any(value => !double.IsFinite(value)) || gap <= 0
            || detailsTop < coverBottom + gap - 1.5
            || firstCardTop < Math.Max(Math.Max(coverBottom, detailsBottom), Math.Max(pictureBottom, controlsBottom)) + gap - 1.5)
            throw new InvalidOperationException("商店操作或正文首卡仍压在封面、剧照或预告控制上");
    }

    internal static void RequireOverview(int actualLines, int expectedLines, bool trimmed, bool squarePanels)
    {
        if (actualLines < 1 || actualLines != expectedLines || !trimmed || !squarePanels)
            throw new InvalidOperationException("商店长简介未按指定行数折叠，或透明面板仍保留裁切圆角");
    }

    internal static StoreFadeRead ReadMask(byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width < 4 || height < 4 || (long)width * height * 4 != bgra.Length)
            throw new ArgumentException("商店遮罩像素或尺寸不完整");
        int Alpha(double x, double y) => bgra[((int)Math.Round(y * (height - 1)) * width
            + (int)Math.Round(x * (width - 1))) * 4 + 3];
        return new(width, height, Alpha(0, 0.35), Alpha(1, 0.35), Alpha(0.5, 1),
            Alpha(0.5, 0.35), Alpha(0.95, 0.35), Alpha(0.5, 0.9), Alpha(0.5, 0));
    }

    internal static void RequireMask(StoreFadeRead read)
    {
        if (read.Left != 0 || read.Right != 0 || read.Bottom != 0 || read.Core < 200
            || read.NearRight <= 0 || read.NearRight >= read.Core || read.NearBottom <= 0 || read.NearBottom >= read.Core
            || read.Top < read.Core - 2)
            throw new InvalidOperationException("商店实际遮罩未在左右和下方渐隐，或误加了顶部渐隐");
    }

    /// <summary>环境底遮罩：左端贴住内容区左沿（不透明，2026-11-15「左边别留这个黑边」），只收右边与底部。</summary>
    internal static void RequireAmbientMask(StoreFadeRead read)
    {
        if (read.Left < 245 || read.Right != 0 || read.Bottom != 0 || read.Core < 200
            || read.NearRight <= 0 || read.NearRight >= read.Core || read.NearBottom <= 0 || read.NearBottom >= read.Core
            || read.Top < read.Core - 2)
            throw new InvalidOperationException("商店环境底未在左沿满色覆盖，或仍保留了左端渐隐");
    }

    /// <summary>
    /// 以TextBlock独立渲染的首个汉字作字形参考，匹配同一实际桌面帧里的高覆盖率笔画。
    /// 参考图只定义应有笔画；通过必须来自实屏，左上半格单独计数以检测旧圆角缺角。
    /// </summary>
    internal static StoreGlyphRead ReadGlyph(byte[] reference, int referenceWidth, int referenceHeight,
        byte[] screen, int screenWidth, int screenHeight, int left, int top, int cellWidth, int cellHeight)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(screen);
        if (referenceWidth <= 0 || referenceHeight <= 0 || (long)referenceWidth * referenceHeight * 4 != reference.Length
            || screenWidth <= 0 || screenHeight <= 0 || (long)screenWidth * screenHeight * 4 != screen.Length
            || cellWidth <= 0 || cellHeight <= 0 || cellWidth > referenceWidth || cellHeight > referenceHeight
            || left < 0 || top < 0 || left + cellWidth > screenWidth || top + cellHeight > screenHeight)
            throw new ArgumentException("首字参考或实屏取样区域无效");
        var expected = 0;
        var matched = 0;
        var upperLeftExpected = 0;
        var upperLeftMatched = 0;
        for (var y = 0; y < cellHeight; y++)
            for (var x = 0; x < cellWidth; x++)
            {
                var source = (y * referenceWidth + x) * 4;
                var alpha = reference[source + 3];
                if (alpha < 230) continue;
                var target = ((top + y) * screenWidth + left + x) * 4;
                var visible = Enumerable.Range(0, 3).All(channel =>
                    Math.Abs(reference[source + channel] * 255.0 / alpha - screen[target + channel]) <= 45);
                expected++;
                if (visible) matched++;
                if (x >= cellWidth / 2 || y >= cellHeight / 2) continue;
                upperLeftExpected++;
                if (visible) upperLeftMatched++;
            }
        return new(expected, matched, upperLeftExpected, upperLeftMatched);
    }

    internal static void RequireGlyph(StoreGlyphRead read)
    {
        if (read.Expected < 10 || read.UpperLeftExpected < 2
            || read.Matched < read.Expected * 0.9 || read.UpperLeftMatched < read.UpperLeftExpected * 0.9)
            throw new InvalidOperationException("商店简介首字或首字左上角没有完整呈现在实际屏幕帧");
    }
}

internal sealed record StoreFadeRead(int Width, int Height, int Left, int Right, int Bottom,
    int Core, int NearRight, int NearBottom, int Top);

internal sealed record StoreGlyphRead(int Expected, int Matched, int UpperLeftExpected, int UpperLeftMatched);
