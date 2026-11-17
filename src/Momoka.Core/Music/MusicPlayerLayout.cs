namespace Momoka.Music;

/// <summary>
/// 音乐播放页的两栏尺寸。放在 Core 是为了让断点与封面上限能被离线测试钉住 ——
/// 页面自己只负责把这三个数摆到 Grid 上。
/// <para>
/// 宽版式与飞牛网页版同一档：封面跟随窗口宽高（30% 宽、52% 高，200～480），右栏歌词
/// 420～520，两栏之间 48～64。窄到放不下两栏时整块收起歌词，左栏按 42% 宽重排；
/// 短窗（低于 320）也不并排，免得歌词只剩一条缝。
/// </para>
/// </summary>
public static class MusicPlayerLayout
{
    public const double MaxStageWidth = 1120;
    public const double MinCover = 320;
    public const double MaxCover = 480;
    public const double LyricsWideFloor = 860;
    public const double LyricsHeightFloor = 320;
    public const double LyricsMinWidth = 420;
    public const double LyricsMaxWidth = 520;
    public const double GapMin = 48;
    public const double GapMax = 64;

    /// <summary>左栏封面底下那一整摞（标题、副标题、进度、时间、控制排）大约占的高度。</summary>
    private const double ColumnChrome = 210;

    /// <summary>窗口里这一段还剩多少高：标题栏 48、页头 64、页脚 56、下边距 20 之外的部分。</summary>
    public static double StageHeight(double windowHeight) => Math.Max(0, windowHeight - 188);

    public static bool ShowsLyrics(double width, double height) =>
        width >= LyricsWideFloor && height >= LyricsHeightFloor;

    /// <summary>
    /// 封面边长。两栏时按 30% 宽，单栏时按 42% 宽；高度既只借 52%，也要给底下那摞控制留出 210。
    /// 320 是宽窗的下限（参考图那一档）；窄窗先让位给宽度与高度，绝不把控制排顶出可视区。
    /// </summary>
    public static double CoverSize(double width, double height, bool lyrics = true)
    {
        if (!(width > 0) || !(height > 0)) return MinCover;
        var room = height - ColumnChrome;
        var wanted = Math.Min(width * (lyrics ? 0.30 : 0.42), Math.Min(height * 0.52, room));
        var floor = Math.Min(MinCover, Math.Min(width - 56, room));
        return Math.Clamp(wanted, Math.Max(0, floor), MaxCover);
    }

    public static double LyricsWidth(double width) =>
        Math.Clamp(width * 0.40625, LyricsMinWidth, LyricsMaxWidth);

    public static double Gap(double width) => Math.Clamp(width * 0.05, GapMin, GapMax);
}
