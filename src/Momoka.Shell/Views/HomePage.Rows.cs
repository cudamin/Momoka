using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class HomePage
{
    private ISettingsService? _homeSettings;
    private int _homeLibraryFlowIndex = -1;
    internal MoviePilotHomeSubscriptions Subscriptions { get; } = new();
    internal ObservableCollection<object> HomeRows { get; } = [];

    private void RefreshHomeRows()
    {
        if (_homeSettings is null) return;
        var timing = _timing;
        timing?.Mark("home-rows-begin");
        var shelves = ViewModel.Shelves.ToDictionary(shelf => shelf.HomeKey, shelf => (object)shelf, StringComparer.Ordinal);
        // 完整次序包含已压上轮播的媒体库，才能找回它在混合列表中的插入位置。
        if (ViewModel.LibraryOnBanner && ViewModel.LibraryShelf is { Cards.Count: > 0 } library)
            shelves[HomeLayout.Libraries] = library;
        if (Subscriptions.Movies.HasContent) shelves[HomeLayout.MoviePilotMovies] = Subscriptions.Movies;
        if (Subscriptions.Series.HasContent) shelves[HomeLayout.MoviePilotSeries] = Subscriptions.Series;
        var ordered = HomeLayout.Plan(_homeSettings.Settings.Ui.HomeRows, null, _homeSettings.Settings.Ui.ShowHomeBanner)
            .Where(row => row.Visible && shelves.ContainsKey(row.Key)).Select(row => shelves[row.Key]).ToList();
        _homeLibraryFlowIndex = ViewModel.LibraryShelf is { } shelf ? ordered.IndexOf(shelf) : -1;
        if (ViewModel.LibraryOnBanner && ViewModel.LibraryShelf is { } overlay) ordered.Remove(overlay);

        for (var index = HomeRows.Count - 1; index >= 0; index--)
            if (!ordered.Contains(HomeRows[index])) HomeRows.RemoveAt(index);
        for (var index = 0; index < ordered.Count; index++)
        {
            var current = HomeRows.IndexOf(ordered[index]);
            if (current < 0) HomeRows.Insert(index, ordered[index]);
            else if (current != index) HomeRows.Move(current, index);
        }
        timing?.Mark("home-rows-end", HomeRows.Count);
    }

    private static string RowTitle(object row) => row switch
    {
        CardShelf shelf => shelf.Title,
        MoviePilotHomeRow subscription => subscription.Title,
        _ => ""
    };

    private static Visibility HomeEmptyVisibility(Visibility empty, int rows) => rows == 0 ? empty : Visibility.Collapsed;
}
