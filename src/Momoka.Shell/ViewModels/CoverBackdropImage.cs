using Momoka.Diagnostics;
using Momoka.Playback;
using Momoka.Theming;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 把遮罩背景图字节解成 DWM 覆盖层的 BGRA 像素，烘法与遮罩一致见 CoverArtwork。
/// 服务器请求宽度不约束实际文件：客户端先限制两边及总像素，整个解码/烘色明确离开 UI。
/// WinRT 的 BitmapDecoder/流不依赖 XAML；调用者仍在原 UI 上下文创建 BitmapImage 并检查当前需求。
/// 解码失败退回纯色，取消直接传播而不伪装成「没图」。限的是交出的像素，不承诺编解码器内部零大图分配。
/// </summary>
internal static class CoverBackdropImage
{
    internal static Task<VideoFrame?> DecodeAsync(byte[] bytes, ThemeColor backdrop,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DecodeInBackgroundAsync(bytes, backdrop, cancellationToken), cancellationToken);

    private static async Task<VideoFrame?> DecodeInBackgroundAsync(byte[] bytes, ThemeColor backdrop,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
                // writer 收摊不能关掉下面仍要用的流。
                writer.DetachStream();
            }

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var size = CoverBackdropSize.Fit(decoder.PixelWidth, decoder.PixelHeight);
            if (size.Width == 0 || size.Height == 0) return null;

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)size.Width,
                ScaledHeight = (uint)size.Height,
                InterpolationMode = BitmapInterpolationMode.Fant
            };
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)
                .AsTask(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var pixels = data.DetachPixelData();
            var stride = checked(size.Width * 4);
            // BGRA8 is tightly packed. Reject a decoder result with an unexpected/unbounded tail too.
            if ((long)stride * size.Height != pixels.Length) return null;

            // Task.Run above is the thread guarantee even when every preceding await completes inline.
            // Bake is at most 921,600 pixels; a superseded run never publishes its bounded result.
            CoverArtwork.Bake(pixels, size.Width, size.Height, stride, backdrop);
            cancellationToken.ThrowIfCancellationRequested();
            return new VideoFrame(size.Width, size.Height, stride, pixels,
                new VideoPresentation.Rect(0, 0, size.Width, size.Height));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Log.Info("播放", $"遮罩背景图没解出覆盖层那份（{error.Message}），起播整屏退回纯色");
            return null;
        }
    }
}
