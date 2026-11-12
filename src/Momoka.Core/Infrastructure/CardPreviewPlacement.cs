namespace Momoka.Infrastructure;

/// <summary>
/// 悬停预览摆在哪儿（2026-11-12 用户令「从中心向两边扩散」）。
/// <para>
/// 横向以卡片中心为原点：面板比卡片宽，向左右两边各长出同样多的一段。只做这一件事的话，靠边的卡片
/// 会把面板顶出窗口 —— 所以再把它夹回窗口里，夹的代价只是那一边少长一点。
/// </para>
/// <para>
/// 这里放纯算术而不是写在视图里：窗口宽窄、卡片在货架里的位置、面板比窗口还宽这几种情形，真机上很难
/// 一个个摆出来看，而算错一格就是「面板半个身子在窗外」。Shell 那边只把卡片的位置和窗口的尺寸递进来，
/// 拿两个偏移交给 Popup。
/// </para>
/// </summary>
public static class CardPreviewPlacement
{
    /// <summary>
    /// 缩略图的高宽比：宽是高的两倍。所有卡片共用同一个形状，竖版海报也裁进这个框。
    /// 不是 16:9 —— 400 宽的面板配 16:9 的图有 225 高，整块预览压住了两排货架；2:1 的宽条是商店那块的比例，
    /// 图仍然认得出是一格画面，面板却矮了四十像素。
    /// </summary>
    public const double WideRatio = 0.5;

    /// <summary>面板宽 <paramref name="width"/> 时那块缩略图该多高。</summary>
    public static double MediaHeight(double width) => width <= 0 ? 0 : Math.Round(width * WideRatio);

    /// <summary>
    /// 面板左沿相对卡片左沿的偏移：先按「面板中心对卡片中心」算，再夹进窗口左右各留
    /// <paramref name="margin"/> 的范围。
    /// </summary>
    /// <param name="cardLeft">卡片左沿在窗口内容里的横坐标。</param>
    /// <param name="cardWidth">卡片当前的宽。</param>
    /// <param name="panelWidth">面板的宽。</param>
    /// <param name="windowWidth">窗口内容的宽。</param>
    /// <param name="margin">面板与窗口边缘至少留出的距离。</param>
    public static double Horizontal(double cardLeft, double cardWidth, double panelWidth, double windowWidth,
        double margin)
    {
        if (panelWidth <= 0 || windowWidth <= 0) return 0;

        var wanted = cardLeft + ((cardWidth - panelWidth) / 2);
        var lowest = margin;

        // 面板比窗口还宽时两头都夹不住，贴左边走：至少左沿那一条是看得见的。
        var highest = Math.Max(lowest, windowWidth - margin - panelWidth);
        return Math.Clamp(wanted, lowest, highest) - cardLeft;
    }

    /// <summary>
    /// 面板上沿相对卡片上沿的偏移：默认与卡片齐平、向下长开；底下放不下就整体上移，但不会移出窗口
    /// 上沿的 <paramref name="margin"/>。
    /// </summary>
    /// <param name="cardTop">卡片上沿在窗口内容里的纵坐标。</param>
    /// <param name="panelHeight">面板量出来的高。</param>
    /// <param name="windowHeight">窗口内容的高。</param>
    /// <param name="margin">面板与窗口边缘至少留出的距离。</param>
    public static double Vertical(double cardTop, double panelHeight, double windowHeight, double margin)
    {
        if (panelHeight <= 0 || windowHeight <= 0) return 0;

        var wanted = Math.Min(cardTop, windowHeight - margin - panelHeight);
        return Math.Max(wanted, margin) - cardTop;
    }
}
