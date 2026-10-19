using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.FnNas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// 飞牛「更多」菜单上那几件事。菜单行由 <see cref="ItemMenu"/> 排，这里把它们接到官方前端核对过的
/// <c>item/*</c>、<c>scrap/*</c> 接口上；Emby/Jellyfin 的同一批命令仍留在 <c>ItemCommands.Server.cs</c>。
/// </summary>
internal static partial class ItemCommands
{
    private const string FnNasLanguage = "zh-CN";

    private static Task FnNasRefreshAsync(
        EmbySessionScope session,
        IShellActions shell,
        FrameworkElement owner,
        CardItem card) =>
        GuardAsync(shell, "刷新元数据失败", async () =>
        {
            if (Root(owner) is not { } root) return;

            // 飞牛的刷新只有两档，没有「替换现有图像／缩略图」这两颗开关；把 Emby 那两行藏起来，
            // 免得用户以为它们会生效。
            var dialog = new RefreshMetadataDialog(supportsThumbnailReplacement: false, supportsImageReplacement: false)
            {
                XamlRoot = root
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var body = FnNasItemActions.Refresh(card.Item.Id, FnNasItemActions.RefreshMode(dialog.Mode));
            await TellAsync(session, (client, token) =>
                client.FnNas.RefreshItemAsync(client.FnNasConnection, body, token)).ConfigureAwait(true);

            shell.Notify($"已请求刷新「{card.Item.Name}」的元数据，服务器在后台处理");
        });

    private static Task FnNasEditAsync(
        EmbySessionScope session,
        IShellActions shell,
        FrameworkElement owner,
        CardItem card,
        Action? changed) =>
        GuardAsync(shell, "编辑元数据失败", async () =>
        {
            if (Root(owner) is not { } root) return;

            var item = card.Item;
            var detail = await AskAsync(session, (client, token) =>
                client.FnNas.GetItemEditDetailAsync(client.FnNasConnection, item.Id, token)).ConfigureAwait(true);

            var dialog = new FnNasMetadataDialog(detail) { XamlRoot = root };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (!dialog.Edit.ApplyTo(detail))
            {
                Log.Debug(Category, $"飞牛编辑元数据：{item.Name} 没有改动");
                return;
            }

            await TellAsync(session, (client, token) =>
                client.FnNas.SaveItemEditDetailAsync(client.FnNasConnection, detail, token)).ConfigureAwait(true);

            shell.Notify($"已保存「{dialog.Edit.Name}」的元数据");
            changed?.Invoke();
        });

    private static Task FnNasVersionsAsync(
        EmbySessionScope session,
        IShellActions shell,
        FrameworkElement owner,
        CardItem card,
        Action? changed) =>
        GuardAsync(shell, "读取版本失败", async () =>
        {
            if (Root(owner) is not { } root) return;

            var item = card.Item;
            var files = await AskAsync(session, (client, token) =>
                client.FnNas.GetItemFilesAsync(client.FnNasConnection, item.Id, token)).ConfigureAwait(true);
            var versions = files.Select(FnNasVersion.From).Where(version => version.Path.Length > 0).ToList();
            if (versions.Count == 0)
            {
                shell.Notify($"服务器上没有「{item.Name}」的版本信息", InfoBarSeverity.Warning);
                return;
            }

            var dialog = new FnNasVersionsDialog(item.Name, versions) { XamlRoot = root };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Selected is not { } chosen) return;
            if (chosen.MediaGuid.Length == 0)
            {
                shell.Notify("这一版没有文件标识，无法单独删除", InfoBarSeverity.Warning);
                return;
            }

            var (title, body) = ("删除这个版本",
                $"将从服务器磁盘删除「{chosen.Path}」。此操作无法撤销。"
                    + (versions.Count == 1 ? "这是这个条目的最后一个版本，删除后条目会一起移除。" : ""));
            if (!await ConfirmDialog.For(owner)(title, body, "删除").ConfigureAwait(true)) return;

            await TellAsync(session, (client, token) => client.FnNas.DeleteItemAsync(
                    client.FnNasConnection, item.Id, FnNasItemActions.Delete(deleteFile: true, [chosen.MediaGuid]), token))
                .ConfigureAwait(true);

            shell.Notify($"已删除「{item.Name}」的一个版本");
            changed?.Invoke();
            shell.RefreshFavorites();
        });

    private static Task FnNasMatchAsync(
        EmbySessionScope session,
        IShellActions shell,
        FrameworkElement owner,
        CardItem card,
        Action? changed) =>
        GuardAsync(shell, "手动匹配失败", async () =>
        {
            if (Root(owner) is not { } root) return;

            var item = card.Item;
            var files = await AskAsync(session, (client, token) =>
                client.FnNas.GetItemFilesAsync(client.FnNasConnection, item.Id, token)).ConfigureAwait(true);
            var versions = files.Select(FnNasVersion.From).ToList();

            var dialog = new FnNasMatchDialog(item.Name, async (source, keyword) =>
            {
                var found = await AskAsync(session, (client, token) => client.FnNas.SearchScrapAsync(
                        client.FnNasConnection,
                        FnNasItemActions.SearchScrap(item.Id, source, keyword, FnNasLanguage), token))
                    .ConfigureAwait(true);
                return found.Select(FnNasScrapResult.From).Where(result => result.SourceId.Length > 0).ToList();
            })
            {
                XamlRoot = root
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.Selected is not { } selected) return;

            var body = FnNasItemActions.Rescrap(item, selected.Result, versions);
            await TellAsync(session, (client, token) =>
                client.FnNas.RescrapAsync(client.FnNasConnection, body, token)).ConfigureAwait(true);

            shell.Notify($"已提交「{item.Name}」的手动匹配，服务器在后台处理");
            changed?.Invoke();
        });

    private static Task FnNasUnmatchAsync(
        EmbySessionScope session,
        IShellActions shell,
        FrameworkElement owner,
        CardItem card,
        Action? changed) =>
        GuardAsync(shell, "解除匹配失败", async () =>
        {
            var item = card.Item;
            var agreed = await ConfirmDialog.For(owner)("解除匹配",
                $"将清除「{item.Name}」现有的元数据，并断开与当前匹配影片的关联。此操作无法撤销。", "解除匹配")
                .ConfigureAwait(true);
            if (!agreed) return;

            await TellAsync(session, (client, token) =>
                client.FnNas.UnscrapAsync(client.FnNasConnection, item.Id, token)).ConfigureAwait(true);

            shell.Notify($"已解除「{item.Name}」的匹配");
            changed?.Invoke();
            shell.RefreshFavorites();
        });

    private static Task FnNasDeleteAsync(
        EmbySessionScope session,
        IShellActions shell,
        FrameworkElement owner,
        CardItem card,
        Action? changed,
        Action<CardItem>? removed) =>
        GuardAsync(shell, "删除失败", async () =>
        {
            var item = card.Item;
            var (title, body) = ItemMenu.DeletePrompt(item);
            if (!await ConfirmDialog.For(owner)(title, body, "删除").ConfigureAwait(true)) return;

            await TellAsync(session, (client, token) => client.FnNas.DeleteItemAsync(
                    client.FnNasConnection, item.Id, FnNasItemActions.Delete(deleteFile: true, []), token))
                .ConfigureAwait(true);

            shell.Notify($"已删除「{item.Name}」");
            if (removed is not null) removed(card);
            else changed?.Invoke();
            shell.RefreshFavorites();
        });
}
