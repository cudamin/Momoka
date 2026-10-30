using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual production edit/service/client with anonymous memory HTTP. No WinUI or real server writes.</summary>
internal static class EmbyMetadataEditorTests
{
    public static void Register()
    {
        foreach (var stamp in new[] { "2026-10-28T09:33:45.1234567Z", "2026-10-28T00:33:45.1234567-09:00", "2026-10-28T23:33:45.1234567+14:00" })
        {
            Test($"Emby元数据：完整表单原样保存不改偏移或毫秒 {stamp}", () =>
            {
                var item = Item();
                item["DateCreated"] = stamp;
                var before = item.ToJsonString();
                Assert.False(EmbyMetadataEdit.Read(item).ApplyTo(item));
                Assert.Equal(before, item.ToJsonString());
            });
        }
        Test("Emby元数据：未碰到的空字符串与异常旧日期保持原样", () =>
        {
            var item = Item();
            foreach (var key in new[] { "OriginalTitle", "ForcedSortName", "Overview", "CustomRating", "Video3DFormat" }) item[key] = "";
            item["DateCreated"] = "invalid-legacy";
            item["PremiereDate"] = "invalid-legacy";
            var before = item.ToJsonString();
            Assert.False(EmbyMetadataEdit.Read(item).ApplyTo(item));
            Assert.Equal(before, item.ToJsonString());
        });
        Test("Emby元数据：人物工作室名称空白不在原样保存时重写", () =>
        {
            var item = Item();
            item["People"]![0]!["Name"] = "  演员一  ";
            item["People"]![0]!["Role"] = "";
            item["Studios"]![0]!["Name"] = "  公司, A  ";
            var before = item.ToJsonString();
            Assert.False(EmbyMetadataEdit.Read(item).ApplyTo(item));
            Assert.Equal(before, item.ToJsonString());
        });
        Test("Emby元数据：新增字段和未知锁值能读回", () =>
        {
            var form = EmbyMetadataEdit.Read(Item());
            Assert.Equal("TV-14", form.CustomRating);
            Assert.Equal("HalfSideBySide", form.Video3DFormat);
            Assert.Equal("zh", form.PreferredMetadataLanguage);
            Assert.Equal("CN", form.PreferredMetadataCountryCode);
            Assert.True(form.LockData);
            Assert.True(form.LockedFields.SequenceEqual(["Name", "Cast", "FutureLock"]));
            Assert.Equal("tt50122563", form.ProviderIds["Imdb"]);
            Assert.Equal("角色, (voice)", form.People[0].Role);
            Assert.Equal("公司, A", form.Studios[0].Name);
        });
        Test("Emby元数据：只改标题保留未编辑字段", () =>
        {
            var item = Item();
            var before = (JsonObject)item.DeepClone();
            var form = EmbyMetadataEdit.Read(item);
            Assert.True((form with { Basic = form.Basic with { Name = "新标题" } }).ApplyTo(item));
            foreach (var key in new[] { "People", "Studios", "ProviderIds", "DateCreated", "PremiereDate", "LockedFields", "UnknownMetadata", "MediaSources" })
                Assert.True(JsonNode.DeepEquals(before[key], item[key]), key);
        });
        Test("Emby元数据：加入日期实际改变使用本地时刻转UTC", () =>
        {
            var item = Item();
            var form = EmbyMetadataEdit.Read(item) with { DateCreated = "2026-10-29 18:12:34" };
            Assert.True(form.ApplyTo(item));
            var expected = new DateTimeOffset(new DateTime(2026, 10, 29, 18, 12, 34, DateTimeKind.Local)).ToUniversalTime();
            Assert.Equal(expected, DateTimeOffset.Parse(item["DateCreated"]!.GetValue<string>(), CultureInfo.InvariantCulture));
            Assert.False(EmbyMetadataEdit.Read(item).ApplyTo(item));
        });
        Test("Emby元数据：加入日期清空且不补假日期", () =>
        {
            var item = Item();
            Assert.True((EmbyMetadataEdit.Read(item) with { DateCreated = " " }).ApplyTo(item));
            Assert.Null(item["DateCreated"]);
            Assert.False(EmbyMetadataEdit.Read(item).ApplyTo(item));
        });
        foreach (var value in new[] { "不是日期", "2026-02-30 10:11:12", "2026-10-28 25:00:00", "2026-10-28" })
            Test($"Emby元数据：错误加入日期拒绝且不先改标题 {value}", () =>
            {
                var item = Item();
                var before = item.ToJsonString();
                var form = EmbyMetadataEdit.Read(item);
                var edited = form with { DateCreated = value, Basic = form.Basic with { Name = "不应写入" } };
                Assert.NotNull(edited.Problem);
                Assert.Throws<InvalidOperationException>(() => edited.ApplyTo(item));
                Assert.Equal(before, item.ToJsonString());
            });
        Test("Emby元数据：独立列表支持逗号并保留原顺序", () =>
        {
            var item = Item();
            var form = EmbyMetadataEdit.Read(item);
            Assert.True((form with { Genres = ["科幻,奇幻", "剧情", "剧情"], Tags = ["一个、完整标签"] }).ApplyTo(item));
            var read = EmbyMetadataEdit.Read(item);
            Assert.True(read.Genres.SequenceEqual(["科幻,奇幻", "剧情"]));
            Assert.True(read.Tags.SequenceEqual(["一个、完整标签"]));
            Assert.False(read.ApplyTo(item));
        });
        Test("Emby元数据：人物重排保留ID图像与未知属性", () =>
        {
            var item = Item();
            var form = EmbyMetadataEdit.Read(item);
            Assert.True((form with { People = [form.People[1], form.People[0] with { Role = "新角色" }] }).ApplyTo(item));
            var people = item["People"]!.AsArray();
            Assert.Equal("person-2", people[0]!["Id"]!.GetValue<string>());
            Assert.Equal("person-1", people[1]!["Id"]!.GetValue<string>());
            Assert.Equal("portrait", people[1]!["PrimaryImageTag"]!.GetValue<string>());
            Assert.True(people[1]!["FutureProperty"]!["keep"]!.GetValue<bool>());
            Assert.Equal("新角色", people[1]!["Role"]!.GetValue<string>());
        });
        Test("Emby元数据：换人物名称不沿用原身份和照片", () =>
        {
            var item = Item();
            var form = EmbyMetadataEdit.Read(item);
            Assert.True((form with { People = [form.People[0] with { Name = "不同演员" }, form.People[1]] }).ApplyTo(item));
            Assert.Null(item["People"]![0]!["Id"]);
            Assert.Null(item["People"]![0]!["PrimaryImageTag"]);
            Assert.NotNull(item["People"]![0]!["FutureProperty"]);
        });
        Test("Emby元数据：人物增删和工作室更新", () =>
        {
            var item = Item();
            var form = EmbyMetadataEdit.Read(item);
            Assert.True((form with
            {
                People = [new EmbyMetadataPerson { Name = "新导演", Type = "Director" }],
                Studios = [form.Studios[0] with { Name = "新工作室" }, new EmbyMetadataStudio { Name = "另一工作室" }]
            }).ApplyTo(item));
            Assert.Equal(1, item["People"]!.AsArray().Count);
            Assert.Equal("Director", item["People"]![0]!["Type"]!.GetValue<string>());
            Assert.Null(item["Studios"]![0]!["Id"]);
            Assert.Equal(2, item["Studios"]!.AsArray().Count);
        });
        Test("Emby元数据：外部ID按原键写回清空只移除对应ID", () =>
        {
            var item = Item();
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["imdb"] = "tt999", ["Tvdb"] = "", ["FutureProvider"] = "keep" };
            Assert.True((EmbyMetadataEdit.Read(item) with { ProviderIds = ids }).ApplyTo(item));
            Assert.Equal("tt999", item["ProviderIds"]!["Imdb"]!.GetValue<string>());
            Assert.Null(item["ProviderIds"]!["imdb"]);
            Assert.Null(item["ProviderIds"]!["Tvdb"]);
            Assert.Equal("keep", item["ProviderIds"]!["FutureProvider"]!.GetValue<string>());
        });
        Test("Emby元数据：新增空白外部ID不会导致每次保存发请求", () =>
        {
            var item = Item();
            var form = EmbyMetadataEdit.Read(item);
            var ids = new Dictionary<string, string>(form.ProviderIds) { ["Tmdb"] = "" };
            Assert.False((form with { ProviderIds = ids }).ApplyTo(item));
        });
        Test("Emby元数据：锁字段、整项锁与偏好分别保存", () =>
        {
            var item = Item();
            Assert.True((EmbyMetadataEdit.Read(item) with
            {
                LockedFields = ["Overview", "FutureLock"],
                LockData = false,
                PreferredMetadataLanguage = "",
                PreferredMetadataCountryCode = "JP",
                CustomRating = "",
                Video3DFormat = "MVC"
            }).ApplyTo(item));
            Assert.False(item["LockData"]!.GetValue<bool>());
            Assert.Null(item["PreferredMetadataLanguage"]);
            Assert.Equal("JP", item["PreferredMetadataCountryCode"]!.GetValue<string>());
            Assert.Null(item["CustomRating"]);
            Assert.Equal("MVC", item["Video3DFormat"]!.GetValue<string>());
            Assert.True(item["LockedFields"]!.AsArray().Select(node => node!.GetValue<string>()).SequenceEqual(["Overview", "FutureLock"]));
            Assert.False(item.ContainsKey("IsLocked"), "LockData才是写字段，不是过滤器IsLocked");
        });
        Test("Emby元数据：空数组和缺失字段不因打开表单变更", () =>
        {
            foreach (var json in new[] { "{\"Id\":\"item\",\"Name\":\"最小条目\"}", "{\"Id\":\"item\",\"Name\":\"最小条目\",\"People\":null,\"Studios\":[],\"LockedFields\":null,\"ProviderIds\":null}" })
            {
                var item = JsonNode.Parse(json)!.AsObject();
                var before = item.ToJsonString();
                Assert.False(EmbyMetadataEdit.Read(item).ApplyTo(item));
                Assert.Equal(before, item.ToJsonString());
            }
        });
        foreach (var bad in new[] { ".", "..", "a/b", "a\\b", "a?b", "a#b", " " })
            Test($"Emby元数据：坏ID阻止网络 {bad}", () =>
            {
                using var fixture = new Fixture();
                Assert.Throws<ArgumentException>(() => Wait(fixture.Client.GetMetadataEditorInfoAsync(bad, default)));
                Assert.Equal(0, fixture.Transport.Requests);
            });
        foreach (var kind in new[] { MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
            Test($"Emby元数据：{kind}不调用Emby专属端点", () =>
            {
                using var fixture = new Fixture(kind: kind);
                Assert.Throws<NotSupportedException>(() => Wait(fixture.Client.GetMetadataEditorInfoAsync("item", default)));
                Assert.Throws<NotSupportedException>(() => Wait(fixture.Editor.LoadAsync("item", default)));
                Assert.Equal(0, fixture.Transport.Requests);
            });
        Test("Emby元数据：目录响应读取评分语言国家和外部ID", () =>
        {
            using var fixture = new Fixture();
            var info = Wait(fixture.Client.GetMetadataEditorInfoAsync("item", default));
            Assert.Equal("TV-14", info.ParentalRatingOptions[0].Name);
            Assert.Equal("zh", info.Cultures[0].TwoLetterISOLanguageName);
            Assert.Equal("CN", info.Countries[0].TwoLetterISORegionName);
            Assert.Equal("Imdb", info.ExternalIdInfos[0].Key);
            Assert.Equal(1, fixture.Transport.Requests);
        });
        Test("Emby元数据服务：未修改保存完全不发POST", () =>
        {
            using var fixture = new Fixture();
            var document = Wait(fixture.Editor.LoadAsync("item", default));
            Assert.False(Wait(fixture.Editor.SaveAsync(document, document.Edit, default)));
            Assert.Equal(0, fixture.Transport.Writes);
            Assert.Equal(2, fixture.Transport.Requests);
        });
        Test("Emby元数据服务：保存整份JSON且确认后更新原稿", () =>
        {
            using var fixture = new Fixture();
            var document = Wait(fixture.Editor.LoadAsync("item", default));
            var edit = document.Edit;
            edit = edit with { Basic = edit.Basic with { Name = "新名字" } };
            Assert.True(Wait(fixture.Editor.SaveAsync(document, edit, default)));
            Assert.Equal("新名字", fixture.Transport.Submitted!["Name"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(Item()["UnknownMetadata"], fixture.Transport.Submitted["UnknownMetadata"]));
            Assert.Equal("新名字", document.Edit.Basic.Name);
            Assert.False(Wait(fixture.Editor.SaveAsync(document, edit, default)));
            Assert.Equal(1, fixture.Transport.Writes);
        });
        Test("Emby元数据服务：保存失败原稿和用户草稿可重试", () =>
        {
            using var fixture = new Fixture();
            var document = Wait(fixture.Editor.LoadAsync("item", default));
            var edit = document.Edit with { CustomRating = "PG-13" };
            fixture.Transport.FailWrite = true;
            Assert.Throws<EmbyApiException>(() => Wait(fixture.Editor.SaveAsync(document, edit, default)));
            Assert.Equal("TV-14", document.Edit.CustomRating);
            Assert.Equal("PG-13", edit.CustomRating);
            fixture.Transport.FailWrite = false;
            Assert.True(Wait(fixture.Editor.SaveAsync(document, edit, default)));
            Assert.Equal("PG-13", document.Edit.CustomRating);
        });
        Test("Emby元数据服务：明确不是管理员时不允许读取或写入", () =>
        {
            using var fixture = new Fixture(admin: false);
            Assert.Throws<UnauthorizedAccessException>(() => Wait(fixture.Editor.LoadAsync("item", default)));
            Assert.Equal(0, fixture.Transport.Requests);
        });
        // Emby 的登录结果与令牌恢复都不写用户策略，连接快照里的管理员标记常态是 null。2026-10-29 的
        // 「点编辑元数据就说没有权限」正是把它当成了「不是管理员」—— 管理员账号也打不开这张表。
        Test("Emby元数据服务：权限未知（Emby 常态）照常读取和保存", () =>
        {
            using var fixture = new Fixture(admin: null);
            var document = Wait(fixture.Editor.LoadAsync("item", default));
            Assert.Equal(2, fixture.Transport.Requests);
            var edit = document.Edit with { Basic = document.Edit.Basic with { Name = "未知权限也能改" } };
            Assert.True(Wait(fixture.Editor.SaveAsync(document, edit, default)));
            Assert.Equal(1, fixture.Transport.Writes);
            Assert.Equal("未知权限也能改", fixture.Transport.Submitted!["Name"]!.GetValue<string>());
        });
        Test("Emby元数据服务：打开后权限撤销不允许保存", () =>
        {
            using var fixture = new Fixture();
            var document = Wait(fixture.Editor.LoadAsync("item", default));
            fixture.Scope.IdentityState.Client = new EmbyClient(fixture.Http, fixture.Client.Connection with { IsAdministrator = false });
            Assert.Throws<UnauthorizedAccessException>(() => Wait(fixture.Editor.SaveAsync(document, document.Edit with { CustomRating = "PG" }, default)));
            Assert.Equal(0, fixture.Transport.Writes);
        });
        Test("Emby元数据服务：服务器返回另一条目不进入编辑", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.Original["Id"] = "different";
            Assert.Throws<InvalidOperationException>(() => Wait(fixture.Editor.LoadAsync("item", default)));
            Assert.Equal(1, fixture.Transport.Requests);
        });
        Test("Emby元数据服务：跨账号文档不允许提交", () =>
        {
            using var a = new Fixture();
            using var b = new Fixture();
            var document = Wait(a.Editor.LoadAsync("item", default));
            Assert.Throws<InvalidOperationException>(() => Wait(b.Editor.SaveAsync(document, document.Edit, default)));
            Assert.Equal(0, b.Transport.Requests);
        });
        Test("Emby元数据服务：取消或账号退出不发送保存", () =>
        {
            using var fixture = new Fixture();
            var document = Wait(fixture.Editor.LoadAsync("item", default));
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => Wait(fixture.Editor.SaveAsync(document, document.Edit with { CustomRating = "PG" }, cancel.Token)));
            fixture.Scope.IdentityState.Lifetime.Cancel();
            Assert.Throws<OperationCanceledException>(() => Wait(fixture.Editor.SaveAsync(document, document.Edit with { CustomRating = "PG" }, default)));
            Assert.Equal(0, fixture.Transport.Writes);
        });
        foreach (var format in new[] { "file:///C:/bad", "javascript:bad", "https://user:password@example.test/{0}", "https://example.test/{99}", "https://example.test/{" })
            Test($"Emby外部ID：拒绝不安全或损坏格式 {format}", () =>
                Assert.Null(EmbyMetadataLink.Create(new ExternalIdInfo { UrlFormatString = format }, "tt1")));
        Test("Emby外部ID：合法ID被转义且空值不打开", () =>
        {
            var info = new ExternalIdInfo { UrlFormatString = "https://example.test/title/{0}" };
            Assert.Equal("https://example.test/title/a%2Fb%3Fc", EmbyMetadataLink.Create(info, "a/b?c")!.AbsoluteUri);
            Assert.Null(EmbyMetadataLink.Create(info, " "));
        });
    }

