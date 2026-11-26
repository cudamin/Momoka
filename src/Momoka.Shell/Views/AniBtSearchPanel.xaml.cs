using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.AniBt;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class AniBtSearchPanel : UserControl
{
    private MoviePilotService? _moviePilot;
    private bool _observing;
    private int _attachment;

    public AniBtSearchPanel()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.SelectedAnime) && ViewModel.SelectedAnime is null)
                AnimeDetailScroll.ChangeView(null, 0, null, true);
        };
    }

    internal AniBtSearchViewModel ViewModel { get; } = new();

    internal void Attach(AniBtService service, MoviePilotService moviePilot, ISystemLauncher launcher)
    {
        Release();
        _moviePilot = moviePilot;
        ViewModel.Attach(service, moviePilot, launcher);
        ResourceBrowser.Attach(ViewModel.Browser);
        ViewModel.UseDownloadPicker((resource, token) => MoviePilotDownloadDialog.ShowAsync(this, moviePilot, resource, token));
    }

    internal void Search(AniBtService service, MoviePilotService moviePilot, ISystemLauncher launcher, AniBtSearchQuery query)
    {
        Attach(service, moviePilot, launcher);
        AnimeSearchInput.Visibility = Visibility.Visible;
        AnimeSearchInput.Text = query.Title;
        _ = SearchAsync(query.Title, query.Episode);
    }

    internal Task SearchAsync(string keyword, int? episode = null)
    {
        ObserveAvailability();
        return ViewModel.SearchAsync(keyword, episode);
    }

    internal void PrepareSearch()
    {
        ViewModel.PrepareSearch();
    }

    internal void Release()
    {
        _attachment++;
        if (_observing && _moviePilot is { } service)
        {
            service.AvailabilityChanged -= OnAvailabilityChanged;
            service.AniBtAvailabilityChanged -= OnAvailabilityChanged;
        }
        _observing = false;
        ViewModel.Cancel();
    }

    private void ObserveAvailability()
    {
        if (_observing || _moviePilot is null) return;
        _attachment++;
        _moviePilot.AvailabilityChanged += OnAvailabilityChanged;
        _moviePilot.AniBtAvailabilityChanged += OnAvailabilityChanged;
        _observing = true;
    }

    private void OnAvailabilityChanged(object? sender, EventArgs args)
    {
        if (ViewModel.ConnectionMatches) return;
        var attachment = _attachment;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_observing && attachment == _attachment && ReferenceEquals(sender, _moviePilot)) Release();
        });
    }

    private void OnResultsLoaded(object sender, RoutedEventArgs args) => ResizeResults();
    private void OnResultsSizeChanged(object sender, SizeChangedEventArgs args) => ResizeResults();

    private void OnAnimeItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (ViewModel.CanChooseAnime && args.InvokedItem is AniBtAnimeRow row)
            row.OpenCommand.Execute(null);
    }

    private void ResizeResults()
    {
        if (AnimeResults.Layout is not UniformGridLayout layout || AnimeResults.ActualWidth < 1) return;
        var available = Math.Max(1, AnimeResults.ActualWidth - 20);
        var columns = available is >= 240 and < 420 ? 2 : Math.Max(1, (int)(available / 168));
        var spacing = 10d;
        var cardWidth = Math.Max(110, Math.Floor((available - spacing * Math.Max(0, columns - 1)) / columns));
        layout.MinItemWidth = cardWidth;
        layout.MinItemHeight = (cardWidth - 8) * 1.45 + 100;
    }

    private void OnHeroSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 540;
        Grid.SetColumn(HeroBody, narrow ? 0 : 1);
        Grid.SetColumnSpan(HeroBody, narrow ? 2 : 1);
        Grid.SetRowSpan(HeroPoster, narrow ? 1 : 2);
        HeroPoster.Width = narrow ? 74 : 154;
        HeroPoster.Height = narrow ? 108 : 224;
        AnimeHero.ColumnSpacing = narrow ? 12 : 16;
    }

    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = SearchAsync(args.QueryText ?? string.Empty, ViewModel.Episode);

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ViewModel.PrepareSearch(preserveEpisode: true);
    }
}
