using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.LocalMedia;
using Momoka.Shell.Media;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 媒体库墙上的一张卡：同一部作品的所有已入库文件。电影通常一条，剧集按媒体库索引里的条目聚合。
/// 只读本机索引，不向任何服务器要数据；点开时交给详情页（与 Emby 条目同一条路）。
/// </summary>
public sealed partial class LocalLibraryGroup : ObservableObject
{
    private CancellationTokenSource? _loading;
    private bool _wanted;

    public LocalLibraryGroup(IReadOnlyList<LocalMediaLibraryEntry> entries)
    {
        Entries = entries;
        var first = entries[0];
        var metadata = first.Metadata;
        Id = entries.Count == 1 ? first.Id : LocalMediaEntryArtwork.GroupKey(first);
        LibraryId = first.LibraryId;
        Title = metadata.Title;
        var year = metadata.Year?.ToString() ?? "";
        if (metadata.MediaType == LocalMediaType.Episode)
        {
            var seasons = entries.Select(entry => entry.Metadata.Season ?? 0).Distinct().Order().ToArray();
            var season = seasons.Length == 1 ? (seasons[0] == 0 ? "特别篇" : $"第 {seasons[0]} 季") : $"{seasons.Length} 季";
            KindText = season + $" · {entries.Count} 集";
        }
        else
        {
            KindText = "电影";
        }
        Subtitle = string.Join(" · ", new[] { year, KindText }.Where(value => value.Length > 0));
        Status = Incomplete ? "已入库 · 部分附属文件未完成" : "已入库";
        PosterPath = entries
            .Select(LocalMediaEntryArtwork.PosterPath)
            .Select(LocalLibraryArtwork.NormalizePath)
            .FirstOrDefault(path => path.Length > 0)
            ?? entries.Select(LocalMediaEntryArtwork.ThumbPath)
                .Select(LocalLibraryArtwork.NormalizePath)
                .FirstOrDefault(path => path.Length > 0)
            ?? "";
        ApplyUserState();
    }

    public IReadOnlyList<LocalMediaLibraryEntry> Entries { get; private set; }
    public string Id { get; }
    public string LibraryId { get; }
    public string Title { get; }
    public string KindText { get; }
    public string Subtitle { get; }
    public string Status { get; }
    public bool Incomplete => Entries.Any(entry => !entry.Complete);
    public bool IsSeries => Entries[0].Metadata.MediaType == LocalMediaType.Episode;
    public bool IsMovie => !IsSeries;
    public string PosterPath { get; }
    public DateTimeOffset AddedAt => Entries.Max(entry => entry.AddedAt);

    /// <summary>整部作品的已看：每一集都看过才算看过，和 Emby 剧卡上的对勾同一个口径。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchedVisibility), nameof(NotWatchedVisibility), nameof(WatchedActionTip))]
    public partial bool Watched { get; set; }

    /// <summary>整部作品的收藏：任意一条收藏就亮红心；点一下按作品分组一起改。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteVisibility), nameof(NotFavoriteVisibility), nameof(FavoriteActionTip))]
    public partial bool Favorite { get; set; }

    public Visibility WatchedVisibility => Watched ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotWatchedVisibility => Watched ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FavoriteVisibility => Favorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NotFavoriteVisibility => Favorite ? Visibility.Collapsed : Visibility.Visible;
    public string WatchedActionTip => Watched ? "标记为未观看" : "标记为已观看";
    public string FavoriteActionTip => Favorite ? "取消收藏" : "添加到收藏";

    internal void ApplyUserState()
    {
        Watched = Entries.Count > 0 && Entries.All(entry => entry.Watched);
        Favorite = Entries.Any(entry => entry.Favorite);
    }

    /// <summary>
    /// 刚在卡上点了已看或收藏：先把这张卡手上的条目快照改成同样的状态，
    /// 点开详情不必等索引通知排到 UI 线程才看到新值。
    /// </summary>
    internal void SetUserState(bool? watched, bool? favorite)
    {
        Entries = Entries.Select(entry => entry with
        {
            Watched = watched ?? entry.Watched,
            Favorite = favorite ?? entry.Favorite
        }).ToArray();
        ApplyUserState();
    }

    /// <summary>同一张卡换了一轮条目快照（已看、收藏、完成状态可能变了），保留卡本身与已解出来的图。</summary>
    internal void RefreshEntries(IReadOnlyList<LocalMediaLibraryEntry> entries)
    {
        Entries = entries;
        ApplyUserState();
    }

    /// <summary>同步比对用的指纹：条目集合或任一条目的完成状态变了，这张卡就要重造。</summary>
    internal string Stamp => string.Join("\n", Entries.Select(entry =>
        $"{entry.Id}|{entry.Complete}|{LocalMediaEntryArtwork.PosterPath(entry)}|{entry.AddedAt.UtcTicks}"));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayVisibility))]
    public partial bool CanPlay { get; set; }
    public Visibility PlayVisibility => CanPlay ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial BitmapImage? Poster { get; set; }

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
            var bitmap = await PosterLoader.DecodeAsync(bytes, 340);
            if (Current()) Poster = bitmap;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException
            or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // 缺失或坏图保留占位图；墙上的其他卡片照常可读。
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

/// <summary>
/// 按 <see cref="LocalMediaEntryArtwork.GroupKey"/> 聚合已入库条目，供页面直接迭代。
/// </summary>
internal static class LocalLibraryGrouping
{
    internal static IReadOnlyList<LocalLibraryGroup> Build(IEnumerable<LocalMediaLibraryEntry> entries) => entries
        .GroupBy(LocalMediaEntryArtwork.GroupKey, StringComparer.Ordinal)
        .Select(group => new LocalLibraryGroup(group
            .OrderBy(entry => entry.Metadata.Season ?? 0)
            .ThenBy(entry => entry.Metadata.Episode ?? 0)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray()))
        .OrderBy(group => group.Title, StringComparer.CurrentCulture)
        .ThenBy(group => group.Entries[0].Metadata.Year ?? 0)
        .ToArray();
}
