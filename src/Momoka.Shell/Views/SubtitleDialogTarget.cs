using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>一个已验证版本的独立字幕服务；切换只选这份委托，不从当前下拉值重新猜请求目标。</summary>
internal sealed class SubtitleDialogTarget
{
    internal required MediaSource Source { get; set; }
    internal required Func<ServerSubtitleSearch, Task<List<RemoteSubtitleInfo>>> Search { get; init; }
    internal required Func<RemoteSubtitleInfo, Task> Download { get; init; }
    internal required Func<MediaStream, Task> Delete { get; init; }
    internal Func<Task<MediaSource>>? Reload { get; init; }
    internal Action<MediaSource>? AcceptReload { get; init; }
    internal bool SupportsForcedOnly { get; init; } = true;
    internal bool DownloadProvidesConfirmation { get; init; } = true;
    internal Func<ServerSubtitleUpload, Task>? Upload { get; init; }
    internal Func<bool>? CanUpload { get; init; }
}
