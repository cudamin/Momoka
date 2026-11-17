using System;
using Microsoft.UI.Xaml;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

public sealed partial class DetailViewModel
{
    /// <summary>装饰背景与沉浸布局背景分开，避免给尾部和正文增加整屏占位。</summary>
    public bool StoreMode => !ImmersiveEnabled
        && ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;

    /// <summary>
    /// 设置里商店详情页右上角选的是不是「艺术图」（2026-11-17 用户令「商店模式新增去掉背景图、渐变，右上角改为
    /// 显示艺术图的功能。做个选择框，选择背景图/预告 或者 艺术图」）。
    /// <para>
    /// 只回答设置那一格；「这一档摆不摆得出来」另外看模式、页宽和艺术图有没有解出来
    /// （<see cref="StoreBackdropVisibility"/>／<see cref="StoreCornerArtVisibility"/>）。
    /// </para>
    /// </summary>
    internal bool StoreArtSelected => _settings?.Settings.Ui.StoreCornerArt == true;

    public Visibility StoreBackdropVisibility => Show(
        DetailHero.StoreBackdropShown(StoreMode, StoreArtSelected, HeroImage is not null));

    /// <summary>
    /// 商店宽版式右上角的艺术图显不显 —— 选了艺术图、宽版式、且这张条目的艺术图已经解出来
    /// （<see cref="CornerImage"/>；季页没有这一张，自然收着）。窄窗没有这一角，只在宽版式出现。
    /// </summary>
    public Visibility StoreCornerArtVisibility => Show(DetailHero.StoreCornerArtShown(
        StoreMode, StoreArtSelected, PageWidth >= DetailHero.StoreWideFloor, CornerImage is not null));

    /// <summary>
    /// 商店宽版式右上角艺术图这一次画多高 —— 与左边封面同高（2026-11-17 用户令「高度要和左边的封面一样高」）。
    /// <para>
    /// 封面在商店档是 <see cref="StillHeight"/>（高跟右栏「徽标上沿到主播放键下沿」那一段实测、宽按图比例
    /// 收在 260 以内，见 <see cref="DetailHero.StoreStillBox"/>），艺术图跟着它走：图自己的比例保持不变，
    /// 宽由高乘出来，不再套沉浸档那个 360×202.5 的上限盒。
    /// </para>
    /// </summary>
    public double StoreArtHeight => StoreMode ? StillHeight : 0;

    /// <summary>
    /// 艺术图那一档的纯色底铺多高（2026-11-17 用户令「面积不够大，要到卡片上方」）：头图那一格，加上尾部
    /// 那道 2 与正文纸面的 14 上留白 —— 正好到正文第一张卡的上沿；不跟右侧剧照视口的高度走。
    /// </summary>
    public double StoreArtWashHeight => StoreMode
        ? HeroHeight + TailInset.Top + SheetPadding.Top : 0;

    /// <summary>
    /// 上面那张艺术图的宽：按这张位图自己的宽高比从 <see cref="StoreArtHeight"/> 乘出来（还没解出像素时按
    /// 16:9 兜底）。图的比例是几就是几，不裁也不拉。
    /// </summary>
    public double StoreArtWidth => StoreArtHeight <= 0 ? 0 : Math.Round(StoreArtHeight *
        (_cornerPixels is { Width: > 0, Height: > 0 } pixels
            ? pixels.Width / (double)pixels.Height
            : DetailHero.CornerWidth / DetailHero.CornerHeight));

    /// <summary>
    /// 商店宽版式右上角艺术图的位置（2026-11-17 用户令「右边边缘不能超过下面的卡片」）：上沿与左边的封面
    /// 同一条线（<see cref="HeroInset"/> 的上留白），右沿贴住正文卡片的内容右沿（<see cref="DetailHero.StorePageInset"/>），
    /// 不再像剧照预览那样几乎贴着窗口边。窄窗不摆这一角，这一份边距用不上。
    /// </summary>
    public Thickness StoreArtMargin => StoreMode
        ? new Thickness(0, HeroInset.Top, DetailHero.StorePageInset(PageWidth), 0) : default;

    /// <summary>顶部背景随页宽缩放，并限制高度；不参与内容布局。</summary>
    public double StoreBackdropHeight => DetailHero.StoreBackdropHeight(PageWidth, HeroHeight);
    public double StoreBackdropWidth => DetailHero.StoreBackdropWidth(PageWidth);

    /// <summary>
    /// 视口几何用的宽：优先取窗口真实宽，还没量到时退回内容列宽（两者在不超过 1840 时相同）。
    /// 画面大小不读它 —— 它只决定视口离<em>窗口右边缘</em>留多宽（见 <see cref="StorePreviewMargin"/>）。
    /// </summary>
    private double StoreWindowWidth => WindowWidth > 0 ? WindowWidth : PageWidth;

    /// <summary>
    /// 剧照、预告与亚克力视口贴右站，但给右侧滚动条留一道空隙（2026-11-15 用户令「最右边要留一点空隙」）。
    /// 三个视口共用同一个边距，屏上才是同一块画面在移动；窗口收窄到窄档时那道空隙照留。
    /// <para>
    /// 2026-11-17 用户令「加大背景图和右边窗口边缘之间的间距，跟随窗口不断变宽」：空隙由
    /// <see cref="DetailHero.StorePreviewEdgeGap"/> 按窗口宽给，窗口越宽留得越多；内容列居中让出的
    /// 右边一段由 <see cref="DetailHero.StorePreviewRightMargin"/> 减掉，视口右沿始终落在窗口边上那一道缝内。
    /// </para>
    /// </summary>
    public Thickness StorePreviewMargin => StoreMode
        ? new(0, 0, DetailHero.StorePreviewRightMargin(StoreWindowWidth, PageWidth), 0) : default;

