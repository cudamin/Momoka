using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.Media;

namespace Momoka.Shell.Views;

public sealed partial class CardItem
{
    private string? _homeLibraryCoverPreset;
    private IReadOnlyList<ArtworkRef> _homeLibraryCoverArtwork = [];
    private readonly List<BitmapImage> _homeLibraryCoverImages = [];
    private readonly HashSet<ArtworkRef> _missingHomeLibraryCoverImages = [];

    public string? HomeLibraryCoverPreset => _homeLibraryCoverPreset;
    public IReadOnlyList<BitmapImage> HomeLibraryCoverImages => _homeLibraryCoverImages;

    internal void ApplyHomeLibraryCover(string? preset, IReadOnlyList<ArtworkRef> artwork)
    {
        preset = preset is null ? null : HomeLibraryCoverPresets.Normalize(preset);
        if (_homeLibraryCoverPreset == preset && _homeLibraryCoverArtwork.SequenceEqual(artwork)) return;
        // 先撤销旧需求，旧解码即使迟到也不能给新风格回填。
        _loading?.Cancel();
        _loading = null;
        if (preset is not null)
        {
            Poster = null;
            ClearCollageTiles();
        }
        _homeLibraryCoverPreset = preset;
        _homeLibraryCoverArtwork = artwork.ToArray();
        _missingHomeLibraryCoverImages.Clear();
        _failedAt = default;
        ClearHomeLibraryCoverImages();
        Raise(nameof(HomeLibraryCoverPreset));
        RaiseImagePending();
        if (_wanted) _ = EnsurePosterAsync(_presentation);
    }

    private async Task EnsureHomeLibraryCoverAsync(Task? presentation)
    {
        if (Scope is not { IsAvailable: true } scope || _loading is not null
            || _homeLibraryCoverImages.Count + _missingHomeLibraryCoverImages.Count >= _homeLibraryCoverArtwork.Count)
            return;
        if (_failedAt != default && !ImageCachePolicy.WorthRetrying(_failedAt, DateTime.UtcNow)) return;
        var preset = _homeLibraryCoverPreset;
        var artwork = _homeLibraryCoverArtwork;
        var loading = new CancellationTokenSource();
        _loading = loading;
        var width = preset == HomeLibraryCoverPresets.Diagonal ? _width : Math.Min(_width, 130);
        try
        {
            foreach (var picture in artwork)
            {
                if (_missingHomeLibraryCoverImages.Contains(picture)) continue;
                var key = PosterCache.Key(scope.Connection, picture.ItemId, picture.ImageType, picture.Tag, width);
                BitmapImage? bitmap;
                try
                {
                    if (!PosterCache.TryGet(key, out bitmap))
                    {
                        presentation ??= BrowsePresentation.WaitForImagesAsync();
                        _presentation = presentation;
                        if (!await WaitForPresentationAsync(loading, scope, presentation).ConfigureAwait(true)) return;
                        bitmap = await _images.GetDecodedAsync(scope, picture.ItemId, picture.ImageType, picture.Tag,
                            EmbyImageStore.RequestWidth(width), bytes => PosterLoader.DecodeAsync(bytes, width), loading.Token)
                            .ConfigureAwait(true);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    if (!Current()) return;
                    // 单张失败不挡住其余格子；下一次容器重入在原有冷却后重试。
                    _failedAt = DateTime.UtcNow;
                    continue;
                }
                if (!Current()) return;
                if (bitmap is null) { _missingHomeLibraryCoverImages.Add(picture); continue; }
                PosterCache.Remember(key, bitmap);
                if (_wanted && !_homeLibraryCoverImages.Contains(bitmap))
                {
                    _homeLibraryCoverImages.Add(bitmap);
                    Raise(nameof(HomeLibraryCoverImages));
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_loading, loading)) _loading = null;
            loading.Dispose();
        }
        bool Current() => !loading.IsCancellationRequested && ReferenceEquals(_loading, loading)
            && scope.IsAvailable && preset == _homeLibraryCoverPreset && ReferenceEquals(artwork, _homeLibraryCoverArtwork);
    }

    private void ClearHomeLibraryCoverImages()
    {
        if (_homeLibraryCoverImages.Count == 0) return;
        _homeLibraryCoverImages.Clear();
        Raise(nameof(HomeLibraryCoverImages));
    }

    internal void ReleaseHomeLibraryCover()
    {
        if (_homeLibraryCoverPreset is null) return;
        _wanted = false;
        _resizeWanted = false;
        _loading?.Cancel();
        _loading = null;
        ClearHomeLibraryCoverImages();
    }
}
