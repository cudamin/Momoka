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
            await UntilAsync(() => page.IsReady && page.ViewModel.StillImage is not null && page.ViewModel.PlateImage is not null);
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
                Require(Math.Abs(box.Left - (box.Top - crumb.Bottom)) < 1 && Math.Abs(box.Left - 32) < 1,
                    $"{width} 封面左右/上方留白不等：左{box.Left:0.##}，上{box.Top - crumb.Bottom:0.##}");
                Require(Math.Abs(prose.Left - box.Left) < 1, $"{width} 剧情未与封面左沿对齐");
                readings.Add($"{width}: 左距={box.Left:0.##}，上距={box.Top - crumb.Bottom:0.##}，封面顶={box.Top:0.##}，字底={crumb.Bottom:0.##}，头部顶={hero.Top:0.##}，剧情左={prose.Left:0.##}");
                if (visit++ == 0)
                {
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
                        Require(old.Top > box.Top + 10, "新的封面没有比旧版上移");
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
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-cover-gap.txt"), readings);
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
        }
    }
}
