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
            ShellPrefs.DetailCastChanged -= OnCastPreferencesChanged;
            ShellPrefs.DetailCastChanged += OnCastPreferencesChanged;
            _cardLayoutObserved = true;
            ViewModel.RefreshCardLayout();
            // 页在台上时设置页改过这一项、而这一页当时没在树上（事件收不到）：回到台上前按当前值补一次。
            ViewModel.RefreshCast();
        };
        Unloaded += (_, _) => StopCardLayout();
    }

    private void StopCardLayout()
    {
        _cardLayoutObserved = false;
        _cardLayoutLifetime++;
        ShellPrefs.DetailLayoutChanged -= OnCardPreferencesChanged;
        ShellPrefs.DetailCastChanged -= OnCastPreferencesChanged;
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

    /// <summary>
    /// 「隐藏没有封面的演职人员」改了：只重排那一排。设置页在另一个窗口里，喊这一声的时候多半不在 UI 线程。
    /// </summary>
    private void OnCastPreferencesChanged(UiSettings ui)
    {
        if (!_cardLayoutObserved) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ViewModel.RefreshCast();
            return;
        }
        var lifetime = _cardLayoutLifetime;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_cardLayoutObserved && lifetime == _cardLayoutLifetime) ViewModel.RefreshCast();
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
