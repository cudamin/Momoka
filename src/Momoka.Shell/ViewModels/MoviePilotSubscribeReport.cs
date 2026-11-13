using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

/// <summary>季选择弹窗交回来的一趟：用户自己关掉了，还是真的提交过一遍。</summary>
internal enum MoviePilotSubscribeReportKind
{
    Cancelled,
    Succeeded,
    Failed
}

/// <summary>
/// 季选择弹窗关窗之后交回给页面的那句话（2026-11-13 用户令「点完订阅直接退出这个弹窗，然后再提示订阅是否成功」）。
/// <para>
/// 弹窗不再把结果留在自己身上：一点「提交订阅」它就收起来，写入在窗外继续，成败用这份记录带回来，由页面放进
/// 自己的提示条。调用方拿到的是一句可以直接上屏的话，不必各自去拼「哪几季成了、哪一季为什么没成」。
/// </para>
/// <para>
/// 单独成文件而不是挂在 <see cref="MoviePilotMediaViewModel"/> 旁边：季选择弹窗、搜索页和订阅页都过这一份，
/// 链接生产源码的测试项目也只需要它，不必为了一个返回类型把整个媒体视图模型拖进来。
/// </para>
/// </summary>
internal sealed record MoviePilotSubscribeReport(MoviePilotSubscribeReportKind Kind, string? NoticeTitle, string NoticeMessage, InfoBarSeverity Severity)
{
    /// <summary>用户没提交（点取消、点窗外、断线被收走）：页面上一个字都不说。</summary>
    internal static MoviePilotSubscribeReport Cancelled { get; } = new(MoviePilotSubscribeReportKind.Cancelled, null, "", InfoBarSeverity.Informational);

    internal bool Succeeded => Kind == MoviePilotSubscribeReportKind.Succeeded;
}
