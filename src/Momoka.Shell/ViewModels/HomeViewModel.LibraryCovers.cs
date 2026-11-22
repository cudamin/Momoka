using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Media;

namespace Momoka.Shell.ViewModels;

public sealed partial class HomeViewModel
{
    /// <summary>
    /// 已经发布过内容的库排。库排还没落屏时先不取预设图：否则会先拿库自己那张封面复制九份，
    /// 等真海报回来再整体换一遍 —— 启动时看到的「封面里的封面在动」就是这两趟凑出来的。
    /// </summary>
    private readonly HashSet<string> _settledLibraryRows = new(StringComparer.Ordinal);

    /// <summary>九宫格里的单张海报最多解到这么宽，与 Emby 那边的小图共用同一档。</summary>
    private const int LibraryCoverTileWidth = 130;

    internal void ApplyLibraryCovers(UiSettings ui)
    {
        if (!Live || !ReferenceEquals(_settings?.Settings.Ui, ui)) return;
        SyncLibraryCovers();
    }

    private void SyncLibraryCovers()
    {
        if (_settings is not { } settings) return;
        var shelf = _all.FirstOrDefault(entry => entry.Row.Key == HomeLayout.Libraries).Shelf;
        if (shelf is null) return;
        var ui = settings.Settings.Ui;
        var preset = ui.HomeLibraryCoverOverride ? HomeLibraryCoverPresets.Normalize(ui.HomeLibraryCoverPreset) : null;
        foreach (var card in shelf.Cards.Materialized)
        {
            // 订阅入口是导航卡，海报来自 MoviePilot 而不是媒体服务器：按订阅列表另取一组，
            // 不能退回那张已经合成好的单图（九宫格会把它复制九份）。这张表只装订阅入口。
            if (_moviePilotLibraries.TryGetValue(card.Item.Id, out var subscription))
            {
                ApplySubscriptionLibraryCover(subscription, preset);
                continue;
            }

            if (preset is null)
            {
                card.ApplyHomeLibraryCover(null, []);
                continue;
            }

            var rowKey = HomeLayout.LibraryKey(card.Item);
            if (card.IsNavigation || !_settledLibraryRows.Contains(rowKey))
            {
                card.ApplyHomeLibraryCover(preset, []);
                continue;
            }

            var samples = _all.Where(entry => entry.Row.Key == rowKey)
                .SelectMany(entry => entry.Shelf.Cards.Materialized).Select(sample => sample.Item);
            card.ApplyHomeLibraryCover(preset, HomeLibraryCoverArtwork.Select(card.Item, samples, preset));
        }
    }

    /// <summary>订阅入口的封面预设：取该分类下各条订阅的海报，取齐后一次性交给卡片。</summary>
    private void ApplySubscriptionLibraryCover(MoviePilotLibraryEntry entry, string? preset)
    {
        if (preset is null)
        {
            entry.CancelTiles();
            entry.Card.ApplyHomeLibraryCover(null, []);
            return;
        }
        if (preset == HomeLibraryCoverPresets.Gradient || entry.Urls.Count == 0)
        {
            entry.Card.ApplyHomeLibraryCoverImages(preset, []);
            return;
        }
        if (entry.Tiles is { Count: > 0 } tiles)
        {
            entry.Card.ApplyHomeLibraryCoverImages(preset, tiles);
            return;
        }
        // 取图期间先画渐变文字，不把合成封面复制成九份再换掉。
        entry.Card.ApplyHomeLibraryCoverImages(preset, []);
        _ = EnsureSubscriptionCoverTilesAsync(entry);
    }

    private async Task EnsureSubscriptionCoverTilesAsync(MoviePilotLibraryEntry entry)
    {
        if (entry.TileLoad is not null || entry.Tiles is { Count: > 0 }) return;
        if (entry.TileFailedAt != default && !ImageCachePolicy.WorthRetrying(entry.TileFailedAt, DateTime.UtcNow)) return;
        var lifetime = new CancellationTokenSource();
        entry.TileLifetime = lifetime;
        var load = MoviePilotLibraryCoverLoader.LoadTilesAsync(entry.Urls, entry.Fetch,
            Math.Min(CardSize.LibraryWidth, LibraryCoverTileWidth), lifetime.Token);
        entry.TileLoad = load;
        try
        {
            var tiles = await load.ConfigureAwait(true);
            if (lifetime.IsCancellationRequested || !_active || !ReferenceEquals(entry.TileLifetime, lifetime)
                || !_moviePilotLibraries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry)) return;
            if (tiles.Count == 0) { entry.TileFailedAt = DateTime.UtcNow; return; }
            entry.Tiles = tiles;
            entry.TileFailedAt = default;
            ApplySubscriptionLibraryCover(entry, CurrentLibraryCoverPreset());
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (lifetime.IsCancellationRequested) return;
            entry.TileFailedAt = DateTime.UtcNow;
            Log.Info("主页", $"加载订阅入口封面失败（{entry.Key}）：{error.Message}");
        }
        finally
        {
            if (ReferenceEquals(entry.TileLifetime, lifetime)) entry.TileLifetime = null;
            if (ReferenceEquals(entry.TileLoad, load)) entry.TileLoad = null;
            lifetime.Dispose();
        }
    }

    private string? CurrentLibraryCoverPreset()
    {
        if (_settings?.Settings.Ui is not { } ui) return null;
        return ui.HomeLibraryCoverOverride ? HomeLibraryCoverPresets.Normalize(ui.HomeLibraryCoverPreset) : null;
    }
}
