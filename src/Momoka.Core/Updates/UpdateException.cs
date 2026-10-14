using System.Net;

namespace Momoka.Updates;

/// <summary>
/// 检查更新这一趟没成。形状照 <see cref="MoviePilot.MoviePilotException"/> 与
/// <see cref="Emby.EmbyApiException"/>：消息已经写成给用户看的样子，状态码留给日志。
/// <para>
/// 两种失败要分开看：连不上发布页（没网、被挡）和发布页答了但答的不是要的东西。前者用户自己能判断（多半没网），
/// 后者是发布页那边的问题。两句话分别写清楚，比统一一句「检查更新失败」有用 —— 而这一趟失败在启动时是静默的，
/// 用户唯一看得到它的时候就是设置页里手动按那一下。
/// </para>
/// </summary>
public sealed class UpdateException(string message, HttpStatusCode? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}
