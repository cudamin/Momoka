using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

public sealed partial class HomeViewModel
{
    private MoviePilotHomeSubscriptions? _librarySubscriptions;
    private Action<string>? _openSubscriptionLibrary;
    private readonly Dictionary<string, (CardItem Card, string Url, Func<string, CancellationToken, Task<byte[]>> Fetch)> _moviePilotLibraries = new(StringComparer.Ordinal);

    internal void UseMoviePilotLibrary(MoviePilotHomeSubscriptions subscriptions, Action<string>? open)
    {
        if (!ReferenceEquals(subscriptions, _librarySubscriptions))
        {
            foreach (var entry in _moviePilotLibraries.Values) entry.Card.AbandonPoster();
            _moviePilotLibraries.Clear();
        }
        _librarySubscriptions = subscriptions;
        _openSubscriptionLibrary = open;
        RefreshMoviePilotLibrary();
    }

    internal void RefreshMoviePilotLibrary()
    {
        if (!_active || !SyncMoviePilotLibrary()) return;
        PublishShelves();
        ShowEmptyNotice = Shelves.Count == 0 && LibraryShelf is null;
    }

    // Two local navigation cards share the already-loaded subscriptions. Neither becomes a server
    // library or enters a media snapshot; missing artwork does not remove the category entrance.
    private bool SyncMoviePilotLibrary()
    {
        var entry = _all.FirstOrDefault(entry => entry.Row.Key == HomeLayout.Libraries);
        if (entry.Shelf is null || _images is not { } images) return false;
        var cards = entry.Shelf.Cards;
        var wanted = new List<CardItem>();
        if (_active && entry.Row.Visible && _openSubscriptionLibrary is not null
            && _librarySubscriptions is { Service: { Available: true } service } owner)
        {
            var fetch = service.CaptureImageFetcher();
            foreach (var key in new[] { HomeLayout.MoviePilotMovies, HomeLayout.MoviePilotSeries })
            {
                var rows = key == HomeLayout.MoviePilotMovies ? owner.ViewModel.Movies : owner.ViewModel.Series;
                var current = rows.Where(row => owner.ViewModel.Owns(row) && row.UsesImages(fetch)
                    && service.IsCurrentSubscription(row.Subscription)).ToArray();
                var url = current.Select(row => row.Subscription.Media.BackdropUrl)
                    .Concat(current.Select(row => row.Subscription.Media.PosterUrl))
                    .FirstOrDefault(IsArtworkUrl) ?? "";
                if (!_moviePilotLibraries.TryGetValue(key, out var kept)
                    || kept.Url != url || !ReferenceEquals(kept.Fetch, fetch))
                {
                    kept.Card?.AbandonPoster();
                    var card = new CardItem(new EmbyItem
                    {
                        Id = key,
                        Name = HomeLayout.FixedTitle(key)!,
                        Type = EmbyItemType.CollectionFolder
                    }, images, CardSize.LibraryWidth, wide: true, subtitle: "", indicators: false,
                        navigate: () => { if (Current()) _openSubscriptionLibrary?.Invoke(key); },
                        fetchPoster: url.Length == 0 ? null : async token =>
                        {
                            if (!Current()) throw new OperationCanceledException();
                            var bytes = await fetch(url, token).ConfigureAwait(true);
                            if (!Current()) throw new OperationCanceledException();
                            return bytes;
                        });
                    kept = (card, url, fetch);
                    _moviePilotLibraries[key] = kept;
                }
                wanted.Add(kept.Card);

                bool Current() => _active && ReferenceEquals(_librarySubscriptions, owner)
                    && ReferenceEquals(owner.Service, service) && service.Available
                    && ReferenceEquals(service.CaptureImageFetcher(), fetch);
            }
        }

        var changed = false;
        for (var index = cards.Count - 1; index >= 0; index--)
        {
            if (!cards[index].IsNavigation || !IsSubscriptionLibrary(cards[index].Item.Id)) continue;
            var replacement = wanted.FirstOrDefault(card => card.Item.Id == cards[index].Item.Id);
            if (ReferenceEquals(cards[index], replacement)) continue;
            cards[index].AbandonPoster();
            if (replacement is null) cards.RemoveAt(index);
            else cards[index] = replacement;
            changed = true;
        }
        foreach (var card in wanted)
            if (!cards.Contains(card)) { cards.Add(card); changed = true; }
        foreach (var key in _moviePilotLibraries.Keys.Where(key => !wanted.Any(card => card.Item.Id == key)).ToArray())
        {
            _moviePilotLibraries[key].Card.AbandonPoster();
            _moviePilotLibraries.Remove(key);
        }
        return changed;
    }

    private static bool IsSubscriptionLibrary(string key) =>
        key is HomeLayout.MoviePilotMovies or HomeLayout.MoviePilotSeries or MoviePilotSubscriptionsRequest.LibraryKey;

    private static bool IsArtworkUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
