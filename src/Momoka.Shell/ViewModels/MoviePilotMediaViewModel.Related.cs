using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Shell.Media;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotRelatedCard(string title, string subtitle, Action open) : ObservableObject
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArtworkHeight))]
    public partial double CardWidth { get; set; } = 128;
    public double ArtworkHeight => CardWidth * 204 / 142;
    [ObservableProperty] public partial BitmapImage? Poster { get; set; }
    public IRelayCommand OpenCommand { get; } = new RelayCommand(open);
}

public sealed partial class MoviePilotMediaViewModel
{
    private double _relatedCardWidth = 128;

    // 八张紧凑卡随正文宽度收缩，窄窗保留可读下限；仅更新尺寸，不重建内容或滚动位置。
    internal void ResizeRelatedCards(double width)
    {
        if (width <= 0) return;
        _relatedCardWidth = Math.Clamp((width - 24 - 7 * 12) / 8, 100, 128);
        foreach (var card in People.Concat(Recommendations).Concat(Similar)) card.CardWidth = _relatedCardWidth;
    }

    public ObservableCollection<MoviePilotRelatedCard> People { get; } = [];
    public ObservableCollection<MoviePilotRelatedCard> Recommendations { get; } = [];
    public ObservableCollection<MoviePilotRelatedCard> Similar { get; } = [];
    public Visibility PeopleVisibility => Show(People.Count > 0);
    public Visibility RecommendationsVisibility => Show(Recommendations.Count > 0);
    public Visibility SimilarVisibility => Show(Similar.Count > 0);
    internal Action<Momoka.MoviePilot.MoviePilotMedia>? OpenMedia { get; set; }
    internal Action<string>? OpenPerson { get; set; }

    private async Task LoadRelatedAsync(CancellationToken token)
    {
        if (Detail is null || _service is null || _picker) return;
        var detail = Detail;
        async Task PeopleAsync()
        {
            try
            {
                var people = await _service.MediaPeopleAsync(detail.Media, token);
                if (!Live || token.IsCancellationRequested || !ReferenceEquals(detail, Detail)) return;
                People.Clear();
                foreach (var person in people)
                {
                    var card = new MoviePilotRelatedCard(person.Name, person.Character, () => { if (Live) OpenPerson?.Invoke(person.Url); }) { CardWidth = _relatedCardWidth };
                    People.Add(card); _ = LoadRelatedImageAsync(card, person.ImageUrl, token);
                }
                OnPropertyChanged(nameof(PeopleVisibility));
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
        async Task RelatedAsync(bool similar)
        {
            try
            {
                var items = await _service.RelatedMediaAsync(detail.Media, similar, token);
                if (!Live || token.IsCancellationRequested || !ReferenceEquals(detail, Detail)) return;
                var rows = similar ? Similar : Recommendations;
                rows.Clear();
                foreach (var item in items)
                {
                    var card = new MoviePilotRelatedCard(item.Display, item.Type ?? "", () => { if (Live) OpenMedia?.Invoke(item); }) { CardWidth = _relatedCardWidth };
                    rows.Add(card); _ = LoadRelatedImageAsync(card, item.PosterUrl ?? "", token);
                }
                OnPropertyChanged(similar ? nameof(SimilarVisibility) : nameof(RecommendationsVisibility));
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
        await Task.WhenAll(PeopleAsync(), RelatedAsync(false), RelatedAsync(true));
    }

    private async Task LoadRelatedImageAsync(MoviePilotRelatedCard card, string url, CancellationToken token)
    {
        if (_service is null || url.Length == 0) return;
        try
        {
            if (_fetchImage is null) return;
            var bytes = await _fetchImage(url, token);
            var image = await PosterLoader.DecodeAsync(bytes, 150);
            if (Live && !token.IsCancellationRequested && (People.Contains(card) || Recommendations.Contains(card) || Similar.Contains(card))) card.Poster = image;
        }
        catch (Exception) { }
    }

    private void ClearRelated()
    {
        People.Clear(); Recommendations.Clear(); Similar.Clear();
        OnPropertyChanged(nameof(PeopleVisibility)); OnPropertyChanged(nameof(RecommendationsVisibility)); OnPropertyChanged(nameof(SimilarVisibility));
    }
}
