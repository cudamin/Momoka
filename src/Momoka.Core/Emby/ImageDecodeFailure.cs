namespace Momoka.Emby;

/// <summary>只判断解码失败；传输、取消、权限和内存失败不允许删除图片缓存。</summary>
public static class ImageDecodeFailure
{
    public static bool CanRefetch(Exception error) => ProvesInvalidData(error)
        || !IsProtected(error) && unchecked((uint)error.HResult) is 0x88982F60 or 0x88982F07;

    public static bool ProvesInvalidData(Exception error)
    {
        if (IsProtected(error)) return false;
        if (error is InvalidDataException) return true;
        // WIC BADHEADER, FRAMEMISSING, BADMETADATAHEADER, BADSTREAMDATA are data failures.
        // BADIMAGE can also be raised under low memory (Microsoft BitmapSource documentation):
        // retry it once without deleting the original, and persist a replacement only after decoding.
        return unchecked((uint)error.HResult) is 0x88982F61 or 0x88982F62 or 0x88982F63 or 0x88982F70;
    }

    private static bool IsProtected(Exception error) => error is OperationCanceledException
        or UnauthorizedAccessException or OutOfMemoryException or ArgumentException or EmbyApiException;
}
