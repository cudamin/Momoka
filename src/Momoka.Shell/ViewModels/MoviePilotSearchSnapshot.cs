using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

/// <summary>返回栈只保留媒体数据，不保留页面、命令、图片、订阅写入状态或单例事件。</summary>
internal sealed record MoviePilotSearchSnapshot(string ConnectionStamp, string Term,
    IReadOnlyList<MoviePilotMedia> Results, string EmptyNotice, bool ShowEmptyNotice,
    string? NoticeTitle, string? NoticeMessage, InfoBarSeverity NoticeSeverity, bool NoticeOpen);
