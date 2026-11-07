using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class JellyfinProtocolTests
{
    private static void RegisterLatest()
    {
        foreach (var version in new[] { "10.10.7", "12.4.0" })
        {
            foreach (var sameSeries in new[] { true, false })
                Case($"Jellyfin {version}：最新单集明确不分组（同剧={sameSeries}）", async () =>
                {
                    using var handler = new Transport
                    {
                        Reply = sent => LatestGroupingFixture.Response(new Uri(sent.Url), sameSeries)
                    };
                    using var http = new EmbyHttp(handler);
                    var items = await Client(http, version).GetLatestAsync("shows", 24, default,
                        EmbyItemType.Episode, unplayedOnly: false, groupItems: false);
                    var sent = handler.Sent.Single();
                    Assert.Equal("false", LatestGroupingFixture.Query(new Uri(sent.Url), "GroupItems"));
                    Assert.Equal(version == "10.10.7" ? "/media/Users/u1/Items/Latest" : "/media/Items/Latest", sent.Path);
                    Assert.Equal(version == "10.10.7" ? null : "u1", LatestGroupingFixture.Query(new Uri(sent.Url), "UserId"));
                    Assert.Equal("shows", LatestGroupingFixture.Query(new Uri(sent.Url), "ParentId"));
                    Assert.Equal("24", LatestGroupingFixture.Query(new Uri(sent.Url), "Limit"));
                    Assert.Equal("Episode", LatestGroupingFixture.Query(new Uri(sent.Url), "IncludeItemTypes"));
                    Assert.Null(LatestGroupingFixture.Query(new Uri(sent.Url), "IsPlayed"));
                    Assert.Contains("PrimaryImageAspectRatio", LatestGroupingFixture.Query(new Uri(sent.Url), "Fields")!);
                    Assert.Equal("episode-1,episode-2", string.Join(',', items.Select(item => item.Id)));
                    Assert.True(items.All(item => item.Type == EmbyItemType.Episode));
                });

            foreach (var explicitTrue in new[] { false, true })
                Case($"Jellyfin {version}：默认及显式启用分组保留剧卡（显式={explicitTrue}）", async () =>
                {
                    using var handler = new Transport { Reply = sent => LatestGroupingFixture.Response(new Uri(sent.Url), true) };
                    using var http = new EmbyHttp(handler);
                    var client = Client(http, version);
                    var items = explicitTrue
                        ? await client.GetLatestAsync("shows", 4, default, EmbyItemType.Episode, groupItems: true)
                        : await client.GetLatestAsync("shows", 4, default, EmbyItemType.Episode);
                    var uri = new Uri(handler.Sent.Single().Url);
                    Assert.Equal("true", LatestGroupingFixture.Query(uri, "GroupItems"));
                    Assert.Equal("false", LatestGroupingFixture.Query(uri, "IsPlayed"));
                    Assert.Equal("series-a", items.Single().Id);
                    Assert.Equal(EmbyItemType.Series, items.Single().Type);
                });

            Case($"Jellyfin {version}：最新电影不分组且保留未看过滤与库范围", async () =>
            {
                using var handler = new Transport { Reply = _ => Json("""[{"Id":"movie-1","Type":"Movie"}]""") };
                using var http = new EmbyHttp(handler);
                var items = await Client(http, version).GetLatestAsync("movies", 7, default,
                    EmbyItemType.Movie, groupItems: false);
                var uri = new Uri(handler.Sent.Single().Url);
                Assert.Equal("false", LatestGroupingFixture.Query(uri, "GroupItems"));
                Assert.Equal("false", LatestGroupingFixture.Query(uri, "IsPlayed"));
                Assert.Equal("Movie", LatestGroupingFixture.Query(uri, "IncludeItemTypes"));
                Assert.Equal("movies", LatestGroupingFixture.Query(uri, "ParentId"));
                Assert.Equal("7", LatestGroupingFixture.Query(uri, "Limit"));
                Assert.Equal("movie-1", items.Single().Id);
            });
        }

        foreach (var grouping in new[] { false, true })
            Case($"Emby：共享最新请求仍明确传分组开关（{grouping}）", async () =>
            {
                using var handler = new Transport { Reply = _ => Json("[]") };
                using var http = new EmbyHttp(handler);
                var connection = Connection() with { ServerKind = MediaServerKind.Emby, ApiBase = new Uri("https://emby.invalid/proxy/emby/") };
                await new EmbyClient(http, connection).GetLatestAsync(null, 4, default, groupItems: grouping);
                var uri = new Uri(handler.Sent.Single().Url);
                Assert.Equal("/proxy/emby/Users/u1/Items/Latest", uri.AbsolutePath);
                Assert.Equal(grouping ? "true" : "false", LatestGroupingFixture.Query(uri, "GroupItems"));
                Assert.Null(LatestGroupingFixture.Query(uri, "ParentId"));
                Assert.Null(LatestGroupingFixture.Query(uri, "IncludeItemTypes"));
            });
    }
}

/// <summary>Anonymous boundary model of the fixed Jellyfin default and Series grouping, not a running server.</summary>
internal static class LatestGroupingFixture
{
    internal static string? Query(Uri uri, string key) => uri.Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Split('=', 2))
        .Where(pair => pair[0].Equals(key, StringComparison.OrdinalIgnoreCase))
        .Select(pair => Uri.UnescapeDataString(pair[1])).SingleOrDefault();

    internal static HttpResponseMessage Response(Uri uri, bool sameSeries)
    {
        // Both fixed controllers default to true; Episode.LatestItemsIndexContainer is Series.
        // A multi-item group returns its Series DTO; false leaves each Episode independent.
        EmbyItem[] items =
        [
            new() { Id = "episode-1", Name = "第一集", Type = EmbyItemType.Episode, SeriesId = "series-a", SeriesName = "示例剧 A", IndexNumber = 1, ParentIndexNumber = 1 },
            new() { Id = "episode-2", Name = "第二集", Type = EmbyItemType.Episode, SeriesId = sameSeries ? "series-a" : "series-b", SeriesName = sameSeries ? "示例剧 A" : "示例剧 B", IndexNumber = 2, ParentIndexNumber = 1 }
        ];
        if (Query(uri, "GroupItems") != "false" && sameSeries)
            items = [new() { Id = "series-a", Name = "示例剧 A", Type = EmbyItemType.Series, IsFolder = true }];
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(items), Encoding.UTF8, "application/json")
        };
    }
}
