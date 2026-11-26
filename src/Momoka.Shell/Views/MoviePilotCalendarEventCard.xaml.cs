using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.AniBt;
using Momoka.MoviePilot;
using Momoka.Shell.Localization;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotCalendarEventCard : UserControl
{
    private CalendarCard? _entry;
    private CancellationTokenSource? _load;
    private Func<string, CancellationToken, Task<BitmapImage?>>? _loadImage;
    private CancellationToken _posterLifetime;
    private string? _poster;
    private bool _posterAttempted;
    private bool _inViewport;
    private double _layoutWidth = -1;

    public MoviePilotCalendarEventCard()
    {
        InitializeComponent();
        SizeChanged += OnCardSizeChanged;
        PosterFrame.EffectiveViewportChanged += OnPosterViewportChanged;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CalendarEventAutomationPeer(this);

    private void OnCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (Math.Abs(width - _layoutWidth) < 0.5) return;
        _layoutWidth = width;
        var layout = MoviePilotCalendarLayout.FitCard(width);
        CardSurface.Padding = new Thickness(layout.Padding);
        CardLayout.ColumnSpacing = layout.Gap;
        PosterColumn.Width = layout.Stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(layout.PosterWidth);
        PosterFrame.Width = layout.PosterWidth;
        PosterFrame.Height = layout.PosterWidth * 1.5;
        PosterFrame.Visibility = layout.PosterWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(PosterFrame, layout.Stacked ? 2 : 1);
        Grid.SetRow(TextPanel, layout.Stacked && layout.PosterWidth > 0 ? 1 : 0);
        Grid.SetColumn(TextPanel, layout.Stacked ? 0 : 1);
        Grid.SetColumnSpan(TextPanel, layout.Stacked ? 2 : 1);
        EventTitle.MaxLines = layout.PosterWidth > 0 ? 2 : 3;
        UpdateDetailVisibility();
    }

    internal bool Matches(CalendarCard entry) => _entry is { } current && current.Date == entry.Date
        && current.Subscription?.SubscriptionId == entry.Subscription?.SubscriptionId
        && current.Anime?.AnimeId == entry.Anime?.AnimeId;

    internal void Update(CalendarCard entry, Func<string, CancellationToken, Task<BitmapImage?>> loadImage, CancellationToken token)
    {
        if (_entry == entry) return;
        _entry = entry;
        if (entry.Subscription is { } subscription) PresentSubscription(subscription);
        else if (entry.Anime is { } anime) PresentAnime(anime);

        var poster = entry.Subscription?.Poster ?? entry.Anime?.ImageUrl;
        if (string.Equals(_poster, poster, StringComparison.Ordinal)) return;
        ClearPoster();
        _poster = poster;
        _loadImage = loadImage;
        _posterLifetime = token;
        StartVisiblePoster();
    }

    private void PresentSubscription(MoviePilotCalendarEntry entry)
    {
        EventTitle.Text = entry.Title;
        var episode = MoviePilotCalendarPresentation.EpisodeLabel(entry, UiText.Translate);
        var state = MoviePilotCalendarPresentation.LibraryState(entry);
        var library = UiText.Translate(MoviePilotCalendarPresentation.LibraryStateText(entry));
        if (library.Length > 0 && entry.LibraryEpisodeCount is { } count)
            library += entry.LibraryTotalEpisodes is { } total ? $" ({count}/{total})" : $" ({count})";
        LibraryText.Style = state switch
        {
            MoviePilotCalendarLibraryState.InLibrary => (Style)Resources["CalendarLibraryCompleteStyle"],
            MoviePilotCalendarLibraryState.PartiallyInLibrary => (Style)Resources["CalendarLibraryPartialStyle"],
            _ => (Style)Resources["CalendarLibraryStyle"]
        };
        PresentDetails(episode, library, MoviePilotCalendarPresentation.UpdatedText(entry, DateTimeOffset.Now, UiText.Translate),
            MoviePilotCalendarPresentation.RuntimeText(entry, UiText.Translate));
    }

    private void PresentAnime(AniBtCalendarEntry entry)
    {
        EventTitle.Text = entry.Title;
        var label = UiText.Translate(entry.IsPremiere ? entry.IsMovie ? "上映" : "首播" : "播出");
        PresentDetails(entry.AiringAt is { } airing ? $"{label} {airing:HH:mm}" : label, "", "", "");
    }

    private void PresentDetails(string episode, string library, string updated, string runtime)
    {
        SetDetail(EpisodeText, episode);
        SetDetail(LibraryText, library);
        SetDetail(UpdatedText, updated);
        SetDetail(RuntimeText, runtime);
        UpdateDetailVisibility();
        var description = string.Join(UiText.Translate("，"), new[] { EventTitle.Text, episode, library, updated, runtime }
            .Where(text => text.Length > 0));
        ToolTipService.SetToolTip(this, description);
        AutomationProperties.SetName(this, description);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Content);
    }

    private static void SetDetail(TextBlock label, string text)
    {
        label.Text = text;
        label.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDetailVisibility()
    {
        var compact = PosterFrame.Visibility == Visibility.Collapsed;
        LibraryText.Visibility = !compact && LibraryText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatedText.Visibility = !compact && UpdatedText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RuntimeText.Visibility = !compact && RuntimeText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPosterViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var viewport = args.EffectiveViewport;
        _inViewport = viewport.Width > 0 && viewport.Height > 0
            && sender.ActualWidth > 0 && sender.ActualHeight > 0
            && viewport.X < sender.ActualWidth && viewport.Y < sender.ActualHeight
            && viewport.X + viewport.Width > 0 && viewport.Y + viewport.Height > 0;
        StartVisiblePoster();
    }

    private void StartVisiblePoster()
    {
        if (!_inViewport || PosterFrame.Visibility != Visibility.Visible || _posterAttempted
            || _posterLifetime.IsCancellationRequested || _poster is not { Length: > 0 } poster
            || _loadImage is not { } loadImage) return;
        _posterAttempted = true;
        _load = CancellationTokenSource.CreateLinkedTokenSource(_posterLifetime);
        _ = LoadImageAsync(poster, loadImage, _load);
    }

    private async Task LoadImageAsync(string url, Func<string, CancellationToken, Task<BitmapImage?>> loadImage,
        CancellationTokenSource pending)
    {
        try
        {
            var image = await loadImage(url, pending.Token);
            if (!ReferenceEquals(_load, pending) || pending.IsCancellationRequested) return;
            PosterImage.Source = image;
            PosterPlaceholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_load, pending))
            {
                _load = null;
                pending.Dispose();
            }
        }
    }

    internal void RetryPoster()
    {
        if (_load is not null || PosterImage.Source is not null) return;
        _posterAttempted = false;
        StartVisiblePoster();
    }

    internal void Release()
    {
        _entry = null;
        _poster = null;
        _loadImage = null;
        _inViewport = false;
        ClearPoster();
    }

    private void ClearPoster()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
        _posterAttempted = false;
        PosterImage.Source = null;
        PosterPlaceholder.Visibility = Visibility.Visible;
    }

    private sealed partial class CalendarEventAutomationPeer(MoviePilotCalendarEventCard owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}
