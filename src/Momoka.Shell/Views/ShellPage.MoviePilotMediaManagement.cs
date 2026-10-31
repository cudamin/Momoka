using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool _moviePilotHistoryShowing;
    private CancellationTokenSource? _moviePilotEpisodeDelete;

    void IShellActions.ShowMoviePilotHistoryAction(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action) =>
        _ = ShowMoviePilotHistoryActionAsync(scope, item, action);

    private async Task ShowMoviePilotHistoryActionAsync(EmbySessionScope scope, EmbyItem item, MoviePilotHistoryAction action)
    {
        if (_moviePilotService is not { Available: true } service || !scope.IsAvailable ||
            !MoviePilotVersionQuery.Supports(item) || XamlRoot is null) return;
        var directDelete = item.Type == EmbyItemType.Episode && action is MoviePilotHistoryAction.DeleteRecord
            or MoviePilotHistoryAction.DeleteSource or MoviePilotHistoryAction.DeleteDestination or MoviePilotHistoryAction.DeleteAll;
        if (_moviePilotHistoryShowing || _moviePilotDialog is not null)
        {
            Notify(directDelete ? "媒体操作正在进行，请稍后再试" : "请先关闭当前 MoviePilot 操作窗口", InfoBarSeverity.Warning);
            return;
        }
        _moviePilotHistoryShowing = true;
        MoviePilotMediaManagementDialog? dialog = null;
        var libraryFilesRemoved = false;
        try
        {
            if (directDelete)
            {
                await DeleteMoviePilotEpisodeAsync(service, scope, item, action);
                return;
            }
            dialog = new MoviePilotMediaManagementDialog(service, scope, item, action,
                ContentFrame, _session)
            { XamlRoot = XamlRoot };
            _moviePilotDialog = dialog;
            await dialog.ShowAsync().AsTask();
            libraryFilesRemoved = dialog.LibraryFilesRemovalSubmitted;
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

        // 确认窗关掉之后才动页面：在窗开着的时候导航会把回执一起关掉。删除媒体库文件之后，主页的货架和
        // 轮播已经不代表服务器上的样子（用户令 2026-10-31：删除完成后必须自动刷新或回主页）。
        if (!libraryFilesRemoved) return;
        try
        {
            await RefreshAfterMoviePilotLibraryDeleteAsync(item);
        }
        catch (Exception error)
        {
            Log.Warn("moviepilot", "媒体管理删除后刷新页面失败", error);
            Notify("删除已完成，但页面未能刷新；请回主页或重新进入查看", InfoBarSeverity.Warning);
        }
    }

    /// <summary>单集菜单已选定删除范围，直接提交；核对、权限与防重仍由原服务负责。</summary>
    private async Task DeleteMoviePilotEpisodeAsync(MoviePilotService service, EmbySessionScope scope,
        EmbyItem item, MoviePilotHistoryAction action)
    {
        using var lifetime = new CancellationTokenSource();
        _moviePilotEpisodeDelete = lifetime;
        var frame = ContentFrame;
        var page = frame.Content;
        var sourceSession = _session;
        var defaultOwner = sourceSession?.Sources.FirstOrDefault(source => source.IsCurrent)?.UserStateOwner;
        var stamp = service.ConnectionStamp;
        var revoked = 0;
        var departed = 0;
        var submitting = false;

        bool OwnsPage() => Volatile.Read(ref departed) == 0 && scope.IsAvailable &&
            ReferenceEquals(_moviePilotService, service) && ReferenceEquals(frame.Content, page);
        bool OwnsOperation() => OwnsPage() && Volatile.Read(ref revoked) == 0 && !lifetime.IsCancellationRequested &&
            service.Available && service.CanManageHistory && service.IsCurrentConnection(stamp);
        void Revoke(bool leavesPage = false)
        {
            if (leavesPage) Interlocked.Exchange(ref departed, 1);
            if (Interlocked.Exchange(ref revoked, 1) != 0) return;
            lifetime.Cancel();
        }
        void Navigating(object sender, NavigatingCancelEventArgs args) => Revoke(leavesPage: true);
        void AvailabilityChanged(object? sender, EventArgs args)
        {
            if (!service.Available || !service.CanManageHistory || !service.IsCurrentConnection(stamp)) Revoke();
        }
        void SourcesChanged(object? sender, EventArgs args)
        {
            // An unchanged secondary source remains valid; an actual default-page handoff must not revive after A→B→A.
            if (!scope.IsAvailable || !ReferenceEquals(defaultOwner,
                sourceSession?.Sources.FirstOrDefault(source => source.IsCurrent)?.UserStateOwner)) Revoke(leavesPage: true);
        }

        frame.Navigating += Navigating;
        // ReleaseContent sets Content directly and skips Navigating, so capture that transition as well.
        var contentChange = frame.RegisterPropertyChangedCallback(ContentControl.ContentProperty, (_, _) =>
        {
            if (!ReferenceEquals(frame.Content, page)) Revoke(leavesPage: true);
        });
        service.AvailabilityChanged += AvailabilityChanged;
        service.HistoryCapabilitiesChanged += AvailabilityChanged;
        if (sourceSession is not null) sourceSession.SourcesChanged += SourcesChanged;
        try
        {
            if (!OwnsOperation()) return;
            var plan = await service.PrepareItemHistoryActionAsync(scope, item, action, stamp, lifetime.Token);
            if (!OwnsOperation()) return;
            submitting = true;
            var result = await service.SubmitItemHistoryActionAsync(scope, plan, lifetime.Token);
            if (OwnsPage()) Notify($"「{item.Name}」{result.Summary}",
                result.NeedsReview ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            if (plan.DeleteDestination && OwnsPage())
            {
                try
                {
                    await RefreshAfterMoviePilotLibraryDeleteAsync(item);
                }
                catch (Exception error)
                {
                    Log.Warn("moviepilot", "单集删除后刷新页面失败", error);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (OwnsPage()) Notify(submitting
                ? $"「{item.Name}」删除请求已取消；若请求已发出，请先到 MoviePilot 核对，不能重复提交"
                : $"「{item.Name}」删除已取消，本次没有提交", InfoBarSeverity.Warning);
        }
        catch (Exception error)
        {
            Log.Warn("moviepilot", "单集媒体删除未完成", error);
            if (OwnsPage()) Notify($"「{item.Name}」" + (submitting ? "删除未完成：" : "无法核对删除范围，本次没有提交：") +
                Failure.Describe(error), InfoBarSeverity.Warning);
        }
        finally
        {
            Interlocked.Exchange(ref revoked, 1);
            frame.Navigating -= Navigating;
            frame.UnregisterPropertyChangedCallback(ContentControl.ContentProperty, contentChange);
            service.AvailabilityChanged -= AvailabilityChanged;
            service.HistoryCapabilitiesChanged -= AvailabilityChanged;
            if (sourceSession is not null) sourceSession.SourcesChanged -= SourcesChanged;
            if (ReferenceEquals(_moviePilotEpisodeDelete, lifetime)) _moviePilotEpisodeDelete = null;
        }
    }
}
