using Momoka.MoviePilot;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell.Media;

/// <summary>订阅入口复用已加载的订阅海报，合成一张封面后走导航卡原有的取消与释放通道。</summary>
internal static class MoviePilotLibraryCoverLoader
{
    public static async Task<byte[]> LoadAsync(IReadOnlyList<string> urls,
        Func<string, CancellationToken, Task<byte[]>> fetch, CancellationToken token)
    {
        var pictures = new List<MoviePilotLibraryArtwork.ArtworkPixels>();
        foreach (var url in urls)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var bytes = await fetch(url, token).ConfigureAwait(true);
                var picture = await DecodeAsync(bytes, token).ConfigureAwait(true);
                if (picture is not null) pictures.Add(picture);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // 图片服务的单次超时不等于整个卡片取消，后续海报仍可用。
            }
            catch (Exception error) when (error is IOException or ArgumentException or HttpRequestException
                or MoviePilotException or System.Runtime.InteropServices.COMException)
            {
                // 单张失效不挡住其余海报；全部失败交还导航卡的失败冷却，避免滚动时重复请求。
            }
        }
        token.ThrowIfCancellationRequested();
        if (pictures.Count == 0) throw new IOException("订阅入口封面暂时不可用。");
        var cover = await Task.Run(() => MoviePilotLibraryArtwork.Compose(pictures, token), token).ConfigureAwait(true);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)cover.Width, (uint)cover.Height, 96, 96, cover.Pixels);
        await encoder.FlushAsync();
        token.ThrowIfCancellationRequested();
        output.Seek(0);
        var result = new byte[checked((int)output.Size)];
        using var reader = new DataReader(output);
        await reader.LoadAsync((uint)result.Length);
        reader.ReadBytes(result);
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static async Task<MoviePilotLibraryArtwork.ArtworkPixels?> DecodeAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length == 0) return null;
        token.ThrowIfCancellationRequested();
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        token.ThrowIfCancellationRequested();
        // 同时限制两维：异常长图也不能先解码成原始大位图再缩小。
        var scale = Math.Min(1d, MoviePilotLibraryArtwork.Width
            / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = Math.Max(1, (uint)Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        token.ThrowIfCancellationRequested();
        var buffer = new Windows.Storage.Streams.Buffer(checked((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4)));
        bitmap.CopyToBuffer(buffer);
        var pixels = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
        return new(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
    }
}
