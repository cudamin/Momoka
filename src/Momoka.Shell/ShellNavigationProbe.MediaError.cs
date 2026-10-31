using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotMediaErrorAsync()
    {
        var app = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle));
        var originalBounds = new Windows.Graphics.RectInt32(app.Position.X, app.Position.Y, app.Size.Width, app.Size.Height);
        using var server = new SubscriptionProbeServer { DetailFailure = "missing" };
        var (_, service) = SubscriptionService(server);
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First(item => item.IsSeries);
        using var fixture = CreateFixture();
        var page = new MoviePilotSubscriptionPage();
        _root.Children.Add(page);
        try
        {
            page.Attach(new(service, subscription, Launcher: fixture.Launcher));
            var vm = page.MediaViewModel;
            await UntilAsync(() => vm.IsReady && page.ViewModel.IsReady);
            await LayoutAsync(page);
            Require(vm.DetailError == "未识别到媒体信息。" && vm.Detail is null, "缺失身份未显示未识别状态");
            var content = Get<Grid>(page, "DetailContent");
            var error = Get<ScrollViewer>(page, "DetailErrorState");
            Require(content.Visibility == Visibility.Collapsed && error.Visibility == Visibility.Visible, "半成品媒体与错误页同时显示");
            Require(!Get<ProgressRing>(page, "DetailLoadingRing").IsActive, "错误页背后仍在运行动画");
            var scale = _root.XamlRoot?.RasterizationScale ?? 1;
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                ResizeInspect((int)Math.Round(1040 * scale), (int)Math.Round(760 * scale));
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"mp-media-unrecognized-{theme.Id}-1040-screen.png");
            }
            ResizeInspect((int)Math.Round(520 * scale), (int)Math.Round(760 * scale));
            await LayoutAsync(page);
            await SaveProbeScreenAsync("mp-media-unrecognized-520-screen.png");
            server.DetailFailure = "";
            InvokeSubscriptionButton(Get<Button>(page, "RetryMediaDetail"));
            await UntilAsync(() => vm.IsReady && vm.Detail is not null);
            await LayoutAsync(page);
            Require(content.Visibility == Visibility.Visible && error.Visibility == Visibility.Collapsed, "重试成功后没有恢复媒体详情");
            server.DetailFailure = "mismatch";
            await vm.ReloadAsync();
            Require(vm.Detail is null && vm.DetailError.Contains("身份不匹配", StringComparison.Ordinal), "显式身份冲突被当作缺失或正常媒体");
            server.DetailFailure = "network";
            await vm.ReloadAsync();
            Require(vm.DetailError.Length > 0 && vm.DetailError != "未识别到媒体信息。", "网络错误被误报成未识别");
            server.DetailFailure = "";
            await vm.ReloadAsync();
            Require(vm.Detail is not null && vm.DetailError.Length == 0, "连接失败后重试不能恢复");
            server.FailSubscriptionRead = true;
            await vm.ReloadAsync();
            Require(vm.Detail is not null && vm.DetailError.Length == 0 && vm.NoticeOpen && !vm.CanInteract
                && vm.SubscribeLabel == "订阅状态未知", "附属读取失败不应丢失已验证媒体或伪造未订阅");
            Require(server.Writes.Count == 0 && fixture.Actions.Plays == 0, "错误页重试不应修改订阅或播放");
        }
        finally
        {
            page.Release(); _root.Children.Remove(page);
            app.MoveAndResize(originalBounds);
            ThemeHost.Apply(_options.Theme ?? "midnight");
        }
    }
}