    private static T Wait<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
    internal static JsonObject Item() => JsonNode.Parse("""
        {
          "Id":"item", "Type":"Episode", "Name":"原标题", "Overview":"原概要", "Path":"/offline/Season 01/E04.mkv",
          "DateCreated":"2026-10-28T09:33:45.1234567Z", "PremiereDate":"2026-10-28T00:00:00.1234567+09:00",
          "IndexNumber":4, "ParentIndexNumber":1, "ProductionYear":2026, "CommunityRating":8.4,
          "OfficialRating":"TV-14", "CustomRating":"TV-14", "Video3DFormat":"HalfSideBySide",
          "PreferredMetadataLanguage":"zh", "PreferredMetadataCountryCode":"CN", "LockData":true,
          "LockedFields":["Name","Cast","FutureLock"], "Genres":["动画,奇幻","剧情"], "Tags":["一个、完整标签"],
          "ProviderIds":{"Imdb":"tt50122563","Tvdb":"12022342","FutureProvider":"keep"},
          "People":[{"Id":"person-1","Name":"演员一","Role":"角色, (voice)","Type":"Actor","PrimaryImageTag":"portrait","FutureProperty":{"keep":true}},
                    {"Id":"person-2","Name":"演员二","Role":"另一个角色","Type":"Actor"}],
          "Studios":[{"Id":1234,"Name":"公司, A","Future":1}],
          "UnknownMetadata":{"keep":[null,1,"原文"]}, "MediaSources":[{"Id":"source","Path":"/offline/E04.mkv"}]
        }
        """)!.AsObject();

