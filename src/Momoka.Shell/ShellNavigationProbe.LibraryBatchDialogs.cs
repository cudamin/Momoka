using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task VerifyEmbyBatchDialogsAsync(LibraryPage page, LibraryBatchServer server, FakeActions actions)
    {
        var wall = Get<ItemsView>(page, "CardWall");
        foreach (var width in new[] { 1100, 760 })
        {
            ResizeProbeClient(width, 900);
            await LayoutAsync(page);
            Select();
            var commands = page.ProbeBatchCommands();
            foreach (var command in new[] { ItemCommand.Play, ItemCommand.PlayFromBeginning, ItemCommand.Shuffle,
                ItemCommand.AddToCollection, ItemCommand.AddToPlaylist, ItemCommand.Favorite, ItemCommand.MarkPlayed,
                ItemCommand.Delete, ItemCommand.ResetMetadata, ItemCommand.RefreshMetadata })
                Require(commands.Contains(command), "Emby 批量菜单缺少操作：" + command);
            Require(!commands.Contains(ItemCommand.ScanLibraryFiles), "电影文件不应出现目录扫描入口");
            new AppBarButtonAutomationPeer(Get<AppBarButton>(page, "LibraryBatchActions")).Invoke();
            await UntilAsync(() => MenuItems().Any(item => item.Tag is ItemCommand.AddToPlaylist));
            Require(MenuItems().All(item => item.Text != "转换…"), "批量菜单不应恢复已移除的转换入口");
            await SettleAsync();
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().Single();
            var menuBounds = presenter.TransformToVisual(_root).TransformBounds(new(0, 0, presenter.ActualWidth, presenter.ActualHeight));
            Require(menuBounds.Top >= 0 && menuBounds.Bottom <= _root.ActualHeight,
                "批量菜单超出窗口，部分操作不可见");
            await SaveProbeScreenAsync($"library-batch-Emby-menu-{width}-screen.png");
            Get<MenuFlyout>(page, "LibraryBatchMenu").Hide();
            await UntilAsync(() => MenuItems().Length == 0);
            await SettleAsync();

            await OpenAsync(ItemCommand.AddToPlaylist);
            var playlist = await DialogAsync();
            (await FindAsync<TextBox>(playlist, "BatchPlaylistName")).Text = "新列表";
            var skipDuplicates = await FindAsync<CheckBox>(playlist, "BatchPlaylistSkipDuplicates");
            await UntilAsync(() => skipDuplicates.Visibility == Visibility.Collapsed);
            (await FindAsync<ComboBox>(playlist, "BatchPlaylist")).SelectedIndex = 0;
            await UntilAsync(() => skipDuplicates.Visibility == Visibility.Visible);
            await LayoutAsync(playlist);
            Require(playlist.IsPrimaryButtonEnabled, "选择播放列表后不能提交");
            await SaveProbeScreenAsync($"library-batch-playlist-{width}-screen.png");
            Primary(playlist);
            await UntilAsync(() => page.LastBatchResult?.Succeeded == 3);
            Require(server.ExtendedWrites.Count(path => path.Contains("/Playlists/playlist/Items", StringComparison.Ordinal)) == (width == 1100 ? 3 : 6),
                "播放列表没有逐项添加或发生重复提交");

            foreach (var command in new[] { ItemCommand.Delete, ItemCommand.ResetMetadata, ItemCommand.RefreshMetadata })
            {
                Select();
                var before = server.ExtendedWrites.Count;
                await OpenAsync(command);
                var confirmation = await DialogAsync();
                Require(DialogDismiss.GetClosesOnOutsideClick(confirmation), "批量确认未支持点击外部取消");
                await LayoutAsync(confirmation);
                await SaveProbeScreenAsync($"library-batch-{command}-{width}-screen.png");
                confirmation.Hide();
                await UntilAsync(() => Dialogs().Length == 0);
                Require(server.ExtendedWrites.Count == before && server.Writes.Count == 0,
                    "取消批量确认仍产生写入");
            }
            page.ProbeEndSelection();
        }

        Select();
        var folder = page.ViewModel.Cards[0].Item;
        var originalType = folder.Type;
        folder.Type = EmbyItemType.Folder;
        folder.IsFolder = true;
        wall.DeselectAll();
        wall.Select(0);
        await OpenAsync(ItemCommand.ScanLibraryFiles);
        var scan = await DialogAsync();
        await LayoutAsync(scan);
        await SaveProbeScreenAsync("library-batch-scan-files-screen.png");
        var scansBefore = server.ExtendedWrites.Count;
        Primary(scan);
        await UntilAsync(() => page.LastBatchResult?.Succeeded == 1);
        Require(server.ExtendedWrites.Count == scansBefore + 1
            && server.ExtendedWrites[^1].Contains("MetadataRefreshMode=Default", StringComparison.Ordinal),
            "文件扫描未使用所选目录的正式扫描请求");
        folder.Type = originalType;
        folder.IsFolder = false;
        page.ProbeEndSelection();

        IReadOnlyList<EmbyItem>? captured = null;
        actions.CapturePlay = (_, queue) => captured = queue;
        try
        {
            foreach (var command in new[] { ItemCommand.Play, ItemCommand.PlayFromBeginning, ItemCommand.Shuffle })
            {
                page.ProbeBeginSelection();
                wall.Select(0);
                wall.Select(2);
                captured = null;
                // The same routed command handler, with a fake PlayAsync boundary that only records the queue.
                Call(page, "OnBatchCommand", new MenuFlyoutItem { Tag = command }, new RoutedEventArgs());
                await UntilAsync(() => captured is not null);
                Require(captured!.Count == 2 && captured.Select(item => item.Id).Order().SequenceEqual(new[] { "101", "103" }),
                    "批量播放扩大到未选择条目或丢失所选条目");
            }
        }
        finally { actions.CapturePlay = null; page.ProbeEndSelection(); }

        void Select()
        {
            page.ProbeEndSelection();
            page.ProbeBeginSelection();
            for (var index = 0; index < 3; index++) wall.Select(index);
        }
        async Task OpenAsync(ItemCommand command)
        {
            await UntilAsync(() => MenuItems().Length == 0);
            await SettleAsync();
            new AppBarButtonAutomationPeer(Get<AppBarButton>(page, "LibraryBatchActions")).Invoke();
            await UntilAsync(() => MenuItems().Any(item => item.Tag is ItemCommand value && value == command));
            new MenuFlyoutItemAutomationPeer(MenuItems().Single(item => item.Tag is ItemCommand value && value == command)).Invoke();
        }
        MenuFlyoutItem[] MenuItems() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutItem>().ToArray();
        ContentDialog[] Dialogs() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().Distinct().ToArray();
        async Task<ContentDialog> DialogAsync()
        {
            await UntilAsync(() => Dialogs().Length == 1);
            return Dialogs().Single();
        }
        static T? FindOrNull<T>(ContentDialog dialog, string name) where T : FrameworkElement =>
            Descendants(dialog).OfType<T>().FirstOrDefault(element => element.Name == name || AutomationProperties.GetAutomationId(element) == name);
        // 弹窗挂进 Popup 与模板展开不是同一拍：DialogAsync 只等到 Popup 里出现 ContentDialog，
        // 子控件可能还没长进视觉树（2026-11-17 实测同一用例另一轮通过）。等它出现，仍在超时后如实报缺控件。
        static async Task<T> FindAsync<T>(ContentDialog dialog, string name) where T : FrameworkElement
        {
            T? found = null;
            await UntilAsync(() => (found = FindOrNull<T>(dialog, name)) is not null);
            return found!;
        }
        static void Primary(ContentDialog dialog)
        {
            Require(dialog.IsPrimaryButtonEnabled, "批量参数未验证通过");
            var button = Descendants(dialog).OfType<Button>().Single(element => element.Name == "PrimaryButton");
            new ButtonAutomationPeer(button).Invoke();
        }
    }
}
