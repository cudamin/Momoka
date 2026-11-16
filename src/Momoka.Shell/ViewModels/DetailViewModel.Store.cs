using Microsoft.UI.Xaml;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

public sealed partial class DetailViewModel
{
    /// <summary>装饰背景与沉浸布局背景分开，避免给尾部和正文增加整屏占位。</summary>
    public bool StoreMode => !ImmersiveEnabled
        && ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;

    public Visibility StoreBackdropVisibility => Show(StoreMode && HeroImage is not null);

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
    /// 商店背景那块环境底的高度：取「剧照盒」「顶部内容」与「第一屏视口」里最高的一个。
    /// <para>
    /// 剧照是 16:9，按钮那一排可能比它矮、也可能比它高：只按剧照盒画，底会在按钮上方截断；
    /// 只按内容画，超宽窗口上图的下半段又会渐隐到窗口画布而不是色底上。取大之后两头都不露边。
    /// </para>
    /// <para>
    /// 2026-11-16 用户报「深/浅窗口底色的那层区域固定、不跟窗口缩放」，并附微软商店条目页参考：
    /// 这一层保留铺满第一屏的画布，但图色只按
    /// <see cref="Views.StoreBackdropBlend.AmbientAlpha"/> 在图盒外走一小段，余下透明处露出固定的主题底色。
    /// 窗口缩放时剧照盒变化，收边跟着重算 —— 图右边和下面不再提前淡成窗口色，也不会把图色拖到窗口底。
    /// </para>
    /// </summary>
    public double StoreAmbientHeight => Math.Max(Math.Max(StoreBackdropHeight, HeroHeight), Viewport);

    public double StorePreviewWidth => Math.Min(640, PageWidth * 0.46);

    /// <summary>
    /// 商店模式左边那一列内容能有多宽。宽版式（<see cref="DetailHero.StoreWideFloor"/> 及以上）要让开
    /// 顶部右侧的剧照／预告视口，窄档才把整幅宽度都给内容 —— 两条判据同一个断点，屏上不会出现
    /// 「背景已经变成右侧视口、内容却还按整幅排」的半档。
    /// </summary>
    public double StoreContentWidth => Math.Max(0, PageWidth - 2 * DetailHero.StorePageInset(PageWidth)
        - (StoreMode && PageWidth >= DetailHero.StoreWideFloor ? StorePreviewWidth + 28 : 0));
    public double StoreTextWidth => StoreMode
        ? Math.Max(0, StoreContentWidth - Math.Max(180, StillWidth) - ColumnSpacing) : double.PositiveInfinity;
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
