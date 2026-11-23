using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Shell.Media;

/// <summary>
/// The posters already decoded, so scrolling back up is instant.
/// <para>
/// A card drops its bitmap when its container is recycled — it has to, or a long scroll ends up holding
/// every cover it ever passed. The cost was that scrolling back up did the whole job again: read the
/// file, decode it, wait for the picture to appear. Keeping the last few hundred here is the middle
/// answer. Native-size animated covers make a pixel-byte budget necessary.
/// </para>
/// <para>
/// Static, because the cache is about the process rather than about any one page: the same poster
/// appears on the home page, in a library grid and on a detail page's 更多类似 row, and all three should
/// find it already decoded. Only the UI thread touches it, and <see cref="BudgetCache{TKey,TValue}"/> locks
/// anyway.
/// </para>
/// </summary>
internal static class PosterCache
{
    /// <summary>
    /// Bound retained decoded pixels, including native-size GIF/APNG fallback images.
    /// </summary>
    private static readonly BudgetCache<string, BitmapImage> Decoded = new(64 * 1024 * 1024,
        bitmap => Math.Max(4L, (long)bitmap.PixelWidth * bitmap.PixelHeight * 4));
    private static readonly ConditionalWeakTable<BitmapImage, AnimationKey> Animations = new();
    private static readonly BudgetCache<AnimationKey, byte[]> AnimationBytes = new(64 * 1024 * 1024, bytes => bytes.Length);

    /// <summary>
    /// The name one decoded picture goes under. The decode width is part of it, not just the width asked
    /// of the server: the server's widths come in steps of 80, so a 170-wide poster card and a 200-wide
    /// one share a download but not a bitmap, and handing one the other's would show a picture decoded
    /// for a different size.
    /// </summary>
    public static string Key(EmbyConnection connection, string itemId, string imageType, string tag, int decodeWidth) =>
        EmbyImageStore.CacheKey(connection, itemId, imageType, tag, decodeWidth);

    public static bool TryGet(string key, [MaybeNullWhen(false)] out BitmapImage bitmap)
    {
        if (!Decoded.TryGet(key, out bitmap)) return false;
        if (!Animations.TryGetValue(bitmap, out var animation) || AnimationBytes.TryGet(animation, out _)) return true;

        // Its compressed animation was evicted. Let the card reload the original from the disk cache.
        Decoded.Remove(key);
        bitmap = null;
        return false;
    }

    public static void Remember(string key, BitmapImage bitmap) => Decoded.Set(key, bitmap);

    internal static void TrackAnimation(BitmapImage bitmap, byte[] bytes)
    {
        var key = Animations.GetValue(bitmap, static _ => new());
        AnimationBytes.Set(key, bytes);
    }

    internal static bool TryGetAnimation(BitmapImage bitmap, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        return Animations.TryGetValue(bitmap, out var key) && AnimationBytes.TryGet(key, out bytes);
    }

    internal static bool IsAnimated(BitmapImage bitmap) => bitmap.IsAnimatedBitmap || Animations.TryGetValue(bitmap, out _);

    /// <summary>Drops everything. 设置 → 清除图片缓存 also has to forget what is already decoded.</summary>
    public static void Clear()
    {
        Decoded.Clear();
        AnimationBytes.Clear();
    }

    private sealed class AnimationKey;
}
