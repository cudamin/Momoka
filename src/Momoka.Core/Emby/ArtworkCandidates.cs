using System.Net;

namespace Momoka.Emby;

/// <summary>按既有顺序选择第一张可用候选；回调在原调用上下文继续，以便 Shell 解码使用 UI 线程。</summary>
public static class ArtworkCandidates
{
    public static async Task<TImage?> FirstAsync<TCandidate, TImage>(IEnumerable<TCandidate> candidates,
        Func<TCandidate, CancellationToken, Task<TImage?>> load, CancellationToken cancellationToken)
        where TImage : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(load);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var image = await load(candidate, cancellationToken).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                if (image is not null) return image;
            }
            catch (Exception error) when (CanTryNext(error))
            {
                // 仅单张明确坏图/允许降级的传输失败继续；同刻取消也必须终止整趟选择。
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        return null;
    }

    private static bool CanTryNext(Exception error)
    {
        // 原生投影可能按 HRESULT 映射成不同 Exception 类型；先保护不能降级的生命周期/权限/资源错误。
        if (error is OperationCanceledException or UnauthorizedAccessException or OutOfMemoryException
            or ArgumentException or NullReferenceException or EmbyAuthenticationException or EmbyTokenExpiredException)
            return false;
        if (error is InvalidDataException or EmbyUnreachableException) return true;
        if (error is EmbyApiException api)
            return api.StatusCode == HttpStatusCode.NotFound || api.StatusCode is { } status && (int)status is >= 500 and <= 599;

        // Constants cross-checked against Microsoft Learn's WIC Codec Error Codes on 2026-10-23.
        // Windows SDK / WIC documented data-or-format failures only, not generic E_FAIL, stream IO,
        // E_ACCESSDENIED, E_ABORT, E_OUTOFMEMORY or arbitrary decoder/program exceptions.
        // WINCODEC_ERR_UNKNOWNIMAGEFORMAT, COMPONENTNOTFOUND, BADIMAGE, BADHEADER,
        // FRAMEMISSING, BADMETADATAHEADER, BADSTREAMDATA. Missing codec allows fallback, not cache deletion.
        return unchecked((uint)error.HResult) is 0x88982F07 or 0x88982F50 or 0x88982F60 or 0x88982F61
            or 0x88982F62 or 0x88982F63 or 0x88982F70;
    }
}
