using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task JellyfinLatestShelfAsync()
    {
        var receipts = new List<object>();
        var poster = await FixtureImageAsync();
        foreach (var version in new[] { "10.10.7", "12.4.0" })
        {
            using var fixture = CreateFixture();
            Uri? latest = null;
            var reads = 0;
            fixture.Transport.Reply = (request, _) =>
            {
                Require(request.Method == HttpMethod.Get, "最新货架探针拒绝写入");
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                    return Task.FromResult(Json(new { ProductName = "Jellyfin", ServerName = "匿名 Jellyfin", Version = version, Id = "jf-server" }));
                if (path.EndsWith("/UserViews", StringComparison.Ordinal))
                    return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
                if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
                {
                    latest = request.RequestUri;
                    reads++;
                    // Fixed 10.10.7/aefb9b2c and 12.4/83d0d0e7 controllers default to true.
                    // This boundary fixture simulates Series grouping; it does not run the server.
                    EmbyItem[] items = Query(request, "GroupItems") == "false"
                        ? [Episode(1), Episode(2)]
                        : [Item("series-a", EmbyItemType.Series)];
                    return Task.FromResult(Json(items));
                }
                return Task.FromResult(JellyfinResponse(request, poster));
            };
            await fixture.SelectAsync(kind: MediaServerKind.Jellyfin);
            var frame = new Frame();
            LibraryPage? page = null;
            _root.Children.Add(frame);
            try
            {
                frame.Navigate(typeof(LibraryPage), new LibraryRequest
                {
                    Services = fixture.Services,
                    Title = $"匿名 Jellyfin {version} 剧集",
                    ParentId = "jf-shows",
                    CollectionType = "tvshows"
                });
                page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
                await SelectTabAsync(page, LibrarySection.Recommended);
                await UntilAsync(() => page.ViewModel.RecommendedShelves.Count == 3 && !page.ViewModel.Busy);
                var shelf = page.ViewModel.RecommendedShelves.Single(row => row.Title == "最新剧集");
                Require(string.Join(',', shelf.Cards.Select(card => card.Item.Id)) == "episode-1,episode-2",
                    "最新剧集把同剧两集折成了父剧卡");
                Require(shelf.Cards.All(card => card.Item.Type == EmbyItemType.Episode
                    && card.Item.Source?.Key == fixture.Session.Capture().Key), "最新单集卡类型或来源不正确");
                Require(reads == 1 && latest is not null && latest.Query.Contains("GroupItems=false", StringComparison.Ordinal)
                    && latest.Query.Contains("ParentId=jf-shows", StringComparison.Ordinal)
                    && latest.Query.Contains("IncludeItemTypes=Episode", StringComparison.Ordinal)
                    && !latest.Query.Contains("IsPlayed=", StringComparison.Ordinal), "最新货架参数或读取次数错误");
                Require(latest!.AbsolutePath == (version == "10.10.7" ? "/media/Users/user/Items/Latest" : "/media/Items/Latest"),
                    "最新货架没有使用当前版本路由");
                foreach (var width in new[] { 1320, 760 })
                {
                    ResizeProbeClient(width, 900);
                    await LayoutAsync(page);
                    var strip = Descendants(page).OfType<ShelfStrip>().Single(value => ReferenceEquals(value.ItemsSource, shelf.Cards));
                    await UntilAsync(() => Descendants(strip).OfType<PosterCard>().Count(card => card.Card is not null) == 2);
                    var cards = Descendants(strip).OfType<PosterCard>().Where(card => card.Card is not null).ToArray();
                    Require(cards.All(card => card.IsLoaded && card.ActualWidth > 0 && card.ActualHeight > 0),
                        "最新单集卡没有实际布局");
                    Require(string.Join(',', cards.Select(card => card.Card!.Item.Id).Order()) == "episode-1,episode-2",
                        "实际货架控件没有画出两张单集卡");
                    await SaveProbeScreenAsync($"jellyfin-latest-{version}-{width}-screen.png");
                    receipts.Add(new
                    {
                        Version = version,
                        Width = width,
                        Reads = reads,
                        Path = latest.AbsolutePath,
                        Cards = cards.Select(card => card.Card!.Item.Id).ToArray(),
                        GroupItems = "false"
                    });
                }
            }
            finally
            {
                page?.Release();
                frame.Content = null;
                _root.Children.Remove(frame);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "jellyfin-latest-receipt.json"),
            JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));

        static EmbyItem Episode(int index)
        {
            var item = Item($"episode-{index}", EmbyItemType.Episode);
            item.Name = index == 1 ? "第一集" : "第二集";
            item.SeriesId = "series-a";
            item.SeriesName = "匿名示例剧";
            item.ParentIndexNumber = 1;
            item.IndexNumber = index;
            return item;
        }
    }
}
