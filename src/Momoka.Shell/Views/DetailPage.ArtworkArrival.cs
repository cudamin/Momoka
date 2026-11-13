namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private ImageArrivalFade[]? _artworkArrivals;
    internal Microsoft.UI.Xaml.Controls.Image ConnectedPoster => PosterImage;
    internal void StopConnectedPosterArrival() => _artworkArrivals?[1].Suppress();

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
