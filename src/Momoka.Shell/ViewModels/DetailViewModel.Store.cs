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

    /// <summary>顶部背景随页宽缩放，并限制高度；不参与内容布局。</summary>
    public double StoreBackdropHeight => DetailHero.StoreBackdropHeight(PageWidth, HeroHeight);
    public double StoreBackdropWidth => DetailHero.StoreBackdropWidth(PageWidth);

    /// <summary>
    /// 剧照、预告与亚克力视口贴右站，但给右侧滚动条留一道空隙（2026-11-15 用户令「最右边要留一点空隙」）。
    /// 三个视口共用同一个边距，屏上才是同一块画面在移动；窗口收窄到窄档时那道空隙照留。
    /// </summary>
    public Thickness StorePreviewMargin => StoreMode
        ? new(0, 0, DetailHero.StorePreviewGap, 0) : default;

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
    private void NotifyStoreRoom()
    {
        OnPropertyChanged(nameof(HeroContent));
        OnPropertyChanged(nameof(HeroRoom));
        OnPropertyChanged(nameof(HeroLayoutHeight));
        OnPropertyChanged(nameof(HeroHeight));
        OnPropertyChanged(nameof(StoreAmbientHeight));
        OnPropertyChanged(nameof(BodyMinHeight));
        OnPropertyChanged(nameof(ScrimHeight));
        OnPropertyChanged(nameof(PictureHeight));
        OnPropertyChanged(nameof(TailMinHeight));
    }
}