    /// <summary>
    /// 商店背景那块环境底的高度：取「剧照盒」与「这一格内容」里更高的一个。
    /// <para>
    /// 剧照是 16:9，按钮那一排可能比它矮、也可能比它高：只按剧照盒画，底会在按钮上方截断；
    /// 只按内容画，超宽窗口上图的下半段又会渐隐到窗口画布而不是色底上。取大之后两头都不露边。
    /// </para>
    /// </summary>
    public double StoreAmbientHeight => Math.Max(StoreBackdropHeight, HeroHeight);

    public double StorePreviewWidth => Math.Min(640, PageWidth * 0.46);

    /// <summary>
    /// 商店模式左边那一列内容能有多宽。宽版式（<see cref="DetailHero.StoreWideFloor"/> 及以上）要让开
    /// 顶部右侧的剧照／预告视口，窄档才把整幅宽度都给内容 —— 两条判据同一个断点，屏上不会出现
    /// 「背景已经变成右侧视口、内容却还按整幅排」的半档。
    /// </summary>
    public double StoreContentWidth => Math.Max(0, PageWidth - 2 * DetailHero.StorePageInset(PageWidth)
        - (StoreMode && PageWidth >= DetailHero.StoreWideFloor ? StorePreviewWidth + 28 : 0));

    /// <summary>
    /// 片名那一栏这一次能有多宽（<c>HeroStack.MaxWidth</c>）。
    /// <para>
    /// 2026-11-16 用户令「位置不够时不要裁切，直接压在背景图上」：让位给右侧剧照之后放不下内容时，
    /// 不再把这一栏压到一百来像素（片名折行、徽标缩小、播放键被切），而是撑到
    /// <see cref="DetailHero.StoreTextFloor"/>，多出来的那一截压在剧照上。上限是整幅内容宽，
    /// 窗口窄到连保底都放不下时收到页边为止（见 <see cref="DetailHero.StoreTextColumn"/>）。
    /// </para>
    /// </summary>
    public double StoreTextWidth => StoreMode
        ? DetailHero.StoreTextColumn(PageWidth, StoreContentWidth, StillWidth, ColumnSpacing) : double.PositiveInfinity;

    /// <summary>
    /// 商店档头图那一行（封面 + 片名栏）这一次占多宽（<c>HeroContent.Width</c>）—— 片名栏撑到保底宽度时
    /// 这一行跟着往右长，压在右侧剧照上；其余档仍是让位之后的内容宽。
    /// </summary>
    public double StoreHeroWidth => StoreMode
        ? DetailHero.StoreHeroColumn(PageWidth, StoreContentWidth, StillWidth, ColumnSpacing) : 0;

    public Visibility RemainingTimeVisibility => StoreMode ? Visibility.Collapsed : ResumeVisibility;
    public Visibility TrailerButtonVisibility => StoreMode ? Visibility.Collapsed : TrailerVisibility;

    /// <summary>
    /// 商店模式片名下方那一块操作键（2026-11-16 用户令：全放在「播放日期」那一行下面）：
    /// 已看、收藏、更多收成图标排一行，主播放键带文字排在它们下面。
    /// 宽窄和图片是否到达都不隐藏整块，按钮自己的权限与可用性照旧判断。
    /// </summary>
    public Visibility StoreCoverActionsVisibility => Show(StoreMode);

    /// <summary>
    /// 已看那颗键的文字要不要画。商店模式在片名下方的操作块里收成图标（2026-11-16 用户令），
    /// 图标的两个状态照旧由 <see cref="WatchedVisibility"/>／<see cref="NotWatchedVisibility"/> 切。
    /// </summary>
    public Visibility ColumnWatchedLabelsVisibility => Show(!StoreMode);

    private double _storeDetailsRoom;
    internal double StoreDetailsRoom
    {
        get => _storeDetailsRoom;
        set { if (SetProperty(ref _storeDetailsRoom, value)) NotifyStoreRoom(); }
    }

    private double _storeCoverRoom;

    /// <summary>
    /// 商店档左边那张封面该长到多高 —— 右栏从徽标上沿到主播放键下沿的那一段，由视图实测
    /// （<c>DetailPage.MeasureStoreCover</c>）。2026-11-17 用户令「把商店模式下媒体详情页左上角的封面放大一点，
    /// 让它上方和徽标上方平行，下方和播放按钮下方平行」。
    /// <para>
    /// 0 表示还没量到、或者这一档没有主播放键（离开商店模式、播放键收起）：那一档退回 180×270 的兜底盒。
    /// 写进来之后重排封面（<see cref="FitStill"/>），<see cref="StillWidth"/> 与 <see cref="StillHeight"/>
    /// 自己的通知再把片名栏宽度、带高和右上角艺术图带过去。
    /// </para>
    /// </summary>
    internal double StoreCoverRoom
    {
        get => _storeCoverRoom;
        set
        {
            if (Math.Abs(_storeCoverRoom - value) < 0.5) return;
            _storeCoverRoom = value;
            if (StoreMode) FitStill();
        }
    }

    private void NotifyStoreRoom()
    {
        OnPropertyChanged(nameof(HeroContent));
        OnPropertyChanged(nameof(HeroRoom));
        OnPropertyChanged(nameof(HeroLayoutHeight));
        OnPropertyChanged(nameof(HeroHeight));
        OnPropertyChanged(nameof(StoreAmbientHeight));
        OnPropertyChanged(nameof(StoreArtWashHeight));
        OnPropertyChanged(nameof(BodyMinHeight));
        OnPropertyChanged(nameof(ScrimHeight));
        OnPropertyChanged(nameof(PictureHeight));
        OnPropertyChanged(nameof(TailMinHeight));
    }
}
