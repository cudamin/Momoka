using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 订阅详情「剧集组」那一节里的一张选项卡：一组的分季分集方案。
/// <para>
/// 与 <see cref="MoviePilotSubscriptionEpisodeRow"/> 同一路数 —— 只读快照 + 一条命令，命令的可用性由外层
/// （能不能管理、是不是当前项）决定，<c>Refresh</c> 由视图模型在忙碌状态变化时统一叫一遍。
/// </para>
/// </summary>
public sealed partial class MoviePilotEpisodeGroupRow(
    MoviePilotEpisodeGroup group, bool current, Func<bool> canSwitch, Func<MoviePilotEpisodeGroupRow, Task> switchGroup)
    : ObservableObject
{
    public MoviePilotEpisodeGroup Group { get; } = group;
    public string Label => Group.Label;
    public string Summary => Group.Summary;

    /// <summary>服务器没给季数/集数时那一行不出现，卡片只剩名字 —— 不写「0 季 · 0 集」充数。</summary>
    public Visibility SummaryVisibility => Summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 订阅现在用的就是这一组：卡片压一圈强调色、前面挂一颗对勾，键也按不动（点了等于没改，不该再走一趟写请求）。
    /// </summary>
    public bool IsCurrent { get; } = current;
    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public bool CanSwitch => !IsCurrent && canSwitch();
    public string SwitchName => (IsCurrent ? "当前剧集组 · " : "切换到剧集组 · ") + Label;
    public string SwitchHelp => IsCurrent ? "正在浏览这一组"
        : "查看这一剧集组的季和集；提交订阅时使用所选剧集组";

    internal void Refresh()
    {
        OnPropertyChanged(nameof(CanSwitch));
        SwitchCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private Task SwitchAsync() => switchGroup(this);
}
