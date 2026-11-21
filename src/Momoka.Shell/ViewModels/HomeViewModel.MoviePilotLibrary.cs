using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

public sealed partial class HomeViewModel
{
    private MoviePilotHomeSubscriptions? _librarySubscriptions;
    private Action<string>? _openSubscriptionLibrary;
    private readonly Dictionary<string, (CardItem Card, IReadOnlyList<string> Urls, Func<string, CancellationToken, Task<byte[]>> Fetch)> _moviePilotLibraries = new(StringComparer.Ordinal);

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
            var rowsByKey = new Dictionary<string, MoviePilotSubscriptionCard[]>(StringComparer.Ordinal);
            foreach (var key in new[] { HomeLayout.MoviePilotMovies, HomeLayout.MoviePilotSeries })
            {
                var rows = key == HomeLayout.MoviePilotMovies ? owner.ViewModel.Movies : owner.ViewModel.Series;
                rowsByKey[key] = rows.Where(row => owner.ViewModel.Owns(row) && row.UsesImages(fetch)
                    && service.IsCurrentSubscription(row.Subscription)).ToArray();
            }

            foreach (var key in new[] { HomeLayout.MoviePilotMovies, HomeLayout.MoviePilotSeries })
            {
                var current = rowsByKey[key];
                var urls = MoviePilotLibraryArtwork.Select(current.Select(row => row.Subscription));
                if (!_moviePilotLibraries.TryGetValue(key, out var kept)
                    || !kept.Urls.SequenceEqual(urls, StringComparer.Ordinal) || !ReferenceEquals(kept.Fetch, fetch))
                {
                    kept.Card?.AbandonPoster();
                    var card = new CardItem(new EmbyItem
                    {
                        Id = key,
                        Name = HomeLayout.FixedTitle(key)!,
                        Type = EmbyItemType.CollectionFolder
                    }, images, CardSize.LibraryWidth, wide: true, subtitle: "", indicators: false,
                        navigate: () =>
                        {
                            if (Current()) _openSubscriptionLibrary?.Invoke(key);
                        },
                        fetchPoster: urls.Count == 0 ? null : async token =>
                        {
                            if (!Current()) throw new OperationCanceledException();
                            var bytes = await MoviePilotLibraryCoverLoader.LoadAsync(urls, async (url, ct) =>
                            {
                                if (!Current()) throw new OperationCanceledException(ct);
                                return await fetch(url, ct).ConfigureAwait(true);
                            }, token).ConfigureAwait(true);
                            if (!Current()) throw new OperationCanceledException();
                            return bytes;
                        });
                    kept = (card, urls, fetch);
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
        changed |= ReorderLibraryEntries(cards);
        return changed;
    }

    private bool ReorderLibraryEntries(CardCollection cards)
    {
        var directory = HomeLibraryLayout.FromItems(_libraryViews)
            .Append(new HomeRowPlan(HomeLibraryLayout.SubscriptionLibraryKey, "MP 订阅库", Visible: true));
        var ordered = HomeLibraryLayout.Plan(_settings?.Settings.Ui.HomeLibraryEntries, directory)
            .Where(row => row.Visible)
            .SelectMany(row => row.Key == HomeLibraryLayout.SubscriptionLibraryKey
                ? new[] { row.Key, HomeLayout.MoviePilotMovies, HomeLayout.MoviePilotSeries }
                : new[] { row.Key })
            .ToList();
        ordered.AddRange([HomeLibraryLayout.SubscriptionLibraryKey, HomeLayout.MoviePilotMovies, HomeLayout.MoviePilotSeries]);

        string? Key(CardItem card) => card.IsNavigation
            ? card.Item.Id is HomeLayout.MoviePilotMovies or HomeLayout.MoviePilotSeries
                or HomeLibraryLayout.SubscriptionLibraryKey ? card.Item.Id : null
            : card.Item.Source is not null ? HomeLayout.LibraryKey(card.Item) : null;

        var before = cards.ToArray();
        var sortable = cards.Where(card => Key(card) is not null).ToArray();
        var seen = new HashSet<CardItem>(ReferenceEqualityComparer.Instance);
        var orderedCards = new List<CardItem>();
        foreach (var key in ordered)
            foreach (var card in cards.Where(card => string.Equals(Key(card), key, StringComparison.Ordinal)))
                if (seen.Add(card)) orderedCards.Add(card);
        orderedCards.AddRange(sortable.Where(card => seen.Add(card)));
        var fullOrder = orderedCards.Concat(cards.Where(card => Key(card) is null).Cast<CardItem>()).ToArray();
        if (fullOrder.Length != cards.Count) return false;

        var index = 0;
        foreach (var card in fullOrder)
        {
            var current = cards.IndexOf(card);
            if (current >= 0 && current != index) cards.Move(current, index);
            index++;
        }
        return !before.SequenceEqual(cards);
    }

    private static bool IsSubscriptionLibrary(string key) =>
        key is HomeLayout.MoviePilotMovies or HomeLayout.MoviePilotSeries or MoviePilotSubscriptionsRequest.LibraryKey;
}
