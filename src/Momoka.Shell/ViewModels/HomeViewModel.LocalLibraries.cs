using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.LocalMedia;
using Momoka.Shell.Services;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

public sealed partial class HomeViewModel
{
    private LocalLibraryCoordinator? _localLibraries;
    private Action<string>? _openLocalLibrary;
    private readonly Dictionary<string, (CardItem Card, string Stamp)> _localLibraryCards = new(StringComparer.Ordinal);

    internal void UseLocalLibraries(LocalLibraryCoordinator? service, Action<string>? open)
    {
        if (!ReferenceEquals(service, _localLibraries))
        {
            foreach (var entry in _localLibraryCards.Values) entry.Card.AbandonPoster();
            _localLibraryCards.Clear();
        }
        _localLibraries = service;
        _openLocalLibrary = open;
        RefreshLocalLibraries();
    }

    internal void RefreshLocalLibraries()
    {
        if (!_active) return;
        PublishShelves();
        ShowEmptyNotice = Shelves.Count == 0 && LibraryShelf is null;
    }

    private void SyncLocalLibraries()
    {
        var entry = _all.FirstOrDefault(entry => entry.Row.Key == HomeLayout.Libraries);
        if (entry.Shelf is null || _images is not { } images) return;
        var cards = entry.Shelf.Cards;
        var service = _localLibraries;
        var wanted = new List<CardItem>();
        if (_active && entry.Row.Visible && service is not null && _openLocalLibrary is not null)
        {
            foreach (var library in service.Libraries.Where(library => !string.IsNullOrWhiteSpace(library.SourceDirectory)))
            {
                var key = LocalLibraryRequest.Prefix + library.Id;
                var entries = service.EntriesFor(library.Id).Where(entry => entry.IsInLibrary).ToArray();
                var poster = entries.Select(LocalMediaEntryArtwork.PosterPath).FirstOrDefault(path => path.Length > 0) ?? "";
                var name = string.IsNullOrWhiteSpace(library.Name) ? Path.GetFileName(library.SourceDirectory.TrimEnd('\\', '/')) : library.Name;
                if (string.IsNullOrWhiteSpace(name)) name = "本地媒体库";
                // 和服务器媒体库那一排一样只写库名：卡片底下的第二行留给「继续观看」那种真的多一行信息的排。
                var subtitle = "";
                var stamp = Momoka.Shell.Media.LocalLibraryArtwork.StampFor(poster);
                if (!_localLibraryCards.TryGetValue(key, out var kept) || kept.Stamp != stamp)
                {
                    kept.Card?.AbandonPoster();
                    var id = library.Id;
                    var card = new CardItem(new EmbyItem { Id = key, Name = name, Type = EmbyItemType.CollectionFolder },
                        images, CardSize.LibraryWidth, wide: true, subtitle: subtitle, indicators: false,
                        localPosterPath: poster, navigate: () =>
                        {
                            if (_active && ReferenceEquals(service, _localLibraries)
                                && service.Libraries.Any(current => current.Id == id && !string.IsNullOrWhiteSpace(current.SourceDirectory)))
                                _openLocalLibrary?.Invoke(id);
                        });
                    kept = (card, stamp);
                    _localLibraryCards[key] = kept;
                }
                kept.Card.UpdateNavigationText(name, subtitle);
                wanted.Add(kept.Card);
            }
        }

        for (var index = cards.Count - 1; index >= 0; index--)
        {
            if (!cards[index].IsNavigation || !cards[index].Item.Id.StartsWith(LocalLibraryRequest.Prefix, StringComparison.Ordinal)
                || wanted.Contains(cards[index])) continue;
            cards[index].AbandonPoster();
            cards.RemoveAt(index);
        }
        foreach (var card in wanted)
            if (!cards.Contains(card)) cards.Add(card);
        foreach (var key in _localLibraryCards.Keys.Where(key => !wanted.Any(card => card.Item.Id == key)).ToArray())
        {
            _localLibraryCards[key].Card.AbandonPoster();
            _localLibraryCards.Remove(key);
        }
    }
}
