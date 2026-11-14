using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 人物页的匿名原生检查：资料、三条作品带、影视作品分组、数据库链接、人物菜单和收藏写入。
    /// <para>
    /// 只走假 HTTP 与假身份：服务端只接受 GET，以及一次收藏写入（用来核人物页那颗心真的落到
    /// <c>FavoriteItems</c> 上）。不播放、不打开外站、不碰真实服务器。
    /// </para>
    /// </summary>
    private async Task PersonPageAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var image = await FixtureImageAsync(400, 600);

        var person = new EmbyItem
        {
            Id = "person-1",
            Name = "高桥李依",
            Type = EmbyItemType.Person,
            PremiereDate = new DateTimeOffset(1994, 2, 27, 0, 0, 0, TimeSpan.Zero),
            ProductionLocations = ["Saitama, Japan"],
            Overview = "离线人物简介：这是探针里的一段话，不来自真实服务器。",
            ProviderIds = new Dictionary<string, string> { ["Imdb"] = "nm1234567", ["Tmdb"] = "76501" }
        };
        person.ImageTags["Primary"] = "fixture";

        var series = new EmbyItem
        {
            Id = "series-1",
            Name = "与你相恋到生命尽头",
            Type = EmbyItemType.Series,
            ProductionYear = 2026,
            Status = "Continuing"
        };
        series.ImageTags["Primary"] = "fixture";
        var secondSeries = new EmbyItem
        {
            Id = "series-2",
            Name = "Re:从零开始的异世界生活",
            Type = EmbyItemType.Series,
            ProductionYear = 2016,
            Status = "Continuing"
        };
        secondSeries.ImageTags["Primary"] = "fixture";
        var movie = new EmbyItem
        {
            Id = "movie-1",
            Name = "剧场版 离线测试",
            Type = EmbyItemType.Movie,
            ProductionYear = 2024
        };
        movie.ImageTags["Primary"] = "fixture";

        var episode = new EmbyItem
        {
            Id = "episode-1",
            Name = "第 3 集",
            Type = EmbyItemType.Episode,
            SeriesId = "series-1",
            SeriesName = "JOJO 的奇妙冒险",
            ParentIndexNumber = 6,
            IndexNumber = 3,
            ProductionYear = 2026,
            UserData = new EmbyUserData { Played = true }
        };
        episode.ImageTags["Primary"] = "fixture";
        var secondEpisode = new EmbyItem
        {
            Id = "episode-2",
            Name = "第 4 集",
            Type = EmbyItemType.Episode,
            SeriesId = "series-1",
            SeriesName = "JOJO 的奇妙冒险",
            ParentIndexNumber = 6,
            IndexNumber = 4,
            ProductionYear = 2026,
            UserData = new EmbyUserData { Played = true }
        };
        secondEpisode.ImageTags["Primary"] = "fixture";

        var actorMovie = new EmbyItem
        {
            Id = "credit-movie-1",
            Name = "魔法骑士",
            Type = EmbyItemType.Movie,
            ProductionYear = 2026,
            People = [new EmbyPerson { Id = "person-1", Name = "高桥李依", Type = "Actor", Role = "Fuu Hououji (voice)" }]
        };
        var actorSeries = new EmbyItem
        {
            Id = "credit-series-1",
            Name = "VERTEX FORCE",
            Type = EmbyItemType.Series,
            ProductionYear = 2026,
            People = [new EmbyPerson { Id = "person-1", Name = "高桥李依", Type = "Actor", Role = "Yoko Nishina (voice)" }]
        };
        var directed = new EmbyItem
        {
            Id = "credit-directed-1",
            Name = "离线导演作品",
            Type = EmbyItemType.Series,
            ProductionYear = 2020,
            People = [new EmbyPerson { Id = "person-1", Name = "高桥李依", Type = "Director" }]
        };

        fixture.Transport.Reply = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (request.Method == HttpMethod.Post && path.EndsWith("/FavoriteItems/person-1", StringComparison.Ordinal))
                return Task.FromResult(Json(new EmbyUserData { IsFavorite = true }));
            Require(request.Method == HttpMethod.Get, "人物页探针只允许收藏这一笔假写入");
            if (path.EndsWith("/Items/person-1", StringComparison.Ordinal)) return Task.FromResult(Json(person));
            if (path.EndsWith("/Items", StringComparison.Ordinal))
            {
                var include = Query(request, "IncludeItemTypes") ?? "";
                var personType = Query(request, "PersonTypes") ?? "";
                if (personType.Equals("Actor", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(new { Items = new[] { actorMovie, actorSeries }, TotalRecordCount = 3 }));
                if (personType.Equals("Director", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Json(new { Items = new[] { directed }, TotalRecordCount = 1 }));
                // 其余四种署名这一台假服务器没有作品；不能落到下面按 IncludeItemTypes 的那几支 ——
                // 影视作品每组都带 Movie,Series,Video，那样会让空组借到节目/电影那一排的条目。
                if (personType.Length > 0)
                    return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
                if (include.Contains(EmbyItemType.Series, StringComparison.Ordinal))
                    return Task.FromResult(Json(new { Items = new[] { series, secondSeries }, TotalRecordCount = 2 }));
                if (include.Contains(EmbyItemType.Movie, StringComparison.Ordinal))
                    return Task.FromResult(Json(new { Items = new[] { movie }, TotalRecordCount = 1 }));
                if (include.Contains(EmbyItemType.Episode, StringComparison.Ordinal))
                    return Task.FromResult(Json(new { Items = new[] { episode, secondEpisode }, TotalRecordCount = 2 }));
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            }
            return Task.FromResult(FakeTransport.Standard(request));
        };

        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(PersonPage), PersonRequest.For(fixture.Services, person));
            var page = (PersonPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            await LayoutAsync(page);

            Require(!page.ViewModel.NoticeOpen, "人物页首屏出现失败提示");
            Require(page.ViewModel.Name == "高桥李依", "人物名字没有读到");
            Require(page.ViewModel.BirthLine.Contains("1994年2月27日", StringComparison.Ordinal)
                && page.ViewModel.BirthLine.Contains("岁", StringComparison.Ordinal), "出生日期/年龄没有排版");
            Require(page.ViewModel.BirthplaceLine == "Saitama, Japan", "出生地没有读到");
            Require(page.ViewModel.Portrait is not null, "人物照片没有解出来");
            Require(page.ViewModel.SeriesShelf?.Cards.Count == 2, $"节目那一排不是 2 张：{page.ViewModel.SeriesShelf?.Cards.Count}");
            Require(page.ViewModel.MovieShelf?.Cards.Count == 1, "电影那一排不是 1 张");
            Require(page.ViewModel.EpisodeShelf?.Cards.Count == 2, "集那一排不是 2 张");
            Require(page.ViewModel.Filmography.Count == 2, $"影视作品不是 2 组：{page.ViewModel.Filmography.Count}");
            Require(page.ViewModel.Filmography[0].Heading == "演员 (3)"
                && page.ViewModel.Filmography[0].Rows.Count == 2
                && page.ViewModel.Filmography[0].MoreVisibility == Visibility.Visible,
                "演员分组标题、行数或「查看全部」不对");
            Require(page.ViewModel.Filmography[0].Rows[0].Role.StartsWith("扮演 ", StringComparison.Ordinal),
                "演员分组没有写出角色名");
            Require(page.ViewModel.OtherFacts.Count == 1, "数据库链接没有变成一行");
            var facts = Get<WrapRow>(page, "OtherFactsRow");
            Require(facts.Children.Count >= 3, "数据库链接那一行没有搭出来");

            // 人物页自己的「更多」不重复「打开」；收藏、编辑、照片、识别、刷新都在，合集和删除不在。
            var menu = ItemCommands.Build(fixture.Session, fixture.Actions, page, page.ViewModel.CommandCard, self: true);
            var labels = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToList();
            Require(!labels.Contains("打开"), "人物页的更多菜单又摆了一次打开");
            Require(labels.Contains("添加到收藏"), "人物页更多菜单没有收藏");
            Require(labels.Contains("编辑元数据…"), "人物页更多菜单没有编辑元数据");
            Require(labels.Contains("修改照片…"), "人物页更多菜单没有修改照片");
            Require(labels.Contains("识别"), "人物页更多菜单没有识别");
            Require(labels.Contains("刷新元数据"), "人物页更多菜单没有刷新");
            Require(!labels.Contains("添加到合集"), "人物菜单不该有合集");
            Require(!labels.Contains("删除"), "人物菜单不该有删除");

            // 收藏走真实 VM 命令和同一条 FavoriteItems 路由；只写假服务器。
            Require(page.ViewModel.FavoriteActionVisibility == Visibility.Visible, "人物页没有显示收藏键");
            await page.ViewModel.ToggleFavoriteCommand.ExecuteAsync(null);
            Require(page.ViewModel.Favorite, "收藏状态没有回填到人物页");
            Require(fixture.Actions.Plays == 0, "人物页探针触发了播放");

            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"person-page-{width}-screen.png");
            }

            // 影视作品和「其他信息」在首屏之外：滚到底再拍一张，行、分组标题和数据库链接才是真的上过屏。
            ResizeProbeClient(1280, 900);
            await LayoutAsync(page);
            var body = Get<ScrollView>(page, "Body");
            await ScrollProbeAsync(body, body.ScrollableHeight);
            await LayoutAsync(page);
            await SaveProbeScreenAsync("person-page-filmography-screen.png");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            _root.Children.Remove(frame);
        }
    }

    /// <summary>
    /// 演职人员卡上的真实封面进入人物页：匿名假 HTTP，只验证原生准备、目标照片交接和启动一次。
    /// 源卡是一个已实现、已取图的 PosterCard；人物页仍走正式构造和 OnNavigatedTo。
    /// </summary>
    private async Task PersonTransitionAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var image = await FixtureImageAsync(400, 600);
        var person = new EmbyItem
        {
            Id = "person-transition-1",
            Name = "关联动画人物",
            Type = EmbyItemType.Person
        };
        person.ImageTags["Primary"] = "fixture";
        fixture.Transport.Reply = (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            if (path.EndsWith("/Items/person-transition-1", StringComparison.Ordinal))
                return Task.FromResult(Json(person));
            if (path.EndsWith("/Items", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            return Task.FromResult(FakeTransport.Standard(request));
        };

        var frame = new Frame();
        var sourceHost = new Grid
        {
            Width = 200,
            Height = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var source = new PosterCard
        {
            Card = new CardItem(person, fixture.Images, 200),
            CardWidth = 200,
            PosterHeight = 300
        };
        sourceHost.Children.Add(source);
        _root.Children.Add(frame);
        _root.Children.Add(sourceHost);
        var receipts = new List<PersonTransitionReceipt>();
        var motion = HomeMotion.PreferenceEnabled;
        Action? disconnect = null;
        try
        {
            ResizeProbeClient(1280, 900);
            HomeMotion.ApplyPreference(true);
            await LayoutAsync(sourceHost);
            await source.Card!.EnsurePosterAsync();
            await UntilAsync(() => source.ConnectedPoster.Source is BitmapSource { PixelWidth: > 0, PixelHeight: > 0 }
                && !source.PosterArrivalRunning);
            Require(person.Source is { IsCurrent: true }, "人物关联动画源卡没有当前账号身份");
            Require(PersonTransition.Prepare(frame, source), "真实人物卡不能准备关联动画");
            disconnect = PersonTransition.Observe(frame, receipts.Add);
            Require(frame.Navigate(typeof(PersonPage), PersonRequest.For(fixture.Services, person),
                new SuppressNavigationTransitionInfo()), "人物页导航失败");
            sourceHost.Visibility = Visibility.Collapsed;
            var page = (PersonPage)frame.Content;
            await UntilAsync(() => receipts.Any(receipt => receipt.Started) && page.ViewModel.Portrait is not null);
            Require(receipts.Any(receipt => receipt.Started && !receipt.Terminal), "人物页关联动画没有启动");
            Require(page.ViewModel.Portrait is not null, "人物页没有接住关联动画的来源照片");
            await UntilAsync(() => receipts.Any(receipt => receipt.Terminal));
            Require(receipts.Count(receipt => receipt.Terminal) == 1 && receipts.Any(receipt => receipt.Completed),
                "人物页关联动画没有唯一的原生完成回执");
            Require(receipts.All(receipt => receipt.ElapsedMs >= 0 && receipt.PrepareToStartMs is >= 0), "人物关联计时缺失或为负");
            await WritePerformanceEvidenceAsync("person-transition.json", new { receipts, RealNetwork = false, MediaLoaded = false });
            await LayoutAsync(page);
            await SaveProbeScreenAsync("person-transition-screen.png");
        }
        finally
        {
            disconnect?.Invoke();
            PersonTransition.Cancel(frame);
            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            _root.Children.Remove(frame);
            _root.Children.Remove(sourceHost);
            HomeMotion.ApplyPreference(motion);
        }
    }
}
