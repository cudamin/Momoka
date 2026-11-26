using System.Collections;
using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomePlexLatestTests
{
    internal static void Register(ShellFixture shell)
    {
        foreach (var media in new[] { CarouselMediaType.Movies, CarouselMediaType.Series })
            Test("Plex首页真实调用：轮播 " + media + " 过滤混合最新并跨原始页补足", () => SingleThread.Run(async () =>
            {
                using var fixture = new Fixture(shell, 1, firstKind: MediaServerKind.Plex);
                fixture.Settings.Settings.Ui.CarouselMedia = media;
                fixture.Settings.Settings.Ui.CarouselCount = 10;
                var rawStarts = new List<int>();
                fixture.Transport.PlexHomeReply = request =>
                {
                    var uri = request.RequestUri!;
                    var latest = uri.AbsolutePath.EndsWith("/recentlyAdded", StringComparison.Ordinal);
                    var start = int.Parse(request.Headers.GetValues("X-Plex-Container-Start").Single());
                    var size = int.Parse(request.Headers.GetValues("X-Plex-Container-Size").Single());
                    if (latest && !uri.AbsolutePath.Contains("/sections/", StringComparison.Ordinal)) rawStarts.Add(start);
                    var rows = new JsonArray();
                    if (latest)
                    {
                        for (var i = start; i < Math.Min(90, start + size); i++)
                            rows.Add(new JsonObject
                            {
                                ["ratingKey"] = "latest-" + i,
                                ["title"] = "anonymous-" + i,
                                ["type"] = i % 3 == 0 ? "movie" : i % 3 == 1 ? "show" : "track",
                                ["art"] = "/library/metadata/" + i + "/art/anonymous"
                            });
                    }
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(new JsonObject
                        {
                            ["MediaContainer"] = new JsonObject { ["size"] = rows.Count, ["totalSize"] = latest ? 90 : 0, ["Metadata"] = rows }
                        }.ToJsonString())
                    });
                };
                var home = fixture.CreateLoadingHome(banner: true);
                try
                {
                    await (Task)shell.Invoke(home, "ReloadAsync")!;
                    await Supplement(home);
                    var slides = ((IEnumerable)shell.Get<object>(home, "Slides")).Cast<object>().ToArray();
                    Assert.Equal(10, slides.Length);
                    var expected = media == CarouselMediaType.Movies ? EmbyItemType.Movie : EmbyItemType.Series;
                    Assert.True(slides.All(slide => shell.Get<EmbyItem>(slide, "Item").Type == expected),
                        "真实HomeVM传入的类型必须贯穿EmbyClient到Plex适配层");
                    var first = media == CarouselMediaType.Movies ? 0 : 1;
                    Assert.Equal(string.Join(',', Enumerable.Range(0, 10).Select(i => "latest-" + (first + i * 3))),
                        string.Join(',', slides.Select(slide => shell.Get<EmbyItem>(slide, "Item").Id)));
                    Assert.True(rawStarts.Contains(30) && rawStarts.Contains(60), "过滤限量30应消耗全部90条原始序列");
                }
                finally { await Supplement(home); ((IDisposable)home).Dispose(); }
            }));
    }
    private static Task Supplement(object home) => (Task)home.GetType().GetField("_supplement",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(home)!;
}
