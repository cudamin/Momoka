using Microsoft.Extensions.DependencyInjection;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    /// <summary>
    /// 删掉媒体库文件之后，当前页手里的条目和海报已经过期。主页原地把这一件连同它的相关媒体摘掉，不重读整页 ——
    /// 重读会换掉一页海报并把滚动位置推回顶上（用户令 2026-11-03：删除后保持当前页与滚动位置，用动画衔接）。
    /// 详情页说的就是被删的那一件时回主页，别的详情页与媒体库页重读本页。其他页面不动，回主页时本来就会重新读。
    /// <para>
    /// 动手之前先记进 <see cref="RemovedMedia"/>：Emby 的元数据可能还没随文件一起消失，回主页那一趟读回来的
    /// 仍会有它；这份记忆是进程内的，关掉 Momoka 即清空。
    /// </para>
    /// <para>
    /// 这一份单独成文件：<c>Momoka.TransferSubmissionTests</c> 只链接 <c>ShellPage.MoviePilotMediaManagement.cs</c>
    /// 来测提交与防重，那里没有主页、详情页和 Frame 的替身；刷新是真实页面的职责，由外壳探针验收。
    /// </para>
    /// </summary>
    private async Task RefreshAfterMoviePilotLibraryDeleteAsync(EmbyItem item)
    {
        _services?.GetRequiredService<RemovedMedia>().Remember(item);

        switch (ContentFrame.Content)
        {
            case HomePage home:
                await home.RemoveAsync(item);
                break;
            case DetailPage detail when Deleted(detail.ViewModel.CurrentItem, item):
                GoTo("home", force: true);
                if (ContentFrame.Content is HomePage landed) await landed.RemoveAsync(item);
                break;
            case DetailPage detail:
                await detail.ViewModel.ReloadAsync();
                break;
            case LibraryPage library:
                await library.ViewModel.ReloadAsync();
                break;
        }

        static bool Deleted(EmbyItem? current, EmbyItem removed) => current is not null &&
            (current.Id == removed.Id || current.SeriesId == removed.Id ||
                removed.Type == EmbyItemType.Season && current.SeasonId == removed.Id);
    }
}
