using System.Net;
using System.Reflection;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Configuration;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexMediaTests
{
    private static void RegisterLibraryCatalog()
    {
        Test("Plex 关联与库筛选：两类查询不能互相覆盖并扩大范围", () =>
        {
            using var fixture = new CatalogFixture();
            foreach (var withFilters in new[] { true, false })
            {
                var query = new ItemQuery
                {
                    ParentId = "1",
                    PlexRelation = new PlexBrowseRelation("1", 1, "actor", "41"),
                    PlexFilters = withFilters ? new PlexFilterSelection { Type = "1" } : null,
                    PlexSortKey = withFilters ? null : "titleSort"
                };
                Assert.Throws<NotSupportedException>(() => fixture.Adapter.GetItemsAsync(query, default).GetAwaiter().GetResult());
            }
            Assert.Equal(0, fixture.Transport.Requests.Count);
        });
        Test("Plex 浏览设置：恢复偏好保留库选择，null输入归一化且不丢未知条件", () =>
        {
            var settings = new AppSettings();
            var selection = Selection("""{"Type":"1","Values":{"genre":["7"]}}""");
            var property = settings.Ui.GetType().GetProperty("PlexFilters");
            Assert.NotNull(property);
            var map = (System.Collections.IDictionary)property!.GetValue(settings.Ui)!;
            map.Add("source:section", selection);
            SettingsPreferences.Apply(settings, new AppSettings());
            Assert.True(ReferenceEquals(map, property.GetValue(settings.Ui)));
            var damaged = SettingsMigration.FromJson("""{"SchemaVersion":26,"Ui":{"PlexFilters":{"library":{"Type":null,"Values":null}}}}""", PassthroughSecretProtector.Instance);
            Assert.Equal("", Read<string>(((System.Collections.IDictionary)property.GetValue(damaged.Ui)!)["library"]!, "Type"));
            Assert.NotNull(Read<object>(((System.Collections.IDictionary)property.GetValue(damaged.Ui)!)["library"]!, "Values"));
        });
        Test("Plex 浏览目录：旧请求不能覆盖刷新得到的新目录", () =>
        {
            using var f = new CatalogFixture();
            f.Transport.DelayFirstDetails = true;
            var old = f.CatalogTask("1");
            f.Transport.Entered.Task.GetAwaiter().GetResult();
            f.Transport.Empty = true;
            var current = f.Catalog("1", true);
            f.Transport.Release.TrySetResult();
            old.GetAwaiter().GetResult();
            Assert.Equal(0, Many(f.Catalog("1"), "Types").Count);
        });
        Test("Plex 浏览目录：库和媒体类型独立，未知字段不出现在面板", () =>
        {
            using var f = new CatalogFixture();
            var catalog = f.Catalog("1");
            Assert.Equal("1", Read<string>(catalog, "SectionId"));
            var type = Many(catalog, "Types").Single();
            Assert.Equal("1", Read<string>(type, "Id"));
            Assert.Equal("genre,year,unwatched,resolution", string.Join(',', Many(type, "Filters").Select(x => Read<string>(x, "Field"))));
            Assert.Equal("2", Read<string>(Many(f.Catalog("2"), "Types").Single(), "Id"));
            Assert.Contains("includeDetails=1", f.Transport.Requests.First(x => x.AbsolutePath.EndsWith("/1", StringComparison.Ordinal)).Query);
            Assert.Contains("includeAdvanced=1", f.Transport.Requests.First(x => x.AbsolutePath.EndsWith("/1", StringComparison.Ordinal)).Query);
        });
        Test("Plex 浏览目录：无服务端声明不伪造筛选，缺目录返回空", () =>
        {
            using var f = new CatalogFixture();
            f.Transport.Enabled = false;
            Assert.Equal(0, Many(Many(f.Catalog("1"), "Types").Single(), "Filters").Count);
            f.Transport.Empty = true;
            Assert.Equal(0, Many(f.Catalog("3"), "Types").Count);
        });
        Test("Plex 浏览目录：失败可重试，刷新后读取新的服务端目录", () =>
        {
            using var f = new CatalogFixture();
            f.Transport.Fail = true;
            Assert.Throws<PlexApiException>(() => f.Catalog("1"));
            f.Transport.Fail = false;
            Assert.Equal(1, Many(f.Catalog("1"), "Types").Count);
        });
        Test("Plex 浏览条件：多字段组合、原生选项值编码和分页头", () =>
        {
            using var f = new CatalogFixture();
            var selection = Selection("""{"Type":"1","Values":{"genre":["7"],"year":["2025"],"unwatched":["0"],"resolution":["4k"]}}""");
            var query = new ItemQuery { ParentId = "1", StartIndex = 12, Limit = 7 };
            SetQuery(query, selection, "addedAt");
            f.Adapter.GetItemsAsync(query, default).GetAwaiter().GetResult();
            var sent = f.Transport.Requests.Last();
            Assert.Equal("/library/sections/1/all", sent.AbsolutePath);
            Assert.Contains("genre=7", sent.Query);
            Assert.Contains("year=2025", sent.Query);
            Assert.Contains("unwatched=0", sent.Query);
            Assert.Contains("resolution=4k", sent.Query);
            Assert.Contains("sort=addedAt", sent.Query);
            Assert.Equal("12", f.Transport.LastHeaders["X-Plex-Container-Start"]);
            Assert.Equal("7", f.Transport.LastHeaders["X-Plex-Container-Size"]);
        });
        Test("Plex 浏览条件：未声明字段、失效值、跨库类型拒绝而不放宽", () =>
        {
            foreach (var json in new[]
            {
                """{"Type":"1","Values":{"privateField":["1"]}}""",
                """{"Type":"1","Values":{"genre":["missing"]}}""",
                """{"Type":"2","Values":{}}"""
            })
            {
                using var f = new CatalogFixture();
                var q = new ItemQuery { ParentId = "1" };
                SetQuery(q, Selection(json), "titleSort");
                Assert.Throws<InvalidOperationException>(() => f.Adapter.GetItemsAsync(q, default).GetAwaiter().GetResult());
                Assert.False(f.Transport.Requests.Any(x => x.AbsolutePath.EndsWith("/all", StringComparison.Ordinal)));
            }
        });
        Test("Plex 浏览排序：降序只使用服务端 descKey，旧键拒绝", () =>
        {
            using var f = new CatalogFixture();
            var q = new ItemQuery { ParentId = "1", Descending = true };
            SetQuery(q, Selection("""{"Type":"1","Values":{}}"""), "titleSort");
            f.Adapter.GetItemsAsync(q, default).GetAwaiter().GetResult();
            Assert.Contains("sort=titleSort%3Adesc", f.Transport.Requests.Last().Query);
            var invalid = new ItemQuery { ParentId = "1" };
            SetQuery(invalid, Selection("""{"Type":"1","Values":{}}"""), "not-declared");
            Assert.Throws<InvalidOperationException>(() => f.Adapter.GetItemsAsync(invalid, default).GetAwaiter().GetResult());
        });
        Test("Plex 浏览选项：标签与值分开，特殊字符不变成查询参数", () =>
        {
            using var f = new CatalogFixture();
            var options = f.Options("1", "1", "genre", Selection("""{"Type":"1","Values":{"year":["2025"]}}"""));
            Assert.Equal("7", Read<string>(options[0], "Value"));
            Assert.Equal("动画", Read<string>(options[0], "Title"));
            Assert.Equal("8&token=bad", Read<string>(options[1], "Value"));
            var q = new ItemQuery { ParentId = "1" };
            SetQuery(q, Selection("""{"Type":"1","Values":{"genre":["8&token=bad"]}}"""), "titleSort");
            f.Adapter.GetItemsAsync(q, default).GetAwaiter().GetResult();
            Assert.Contains("genre=8%26token%3Dbad", f.Transport.Requests.Last().Query);
            Assert.DoesNotContain("&token=", f.Transport.Requests.Last().Query);
        });
        Test("Plex 字母索引：同条件分组计数累加，坏计数和空表不生成目标", () =>
        {
            using var f = new CatalogFixture();
            var rows = f.Alphabet("1", Selection("""{"Type":"1","Values":{"genre":["7"]}}"""));
            Assert.Equal("0,3,7", string.Join(',', rows.Select(x => Read<int>(x, "Index"))));
            Assert.Equal("A,B,#", string.Join(',', rows.Select(x => Read<string>(x, "Key"))));
            Assert.Contains("genre=7", f.Transport.Requests.Last().Query);
            Assert.Contains("type=1", f.Transport.Requests.Last().Query);
            f.Transport.EmptyAlphabet = true;
            Assert.Equal(0, f.Alphabet("1", Selection("""{"Type":"1","Values":{}}""")).Count);
        });
    }

    private static object Selection(string json)
    {
        var type = typeof(PlexMediaAdapter).Assembly.GetType("Momoka.Plex.PlexFilterSelection");
        Assert.NotNull(type, "Plex 独立选择模型必须存在");
        return JsonSerializer.Deserialize(json, type!)!;
    }

    private static void SetQuery(ItemQuery query, object selection, string sort)
    {
        var property = typeof(ItemQuery).GetProperty("PlexFilters");
        Assert.NotNull(property, "查询须携带 Plex 原生筛选");
        property!.SetValue(query, selection);
        typeof(ItemQuery).GetProperty("PlexSortKey")!.SetValue(query, sort);
    }

    private static T Read<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
    private static List<object> Many(object value, string name) => ((System.Collections.IEnumerable)Read<object>(value, name)).Cast<object>().ToList();

    private sealed class CatalogFixture : IDisposable
    {
        public CatalogTransport Transport { get; } = new();
        private readonly PlexHttp _http;
        public PlexMediaAdapter Adapter { get; }
        public CatalogFixture()
        {
            _http = new PlexHttp(Transport, false);
            Adapter = new PlexMediaAdapter(new PlexClient(_http), new PlexConnection(new Uri("https://catalog.invalid/"), "placeholder-only"));
        }
        public object Catalog(string section, bool refresh = false) => Call("GetLibraryCatalogAsync", section, default(CancellationToken), refresh);
        public Task CatalogTask(string section) => (Task)typeof(PlexMediaAdapter).GetMethod("GetLibraryCatalogAsync")!.Invoke(Adapter, [section, default(CancellationToken), false])!;
        public List<object> Options(string section, string type, string field, object selection) =>
            ((System.Collections.IEnumerable)Call("GetLibraryFilterOptionsAsync", section, type, field, selection, default(CancellationToken))).Cast<object>().ToList();
        public List<object> Alphabet(string section, object selection) =>
            ((System.Collections.IEnumerable)Call("GetLibraryFirstCharactersAsync", section, selection, default(CancellationToken))).Cast<object>().ToList();
        private object Call(string name, params object[] args)
        {
            var method = typeof(PlexMediaAdapter).GetMethod(name);
            Assert.NotNull(method, "必须实现 " + name);
            try
            {
                var task = (Task)method!.Invoke(Adapter, args)!;
                task.GetAwaiter().GetResult();
                return task.GetType().GetProperty("Result")!.GetValue(task)!;
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
        public void Dispose() { _http.Dispose(); Transport.Dispose(); }
    }

    private sealed class CatalogTransport : HttpMessageHandler
    {
        public bool Enabled { get; set; } = true;
        public bool Empty { get; set; }
        public bool Fail { get; set; }
        public bool EmptyAlphabet { get; set; }
        public bool DelayFirstDetails { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _details;
        public List<Uri> Requests { get; } = [];
        public Dictionary<string, string> LastHeaders { get; private set; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Requests.Add(uri);
            LastHeaders = request.Headers.ToDictionary(x => x.Key, x => string.Join(',', x.Value));
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var section = uri.AbsolutePath.Split('/').ElementAtOrDefault(3) ?? "1";
            var type = section == "2" ? "show" : "movie";
            var id = section == "2" ? "2" : "1";
            var json = uri.AbsolutePath switch
            {
                "/library/sections" => $$"""{"Directory":[{"key":"1","type":"movie","title":"电影","filters":{{Enabled.ToString().ToLowerInvariant()}}},{"key":"2","type":"show","title":"剧","filters":true},{"key":"3","type":"movie","title":"空","filters":true}]}""",
                var path when path.EndsWith("/genre", StringComparison.Ordinal) => """{"Directory":[{"key":"7","title":"动画"},{"key":"8&token=bad","title":"剧情"}]}""",
                var path when path.EndsWith("/year", StringComparison.Ordinal) => """{"Directory":[{"key":"2025","title":"2025"}]}""",
                var path when path.EndsWith("/resolution", StringComparison.Ordinal) => """{"Directory":[{"key":"4k","title":"4K"},{"key":"1080","title":"1080p"}]}""",
                var path when path.EndsWith("/firstCharacter", StringComparison.Ordinal) => EmptyAlphabet ? """{"Directory":[]}""" : """{"Directory":[{"key":"A","title":"A","size":3},{"key":"B","title":"B","size":"4"},{"key":"#","title":"#","size":2},{"key":"invalid","title":"bad","size":-1}]}""",
                var path when path.EndsWith("/all", StringComparison.Ordinal) => """{"Metadata":[],"totalSize":0}""",
                _ => Empty ? """{"Type":[]}""" : $$"""{"librarySectionID":"{{section}}","Type":[{"type":"{{type}}","key":"/library/sections/{{section}}/all?type={{id}}","title":"媒体","Filter":[{"filter":"genre","filterType":"string","key":"/library/sections/{{section}}/genre?type={{id}}","title":"类型"},{"filter":"year","filterType":"integer","key":"/library/sections/{{section}}/year?type={{id}}","title":"年份"},{"filter":"unwatched","filterType":"boolean","title":"已看状态"},{"filter":"resolution","filterType":"string","key":"/library/sections/{{section}}/resolution?type={{id}}","title":"分辨率"},{"filter":"privateField","filterType":"string","title":"未知"}],"Sort":[{"key":"titleSort","descKey":"titleSort:desc","title":"标题","default":"asc","firstCharacterKey":"firstCharacter"},{"key":"addedAt","descKey":"addedAt:desc","title":"添加时间","default":"desc"}]}]}"""
            };
            if (uri.AbsolutePath == "/library/sections/1" && DelayFirstDetails && Interlocked.Increment(ref _details) == 1)
            { Entered.TrySetResult(); await Release.Task; }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"MediaContainer\":" + json + "}") };
        }
    }
}
