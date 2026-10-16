using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SeriesCardDatesAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var poster = await FixtureImageAsync(400, 600);
        var rows = new[]
        {
            new EmbyItem { Id = "airing", Name = "连载剧", Type = EmbyItemType.Series,
                ProductionYear = 2023, Status = "Continuing", ChildCount = 3 },
            new EmbyItem { Id = "ended", Name = "跨年完结剧", Type = EmbyItemType.Series,
                ProductionYear = 2016, Status = "Ended", EndDate = new DateTimeOffset(2018, 6, 1, 0, 0, 0, TimeSpan.Zero), ChildCount = 3 },
            new EmbyItem { Id = "single", Name = "同年完结剧", Type = EmbyItemType.Series,
                ProductionYear = 2024, Status = "Ended", EndDate = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), ChildCount = 1 },
            new EmbyItem { Id = "unknown", Name = "年份未知", Type = EmbyItemType.Series, ChildCount = 2 }
        };
        foreach (var row in rows) row.ImageTags["Primary"] = "fixture";
        fixture.Transport.Reply = (request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) }
                : Query(request, "ParentId") == "series-library"
                    ? Json(new { Items = rows, TotalRecordCount = rows.Length })
                    : FakeTransport.Standard(request));
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            ResizeProbeClient(1280, 900);
            frame.Navigate(typeof(LibraryPage), new LibraryRequest
            {
                Services = fixture.Services,
                Title = "电视剧",
                ParentId = "series-library",
                CollectionType = "tvshows"
            });
            var page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.IsReady && page.ViewModel.Cards.Count == rows.Length);
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(page);
                var cards = Descendants(page).OfType<PosterCard>().Where(card => card.Card is not null).ToArray();
                Require(cards.Length == rows.Length, "电视媒体库未呈现全部日期夹具");
                var expected = new Dictionary<string, string>
                {
                    ["airing"] = "2023 – 至今",
                    ["ended"] = "2016 – 2018",
                    ["single"] = "2024",
                    ["unknown"] = ""
                };
                foreach (var card in cards)
                {
                    var item = card.Card ?? throw new InvalidOperationException("电视剧卡片尚未绑定条目");
                    Require(Get<TextBlock>(card, "SubLine").Text == expected[item.Item.Id],
                        $"电视剧封面日期绑定错误或单账号多出了来源：{item.Item.Id}");
                }
                await SaveProbeScreenAsync($"series-card-dates-{width}.png");
            }
            Require(fixture.Actions.Plays == 0, "日期检查意外请求播放");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
