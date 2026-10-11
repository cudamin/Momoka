using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.MoviePilot;
using Momoka.Infrastructure;
using Momoka.Shell.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotSubscriptionEpisodeRow(
    MoviePilotSubscriptionEpisode episode, Func<bool> canPlay, Func<MoviePilotSubscriptionEpisodeRow, Task> play,
    Func<bool> canSearch, Action<MoviePilotSubscriptionEpisodeRow> search) : ObservableObject
{
    private CancellationTokenSource? _artworkLoad;
    private bool _retired;
    public MoviePilotSubscriptionEpisode Episode { get; } = episode;
    public MoviePilotPlaybackTarget? Target { get; private set; }
    public string Label => Episode.Label;
    public string Title => Episode.Title;
    public string Description => Episode.Description;
    public string RowTitle => string.IsNullOrWhiteSpace(Title) ? Label : $"{Label}  {Title}";
    public double ArtworkWidth => CardSize.WideWidth;
    public double ArtworkHeight => CardSize.HeightFor(CardSize.WideWidth, true);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyArtworkVisibility))]
    public partial BitmapImage? Artwork { get; set; }
    public Visibility EmptyArtworkVisibility => Artwork is null ? Visibility.Visible : Visibility.Collapsed;
    public string Status => Episode.Status;

    /// <summary>
    /// 播出日期那一行（来自 TMDB，见 <see cref="MoviePilotEpisodeAirDates"/>）。取不到日期时整行不出现 ——
    /// 不写「未知」占位，因为「服务器没给」和「确实没有日期」在屏上该长得不一样。
    /// </summary>
    public string AirDateLabel => Episode.AirDateLabel;
    public Visibility AirDateVisibility => AirDateLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool InLibrary => Episode.InLibrary;
    public bool CanPlay => InLibrary && Target is not null && canPlay();
    public Visibility PlayVisibility => InLibrary ? Visibility.Visible : Visibility.Collapsed;
    public string PlayLabel => Target?.Item.HasResumePosition == true ? "继续播放" : "播放";
    public string PlayName => $"{PlayLabel} · {RowTitle}";
    public string PlayHelp => Target is null ? "当前 Emby 中尚未找到匹配的可播放条目，可刷新重试" : "使用当前 Emby 账号播放";

    /// <summary>只有已播出的集才给「搜索资源」：没播出的集搜也搜不到，摆一颗按不动的键只会让人以为坏了。</summary>
    public bool CanSearch => Episode.HasAired && canSearch();
    public Visibility SearchVisibility => Episode.HasAired ? Visibility.Visible : Visibility.Collapsed;
    public string SearchName => $"搜索资源 · {RowTitle}";
    public string SearchHelp => "在 MoviePilot 各站点搜索这一集的可下载资源";

    internal void SetTarget(MoviePilotPlaybackTarget? target) { Target = target; Refresh(); }
    internal void Refresh()
    {
        OnPropertyChanged(nameof(CanPlay)); OnPropertyChanged(nameof(PlayLabel)); OnPropertyChanged(nameof(PlayName)); OnPropertyChanged(nameof(PlayHelp));
        PlayCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSearch));
        SearchCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task PlayAsync() => play(this);
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search() => search(this);

    internal async Task LoadArtworkAsync(MoviePilotService service, CancellationToken lifetime)
    {
        if (_retired || Artwork is not null || _artworkLoad is not null || lifetime.IsCancellationRequested
            || !Uri.TryCreate(Episode.ArtworkUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) return;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        pending.CancelAfter(TimeSpan.FromSeconds(12));
        _artworkLoad = pending;
        var connection = service.ConnectionStamp;
        try
        {
            var bytes = await service.FetchImageAsync(url.AbsoluteUri, pending.Token);
            var bitmap = await PosterLoader.DecodeAsync(bytes, CardSize.WideWidth);
            if (!_retired && !pending.IsCancellationRequested && service.IsCurrentConnection(connection)) Artwork = bitmap;
        }
        catch (Exception) { /* 图片不可用时保留占位，不影响入库信息和播放。 */ }
        finally { if (ReferenceEquals(_artworkLoad, pending)) _artworkLoad = null; }
    }

    internal void Retire()
    {
        _retired = true;
        _artworkLoad?.Cancel();
        _artworkLoad = null;
        Artwork = null;
    }
}
