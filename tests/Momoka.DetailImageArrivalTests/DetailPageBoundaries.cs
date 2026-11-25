using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    // 关联封面抢排那一支（SeedConnectedLayoutWidth／ForceConnectedLayout）读页面宽度、Frame 和日志分类；
    // 这一份边界只需要它们可编译，实际布局仍由生产 ImageArrivalFade 与页面另一侧的用例覆盖。
    private const string Category = "fixture";
    internal DetailViewModel ViewModel { get; } = new();
    public Frame? Frame { get; set; }
    public XamlRoot? XamlRoot { get; set; }
    public void UpdateLayout() { }

    public ImageBrush BackdropImageBrush { get; } = new();
    public FrameworkElement BackdropPicture { get; } = new();
    public Image PosterImage { get; } = new();
    public Border PosterSurface { get; } = new();
    public Image TitlePlate { get; } = new();
    public Image CornerArt { get; } = new();
    public Image FooterBanner { get; } = new();
    public ImageBrush StoreBackdropImageBrush { get; } = new();
    public FrameworkElement StoreBackdropPicture { get; } = new();
    public void Watch() => WatchArtworkArrival();
    public void Release() => StopArtworkArrival();
}

internal sealed class CardItem
{
    internal Microsoft.UI.Xaml.Media.ImageSource? Poster { get; set; }
}

internal sealed partial class PosterCard
{
    internal Image Poster { get; } = new();
    internal CardItem? Card { get; set; }
    private readonly ImageArrivalFade _posterArrival;

    internal PosterCard() => _posterArrival = new ImageArrivalFade(Poster, () => Card);
}
