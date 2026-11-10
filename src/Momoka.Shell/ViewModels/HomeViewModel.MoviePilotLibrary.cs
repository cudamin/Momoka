using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

public sealed partial class HomeViewModel
{
    private MoviePilotService? _libraryMoviePilot;
    private CardItem? _moviePilotLibrary;
    private Action? _openSubscriptionLibrary;

    internal void UseMoviePilotLibrary(MoviePilotService service, Action? open)
    {
        var keep = ReferenceEquals(service, _libraryMoviePilot);
        _libraryMoviePilot = service;
        _openSubscriptionLibrary = open;
        if (open is null || _images is null) _moviePilotLibrary = null;
        else if (!keep || _moviePilotLibrary is null) _moviePilotLibrary = new CardItem(new EmbyItem
        {
            Id = MoviePilotSubscriptionsRequest.LibraryKey,
            Name = MoviePilotSubscriptionsRequest.LibraryTitle,
            Type = EmbyItemType.CollectionFolder
        }, _images, CardSize.LibraryWidth, wide: true, subtitle: "", indicators: false,
            navigate: () => { if (_active && service.Available) _openSubscriptionLibrary?.Invoke(); });
        RefreshMoviePilotLibrary();
    }

    internal void RefreshMoviePilotLibrary()
    {
        if (!_active || !SyncMoviePilotLibrary()) return;
        PublishShelves();
        ShowEmptyNotice = Shelves.Count == 0 && LibraryShelf is null;
    }

    // This is a local navigation card, never a server library or a cached media item.
    // All load, warm-cache and fnOS directory paths publish through PublishShelves.
    private bool SyncMoviePilotLibrary()
    {
        var entry = _all.FirstOrDefault(entry => entry.Row.Key == HomeLayout.Libraries);
        if (entry.Shelf is null) return false;
        var cards = entry.Shelf.Cards;
        var wanted = _active && entry.Row.Visible && _libraryMoviePilot?.Available == true ? _moviePilotLibrary : null;
        var changed = false;
        for (var index = cards.Count - 1; index >= 0; index--)
        {
            if (!cards[index].IsNavigation || ReferenceEquals(cards[index], wanted)) continue;
            cards[index].AbandonPoster();
            cards.RemoveAt(index);
            changed = true;
        }
        if (wanted is not null && !cards.Contains(wanted)) { cards.Add(wanted); changed = true; }
        return changed;
    }
}
