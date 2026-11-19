using Microsoft.UI.Xaml;
using Momoka.Diagnostics;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private ImageArrivalFade[]? _artworkArrivals;
    internal Microsoft.UI.Xaml.Controls.Image ConnectedPoster => PosterImage;
    internal Microsoft.UI.Xaml.Controls.Border ConnectedPosterSurface => PosterSurface;
    internal void StopConnectedPosterArrival() => _artworkArrivals?[1].Suppress();

    /// <summary>
    /// 徽标接住悬停面板那张图之后，别再让「图到了」的淡入把它从透明重新拉一遍：
    /// 这次它是从面板原位飞进来的，透明度由 <see cref="DetailPage.BeginPreviewHeadingMotion"/> 自己管。
    /// </summary>
    internal void StopConnectedPlateArrival() => _artworkArrivals?[2].Suppress();

    /// <summary>
    /// 关联动画抢跑前先把这一页的宽度交给视图模型。封面那一格显不显示、多大（<c>CoverShown</c> →
    /// <c>StillVisibility</c>／<c>StillWidth</c>）都由 <c>PageWidth</c> 算，而平时这个数要等第一次布局
    /// 由 <c>Body</c> 量出来（<see cref="OnBodySizeChanged"/>）。详情页与来源页同处一个窗口，
    /// Frame 的宽就是 Body 的宽，于是导航事务里就能给出同一个值。
    /// </summary>
    internal void SeedConnectedLayoutWidth()
    {
        if (Frame is not { ActualWidth: > 0 } frame) return;
        ViewModel.PageWidth = frame.ActualWidth;
        // 2026-11-17 起商店档的内容列也按窗口宽排（整列内容的收窄由视图模型的留白负责）；窗口真实宽
        // 另记一份，右侧视口与窗口右边缘的空隙靠它随窗口加大。
        ViewModel.WindowWidth = frame.ActualWidth;
    }

    /// <summary>
    /// 关联动画抢跑前强制一次布局：平台要拿到目标图真实的位置与尺寸才画得出那一段补间。
    /// 这一次布局本来就要做，只是挪到导航事务里；页面还没进树（XamlRoot 为空）就跳过，
    /// 仍等 Loaded／LayoutUpdated 那条原路。
    /// </summary>
    internal void ForceConnectedLayout()
    {
        if (XamlRoot is null) return;
        try { UpdateLayout(); }
        catch (Exception error) { Log.Warn(Category, "关联封面抢排失败，等第一次布局", error); }
    }

    internal bool ArtworkArrivalRunning => _artworkArrivals?.Any(fade => fade.IsRunning) == true;

    private void WatchArtworkArrival()
    {
        if (_artworkArrivals is not null) return;
        _artworkArrivals =
        [
            new(BackdropImageBrush, BackdropPicture),
            new(PosterImage),
            new(TitlePlate),
            new(CornerArt),
            new(FooterBanner),
            new(StoreBackdropImageBrush, StoreBackdropPicture),
        ];
    }

    private void StopArtworkArrival()
    {
        var arrivals = _artworkArrivals;
        _artworkArrivals = null;
        if (arrivals is null) return;
        foreach (var fade in arrivals) fade.Dispose();
    }
}
