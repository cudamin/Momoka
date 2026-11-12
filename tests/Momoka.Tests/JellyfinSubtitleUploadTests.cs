using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Current Core operations and permission publication against anonymous HTTP; no real files or accounts.</summary>
internal static class JellyfinSubtitleUploadTests
{
    private const string ItemA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ItemB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public static void Register()
    {
        Test("Jellyfin上传原生探针分区只接受独立合法启动", () =>
        {
            Assert.True(ShellProbeSections.IsKnown("JELLYFIN-UPLOAD"));
            Assert.True(StartupArgs.ShellProbeSectionFilter(["--probe-shell", "--probe-section", "jellyfin-upload,JELLYFIN-UPLOAD"])!
                .SetEquals([ShellProbeSections.JellyfinUpload]));
            StartupArgs.ValidateShellProbe(["--probe-shell", "--probe-section", "jellyfin-upload", "--screen", "1"]);
            Assert.Throws<ArgumentException>(() => StartupArgs.ValidateShellProbe(["--probe-shell", "--probe-section", "jellyfin-upload", "--play"]));
        });

        foreach (var version in new[] { "10.10.7", "12.4.0" })
            foreach (var grant in new bool?[] { true, false, null })
                Test($"Jellyfin {version} 恢复令牌上传权限[{grant}]进入当前Scope", () => Run(async () =>
                {
                    var paths = new AppPaths(Path.Combine(AppContext.BaseDirectory, "jellyfin-upload-restore", Guid.NewGuid().ToString("N")));
                    paths.EnsureCreated();
                    var settings = new AppSettings();
                    settings.Servers.Clear();
                    var store = new SettingsStore(paths, PassthroughSecretProtector.Instance);
                    var server = new ServerProfile { Name = "fixture", Url = "https://jellyfin-upload.invalid/media", ServerKind = MediaServerKind.Jellyfin };
                    var account = new AccountProfile { Username = "user", UserId = "user", ProtectedAccessToken = "offline-placeholder" };
                    server.Accounts.Add(account);
                    settings.Servers.Add(server);
                    using var transport = new Transport { PolicyGrant = grant, ServerVersion = version };
                    using var session = new EmbySession(settings, store, new CredentialVault(PassthroughSecretProtector.Instance), Connection().Device, transport);
                    try
                    {
                        Assert.True(await session.TryRestoreAsync(server, account, default));
                        Assert.Equal(grant, session.Connection!.CanUploadSubtitles);
                        var scope = session.Capture();
                        Assert.Equal(grant, scope.CanUploadSubtitles);
                        var capabilities = new ServerCapabilities(session);
                        Assert.Equal(grant, capabilities.PermissionsFor(scope.Key).CanUploadSubtitles);
                        var permissions = capabilities.PermissionsFor(scope.Key);
                        Assert.True(ItemMenu.Allows(ItemCommand.Subtitles, permissions, MediaServerKind.Jellyfin));
                        Assert.True(ItemMenu.For(new EmbyItem { Id = ItemA, Type = EmbyItemType.Movie }, moviePilotEnabled: false,
                            jellyfin: true, permissions: permissions).Any(row => row.Command == ItemCommand.Subtitles));
                        Assert.Equal(0, transport.Writes);
                    }
                    finally { if (Directory.Exists(paths.Root)) Directory.Delete(paths.Root, true); }
                }));

        foreach (var grant in new bool?[] { false, true, null })
            Test($"Jellyfin字幕上传权限独立于下载和管理员[{grant}]", () =>
            {
                var policy = new EmbyUserPolicy { IsAdministrator = true, EnableSubtitleDownloading = true, EnableSubtitleManagement = grant };
                Assert.Equal(grant, UploadGrant(policy.ApplyTo(Connection())));
                Assert.Equal(grant, UploadGrant(policy.ToPermissions(MediaServerKind.Jellyfin)));
                Assert.Equal(false, UploadGrant(policy.ToPermissions(MediaServerKind.Emby)));
                Assert.Equal(false, UploadGrant(policy.ApplyTo(Connection(MediaServerKind.Emby))));
            });

        foreach (var extension in new[] { "srt", "SUB", "vtt", "ass", "ssa", "mks" })
            Test($"Jellyfin上传保留原始字节与格式[{extension}]", () =>
            {
                var bytes = new byte[] { 0xff, 0xfe, 0x2d, 0x4e, 0x87, 0x65, 0, 0x0a };
                var file = File("字幕." + extension, bytes);
                bytes[0] = 0;
                Assert.Equal(extension.ToLowerInvariant(), Value<string>(file, "Format"));
                Assert.Equal("//4tTodlAAo=", Value<string>(file, "Data"));
            });

        foreach (var name in new[] { "movie.mkv", "caption.idx", "caption", ".srt", "../caption.srt", "a\\caption.srt", "x\0.srt" })
            Test($"Jellyfin字幕上传非法文件名拒绝[{name.Replace('\0', '_')}]", () =>
                Assert.Throws<InvalidOperationException>(() => File(name, [1])));

        Test("Jellyfin字幕上传空文件、10MiB边界和超限", () =>
        {
            Assert.Throws<InvalidOperationException>(() => File("caption.srt", []));
            var maximum = 10 * 1024 * 1024;
            Assert.Equal(maximum, Value<int>(File("caption.srt", new byte[maximum]), "ByteCount"));
            Assert.Throws<InvalidOperationException>(() => File("caption.srt", new byte[maximum + 1]));
        });

        Test("Jellyfin字幕上传空草稿本地拒绝且不产生未知回执", () => Run(async () =>
        {
            using var fixture = new Fixture();
            await ThrowsAsync<ArgumentNullException>(() => fixture.Operations().UploadAsync(null!));
            Assert.Equal(0, fixture.Transport.Requests.Count);
        }));

        Test("Jellyfin字幕上传空文件和语言在构造时拒绝", () =>
        {
            Assert.Throws<ArgumentNullException>(() => new ServerSubtitleFile("a.srt", null!));
            Assert.Throws<ArgumentNullException>(() => new ServerSubtitleUpload(null!, "eng"));
            Assert.Throws<ArgumentNullException>(() => new ServerSubtitleUpload(new ServerSubtitleFile("a.srt", [1]), null!));
        });

        foreach (var language in new[] { "", "en", "../", "eng/x", "中文", "e1g" })
            Test($"Jellyfin字幕上传语言安全段校验[{language}]", () =>
                Assert.Throws<InvalidOperationException>(() => Upload(File("a.srt", [1]), language)));

        foreach (var version in new[] { "10.10.7", "12.4.0" })
            Test($"Jellyfin {version} 上传非首版本五字段JSON与原字节base64", () => Run(async () =>
            {
                using var fixture = new Fixture(version);
                var operations = fixture.Operations();
                var bytes = new byte[] { 0xd6, 0xd0, 0xce, 0xc4, 0x0d, 0x0a };
                await Send(operations, Upload(File("旧编码.SRT", bytes), "CHI", true, true));
                var write = fixture.Transport.Requests.Single(request => request.Method == "POST");
                Assert.Equal($"/media/Videos/{ItemB}/Subtitles", write.Path);
                Assert.DoesNotContain("MediaSourceId", write.Query);
                using var body = JsonDocument.Parse(write.Body!);
                Assert.Equal(5, body.RootElement.EnumerateObject().Count());
                Assert.Equal(Convert.ToBase64String(bytes), body.RootElement.GetProperty("Data").GetString());
                Assert.Equal("chi", body.RootElement.GetProperty("Language").GetString());
                Assert.Equal("srt", body.RootElement.GetProperty("Format").GetString());
                Assert.True(body.RootElement.GetProperty("IsForced").GetBoolean());
                Assert.True(body.RootElement.GetProperty("IsHearingImpaired").GetBoolean());
                Assert.True(fixture.Transport.Requests.All(request => request.Host == "jellyfin-upload.invalid"));
                Assert.True(fixture.Transport.Requests.Where(request => request.Method == "GET").All(request => request.Path.EndsWith("/Items/" + ItemB, StringComparison.Ordinal)));
            }));

        foreach (var change in new[] { "source", "path", "duplicate", "item" })
            Test($"Jellyfin字幕上传复读目标[{change}]变化零写入", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var operations = fixture.Operations();
                fixture.Transport.Change = change;
                await ThrowsAsync<InvalidOperationException>(() => Send(operations, Draft()));
                Assert.Equal(0, fixture.Transport.Writes);
            }));

        foreach (var grant in new bool?[] { false, null })
            Test($"Jellyfin字幕上传无明确管理授权[{grant}]直接服务零请求", () => Run(async () =>
            {
                using var fixture = new Fixture(grant: grant);
                var operations = fixture.Operations();
                Assert.False(Value<bool>(operations, "CanUpload"));
                await ThrowsAsync<UnauthorizedAccessException>(() => Send(operations, Draft()));
                Assert.Equal(0, fixture.Transport.Requests.Count);
                fixture.Grant(true);
                Assert.True(Value<bool>(operations, "CanUpload"));
                await Send(operations, Draft());
                Assert.Equal(1, fixture.Transport.Writes);
            }));

        foreach (var restore in new[] { false, true })
            Test($"Jellyfin字幕上传验证读取期间降权{(restore ? "ABA恢复" : "")}拒绝旧操作", () => Run(async () =>
            {
                using var fixture = new Fixture();
                var operations = fixture.Operations();
                fixture.Transport.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var upload = Send(operations, Draft());
                await fixture.Transport.ReadEntered.Task;
                fixture.Grant(false);
                if (restore) fixture.Grant(true);
                fixture.Transport.ReadGate.SetResult();
                await ThrowsAsync<UnauthorizedAccessException>(() => upload);
                Assert.Equal(0, fixture.Transport.Writes);
                if (restore) { await Send(operations, Draft()); Assert.Equal(1, fixture.Transport.Writes); }
            }));

        Test("Jellyfin上传权限探测发布到当前Scope与能力并拒绝迟到授权", () => Run(async () =>
        {
            using var fixture = new Fixture(grant: null);
            fixture.Transport.PolicyGrant = true;
            await fixture.Capabilities.ProbeAsync();
            Assert.Equal(true, UploadGrant(fixture.Scope));
            Assert.Equal(true, UploadGrant(fixture.Capabilities.PermissionsFor(fixture.Scope.Key)));
            fixture.Transport.PolicyEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Transport.PolicyGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Transport.SkipSystemPolicyGate = true;
            var old = fixture.Capabilities.ProbeAsync();
            await fixture.Transport.PolicyEntered.Task;
            fixture.Grant(false);
            fixture.Transport.PolicyGate.SetResult();
            await old;
            Assert.Equal(false, UploadGrant(fixture.Scope));
            Assert.Equal(false, UploadGrant(fixture.Capabilities.PermissionsFor(fixture.Scope.Key)));
        }));

        foreach (var exit in new[] { "scope", "page" })
            Test($"Jellyfin字幕上传可靠204后[{exit}]退出仍保留成功回执", () => Run(async () =>
            {
                using var fixture = new Fixture();
                using var page = new CancellationTokenSource();
                fixture.Transport.AfterWrite = exit == "scope" ? fixture.Revoke : page.Cancel;
                await Send(fixture.Operations(page.Token), Draft());
                Assert.Equal(1, fixture.Transport.Writes);
                if (exit == "scope") Assert.False(fixture.Scope.IsAvailable);
                else Assert.True(page.IsCancellationRequested);
            }));

        foreach (var exit in new[] { "scope", "page" })
            Test($"Jellyfin字幕上传验证期间[{exit}]退出零写入", () => Run(async () =>
            {
                using var fixture = new Fixture();
                using var page = new CancellationTokenSource();
                var operations = fixture.Operations(page.Token);
                fixture.Transport.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var upload = Send(operations, Draft());
                await fixture.Transport.ReadEntered.Task;
                if (exit == "scope") fixture.Revoke(); else page.Cancel();
                fixture.Transport.ReadGate.SetResult();
                await ThrowsAsync<OperationCanceledException>(() => upload);
                Assert.Equal(0, fixture.Transport.Writes);
            }));

        foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.Accepted, HttpStatusCode.ServiceUnavailable })
            Test($"Jellyfin字幕上传非204[{status}]结果未知不重发", () => Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Transport.WriteStatus = status;
                var error = await Capture(() => Send(fixture.Operations(), Draft()));
                Assert.Equal("ServerSubtitleUploadUnconfirmedException", error.GetType().Name);
                Assert.Equal(1, fixture.Transport.Writes);
            }));

        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.BadRequest })
            Test($"Jellyfin字幕上传明确拒绝[{status}]不变成未知回执", () => Run(async () =>
            {
                using var fixture = new Fixture();
                fixture.Transport.WriteStatus = status;
                await ThrowsAsync<EmbyApiException>(() => Send(fixture.Operations(), Draft()));
                Assert.Equal(1, fixture.Transport.Writes);
            }));

        foreach (var product in new[] { MediaServerKind.Emby, MediaServerKind.FnNas, MediaServerKind.Plex })
            Test($"[{product}]不扩展Jellyfin字幕上传", () => Run(async () =>
            {
                using var fixture = new Fixture(product: product);
                Assert.False(Value<bool>(fixture.Operations(), "SupportsUpload"));
                await ThrowsAsync<NotSupportedException>(() => Send(fixture.Operations(), Draft()));
                Assert.Equal(0, fixture.Transport.Requests.Count);
            }));
    }

    private static bool? UploadGrant(object target) => Value<bool?>(target, "CanUploadSubtitles");
    private static T Value<T>(object target, string property)
    {
        var member = target.GetType().GetProperty(property);
        Assert.True(member is not null, $"Missing production property {target.GetType().Name}.{property}");
        return (T)member!.GetValue(target)!;
    }
    private static object File(string name, byte[] bytes) => Construct("ServerSubtitleFile", [name, bytes]);
    private static object Upload(object file, string language, bool forced = false, bool hearing = false) => Construct("ServerSubtitleUpload", [file, language, forced, hearing]);
    private static object Draft() => Upload(File("caption.srt", Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:01,000\n离线\n")), "chi");
    private static object Construct(string typeName, object[] args)
    {
        var type = typeof(ServerSubtitles).Assembly.GetType("Momoka.Emby." + typeName);
        Assert.True(type is not null, "Missing production upload type " + typeName);
        try { return Activator.CreateInstance(type!, args)!; }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; }
    }
    private static Task Send(ServerSubtitles operations, object upload)
    {
        var method = typeof(ServerSubtitles).GetMethod("UploadAsync", [upload.GetType()]);
        Assert.True(method is not null, "Missing production ServerSubtitles.UploadAsync");
        try { return (Task)method!.Invoke(operations, [upload])!; }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; }
    }
    private static void Run(Func<Task> action) => action().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    private static async Task<Exception> Capture(Func<Task> action)
    {
        try { await action(); }
        catch (Exception error) { return error; }
        throw new AssertionException("Expected upload rejection");
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception => Assert.True(await Capture(action) is T, "Expected " + typeof(T).Name);
    private static EmbyConnection Connection(MediaServerKind product = MediaServerKind.Jellyfin) => new(new Uri("https://jellyfin-upload.invalid/media/"), "placeholder", "user", "user", "offline", new("fixture", "fixture", "fixture", "1"))
    { ServerKind = product, ServerVersion = "10.10.7", IsAdministrator = false, CanManageSubtitles = true };

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal readonly Transport Transport = new();
        internal EmbySession Session { get; }
        internal EmbySessionScope Scope { get; }
        internal ServerCapabilities Capabilities { get; }
        internal Fixture(string version = "10.10.7", bool? grant = true, MediaServerKind product = MediaServerKind.Jellyfin)
        {
            var secrets = new NoSecrets();
            Session = new(new AppSettings(), new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "no-upload-settings-IO")), secrets), new CredentialVault(secrets), Connection().Device, Transport);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
            var connection = Connection(product) with { ServerVersion = version };
            var member = typeof(EmbyConnection).GetProperty("CanUploadSubtitles");
            Assert.True(member is not null, "Missing current connection upload grant");
            member!.SetValue(connection, grant);
            Scope = new(Session, new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = "server" }, new AccountProfile { Id = "account" }));
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(Session)!).Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", Private)!.SetValue(Session, Scope);
            Capabilities = new(Session);
        }
        internal ServerSubtitles Operations(CancellationToken lifetime = default)
        {
            var item = Transport.Item(ItemA);
            var source = item.MediaSources[1];
            if (!lifetime.CanBeCanceled) return new(Scope, item, source);
            var constructor = typeof(ServerSubtitles).GetConstructor([typeof(EmbySessionScope), typeof(EmbyItem), typeof(MediaSource), typeof(CancellationToken)]);
            Assert.True(constructor is not null, "Missing page-bound upload lifetime");
            return (ServerSubtitles)constructor!.Invoke([Scope, item, source, lifetime]);
        }
        internal void Grant(bool grant)
        {
            var connection = Scope.CurrentConnection with { IsAdministrator = false };
            typeof(EmbyConnection).GetProperty("CanUploadSubtitles")!.SetValue(connection, grant);
            Session.ReplaceClient(Scope, connection);
            Session.NotifyPermissionsChanged(Scope);
        }
        internal void Revoke() => Scope.IdentityState.Lifetime.Cancel();
        public void Dispose() => Session.Dispose();
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string text) => throw new InvalidOperationException("No secret IO");
        public string Unprotect(string text) => throw new InvalidOperationException("No secret IO");
    }
    private sealed record Request(string Method, string Host, string Path, string Query, string? Body);
    private sealed class Transport : HttpMessageHandler
    {
        internal List<Request> Requests { get; } = [];
        internal int Writes { get; private set; }
        internal string? Change { get; set; }
        internal bool? PolicyGrant { get; set; }
        internal string ServerVersion { get; set; } = "10.10.7";
        internal TaskCompletionSource? ReadGate { get; set; }
        internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? PolicyGate { get; set; }
        internal TaskCompletionSource PolicyEntered { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool SkipSystemPolicyGate { get; set; }
        internal Action? AfterWrite { get; set; }
        internal HttpStatusCode WriteStatus { get; set; } = HttpStatusCode.NoContent;
        internal EmbyItem Item(string id)
        {
            var b = new MediaSource { Id = Change == "source" ? "cccccccccccccccccccccccccccccccc" : ItemB, Path = Change == "path" ? "/virtual/changed.mkv" : "/virtual/b.mkv", Name = "B" };
            return new EmbyItem { Id = Change == "item" ? ItemA : id, Type = EmbyItemType.Movie, MediaSources = Change == "duplicate" ? [new() { Id = ItemA, Path = "/virtual/a.mkv" }, b, b] : [new() { Id = ItemA, Path = "/virtual/a.mkv" }, b] };
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            Assert.Equal("jellyfin-upload.invalid", uri.Host);
            Requests.Add(new(request.Method.Method, uri.Host, uri.AbsolutePath, uri.Query, request.Content is null ? null : await request.Content.ReadAsStringAsync(token)));
            if (request.Method == HttpMethod.Get && uri.AbsolutePath.Contains("/Items/", StringComparison.Ordinal))
            {
                ReadEntered.TrySetResult();
                if (ReadGate is { } read) await read.Task.WaitAsync(token);
                return Json(Item(uri.Segments[^1]));
            }
            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/Users/Me", StringComparison.Ordinal))
            {
                var grant = PolicyGrant;
                if (SkipSystemPolicyGate) SkipSystemPolicyGate = false;
                else
                {
                    PolicyEntered.TrySetResult();
                    if (PolicyGate is { } policy) await policy.Task.WaitAsync(token);
                }
                return Json(new EmbyUser { Id = "user", Policy = new() { IsAdministrator = false, EnableSubtitleDownloading = true, EnableSubtitleManagement = grant } });
            }
            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return Json(new { ProductName = "Jellyfin", Version = ServerVersion, Id = "upload-fixture" });
            if (request.Method == HttpMethod.Get && (uri.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal)
                || uri.AbsolutePath.EndsWith("/UserViews", StringComparison.Ordinal))) return Json(new { Items = Array.Empty<EmbyItem>() });
            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/System/Info", StringComparison.Ordinal)) return new(HttpStatusCode.Forbidden);
            if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/Subtitles", StringComparison.Ordinal)) { Writes++; AfterWrite?.Invoke(); return new(WriteStatus); }
            throw new AssertionException("Unexpected fixture endpoint " + uri.AbsolutePath);
        }
        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
