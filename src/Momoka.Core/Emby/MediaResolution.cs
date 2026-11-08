namespace Momoka.Emby;

/// <summary>
/// 分辨率档位：把视频轨的宽高读成「4K / 1440p / 1080p / 720p / 576p / 480p」这类发行档位。
/// <para>
/// <b>不能只看高度。</b>2.39:1 的宽银幕片源把上下黑边裁掉之后只剩八百来行，实测这一份
/// <c>1920×804</c>（用户 2026-11-08 报的《摩加迪沙》）按高度分档就掉进了 720p，而它是 1080p 的片子：
/// 同一份文件的「媒体信息」写着 1920 x 804，Emby 自己的「分辨率」筛选也按宽度把它算作 1080p
/// （<c>MinWidth=1800</c>，见 <see cref="EmbyFilterBy"/>）。所以宽、高各算一档，取高的那一个 ——
/// 宽说 1080、高说 720，档位是 1080p；反过来 4:3 的 <c>1440×1080</c> 是宽说 720、高说 1080，
/// 档位同样是 1080p。
/// </para>
/// <para>
/// 宽度只用 Emby 网页端那三档（4K ≥ 3800、1080p ≥ 1800、720p ≥ 1200，与 <see cref="EmbyFilterBy"/>
/// 的「分辨率」筛选同一套数字）；够不上 1200 的宽度不表态，档位交给高度。高宽比超过 3:1 的条幅
/// （32:9 超宽屏、VR 双画面）也不拿宽度提档：<c>5120×1440</c> 是 1440p 的片子，不该因为够宽就写成 4K。
/// 高度往上归到标准档位，认不出的照写像素数（<c>404p</c>）。
/// </para>
/// </summary>
public static class MediaResolution
{
    /// <summary>
    /// 「1080p」for <c>1920×804</c>，「4K」for <c>3840×1600</c>，「」when neither dimension is known.
    /// </summary>
    public static string Describe(int? width, int? height)
    {
        // 竖屏视频把短边当高度：1080×1920 是 1080p，不是 1440p。
        if (width is > 0 && height is > 0 && height > width) (width, height) = (height, width);

        var tier = Math.Max(Tier(height), WidthTier(width, height));
        if (tier <= 0) return "";

        return tier switch
        {
            >= 2160 => "4K",
            >= 1440 => "1440p",
            >= 1080 => "1080p",
            >= 720 => "720p",
            >= 576 => "576p",
            _ => tier + "p"
        };
    }

    /// <summary>高度往上归到标准档位：2160 / 1440 / 1080 / 720 / 576，认不出的高度原样返回。</summary>
    private static int Tier(int? height) => height is > 0 and var value
        ? value switch
        {
            >= 2000 => 2160,
            >= 1300 => 1440,
            >= 1000 => 1080,
            >= 700 => 720,
            >= 576 => 576,
            _ => value
        }
        : 0;

    /// <summary>宽度那三档，数字同 Emby 网页端的「分辨率」筛选（见类注释）。</summary>
    private static int WidthTier(int? width, int? height)
    {
        if (width is not > 0) return 0;

        // 没有高度就无从判断是不是条幅，宽度照算；有高度时只认不超过 3:1 的常规画幅。
        if (height is > 0 && width.Value > height.Value * 3) return 0;

        return width.Value switch
        {
            >= 3800 => 2160,
            >= 1800 => 1080,
            >= 1200 => 720,
            _ => 0
        };
    }
}
