using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task FnNasSubtitlesAsync()
    {
        var topMost = _window!.TopMost;
        try
        {
            _window.TopMost = true;
            foreach (var width in new[] { 1280, 760 })
                await FnNasSubtitleControlsAsync(width);
        }
        finally { _window.TopMost = topMost; ResizeInspect(1420, 980); }
    }

    // Restores only an anonymous .invalid identity. Every write terminates in this in-memory
    // transport; the injected picker never opens Windows or reads a user's subtitle file.
    private async Task FnNasSubtitleControlsAsync(int width)
    {
        using var fixture = CreateFixture();
        var state = new FnNasSubtitleProbeState();
        fixture.Transport.Reply = state.ReplyAsync;
        await fixture.SelectAsync(host: "fn-subtitles.invalid", kind: MediaServerKind.FnNas);
        var scope = fixture.Session.Capture();
        var current = true;
        var permissions = new ItemMenuPermissions(IsAdministrator: false, CanManageSubtitles: true);
        fixture.Actions.MenuPermissions = _ => permissions;
        var item = scope.Bind(new EmbyItem
        {
            Id = "movie", Name = "匿名双版本电影", Type = EmbyItemType.Movie,
            MediaSources = [new() { Id = "file-a", Path = "/media/movie.1080p.mkv" }, new() { Id = "file-b", Path = "/media/movie.2160p.mkv" }]
        });
        var menuOwner = new Page();
        _root.Children.Add(menuOwner);
        Task? menuOperation = null;
        FnNasSubtitleDialog? menuDialog = null;
        try
        {
            await LayoutAsync(menuOwner);
            var card = new CardItem(item, fixture.Images, 160);
            var changed = 0;
            menuOperation = (Task)typeof(ItemCommands).GetMethod("SubtitlesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [scope, fixture.Actions, menuOwner, card, (Action)(() => changed++)])!;
            await UntilAsync(() => (menuDialog = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<FnNasSubtitleDialog>().SingleOrDefault()) is not null
                && !Get<bool>(menuDialog, "_busy") && Get<FnNasSubtitles>(menuDialog, "_service").CanWrite);
            await LayoutAsync(menuDialog!);
            MetadataInvoke(Get<Button>(menuDialog!, "HeaderCloseButton"));
            await menuOperation;
            Require(changed == 0 && state.Writes.Count == 0, "正式字幕菜单读取/取消触发了写入或父刷新");
        }
        finally
        {
            menuDialog?.Hide();
            if (menuOperation is not null) await menuOperation;
            _root.Children.Remove(menuOwner);
        }
        FnNasSubtitles[] Services() => item.MediaSources.Select(source =>
            new FnNasSubtitles(scope, item, source, () => permissions, () => current)).ToArray();
        var versions = Services();
        var pickCalls = 0;
        var cancelPick = true;
        TaskCompletionSource<FnNasSubtitleUpload?>? pendingPick = null;
        Task<FnNasSubtitleUpload?> Pick(XamlRoot root, Func<bool> isCurrent)
        {
            Require(root == _root.XamlRoot && isCurrent(), "上传没有固定原窗口和原版本");
            pickCalls++;
            return pendingPick?.Task ?? Task.FromResult(cancelPick ? null
                : new FnNasSubtitleUpload("anonymous.zh.srt", Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\n匿名字幕\n")));
        }
        ResizeProbeClient(width, 900);
        await LayoutAsync(_root);
        var dialog = new FnNasSubtitleDialog(item.Name, versions, () => current, Pick) { XamlRoot = _root.XamlRoot };
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await Ready(dialog, "file-a");
            var boundTracks = Get<ItemsControl>(dialog, "Tracks").ItemsSource;
            var boundResults = Get<ItemsControl>(dialog, "Results").ItemsSource;
            Require(MenuAvailable() && Get<StackPanel>(dialog, "ServerFileArea").Visibility == Visibility.Collapsed,
                "普通获权账号缺字幕菜单或看到了管理员服务器路径");
            await Capture("ordinary", Get<ComboBox>(dialog, "VersionBox"));
            Get<ComboBox>(dialog, "VersionBox").SelectedIndex = 1;
            await Ready(dialog, "file-b");
            Require(Get<TextBlock>(dialog, "SourceFileText").Text.EndsWith(versions[1].Path, StringComparison.Ordinal),
                "非首版本的文件说明没有更新");
            Require(ReferenceEquals(boundTracks, Get<ItemsControl>(dialog, "Tracks").ItemsSource)
                && ReferenceEquals(boundResults, Get<ItemsControl>(dialog, "Results").ItemsSource), "换版本替换了绑定集合");
            await Capture("version-b", Get<ItemsControl>(dialog, "Tracks"));

            permissions = new(IsAdministrator: false, CanManageSubtitles: false);
            fixture.Session.ReplaceClient(scope, scope.CurrentConnection with { CanManageSubtitles = false });
            dialog.RefreshCapabilities();
            Require(!MenuAvailable() && Get<StackPanel>(dialog, "ManagementArea").Visibility == Visibility.Collapsed
                && Get<ItemsControl>(dialog, "Tracks").Visibility == Visibility.Collapsed,
                "字幕拒权没有收起菜单及完整从属区域");
            await Capture("denied", Get<ComboBox>(dialog, "VersionBox"));
            permissions = new(IsAdministrator: false, CanManageSubtitles: true);
            fixture.Session.ReplaceClient(scope, scope.CurrentConnection with { CanManageSubtitles = true });
            dialog.RefreshCapabilities();
            MetadataInvoke(Get<Button>(dialog, "RefreshTracksButton"));
            await Ready(dialog, "file-b");
            Require(MenuAvailable() && Get<ComboBox>(dialog, "LanguageBox").SelectedIndex == 0
                && Get<StackPanel>(dialog, "ManagementArea").Visibility == Visibility.Visible,
                "同页恢复字幕权限没有保留语言或恢复操作");

            var remove = TrackButton("sidecar-b");
            var before = state.Writes.Count;
            MetadataInvoke(remove);
            await UntilAsync(() => Get<Border>(dialog, "DeleteConfirmation").Visibility == Visibility.Visible);
            Require(Get<TextBlock>(dialog, "DeleteConfirmationText").Text.Contains(versions[1].Path, StringComparison.Ordinal)
                && Get<TextBlock>(dialog, "DeleteConfirmationText").Text.Contains("movie.2160p.zh.srt", StringComparison.Ordinal),
                "删除确认未注明媒体文件和字幕名称");
            await Capture("delete-confirm", Get<Border>(dialog, "DeleteConfirmation"));
            MetadataInvoke(Get<Button>(dialog, "CancelDeleteButton"));
            Require(state.Writes.Count == before && Get<Border>(dialog, "DeleteConfirmation").Visibility == Visibility.Collapsed,
                "删除取消发送了请求");
            MetadataInvoke(remove);
            MetadataInvoke(Get<Button>(dialog, "ConfirmDeleteButton"));
            await UntilAsync(() => state.Writes.Count == before + 1 && !Get<bool>(dialog, "_busy"));
            Require(state.Writes[^1] == "DELETE /media/v/api/v1/subtitle/del {\"subtitle_guid\":\"sidecar-b\"}",
                "删除没有固定字幕GUID");
            await Ready(dialog, "file-b");

            MetadataInvoke(Get<Button>(dialog, "UploadButton"));
            await UntilAsync(() => pickCalls == 1 && !Get<bool>(dialog, "_busy"));
            Require(state.Writes.Count == before + 1, "本地文件选择取消仍发送上传");
            cancelPick = false;
            MetadataInvoke(Get<Button>(dialog, "UploadButton"));
            await UntilAsync(() => pickCalls == 2 && !Get<bool>(dialog, "_busy"));
            Require(state.Writes.Count == before + 2 && state.Writes[^1].StartsWith("POST /media/v/api/v1/subtitle/upload/file-b ", StringComparison.Ordinal),
                "上传写入了首版本或重复写入");
            await Ready(dialog, "file-b");

            MetadataInvoke(Get<Button>(dialog, "SearchButton"));
            await UntilAsync(() => Get<ItemsControl>(dialog, "Results").ItemsSource is System.Collections.ICollection { Count: 1 }
                && !Get<bool>(dialog, "_busy"));
            await LayoutAsync(dialog);
            await Capture("search", Get<ItemsControl>(dialog, "Results"));
            var add = Descendants(dialog).OfType<Button>().Single(button => button.Name == "DownloadFnNasSubtitleButton"
                && button.DataContext is FnNasSubtitleResultRow);
            state.FailNextReadAfterWrite = true;
            MetadataInvoke(add);
            await UntilAsync(() => !Get<bool>(dialog, "_busy") && Get<TextBlock>(dialog, "Status").Text.Contains("刷新列表失败", StringComparison.Ordinal));
            Require(dialog.Touched && dialog.NeedsRefresh && state.Writes.Count == before + 3 && !versions[1].CanWrite,
                "保存后的回读失败丢失成功或允许旧写入");
            await Capture("saved-refresh-failed", Get<TextBlock>(dialog, "Status"));
            state.FailRead = false;
            MetadataInvoke(Get<Button>(dialog, "RefreshTracksButton"));
            await Ready(dialog, "file-b");
            Require(state.Writes.Count == before + 3 && versions[1].CanWrite,
                "已确认写入的恢复重发了下载或未恢复能力");

            permissions = new(IsAdministrator: true, CanManageSubtitles: true);
            state.Administrator = true;
            fixture.Session.ReplaceClient(scope, scope.CurrentConnection with { IsAdministrator = true, CanManageSubtitles = true });
            dialog.RefreshCapabilities();
            Require(Get<StackPanel>(dialog, "ServerFileArea").Visibility == Visibility.Visible, "管理员服务器文件分支没有恢复");
            Get<TextBox>(dialog, "ServerPathBox").Text = "/media/anonymous.zh.ass";
            await Capture("administrator", Get<StackPanel>(dialog, "ServerFileArea"));
            MetadataInvoke(Get<Button>(dialog, "MarkServerFileButton"));
            await UntilAsync(() => !Get<bool>(dialog, "_busy") && state.Writes.Count == before + 4);
            Require(state.Writes[^1].Contains("\"media_guid\":\"file-b\",\"filepaths\":[\"/media/anonymous.zh.ass\"]", StringComparison.Ordinal),
                "管理员服务器文件没有固定非首版本");

            pendingPick = Pending<FnNasSubtitleUpload?>();
            MetadataInvoke(Get<Button>(dialog, "UploadButton"));
            await UntilAsync(() => pickCalls == 3 && Get<bool>(dialog, "_mutating"));
            dialog.Hide();
            await SettleAsync();
            Require(!shown.IsCompleted && !Get<ComboBox>(dialog, "VersionBox").IsEnabled,
                "上传等待时允许关闭或更换媒体版本");
            pendingPick.TrySetResult(null);
            await UntilAsync(() => !Get<bool>(dialog, "_busy"));
            Require(state.Writes.Count == before + 4, "等待中的选择取消发送了写入");
            pendingPick = null;

            Get<ComboBox>(dialog, "VersionBox").SelectedIndex = 0;
            await Ready(dialog, "file-a");
            Require(dialog.Touched && dialog.NeedsRefresh, "切回未改版本丢失了其他版本的刷新债务");
            await Capture("return-version-a", Get<ComboBox>(dialog, "VersionBox"));
            MetadataInvoke(Get<Button>(dialog, "HeaderCloseButton"));
            Require(await shown == ContentDialogResult.None, "关闭字幕管理没有返回取消");

            var unknownVersions = Services();
            var unknown = new FnNasSubtitleDialog(item.Name, unknownVersions, () => current, Pick) { XamlRoot = _root.XamlRoot };
            var unknownShown = unknown.ShowAsync().AsTask();
            try
            {
                await Ready(unknown, "file-a");
                state.UnknownWrite = true;
                MetadataInvoke(Get<Button>(unknown, "UploadButton"));
                await UntilAsync(() => !Get<bool>(unknown, "_busy") && unknown.NeedsRefresh);
                var unknownWrites = state.Writes.Count;
                Require(!unknown.Touched && Get<StackPanel>(unknown, "ManagementArea").Visibility == Visibility.Collapsed,
                    "未知回执被追认成功或仍显示写入区");
                state.UnknownWrite = false;
                state.FailRead = true;
                MetadataInvoke(Get<Button>(unknown, "RefreshTracksButton"));
                await UntilAsync(() => !Get<bool>(unknown, "_busy") && Get<TextBlock>(unknown, "Status").Text.Contains("刷新字幕失败", StringComparison.Ordinal));
                state.FailRead = false;
                MetadataInvoke(Get<Button>(unknown, "RefreshTracksButton"));
                await UntilAsync(() => !Get<bool>(unknown, "_busy") && Get<TextBlock>(unknown, "Status").Text.Contains("未确认", StringComparison.Ordinal));
                Require(state.Writes.Count == unknownWrites && unknown.NeedsRefresh && !unknown.Touched && !unknownVersions[0].CanWrite,
                    "未知回执只读恢复重发或追认了写入");
                await CaptureUnknown();
                unknown.Hide();
                Require(await unknownShown == ContentDialogResult.None && unknown.NeedsRefresh, "关闭未知写入丢失刷新债务");
            }
            finally { unknown.Hide(); await unknownShown; }
            Require(fixture.Actions.Plays == 0, "字幕探针启动了播放");

            async Task CaptureUnknown()
            {
                await LayoutAsync(unknown);
                await ScrollProbeAsync(Get<ScrollViewer>(unknown, "SubtitleScroll"), Get<ScrollViewer>(unknown, "SubtitleScroll").ScrollableHeight);
                Require(!unknownShown.IsCompleted && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                    .Any(popup => Descendants(popup.Child).Contains(unknown)), "未知写入截图时字幕弹窗已关闭或未挂在Popup内");
                await SaveFnNasSubtitleDialogScreenAsync(unknown, $"fnnas-subtitles-{width}-unknown-recovered-screen.png");
            }
        }
        finally
        {
            pendingPick?.TrySetResult(null);
            await UntilAsync(() => !Get<bool>(dialog, "_mutating"));
            dialog.Hide();
            await shown;
        }

        bool MenuAvailable() => ItemMenu.For(item, fnNas: true, permissions: permissions).Any(row => row.Command == ItemCommand.Subtitles);
        Button TrackButton(string guid) => Descendants(dialog).OfType<Button>().Single(button => button.Name == "DeleteFnNasSubtitleButton"
            && button.DataContext is FnNasSubtitleRow row && row.Track.Guid == guid);
        async Task Ready(FnNasSubtitleDialog target, string mediaGuid)
        {
            await UntilAsync(() => !Get<bool>(target, "_busy") && Get<FnNasSubtitles>(target, "_service").MediaGuid == mediaGuid
                && Get<FnNasSubtitles>(target, "_service").CanWrite);
            await LayoutAsync(target);
            Require(Get<ComboBox>(target, "VersionBox").IsLoaded && Get<Button>(target, "HeaderCloseButton").ActualWidth > 0,
                "字幕对话框控件未挂树");
        }
        async Task Capture(string stage, FrameworkElement element)
        {
            await LayoutAsync(dialog);
            var scroll = Get<ScrollViewer>(dialog, "SubtitleScroll");
            var offset = element.TransformToVisual(scroll).TransformPoint(new(0, 0)).Y + scroll.VerticalOffset;
            await ScrollProbeAsync(scroll, offset);
            var close = Get<Button>(dialog, "HeaderCloseButton");
            var bounds = close.TransformToVisual(_root).TransformBounds(new(0, 0, close.ActualWidth, close.ActualHeight));
            Require(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= _root.ActualWidth + 1 && bounds.Bottom <= _root.ActualHeight + 1,
                "字幕关闭按钮被裁出窗口");
            Require(Get<StackPanel>(dialog, "SubtitleBody").ActualWidth <= _root.ActualWidth - 48
                && Get<ScrollViewer>(dialog, "SubtitleScroll").ActualHeight <= _root.ActualHeight - 160,
                "字幕正文超出宽窄窗口");
            Require(AutomationProperties.GetName(close).Length > 0, "字幕关闭按钮缺少可访问名称");
            Require(!shown.IsCompleted && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .Any(popup => Descendants(popup.Child).Contains(dialog)), "字幕截图时弹窗已关闭或未挂在Popup内");
            await SaveFnNasSubtitleDialogScreenAsync(dialog, $"fnnas-subtitles-{width}-{stage}-screen.png");
        }
    }

    private async Task SaveFnNasSubtitleDialogScreenAsync(FnNasSubtitleDialog dialog, string name)
    {
        await Task.Delay(300);
        if (_window is null) throw new InvalidOperationException("飞牛字幕探针窗口不存在");
        var viewport = Get<ScrollViewer>(dialog, "SubtitleScroll");
        var centre = viewport.TransformToVisual(_root).TransformPoint(new(viewport.ActualWidth / 2, viewport.ActualHeight / 2));
        var origin = new Interop.NativePoint();
        Require(Interop.Native.ClientToScreen(_window.Handle, ref origin), "飞牛字幕探针坐标不可用");
        var scale = _root.XamlRoot.RasterizationScale;
        var point = new Interop.NativePoint { X = origin.X + (int)Math.Round(centre.X * scale), Y = origin.Y + (int)Math.Round(centre.Y * scale) };
        var target = Interop.Native.GetAncestor(Interop.Native.WindowFromPoint(point), 2);
        Interop.Native.GetWindowThreadProcessId(target, out var process);
        Require(process == (uint)Environment.ProcessId && (target == _window.Handle || Interop.Native.GetWindow(target, 4) == _window.Handle),
            "飞牛字幕实际像素窗口不属于本次探针宿主");
        // Capture the verified dialog Popup HWND; keep the shared main-window occlusion guard intact.
        await CompositionPlaybackProbe.SaveWindowScreenAsync(target, Path.Combine(_options.Paths.LogDirectory, name));
    }

    private sealed class FnNasSubtitleProbeState
    {
        internal List<string> Writes { get; } = [];
        internal bool Administrator { get; set; }
        internal bool UnknownWrite { get; set; }
        internal bool FailRead { get; set; }
        internal bool FailNextReadAfterWrite { get; set; }
        private bool _deleted;
        private readonly HashSet<string> _uploaded = new(StringComparer.Ordinal);
        private bool _downloaded;
        private bool _marked;

        internal async Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            if (path.EndsWith("/sys/version", StringComparison.Ordinal)) return FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.8.45" });
            if (path.EndsWith("/sys/config", StringComparison.Ordinal)) return FnNasEnvelope(new { server_guid = "anonymous-subtitles", server_name = "匿名飞牛" });
            if (path.EndsWith("/user/info", StringComparison.Ordinal)) return FnNasEnvelope(new { guid = "user", username = "user", is_admin = Administrator ? 1 : 0 });
            if (path.EndsWith("/mediadb/list", StringComparison.Ordinal) || path.EndsWith("/mdb/list", StringComparison.Ordinal)) return FnNasEnvelope(Array.Empty<object>());
            if (path.EndsWith("/mediadb/sum", StringComparison.Ordinal)) return FnNasEnvelope(new { total = 0 });
            if (path.EndsWith("/play/list", StringComparison.Ordinal)) return FnNasEnvelope(Array.Empty<object>());
            if (path.EndsWith("/item/movie", StringComparison.Ordinal)) return FnNasEnvelope(new { guid = "movie", title = "匿名双版本电影", type = "Movie", parent_guid = "", genres = Array.Empty<int>() });
            if (path.EndsWith("/play/info", StringComparison.Ordinal)) return FnNasEnvelope(new { duration = 0, ts = 0 });
            if (path.EndsWith("/tag/genres", StringComparison.Ordinal)) return FnNasEnvelope(Array.Empty<object>());
            if (path.EndsWith("/stream/list/movie", StringComparison.Ordinal))
                return FailRead ? new(HttpStatusCode.ServiceUnavailable) : FnNasEnvelope(Streams());
            if (path.EndsWith("/subtitle/search", StringComparison.Ordinal))
                return FnNasEnvelope(new { subtitles = new[] { new { trim_id = "provider", filename = "anonymous.zh.srt", format = "srt", download = 12 } } });
            if (!path.Contains("/subtitle/", StringComparison.Ordinal)) throw new InvalidOperationException("未登记匿名字幕路由：" + path);
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
            Writes.Add(request.Method + " " + path + " " + body);
            if (UnknownWrite) return new(HttpStatusCode.ServiceUnavailable);
            if (path.EndsWith("/del", StringComparison.Ordinal)) _deleted = true;
            else if (path.Contains("/upload/", StringComparison.Ordinal))
            {
                Require(!request.Headers.Contains("authx") && body.Contains("匿名字幕", StringComparison.Ordinal), "上传没有保持原字节或错误加签");
                _uploaded.Add(path.Split('/')[^1]);
            }
            else if (path.EndsWith("/download", StringComparison.Ordinal)) _downloaded = true;
            else if (path.EndsWith("/mark", StringComparison.Ordinal)) _marked = true;
            else throw new InvalidOperationException("探针禁止字幕下载流与播放：" + path);
            if (FailNextReadAfterWrite) { FailNextReadAfterWrite = false; FailRead = true; }
            return Json(new { code = 0, data = (object?)null });
        }

        private JsonObject Streams()
        {
            var tracks = new JsonArray(Track("sidecar-a", "file-a", "movie.1080p.zh.srt"));
            if (!_deleted) tracks.Add(Track("sidecar-b", "file-b", "movie.2160p.zh.srt"));
            var extra = Track("extra-b", "file-b", "extra.zh.ass");
            extra["is_external"] = 0;
            extra["extra_file"] = 1;
            tracks.Add(extra);
            var url = Track("remote-b", "file-b", "remote.srt");
            url["url"] = "https://external.invalid/a.srt";
            tracks.Add(url);
            foreach (var mediaGuid in _uploaded) tracks.Add(Track("upload-" + mediaGuid, mediaGuid, "anonymous.zh.srt"));
            if (_downloaded) { var downloaded = Track("download-b", "file-b", "downloaded.zh.srt"); downloaded["trim_id"] = "provider"; tracks.Add(downloaded); }
            if (_marked) tracks.Add(Track("marked-b", "file-b", "anonymous.zh.ass"));
            return new JsonObject
            {
                ["files"] = new JsonArray(new JsonObject { ["guid"] = "file-a", ["path"] = "/media/movie.1080p.mkv" },
                    new JsonObject { ["guid"] = "file-b", ["path"] = "/media/movie.2160p.mkv" }),
                ["subtitle_streams"] = tracks
            };
        }
        private static JsonObject Track(string guid, string mediaGuid, string fileName) => new()
        { ["guid"] = guid, ["media_guid"] = mediaGuid, ["filename"] = fileName, ["format"] = "srt", ["is_external"] = 1, ["language"] = "chi" };
    }
}
