using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// The date-only editor must not rewrite a valid, untouched timestamp just because its UTC day is
/// different. Executes the production JSON rules and client against an in-memory HTTP boundary; the
/// conditional save driver here is not the real WinUI dialog or an observation of a deployed server.
/// </summary>
internal static class ItemMetadataDateTests
{
    private static readonly string[] Timestamps =
    [
        "2016-08-26T00:00:00+09:00",
        "2016-08-26T23:30:00.1234567-09:00",
        "2016-08-26T15:00:00.1234567+09:00",
        "2016-08-26T01:00:00-09:00",
        "2016-08-26T16:00:00.0000000Z",
        "2016-08-26T16:00:00+00:00",
        "2024-02-29T00:01:00+14:00",
        "1999-12-31T23:59:59.1234567-12:00"
    ];

    private static readonly string[] EquivalentFormats =
        ["yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d", "yyyyMMdd", "yyyy年M月d日"];

    public static void Register()
    {
        foreach (var timestamp in Timestamps)
        {
            Test($"元数据日期：{timestamp} 原样保存保留整份JSON", () =>
            {
                var item = Item(timestamp);
                var before = item.ToJsonString();
                var form = ItemMetadataEdit.Read(item);
                Assert.Equal(CalendarDay(timestamp), form.PremiereDate);
                Assert.Null(form.Problem);
                Assert.False(form.ApplyTo(item), "比较的是日期框所显示的日历日，不是UTC日");
                Assert.Equal(before, item.ToJsonString());
            });

            Test($"元数据日期：{timestamp} 只改名称不动日期和未知字段", () =>
            {
                var item = Item(timestamp);
                var expected = item.DeepClone().AsObject();
                expected["Name"] = "新的名称";
                Assert.True((ItemMetadataEdit.Read(item) with { Name = "新的名称" }).ApplyTo(item));
                Assert.Equal(expected.ToJsonString(), item.ToJsonString());
                Assert.False(ItemMetadataEdit.Read(item).ApplyTo(item), "保存结果再原样读回不应再改");
            });

            foreach (var format in EquivalentFormats)
            {
                Test($"元数据日期：{timestamp} 同日格式{format}不重写", () =>
                {
                    var item = Item(timestamp);
                    var before = item.ToJsonString();
                    var text = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture)
                        .ToString(format, CultureInfo.InvariantCulture);
                    var form = ItemMetadataEdit.Read(item) with { PremiereDate = $" {text} " };
                    Assert.Null(form.Problem);
                    Assert.False(form.ApplyTo(item));
                    Assert.Equal(before, item.ToJsonString());
                });
            }

            Test($"元数据日期：{timestamp} 实际匿名GET后原样表单不发POST", () =>
            {
                using var transport = new MetadataTransport(Item(timestamp));
                using var http = new EmbyHttp(transport);
                var client = Client(http);
                var item = client.GetItemJsonAsync("subject", CancellationToken.None).GetAwaiter().GetResult();
                if (ItemMetadataEdit.Read(item).ApplyTo(item))
                    client.UpdateItemAsync("subject", item, CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(1, transport.Reads);
                Assert.Equal(0, transport.Writes);
                Assert.Null(transport.Submitted);
            });

            Test($"元数据日期：{timestamp} 实际名称POST只改名称", () =>
            {
                var expected = Item(timestamp);
                expected["Name"] = "新的名称";
                using var transport = new MetadataTransport(Item(timestamp));
                using var http = new EmbyHttp(transport);
                var client = Client(http);
                var item = client.GetItemJsonAsync("subject", CancellationToken.None).GetAwaiter().GetResult();
                var form = ItemMetadataEdit.Read(item) with { Name = "新的名称" };
                Assert.True(form.ApplyTo(item));
                client.UpdateItemAsync("subject", item, CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(1, transport.Reads);
                Assert.Equal(1, transport.Writes);
                Assert.Equal(expected.ToJsonString(), transport.Submitted!.ToJsonString());
            });
        }

        foreach (var timestamp in new[] { Timestamps[0], Timestamps[1], Timestamps[6], Timestamps[7] })
        {
            Test($"元数据日期：{timestamp} 主动改到原UTC日不能被旧同日判断吞掉", () =>
            {
                var item = Item(timestamp);
                var utcDay = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture).UtcDateTime
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Assert.False(CalendarDay(timestamp) == utcDay, "本反例必须跨UTC日");
                var form = ItemMetadataEdit.Read(item) with { PremiereDate = utcDay };
                Assert.True(form.ApplyTo(item));
                Assert.Equal($"{utcDay}T00:00:00.0000000Z", item["PremiereDate"]!.GetValue<string>());
                Assert.False(ItemMetadataEdit.Read(item).ApplyTo(item));
            });
        }

        Test("元数据日期：真改日期仍按原有UTC午夜策略写入一次", () =>
        {
            using var transport = new MetadataTransport(Item(Timestamps[0]));
            using var http = new EmbyHttp(transport);
            var client = Client(http);
            var item = client.GetItemJsonAsync("subject", CancellationToken.None).GetAwaiter().GetResult();
            var form = ItemMetadataEdit.Read(item) with { PremiereDate = "2016年12月2日" };
            Assert.True(form.ApplyTo(item));
            client.UpdateItemAsync("subject", item, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, transport.Writes);
            Assert.Equal("2016-12-02T00:00:00.0000000Z", transport.Submitted!["PremiereDate"]!.GetValue<string>());
            Assert.Equal(item["People"]!.ToJsonString(), transport.Submitted["People"]!.ToJsonString());
        });

        foreach (var input in new[] { "", "  " })
        {
            Test($"元数据日期：清空长度{input.Length}仍写null且再次保存无改动", () =>
            {
                var item = Item(Timestamps[0]);
                Assert.True((ItemMetadataEdit.Read(item) with { PremiereDate = input }).ApplyTo(item));
                Assert.True(item.ContainsKey("PremiereDate"));
                Assert.Null(item["PremiereDate"]);
                Assert.False(ItemMetadataEdit.Read(item).ApplyTo(item));
            });
        }

        foreach (var present in new[] { false, true })
        {
            Test($"元数据日期：原日期{(present ? "null" : "缺失")}原样保存不补字段", () =>
            {
                var item = Item(Timestamps[0]);
                if (present) item["PremiereDate"] = null;
                else item.Remove("PremiereDate");
                var before = item.ToJsonString();
                Assert.Equal("", ItemMetadataEdit.Read(item).PremiereDate);
                Assert.False(ItemMetadataEdit.Read(item).ApplyTo(item));
                Assert.Equal(before, item.ToJsonString());
            });
        }

        foreach (var input in new[] { "去年夏天", "2024-02-30", "2023-02-29", "2024-13-01", "0000-01-01" })
        {
            Test($"元数据日期：坏输入{input}拒绝且不先改其它字段", () =>
            {
                var item = Item(Timestamps[0]);
                var before = item.ToJsonString();
                var form = ItemMetadataEdit.Read(item) with { Name = "不能先写入", PremiereDate = input };
                Assert.NotNull(form.Problem);
                Assert.Throws<InvalidOperationException>(() => form.ApplyTo(item));
                Assert.Equal(before, item.ToJsonString());
            });
        }

        foreach (var culture in new[] { "zh-CN", "en-US" })
        {
            foreach (var (input, expectedDate) in new[]
            {
                ("2016-08-26T23:30:00-09:00", (string?)null),
                ("2016-08-26T16:00:00Z", (string?)null),
                ("2016-08-25T00:00:00Z", "2016-08-25T00:00:00.0000000Z"),
                ("2016-08-27T00:00:00+09:00", "2016-08-26T15:00:00.0000000Z")
            })
            {
                Test($"元数据日期：{culture}宽松偏移输入{input}按输入日历日判断", () =>
                {
                    var previous = CultureInfo.CurrentCulture;
                    try
                    {
                        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                        var item = Item(Timestamps[0]);
                        var before = item.ToJsonString();
                        var form = ItemMetadataEdit.Read(item) with { PremiereDate = input };
                        Assert.Null(form.Problem, "显式时刻/偏移沿用已有宽松输入支持");
                        Assert.Equal(expectedDate is not null, form.ApplyTo(item));
                        if (expectedDate is null) Assert.Equal(before, item.ToJsonString());
                        else Assert.Equal(expectedDate, item["PremiereDate"]!.GetValue<string>());
                    }
                    finally { CultureInfo.CurrentCulture = previous; }
                });
            }
        }

        foreach (var culture in new[] { "zh-CN", "en-US" })
        {
            Test($"元数据日期：{culture}下偏移同日仍不重写", () =>
            {
                var previous = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    var item = Item(Timestamps[0]);
                    var before = item.ToJsonString();
                    Assert.False((ItemMetadataEdit.Read(item) with { PremiereDate = "2016/8/26" }).ApplyTo(item));
                    Assert.Equal(before, item.ToJsonString());
                }
                finally { CultureInfo.CurrentCulture = previous; }
            });
        }
    }

    private static string CalendarDay(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static JsonObject Item(string timestamp)
    {
        var item = JsonNode.Parse("""
            {
              "Id": "subject",
              "Name": "原名称",
              "EndDate": "2030-12-31T23:59:59-09:00",
              "People": [{"Name":"演员", "Type":"Actor", "Unknown":true}],
              "Studios": [{"Name":"公司", "Id":"studio"}],
              "UnknownMetadata": {"Date":"1999-12-31T00:00:00+09:00", "Values":[null,1,"原文"]}
            }
            """)!.AsObject();
        item["PremiereDate"] = timestamp;
        return item;
    }

    private static EmbyClient Client(EmbyHttp http) => new(http, new EmbyConnection(
        new Uri("https://metadata-date.example.test/emby/"), "", "anonymous", "anonymous", "fixture",
        DeviceIdentity.Create("metadata-date-fixture", "1")));

    private sealed class MetadataTransport(JsonObject original) : HttpMessageHandler
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public JsonObject? Submitted { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("metadata-date.example.test", request.RequestUri!.Host);
            Assert.True(string.IsNullOrEmpty(request.RequestUri.Query));
            Assert.False(request.Headers.Contains("X-Emby-Token"), "夹具不提供令牌或访问凭据");
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/emby/Users/anonymous/Items/subject")
            {
                Reads++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(original.ToJsonString(), Encoding.UTF8, "application/json")
                };
            }
            if (request.Method == HttpMethod.Post && path == "/emby/Items/subject")
            {
                Writes++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Submitted = JsonNode.Parse(body)!.AsObject();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            throw new InvalidOperationException($"未预期的匿名夹具请求：{request.Method} {path}");
        }
    }
}
