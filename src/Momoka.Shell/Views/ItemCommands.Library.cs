using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Shell.Views;

/// <summary>
/// 媒体库卡片上网页端那一组命令 —— 播放、随机播放、转换、扫描媒体库文件。
/// <para>
/// 和条目自己的命令分开，只因这四条都作用在「整个库」上：播放要先把这个库展开成一列可播的视频再交给
/// 播放器（<see cref="ItemBatchPlayback.CollectAsync"/>，与多选播放同一条收集器），转换与扫描各自是一条
/// 服务器接口。菜单上哪几条成立归 <see cref="ItemMenu"/>；这里只做按下去之后真正发生的事。
/// </para>
/// </summary>
internal static partial class ItemCommands
{
    /// <summary>
    /// 播放 / 随机播放一张媒体库卡片：先把这个库里的视频收齐，再整列交给播放器。
    /// <para>
    /// 收集是异步的，而卡片可能在这段时间里被回收或换了账号 —— 收齐之后要按身份再核对一次；
    /// 取消与断线沿用会话的生命周期。空库在提示条上明说，不弹一张空播放页。
    /// </para>
    /// </summary>
    private static Task PlayLibraryAsync(EmbySessionScope session, IShellActions shell, CardItem card, bool shuffle) =>
        GuardAsync(shell, shuffle ? "随机播放失败" : "播放失败", async () =>
        {
            var item = card.Item;
            var identity = item.IdentityKey;
            bool Current() => card.IsCurrent && card.Item.IdentityKey == identity && session.IsAvailable;

            var collected = await ItemBatchPlayback.CollectAsync(session, [item], Current, session.Lifetime)
                .ConfigureAwait(true);
            if (!Current()) return;

            if (collected.Count == 0)
            {
                shell.Notify($"「{item.Name}」没有可播放的内容", InfoBarSeverity.Warning);
                return;
            }

            var queue = collected.ToArray();
            if (shuffle)
                for (var index = queue.Length - 1; index > 0; index--)
                {
                    var swap = Random.Shared.Next(index + 1);
                    (queue[index], queue[swap]) = (queue[swap], queue[index]);
                }

            await shell.PlayAsync(queue[0], episodes: new UserPlaybackQueue(queue)).ConfigureAwait(true);
        });

    /// <summary>
    /// 转换：照网页端那张表（目标、配置、质量以及自定义容器／编码）选好，再提交一个转换任务。
    /// <para>
    /// 这张表和媒体库多选那一头共用 <see cref="LibraryBatchDialogs"/>：同一组服务器选项、同一套校验，
    /// 只是所选项目从一张卡算起。表关掉、页面走了或账号换了都不再提交。
    /// </para>
    /// </summary>
    private static Task ConvertLibraryAsync(EmbySessionScope session, IShellActions shell, FrameworkElement owner, CardItem card) =>
        GuardAsync(shell, "转换失败", async () =>
        {
            if (Root(owner) is null) return;

            var item = card.Item;
            var identity = item.IdentityKey;
            var revision = session.PermissionRevision;
            var ids = new[] { item.Id };
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime);
            void Unloaded(object sender, RoutedEventArgs args) => lifetime.Cancel();
            owner.Unloaded += Unloaded;
            try
            {
                bool Current() => card.IsCurrent && card.Item.IdentityKey == identity
                    && session.IsAvailable && session.PermissionRevision == revision;

                var available = await AskAsync(session, (client, token) =>
                    client.GetConversionOptionsAsync(ids, null, token)).ConfigureAwait(true);
                if (!Current() || lifetime.IsCancellationRequested) return;

                var selection = await LibraryBatchDialogs.ConvertAsync(owner, 1, available,
                    (target, token) => session.ExecuteAsync((client, ct) =>
                        client.GetConversionOptionsAsync(ids, target, ct), token),
                    lifetime.Token).ConfigureAwait(true);
                if (selection is null || !Current() || lifetime.IsCancellationRequested) return;

                await TellAsync(session, (client, token) =>
                    client.CreateConversionJobAsync(ids, selection, token), shell, ItemCommand.Convert).ConfigureAwait(true);
                shell.Notify("已提交转换任务，服务器在后台处理");
            }
            finally
            {
                owner.Unloaded -= Unloaded;
            }
        });

    /// <summary>
    /// 扫描媒体库文件：网页端媒体库卡片上那一条，只扫这一个库（<c>Items/{id}/Refresh</c>，递归、Default），
    /// 和「重新扫描媒体库」那种整台服务器扫一遍不是同一件事。
    /// </summary>
    private static Task ScanLibraryFilesAsync(EmbySessionScope session, IShellActions shell, CardItem card) =>
        GuardAsync(shell, "扫描媒体库文件失败", async () =>
        {
            await TellAsync(session, (client, token) => client.ScanLibraryFilesAsync(card.Item.Id, token),
                shell, ItemCommand.ScanLibraryFiles).ConfigureAwait(true);
            shell.Notify($"已开始扫描「{card.Item.Name}」的文件，服务器在后台处理");
        });
}
