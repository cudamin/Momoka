using Momoka.Emby;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class SubtitleDialog
{
    internal static async Task<(bool Ok, string Detail)> ProbeOperationsAsync(XamlRoot root)
    {
        if (App.Instance?.SubtitleProbeActive != true) return (false, "只允许隔离字幕探针");
        var stream = new MediaStream { Index = 1, Type = "Subtitle", IsExternal = true, Codec = "srt", Title = "离线字幕" };
        var source = new MediaSource { Id = "fixture", Name = "离线文件.mkv", MediaStreams = [stream] };
        var downloads = 0;
        var searches = 0;
        var deletes = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new SubtitleDialog(new EmbyItem { Id = "fixture", Name = "离线操作互斥" }, source,
            _ => { searches++; return Task.FromResult(new List<RemoteSubtitleInfo>()); },
            _ => { downloads++; return completion.Task; },
            _ => { deletes++; return Task.CompletedTask; }, () => Task.FromResult(source))
        { XamlRoot = root };
        var shown = dialog.ShowAsync();
        var exclusive = false;
        var waited = false;
        var completed = false;
        try
        {
            await Task.Delay(300);
            dialog.UpdateLayout();
            var button = new Button { DataContext = new SubtitleCandidate(new RemoteSubtitleInfo { Id = "one", Name = "离线候选" }) };
            dialog.OnDownload(button, new RoutedEventArgs());
            dialog.OnDownload(button, new RoutedEventArgs());
            dialog.OnSearch(dialog, new RoutedEventArgs());
            dialog.BeginDelete(dialog._tracks[0]);
            await dialog.DeleteConfirmedAsync();
            exclusive = downloads == 1 && searches == 0 && deletes == 0 && dialog._mutating;
            dialog.Hide();
            await Task.Delay(650);
            waited = shown.Status == Windows.Foundation.AsyncStatus.Started;
            completion.TrySetResult();
            await SettledAsync(dialog);
            completed = dialog.Touched && dialog.NeedsRefresh && !dialog._busy;
        }
        finally
        {
            completion.TrySetResult();
            await SettledAsync(dialog);
            dialog.Hide();
            await shown;
        }

        var writes = 0;
        var recovery = new SubtitleDialog(new EmbyItem { Id = "fixture", Name = "离线响应中断" }, source,
            _ => Task.FromResult(new List<RemoteSubtitleInfo>()), _ =>
            {
                writes++;
                source.MediaStreams.Add(new MediaStream { Index = 2, Type = "Subtitle", IsExternal = true, Codec = "srt", Title = "已下载" });
                return Task.FromException(new IOException("模拟服务器完成后响应中断"));
            }, _ => Task.CompletedTask, () => Task.FromResult(source))
        { XamlRoot = root };
        var recoveryShown = recovery.ShowAsync();
        var recovered = false;
        try
        {
            await Task.Delay(300);
            recovery.OnDownload(new Button { DataContext = new SubtitleCandidate(new RemoteSubtitleInfo { Id = "two" }) }, new RoutedEventArgs());
            await SettledAsync(recovery);
            var unknown = !recovery.Touched && recovery._tracksStale && !recovery.Tracks.IsEnabled;
            recovery.OnRefreshTracks(recovery, new RoutedEventArgs());
            await SettledAsync(recovery);
            recovered = unknown && recovery.NeedsRefresh && writes == 1 && recovery._tracks.Count == 2 && recovery.Tracks.IsEnabled;
        }
        finally { recovery.Hide(); await recoveryShown; }
        return (exclusive && waited && completed && recovered,
            $"真实对话框＋假委托：操作互斥={exclusive}、写入期间等待关闭={waited}、完成后刷新={completed}、响应中断恢复不重发={recovered}");
    }

    internal static async Task<(bool Ok, string Detail)> ProbeSearchAsync(XamlRoot root, Func<string, SubtitleDialog, Task> capture)
    {
        if (App.Instance?.SubtitleProbeActive != true) return (false, "只允许隔离字幕探针");
        var source = new MediaSource { Id = "fixture", Name = "离线影片.1080p.mkv" };
        var requests = new List<ServerSubtitleSearch>();
        TaskCompletionSource<List<RemoteSubtitleInfo>>? reply = null;
        var fail = false;
        var dialog = new SubtitleDialog(new EmbyItem { Id = "fixture", Name = "字幕搜索（离线夹具）" }, source,
            search =>
            {
                requests.Add(search);
                return reply?.Task ?? (fail ? Task.FromException<List<RemoteSubtitleInfo>>(new IOException("模拟搜索失败"))
                    : Task.FromResult(new List<RemoteSubtitleInfo>()));
            }, _ => Task.CompletedTask, _ => Task.CompletedTask)
        { XamlRoot = root };
        var shown = dialog.ShowAsync();
        var initial = false;
        var empty = false;
        var conditions = false;
        var exclusive = false;
        var result = false;
        var cleared = false;
        var recovered = false;
        var late = false;
        try
        {
            await Task.Delay(350);
            dialog.UpdateLayout();
            initial = !dialog.ForcedOnly.IsOn
                && dialog.NoSearchResults.Visibility == Visibility.Collapsed && dialog.Results.Visibility == Visibility.Collapsed
                && dialog.SourceFileText.Text.Contains(source.Name, StringComparison.Ordinal);
            dialog.OnSearch(dialog.SearchButton, new RoutedEventArgs());
            await SettledAsync(dialog);
            empty = requests.Count == 1 && requests[0] == new ServerSubtitleSearch("chi")
                && dialog.NoSearchResults.Visibility == Visibility.Visible && dialog.Status.Visibility == Visibility.Collapsed
                && dialog.SubtitleDownloadHelp.NavigateUri.AbsoluteUri == "https://emby.media/support/articles/Manual-Subtitle-Downloads.html"
                && !dialog.Touched && !dialog.NeedsRefresh;
            await capture("empty", dialog);

            dialog.LanguageBox.SelectedIndex = 1;
            dialog.ForcedOnly.IsOn = true;
            reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var boundResults = dialog.Results.ItemsSource;
            dialog.OnSearch(dialog.SearchButton, new RoutedEventArgs());
            dialog.OnSearch(dialog.SearchButton, new RoutedEventArgs());
            conditions = requests.Count == 2 && requests[1] == new ServerSubtitleSearch("eng", ForcedOnly: true);
            exclusive = dialog._busy && !dialog.SearchButton.IsEnabled && !dialog.LanguageBox.IsEnabled
                && !dialog.ForcedOnly.IsEnabled
                && dialog.NoSearchResults.Visibility == Visibility.Collapsed && dialog.Busy.IsActive;
            reply.SetResult([new RemoteSubtitleInfo
            {
                Id = "fixture/candidate", Name = "Offline.Movie.1080p.English.forced.srt", ProviderName = "离线字幕源",
                Language = "eng", Format = "srt", IsForced = true, IsHashMatch = true, DownloadCount = 12
            }]);
            await SettledAsync(dialog);
            result = dialog._results.Count == 1 && dialog.Results.Visibility == Visibility.Visible
                && dialog.NoSearchResults.Visibility == Visibility.Collapsed && ReferenceEquals(boundResults, dialog.Results.ItemsSource)
                && dialog.SearchButton.IsEnabled && dialog.ForcedOnly.IsEnabled && !dialog.Busy.IsActive;
            await capture("results", dialog);

            dialog.ForcedOnly.IsOn = false;
            cleared = dialog._results.Count == 0 && dialog.Results.Visibility == Visibility.Collapsed
                && dialog.NoSearchResults.Visibility == Visibility.Collapsed && !dialog.ForcedOnly.IsOn;
            reply = null;
            fail = true;
            dialog.OnSearch(dialog.SearchButton, new RoutedEventArgs());
            await SettledAsync(dialog);
            var failed = dialog.Status.Text.Contains("搜索字幕失败", StringComparison.Ordinal)
                && dialog.NoSearchResults.Visibility == Visibility.Collapsed && dialog.SearchButton.IsEnabled;
            fail = false;
            dialog.OnSearch(dialog.SearchButton, new RoutedEventArgs());
            await SettledAsync(dialog);
            recovered = failed && dialog.NoSearchResults.Visibility == Visibility.Visible && dialog.Status.Visibility == Visibility.Collapsed;

            reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.OnSearch(dialog.SearchButton, new RoutedEventArgs());
            dialog.Hide();
            await shown;
            reply.SetResult([new RemoteSubtitleInfo { Id = "late" }]);
            await SettledAsync(dialog);
            late = dialog._closed && dialog._results.Count == 0 && !dialog.Touched;
        }
        finally
        {
            reply?.TrySetResult([]);
            await SettledAsync(dialog);
            dialog.Hide();
            await shown;
        }

        ServerSubtitleSearch? jellyfinSearch = null;
        var jellyfin = new SubtitleDialog(new EmbyItem { Id = "fixture", Name = "Jellyfin 字幕（离线夹具）" }, source,
            search => { jellyfinSearch = search; return Task.FromResult(new List<RemoteSubtitleInfo>()); },
            _ => Task.CompletedTask, _ => Task.CompletedTask, supportsForcedOnly: false)
        { XamlRoot = root };
        var jellyfinShown = jellyfin.ShowAsync();
        var unsupportedHidden = false;
        try
        {
            await Task.Delay(350);
            jellyfin.ForcedOnly.IsOn = true;
            jellyfin.OnSearch(jellyfin.SearchButton, new RoutedEventArgs());
            await SettledAsync(jellyfin);
            unsupportedHidden = jellyfin.ForcedOnlyRow.Visibility == Visibility.Collapsed
                && jellyfinSearch == new ServerSubtitleSearch("chi")
                && jellyfin.SearchButton.IsEnabled;
            await capture("jellyfin", jellyfin);
        }
        finally { jellyfin.Hide(); await jellyfinShown; }
        return (initial && empty && conditions && exclusive && result && cleared && recovered && late && unsupportedHidden,
            $"真实控件＋假委托：初始={initial}、空结果帮助={empty}、条件传递={conditions}、搜索互斥={exclusive}、结果={result}、条件变更清理={cleared}、失败重试={recovered}、关闭后迟到结果={late}、不支持的整行隐藏={unsupportedHidden}");
    }

    internal bool ProbeSearchLayoutFits() => XamlRoot is { } root && SubtitleBody.ActualWidth > 0
        && SubtitleBody.ActualWidth <= root.Size.Width - 48 && SubtitleScroll.ActualHeight <= root.Size.Height - 160
        && Math.Abs(SearchButton.ActualWidth - SubtitleBody.ActualWidth) < 2;

    /// <summary>探针用：写入是否还在飞（等收尾用）。</summary>
    internal bool ProbeBusy => _busy;

    /// <summary>探针用：按右上角那颗 ✕（与用户点它是同一条 <see cref="OnClose"/>）。</summary>
    internal void OnCloseForProbe() => OnClose(HeaderCloseButton, new RoutedEventArgs());

    /// <summary>探针用：点一次「下载」，走真控件那条 <see cref="OnDownload"/>，委托由夹具决定。</summary>
    internal void OnDownloadForProbe() => OnDownload(
        new Button { DataContext = new SubtitleCandidate(new RemoteSubtitleInfo { Id = "probe", Name = "离线候选" }) },
        new RoutedEventArgs());

    /// <summary>
    /// 探针用：2026-10-29 用户令那一条 —— 右下角的关闭没了、右上角那颗 ✕ 真的在右上角（与正文右沿对齐、
    /// 在正文上方、落在弹窗表面里）。读的是排过版的真实控件，不是 XAML 字面量。
    /// </summary>
    internal (bool Ok, string Detail, bool CloseEnabled) ProbeHeader()
    {
        var close = HeaderCloseButton;
        var commandSpace = DialogDismiss.TemplatePart(this, "CommandSpace");
        if (DialogDismiss.Surface(this) is not { } surface || commandSpace is null)
            return (false, "找不到弹窗模板部件（BackgroundElement / CommandSpace）", close.IsEnabled);
        var closeBounds = Bounds(close);
        var bodyBounds = Bounds(SubtitleBody);
        var surfaceBounds = Bounds(surface);
        // 一个按钮都不给的时候模板落到 NoneVisible，整条按钮区连同它 24 DIP 的上边距一起收起。
        var noBottom = string.IsNullOrEmpty(CloseButtonText) && commandSpace.ActualHeight <= 0;
        var visible = close.Visibility == Visibility.Visible && close.ActualWidth > 0;
        var aligned = Math.Abs(closeBounds.Right - bodyBounds.Right) <= 2;
        var above = closeBounds.Top < bodyBounds.Top;
        var contained = closeBounds.Left >= surfaceBounds.Left && closeBounds.Right <= surfaceBounds.Right
            && closeBounds.Top >= surfaceBounds.Top && closeBounds.Bottom <= surfaceBounds.Bottom;
        return (noBottom && visible && aligned && above && contained,
            $"右下角关闭={(noBottom ? "已收起" : "还在")}、✕ 可见={visible}、✕ 右沿与正文差 {closeBounds.Right - bodyBounds.Right:0.#} DIP、"
                + $"✕ 顶 {closeBounds.Top:0.#} / 正文顶 {bodyBounds.Top:0.#}、在弹窗表面内={contained}",
            close.IsEnabled);

        Rect Bounds(FrameworkElement element) =>
            element.TransformToVisual(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    private static async Task SettledAsync(SubtitleDialog dialog)
    {
        for (var index = 0; index < 40 && dialog._busy; index++) await Task.Delay(50);
        if (dialog._busy) throw new TimeoutException("离线字幕操作未完成");
    }
}
