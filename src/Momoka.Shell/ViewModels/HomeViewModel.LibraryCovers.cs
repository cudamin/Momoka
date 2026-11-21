using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

public sealed partial class HomeViewModel
{
    internal void ApplyLibraryCovers(UiSettings ui)
    {
        if (!Live || !ReferenceEquals(_settings?.Settings.Ui, ui)) return;
        SyncLibraryCovers();
    }

    private void SyncLibraryCovers()
    {
        if (_settings is not { } settings) return;
        var shelf = _all.FirstOrDefault(entry => entry.Row.Key == HomeLayout.Libraries).Shelf;
        if (shelf is null) return;
        var ui = settings.Settings.Ui;
        var preset = ui.HomeLibraryCoverOverride ? HomeLibraryCoverPresets.Normalize(ui.HomeLibraryCoverPreset) : null;
        foreach (var card in shelf.Cards.Materialized)
        {
            var samples = _all.Where(entry => entry.Row.Key == HomeLayout.LibraryKey(card.Item))
                .SelectMany(entry => entry.Shelf.Cards.Materialized).Select(sample => sample.Item);
            var pictures = preset is null || card.IsNavigation ? [] : HomeLibraryCoverArtwork.Select(card.Item, samples, preset);
            card.ApplyHomeLibraryCover(preset, pictures);
        }
    }
}
