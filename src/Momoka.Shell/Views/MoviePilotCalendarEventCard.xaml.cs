using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.MoviePilot;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotCalendarEventCard : UserControl
{
    private CancellationTokenSource? _load;

    public MoviePilotCalendarEventCard() => InitializeComponent();

    internal void Attach(MoviePilotCalendarEntry entry, Func<string, CancellationToken, Task<BitmapImage?>> loadImage,
        CancellationToken token)
    {
        Release();
        EventTitle.Text = entry.Title;
        EpisodeText.Text = MoviePilotCalendarPresentation.EpisodeLabel(entry);
        var state = MoviePilotCalendarPresentation.LibraryState(entry);
        var stateLabel = MoviePilotCalendarPresentation.LibraryStateText(entry);
        var progress = entry.LibraryEpisodeCount is { } count
            ? entry.LibraryTotalEpisodes is { } total ? $" ({count}/{total})" : $" ({count})" : "";
        LibraryText.Text = stateLabel + progress;
        LibraryRow.Visibility = entry.Type == "电影" ? Visibility.Collapsed : Visibility.Visible;
        LibraryBadge.Visibility = state == MoviePilotCalendarLibraryState.InLibrary ? Visibility.Visible : Visibility.Collapsed;
        LibraryIcon.Glyph = state == MoviePilotCalendarLibraryState.InLibrary ? "\uE73E" : "\uE738";
        var complete = state == MoviePilotCalendarLibraryState.InLibrary;
        var partial = state == MoviePilotCalendarLibraryState.PartiallyInLibrary;
        LibraryText.Visibility = LibraryIcon.Visibility = complete || partial ? Visibility.Collapsed : Visibility.Visible;
        CompleteText.Visibility = CompleteIcon.Visibility = complete ? Visibility.Visible : Visibility.Collapsed;
        PartialText.Visibility = PartialIcon.Visibility = partial ? Visibility.Visible : Visibility.Collapsed;
        CompleteText.Text = LibraryText.Text;
        PartialText.Text = LibraryText.Text;
        UpdatedText.Text = MoviePilotCalendarPresentation.UpdatedText(entry, DateTimeOffset.Now);
        UpdatedRow.Visibility = UpdatedText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RuntimeText.Text = MoviePilotCalendarPresentation.RuntimeText(entry);
        RuntimeText.Visibility = RuntimeText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var description = string.Join("，", new[] { entry.Title, EpisodeText.Text, LibraryRow.Visibility == Visibility.Visible ? LibraryText.Text : "", UpdatedText.Text, RuntimeText.Text }
            .Where(text => text.Length > 0));
        ToolTipService.SetToolTip(this, description);
        AutomationProperties.SetName(this, description);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Content);
        _load = CancellationTokenSource.CreateLinkedTokenSource(token);
        _ = LoadImageAsync(entry, loadImage, _load);
    }

    private async Task LoadImageAsync(MoviePilotCalendarEntry entry, Func<string, CancellationToken, Task<BitmapImage?>> loadImage,
        CancellationTokenSource pending)
    {
        try
        {
            var image = await loadImage(entry.Poster, pending.Token);
            if (!ReferenceEquals(_load, pending) || pending.IsCancellationRequested) return;
            PosterImage.Source = image;
            PosterPlaceholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* A failed poster leaves the title and all calendar metadata readable. */ }
    }

    internal void Release()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
        PosterImage.Source = null;
        PosterPlaceholder.Visibility = Visibility.Visible;
    }
}
