using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    public ImageBrush BackdropImageBrush { get; } = new();
    public FrameworkElement BackdropPicture { get; } = new();
    public Image PosterImage { get; } = new();
    public Image TitlePlate { get; } = new();
    public Image CornerArt { get; } = new();
    public Image FooterBanner { get; } = new();
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

    internal PosterCard() => _posterArrival = new ImageArrivalFade(Poster);
}
