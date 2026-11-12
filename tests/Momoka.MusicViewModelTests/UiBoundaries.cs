using Momoka.Emby;

// Only framework values, card image IO and navigation data are substitutes. The two VMs and their
// inherited loading/notice logic are linked production source, using the actual pinned generator.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}

namespace Momoka.Shell.Platform
{
    public interface IUiDispatcher
    {
        bool Post(Action work);
        void Run(Action work);
    }
}

namespace Momoka.Shell.Views
{
    public sealed class CardItem
    {
        public CardItem(EmbyItem item, EmbyImageStore images, int width, double aspect = 1,
            string? subtitle = null, bool indicators = true)
        {
            Item = item;
            Width = width;
            Subtitle = subtitle;
            Indicators = indicators;
        }
        public EmbyItem Item { get; }
        public int Width { get; }
        public string? Subtitle { get; }
        public bool Indicators { get; }
        public bool Abandoned { get; private set; }
        public bool IsPlaying { get; set; }
        public void ClaimCard(object owner) { }
        public void AbandonPoster() => Abandoned = true;
        public Task EnsurePosterAsync(Task? readiness = null) => Task.CompletedTask;
    }

    internal sealed record LibraryRequest
    {
        public required IServiceProvider Services { get; init; }
        public required string Title { get; init; }
        public EmbySessionScope? Source { get; init; }
        public string? ParentId { get; init; }
        public string? MusicRootId { get; init; }
        public string? MusicContainerType { get; init; }
        public string? SearchTerm { get; init; }
    }
}
