using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Momoka.Tests;
using static Momoka.Tests.TestHarness;

namespace Momoka.FnNasSubtitleTests;

internal static class FnNasSubtitleCases
{
    internal static void Register()
    {
        Test("原生搜索使用媒体版本和官方语言码，响应候选去重", () =>
        {
            using var f = new Fixture();
            var found = f.Search();
            Assert.Equal(2, found.Count);
            var request = f.Requests.Single(value => value.Path.EndsWith("/subtitle/search", StringComparison.Ordinal));
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("{\"lan\":\"zh-CN\",\"media_guid\":\"file-b\"}", request.Body);
            Assert.True(request.Signed);
            Assert.DoesNotContain("placeholder", request.Path);
            Assert.Equal("provider", found[0].TrimId);
            Assert.True(found[0].CanDownload);
            Assert.False(found[1].CanDownload);
        });
        Test("非首版本下载传trim_id与同步标志，确认后独立回读失败只重读", () =>
        {
            using var f = new Fixture();
            var candidate = f.Search()[0];
            f.Service.DownloadAsync(candidate).Finish();
            Assert.Equal("{\"media_guid\":\"file-b\",\"trim_id\":\"provider\",\"sync_download\":1}", f.Writes.Single().Body);
            Assert.True(f.Service.Touched);
            Assert.True(f.Service.NeedsRefresh);
            Assert.False(f.Service.CanWrite);
            f.ReadFailure = HttpStatusCode.ServiceUnavailable;
            Assert.Throws<EmbyApiException>(() => f.Reload());
            Assert.Throws<InvalidOperationException>(() => f.Service.DownloadAsync(candidate).Finish());
            f.ReadFailure = null;
            f.Reload();
            Assert.True(f.Service.CanWrite);
            Assert.Throws<InvalidOperationException>(() => f.Service.DownloadAsync(candidate).Finish());
            Assert.Equal(1, f.Writes.Count);
        });
        Test("本地上传保持原字节和单一multipart file字段，没有authx", () =>
        {
            using var f = new Fixture();
            f.Service.UploadAsync(new("中文.srt", Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\n字幕\n"))).Finish();
            var request = f.Writes.Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v/api/v1/subtitle/upload/file-b", request.Path);
            Assert.False(request.Signed);
            Assert.Contains("name=\"file\"", request.Body.Replace("name=file", "name=\"file\"", StringComparison.Ordinal));
            Assert.Contains("字幕", request.Body);
            Assert.True(f.Service.Touched);
        });
        Test("字幕删除取消零额外请求，确认才按subtitle_guid删除", () =>
        {
            using var f = new Fixture();
            var track = f.Snapshot.ExternalTracks.Single();
            var before = f.Requests.Count;
            f.Service.DeleteAsync(track, false).Finish();
            Assert.Equal(before, f.Requests.Count);
            f.Service.DeleteAsync(track, true).Finish();
            Assert.Equal(HttpMethod.Delete, f.Writes.Single().Method);
            Assert.Equal("{\"subtitle_guid\":\"sidecar-b\"}", f.Writes.Single().Body);
        });
        foreach (var admin in new bool?[] { null, false })
            Test("普通账号或未知管理员拒绝指定服务器文件：" + admin, () =>
            {
                using var f = new Fixture(admin);
                Assert.True(f.Service.CanManage);
                Assert.False(f.Service.CanMarkServerFile);
                Assert.Throws<InvalidOperationException>(() => f.Service.MarkServerFileAsync("/media/subtitle.srt").Finish());
                Assert.Equal(0, f.Writes.Count);
                Assert.Equal(0, f.Requests.Count(value => value.Path.EndsWith("/user/info", StringComparison.Ordinal)));
            });
        Test("管理员指定文件分支额外核对is_admin，PUT filepaths不套上传路由", () =>
        {
            using var f = new Fixture(true);
            f.Service.MarkServerFileAsync("/media/字幕.srt").Finish();
            Assert.Equal(HttpMethod.Put, f.Writes.Single().Method);
            Assert.Equal("/v/api/v1/subtitle/mark", f.Writes.Single().Path);
            Assert.Equal("{\"media_guid\":\"file-b\",\"filepaths\":[\"/media/字幕.srt\"]}", f.Writes.Single().Body);
        });
        Test("显式字幕拒权后可恢复，管理员权限不会替代字幕拒权", () =>
        {
            using var f = new Fixture(true);
            f.Permissions = new(IsAdministrator: true, CanManageSubtitles: false);
            Assert.False(f.Service.CanManage);
            Assert.False(f.Service.CanMarkServerFile);
            Assert.Throws<InvalidOperationException>(() => f.Search());
            Assert.Equal(0, f.Writes.Count);
            f.Permissions = new(IsAdministrator: true, CanManageSubtitles: true);
            Assert.True(f.Service.CanManage);
            Assert.True(f.Service.CanMarkServerFile);
            Assert.Equal(2, f.Search().Count);
        });
        foreach (var failure in new[] { "http403", "http401", "business-permission", "business-file-permission", "business-expired", "business-token", "business-parameter" })
            Test("显式写拒绝不制造刷新债务：" + failure, () =>
            {
                using var f = new Fixture();
                f.WriteFailure = failure;
                Assert.Throws<Exception>(() => f.Service.UploadAsync(new("a.srt", new byte[] { 1 })).Finish());
                Assert.False(f.Service.Touched);
                Assert.False(f.Service.NeedsRefresh);
                Assert.False(f.Service.IsUnconfirmed);
            });
        foreach (var failure in new[] { "http503", "broken-envelope", "transport" })
            Test("未知写回执只读恢复且从不追认成功：" + failure, () =>
            {
                using var f = new Fixture();
                f.WriteFailure = failure;
                Assert.Throws<FnNasSubtitleWriteUnconfirmedException>(() => f.Service.UploadAsync(new("a.srt", new byte[] { 1 })).Finish());
                Assert.False(f.Service.Touched);
                Assert.True(f.Service.NeedsRefresh);
                Assert.True(f.Service.IsUnconfirmed);
                f.Reload();
                Assert.False(f.Service.CanWrite);
                Assert.Throws<InvalidOperationException>(() => f.Service.UploadAsync(new("a.srt", new byte[] { 1 })).Finish());
                Assert.Equal(1, f.Writes.Count);
                Assert.True(f.Service.NeedsRefresh);
            });
        foreach (var invalid in new[] { "../x.srt", "a.exe", "a\r.srt", "C:\\a.srt", "" })
            Test("本地文件名校验零写：" + invalid.Replace("\r", "CR", StringComparison.Ordinal), () =>
            {
                using var f = new Fixture();
                Assert.Throws<ArgumentException>(() => f.Service.UploadAsync(new(invalid, new byte[] { 1 })).Finish());
                Assert.Equal(0, f.Writes.Count);
            });
        Test("本地空或过大文件校验零写", () =>
        {
            using var f = new Fixture();
            Assert.Throws<ArgumentException>(() => f.Service.UploadAsync(new("a.srt", [])).Finish());
            Assert.Throws<ArgumentException>(() => f.Service.UploadAsync(new("a.srt", new byte[FnNasSubtitleInput.MaxUploadBytes + 1])).Finish());
            Assert.Equal(0, f.Writes.Count);
        });
        foreach (var invalid in new[] { "/media/../a.srt", "a.srt", "https://host/a.srt", "/media/a.exe", "/media/a\n.srt" })
            Test("服务器文件路径校验零写：" + invalid.Replace("\n", "LF", StringComparison.Ordinal), () =>
            {
                using var f = new Fixture(true);
                Assert.Throws<ArgumentException>(() => f.Service.MarkServerFileAsync(invalid).Finish());
                Assert.Equal(0, f.Writes.Count);
            });
        foreach (var changed in new[] { "id", "path", "missing", "duplicate", "subtitle" })
            Test("确认后版本或完整字幕快照变化拒绝写：" + changed, () =>
            {
                using var f = new Fixture();
                var track = f.Snapshot.ExternalTracks.Single();
                f.Change = changed;
                Assert.Throws<InvalidOperationException>(() => f.Service.DeleteAsync(track, true).Finish());
                Assert.Equal(0, f.Writes.Count);
            });
        Test("伪造候选和另版本字幕对象均不能写", () =>
        {
            using var f = new Fixture();
            var candidate = f.Search()[0];
            Assert.Throws<InvalidOperationException>(() => f.Service.DownloadAsync(candidate with { }).Finish());
            Assert.Throws<InvalidOperationException>(() => f.Service.DeleteAsync(f.Snapshot.ExternalTracks.Single() with { MediaGuid = "file-a" }, true).Finish());
            Assert.Equal(0, f.Writes.Count);
        });
        foreach (var what in new[] { "page", "source", "permission" })
            Test("只读请求迟到时拒绝离页/身份/权限变化：" + what, () =>
            {
                using var f = new Fixture();
                f.BeforeReadReturn = () => f.Invalidate(what);
                Assert.Throws<OperationCanceledException>(() => f.Reload());
                Assert.Equal(0, f.Writes.Count);
            });
        Test("确认前身份及页面变更零请求", () =>
        {
            foreach (var what in new[] { "page", "source", "permission" })
            {
                using var f = new Fixture();
                var track = f.Snapshot.ExternalTracks.Single();
                f.Invalidate(what);
                Assert.Throws<Exception>(() => f.Service.DeleteAsync(track, true).Finish());
                Assert.Equal(0, f.Writes.Count);
            }
        });
        Test("确认回执之后注销仍保留成功和父页面刷新义务", () =>
        {
            using var f = new Fixture();
            f.AfterWriteReturn = () => f.Invalidate("source");
            f.Service.UploadAsync(new("a.srt", new byte[] { 1 })).Finish();
            Assert.True(f.Service.Touched);
            Assert.True(f.Service.NeedsRefresh);
            Assert.False(f.Service.IsCurrent);
            Assert.Equal(1, f.Writes.Count);
        });
        Test("预取消和重复并发写入零额外写", () =>
        {
            using var f = new Fixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => f.Service.UploadAsync(new("a.srt", new byte[] { 1 }), cancellation.Token).Finish());
            Assert.Equal(0, f.Writes.Count);
        });
        Test("外置服务器字幕进入真实Map与Planner且不生成Emby路径", () =>
        {
            using var f = new Fixture();
            var source = FnNasMediaAdapter.Sources(f.Streams(), "movie", f.Scope.Client.FnNas, f.Scope.Client.FnNasConnection).Single(value => value.Id == "file-b");
            Assert.Equal(1, source.SubtitleStreams.Count(value => value.IsExternal));
            var track = source.SubtitleStreams.Single(value => value.IsExternal);
            var map = MpvTrackMap.Build(source);
            Assert.True(map.CanSelect(track.Index));
            var settings = new AppSettings();
            var planner = new PlaybackPlanner(settings, new ShaderGroupResolver(settings.Shaders));
            var request = planner.Plan(new PlaybackTicket { Item = f.Item, Source = source, SubtitleStreamIndex = track.Index }, f.Scope.Connection);
            Assert.Equal("/v/api/v1/subtitle/dl/sidecar-b", request.ExternalSubtitles.Single().AbsolutePath);
            Assert.Equal(request.ExternalSubtitles.Single(), request.SelectedExternalSubtitle);
            Assert.DoesNotContain("placeholder", request.ExternalSubtitles.Single().AbsoluteUri);
            Assert.Equal("sidecar-b", request.FnNasSelection!.SubtitleGuid(track.Index));
        });
        Test("同名外部字幕拒绝删除，即使GUID不同", () =>
        {
            using var f = new Fixture();
            f.Change = "same-label";
            var snapshot = f.Reload();
            Assert.Throws<InvalidOperationException>(() => f.Service.DeleteAsync(snapshot.ExternalTracks[0], true).Finish());
            Assert.Equal(0, f.Writes.Count);
        });
        Test("ExtraFile展示与映射但不扩展删除权限，URL外轨不能删除或加载", () =>
        {
            using var f = new Fixture();
            f.Change = "extra-and-url";
            var snapshot = f.Reload();
            Assert.Equal(3, snapshot.ExternalTracks.Count);
            Assert.False(snapshot.ExternalTracks.Single(track => track.Guid == "extra-b").CanDelete);
            Assert.False(snapshot.ExternalTracks.Single(track => track.Guid == "remote-b").CanDelete);
            var source = FnNasMediaAdapter.Sources(f.Streams(), "movie", f.Scope.Client.FnNas, f.Scope.Client.FnNasConnection).Single(value => value.Id == "file-b");
            Assert.True(source.SubtitleStreams.Any(track => track.ProviderId == "extra-b" && track.IsExternal));
            Assert.False(source.SubtitleStreams.Any(track => track.ProviderId == "remote-b"));
        });
        Test("连续搜索只接受最后一代候选", () =>
        {
            using var f = new Fixture();
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                f.SearchReply = gate.Task;
                var first = f.Service.SearchAsync("zh-CN");
                f.SearchReply = null;
                var latest = await f.Service.SearchAsync("en");
                gate.TrySetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"code\":0,\"data\":{\"subtitles\":[{\"trim_id\":\"late\",\"format\":\"srt\"}]}}") });
                await FailsAsync<OperationCanceledException>(async () => await first);
                Assert.True(f.Service.CanDownload(latest[0]));
            });
        });
        Test("请求内容准备失败不制造未知写入债务", () =>
        {
            using var f = new Fixture();
            var acknowledged = false;
            var dispatched = false;
            Assert.Throws<Exception>(() => f.Scope.Client.FnNas.UploadSubtitleAsync(
                f.Scope.Client.FnNasConnection with { AccessToken = "invalid\n" }, "file-b", new("a.srt", [1]),
                () => acknowledged = true, dispatched: () => dispatched = true).Finish());
            Assert.False(dispatched);
            Assert.False(acknowledged);
            Assert.Equal(0, f.Writes.Count);
        });
        Test("字幕下载URL两出口同源，伪造地址与图形外挂拒绝", () =>
        {
            using var f = new Fixture();
            var source = FnNasMediaAdapter.Sources(f.Streams(), "movie", f.Scope.Client.FnNas, f.Scope.Client.FnNasConnection).Single(value => value.Id == "file-b");
            var track = source.SubtitleStreams.Single(value => value.IsExternal);
            Assert.Equal("/v/api/v1/subtitle/dl/sidecar-b", f.Scope.Client.SubtitleUrl(f.Item, source, track).AbsolutePath);
            track.DeliveryUrl = "https://foreign.invalid/subtitle.srt";
            Assert.Throws<InvalidOperationException>(() => f.Scope.Client.SubtitleUrl(f.Item, source, track));
            track.DeliveryUrl = FnNasClient.SubtitleUrl(f.Scope.Connection.ApiBase, "sidecar-b").AbsoluteUri;
            track.Codec = "sup";
            Assert.Throws<InvalidOperationException>(() => f.Scope.Client.SubtitleUrl(f.Item, source, track));
        });
        Test("当前源切换保留固定在线账号字幕，注销才撤销", () =>
        {
            using var f = new Fixture();
            f.ClearCurrent();
            Assert.False(f.Scope.IsCurrent);
            Assert.True(f.Service.IsCurrent);
            f.Service.UploadAsync(new("a.srt", [1])).Finish();
            Assert.Equal(1, f.Writes.Count);
            f.Invalidate("source");
            Assert.False(f.Service.IsCurrent);
        });
        RegisterDialogs();
    }

    private static void RegisterDialogs()
    {
        Test("生产对话：权限收起与同页恢复保持选择和绑定集合", () =>
        {
            using var f = new Fixture();
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var dialog = Dialog(f);
                var shown = dialog.ShowAsync();
                try
                {
                    await queue.IdleAsync();
                    var tracks = dialog.Tracks.ItemsSource;
                    dialog.LanguageBox.SelectedIndex = 2;
                    f.Permissions = new(IsAdministrator: true, CanManageSubtitles: false);
                    dialog.RefreshCapabilities();
                    Assert.Equal(Visibility.Collapsed, dialog.ManagementArea.Visibility);
                    Assert.Equal(Visibility.Collapsed, dialog.Tracks.Visibility);
                    Assert.Equal(Visibility.Collapsed, dialog.ServerFileArea.Visibility);
                    f.Permissions = new(IsAdministrator: false, CanManageSubtitles: true);
                    dialog.RefreshCapabilities();
                    dialog.FixtureRefresh();
                    await queue.IdleAsync();
                    Assert.Equal(Visibility.Visible, dialog.ManagementArea.Visibility);
                    Assert.Equal(2, dialog.LanguageBox.SelectedIndex);
                    Assert.True(ReferenceEquals(tracks, dialog.Tracks.ItemsSource));
                }
                finally { dialog.Hide(); await shown; }
            });
        });
        Test("生产对话：删除与本地选择取消零写入", () =>
        {
            using var f = new Fixture();
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var pickCalls = 0;
                var dialog = Dialog(f, (_, _) => { pickCalls++; return Task.FromResult<FnNasSubtitleUpload?>(null); });
                var shown = dialog.ShowAsync();
                try
                {
                    await queue.IdleAsync();
                    var before = f.Requests.Count;
                    dialog.FixtureDelete();
                    Assert.Equal(Visibility.Visible, dialog.DeleteConfirmation.Visibility);
                    Assert.Contains(f.Service.Path, dialog.DeleteConfirmationText.Text);
                    dialog.FixtureCancelDelete();
                    Assert.Equal(before, f.Requests.Count);
                    dialog.FixtureUpload();
                    await queue.IdleAsync();
                    Assert.Equal(1, pickCalls);
                    Assert.Equal(0, f.Writes.Count);
                    Assert.False(dialog.Touched);
                    Assert.False(dialog.NeedsRefresh);
                }
                finally { dialog.Hide(); await shown; }
            });
        });
        Test("生产对话：上传在飞阻止关闭和重复操作，成功回读失败仅重读", () =>
        {
            using var f = new Fixture();
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var pick = new TaskCompletionSource<FnNasSubtitleUpload?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var picks = 0;
                var dialog = Dialog(f, (_, _) => { picks++; return pick.Task; });
                var shown = dialog.ShowAsync();
                try
                {
                    await queue.IdleAsync();
                    dialog.FixtureUpload();
                    dialog.FixtureUpload();
                    dialog.Hide();
                    Assert.False(shown.IsCompleted);
                    Assert.False(dialog.VersionBox.IsEnabled);
                    Assert.Equal(1, picks);
                    f.AfterWriteReturn = () => f.ReadFailure = HttpStatusCode.ServiceUnavailable;
                    pick.TrySetResult(new("a.srt", [1]));
                    await queue.IdleAsync();
                    Assert.True(dialog.Touched && dialog.NeedsRefresh);
                    Assert.Contains("刷新列表失败", dialog.Status.Text);
                    Assert.Equal(1, f.Writes.Count);
                    Assert.False(f.Service.CanWrite);
                    f.ReadFailure = null;
                    dialog.FixtureRefresh();
                    await queue.IdleAsync();
                    Assert.True(f.Service.CanWrite);
                    Assert.Equal(1, f.Writes.Count);
                }
                finally { pick.TrySetResult(null); await queue.IdleAsync(); dialog.Hide(); await shown; }
            });
        });
        Test("生产对话：未知写入刷新失败与关闭仍保留原版本债务", () =>
        {
            using var f = new Fixture();
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var dialog = Dialog(f, (_, _) => Task.FromResult<FnNasSubtitleUpload?>(new("a.srt", [1])));
                var shown = dialog.ShowAsync();
                try
                {
                    await queue.IdleAsync();
                    f.WriteFailure = "http503";
                    dialog.FixtureUpload();
                    await queue.IdleAsync();
                    Assert.True(dialog.NeedsRefresh);
                    Assert.False(dialog.Touched);
                    Assert.Equal(Visibility.Collapsed, dialog.ManagementArea.Visibility);
                    f.ReadFailure = HttpStatusCode.ServiceUnavailable;
                    dialog.FixtureRefresh();
                    await queue.IdleAsync();
                    Assert.True(dialog.NeedsRefresh);
                    f.ReadFailure = null;
                    dialog.FixtureRefresh();
                    await queue.IdleAsync();
                    Assert.False(f.Service.CanWrite);
                    Assert.Equal(1, f.Writes.Count);
                    Assert.Equal(Visibility.Visible, dialog.Tracks.Visibility);
                    Assert.False(dialog.Tracks.IsEnabled);
                    Assert.True(dialog.FixtureTracks.Count > 0);
                }
                finally { dialog.Hide(); await shown; }
                Assert.True(dialog.NeedsRefresh);
                Assert.True(dialog.FixtureClosed);
            });
        });
        Test("生产对话：多版本切换保留其他版本成功与刷新义务", () =>
        {
            using var f = new Fixture();
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var first = new FnNasSubtitles(f.Scope, f.Item, f.Item.MediaSources[0], () => f.Permissions);
                var dialog = new FnNasSubtitleDialog("双版本", new[] { first, f.Service }, null,
                    (_, _) => Task.FromResult<FnNasSubtitleUpload?>(new("a.srt", [1]))) { XamlRoot = new() };
                var shown = dialog.ShowAsync();
                try
                {
                    await queue.IdleAsync();
                    dialog.FixtureVersion(1);
                    await queue.IdleAsync();
                    Assert.Equal("file-b", dialog.FixtureService.MediaGuid);
                    dialog.FixtureUpload();
                    await queue.IdleAsync();
                    Assert.Contains("/upload/file-b", f.Writes.Single().Path);
                    dialog.FixtureVersion(0);
                    await queue.IdleAsync();
                    Assert.Equal("file-a", dialog.FixtureService.MediaGuid);
                    Assert.True(dialog.Touched && dialog.NeedsRefresh);
                    Assert.False(first.Touched);
                }
                finally { dialog.Hide(); await shown; }
            });
        });
        Test("生产对话：离页期间本地读取迟到不得发送或反馈新页面", () =>
        {
            using var f = new Fixture();
            using var queue = new UiQueue();
            queue.Run(async () =>
            {
                var pick = new TaskCompletionSource<FnNasSubtitleUpload?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var dialog = Dialog(f, (_, _) => pick.Task);
                var shown = dialog.ShowAsync();
                await queue.IdleAsync();
                dialog.FixtureUpload();
                f.Invalidate("page");
                dialog.RefreshCapabilities();
                pick.TrySetResult(new("a.srt", [1]));
                await queue.IdleAsync();
                Assert.Equal(0, f.Writes.Count);
                Assert.False(dialog.Touched);
                Assert.True(dialog.FixtureClosed);
                await shown;
            });
        });
    }

    private static FnNasSubtitleDialog Dialog(Fixture f, Func<XamlRoot, Func<bool>, Task<FnNasSubtitleUpload?>>? pick = null) =>
        new("匿名电影", new[] { f.Service }, () => f.Service.IsCurrent,
            pick ?? ((_, _) => Task.FromResult<FnNasSubtitleUpload?>(null))) { XamlRoot = new() };

    private static async Task FailsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new AssertionException("Expected " + typeof(T).Name);
    }

    private sealed record Request(HttpMethod Method, string Path, string Body, bool Signed);

    private sealed class Fixture : IDisposable
    {
        private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly EmbySession _session;
        internal EmbySessionScope Scope { get; }
        internal EmbyItem Item { get; }
        internal FnNasSubtitles Service { get; }
        internal FnNasSubtitleSnapshot Snapshot { get; }
        internal ItemMenuPermissions Permissions { get; set; }
        internal List<Request> Requests { get; } = [];
        internal IReadOnlyList<Request> Writes => Requests.Where(value => value.Path.Contains("/subtitle/", StringComparison.Ordinal)
            && !value.Path.EndsWith("/search", StringComparison.Ordinal)).ToArray();
        internal string? WriteFailure { get; set; }
        internal string? Change { get; set; }
        internal HttpStatusCode? ReadFailure { get; set; }
        internal Action? BeforeReadReturn { get; set; }
        internal Action? AfterWriteReturn { get; set; }
        internal Task<HttpResponseMessage>? SearchReply { get; set; }
        private bool _pageCurrent = true;

        internal Fixture(bool? administrator = false)
        {
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = new(new AppSettings(), new SettingsStore(new AppPaths("invalid|no-IO"), new NoSecrets()),
                new CredentialVault(new NoSecrets()), device, new Handler(this));
            var connection = new EmbyConnection(new Uri("https://subtitles.invalid/v/"), "placeholder", "user", "user", "server", device)
            { ServerKind = MediaServerKind.FnNas, IsAdministrator = administrator };
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            Scope = new(_session, new(new EmbyClient(http, connection), new ServerProfile { Id = "server", Url = "https://subtitles.invalid/v/", ServerKind = MediaServerKind.FnNas },
                new AccountProfile { Id = "user", Username = "user" }));
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!).Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, Scope);
            Item = Scope.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, MediaSources = [new() { Id = "file-a", Path = "/media/a.mkv" }, new() { Id = "file-b", Path = "/media/b.mkv" }] });
            Permissions = new(IsAdministrator: administrator);
            Service = new(Scope, Item, Item.MediaSources[1], () => Permissions, () => _pageCurrent);
            Snapshot = Reload();
        }
        internal IReadOnlyList<FnNasSubtitleCandidate> Search() => Service.SearchAsync("zh-CN").Finish();
        internal FnNasSubtitleSnapshot Reload() => Service.ReloadAsync().Finish();
        internal void ClearCurrent() => typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, null);
        internal void Invalidate(string what)
        {
            if (what == "page") _pageCurrent = false;
            else if (what == "source") Scope.IdentityState.Lifetime.Cancel();
            else
            {
                _session.ReplaceClient(Scope, Scope.Client.Connection with { IsAdministrator = false });
                Permissions = new(IsAdministrator: false, CanManageSubtitles: false);
            }
        }
        internal JsonObject Streams()
        {
            var files = new JsonArray(new JsonObject { ["guid"] = "file-a", ["path"] = "/media/a.mkv" },
                new JsonObject { ["guid"] = Change == "id" ? "file-c" : "file-b", ["path"] = Change == "path" ? "/media/replaced.mkv" : "/media/b.mkv" });
            if (Change == "missing") files.RemoveAt(1);
            if (Change == "duplicate") files.Add(files[1]!.DeepClone());
            var subtitles = new JsonArray(new JsonObject { ["guid"] = "sidecar-a", ["media_guid"] = "file-a", ["is_external"] = 1, ["index"] = 1, ["format"] = "srt", ["filename"] = "a.srt" },
                new JsonObject { ["guid"] = "sidecar-b", ["media_guid"] = "file-b", ["is_external"] = 1, ["format"] = "srt", ["filename"] = Change == "subtitle" ? "changed.srt" : "b.srt", ["language"] = "chi" });
            if (Change == "same-label") subtitles.Add(new JsonObject { ["guid"] = "duplicate-name-b", ["media_guid"] = "file-b", ["is_external"] = 1, ["format"] = "srt", ["filename"] = "b.srt" });
            if (Change == "extra-and-url")
            {
                subtitles.Add(new JsonObject { ["guid"] = "extra-b", ["media_guid"] = "file-b", ["extra_file"] = 1, ["format"] = "ass", ["filename"] = "extra.ass" });
                subtitles.Add(new JsonObject { ["guid"] = "remote-b", ["media_guid"] = "file-b", ["is_external"] = 1, ["format"] = "srt", ["filename"] = "remote.srt", ["url"] = "https://foreign.invalid/remote.srt" });
            }
            return new JsonObject
            {
                ["files"] = files,
                ["video_streams"] = new JsonArray(new JsonObject { ["guid"] = "video-b", ["media_guid"] = "file-b", ["index"] = 0, ["codec_name"] = "h264" }),
                ["audio_streams"] = new JsonArray(),
                ["subtitle_streams"] = subtitles
            };
        }
        public void Dispose() => _session.Dispose();

        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath;
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                fixture.Requests.Add(new(request.Method, path, body, request.Headers.Contains("authx")));
                Assert.Equal("placeholder", request.Headers.GetValues("Authorization").Single());
                if (path.Contains("/stream/list/", StringComparison.Ordinal))
                {
                    fixture.BeforeReadReturn?.Invoke();
                    return fixture.ReadFailure is { } status ? new(status) : Reply(fixture.Streams());
                }
                if (path.EndsWith("/user/info", StringComparison.Ordinal)) return Reply(new JsonObject { ["is_admin"] = 1 });
                if (path.EndsWith("/subtitle/search", StringComparison.Ordinal) && fixture.SearchReply is { } searchReply) return await searchReply;
                if (path.EndsWith("/subtitle/search", StringComparison.Ordinal)) return Reply(new JsonObject { ["subtitles"] = new JsonArray(
                    new JsonObject { ["trim_id"] = "provider", ["filename"] = "候选.srt", ["format"] = "srt", ["download"] = 20 },
                    new JsonObject { ["trim_id"] = "provider", ["filename"] = "重复.srt", ["format"] = "srt" },
                    new JsonObject { ["trim_id"] = "bitmap", ["filename"] = "候选.sup", ["format"] = "sup" }) });
                if (path.Contains("/subtitle/", StringComparison.Ordinal))
                {
                    if (fixture.WriteFailure == "http403") return new(HttpStatusCode.Forbidden);
                    if (fixture.WriteFailure == "http401") return new(HttpStatusCode.Unauthorized);
                    if (fixture.WriteFailure == "http503") return new(HttpStatusCode.ServiceUnavailable);
                    if (fixture.WriteFailure == "business-permission") return Raw("{\"code\":-5}");
                    if (fixture.WriteFailure == "business-file-permission") return Raw("{\"code\":-16}");
                    if (fixture.WriteFailure == "business-expired") return Raw("{\"code\":-2}");
                    if (fixture.WriteFailure == "business-token") return Raw("{\"code\":-15}");
                    if (fixture.WriteFailure == "business-parameter") return Raw("{\"code\":-1}");
                    if (fixture.WriteFailure == "broken-envelope") return Raw("{\"data\":null}");
                    if (fixture.WriteFailure == "transport") throw new HttpRequestException("anonymous");
                    if (fixture.AfterWriteReturn is { } after) return new(HttpStatusCode.OK) { Content = new StreamContent(new CompletingStream("{\"code\":0,\"data\":null}", after)) };
                    return Reply(null);
                }
                throw new AssertionException("未登记匿名路径：" + path);
            }
            private static HttpResponseMessage Reply(JsonNode? data) => Raw(new JsonObject { ["code"] = 0, ["data"] = data }.ToJsonString());
            private static HttpResponseMessage Raw(string data) => new(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") };
        }
        private sealed class CompletingStream(string body, Action completed) : MemoryStream(Encoding.UTF8.GetBytes(body))
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var count = Read(buffer.Span);
                if (count == 0) completed();
                return ValueTask.FromResult(count);
            }
        }
        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string plaintext) => throw new AssertionException("禁止凭据或设置写入");
            public string Unprotect(string protectedText) => throw new AssertionException("禁止读取真实凭据");
        }
    }
}
