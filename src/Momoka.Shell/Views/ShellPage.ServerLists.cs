using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Services;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool HasServerLists => _session?.Sources.Any(ServerListsService.Supports) == true;

    private void RefreshServerListEntrance() => ServerListsEntry.Visibility = HasServerLists ? Visibility.Visible : Visibility.Collapsed;

    private void OnServerListsClicked(object sender, RoutedEventArgs args)
    {
        AccountMenu.Hide();
        if (_services is null || !HasServerLists) return;
        Open(typeof(ServerListsPage), new ServerListsRequest(_services), "服务器清单", ServerListsRequest.Tag);
    }

    private bool OpenServerList(EmbyItem item)
    {
        if (_services is null || item.Source is not { IsAvailable: true } source || !ServerListsService.Supports(source)) return false;
        if (item.Type != EmbyItemType.Playlist && !(source.Connection.IsPlex && item.Type == EmbyItemType.BoxSet)) return false;
        if (item.Type == EmbyItemType.Playlist && (item.MediaType == "Audio" || source.Connection.IsPlex && item.PlexPlaylistType == "audio")) return false;
        RememberTrail();
        ContentFrame.Navigate(typeof(ServerListsPage), new ServerListsRequest(_services, source, item), BrowseTransition());
        _trail.Add(new Crumb(item.Name, ""));
        _current = null;
        SyncChrome();
        return true;
    }
}
