namespace Momoka.Emby;

/// <summary>
/// 主页第一屏上媒体库那一排摆在哪 —— 设置 → 主页 → 「主页媒体库位置」（2026-10-16 前在界面卡上、叫「首页媒体库
/// 位置」）。
/// <para>
/// 三档里的后两档是「钉住」：<see cref="OnCarousel"/> 和 <see cref="BelowCarousel"/> 不看窗口高矮，用户说了
/// 算；<see cref="Auto"/> 才是从 2026-09-13 起一直有的那一档 —— 由 <see cref="HomeFold.LibraryOnBanner"/> 按量
/// 出来的几何自己翻。
/// </para>
/// <para>
/// 枚举存整数（同 <see cref="CarouselSource"/> 那条规矩），<see cref="Auto"/> 必须是 0：缺键的旧设置文件读出来
/// 就是它，从旧版本升上来的人一个像素都不变。认不出来的数字由 <c>SettingsMigration.Normalize</c> 拨回。
/// </para>
/// </summary>
public enum HomeLibraryPlacement
{
    /// <summary>随窗口高度变换（装机默认）：窗口矮到那条线被第一屏裁掉时压上轮播，拉高又落回轮播下面。</summary>
    Auto = 0,

    /// <summary>固定在轮播图中：只要轮播在，媒体库那一排就压在封面的左下角，不看窗口高矮。</summary>
    OnCarousel = 1,

    /// <summary>固定在轮播图下方：媒体库那一排始终站在横排里，一次都不压上轮播。</summary>
    BelowCarousel = 2
}

