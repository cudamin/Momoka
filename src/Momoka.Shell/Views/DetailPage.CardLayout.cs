using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private bool _cardLayoutObserved;
    private int _cardLayoutLifetime;

    private void PrepareCardLayout()
    {
        ViewModel.PropertyChanged += OnCardLayoutChanged;
        Loaded += (_, _) =>
        {
            ShellPrefs.DetailLayoutChanged -= OnCardPreferencesChanged;
            ShellPrefs.DetailLayoutChanged += OnCardPreferencesChanged;
            _cardLayoutObserved = true;
            ViewModel.RefreshCardLayout();
        };
        Unloaded += (_, _) => StopCardLayout();
    }

    private void StopCardLayout()
    {
        _cardLayoutObserved = false;
        _cardLayoutLifetime++;
        ShellPrefs.DetailLayoutChanged -= OnCardPreferencesChanged;
    }

    private void OnCardPreferencesChanged(UiSettings ui)
    {
        if (!_cardLayoutObserved) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ViewModel.RefreshCardLayout();
            return;
        }
        var lifetime = _cardLayoutLifetime;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_cardLayoutObserved && lifetime == _cardLayoutLifetime) ViewModel.RefreshCardLayout();
        });
    }

    private void OnCardLayoutChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DetailViewModel.CardLayout)) PlaceEpisodes();
    }

    private StackPanel PanelFor(string key) => key switch
    {
        "episodes" => EpisodePanel,
        "info" => InfoPanel,
        "seasons" => SeasonPanel,
        "cast" => CastPanel,
        "similar" => SimilarPanel,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
}
