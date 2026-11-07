using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Shell.Media;

/// <summary>
/// 已解码的轮播背景。一程「取字节 → WIC 解码 → 左侧扩边 → BMP 编码 → <see cref="BitmapImage"/>」在
/// 1280 宽的图上要十几到二十几毫秒，而每次返回主页都会重建一批幻灯片；字节虽然在磁盘缓存里，这条处理链
/// 却一趟不落。这里把最后交出去的那张图按来源、图片身份和规则留住，返回时直接上屏。
/// <para>
/// 和 <see cref="PosterCache"/> 同一个模式：静态、只由界面线程使用、共享同一个 <see cref="BitmapImage"/>，
/// 条目按「最近用过」淘汰。区别只在封顶方式 —— 轮播图的高度随片源比例变，按张数封顶挡不住内存，所以走
/// <see cref="BudgetCache{TKey,TValue}"/> 的字节预算。
/// </para>
/// <para>
/// **键里必须有这几样**：服务器基址与用户（<see cref="EmbyImageStore.CacheKey"/> 已经带上）、条目、图种、
/// 图片 tag、请求宽度，以及处理规则。tag 变了说明服务器换过图，规则变了说明扩边/编码改了 —— 任何一样
/// 不同都不能把旧画面交给新请求。
/// </para>
/// </summary>
internal static class BannerBackdropCache
{
    /// <summary>
    /// 默认十张 16:9 背景各约 4.7 MB（1280 宽、扩边后 1472 宽、720 高），缓存按原像素和渲染结果的实际成本淘汰。
    /// 张数上限虽然可调到十五，但十五张同时解码本来就不在屏上；超出预算的会从最久没看的那张开始走。
    /// </summary>
    public const long BudgetBytes = 48L * 1024 * 1024;

    /// <summary>
    /// 处理规则的名字。扩边比例单独拼在后面，所以改比例本身就换键；这个版本号留给「同样的参数但算法换了」
    /// 的那一天，否则同一张图会顶着旧处理结果一直命中。
    /// </summary>
    private const string Rule = "carousel-backdrop-v5";

    private sealed record Entry(HeroPictureLoader.HeroPixels? Pixels, BitmapImage? Original, BitmapImage? Gaussian)
    {
        public long Cost => (Pixels?.Pixels.LongLength ?? 0) + (Original is null ? 0 : ImageCost(Original))
            + (Gaussian is null ? 0 : ImageCost(Gaussian));
    }
    private static readonly BudgetCache<string, Entry> Decoded = new(BudgetBytes, entry => entry.Cost);
    public static long Generation { get; private set; }

    /// <summary>缓存现在按成本记了多少字节。测试和读数用，不参与命中判断。</summary>
    public static long Bytes => Decoded.Bytes;

    /// <summary>
    /// 一张轮播背景的键。请求宽度先按 <see cref="EmbyImageStore.RequestWidth"/> 归到步进档，和磁盘缓存
    /// 用的是同一个数；解码尺寸由同一份字节和同一条规则唯一决定，条目的实际像素尺寸只用于预算。
    /// </summary>
    public static string Key(EmbyConnection connection, ArtworkRef artwork, int requestedWidth) =>
        EmbyImageStore.CacheKey(
            connection,
            artwork.ItemId,
            artwork.ImageType,
            artwork.Tag,
            EmbyImageStore.RequestWidth(requestedWidth))
        + "|" + Rule
        + "|" + CarouselBackdrop.LeftExtensionShare.ToString("0.###", CultureInfo.InvariantCulture);

    public static bool TryGet(string key, [MaybeNullWhen(false)] out BitmapImage bitmap) => TryGet(key, false, out bitmap);

    public static bool TryGet(string key, bool gaussian, [MaybeNullWhen(false)] out BitmapImage bitmap)
    {
        if (Decoded.TryGet(key, out var entry) && (gaussian ? entry.Gaussian : entry.Original) is { } image)
        {
            bitmap = image;
            return true;
        }
        bitmap = null;
        return false;
    }

    public static bool TryGetPixels(string key, [MaybeNullWhen(false)] out HeroPictureLoader.HeroPixels pixels)
    {
        if (Decoded.TryGet(key, out var entry) && entry.Pixels is { } source)
        {
            pixels = source;
            return true;
        }
        pixels = null;
        return false;
    }

    public static void RememberPixels(string key, HeroPictureLoader.HeroPixels pixels, long? generation = null)
    {
        if (generation is { } expected && expected != Generation) return;
        var entry = Decoded.TryGet(key, out var kept) ? kept : new Entry(null, null, null);
        Decoded.Set(key, entry with { Pixels = pixels });
    }

    public static void Remember(string key, BitmapImage bitmap) => Remember(key, bitmap, false);

    public static void Remember(string key, BitmapImage bitmap, bool gaussian, long? generation = null)
    {
        if (generation is { } expected && expected != Generation) return;
        var entry = Decoded.TryGet(key, out var kept) ? kept : new Entry(null, null, null);
        Decoded.Set(key, gaussian ? entry with { Gaussian = bitmap } : entry with { Original = bitmap });
    }

    /// <summary>设置里「清除图片缓存」也要忘掉这些，否则屏上还会从内存里画出来。</summary>
    public static void Clear()
    {
        Generation++;
        Decoded.Clear();
    }

    /// <summary>
    /// 解码后的表面按 BGRA 每像素四字节算。正常路径上 <see cref="BitmapImage.PixelWidth"/> 就是 BMP 的
    /// 实际尺寸；万一运行时还没填这个数，就退回解码请求宽度，宁可多算一点也不少算。
    /// </summary>
    private static long ImageCost(BitmapImage image)
    {
        var width = image.PixelWidth > 0 ? image.PixelWidth : image.DecodePixelWidth;
        var height = image.PixelHeight > 0 ? image.PixelHeight : Math.Max(1, width * 9 / 16);

        return Math.Max(1, width) * (long)Math.Max(1, height) * 4;
    }
}
