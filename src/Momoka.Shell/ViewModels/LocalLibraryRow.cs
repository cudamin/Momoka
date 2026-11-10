using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.LocalMedia;
using Momoka.Shell.Media;

namespace Momoka.Shell.ViewModels;

public sealed partial class LocalLibraryRow : ObservableObject
{
    private CancellationTokenSource? _loading;
    private bool _wanted;

    public LocalLibraryRow(LocalMediaLibraryEntry entry)
    {
        Entry = entry;
        Title = entry.Metadata.Title;
        var year = entry.Metadata.Year?.ToString() ?? "";
        var episode = entry.Metadata.MediaType == LocalMediaType.Episode
            ? $"第 {entry.Metadata.Season ?? 0} 季 · 第 {entry.Metadata.Episode ?? 0} 集" : "电影";
        MetadataText = string.Join(" · ", new[] { year, episode }.Where(value => value.Length > 0));
        EpisodeTitle = entry.Metadata.EpisodeTitle;
        TargetPath = entry.TargetPath;
        Status = entry.Complete ? "已入库" : "已入库 · 部分附属文件未完成";
        PosterPath = PosterPathFor(entry);
    }

    public LocalMediaLibraryEntry Entry { get; }
    public string Id => Entry.Id;
    public string Title { get; }
    public string MetadataText { get; }
    public string EpisodeTitle { get; }
    public string TargetPath { get; }
    public string Status { get; }
    public string PosterPath { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayVisibility))]
    public partial bool CanPlay { get; set; }
    public Visibility PlayVisibility => CanPlay ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial BitmapImage? Poster { get; set; }

    internal static string PosterPathFor(LocalMediaLibraryEntry entry) => entry.Outputs
        .Where(output => output.Success && output.Kind == LocalMediaOutputKind.Poster)
        .Select(output => LocalLibraryArtwork.NormalizePath(output.Path))
        .FirstOrDefault(path => path.Length > 0) ?? "";

    internal async Task EnsurePosterAsync()
    {
        _wanted = true;
        if (Poster is not null || _loading is not null || PosterPath.Length == 0) return;
        var loading = new CancellationTokenSource();
        _loading = loading;
        try
        {
            var bytes = await LocalLibraryArtwork.ReadAsync(PosterPath, loading.Token);
            if (!Current()) return;
            var bitmap = await PosterLoader.DecodeAsync(bytes, 96);
            if (Current()) Poster = bitmap;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException
            or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // 缺失或坏图保留占位图；列表数据和其他条目照常可读。
        }
        finally
        {
            if (ReferenceEquals(loading, _loading)) _loading = null;
            loading.Dispose();
        }
        bool Current() => _wanted && !loading.IsCancellationRequested && ReferenceEquals(loading, _loading);
    }

    internal void ReleasePoster()
    {
        _wanted = false;
        _loading?.Cancel();
        _loading = null;
        Poster = null;
    }
}
