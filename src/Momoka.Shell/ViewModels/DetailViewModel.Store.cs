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
    public double StoreBackdropHeight => DetailHero.StorePictureHeight(PageWidth, HeroHeightRatio);
    public double StoreBackdropWidth => DetailHero.StorePictureWidth(PageWidth, HeroHeightRatio);

    public double StorePreviewWidth => Math.Min(640, PageWidth * 0.46);
    public double StoreContentWidth => Math.Max(0, PageWidth - 64
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
    private double _storeTrailerRoom;
    internal double StoreTrailerRoom
    {
        get => _storeTrailerRoom;
        set { if (SetProperty(ref _storeTrailerRoom, value)) NotifyStoreRoom(); }
    }
    private void NotifyStoreRoom()
    {
        OnPropertyChanged(nameof(HeroContent));
        OnPropertyChanged(nameof(HeroRoom));
        OnPropertyChanged(nameof(HeroLayoutHeight));
        OnPropertyChanged(nameof(HeroHeight));
        OnPropertyChanged(nameof(BodyMinHeight));
        OnPropertyChanged(nameof(ScrimHeight));
        OnPropertyChanged(nameof(PictureHeight));
    }
}
