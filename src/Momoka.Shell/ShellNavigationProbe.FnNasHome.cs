using System.Net;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 2026-10-25 用户令两轮：「飞牛影视库不要再显示单集封面」（图上并排摆着 JOJO 和 暗蓝之月 S02E06）、
    /// 「首页飞牛影视库的标题去掉最近添加」；同日又报「只有飞牛登录时，首页没有轮播图」。
    /// 离线夹具只登录一台飞牛，核对：货架标题就是库名、货架上没有单集、轮播靠详情补的背景图站得起来。
    /// </summary>
    private async Task FnNasHomeAsync()
    {
        using var fixture = CreateFixture();
        var poster = await FixtureImageAsync(400, 600);
        var wide = await FixtureImageAsync(1280, 720);
        var lists = new List<JsonObject>();
        var details = new List<string>();
        fixture.Transport.Reply = async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            // 产品探测先试 Emby 的公开信息；飞牛地址必须让这条落空，否则会被认成 Emby。
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal))
                return FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.9.8" });
            if (path.EndsWith("/api/v1/sys/config", StringComparison.Ordinal))
                return FnNasEnvelope(new { server_guid = "fn-guid", server_name = "飞牛影视库" });
            if (path.EndsWith("/api/v1/user/info", StringComparison.Ordinal))
                return FnNasEnvelope(new { guid = "user", username = "user", is_admin = "1" });
            if (path.EndsWith("/api/v1/mediadb/list", StringComparison.Ordinal))
                return FnNasEnvelope(new[] { new { guid = "fn-tv", name = "电视节目", category = "TV" } });
            if (path.EndsWith("/api/v1/play/list", StringComparison.Ordinal))
                return FnNasEnvelope(Array.Empty<object>());
            if (path.EndsWith("/api/v1/item/list", StringComparison.Ordinal))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                lists.Add(body);
                // 和真实服务器一样按 tags.type 过滤：货架问 TV 时单集不该出现在回包里。
                var types = body["tags"]?["type"]?.AsArray()
                    .Select(node => node!.GetValue<string>()).ToArray() ?? [];
                var all = new[]
                {
                    FnNasSeriesItem("fn-jojo", "JOJO的奇妙冒险"),
                    FnNasEpisodeItem("fn-ep", "暗蓝之月"),
                    FnNasSeriesItem("fn-basara", "战国BASARA")
                };
                var list = types.Length == 0
                    ? all
                    : all.Where(item => types.Contains(item["type"]!.GetValue<string>())).ToArray();
                return FnNasEnvelope(new { list, total = list.Length });
            }
            if (path.Contains("/api/v1/item/", StringComparison.Ordinal))
            {
                // 列表不带宽图，宽图只在详情里（真实服务器的 item/list 就是这样）。
                var id = path[(path.LastIndexOf('/') + 1)..];
                details.Add(id);
                return FnNasEnvelope(new
                {
                    guid = id,
                    title = id,
                    type = "TV",
                    poster = "/poster/" + id + ".webp",
                    backdrops = "/wide/" + id + ".webp"
                });
            }
            if (path.EndsWith("/api/v1/tag/list", StringComparison.Ordinal))
                return FnNasEnvelope(new { });
            if (path.EndsWith("/api/v1/tag/genres", StringComparison.Ordinal)
                || path.EndsWith("/api/v1/tag/iso3166", StringComparison.Ordinal))
                return FnNasEnvelope(Array.Empty<object>());
            if (path.Contains("/api/v1/sys/img/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(path.Contains("/wide/", StringComparison.Ordinal) ? wide : poster)
                };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        await fixture.SelectAsync(host: "fn.invalid", kind: MediaServerKind.FnNas);

        var libraries = await SourceLibraries.LoadAsync(fixture.Session, CancellationToken.None);
        Require(libraries.Items.Count == 1 && libraries.Items[0].Name == "电视节目", "飞牛库目录没读到");
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.CarouselCount = 4;
        fixture.Settings.Ui.CarouselSeconds = 60;
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, libraries.Items));

        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            ResizeProbeClient(1280, 1000);
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, libraries.Items, null));
            var home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.ViewModel.Shelves.Count > 0);

            var shelf = home.ViewModel.Shelves.SingleOrDefault(item => item.HomeKey == HomeLayout.LibraryKey(libraries.Items[0]))
                ?? throw new InvalidOperationException("主页没有飞牛库那一排");
            Require(shelf.Title == "电视节目", $"飞牛货架的标题还是「{shelf.Title}」");
            Require(shelf.Cards.Count == 2 && shelf.Cards.All(card => card.Item.Type == EmbyItemType.Series),
                $"飞牛货架上混进了单集：{string.Join('、', shelf.Cards.Select(card => card.Item.Type))}");

            var shelfQuery = lists.FirstOrDefault(body => body["ancestor_guid"]?.GetValue<string>() == "fn-tv")
                ?? throw new InvalidOperationException("主页没有向飞牛库问最近添加");
            var types = shelfQuery["tags"]?["type"]?.AsArray()
                .Select(node => node!.GetValue<string>()).ToArray() ?? [];
            Require(!types.Contains("Episode", StringComparer.Ordinal),
                $"飞牛货架仍向服务器要单集：{string.Join(',', types)}");

            // 轮播：列表里两张剧都没有宽图，详情才有 backdrops；单集那张 16:9 剧照直接能上带。
            Require(home.ViewModel.Slides.Count == 2,
                $"只登录飞牛时轮播站不起来：{home.ViewModel.Slides.Count} 张");
            Require(details.Contains("fn-jojo", StringComparer.Ordinal) && details.Contains("fn-basara", StringComparer.Ordinal),
                "轮播没有去详情取宽图");
            Require(home.ViewModel.Slides.All(slide => ItemArtwork.Banner(slide.Item) is not null),
                "轮播里有幻灯片没有宽图");

            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 1000);
                await LayoutAsync(home);
                await SaveProbeScreenAsync($"home-fnnas-{width}-screen.png");
            }

            Require(fixture.Actions.Plays == 0, "主页检查意外请求播放");
            home.Release();
        }
        finally
        {
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }

    private static JsonObject FnNasSeriesItem(string id, string title) => new()
    {
        ["guid"] = id,
        ["title"] = title,
        ["type"] = "TV",
        ["poster"] = "/poster/" + id + ".webp",
        ["poster_width"] = 1920,
        ["poster_height"] = 2560,
        ["local_number_of_episodes"] = 3,
        ["watched"] = 0
    };

    /// <summary>集的 poster 是 16:9 剧照（真实服务器 1920×1080），列表里它就够当宽图。</summary>
    private static JsonObject FnNasEpisodeItem(string id, string title) => new()
    {
        ["guid"] = id,
        ["title"] = title,
        ["type"] = "Episode",
        ["tv_guid"] = "fn-jojo",
        ["tv_title"] = "JOJO的奇妙冒险",
        ["poster"] = "/still/" + id + ".webp",
        ["poster_width"] = 1920,
        ["poster_height"] = 1080,
        ["watched"] = 0
    };
}
