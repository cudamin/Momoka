using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexMediaTests
{
    private static void RegisterWatchedCounts()
    {
        (string Total, string Viewed, bool Played, int? Remaining)[] cases =
        [
            ("2", "2", true, 0), ("\"2\"", "\"2\"", true, 0),
            ("2", "1", false, 1), ("2", "0", false, 2), ("0", "0", false, 0),
            ("null", "2", false, null), ("2", "null", false, null),
            ("2", "3", false, null), ("-1", "0", false, null), ("2", "-1", false, null),
            ("\"abc\"", "0", false, null), ("2", "\"NaN\"", false, null),
            ("2.5", "2", false, null), ("2", "1.5", false, null),
            ("2147483647", "0", false, 2_147_483_647), ("2147483648", "0", false, null)
        ];
        foreach (var type in new[] { "show", "season" })
        {
            foreach (var (total, viewed, played, remaining) in cases)
                Test($"Plex 观看：{type} 总数={total} 已看={viewed}", () =>
                {
                    var item = PlexMediaAdapter.Item(new JsonObject
                    {
                        ["ratingKey"] = "container",
                        ["type"] = type,
                        ["leafCount"] = JsonNode.Parse(total),
                        ["viewedLeafCount"] = JsonNode.Parse(viewed)
                    });
                    Assert.Equal(played, item.IsWatched);
                    Assert.Equal(remaining, item.UserData!.UnplayedItemCount);
                    var menu = ItemMenu.For(item, plex: true);
                    Assert.Equal(played, menu.Any(row => row.Command == ItemCommand.MarkUnplayed));
                    Assert.Equal(!played, menu.Any(row => row.Command == ItemCommand.MarkPlayed));
                });
            Test($"Plex 观看：{type} 缺集数时不借单片 viewCount 判定已看完", () =>
            {
                var item = PlexMediaAdapter.Item(new JsonObject { ["type"] = type, ["viewCount"] = 1 });
                Assert.False(item.IsWatched);
                Assert.Equal<int?>(null, item.UserData!.UnplayedItemCount);
            });
            Test($"Plex 观看：{type} 详情与标记后的实际HTTP回读使用集数", () =>
            {
                using var http = new PlexHttp(new WatchedCountTransport(type));
                var adapter = new PlexMediaAdapter(new PlexClient(http),
                    new PlexConnection(new Uri("http://plex.invalid/"), "fixture-token"));
                var initial = adapter.GetItemAsync("container", default).GetAwaiter().GetResult();
                Assert.False(initial.IsWatched);
                Assert.Equal(1, initial.UserData!.UnplayedItemCount);
                Assert.Null(adapter.SetWatchedAsync("container", true, default).GetAwaiter().GetResult());
                var watched = adapter.GetUserDataAsync("container", default).GetAwaiter().GetResult()!;
                Assert.True(watched.Played);
                Assert.Equal(0, watched.UnplayedItemCount);
                Assert.Null(adapter.SetWatchedAsync("container", false, default).GetAwaiter().GetResult());
                var unplayed = adapter.GetUserDataAsync("container", default).GetAwaiter().GetResult()!;
                Assert.False(unplayed.Played);
                Assert.Equal(2, unplayed.UnplayedItemCount);
                Assert.False(adapter.GetItemAsync("container", default).GetAwaiter().GetResult().IsWatched);
            });
        }
        foreach (var type in new[] { "movie", "episode" })
            foreach (var views in new[] { 0, 1 })
                Test($"Plex 观看：{type} 仍按单片次数 {views} 判定", () =>
                {
                    var item = PlexMediaAdapter.Item(new JsonObject
                    {
                        ["type"] = type,
                        ["viewCount"] = views,
                        ["leafCount"] = 2,
                        ["viewedLeafCount"] = 2
                    });
                    Assert.Equal(views > 0, item.IsWatched);
                    Assert.Equal<int?>(null, item.UserData!.UnplayedItemCount);
                });
    }

    private sealed class WatchedCountTransport(string type) : HttpMessageHandler
    {
        private int _viewed = 1;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri!.Host != "plex.invalid" || request.Method != HttpMethod.Get)
                throw new InvalidOperationException("Unexpected watched-count request");
            switch (request.RequestUri.AbsolutePath)
            {
                case "/:/scrobble": _viewed = 2; break;
                case "/:/unscrobble": _viewed = 0; break;
                case "/library/metadata/container": break;
                default: throw new InvalidOperationException("Unexpected watched-count path");
            }
            var json = new JsonObject
            {
                ["MediaContainer"] = new JsonObject
                {
                    ["Metadata"] = new JsonArray(new JsonObject
                    {
                        ["ratingKey"] = "container",
                        ["type"] = type,
                        ["leafCount"] = 2,
                        ["viewedLeafCount"] = _viewed
                    })
                }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json")
            });
        }
    }
}
