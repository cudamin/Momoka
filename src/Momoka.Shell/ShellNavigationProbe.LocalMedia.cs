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
        await File.WriteAllTextAsync(Path.Combine(source, "Example.2026.mkv"), "anonymous probe video bytes");
        var settings = fixture.Settings.LocalMedia;
        settings.SourceDirectory = source;
        settings.TargetDirectory = Path.Combine(directory, "target");
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
            await vm.ScanAsync();
            Require(vm.Rows.Count == 1 && vm.Rows[0].Ready && vm.CanApply, "匿名电影没有生成实际链接计划");
            var expectedProvider = vm.ProviderIndex;
            var expectedMethod = vm.MethodIndex;
            var expectedTarget = vm.TargetDirectory;
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
                Require(Get<TextBox>(page, "LocalMediaSource").ActualWidth > 120, "窄窗源目录输入被挤出");
                foreach (var cardName in new[] { "LocalLibraryCard", "LocalMediaRecognitionCard", "LocalMediaDirectoriesCard", "LocalLibraryAutomationCard" })
                {
                    var card = Get<Grid>(page, cardName);
                    Require(card.ActualWidth > 200 && card.Children.OfType<StackPanel>().Any(header => header.Children.OfType<FontIcon>().Any()),
                        $"{cardName} 没有展开或图标缺失");
                    Require(card.Children.OfType<StackPanel>().Any(content => Grid.GetRow(content) == 1), "卡片内容没有在标题下展开");
                    var cardPosition = card.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
                    Require(cardPosition.X >= 0 && cardPosition.X + card.ActualWidth <= page.ActualWidth + 1, "卡片超出页面边界");
                }
                foreach (var pickerName in new[] { "LocalMediaPickSource", "LocalMediaPickTarget" })
                {
                    var picker = Get<Button>(page, pickerName);
                    var position = picker.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
                    Require(position.X >= 0 && position.X + picker.ActualWidth <= page.ActualWidth + 1, "目录选择按钮被长路径挤出");
                }
                var modes = Get<Grid>(page, "LocalMediaModes");
                var method = Get<ComboBox>(page, "LocalMediaMethod");
                var wide = modes.ActualWidth >= 520;
                Require(width != 600 || !wide, $"600 DIP窗口没有覆盖窄态，内容实际宽度 {modes.ActualWidth}");
                Require(width != 1100 || wide, $"1100 DIP窗口没有覆盖宽态，内容实际宽度 {modes.ActualWidth}");
                Require(Grid.GetRow(method) == (wide ? 0 : 1) && Grid.GetColumn(method) == (wide ? 1 : 0)
                    && method.Margin.Top == (wide ? 0 : 12)
                    && (wide ? modes.ColumnDefinitions[1].ActualWidth > 100 : modes.ColumnDefinitions[1].ActualWidth == 0),
                    "识别和整理选项没有按内容宽度折行或恢复");
                Require(vm.ProviderIndex == expectedProvider && vm.MethodIndex == expectedMethod
                    && vm.SourceDirectory == source && vm.TargetDirectory == expectedTarget,
                    "宽窄布局切换改变了识别方式、整理方式或目录输入");
                layoutReadings.Add(new { WindowWidth = width, ContentWidth = modes.ActualWidth, MethodRow = Grid.GetRow(method), MethodColumn = Grid.GetColumn(method), Restored = screenshotSize.StartsWith("restored", StringComparison.Ordinal) });
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
            var applying = vm.ApplyAsync();
            await UntilAsync(() => Get<ContentDialog?>(page, "_confirmation") is not null);
            var confirmation = Get<ContentDialog>(page, "_confirmation");
            await LayoutAsync(confirmation);
            await SaveProbeScreenAsync("local-media-confirm-screen.png");
            confirmation.Hide();
            await applying;
            await SettleAsync();
            Require(!Directory.Exists(settings.TargetDirectory), "取消确认后仍创建文件");
            Get<ComboBox>(page, "LocalMediaMethod").SelectedIndex = 2;
            await LayoutAsync(page);
            Require(!vm.NeedsTarget && !vm.CanApply && vm.SourceDirectory == source, "直接改名未收起目标目录或旧预览未撤销");
            Require(Get<Grid>(page, "LocalMediaTargetRow").Visibility == Visibility.Collapsed
                && Get<Grid>(page, "LocalMediaDirectoriesCard").Visibility == Visibility.Visible,
                "直接改名没有完整隐藏目标行或误隐藏目录卡片");
            Get<ComboBox>(page, "LocalMediaMethod").SelectedIndex = 1;
            Require(vm.NeedsTarget && vm.TargetDirectory == settings.TargetDirectory, "软链接未恢复目标目录偏好");
            Get<ComboBox>(page, "LocalMediaProvider").SelectedIndex = 0;
            await LayoutAsync(page);
            Require(vm.IsMomoka && !vm.CanScan, "无TMDB凭据仍开放识别或错误依赖MP");
            Require(Get<StackPanel>(page, "LocalMediaTmdbCredentials").Visibility == Visibility.Visible
                && Get<TextBlock>(page, "LocalMediaMpNotice").Visibility == Visibility.Collapsed, "识别凭据和来源说明没有整组切换");
            await SaveProbeScreenAsync("local-media-momoka-screen.png");
            Get<ComboBox>(page, "LocalMediaProvider").SelectedIndex = 1;
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => !vm.MpAvailable);
            Require(!vm.CanScan && !vm.CanApply, "MP断线仍能执行");
            await ConnectMoviePilotAsync(fixture);
            await UntilAsync(() => vm.MpAvailable);
            Get<ComboBox>(page, "LocalMediaMethod").SelectedIndex = 0;
            Require(vm.CanScan && vm.TargetDirectory == settings.TargetDirectory, "连接恢复未恢复输入");
            Require(vm.Libraries.Count == 1 && vm.SelectedLibrary is not null, "旧目录没有成为一个本地媒体库");
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
