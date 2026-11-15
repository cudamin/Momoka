namespace Momoka.Infrastructure;

/// <summary>
/// 悬停预览里的徽标与片名飞进详情页时，目标元素该从哪儿起跑（2026-11-16 用户令
/// 「新增徽标和片名移进详情页的动画」）。
/// <para>
/// 源面板在点击那一拍就收起了，所以这里不搬元素，只记下它们在窗口内容里的最后位置；详情页拿自己的标题和
/// 徽标从同一个位置长回去。纯算术放在 Core：起飞缩放与平移量算错一格，屏上就是「从旁边跳进来」而不是
/// 「原来那一个移过去」，而真机上看不出是哪一个数错了。
/// </para>
/// </summary>
public static class PreviewHeadingPlacement
{
    /// <summary>目标元素的起始缩放：源高与目标高之比。高度没量出来时按 1，宁可不缩放也不除零。</summary>
    public static double Scale(double sourceHeight, double targetHeight) =>
        sourceHeight > 0 && targetHeight > 0 ? Math.Clamp(sourceHeight / targetHeight, 0.1, 3) : 1;

    /// <summary>
    /// 进度为零时的平移量：让缩放后的左上角落在源元素的左上角。返回的是相对目标元素自身位置的偏移，
    /// 直接喂给 <c>CompositeTransform</c>。
    /// </summary>
    public static (double TranslateX, double TranslateY) Offset(
        double sourceLeft, double sourceTop, double targetLeft, double targetTop) =>
        (sourceLeft - targetLeft, sourceTop - targetTop);
}
