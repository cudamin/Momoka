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

    public double StorePreviewWidth => Math.Min(640, PageWidth * 0.46);
    public double StoreContentWidth => Math.Max(0, PageWidth - 2 * DetailHero.StorePageInset(PageWidth)
        - (StoreMode && PageWidth >= 1100 ? StorePreviewWidth + 28 : 0));
    public double StoreTextWidth => StoreMode
        ? Math.Max(0, StoreContentWidth - StillWidth - ColumnSpacing) : double.PositiveInfinity;
    public Visibility RemainingTimeVisibility => StoreMode ? Visibility.Collapsed : ResumeVisibility;
    public Visibility TrailerButtonVisibility => StoreMode ? Visibility.Collapsed : TrailerVisibility;

    /// <summary>
    /// 简洁（商店）模式里简介能写几行 —— 由页面按「封面右栏从简介顶到封面下沿还剩多高」实测回来。
    /// 第一帧还没量到时按 2 行排，量到后由 <c>DetailPage.Store.cs</c> 的 <c>UpdateStoreOverviewLines</c> 写入；
    /// 写不完的文字由 <see cref="OverviewTrimming"/> 那一支以省略号收尾。
    /// </summary>
    private int _storeOverviewLines = 2;
    internal int StoreOverviewLines
    {
        get => _storeOverviewLines;
        set
        {
            if (!SetProperty(ref _storeOverviewLines, value)) return;
            OnPropertyChanged(nameof(OverviewMaxLines));
        }
    }

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
        OnPropertyChanged(nameof(BodyMinHeight));
        OnPropertyChanged(nameof(ScrimHeight));
        OnPropertyChanged(nameof(PictureHeight));
    }
}
