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