    private sealed class Fixture : IDisposable
    {
        public Transport Transport { get; } = new();
        public EmbySession Session { get; }
        public EmbyHttp Http { get; }
        public EmbyClient Client { get; }
        public EmbySessionScope Scope { get; }
        public EmbyMetadataEditor Editor { get; }
        public Fixture(MediaServerKind kind = MediaServerKind.Emby, bool? admin = true)
        {
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var connection = new EmbyConnection(new Uri("https://metadata-editor.invalid/emby/"), "placeholder-only", "fixture", "fixture", "fixture", device)
            { ServerKind = kind, IsAdministrator = admin };
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "metadata-editor-config|IO-forbidden"));
            Session = new EmbySession(new AppSettings(), new SettingsStore(paths, new NoSecrets()), new CredentialVault(new NoSecrets()), device, Transport);
            Http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
            Client = new EmbyClient(Http, connection);
            var identity = new EmbySessionScope.Identity(Client, new ServerProfile { Id = "fixture", Url = "https://metadata-editor.invalid/emby/", ServerKind = kind },
                new AccountProfile { Id = "fixture", Username = "fixture" });
            Scope = new EmbySessionScope(Session, identity);
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
            sources.Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Scope);
            Editor = new EmbyMetadataEditor(Scope);
        }
        public void Dispose() => Session.Dispose();
    }

    private sealed class Transport : HttpMessageHandler
    {
        public JsonObject Original { get; } = Item();
        public int Requests { get; private set; }
        public int Writes { get; private set; }
        public bool FailWrite { get; set; }
        public JsonObject? Submitted { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("metadata-editor.invalid", request.RequestUri!.Host);
            Assert.False(request.RequestUri.Query.Contains("api_key", StringComparison.OrdinalIgnoreCase));
            Requests++;
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/emby/Users/fixture/Items/item")
                return Json(Original.ToJsonString());
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/emby/Items/item/MetadataEditor")
                return Json("""
                    {"ParentalRatingOptions":[{"Name":"TV-14","Value":14}],
                     "Cultures":[{"Name":"Chinese","DisplayName":"中文","TwoLetterISOLanguageName":"zh"}],
                     "Countries":[{"Name":"China","DisplayName":"中国","TwoLetterISORegionName":"CN"}],
                     "ExternalIdInfos":[{"Name":"IMDb","Key":"Imdb","UrlFormatString":"https://example.test/title/{0}"}]}
                    """);
            if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/emby/Items/item")
            {
                Writes++;
                Submitted = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                return new HttpResponseMessage(FailWrite ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent)
                { Content = new StringContent("") };
            }
            throw new AssertionException("未授权的匿名HTTP路径：" + request.Method + " " + request.RequestUri.AbsolutePath);
        }
        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
        { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("不允许凭据或配置IO");
        public string Unprotect(string cipherText) => throw new AssertionException("不允许凭据或配置IO");
    }
}
