using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实外壳的面包屑文字下沿与封面左、上间距；仅使用匿名夹具。</summary>
    private async Task DetailCoverGapAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        if (_window!.IsMaximized) _window.ToggleMaximize();
        await SettleAsync();
        var poster = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync(1600, 900);
        var logo = await FixtureImageAsync(800, 200);
        var series = Series();
        series.Name = "离线等距测试剧集";
        series.Overview = ResponsiveOverview;
        series.Genres = ["动画", "喜剧"];
        series.ProductionYear = 2014;
        series.CommunityRating = 7.2f;
        series.OfficialRating = "TV-14";
        series.People = [new() { Id = "person", Name = "离线演员", Type = "Actor", PrimaryImageTag = "fixture" }];
        series.BackdropImageTags.Add("fixture");
        series.ImageTags["Primary"] = "fixture";
        series.ImageTags["Logo"] = "fixture";
        var episode = Episode("a");
        episode.RunTimeTicks = TimeSpan.FromMinutes(24).Ticks;
        episode.UserData = new() { PlaybackPositionTicks = TimeSpan.FromMinutes(3).Ticks };
        episode.MediaSources =
        [
            new()
            {
                Id = "offline", Size = 461L * 1024 * 1024, Container = "mkv",
                MediaStreams =
                [
                    new() { Index = 0, Type = "Video", Codec = "av1", Width = 1920, Height = 1080 },
                    new() { Index = 1, Type = "Audio", DisplayTitle = "OPUS stereo", IsDefault = true },
                    new() { Index = 2, Type = "Subtitle", DisplayTitle = "自动（由 mpv 决定）", IsDefault = true }
                ]
            }
        ];
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(path.Contains("/Primary", StringComparison.Ordinal) ? poster
                        : path.Contains("/Logo", StringComparison.Ordinal) ? logo : backdrop)
                });
            if (path.EndsWith("/Items/show", StringComparison.Ordinal)) return Task.FromResult(Json(series));
            if (path.EndsWith("/Episodes", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = new[] { episode } }));
            if (path.EndsWith("/Similar", StringComparison.Ordinal)) return Task.FromResult(Items(2, 2, "similar"));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        var readings = new List<string>();
        try
        {
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            shell.OpenDetail(DetailRequest.For(fixture.Services, series));
            var page = (DetailPage)Get<Frame>(shell, "ContentFrame").Content;
            page.AttachWindow(_window);
            await UntilAsync(() => page.IsReady && page.ViewModel.StillImage is not null && page.ViewModel.PlateImage is not null
                && page.ViewModel.CastVisibility == Visibility.Visible && page.ViewModel.SimilarVisibility == Visibility.Visible);
            shell.SetTitleStrip(TitleStrip.OnScrim);
            var visit = 0;
            foreach (var width in new[] { 1280, 1100, 1024, 1280 })
            {
                await ResizeResponsiveAsync(page, width, 900);
                await LayoutAsync(shell);
                Call(_window, "MoveToScreen", _options.Screen);
                await LayoutAsync(shell);
                var trailText = Descendants(Get<BreadcrumbBar>(shell, "Trail")).OfType<TextBlock>()
                    .First(text => text.Text == series.Name && text.ActualHeight > 0 && text.Visibility == Visibility.Visible);
                var cover = Get<Border>(page, "PosterStill");
                var box = Bounds(cover, shell);
                var crumb = Bounds(trailText, shell);
                var prose = Bounds(Get<TextBlock>(page, "Overview"), shell);
                var hero = Bounds(Get<Grid>(page, "HeroContent"), shell);
                Require(Math.Abs(box.Left - (box.Top - crumb.Bottom)) < 1 && Math.Abs(box.Left - 42) < 1,
                    $"{width} 封面左右/上方留白不等：左{box.Left:0.##}，上{box.Top - crumb.Bottom:0.##}");
                Require(Math.Abs(prose.Left - box.Left) < 1, $"{width} 剧情未与封面左沿对齐");
                var sheet = Get<StackPanel>(page, "BodySheet");
                var head = Get<ShelfHead>(page, "EpisodeHead");
                var strip = Get<ShelfStrip>(page, "EpisodeStrip");
                Require(Math.Abs(Bounds(head, shell).Left - box.Left) < 1
                    && Math.Abs(Bounds(strip, shell).Left - box.Left) < 1,
                    $"{width} 下方集列表标题与卡片未和封面对齐");
                var panels = sheet.Children.OfType<StackPanel>().Where(panel => panel.Visibility == Visibility.Visible).ToArray();
                Require(panels.Length == 5, $"下方五个组件未全部装载：{panels.Length}");
                var sheetRight = Bounds(sheet, shell).Right;
                foreach (var bodyPanel in panels)
                {
                    var bodyHead = bodyPanel.Children.OfType<ShelfHead>().First();
                    Require(Math.Abs(Bounds(bodyHead, shell).Left - box.Left) < 1,
                        $"{width} {bodyPanel.Name} 标题起点未对齐封面");
                    Require(Math.Abs(sheetRight - Bounds(bodyPanel, shell).Right - 8) < 1
                        && Math.Abs(sheetRight - Bounds(bodyHead, shell).Right - 32) < 1,
                        $"{width} {bodyPanel.Name} 右侧留白未与左侧一致");
                    // 2026-11-08 用户令「右边空位的太多了，左边加宽一点点，然后两边对齐」：量的不是纸面内那一层
                    // （上面两句只说明板自己的内边距没歪），而是这一块在**窗口**里左右两侧的空位 —— 纸面那一层
                    // 从前只让右边 20，整页因此偏左。两边各让 10 之后这两个数必须相等。
                    var shellWidth = shell.ActualWidth;
                    Require(Math.Abs(Bounds(bodyHead, shell).Left - (shellWidth - Bounds(bodyHead, shell).Right)) < 1,
                        $"{width} {bodyPanel.Name} 窗口左右空位不等：左 {Bounds(bodyHead, shell).Left:0.##}，"
                            + $"右 {shellWidth - Bounds(bodyHead, shell).Right:0.##}");
                    foreach (var row in bodyPanel.Children.OfType<ShelfStrip>())
                    {
                        Require(Math.Abs(Bounds(row, shell).Left - box.Left) < 1,
                            $"{width} {bodyPanel.Name} 卡片起点未对齐封面");
                        Require(Math.Abs(sheetRight - Bounds(row, shell).Right - 32) < 1
                            && Math.Abs(Get<ScrollView>(row, "Scroller").ViewportWidth - row.ActualWidth) < 1,
                            $"{width} {bodyPanel.Name} 卡片右沿或滚动视口未展开");
                    }
                    readings.Add($"{width} {bodyPanel.Name}: 左距={Bounds(bodyHead, shell).Left:0.##}，右距={sheetRight - Bounds(bodyHead, shell).Right:0.##}");
                }
                var infoBox = Bounds(Get<ItemsRepeater>(page, "InfoTable"), shell);
                Require(Math.Abs(infoBox.Left - box.Left) < 1 && Math.Abs(sheetRight - infoBox.Right - 32) < 1,
                    $"{width} 信息表左右留白未对齐");
                readings.Add($"{width}: 左距={box.Left:0.##}，上距={box.Top - crumb.Bottom:0.##}，封面顶={box.Top:0.##}，字底={crumb.Bottom:0.##}，头部顶={hero.Top:0.##}，剧情左={prose.Left:0.##}");
                if (visit++ == 0)
                {
                    var finalPadding = sheet.Padding;
                    var finalHead = Bounds(head, shell);
                    try
                    {
                        sheet.Padding = new Thickness(8, finalPadding.Top, 48, finalPadding.Bottom);
                        await LayoutAsync(shell);
                        var oldHead = Bounds(head, shell);
                        Require(Math.Abs(oldHead.Left - finalHead.Left) < 1
                            && Math.Abs(finalHead.Right - oldHead.Right - 40) < 1,
                            "旧正文右侧空位没有被对称留白检查拒绝");
                        readings.Add($"旧正文: 左距={oldHead.Left:0.##}，右距={sheetRight - oldHead.Right:0.##}；右侧扩展={finalHead.Right - oldHead.Right:0.##}");
                        await SaveProbeScreenAsync("detail-body-alignment-before.png");
                    }
                    finally { sheet.Padding = finalPadding; }
                    await LayoutAsync(shell);
                    Require(Math.Abs(Bounds(head, shell).Left - box.Left) < 1
                        && Math.Abs(Bounds(head, shell).Right - finalHead.Right) < 1,
                        "正文反例后左右沿没有恢复");
                    await SaveProbeScreenAsync("detail-body-alignment-after.png");
                    var bodyScroll = Get<ScrollView>(page, "Body");
                    foreach (var bodyPanel in panels)
                    {
                        await ScrollProbeAsync(bodyScroll, Bounds(bodyPanel, (UIElement)bodyScroll.Content).Top);
                        await LayoutAsync(shell);
                        await SaveProbeScreenAsync($"detail-body-alignment-{bodyPanel.Name}.png");
                    }
                    await ScrollProbeAsync(bodyScroll, 0);
                    await LayoutAsync(shell);
                    var band = Get<Grid>(page, "HeroBand");
                    var layout = Get<Grid>(page, "HeroLayout");
                    var heroContent = Get<Grid>(page, "HeroContent");
                    var button = Get<Button>(page, "CoverButton");
                    var finalBand = band.Height;
                    var finalHeight = layout.Height;
                    var finalInset = heroContent.Margin;
                    var finalAlignment = button.VerticalAlignment;
                    try
                    {
                        var oldTop = DetailHero.Between(12, 0, page.ViewModel.Squeeze);
                        var delta = oldTop - finalInset.Top;
                        band.Height = finalBand + delta;
                        layout.Height = finalHeight + delta;
                        heroContent.Margin = new Thickness(44, oldTop, 76, finalInset.Bottom);
                        button.VerticalAlignment = VerticalAlignment.Center;
                        await LayoutAsync(shell);
                        var old = Bounds(cover, shell);
                        Require(Math.Abs(old.Top - crumb.Bottom - box.Left) > 10,
                            "旧居中布局没有被等距检查拒绝");
                        // 统一为375高后，居中反例可能向上或向下偏移；上面的等距拒绝仍验证错误布局。
                        readings.Add($"旧版: 左距={old.Left:0.##}，上距={old.Top - crumb.Bottom:0.##}，封面顶={old.Top:0.##}，新封面上移={old.Top - box.Top:0.##}");
                        await SaveProbeScreenAsync("detail-cover-gap-before.png");
                    }
                    finally
                    {
                        band.Height = finalBand;
                        layout.Height = finalHeight;
                        heroContent.Margin = finalInset;
                        button.VerticalAlignment = finalAlignment;
                    }
                    await LayoutAsync(shell);
                    Require(Math.Abs(Bounds(cover, shell).Top - box.Top) < 1, "反例后封面顶未恢复");
                }
                await SaveProbeScreenAsync($"detail-cover-gap-{width}.png");
            }
            Require(fixture.Actions.Plays == 0, "封面等距检查意外请求播放");
        }
        finally
        {
            try { File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-cover-gap.txt"), readings); }
            finally
            {
                Call(shell, "ReleaseContent");
                shell.SignInRoot.Detach();
                _root.Children.Remove(shell);
            }
        }
    }
}
