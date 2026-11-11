using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.FnNas;

namespace Momoka.Shell.Views;

internal static partial class ItemCommands
{
    private static readonly ConditionalWeakTable<object, HashSet<string>> LibraryRemovals = new();

    private static Task FnNasRemoveFromLibraryAsync(EmbySessionScope session, IShellActions shell, FrameworkElement owner,
        CardItem card, Action? changed, Action<CardItem>? removed) => GuardAsync(shell, "移出媒体库失败", async () =>
    {
        if (!card.IsCurrent || !ItemMenu.Allows(ItemCommand.RemoveFromLibrary, shell.MenuPermissionsFor(session),
                session.Connection.ServerKind, card.Item) || !shell.FnNasRemovalAvailable) return;
        var revision = session.PermissionRevision;
        var item = card.Item;
        var files = await AskAsync(session, (client, token) => client.FnNas.GetItemFilesAsync(client.FnNasConnection, item.Id, token)).ConfigureAwait(true);
        if (!card.IsCurrent || session.PermissionRevision != revision) return;
        var versions = files.Select(FnNasVersion.From).ToArray();
        await FnNasRemoveConfirmedAsync(session, shell, owner, card, versions, null, changed, removed).ConfigureAwait(true);
    });

    private static Task FnNasRemoveConfirmedAsync(EmbySessionScope session, IShellActions shell, FrameworkElement owner,
        CardItem card, IReadOnlyList<FnNasVersion> versions, string? mediaGuid, Action? changed, Action<CardItem>? removed) =>
        GuardAsync(shell, "移出媒体库失败", async () =>
        {
            if (!card.IsCurrent || !shell.FnNasRemovalAvailable) return;
            RequirePermission(shell, session, ItemCommand.RemoveFromLibrary);
            if (versions.Count == 0 || versions.Any(version => string.IsNullOrWhiteSpace(version.MediaGuid) || string.IsNullOrWhiteSpace(version.Path)))
            {
                shell.Notify("服务器没有完整的文件清单，无法安全移出，请重新读取版本列表", InfoBarSeverity.Warning);
                return;
            }
            var item = card.Item;
            var identity = item.IdentityKey;
            var revision = session.PermissionRevision;
            var pending = LibraryRemovals.GetValue(session.UserStateOwner, static _ => new(StringComparer.Ordinal));
            lock (pending) if (!pending.Add(item.Id)) return;
            try
            {
                var chosen = mediaGuid is null ? versions : versions.Where(version => version.MediaGuid == mediaGuid).ToArray();
                if (chosen.Count == 0) return;
                var message = "仅从媒体库移出，磁盘文件保留：\n" + string.Join("\n", chosen.Select(version => version.Path))
                    + "\n可在设置 → 影视服务器 → 媒体库的“已移除文件”中恢复。";
                if (!await ConfirmDialog.ForLibrary(owner)(mediaGuid is null ? "移出媒体库" : "移出这个版本", message, "移出").ConfigureAwait(true)) return;
                if (!card.IsCurrent || card.Item.IdentityKey != identity || session.PermissionRevision != revision) return;
                RequirePermission(shell, session, ItemCommand.RemoveFromLibrary);
                var receipt = await shell.RemoveFnNasLibraryItemAsync(session, item, versions, mediaGuid).ConfigureAwait(true);
                if (receipt.SubjectRemoved) item.MediaSources.Clear();
                else item.MediaSources.RemoveAll(source => source.Id == mediaGuid);
                shell.Notify("已移出媒体库，文件已保留");
                // Confirmation has completed. A failed page callback must never become a retry of the write.
                try
                {
                    if (receipt.SubjectRemoved)
                    {
                        if (removed is not null)
                        {
                            using (RemovedMedia.TemporaryRemoval(receipt.Ticket)) removed(card);
                        }
                        else changed?.Invoke();
                    }
                    else changed?.Invoke();
                    shell.RefreshFavorites();
                }
                catch (Exception error)
                {
                    shell.Notify("已移出并保留文件，但页面刷新失败：" + Momoka.Diagnostics.Failure.Describe(error), InfoBarSeverity.Warning);
                }
            }
            finally
            {
                lock (pending) pending.Remove(item.Id);
            }
        });
}
