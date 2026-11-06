using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Media;

namespace Momoka.Shell.ViewModels;

/// <summary>整理历史的展示行；原记录快照保留给 Core 的预览与提交核验。</summary>
public sealed partial class MoviePilotSeriesTransferRow(MoviePilotTransferHistory history) : ObservableObject
{
    private CancellationTokenSource? _artworkLoad;
    private bool _retired;
    private Func<string, CancellationToken, Task<byte[]>>? _fetchImage;
    internal void BindImages(Func<string, CancellationToken, Task<byte[]>> fetch) => _fetchImage ??= fetch;

    public MoviePilotTransferHistory History { get; } = history;
    public string Title => History.Title;
    public string EpisodeLabel => History.Seasons + History.Episodes;
    public string YearLabel => History.Year;
    public Visibility YearVisibility => History.Year.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SourcePath => History.SourcePath;
    public string TargetPath => History.TargetPath.Length > 0 ? History.TargetPath : "尚未整理到目标文件";
    public string SourceStorage => "源 · " + Storage(History.SourceStorage);
    public string TargetStorage => "目标 · " + Storage(History.TargetStorage);
    public string Status => History.Success ? "成功" : "失败";
    public Visibility SuccessVisibility => History.Success ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FailureVisibility => History.Success ? Visibility.Collapsed : Visibility.Visible;
    public string Mode => MoviePilotTransferHistoryView.ModeLabel(History.Mode);
    public string Details => string.Join(" · ", new[] { Mode, TimeFormat.FileSize(History.Size), History.Date }
        .Where(value => value.Length > 0));
    [ObservableProperty]
    public partial bool Selected { get; set; }

    public string AutomationName => $"{Title} {EpisodeLabel}，{Status}，记录 {History.Id}";

    public string SelectionName => "选择 " + AutomationName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyArtworkVisibility))]
    public partial BitmapImage? Artwork { get; set; }
    public Visibility EmptyArtworkVisibility => Artwork is null ? Visibility.Visible : Visibility.Collapsed;

    internal async Task LoadArtworkAsync(MoviePilotService service, string connection, CancellationToken lifetime)
    {
        if (_retired || Artwork is not null || _artworkLoad is not null || lifetime.IsCancellationRequested ||
            !Uri.TryCreate(History.Image, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) return;
        if (_fetchImage is null) return;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        pending.CancelAfter(TimeSpan.FromSeconds(12));
        _artworkLoad = pending;
        try
        {
            var bytes = await _fetchImage(url.AbsoluteUri, pending.Token).ConfigureAwait(true);
            var bitmap = await PosterLoader.DecodeAsync(bytes, 48).ConfigureAwait(true);
            if (!_retired && !pending.IsCancellationRequested && service.Available && service.IsCurrentConnection(connection))
                Artwork = bitmap;
        }
        catch (Exception) { /* 历史资源仍可核对与选择，图片失败只保留占位。 */ }
        finally { if (ReferenceEquals(_artworkLoad, pending)) _artworkLoad = null; }
    }

    internal void Retire()
    {
        _retired = true;
        _artworkLoad?.Cancel();
    }

    private static string Storage(string storage) => storage switch { "local" => "本地", "" => "存储未返回", _ => storage };
}
