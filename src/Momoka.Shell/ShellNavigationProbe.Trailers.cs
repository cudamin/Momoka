using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task DetailTrailersAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Ui.EnableImmersive = true;
        await fixture.SelectAsync();
        var poster = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync(1600, 900);
        var movie = new EmbyItem
        {
            Id = "movie",
            Name = "预告片 · 离线测试电影",
            Type = EmbyItemType.Movie,
            Overview = "选择服务器提供的预告片，观看前先了解这部影片。",
            LocalTrailerCount = 1,
            RemoteTrailers = [new() { Name = "官方预告", Url = "https://trailers.invalid/watch" }],
            ImageTags = new() { ["Primary"] = "fixture" },
            BackdropImageTags = ["fixture"],
            MediaSources = [new() { Id = "movie-file", Container = "mkv" }]
        };
        var local = new EmbyItem { Id = "trailer", Name = "院线预告", Type = EmbyItemType.Trailer };
        var fail = false;
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/LocalTrailers", StringComparison.Ordinal))
                return Task.FromResult(fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(new[] { local }));
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(path.Contains("/Primary", StringComparison.Ordinal) ? poster : backdrop) });
            if (path.EndsWith("/Items/movie", StringComparison.Ordinal)) return Task.FromResult(Json(movie));
            if (path.EndsWith("/Similar", StringComparison.Ordinal)) return Task.FromResult(Items(0, 0, "similar"));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var host = new Grid { Margin = new Thickness(0, 68, 0, 0) };
        var frame = new Frame();
        host.Children.Add(frame);
        _root.Children.Add(host);
        try
        {
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, movie));
            var page = (DetailPage)frame.Content;
            page.AttachWindow(_window!);
            await UntilAsync(() => page.IsReady && page.ViewModel.StillImage is not null);
            foreach (var width in new[] { 1280, 760 })
            {
                await ResizeResponsiveAsync(page, width, 900);
                await LayoutAsync(page);
                var button = Get<Button>(page, width < 820 ? "CompactTrailerButton" : "ColumnTrailerButton");
                Require(button.Visibility == Visibility.Visible && button.ActualWidth > 0 && button.ActualHeight > 0,
                    $"{width}：预告片入口不可见");
                await SaveProbeScreenAsync($"detail-trailers-{width}-screen.png");
                await page.ShowTrailersAsync(button);
                var menu = Get<MenuFlyout>(page, "_trailerMenu");
                Require(menu.Items.OfType<MenuFlyoutItem>().Count() == 2, "本地和远程预告没有同时列出");
                Require(menu.Items.OfType<MenuFlyoutItem>().All(row => !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(row))),
                    "预告选项缺少自动化句柄");
                Require(menu.Items.OfType<MenuFlyoutItem>().Any(row => row.Text.Contains("在 Momoka 播放", StringComparison.Ordinal))
                    && menu.Items.OfType<MenuFlyoutItem>().Any(row => row.Text.Contains("在浏览器打开", StringComparison.Ordinal)),
                    "预告目的地未标明");
                await SettleAsync();
                await SaveProbeScreenAsync($"detail-trailers-menu-{width}-screen.png");
                menu.Hide();
            }
            var narrow = Get<Button>(page, "CompactTrailerButton");
            fail = true;
            await page.ShowTrailersAsync(narrow);
            var failed = Get<MenuFlyout>(page, "_trailerMenu");
            Require(failed.Items.OfType<MenuFlyoutItem>().Any(row => row.Text == "重新加载")
                && failed.Items.OfType<MenuFlyoutItem>().Any(row => row.Text.Contains("在浏览器打开", StringComparison.Ordinal)),
                "读取失败未保留远程链接和重试");
            await SettleAsync();
            await SaveProbeScreenAsync("detail-trailers-failed-screen.png");
            fail = false;
            var retry = failed.Items.OfType<MenuFlyoutItem>().Single(row => row.Text == "重新加载");
            new MenuFlyoutItemAutomationPeer(retry).Invoke();
            await UntilAsync(() => Get<MenuFlyout?>(page, "_trailerMenu") is { IsOpen: true } reopened
                && !ReferenceEquals(reopened, failed) && reopened.Items.OfType<MenuFlyoutItem>().Count() == 2);
            var recovered = Get<MenuFlyout>(page, "_trailerMenu");
            Require(recovered.Items.OfType<MenuFlyoutItem>().Any(row => row.Text.Contains("在 Momoka 播放", StringComparison.Ordinal)),
                "重试没有重新读取本地预告");
            recovered.Hide();
            movie.LocalTrailerCount = 0;
            movie.RemoteTrailers = [];
            await page.ViewModel.ReloadAsync();
            await LayoutAsync(page);
            Require(narrow.Visibility == Visibility.Collapsed, "没有资源时入口未隐藏");
            movie.RemoteTrailers = [new() { Url = "https://trailers.invalid/returned" }];
            await page.ViewModel.ReloadAsync();
            await LayoutAsync(page);
            Require(narrow.Visibility == Visibility.Visible, "服务器重新提供资源后入口未恢复");
            await page.ShowTrailersAsync(narrow);
            var stale = Get<MenuFlyout>(page, "_trailerMenu");
            page.Release();
            Require(!stale.IsOpen && page.ViewModel.TrailerVisibility == Visibility.Collapsed, "离页未关闭预告菜单");
            // A deliberately wrong expectation must fail through the same assertion helper.
            var rejected = false;
            try { Require(page.ViewModel.TrailerVisibility == Visibility.Visible, "负向对照：取消后不可显示"); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "预告显隐断言没有拒绝反例");
        }
        finally
        {
            if (frame.Content is IShellContent content) content.Release();
            _root.Children.Remove(host);
        }
    }
}
