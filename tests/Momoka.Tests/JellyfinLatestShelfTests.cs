using System.Collections;
using Momoka.Emby;
using static Momoka.Tests.LibraryResumePagingTests;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual Release recommendation VM and CardShelf, with only anonymous HTTP replaced.</summary>
internal static class JellyfinLatestShelfTests
{
    public static int Run(string shellPath, string root)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var shell = new ShellFixture(shellPath, root);
        foreach (var version in new[] { "10.10.7", "12.4.0" })
            Test($"Jellyfin {version}：真实推荐货架保留同剧两张单集卡和来源", () => SingleThread.Run(async () =>
            {
                using var fixture = new Fixture(shell, 0, firstKind: MediaServerKind.Jellyfin, firstVersion: version);
                Uri? latest = null;
                fixture.Transport.LatestReply = uri =>
                {
                    latest = uri;
                    return LatestGroupingFixture.Response(uri, sameSeries: true);
                };
                var vm = fixture.CreateLibraryView(collectionType: "tvshows", section: LibrarySection.Recommended);
                try
                {
                    await (Task)shell.Invoke(vm, "LoadAsync", true)!;
                    var shelves = ((IEnumerable)shell.Get<object>(vm, "RecommendedShelves")).Cast<object>().ToArray();
                    var shelf = shelves.Single(row => shell.Get<string>(row, "Title") == "最新剧集");
                    var cards = ((IEnumerable)shell.Get<object>(shelf, "Cards")).Cast<object>().ToArray();
                    Assert.Equal("episode-1,episode-2", string.Join(',', cards.Select(card => shell.Get<EmbyItem>(card, "Item").Id)));
                    Assert.True(cards.All(card => shell.Get<EmbyItem>(card, "Item").Type == EmbyItemType.Episode));
                    Assert.True(cards.All(card => ReferenceEquals(fixture.Source("a"), shell.Get<EmbyItem>(card, "Item").Source)));
                    Assert.Equal("false", LatestGroupingFixture.Query(latest!, "GroupItems"));
                    Assert.Equal("library", LatestGroupingFixture.Query(latest!, "ParentId"));
                    Assert.Null(LatestGroupingFixture.Query(latest!, "IsPlayed"));
                    Assert.Equal("Episode", LatestGroupingFixture.Query(latest!, "IncludeItemTypes"));
                    Assert.Equal(version == "10.10.7" ? "/emby/Users/fixture-user/Items/Latest" : "/emby/Items/Latest", latest!.AbsolutePath);
                    Assert.False(shell.Get<bool>(vm, "Busy"));
                }
                finally { ((IDisposable)vm).Dispose(); }
            }));
        return TestHarness.Run();
    }
}
