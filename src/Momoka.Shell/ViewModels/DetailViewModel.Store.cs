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
    /// 商店背景那块环境底的高度：取「剧照盒」与「这一格内容」里更高的一个。
    /// <para>
    /// 剧照是 16:9，按钮那一排可能比它矮、也可能比它高：只按剧照盒画，底会在按钮上方截断；
    /// 只按内容画，超宽窗口上图的下半段又会渐隐到窗口画布而不是色底上。取大之后两头都不露边。
    /// </para>
    /// </summary>
    public double StoreAmbientHeight => Math.Max(StoreBackdropHeight, HeroHeight);

    public double StorePreviewWidth => Math.Min(640, PageWidth * 0.46);
    public double StoreContentWidth => Math.Max(0, PageWidth - 2 * DetailHero.StorePageInset(PageWidth)
        - (StoreMode && PageWidth >= 1100 ? StorePreviewWidth + 28 : 0));
    public double StoreTextWidth => StoreMode
        ? Math.Max(0, StoreContentWidth - StillWidth - ColumnSpacing) : double.PositiveInfinity;
    public Visibility RemainingTimeVisibility => StoreMode ? Visibility.Collapsed : ResumeVisibility;
    public Visibility TrailerButtonVisibility => StoreMode ? Visibility.Collapsed : TrailerVisibility;

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
    }
}
