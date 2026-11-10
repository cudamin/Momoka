using System.Globalization;
using System.Text;
using Momoka.Playback;

namespace Momoka.Mpv;

/// <summary>Builds mpv's continuous EDL resource after each file has passed the transport boundary.</summary>
public static class MpvMediaResource
{
    public static string Build(PlaybackRequest request, Func<Uri, string>? map = null)
    {
        map ??= address => address.AbsoluteUri;
        if (request.MediaParts.Count == 0) return map(request.MediaUrl);
        var resource = new StringBuilder("edl://");
        for (var index = 0; index < request.MediaParts.Count; index++)
        {
            var part = request.MediaParts[index];
            if (part.DurationTicks <= 0) throw new InvalidOperationException("分段媒体缺少有效时长");
            var address = map(part.MediaUrl);
            if (address.Length == 0 || address.Any(char.IsControl))
                throw new InvalidOperationException("分段媒体地址无效");
            if (index > 0) resource.Append(';');
            // mpv counts UTF-8 bytes, not characters, and consumes the protected value literally.
            resource.Append('%').Append(Encoding.UTF8.GetByteCount(address)).Append('%').Append(address);
            // Omit start: mpv uses the file's native start_time rather than assuming zero timestamps.
            resource.Append(",length=").Append((part.DurationTicks / (decimal)TimeSpan.TicksPerSecond)
                .ToString(CultureInfo.InvariantCulture));
            // Match the existing source picker, whose tracks come from the first Part.
            if (index == 0) resource.Append(",layout=this");
        }
        return resource.ToString();
    }
}
