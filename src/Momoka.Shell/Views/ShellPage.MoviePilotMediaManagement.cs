using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool _moviePilotHistoryShowing;

    void IShellActions.ShowMoviePilotHistoryAction(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action) =>
        _ = ShowMoviePilotHistoryActionAsync(scope, item, action);

    private async Task ShowMoviePilotHistoryActionAsync(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action)
    {
        if (_moviePilotService is not { Available: true } service || !scope.IsAvailable ||
            !MoviePilotVersionQuery.Supports(item) || XamlRoot is null) return;
        if (_moviePilotHistoryShowing || _moviePilotDialog is not null)
        {
            Notify("请先关闭当前 MoviePilot 操作窗口", InfoBarSeverity.Warning);
            return;
        }
        _moviePilotHistoryShowing = true;
        MoviePilotMediaManagementDialog? dialog = null;
        try
        {
            dialog = new MoviePilotMediaManagementDialog(service, scope, item, action,
                ContentFrame, _session)
            { XamlRoot = XamlRoot };
            _moviePilotDialog = dialog;
            await dialog.ShowAsync().AsTask();
        }
        catch (Exception error)
        {
            Log.Warn("moviepilot", "媒体管理确认窗口未打开", error);
            Notify("媒体管理确认窗口未打开；本次没有提交，请关闭当前对话框后重试", InfoBarSeverity.Warning);
        }
        finally
        {
            _moviePilotHistoryShowing = false;
            if (ReferenceEquals(_moviePilotDialog, dialog)) _moviePilotDialog = null;
        }
    }
}