/// <summary>
/// 主页首屏的「矮窗档」：窗口拉矮到媒体库那一排被第一屏裁掉之后，媒体库那一排不再占横排的格子，改压到轮播封面的
/// 左下角，原来在它下面的那一排向上补位 ——「把媒体库那压到轮播图封面的左下角，原来媒体库下方的哪一栏向上移填补
/// 媒体库的位置」（用户的话，2026-09-13）。
/// <para>
/// 触发线改过三站：2026-09-13 落在「继续观看」牌子底下那条通栏线（媒体库下面那一排的封面顶）——「窗口裁切超过
/// 继续观看下方的那条线的时候就触发媒体库上移」；2026-09-22 又往下挪了一排（「再下面那一排」的封面顶，
/// 用户配截图说「改为快要显示下方媒体库封面的时候上移」）；2026-11-13 收回媒体库自己（见下一段）。
/// </para>
/// <para>
/// 触发线 2026-11-13 按用户报的实屏截图收回「媒体库那一排自己」：**线 ＝ 媒体库整排（牌子 ＋ 空当 ＋ 卡片，
/// 连卡底那两行字）的下沿**。用户的话：「这都重叠了媒体库还不下移，修复」——那次窗口 1064×761（视口 744、
/// 轮播带宽 758），媒体库整排明明放得进第一屏，却因为 2026-09-22 那条「看下面第二排封面顶」的线（909）还压在
/// 轮播上，轮播里的字块被挤得离压上来的那一排只剩几个像素。09-22 那条线是在通栏高轮播（带宽 1422 时带高
/// 608）上定的：那时媒体库下方确实放不下；2026-11-12 晚轮播改成四边留白的圆角卡片、带子矮下来之后，同一条线
/// 就让媒体库在放得下的窗口里也压上去 —— 现在改回「媒体库整排自己被第一屏裁掉才压」，放得下就留在轮播下方。
/// 两条边界（线、带子的设计形状）的形状一字未动，动的是线上量的是谁。
/// </para>
/// <para>
/// 回默认的那一档是 2026-09-13 定的：**不看媒体库自己还在不在屏上，看轮播带还撑不撑得住设计形状** ——「轮播图上下
/// 裁切过多时隐藏，默认大小时不隐藏」。
/// </para>
/// <para>
/// 纯的、在 Core，和 <see cref="HomeCarousel"/> 同一个理由：这条界线是「哪一档窗口该用哪种摆法」的决定，
/// 输入全是量出来的几何，输出只有一个布尔 —— 正该被单测钉住的部分。屏上量这几样（那条线 ＝ 媒体库那一排的顶 ＋
/// 整排实高，牌子、空当、卡片都从排好的树上量）、把媒体库搬进搬出横排的那一半在 <c>HomePage</c>（纯 UI 行为，住代码后置）。
/// </para>
/// <para>
/// 两条边界，中间是压上档的窗口高度带：
/// <list type="number">
/// <item>媒体库那一排的**下沿**（<paramref name="nextRowLine"/>：整排自己，连卡底那两行字）还在第一屏外或正好压
/// 在第一屏底上 —— 这一排已经（或正好要）被裁掉，压上；它整个露在第一屏里就不压，留在轮播下方。媒体库自己露不露
/// 头不再参与：它整个出了屏、轮播还完好的那一档照样压着（那一档要是回默认，媒体库跟着沉到第一屏外，第一屏就真的
/// 什么都不剩了）；</item>
/// <item>视口够不到带子的设计形状（<paramref name="bandHeight"/>，<see cref="HomeCarousel.NaturalHeight"/>）
/// —— 带子被一屏压矮，剧照上下裁切开始超出默认大小那一档（一边约 12%），该回默认了。默认大小裁多少就是
/// 多少，所以「过多」不用再去挑一个百分比：超出设计形状的第一像素就是那道线。</item>
/// </list>
/// </para>
/// <para>
/// 这两条边界只管 <see cref="HomeLibraryPlacement.Auto"/>（「随窗口高度变换」）那一档。2026-10-12 起设置里
/// 多了一行「主页媒体库位置」，另两档由用户钉死：<see cref="HomeLibraryPlacement.OnCarousel"/> 压着不放、
/// <see cref="HomeLibraryPlacement.BelowCarousel"/> 一次不压 —— 那两档下这里两条边界都不参与，见
/// <see cref="LibraryOnBanner(HomeLibraryPlacement, double, double, double)"/>。
/// </para>
/// </summary>
public static class HomeFold
{
    /// <summary>
    /// 这一档窗口该不该把媒体库那一排压上轮播。设置里那一行（<paramref name="placement"/>）先说哪一档：
    /// <list type="bullet">
    /// <item><see cref="HomeLibraryPlacement.OnCarousel"/> / <see cref="HomeLibraryPlacement.BelowCarousel"/>
    /// 是用户钉住的，几何一个都不看 —— 那两档下 <paramref name="nextRowLine"/> 和 <paramref name="bandHeight"/>
    /// 递什么都不参与判定（调用方也就不用去算那条线：钉在轮播里那一档，媒体库下面有没有别的排都不相干）。</item>
    /// <item><see cref="HomeLibraryPlacement.Auto"/> 才走下面那条几何判定。</item>
    /// </list>
    /// <para>
    /// <paramref name="nextRowLine"/> 是**滚回顶上之后**媒体库那一排的下沿到第一屏顶边的距离（视口坐标，不带滚动
    /// 位移，按默认摆法的几何还原）—— 拿现场坐标来判，用户往下滚一下这一档就会自己翻面。这条线由 <c>HomePage</c>
    /// 量：默认档量媒体库自己那一排（排顶 ＋ 实高，牌子、空当、卡片连卡底都在里面）；压上档里媒体库不在横排，
    /// 从站进它那一格的补位排倒推（补位排的顶就是媒体库的顶，添回媒体库整排的高就是它的下沿）—— 两个方向量到
    /// 的是同一条线。
    /// </para>
    /// </summary>
    /// <param name="placement">设置 → 主页 → 「主页媒体库位置」。</param>
    /// <param name="viewport">第一屏的高（窗口客户区）。</param>
    /// <param name="nextRowLine">媒体库那一排的下沿在第一屏里的位置（默认摆法的几何），见类型开头那一段。只在默认档用。</param>
    /// <param name="bandHeight">轮播带按设计形状的高（<see cref="HomeCarousel.NaturalHeight"/>，不被一屏封住）。只在默认档用。</param>
    public static bool LibraryOnBanner(
        HomeLibraryPlacement placement,
        double viewport,
        double nextRowLine,
        double bandHeight)
    {
        // 量不到视口、或者带子这一拍收着（页面那头先把这两档挡掉了）：没有左下角可压，钉也钉不住。
        if (viewport <= 0) return false;

        return placement switch
        {
            // 钉在轮播里：只要轮播在，就压着不放 —— 窗口矮到带子被压扁、剧照多裁一截也不松手（「固定」就是这个意思）。
            HomeLibraryPlacement.OnCarousel => true,

            // 钉在轮播下方：一次都不压上，跟窗口高矮没关系。
            HomeLibraryPlacement.BelowCarousel => false,

            // 默认档：媒体库整排的下沿压着第一屏底或更靠下（它已经被裁掉），并且视口还撑得住带子的设计形状（剧照上下裁切没超出默认档）。
            _ => nextRowLine >= viewport && viewport >= bandHeight
        };
    }
}
