using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task PlayerTransportAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
        fixture.Settings.Danmaku.Enabled = true;
        fixture.Settings.Danmaku.AppId = "offline-transport-app";
        fixture.Settings.Danmaku.ProtectedAppSecret = "offline-transport-secret";
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "player-transport"));
        paths.EnsureCreated();
        using var client = new DandanPlayClient(fixture.Settings, new(PassthroughSecretProtector.Instance),
            Path.Combine(paths.Root, "cache"), new RejectNetwork());
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("控制条检查禁止真实播放"),
            new PlaybackPlanner(fixture.Settings, shaders), allowPlayback: false);
        var vm = new PlayerViewModel(playback, fixture.SettingsService, fixture.Session, fixture.Images,
            shaders, fixture.Dispatcher, client);
        var page = new PlayerPage();
        page.Attach(vm, new ShellPage(), _window!);
        _root.Children.Add(page);
        try
        {
            ResizeProbeClient(1100, 760);
            page.ShowChromeForShot(null);
            await LayoutAsync(page);
            var result = page.ProbeTransportLayout();
            Require(result.Ok, result.Detail);
            var fullscreen = Get<Button>(page, "FullscreenButton");
            var width = fullscreen.Width;
            var minimum = fullscreen.MinWidth;
            fullscreen.Width = fullscreen.MinWidth = 1500;
            try { Require(!page.ProbeTransportLayout().Ok, "宽窄检查没有拒绝故意溢出的全屏按钮"); }
            finally { fullscreen.Width = width; fullscreen.MinWidth = minimum; }
            result = page.ProbeTransportLayout();
            Require(result.Ok, "负向夹具还原后布局仍不正常：" + result.Detail);
            _window!.Fullscreen = true;
            page.ApplyChromeScale();
            result = page.ProbeTransportLayout();
            Require(result.Ok, "大档控件在窄窗口溢出：" + result.Detail);
            _window!.Fullscreen = false;
            page.ApplyChromeScale();
        }
        finally
        {
            _window!.Fullscreen = false;
            _root.Children.Remove(page);
        }
    }
}
