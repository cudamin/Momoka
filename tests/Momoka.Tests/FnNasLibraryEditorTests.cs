using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Tests;

/// <summary>Real native-library VM/editor/client with memory-only source state and read/write fake HTTP.</summary>
internal static class FnNasLibraryEditorTests
{
    private const string Playlist = "#EXTM3U\n#EXTINF:-1,Fixture\nhttps://channel.invalid/live.m3u8\n";
    private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("飞牛媒体库 IPTV 真实 VM 回归（假 HTTP／无登录、文件框或真实写入）");
        try
        {
            var shell = new SettingsPageStateTests.ShellFixture(shellAssemblyPath, sourceRoot);
            Register(shell);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Register(SettingsPageStateTests.ShellFixture shell)
    {
        foreach (var category in new[] { "Movie", "TV", "Mix", "Others" })
        {
            Case($"R-020 {category}无源切IPTV，显示新源整区且空源不发更新", async () =>
            {
                using var state = new State(shell, Document(category));
                await state.Open();
                state.Set("Category", "IPTV");
                Assert.False(state.Get<bool>("HasSavedPlaylist"));
                Assert.Equal("Visible", state.Get<object>("PlaylistEditorVisibility").ToString());
                Assert.Equal("Collapsed", state.Get<object>("KeepPlaylistVisibility").ToString());
                await state.Save();
                Assert.Equal(0, state.Transport.Updates.Count);
                Assert.True(state.Get<bool>("NoticeOpen"));
                Assert.Equal("Error", state.Get<object>("NoticeSeverity").ToString());
            });
            Case($"R-020 {category}切IPTV有效URL，实际发送来源而非只有类别", async () =>
            {
                using var state = new State(shell, Document(category));
                await state.Open();
                state.Set("Category", "IPTV");
                state.ConfigurePlaylist(0);
                await state.Save();
                var body = state.Transport.Updates.Single();
                Assert.Equal("IPTV", body["category"]!.GetValue<string>());
                Assert.True(body["iptv"] is JsonObject, "类别转换必须发送实际来源而非只更新类别");
                Assert.Equal("url", body["iptv"]!["source_type"]!.GetValue<string>());
                Assert.Equal("https://channel.invalid/list.m3u8", body["iptv"]!["source_url"]!.GetValue<string>());
                Assert.False(body.ContainsKey("guid"));
                Assert.False(state.Get<bool>("Editing"));
                Assert.Equal(1, state.Transport.AdminReads);
                Assert.False(state.Original!.ContainsKey("iptv"), "编辑不能污染原文档");
            });
        }
        foreach (var type in new[] { "text", "local_file", "upload" })
            Case($"R-020 既有IPTV {type}只改名保留旧来源，不清源/上传", async () =>
            {
                var document = Document("IPTV");
                document["iptv"] = new JsonObject { ["source_type"] = type };
                using var state = new State(shell, document);
                await state.Open();
                Assert.True(state.Get<bool>("HasSavedPlaylist"));
                Assert.Equal("Collapsed", state.Get<object>("PlaylistEditorVisibility").ToString());
                state.Set("LibraryName", "renamed");
                await state.Save();
                var body = state.Transport.Updates.Single();
                Assert.Equal("renamed", body["name"]!.GetValue<string>());
                Assert.False(body.ContainsKey("iptv"), "保留来源语义以不更新该字段表达");
                Assert.Equal(0, state.Transport.Uploads);
                Assert.True(state.Original!["iptv"] is JsonObject);
            });
        Case("R-020 普通库带陈旧text元数据，也不能误判已有IPTV来源", async () =>
        {
            var document = Document("Movie");
            document["iptv"] = new JsonObject { ["source_type"] = "text" };
            using var state = new State(shell, document);
            await state.Open();
            state.Set("Category", "IPTV");
            Assert.False(state.Get<bool>("HasSavedPlaylist"));
            await state.Save();
            Assert.Equal(0, state.Transport.Updates.Count);
        });
        foreach (var type in new[] { "", "unknown" })
            Case($"R-020 IPTV source_type={type}不是可保留来源，空值不写", async () =>
            {
                var document = Document("IPTV");
                document["iptv"] = new JsonObject { ["source_type"] = type };
                using var state = new State(shell, document);
                await state.Open();
                Assert.False(state.Get<bool>("HasSavedPlaylist"));
                await state.Save();
                Assert.Equal(0, state.Transport.Updates.Count);
            });
        Case("R-020 既有URL仍可编辑并原样发送URL，不改保留文本语义", async () =>
        {
            var document = Document("IPTV");
            document["iptv"] = new JsonObject { ["source_type"] = "url", ["source_url"] = "https://channel.invalid/list.m3u8", ["refresh_interval"] = 3600 };
            using var state = new State(shell, document);
            await state.Open();
            Assert.False(state.Get<bool>("HasSavedPlaylist"));
            Assert.Equal("Visible", state.Get<object>("PlaylistEditorVisibility").ToString());
            await state.Save();
            Assert.Equal(3600, state.Transport.Updates.Single()["iptv"]!["refresh_interval"]!.GetValue<int>());
        });
        foreach (var mode in new[] { 0, 1, 2, 3 })
            Case($"R-020 新建IPTV四种导入模式{mode}都实际生成有效源", async () =>
            {
                using var state = new State(shell, original: null);
                await state.Open();
                state.Set("LibraryName", "fixture");
                state.Set("Category", "IPTV");
                Assert.False(state.Get<bool>("HasSavedPlaylist"));
                state.ConfigurePlaylist(mode);
                await state.Save();
                var body = state.Transport.Updates.Single();
                Assert.Equal(new[] { "url", "text", "local_file", "upload" }[mode], body["iptv"]!["source_type"]!.GetValue<string>());
                Assert.Equal(mode == 3 ? 1 : 0, state.Transport.Uploads);
            });
        Case("R-020 明确更换既有文本源必须填写内容，未填不写", async () =>
        {
            var document = Document("IPTV");
            document["iptv"] = new JsonObject { ["source_type"] = "text" };
            using var state = new State(shell, document);
            await state.Open();
            state.Set("ReplacePlaylist", true);
            Assert.Equal("Visible", state.Get<object>("PlaylistEditorVisibility").ToString());
            await state.Save();
            Assert.Equal(0, state.Transport.Updates.Count);
            state.ConfigurePlaylist(1);
            await state.Save();
            Assert.Equal(Playlist, state.Transport.Updates.Single()["iptv"]!["content"]!.GetValue<string>());
        });
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0d, 1025d })
            Case($"R-021 隐藏文件大小{value}不能阻止有效IPTV保存且偏好保留", async () =>
            {
                using var state = new State(shell, Document("Movie"));
                await state.Open();
                state.Set("MinimumFileSize", value);
                state.Set("Category", "IPTV");
                state.ConfigurePlaylist(0);
                Assert.Equal("Collapsed", state.Get<object>("FilesVisibility").ToString());
                await state.Save();
                Assert.Equal(1, state.Transport.Updates.Count);
                var kept = state.Get<double>("MinimumFileSize");
                Assert.True(double.IsNaN(value) ? double.IsNaN(kept) : value.Equals(kept));
                Assert.Equal(3, state.Transport.Updates.Single()["skip_filesize"]!.GetValue<int>(), "隐藏字段不从草稿NaN重新写入");
            });
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, 0d, 1025d })
            Case($"R-021 普通库启用大小过滤仍拒绝{value}且零写入", async () =>
            {
                using var state = new State(shell, Document("Movie"));
                await state.Open();
                state.Set("MinimumFileSize", value);
                await state.Save();
                Assert.Equal(0, state.Transport.Updates.Count);
                Assert.Equal("Error", state.Get<object>("NoticeSeverity").ToString());
            });
        foreach (var value in new[] { 1d, 1024d })
            Case($"R-021 普通库有效边界{value}保留原校验与正文", async () =>
            {
                using var state = new State(shell, Document("Movie"));
                await state.Open();
                state.Set("MinimumFileSize", value);
                await state.Save();
                Assert.Equal((int)value, state.Transport.Updates.Single()["skip_filesize"]!.GetValue<int>());
            });
        Case("R-021 关闭文件过滤不被保留NaN阻断，普通库正文为0", async () =>
        {
            using var state = new State(shell, Document("Movie"));
            await state.Open();
            state.Set("MinimumFileSize", double.NaN);
            state.Set("FilterSmallFiles", false);
            await state.Save();
            Assert.Equal(0, state.Transport.Updates.Single()["skip_filesize"]!.GetValue<int>());
            Assert.True(double.IsNaN(state.Get<double>("MinimumFileSize")));
        });
        Case("R-021 IPTV切回普通库原NaN仍报错，不清偏好洗绿", async () =>
        {
            using var state = new State(shell, Document("Movie"));
            await state.Open();
            state.Set("MinimumFileSize", double.NaN);
            state.Set("Category", "IPTV");
            state.ConfigurePlaylist(0);
            state.Set("Category", "Movie");
            await state.Save();
            Assert.Equal(0, state.Transport.Updates.Count);
            Assert.True(double.IsNaN(state.Get<double>("MinimumFileSize")));
        });
        Case("R-020/R-021 非管理员仍不能写库，目标源校验不绕过权限", async () =>
        {
            using var state = new State(shell, Document("Movie")) { PermitAdminWrite = false };
            await state.Open();
            state.Set("Category", "IPTV");
            state.ConfigurePlaylist(0);
            await state.Save();
            Assert.Equal(0, state.Transport.Updates.Count);
            Assert.Equal(1, state.Transport.AdminReads);
        });
        Case("R-020/R-021 来源撤销后保存零请求，不借另一个默认身份", async () =>
        {
            using var state = new State(shell, Document("Movie"));
            await state.Open();
            var requests = state.Transport.Requests;
            state.Scope.IdentityState.Lifetime.Cancel();
            await state.Save();
            Assert.Equal(requests, state.Transport.Requests);
            Assert.Equal(0, state.Transport.Updates.Count);
        });
    }

    private static JsonObject Document(string category) => new()
    {
        ["guid"] = "library",
        ["name"] = "fixture",
        ["category"] = category,
        ["dir_list"] = new JsonArray("/volume/media"),
        ["skip_filesize"] = 3
    };
    private static void Case(string name, Func<Task> action) => TestHarness.Test(name, () => action().GetAwaiter().GetResult());

    private sealed class State : IDisposable
    {
        private readonly SettingsPageStateTests.ShellFixture _shell;
        private readonly object _vm;
        private readonly EmbySession _session;
        public EditorTransport Transport { get; }
        public JsonObject? Original { get; }
        public EmbySessionScope Scope { get; }
        public bool PermitAdminWrite { set => Transport.PermitAdminWrite = value; }
        public State(SettingsPageStateTests.ShellFixture shell, JsonObject? original)
        {
            _shell = shell;
            Original = original;
            Transport = new EditorTransport(original);
            var settings = new AppSettings();
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "iptv-config|forbidden-IO"));
            _session = new EmbySession(settings, new SettingsStore(paths, new NoSecrets()),
                new CredentialVault(new NoSecrets()), device, Transport);
            var connection = new EmbyConnection(new Uri("https://library-edit.invalid/v/"), "placeholder-only", "fixture", "fixture", "fixture", device)
            { ServerKind = MediaServerKind.FnNas, IsAdministrator = true };
            var server = new ServerProfile { Id = "fixture", Url = "https://library-edit.invalid/v/", ServerKind = MediaServerKind.FnNas };
            var account = new AccountProfile { Id = "fixture", Username = "fixture" };
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            Scope = new EmbySessionScope(_session, new EmbySessionScope.Identity(new EmbyClient(http, connection), server, account));
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!;
            sources.Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, Scope);
            _vm = shell.NewRow("FnNasLibrariesViewModel");
            shell.Invoke(_vm, "Attach", _session, Scope.Key);
        }
        public Task Open()
        {
            var row = Original is null ? null : _shell.NewRow("FnNasLibraryRow", Original);
            return (Task)_shell.Invoke(_vm, "OpenAsync", row)!;
        }
        public T Get<T>(string name) => _shell.Get<T>(_vm, name);
        public void Set(string name, object value) => _shell.Set(_vm, name, value);
        public Task Save() => (Task)_shell.Invoke(_vm, "SaveAsync")!;
        public void ConfigurePlaylist(int mode)
        {
            Set("PlaylistMode", mode);
            switch (mode)
            {
                case 0: Set("PlaylistUrl", "https://channel.invalid/list.m3u8"); break;
                case 1: Set("PlaylistText", Playlist); break;
                case 2: Set("PlaylistPath", "/volume/fixture/list.m3u8"); break;
                case 3:
                    var type = _vm.GetType().Assembly.GetType("Momoka.Shell.Views.PickedFnNasPlaylist", throwOnError: true)!;
                    var file = Activator.CreateInstance(type, Members, binder: null, [Encoding.UTF8.GetBytes(Playlist), "fixture.m3u8"], culture: null)!;
                    _vm.GetType().GetField("_playlist", Members)!.SetValue(_vm, file);
                    break;
            }
        }
        public void Dispose()
        {
            ((IDisposable)_vm).Dispose();
            _session.Dispose();
        }
    }

    private sealed class EditorTransport(JsonObject? original) : HttpMessageHandler
    {
        public ConcurrentQueue<JsonObject> Updates { get; } = new();
        public int Uploads { get; private set; }
        public int Requests { get; private set; }
        public int AdminReads { get; private set; }
        public bool PermitAdminWrite { get; set; } = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.Equal("library-edit.invalid", uri.Host);
            Assert.True(uri.AbsolutePath.StartsWith("/v/api/v1/", StringComparison.Ordinal));
            Requests++;
            JsonNode? data;
            var path = uri.AbsolutePath[10..];
            if (request.Method == HttpMethod.Get)
                data = path switch
                {
                    "mdb/library" => original?.DeepClone() ?? throw new AssertionException("新库不应读取旧库"),
                    "server/info" => new JsonObject { ["supported_languages"] = new JsonArray("any") },
                    "mdb/list" => new JsonArray(),
                    "user/info" => Admin(),
                    _ => throw new AssertionException("未授权的假GET：" + path)
                };
            else if (request.Method == HttpMethod.Post && path == "mdb/iptv/temp/upload")
            {
                Assert.True(request.Content is MultipartFormDataContent, "上传必须走真实multipart但不读本机文件");
                Uploads++;
                data = new JsonObject { ["temp_file_id"] = "fixture-temp" };
            }
            else if ((request.Method == HttpMethod.Post && path == "mdb/library")
                || (request.Method == HttpMethod.Put && path == "mdb/create"))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Updates.Enqueue(JsonNode.Parse(body)!.AsObject());
                data = new JsonObject();
            }
            else throw new AssertionException("未授权的假写入：" + request.Method + " " + path);
            var envelope = new JsonObject { ["code"] = 0, ["data"] = data };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json") };
        }
        private JsonObject Admin()
        {
            AdminReads++;
            return new JsonObject { ["is_admin"] = PermitAdminWrite ? 1 : 0 };
        }
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("不允许凭据/配置IO");
        public string Unprotect(string cipherText) => throw new AssertionException("不允许凭据/配置IO");
    }
}
