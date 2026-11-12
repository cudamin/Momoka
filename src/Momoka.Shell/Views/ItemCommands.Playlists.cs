using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using System.Runtime.CompilerServices;

namespace Momoka.Shell.Views;

internal static partial class ItemCommands
{
    private sealed class PlaylistReadback { internal bool Required; }
    private static readonly ConditionalWeakTable<object, PlaylistReadback> PlaylistReadbacks = new();
    private static Task AddPlaylistAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card) =>
        GuardAsync(shell, "添加到播放列表失败", async () =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(scope.Lifetime);
            var ownerRevision = scope.PermissionRevision;
            var subject = card.Item;
            void Unloaded(object sender, RoutedEventArgs args) => lifetime.Cancel();
            owner.Unloaded += Unloaded;
            using var unregister = new UnloadedRegistration(owner, Unloaded);
            bool Current() => !lifetime.IsCancellationRequested && card.IsCurrent && ReferenceEquals(subject, card.Item)
                && scope.IsAvailable && scope.PermissionRevision == ownerRevision;
            if (!Current()) return;
            var readback = PlaylistReadbacks.GetOrCreateValue(scope.UserStateOwner);
            var playlists = await scope.ExecuteAsync((client, token) => client.GetEditableVideoPlaylistsAsync(token), lifetime.Token);
            if (!Current()) return;
            if (readback.Required)
            {
                readback.Required = false;
                shell.Notify("已重新读取播放列表；请先核对上次操作的结果，再决定是否添加。", InfoBarSeverity.Warning);
                return;
            }
            var options = await LibraryBatchDialogs.PlaylistAsync(owner, 1, playlists, lifetime.Token);
            if (options is null || !Current()) return;
            var confirmed = false;
            try
            {
                await scope.ExecuteAsync(async (client, token) =>
                {
                    var revision = scope.PermissionRevision;
                    void Guard()
                    {
                        token.ThrowIfCancellationRequested();
                        scope.ThrowIfNotAvailable();
                        if (!Current() || scope.PermissionRevision != revision) throw new OperationCanceledException("账号或页面已更新，请重新打开菜单");
                    }
                    if (options.NewPlaylistName is { } name)
                        await client.CreatePlaylistAsync(name, [card.Item.Id], token, () => confirmed = true, Guard);
                    else if (options.PlaylistId is { } id)
                        await client.AddToPlaylistAsync(id, [card.Item.Id], options.SkipPlaylistDuplicates, token, () => confirmed = true, Guard);
                }, lifetime.Token);
            }
            catch (OperationCanceledException) when (confirmed) { }
            catch (PlaylistWriteUncertainException)
            {
                readback.Required = true;
                throw;
            }
            if (confirmed && scope.IsAvailable) shell.Notify("已添加到服务器播放列表", InfoBarSeverity.Success);
        });

    private sealed class UnloadedRegistration(FrameworkElement owner, RoutedEventHandler handler) : IDisposable
    {
        public void Dispose() => owner.Unloaded -= handler;
    }
}
