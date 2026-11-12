using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private const string JellyfinUploadA = "11111111111111111111111111111111";
    private const string JellyfinUploadB = "22222222222222222222222222222222";

    /// <summary>普通获权账号，真实 ShowAsync/控件与 Core；文件选择和 HTTP 均为内存端点。</summary>
    private async Task JellyfinUploadAsync()
    {
        var topMost = _window!.TopMost;
        _window.TopMost = true;
        try
        {
            foreach (var width in new[] { 1160, 680 })
            {
                using var fixture = CreateFixture();
                var server = new JellyfinUploadProbeServer();
                fixture.Transport.Reply = server.Reply;
                await fixture.SelectAsync("jellyfin-upload.invalid", kind: MediaServerKind.Jellyfin);
                var scope = fixture.Session.Capture();
                Require(scope.CanUploadSubtitles == true, "恢复登录未带入已确认上传权限");
                Grant(true);
                var item = await scope.ExecuteAsync((client, token) => client.GetItemAsync(JellyfinUploadA, token, EmbyFields.Files), CancellationToken.None);
                var services = item.MediaSources.Select(source => new ServerSubtitles(scope, item, source)).ToArray();
                var dialog = CreateJellyfinUploadDialog(item, services, scope);
                void OnAvailability(object? sender, EventArgs args) => dialog.DispatcherQueue.TryEnqueue(dialog.RefreshUploadAvailability);
                foreach (var service in services)
                {
                    service.AvailabilityChanged += OnAvailability;
                    service.StartObservingAvailability();
                }
                ResizeProbeClient(width, 880);
                var shown = dialog.ShowAsync().AsTask();
                try
                {
                    await LayoutAsync(dialog);
                    Require(Get<StackPanel>(dialog, "VersionRow").Visibility == Visibility.Visible,
                        "多版本字幕选择没有显示");
                    Require(Get<Grid>(dialog, "ForcedOnlyRow").Visibility == Visibility.Collapsed,
                        "Jellyfin 无效的搜索强制筛选重新出现");
                    var version = Get<ComboBox>(dialog, "VersionBox");
                    Require(version is { IsLoaded: true, Items.Count: 2 }
                        && AutomationProperties.GetName(version) == "字幕所属媒体版本", "版本选择没有原生自动化名字");
                    await SaveJellyfinUploadScreenAsync($"jellyfin-upload-initial-{width}-screen.png");

                    Call(dialog, "OnPickUpload", Get<Button>(dialog, "PickSubtitleButton"), new RoutedEventArgs());
                    await UntilAsync(() => !Get<bool>(dialog, "_busy"));
                    Require(Get<StackPanel>(dialog, "UploadDraft").Visibility == Visibility.Visible, "假文件选择未进入真实草稿");
                    Get<ComboBox>(dialog, "UploadLanguageBox").SelectedIndex = 1;
                    Get<ToggleSwitch>(dialog, "UploadForced").IsOn = true;
                    Get<ToggleSwitch>(dialog, "UploadHearingImpaired").IsOn = true;
                    Grant(false);
                    await UntilAsync(() => Get<StackPanel>(dialog, "UploadSection").Visibility == Visibility.Collapsed);
                    await (Task)Call(dialog, "UploadSelectedAsync")!;
                    Require(server.Writes == 0, "权限收回后仍上传");
                    await SaveJellyfinUploadScreenAsync($"jellyfin-upload-denied-{width}-screen.png");
                    Grant(true);
                    await UntilAsync(() => Get<StackPanel>(dialog, "UploadSection").Visibility == Visibility.Visible);
                    Require(Get<StackPanel>(dialog, "UploadDraft").Visibility == Visibility.Visible
                        && Get<ComboBox>(dialog, "UploadLanguageBox").SelectedIndex == 1
                        && Get<ToggleSwitch>(dialog, "UploadForced").IsOn
                        && Get<ToggleSwitch>(dialog, "UploadHearingImpaired").IsOn, "权限恢复没有保留草稿和标记");
                    await SaveJellyfinUploadScreenAsync($"jellyfin-upload-restored-{width}-screen.png");

                    version.IsDropDownOpen = true;
                    await LayoutAsync(dialog);
                    Require(version.Items.OfType<ComboBoxItem>().All(option => option.IsLoaded), "原生版本下拉未实现候选");
                    await SaveJellyfinUploadScreenAsync($"jellyfin-upload-version-open-{width}-screen.png");
                    version.SelectedIndex = 1;
                    version.IsDropDownOpen = false;
                    await LayoutAsync(dialog);
                    Require(Get<StackPanel>(dialog, "UploadDraft").Visibility == Visibility.Collapsed
                        && Get<TextBlock>(dialog, "SourceFileText").Text.Contains("版本乙", StringComparison.Ordinal),
                        "切版未清草稿或未绑定版本乙");
                    Call(dialog, "OnPickUpload", Get<Button>(dialog, "PickSubtitleButton"), new RoutedEventArgs());
                    await UntilAsync(() => !Get<bool>(dialog, "_busy"));
                    Get<ComboBox>(dialog, "UploadLanguageBox").SelectedIndex = 1;
                    Get<ToggleSwitch>(dialog, "UploadForced").IsOn = true;
                    Get<ToggleSwitch>(dialog, "UploadHearingImpaired").IsOn = true;
                    await ScrollProbeAsync(Get<ScrollViewer>(dialog, "SubtitleScroll"), 0);
                    await SaveJellyfinUploadScreenAsync($"jellyfin-upload-draft-{width}-screen.png");

                    server.WriteGate = Pending<bool>();
                    var uploading = (Task)Call(dialog, "UploadSelectedAsync")!;
                    await server.WriteEntered.Task;
                    dialog.Hide();
                    version.SelectedIndex = 0;
                    Require(!shown.IsCompleted && version.SelectedIndex == 1, "写入中允许关闭或切版");
                    await SaveJellyfinUploadScreenAsync($"jellyfin-upload-pending-{width}-screen.png");
                    server.FailNextRead = true;
                    server.WriteGate.TrySetResult(true);
                    await uploading;
                    Require(dialog.Touched && dialog.NeedsRefresh && server.Writes == 1
                        && Get<StackPanel>(dialog, "UploadDraft").Visibility == Visibility.Collapsed
                        && Get<TextBlock>(dialog, "Status").Text.Contains("服务器已接收", StringComparison.Ordinal),
                        "204与回读失败未保留接收成功/父刷新义务");
                    Require(server.UploadItem == JellyfinUploadB && server.TracksA.Count == 1 && server.TracksB.Count == 2,
                        "非首版本写入落到了首版本");
                    using var body = JsonDocument.Parse(server.UploadBody!);
                    Require(body.RootElement.EnumerateObject().Count() == 5
                        && body.RootElement.GetProperty("Data").GetString() == "//4tTodlAAo="
                        && body.RootElement.GetProperty("Language").GetString() == "eng"
                        && body.RootElement.GetProperty("Format").GetString() == "srt"
                        && body.RootElement.GetProperty("IsForced").GetBoolean()
                        && body.RootElement.GetProperty("IsHearingImpaired").GetBoolean(), "上传JSON丢失原字节、五字段或标记");
                    Call(dialog, "OnRefreshTracks", Get<Button>(dialog, "RefreshTracksButton"), new RoutedEventArgs());
                    await UntilAsync(() => !Get<bool>(dialog, "_busy"));
                    await (Task)Call(dialog, "UploadSelectedAsync")!;
                    Require(server.Writes == 1 && Get<bool>(dialog, "_tracksStale") == false, "只读重试又上传或列表未恢复");
                    await SaveJellyfinUploadScreenAsync($"jellyfin-upload-confirmed-{width}-screen.png");
                    await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, $"jellyfin-upload-{width}-receipt.json"),
                        JsonSerializer.Serialize(new { server.Writes, server.UploadItem, server.Reads, dialog.Touched, dialog.NeedsRefresh,
                            NativePickerCalls = 0, RealNetworkCalls = 0, Plays = fixture.Actions.Plays }));
                    Require(fixture.Actions.Plays == 0, "上传探针启动播放");
                }
                finally
                {
                    server.WriteGate?.TrySetResult(true);
                    await UntilAsync(() => !Get<bool>(dialog, "_mutating"));
                    dialog.Hide();
                    await shown;
                    foreach (var service in services)
                    {
                        service.StopObservingAvailability();
                        service.AvailabilityChanged -= OnAvailability;
                    }
                    ResizeProbeClient(1280, 800);
                }

                void Grant(bool allowed)
                {
                    fixture.Session.ReplaceClient(scope, scope.CurrentConnection with { IsAdministrator = false, CanUploadSubtitles = allowed });
                    fixture.Session.NotifyPermissionsChanged(scope);
                }
            }
        }
        finally { _window.TopMost = topMost; }
    }

    private async Task JellyfinUploadRecoveryAsync()
    {
        var topMost = _window!.TopMost;
        _window.TopMost = true;
        try
        {
            foreach (var width in new[] { 1160, 680 })
            {
                using var fixture = CreateFixture();
                var server = new JellyfinUploadProbeServer { LoseResponse = true };
                fixture.Transport.Reply = server.Reply;
                await fixture.SelectAsync("jellyfin-upload.invalid", kind: MediaServerKind.Jellyfin);
                var scope = fixture.Session.Capture();
                fixture.Session.ReplaceClient(scope, scope.CurrentConnection with { IsAdministrator = false, CanUploadSubtitles = true });
                var item = await scope.ExecuteAsync((client, token) => client.GetItemAsync(JellyfinUploadA, token, EmbyFields.Files), CancellationToken.None);
                var services = item.MediaSources.Select(source => new ServerSubtitles(scope, item, source)).ToArray();
                var dialog = CreateJellyfinUploadDialog(item, services, scope);
                ResizeProbeClient(width, 880);
                var shown = dialog.ShowAsync().AsTask();
                try
                {
                    await LayoutAsync(dialog);
                    Call(dialog, "OnPickUpload", Get<Button>(dialog, "PickSubtitleButton"), new RoutedEventArgs());
                    await UntilAsync(() => !Get<bool>(dialog, "_busy"));
                    await (Task)Call(dialog, "UploadSelectedAsync")!;
                    Require(!dialog.Touched && dialog.NeedsRefresh && server.Writes == 1, "未知写入被追认成功或失去父刷新义务");
                    Get<ComboBox>(dialog, "VersionBox").SelectedIndex = 1;
                    Require(Get<ComboBox>(dialog, "VersionBox").SelectedIndex == 0, "未知写入允许跨版本");
                    Call(dialog, "OnRefreshTracks", Get<Button>(dialog, "RefreshTracksButton"), new RoutedEventArgs());
                    await UntilAsync(() => !Get<bool>(dialog, "_busy"));
                    await (Task)Call(dialog, "UploadSelectedAsync")!;
                    Require(!Get<Button>(dialog, "PickSubtitleButton").IsEnabled
                        && !Get<Button>(dialog, "UploadSubtitleButton").IsEnabled
                        && !Get<Button>(dialog, "SearchButton").IsEnabled
                        && Get<Button>(dialog, "RefreshTracksButton").IsEnabled
                        && !dialog.Touched && dialog.NeedsRefresh && server.Writes == 1,
                        "恢复GET追认未知写入或允许重复上传");
                    await ScrollProbeAsync(Get<ScrollViewer>(dialog, "SubtitleScroll"), Get<ScrollViewer>(dialog, "SubtitleScroll").ScrollableHeight);
                    await SaveJellyfinUploadScreenAsync($"jellyfin-upload-unknown-{width}-screen.png");
                    dialog.Hide();
                    Require(await shown == ContentDialogResult.None, "未知回执结算后不能关闭");
                    await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, $"jellyfin-upload-unknown-{width}-receipt.json"),
                        JsonSerializer.Serialize(new { server.Writes, server.Reads, dialog.Touched, dialog.NeedsRefresh,
                            NativePickerCalls = 0, RealNetworkCalls = 0, Plays = fixture.Actions.Plays }));
                }
                finally { dialog.Hide(); await shown; ResizeProbeClient(1280, 800); }
            }
        }
        finally { _window.TopMost = topMost; }
    }

    private async Task SaveJellyfinUploadScreenAsync(string name)
    {
        // Keep z-order unchanged while ContentDialog/ComboBox own native popup windows.
        await Task.Delay(300);
        await CompositionPlaybackProbe.SaveScreenAsync(_window!, Path.Combine(_options.Paths.LogDirectory, name));
    }

    private SubtitleDialog CreateJellyfinUploadDialog(EmbyItem item, ServerSubtitles[] services, EmbySessionScope scope)
    {
        var targets = item.MediaSources.Select((source, index) => new SubtitleDialogTarget
        {
            Source = source, Search = services[index].SearchAsync, Download = services[index].DownloadAsync,
            Delete = services[index].DeleteAsync, Reload = services[index].ReadReloadAsync, AcceptReload = services[index].AcceptReload, SupportsForcedOnly = false,
            DownloadProvidesConfirmation = false, Upload = services[index].UploadAsync, CanUpload = () => services[index].CanUpload
        }).ToArray();
        return new SubtitleDialog(item, targets, () => scope.IsAvailable,
            (_, isCurrent) => Task.FromResult(isCurrent()
                ? new ServerSubtitleFile("原字节.srt", [0xff, 0xfe, 0x2d, 0x4e, 0x87, 0x65, 0, 0x0a]) : null)) { XamlRoot = _root.XamlRoot };
    }

    private sealed class JellyfinUploadProbeServer
    {
        internal List<MediaStream> TracksA { get; } = [Track("甲", 3)];
        internal List<MediaStream> TracksB { get; } = [Track("乙", 4)];
        internal int Writes;
        internal int Reads;
        internal bool LoseResponse;
        internal bool FailNextRead;
        internal string? UploadItem;
        internal string? UploadBody;
        internal TaskCompletionSource<bool>? WriteGate;
        internal TaskCompletionSource<bool> WriteEntered { get; } = Pending<bool>();

        internal async Task<HttpResponseMessage> Reply(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            Require(uri.Host == "jellyfin-upload.invalid", "字幕上传探针拒绝其他目标");
            if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/Subtitles", StringComparison.Ordinal))
            {
                Writes++;
                UploadItem = uri.Segments[^2].TrimEnd('/');
                Require(UploadItem is JellyfinUploadA or JellyfinUploadB, "字幕上传目标不是已验证版本");
                UploadBody = await request.Content!.ReadAsStringAsync(token);
                WriteEntered.TrySetResult(true);
                if (WriteGate is { } gate) await gate.Task;
                (UploadItem == JellyfinUploadA ? TracksA : TracksB).Add(Track("上传结果", 7));
                if (LoseResponse) throw new HttpRequestException("匿名夹具：服务器落实后丢失回执");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            Require(request.Method == HttpMethod.Get, "字幕上传探针拒绝非预期写入");
            Reads++;
            if (uri.AbsolutePath.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return Json(new { ProductName = "Jellyfin", Version = "10.10.7", Id = "subtitle-upload-fixture" });
            if (uri.AbsolutePath.EndsWith("/Users/Me", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/Users/user", StringComparison.Ordinal))
                return Json(new { Id = "user", Name = "普通获权用户", Policy = new { IsAdministrator = false, EnableSubtitleManagement = true, EnableSubtitleDownloading = false } });
            if (uri.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal)) return Json(new { Items = Array.Empty<EmbyItem>() });
            if (uri.AbsolutePath.EndsWith("/Items/" + JellyfinUploadA, StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/Items/" + JellyfinUploadB, StringComparison.Ordinal))
            {
                if (FailNextRead) { FailNextRead = false; return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); }
                var second = uri.AbsolutePath.EndsWith(JellyfinUploadB, StringComparison.Ordinal);
                var sourceA = new MediaSource { Id = JellyfinUploadA, Name = "版本甲", Path = "/fixture/version-a.mkv", MediaStreams = [.. TracksA] };
                var sourceB = new MediaSource { Id = JellyfinUploadB, Name = "版本乙", Path = "/fixture/version-b.mkv", MediaStreams = [.. TracksB] };
                return Json(new EmbyItem { Id = second ? JellyfinUploadB : JellyfinUploadA, Name = "匿名 Jellyfin 字幕上传", Type = EmbyItemType.Movie,
                    MediaSources = second ? [sourceB] : [sourceA, sourceB] });
            }
            return Json(Array.Empty<object>());
        }

        private static MediaStream Track(string title, int index) => new()
        { Index = index, Type = "Subtitle", IsExternal = true, Codec = "subrip", Language = "chi", DisplayTitle = "版本" + title + " · 中文字幕" };
    }
}
