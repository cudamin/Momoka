using Momoka.AniBt;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class AniBtSearchPanel : UserControl
{
    private MoviePilotService? _moviePilot;
    private bool _observing;
    private int _attachment;

    public AniBtSearchPanel() => InitializeComponent();

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
        ResourceBrowser.ClosePopups();
        return ViewModel.SearchAsync(keyword, episode);
    }

    internal void PrepareSearch()
    {
        ResourceBrowser.ClosePopups();
        ViewModel.PrepareSearch();
    }

    internal void Release()
    {
        _attachment++;
        if (_observing && _moviePilot is { } service) service.AvailabilityChanged -= OnAvailabilityChanged;
        _observing = false;
        ViewModel.Cancel();
        ResourceBrowser.ClosePopups();
    }

    private void ObserveAvailability()
    {
        if (_observing || _moviePilot is null) return;
        _attachment++;
        _moviePilot.AvailabilityChanged += OnAvailabilityChanged;
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

    private void OnReturnToAnime(object sender, RoutedEventArgs args) => ResourceBrowser.ClosePopups();

    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = SearchAsync(args.QueryText ?? string.Empty, ViewModel.Episode);

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ResourceBrowser.ClosePopups();
        ViewModel.PrepareSearch(preserveEpisode: true);
    }
}
