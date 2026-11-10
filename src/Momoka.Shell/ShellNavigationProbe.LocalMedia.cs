using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Infrastructure;
using Momoka.Shell.Platform;
using Momoka.Shell.Services;
using Momoka.Shell.Views;

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
        var services = new LocalLibrariesProbeServices(fixture.Services, coordinator);
        var frame = new Frame { Margin = new Thickness(24, 42, 24, 18) };
        _root.Children.Add(frame);
        var topMost = _window!.TopMost;
        // 保持本次短检查窗口置顶，避免每张截图之间恢复z序后切换弹窗抢走下张截图。
        _window.TopMost = true;
        try
        {
            frame.Navigate(typeof(LocalMediaPage), new LocalMediaRequest(services));
            var page = (LocalMediaPage)frame.Content;
            await LayoutAsync(page);
            var vm = page.ViewModel;
            await vm.ScanAsync();
            Require(vm.Rows.Count == 1 && vm.Rows[0].Ready && vm.CanApply, "匿名电影没有生成实际链接计划");
            foreach (var width in new[] { 1100, 760 })
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
                await SaveProbeScreenAsync($"local-media-preview-{width}-screen.png");
            }
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
            Get<ComboBox>(page, "LocalMediaMethod").SelectedIndex = 1;
            Require(vm.NeedsTarget && vm.TargetDirectory == settings.TargetDirectory, "软链接未恢复目标目录偏好");
            Get<ComboBox>(page, "LocalMediaProvider").SelectedIndex = 0;
            await LayoutAsync(page);
            Require(vm.IsMomoka && !vm.CanScan, "无TMDB凭据仍开放识别或错误依赖MP");
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
        }
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
