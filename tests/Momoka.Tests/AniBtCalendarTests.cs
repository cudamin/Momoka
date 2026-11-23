using System.Net;
using System.Text;
using Momoka.AniBt;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AniBtCalendarTests
{
    private static readonly DateOnly Start = new(2026, 12, 28);
    private static readonly DateOnly End = Start.AddDays(42);
    private const string CoverUrl = "https://r2.anibt.net/anime_covers/calendar-fixture/test.jpg";

    internal static void Register()
    {
        Test("AniBT 日历读取跨年季度，仅展示精确日期，合并同日首播与放送", () =>
        {
            var airing = new DateTimeOffset(2026, 12, 31, 17, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var endAiring = new DateTimeOffset(2027, 2, 7, 17, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            var local = TimeZoneInfo.CreateCustomTimeZone("CalendarFixture", TimeSpan.FromHours(8), "CalendarFixture", "CalendarFixture");
            var winter = $$"""
                {"animeId":"shared","bgmId":null,"title":{"primary":"同日首播"},"kind":"MOVIE","format":null,
                 "cover":"{{CoverUrl}}","premiereDate":"2027-01-01","premierePrecision":"day","scheduleStatus":"weekday_only"},
                {"animeId":"other","bgmId":201,"title":{"primary":"同日首播"},"kind":"TV",
                 "premiereDate":"2027-01-01","premierePrecision":"day"},
                {"animeId":"rough","title":{"primary":"仅到月及星期"},"premiereDate":"2027-01-01","premierePrecision":"month",
                 "scheduleStatus":"weekday_only","airingAt":{{airing}},"weekday":5},
                {"animeId":"end-outside","title":{"primary":"窗口右端不纳入"},"premiereDate":"2027-02-08","premierePrecision":"day",
                 "scheduleStatus":"scheduled","airingAt":{{endAiring}}},
                {"animeId":"milliseconds","title":{"primary":"单位与时刻不合法"},"scheduleStatus":"scheduled","airingAt":{{airing}}000}
                """;
            var fall = $$"""
                {"animeId":"shared","bgmId":null,"title":{"primary":"同日首播"},"kind":"MOVIE","format":null,
                 "scheduleStatus":"scheduled","airingAt":{{airing}}},
                {"animeId":"before","title":{"primary":"窗口前不纳入"},"premiereDate":"2026-12-27","premierePrecision":"day"},
                {"animeId":"at-start","title":{"primary":"窗口左端纳入"},"premiereDate":"2026-12-28","premierePrecision":"day"},
                {"animeId":"floating","title":{"primary":"分别保留两个日期"},"premiereDate":"2026-12-31","premierePrecision":"day",
                 "scheduleStatus":"scheduled","airingAt":{{airing}}}
                """;
            using var transport = new Transport(uri => uri.Query.EndsWith("2026-FALL", StringComparison.Ordinal)
                ? Season(fall) : Season(winter, truncated: true));
            using var client = new AniBtClient(transport);
            var result = client.CalendarAsync(Start, End, local, default).GetAwaiter().GetResult();

            Assert.True(result.Truncated);
            Assert.False(result.PartialFailure);
            Assert.Equal(2, transport.Requests.Count);
            Assert.Equal("https://anibt.net/api/seasons/anime?season=2026-FALL", transport.Requests[0]);
            Assert.Equal("https://anibt.net/api/seasons/anime?season=2027-WINTER", transport.Requests[1]);
            Assert.Equal(5, result.Entries.Count);
            Assert.Equal(Start, result.Entries[0].Date);
            var merged = result.Entries.Single(entry => entry.AnimeId == "shared");
            Assert.True(merged.IsPremiere);
            Assert.True(merged.IsMovie);
            Assert.Null(merged.BgmId);
            Assert.Equal(CoverUrl, merged.ImageUrl);
            Assert.Equal(new DateOnly(2027, 1, 1), merged.Date);
            Assert.Equal(1, merged.AiringAt!.Value.Hour);
            Assert.Equal(TimeSpan.FromHours(8), merged.AiringAt.Value.Offset);
            Assert.Equal(2, result.Entries.Count(entry => entry.AnimeId == "floating"));
            Assert.Equal(2, result.Entries.Count(entry => entry.Title == "同日首播"));
        });

        Test("AniBT 日历部分失败保留可读季度，全部失败报告错误", () =>
        {
            const string anime = """
                {"animeId":"readable","title":{"primary":"可读季度"},"premiereDate":"2027-01-01","premierePrecision":"day"}
                """;
            using var partial = new Transport(uri => uri.Query.EndsWith("2026-FALL", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Season(anime));
            using var client = new AniBtClient(partial);
            var result = client.CalendarAsync(Start, End, TimeZoneInfo.Utc, default).GetAwaiter().GetResult();
            Assert.True(result.PartialFailure);
            Assert.False(result.Truncated);
            Assert.Equal(1, result.Entries.Count);

            using var all = new Transport(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            using var limited = new AniBtClient(all);
            var error = Assert.Catch<AniBtException>(() => limited.CalendarAsync(Start, End, default).GetAwaiter().GetResult());
            Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
            Assert.Equal(1, all.Requests.Count);
        });

        Test("AniBT 日历错误信封、错误作用域、坏分组及改季度重定向均被拒绝", () =>
        {
            foreach (var reply in new Func<HttpResponseMessage>[]
            {
                () => Json("""{"ok":false,"data":{"scope":{"kind":"season"},"byWeekday":[],"truncated":false}}"""),
                () => Json("""{"ok":true,"data":{"scope":{"kind":"query"},"byWeekday":[],"truncated":false}}"""),
                () => Json("""{"ok":true,"data":{"scope":{"kind":"season"},"byWeekday":[{"animes":null}],"truncated":false}}"""),
                () => Redirect("/api/seasons/anime?season=2026-FALL")
            })
            {
                using var transport = new Transport(_ => reply());
                using var client = new AniBtClient(transport);
                Assert.Throws<AniBtException>(() => client.CalendarAsync(new(2027, 1, 1), End, default).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Requests.Count);
            }
        });

        Test("AniBT 日历无效窗口与主动取消不发请求", () =>
        {
            using var transport = new Transport(_ => Season(""));
            using var client = new AniBtClient(transport);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => client.CalendarAsync(Start, End, cancellation.Token).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() => client.CalendarAsync(Start, Start, default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentOutOfRangeException>(() => client.CalendarAsync(Start, Start.AddDays(94), default).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Requests.Count);
        });

        Test("AniBT 日历封面仅匿名读取指定 HTTPS 主机并拒绝非图片、超限和跳转", () =>
        {
            using var transport = new Transport(uri => uri.AbsolutePath switch
            {
                "/redirect" => Redirect(CoverUrl),
                "/json" => Json("{}"),
                "/oversize" => Picture(8 * 1024 * 1024 + 1),
                _ => Picture()
            });
            using var client = new AniBtClient(transport);
            foreach (var url in new[] { "http://r2.anibt.net/a", "https://r2.anibt.net:8443/a", "https://r2.anibt.net.example.org/a",
                         "https://server.invalid/a", "https://placeholder@r2.anibt.net/a", CoverUrl + "?credential=placeholder" })
                Assert.Equal(0, client.CalendarCoverAsync(url, default).GetAwaiter().GetResult().Length);
            Assert.Equal(0, transport.Requests.Count);
            foreach (var path in new[] { "redirect", "json", "oversize" })
                Assert.Equal(0, client.CalendarCoverAsync("https://r2.anibt.net/" + path, default).GetAwaiter().GetResult().Length);
            Assert.Equal(4, client.CalendarCoverAsync(CoverUrl, default).GetAwaiter().GetResult().Length);
            Assert.Equal(4, transport.Requests.Count);
        });
    }

    private static HttpResponseMessage Season(string entries, bool truncated = false) => Json(
        $$$"""{"ok":true,"data":{"scope":{"kind":"season"},"byWeekday":[{"animes":[{{{entries}}}]}],"truncated":{{{(truncated ? "true" : "false")}}}}}""");

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Picture(int? contentLength = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xff, 0xd8, 0xff, 0xd9]) };
        response.Content.Headers.ContentType = new("image/jpeg");
        if (contentLength is not null) response.Content.Headers.ContentLength = contentLength;
        return response;
    }

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private sealed class Transport(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            var uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            return Task.FromResult(respond(uri));
        }
    }
}
