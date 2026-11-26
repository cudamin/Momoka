using System.Net;

namespace Momoka.MoviePilot;

/// <summary>只保留客户端生成的提示和 HTTP 状态。</summary>
public class MoviePilotException : Exception
{
    public MoviePilotException(string message, HttpStatusCode? status = null, Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
    }

    public HttpStatusCode? Status { get; }

    internal bool LegacyCalendarRoute { get; init; }
}

/// <summary>提交前校验失败，尚未发出实际整理请求。</summary>
public sealed class MoviePilotTransferBlockedException(string message, Exception? inner = null)
    : MoviePilotException(message, (inner as MoviePilotException)?.Status, inner: inner);

public sealed class MoviePilotOperationBlockedException(string message) : MoviePilotException(message);

internal sealed class MoviePilotSearchRejectedException() : MoviePilotException("MoviePilot 未能完成搜索，请稍后重试");

public sealed class MoviePilotMediaNotRecognizedException() : MoviePilotException("未识别到媒体信息。");

/// <summary>服务端确认尚未加入下载；用户可在高级选项核对媒体后重新提交。</summary>
public sealed class MoviePilotDownloadConfirmationRequiredException()
    : MoviePilotException("MoviePilot 尚未识别此资源，未加入下载；请重新打开下载，在高级选项核对媒体后提交。");

public sealed class MoviePilotOperationUncertainException(string message) : MoviePilotException(message);

/// <summary>Nothing answered at that address, or it answered too slowly. The one failure worth retrying.</summary>
public sealed class MoviePilotUnreachableException(string message, Exception? inner = null)
    : MoviePilotException(message, inner: inner);

/// <summary>
/// A 401 on something other than the sign-in call, i.e. the token has run out.
/// <para>
/// Its own type because the fix differs from every other failure: the address is fine and the credentials
/// were fine — the JWT simply has an expiry, so the client signs in again and replays.
/// </para>
/// </summary>
public sealed class MoviePilotTokenExpiredException(string message)
    : MoviePilotException(message, HttpStatusCode.Unauthorized);
