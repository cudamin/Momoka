using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.ViewModels;

/// <summary>整理历史的展示行；原记录快照保留给 Core 的预览与提交核验。</summary>
public sealed partial class MoviePilotSeriesTransferRow(MoviePilotTransferHistory history) : ObservableObject
{
    private CancellationTokenSource? _artworkLoad;
    private bool _retired;

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
    public string Mode => History.Mode switch
    {
        "copy" => "复制",
        "move" => "移动",
        "link" => "硬链接",
        "softlink" => "软链接",
        "" => "方式未返回",
        var mode => mode
    };
    public string Details => string.Join(" · ", new[] { Mode, TimeFormat.FileSize(History.Size), History.Date }
        .Where(value => value.Length > 0));
    public string AutomationName => $"{Title} {EpisodeLabel}，{Status}，记录 {History.Id}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyArtworkVisibility))]
    public partial BitmapImage? Artwork { get; set; }
    public Visibility EmptyArtworkVisibility => Artwork is null ? Visibility.Visible : Visibility.Collapsed;

    internal async Task LoadArtworkAsync(MoviePilotService service, string connection, CancellationToken lifetime)
    {
        if (_retired || Artwork is not null || _artworkLoad is not null || lifetime.IsCancellationRequested ||
            !Uri.TryCreate(History.Image, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) return;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        pending.CancelAfter(TimeSpan.FromSeconds(12));
        _artworkLoad = pending;
        try
        {
            var bytes = await service.FetchImageAsync(url.AbsoluteUri, pending.Token).ConfigureAwait(true);
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
