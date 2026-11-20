using Momoka.Emby;

namespace Momoka.Shell.Views;

// The complete production methods decide when to ask. Only owner resolution and the image client are inert.
internal sealed partial class PosterDemandFixture
{
    internal object? XamlRoot { get; set; }
    internal DemandCard Card { get; } = new();
    internal void Request() => Begin();
}

internal sealed partial class EpisodeDemandFixture
{
    internal object? XamlRoot { get; set; }
    internal DemandCard Card { get; } = new();
    internal void Request() => Begin();
}

internal sealed partial class BannerDemandFixture
{
    private readonly List<BannerSlide> _slides;
    private int _index;
    private bool _recommendationsShown;

    internal BannerDemandFixture(int count = 2) =>
        _slides = [.. Enumerable.Range(0, count).Select(index => new BannerSlide(index.ToString()))];

    internal object? XamlRoot { get; set; }
    internal IReadOnlyList<BannerSlide> Slides => _slides;
    internal void SetCurrent(int index) => _index = index;
    internal void SetLayout(CarouselLayout layout, double width) =>
        _recommendationsShown = HomeCarousel.ShowsRecommendations(layout, width, _slides.Count);
    internal void Request() => Prefetch();
}

internal sealed class DemandCard
{
    internal List<Task?> Requests { get; } = [];
    internal Task EnsurePosterAsync(Task? presentation = null)
    {
        Requests.Add(presentation);
        return Task.CompletedTask;
    }
}

internal static class BrowsePresentation
{
    internal static object? LastOwner { get; private set; }
    internal static Task Gate { get; set; } = Task.CompletedTask;
    internal static Task WaitForImagesAsync(object owner)
    {
        LastOwner = owner;
        return Gate;
    }
}
