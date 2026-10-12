using System.Collections.ObjectModel;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.Views;

public sealed partial class HomePage
{
    private ISettingsService? _homeSettings;
    private int _homeLibraryFlowIndex = -1;
    private double _libraryFlowHeight;
    internal MoviePilotHomeSubscriptions Subscriptions { get; } = new();
    internal ObservableCollection<object> HomeRows { get; } = [];

    private void RefreshHomeRows()
    {
        if (_homeSettings is null) return;
        var shelves = ViewModel.Shelves.ToDictionary(shelf => shelf.HomeKey, shelf => (object)shelf, StringComparer.Ordinal);
        // 完整次序包含已压上轮播的媒体库，才能找回它在混合列表中的插入位置。
        if (ViewModel.LibraryOnBanner && ViewModel.LibraryShelf is { Cards.Count: > 0 } library)
            shelves[HomeLayout.Libraries] = library;
        if (Subscriptions.Movies.HasContent) shelves[HomeLayout.MoviePilotMovies] = Subscriptions.Movies;
        if (Subscriptions.Series.HasContent) shelves[HomeLayout.MoviePilotSeries] = Subscriptions.Series;
        var ordered = HomeLayout.Plan(_homeSettings.Settings.Ui.HomeRows, null)
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
    }

    private static double RowHeightOf(object row) => row switch
    {
        CardShelf shelf => shelf.RowHeight,
        MoviePilotHomeRow subscription => subscription.RowHeight,
        _ => 0
    };

    private static string RowTitle(object row) => row switch
    {
        CardShelf shelf => shelf.Title,
        MoviePilotHomeRow subscription => subscription.Title,
        _ => ""
    };

    private static Visibility HomeEmptyVisibility(Visibility empty, int rows) => rows == 0 ? empty : Visibility.Collapsed;

    /// <summary>混排的标题、加载与错误区域高度不同，直接读取目标条带，不能按相同标题高拼算。</summary>
    private (double Line, bool HasNext)? MeasureMixedCoversLine(double gap)
    {
        if (_homeLibraryFlowIndex < 0) return null;
        var below = _homeLibraryFlowIndex + (ViewModel.LibraryOnBanner ? 0 : 1);
        if (below >= HomeRows.Count) return (0, false);
        var target = Math.Min(below + 1, HomeRows.Count - 1);
        var element = (FrameworkElement)(ShelfRepeater.TryGetElement(target) ?? ShelfRepeater.GetOrCreateElement(target));
        UpdateLayout();
        if (HomeFoldMotion.Find<ShelfStrip>(element) is not { ActualHeight: > 0 } strip) return null;
        var line = ScreenTopAtScrollZero(strip);
        // 新实现的货架可能正在入场或补位；阈值取布局坐标，不把动画位移算进来。
        for (DependencyObject? node = strip; node is not null && !ReferenceEquals(node, ShelfRepeater);
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { RenderTransform: Microsoft.UI.Xaml.Media.TranslateTransform drift }) line -= drift.Y;
        if (ViewModel.LibraryOnBanner)
        {
            if (_libraryFlowHeight <= 0) return null;
            line += _libraryFlowHeight + gap;
        }
        return (line, true);
    }
}
