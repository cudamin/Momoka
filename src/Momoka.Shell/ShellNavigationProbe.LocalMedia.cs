using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.Services;
using Momoka.Shell.Views;
using Momoka.Updates;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LocalMediaAsync()
    {
        using var server = new LocalMediaProbeServer();
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        var directory = Path.Combine(_options.Paths.Root, "local-media-fixture");
        var source = Path.Combine(directory, "source");
        Directory.CreateDirectory(source);
        var video = Path.Combine(source, "Example.2026.mkv");
        const string original = "anonymous probe video bytes";
        await File.WriteAllTextAsync(video, original);
        var settings = fixture.Settings.LocalMedia;
        settings.Provider = "moviepilot";
        await ConnectMoviePilotAsync(fixture);
        using var coordinator = new LocalLibraryCoordinator(fixture.SettingsService, PassthroughSecretProtector.Instance,
            fixture.Services.GetRequiredService<MoviePilotService>(), new AppPaths(directory),
            new UiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()!));
        // SettingsOpeningAsync 的同一隔离依赖：空字体目录、假设备与拒绝播放/联网的服务。
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var paths = new AppPaths(directory);
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("本机整理页面检查禁止播放"),
            new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders),
                paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        using var settingsServices = new ServiceCollection()
            .AddSingleton(new ShaderStaging(fixture.Settings)).AddSingleton(fonts)
            .AddSingleton(new AudioDeviceCatalogue(() => null)).AddSingleton(playback)
            .AddSingleton(new MoviePilotCredentials(PassthroughSecretProtector.Instance))
            .AddSingleton(new UpdateClient(new RejectNetwork())).BuildServiceProvider();
        var services = new LocalMediaSettingsProbeServices(settingsServices, new LocalLibrariesProbeServices(fixture.Services, coordinator));
        var frame = new Frame { Margin = new Thickness(24, 42, 24, 18) };
        _root.Children.Add(frame);
        var topMost = _window!.TopMost;
        // 保持本次短检查窗口置顶，避免每张截图之间恢复z序后切换弹窗抢走下张截图。
        _window.TopMost = true;
        try
        {
            // 0.3.3 起设置页不再列出「本机整理」入口；页面本身仍保留，探针直接导航到页面核对内部布局。
            frame.Navigate(typeof(LocalMediaPage), new LocalMediaRequest(services));
            await UntilAsync(() => frame.Content is LocalMediaPage);
            var page = (LocalMediaPage)frame.Content;
            await LayoutAsync(page);
            var vm = page.ViewModel;
            Require(vm.Libraries.Count == 0, "未保存的目录不应自动占一行");
            vm.NewLibrary();
            var library = vm.Libraries.Single();
            library.Name = "示例媒体库";
            library.Directory = source;
            await vm.SaveLibraryAsync(library);
            Require(library.Saved && coordinator.Libraries.Count == 1, "目录没有按行保存为媒体库");
            await LayoutAsync(page);
            await vm.ScanAsync();
            Require(vm.Rows.Count == 1 && vm.Rows[0].Ready && vm.CanApply, "匿名电影没有生成原地入库计划");
            Require(File.Exists(video) && await File.ReadAllTextAsync(video) == original, "扫描改动了源文件");
            var metadataRoot = paths.LocalMediaMetadataDirectory;
            Require(!Directory.Exists(Path.Combine(directory, "target")), "不应再创建目标链接目录");
            var layoutReadings = new List<object>();
            foreach (var (width, screenshotSize) in new[] { (1100, "1100"), (760, "760"), (600, "600"), (1100, "restored-1100") })
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (_window.IsMaximized) _window.ToggleMaximize();
                    ResizeProbeClient(width, 860);
                    var deadline = Environment.TickCount64 + 2500;
                    while (Environment.TickCount64 < deadline)
                    {
                        _root.UpdateLayout();
                        if (Math.Abs(_root.ActualWidth - width) < 1) break;
                        await Task.Delay(16);
                    }
                    if (Math.Abs(_root.ActualWidth - width) < 1) break;
                }
                Require(Math.Abs(_root.ActualWidth - width) < 1, $"窗口实际宽度 {_root.ActualWidth} 未达到 {width}；客户区 {_window.ClientSize}，缩放 {_root.XamlRoot.RasterizationScale}");
                await LayoutAsync(page);
                Require(Get<TextBox>(page, "LocalMediaMetadataDirectory").ActualWidth > 120, "窄窗元数据目录输入被挤出");
                var list = Get<ItemsRepeater>(page, "LocalMediaLibraryList");
                Require(list.ActualWidth > 200, "目录列表没有展开");
                Require(vm.ProviderIndex == 1 && vm.Libraries.Count == 1 && vm.Libraries[0].Directory == source,
                    "宽窄布局切换改变了识别方式或目录输入");
                layoutReadings.Add(new { WindowWidth = width, ContentWidth = list.ActualWidth, Restored = screenshotSize.StartsWith("restored", StringComparison.Ordinal) });
                await SaveProbeScreenAsync($"local-media-preview-{screenshotSize}-screen.png");
                var scroll = Get<ScrollViewer>(page, "LocalMediaScroll");
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await UntilAsync(() => Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1);
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"local-media-actions-{screenshotSize}-screen.png");
                scroll.ChangeView(null, 0, null, true);
                await UntilAsync(() => scroll.VerticalOffset < 1);
                await LayoutAsync(page);
            }
            await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "local-media-layout.json"), JsonSerializer.Serialize(layoutReadings, new JsonSerializerOptions { WriteIndented = true }));
            vm.Confirm = _ => Task.FromResult(false);
            await vm.ApplyAsync();
            Require(File.Exists(video) && await File.ReadAllTextAsync(video) == original, "取消确认后改动了源文件");
            Require(!Directory.Exists(metadataRoot) || !Directory.EnumerateFiles(metadataRoot, "*.nfo", SearchOption.AllDirectories).Any(), "取消确认后仍写入元数据");
            vm.Confirm = _ => Task.FromResult(true);
            await vm.ApplyAsync();
            Require(File.Exists(video) && await File.ReadAllTextAsync(video) == original, "入库改动了源文件");
            Require(Directory.Exists(metadataRoot) && Directory.EnumerateFiles(metadataRoot, "*.nfo", SearchOption.AllDirectories).Any(),
                "确认后没有把 NFO 写入元数据目录");
            vm.ProviderIndex = 0;
            await LayoutAsync(page);
            Require(vm.IsMomoka && !vm.CanScan, "无TMDB凭据仍开放识别或错误依赖MP");
            Require(Get<StackPanel>(page, "LocalMediaTmdbCredentials").Visibility == Visibility.Visible
                && Get<TextBlock>(page, "LocalMediaMpNotice").Visibility == Visibility.Collapsed, "识别凭据和来源说明没有整组切换");
            await SaveProbeScreenAsync("local-media-momoka-screen.png");
            vm.ProviderIndex = 1;
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => !vm.MpAvailable);
            Require(!vm.CanScan && !vm.CanApply, "MP断线仍能执行");
            await ConnectMoviePilotAsync(fixture);
            await UntilAsync(() => vm.MpAvailable);
            Require(server.Writes == 0, "本机整理发出了服务器写请求");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            _window.TopMost = topMost;
            await playback.ShutdownAsync();
        }
    }

    private sealed class LocalMediaSettingsProbeServices(IServiceProvider settings, IServiceProvider fixture) : IServiceProvider
    {
        public object? GetService(Type type) => settings.GetService(type) ?? fixture.GetService(type);
    }

    private sealed class LocalMediaProbeServer : HttpMessageHandler
    {
        internal int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Task.FromResult(Json(new { access_token = "fixture-token" }));
            if (request.Method != HttpMethod.Get) { Writes++; throw new InvalidOperationException("本机整理探针禁止服务器写入"); }
            var media = new { title = "示例电影", year = "2026", type = "电影", media_source = "themoviedb", media_id = "42", tmdb_id = 42 };
            return Task.FromResult(path switch
            {
                "/api/v1/dashboard/system" => Json(new { version = "v3.0.8" }),
                "/api/v1/media/recognize" => Json(new { meta_info = new { }, media_info = media }),
                "/api/v1/media/42" => Json(media),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        }
    }
}
