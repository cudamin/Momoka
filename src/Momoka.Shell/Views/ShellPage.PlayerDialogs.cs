using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool _playerDialogOpen;

    private async void OnPlayerDialogRequested(string action)
    {
        if (_playerDialogOpen || Root.XamlRoot is not { } root) return;
        var vm = Player.ViewModel;
        var context = vm.CaptureInteraction();
        _playerDialogOpen = true;
        _window?.Activate();
        try
        {
            if (action == "chapter-rename")
            {
                var input = new TextBox { Text = vm.ChapterTitleDraft, MaxLength = 200 };
                var answer = await new ContentDialog
                {
                    XamlRoot = root,
                    Title = "章节标题",
                    Content = input,
                    PrimaryButtonText = "保存",
                    CloseButtonText = "取消"
                }.ShowAsync();
                if (answer == ContentDialogResult.Primary && vm.IsCurrentInteraction(context))
                    await vm.RenameCurrentChapterAsync(input.Text, context).ConfigureAwait(true);
                return;
            }
            if (action == "subtitle-content")
            {
                var text = await vm.ReadCurrentSubtitleTextAsync().ConfigureAwait(true);
                if (!vm.IsCurrentInteraction(context)) return;
                await new ContentDialog
                {
                    XamlRoot = root,
                    Title = "当前字幕",
                    CloseButtonText = "关闭",
                    Content = new TextBox
                    {
                        Text = text.Length == 0 ? "当前时刻没有文字字幕" : text,
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                    }
                }.ShowAsync();
                return;
            }
            if (action != "subtitle-download" || vm.CaptureSubtitleManager() is not { } selected) return;
            var (scope, item, source) = selected;
            var subtitles = new ServerSubtitles(scope, item, source);
            void RequireCurrent()
            {
                if (!vm.IsCurrentInteraction(context) || !vm.CanDownloadPlayingSubtitles || !subtitles.IsCurrent)
                    throw new InvalidOperationException("当前影片或字幕权限已变化，请重新打开字幕菜单。");
            }
            var dialog = new SubtitleDialog(item, source,
                search => { RequireCurrent(); return subtitles.SearchAsync(search); },
                subtitle => { RequireCurrent(); return subtitles.DownloadAsync(subtitle); },
                stream => { RequireCurrent(); return subtitles.DeleteAsync(stream); },
                () => { RequireCurrent(); return subtitles.ReloadAsync(); },
                subtitles.SupportsForcedOnly, subtitles.DownloadProvidesConfirmation)
            { XamlRoot = root };
            await dialog.ShowAsync();
            if ((dialog.Touched || dialog.NeedsRefresh) && vm.IsCurrentInteraction(context))
                Notify("字幕列表已更新，可在重新播放时选择新字幕。", InfoBarSeverity.Informational);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Notify("字幕操作未完成，请重新打开菜单后重试。", InfoBarSeverity.Warning); }
        finally { _playerDialogOpen = false; }
    }
}
