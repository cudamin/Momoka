using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;
using Momoka.Shell.Services;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LocalLibrariesAsync()
    {
        using var server = new LocalLibrariesProbeServer { Image = await FixtureImageAsync(320, 480) };
        using var fixture = CreateFixture(server);
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal) || path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var root = Path.Combine(_options.Paths.Root, "multiple-local-libraries");
        var sourceA = Path.Combine(root, "movies");
        var sourceB = Path.Combine(root, "series");
        Directory.CreateDirectory(sourceA); Directory.CreateDirectory(sourceB);
        await File.WriteAllTextAsync(Path.Combine(sourceA, "Example.2026.mkv"), "existing fake media");
        var dispatcher = new UiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()!);
        using var coordinator = new LocalLibraryCoordinator(fixture.SettingsService, PassthroughSecretProtector.Instance,
            fixture.Services.GetRequiredService<MoviePilotService>(), new AppPaths(root), dispatcher);
        var services = new LocalLibrariesProbeServices(fixture.Services, coordinator);
        await coordinator.SaveAsync(new() { Id = "movies", Name = "本地电影", SourceDirectory = sourceA, TargetDirectory = Path.Combine(root, "movie-library"), Provider = "moviepilot", MonitorEnabled = true });
        await coordinator.SaveAsync(new() { Id = "series", Name = "本地剧集", SourceDirectory = sourceB, TargetDirectory = Path.Combine(root, "series-library"), Provider = "moviepilot", MonitorEnabled = true });
        var shell = new ShellPage();
        shell.Attach(services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        var topMost = _window!.TopMost;
        _window.TopMost = true;
        try
        {
            await coordinator.StartAsync();
            // 写入发生在监控启动之后，必须由真实目录事件引发后续入库。
            await File.WriteAllTextAsync(Path.Combine(sourceB, "Example.S01E02.mkv"), "new fake episode");
            var libraryDeadline = Environment.TickCount64 + 15000;
            while (Environment.TickCount64 < libraryDeadline && !(coordinator.EntriesFor("movies").Any(entry => entry.IsInLibrary)
                && coordinator.EntriesFor("series").Any(entry => entry.IsInLibrary))) await Task.Delay(50);
            Require(coordinator.EntriesFor("movies").Any(entry => entry.IsInLibrary) && coordinator.EntriesFor("series").Any(entry => entry.IsInLibrary),
                "已有或新增文件未自动入库：" + coordinator.StatusFor("movies") + "；" + coordinator.StatusFor("series"));
            Require(coordinator.EntriesFor("movies").All(entry => entry.Complete) && coordinator.EntriesFor("series").All(entry => entry.Complete), "自动NFO/图片没有全部完成");
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.Pages.Content is HomePage page && page.IsReady);
            var home = (HomePage)shell.Pages.Content;
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.Count(card => card.Item.Id.StartsWith("local-library:", StringComparison.Ordinal)) == 2);
            await File.WriteAllTextAsync(Path.Combine(sourceB, "Example.S01E03.mkv"), "another new episode while home is open");
            var updateDeadline = Environment.TickCount64 + 15000;
            while (Environment.TickCount64 < updateDeadline && coordinator.EntriesFor("series").Count(entry => entry.IsInLibrary) < 2) await Task.Delay(50);
            Require(coordinator.EntriesFor("series").Count(entry => entry.IsInLibrary) == 2, "首页打开后的新文件没有自动入库");
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.SingleOrDefault(card => card.Item.Id == "local-library:series")?.Subtitle == "本地 · 2 项");
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 860);
                await UntilAsync(() => { _root.UpdateLayout(); return Math.Abs(_root.ActualWidth - width) < 1; });
                await LayoutAsync(home);
                await SaveProbeScreenAsync($"local-libraries-home-{width}-screen.png");
            }
            var localCard = home.ViewModel.LibraryShelf!.Cards.Single(card => card.Item.Id == "local-library:movies");
            home.ViewModel.Open(localCard);
            await UntilAsync(() => shell.Pages.Content is LocalLibraryPage);
            await LayoutAsync((LocalLibraryPage)shell.Pages.Content);
            await SaveProbeScreenAsync("local-libraries-movies-screen.png");
            shell.GoTo("local-library:series");
            await LayoutAsync((LocalLibraryPage)shell.Pages.Content);
            await SaveProbeScreenAsync("local-libraries-series-screen.png");
            await coordinator.StopAsync();
            Require(server.Writes == 0, "自动本机整理向服务器提交了写操作");
        }
        finally
        {
            await coordinator.StopAsync();
            shell.Shutdown();
            _root.Children.Remove(shell);
            _window.TopMost = topMost;
        }
    }

    private sealed class LocalLibrariesProbeServices(IServiceProvider inner, LocalLibraryCoordinator coordinator) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(LocalLibraryCoordinator) ? coordinator : inner.GetService(type);
    }
    private sealed class LocalLibrariesProbeServer : HttpMessageHandler
    {
        internal byte[] Image { get; init; } = [];
        internal int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Task.FromResult(Json(new { access_token = "fixture-token" }));
            if (request.Method != HttpMethod.Get) { Writes++; throw new InvalidOperationException("禁止服务器媒体写入"); }
            var movie = new { title = "示例电影", year = "2026", type = "电影", media_source = "themoviedb", media_id = "42", tmdb_id = 42, poster_path = "https://artwork.invalid/poster.png", backdrop_path = "https://artwork.invalid/backdrop.png" };
            var series = new { title = "示例剧集", year = "2026", type = "电视剧", media_source = "themoviedb", media_id = "43", tmdb_id = 43, poster_path = "https://artwork.invalid/series.png" };
            if (path.EndsWith("system/cache/image", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Image) });
            object? data = path switch
            {
                "/api/v1/dashboard/system" => new { version = "v3.0.8" },
                "/api/v1/media/42" => movie,
                "/api/v1/media/43" => series,
                "/api/v1/tmdb/43/1" => new[] { new { id = 432, season_number = 1, episode_number = 2, name = "第二集", overview = "匿名剧集简介", air_date = "2026-11-01" }, new { id = 433, season_number = 1, episode_number = 3, name = "第三集", overview = "匿名剧集简介", air_date = "2026-11-08" } },
                "/api/v1/subscribe/" => Array.Empty<object>(),
                "/api/v1/subscribe" => Array.Empty<object>(),
                "/api/v1/history/transfer/capabilities" => new { },
                _ => null
            };
            if (path.EndsWith("media/recognize", StringComparison.Ordinal)) data = request.RequestUri.Query.Contains("S01E", StringComparison.Ordinal)
                ? new { meta_info = new { begin_season = 1, begin_episode = request.RequestUri.Query.Contains("S01E03", StringComparison.Ordinal) ? 3 : 2 }, media_info = (object)series }
                : new { meta_info = new { }, media_info = (object)movie };
            return Task.FromResult(data is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(data));
        }
    }
}
