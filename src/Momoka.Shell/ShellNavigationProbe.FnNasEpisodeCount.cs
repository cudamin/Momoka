using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 2026-10-25 用户令「飞牛电视剧库的右上角照 Emby 库显示集数」：飞牛列表里没有「未看几集」这个数，
    /// 同一个角于是改说这部剧库里有多少集（<c>local_number_of_episodes</c>）。离线夹具核对四件事：
    /// 写的是本地集数（不是元数据总集数、也不是季数）、跨季的剧写合计、看完的剧让位给已看对勾、角标真在右上角。
    /// </summary>
    private async Task FnNasEpisodeCountAsync()
    {
        using var fixture = CreateFixture();
        var poster = await FixtureImageAsync(400, 600);
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            // 产品探测先试 Emby 的公开信息；飞牛地址必须让这条落空，否则会被认成 Emby。
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.9.8" }));
            if (path.EndsWith("/api/v1/sys/config", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new { server_guid = "fn-guid", server_name = "离线飞牛" }));
            if (path.EndsWith("/api/v1/user/info", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new { guid = "user", username = "user", is_admin = "1" }));
            if (path.EndsWith("/api/v1/mediadb/list", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new[] { new { guid = "fn-tv", name = "电视节目", category = "TV" } }));
            if (path.EndsWith("/api/v1/item/list", StringComparison.Ordinal))
            {
                // 三张卡照真实服务器的形状造：牧神记元数据 105 集、库里 14 集；JOJO 本地两季共 3 集；
                // 第三部整部看完（watched=1）。元数据那两个数故意留着，用来证明角标没写错字段。
                return Task.FromResult(FnNasEnvelope(new
                {
                    list = new object[]
                    {
                        new
                        {
                            guid = "fn-series", title = "牧神记 年番1", type = "TV", poster = "poster.jpg",
                            release_date = "2024-10-27", number_of_seasons = 1, number_of_episodes = 105,
                            local_number_of_seasons = 1, local_number_of_episodes = 14, watched = 0
                        },
                        new
                        {
                            guid = "fn-jojo", title = "JOJO的奇妙冒险", type = "TV", poster = "poster.jpg",
                            release_date = "2012-10-05", number_of_seasons = 6, number_of_episodes = 202,
                            local_number_of_seasons = 2, local_number_of_episodes = 3, watched = 0
                        },
                        new
                        {
                            guid = "fn-watched", title = "已看完的剧", type = "TV", poster = "poster.jpg",
                            release_date = "2019-04-01", number_of_seasons = 1, number_of_episodes = 8,
                            local_number_of_seasons = 1, local_number_of_episodes = 8, watched = 1
                        }
                    },
                    total = 3
                }));
            }
            if (path.EndsWith("/api/v1/tag/list", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(new { }));
            if (path.EndsWith("/api/v1/tag/genres", StringComparison.Ordinal)
                || path.EndsWith("/api/v1/tag/iso3166", StringComparison.Ordinal))
                return Task.FromResult(FnNasEnvelope(Array.Empty<object>()));
            if (path.Contains("/api/v1/sys/img/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        await fixture.SelectAsync(host: "fn.invalid", kind: MediaServerKind.FnNas);

        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            var request = new LibraryRequest
            {
                Services = fixture.Services,
                Title = "电视节目",
                ParentId = "fn-tv",
                CollectionType = "tvshows"
            };
            frame.Navigate(typeof(LibraryPage), request);
            var page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.IsFnNas, "探针没有落进飞牛媒体库页");

            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(page);
                var cards = Descendants(page).OfType<PosterCard>()
                    .Where(card => card.Card is not null)
                    .ToDictionary(card => card.Card!.Item.Id, card => card, StringComparer.Ordinal);
                Require(cards.Count == 3, "飞牛电视剧库未呈现全部三张夹具卡片");

                var series = cards["fn-series"].Card!;
                Require(series.EpisodeCountText == "14", $"角标写的不是本地集数：{series.EpisodeCountText}");
                Require(series.EpisodeCountVisibility == Visibility.Visible, "未看完的剧没有画出集数角标");
                Require(series.Item.ChildCount == 1, "季数应仍走 ChildCount，不该被集数顶掉");

                var multi = cards["fn-jojo"].Card!;
                Require(multi.EpisodeCountText == "3", $"跨季的剧角标不是合计：{multi.EpisodeCountText}");

                var watched = cards["fn-watched"].Card!;
                Require(watched.EpisodeCountVisibility == Visibility.Collapsed,
                    "看完的剧仍在角上写集数，会和已看对勾叠在一起");
                Require(watched.WatchedBadgeVisibility == Visibility.Visible, "看完的剧没有已看对勾");

                // 画出来才算数：那颗字真在视觉树里、有宽高、并且贴着封面（Art 那张图）的右上角。
                // 量的参照物是 Art 而不是 PosterCard：后者是这一格的外框，卡片在里面居中，量它会把
                // 两侧的留白算成「没贴边」。
                var label = Descendants(cards["fn-series"]).OfType<TextBlock>()
                    .FirstOrDefault(text => text.Text == "14");
                if (label is not { Visibility: Visibility.Visible } || label.ActualWidth <= 0)
                    throw new InvalidOperationException("集数角标没有真的画在屏上");
                var art = Get<Border>(cards["fn-series"], "Art");
                var box = label.TransformToVisual(art).TransformBounds(new(0, 0, label.ActualWidth, label.ActualHeight));
                Require(box.Right > art.ActualWidth - 8 && box.Top is >= 0 and < 8,
                    $"集数角标不在封面右上角：右沿 {box.Right:0}/{art.ActualWidth:0}，上沿 {box.Top:0}");

                await SaveProbeScreenAsync($"library-fnnas-episode-count-{width}.png");
            }
            Require(fixture.Actions.Plays == 0, "集数角标检查意外请求播放");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
