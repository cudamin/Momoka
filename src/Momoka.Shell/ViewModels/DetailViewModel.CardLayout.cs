using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

public sealed partial class DetailViewModel
{
    internal IReadOnlyList<DetailCardSetting> CardLayout { get; private set; } = DetailLayout.Plan("", null);

    private bool CardVisible(string key) => CardLayout.Any(card => card.Key == key && card.Visible);

    internal void RefreshCardLayout()
    {
        CardLayout = _settings is null ? DetailLayout.Plan(ItemType, null) : DetailLayout.For(Settings.Ui, ItemType);
        OnPropertyChanged(nameof(CardLayout));
        OnPropertyChanged(nameof(EpisodeVisibility));
        OnPropertyChanged(nameof(InfoVisibility));
        OnPropertyChanged(nameof(SeasonShelfVisibility));
        OnPropertyChanged(nameof(CastVisibility));
        OnPropertyChanged(nameof(SimilarVisibility));
    }
}
