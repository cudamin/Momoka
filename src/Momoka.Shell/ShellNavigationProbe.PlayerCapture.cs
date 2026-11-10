using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Playback;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task PlayerCaptureDialogAsync()
    {
        var topMost = _window!.TopMost;
        _window.TopMost = true;
        try { await CheckPlayerCaptureDialogAsync(); }
        finally { _window.TopMost = topMost; }
    }

    private async Task CheckPlayerCaptureDialogAsync()
    {
        var largeInput = Path.Combine(_options.Paths.LogDirectory, "player-capture-4k-fixture.png");
        await File.WriteAllBytesAsync(largeInput, await FixtureImageAsync(3840, 2160));
        var largePicture = await PlayerCaptureController.LoadPictureAsync(largeInput);
        Require(largePicture.PixelWidth == 3840 && largePicture.PixelHeight == 2160,
            "4K截图预览缩小后丢失原始像素尺寸。");
        File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "player-capture-4k-metadata.txt"),
            $"Original={largePicture.PixelWidth}x{largePicture.PixelHeight}; DecodePixelWidth={largePicture.DecodePixelWidth}");
        var input = Path.Combine(_options.Paths.LogDirectory, "player-capture-fixture.png");
        await File.WriteAllBytesAsync(input, await FixtureImageAsync(1280, 720));
        var picture = await PlayerCaptureController.LoadPictureAsync(input);
        foreach (var width in new[] { 1100, 760 })
        {
            ResizeInspect(width, 900);
            var attempts = 0;
            var dialog = new PlayerCaptureDialog(picture, true, _ =>
            {
                attempts++;
                throw new PlayerToolException("离线保存失败示例");
            }) { XamlRoot = _root.XamlRoot };
            Exception? error = null;
            dialog.Opened += async (_, _) =>
            {
                try
                {
                    await Task.Delay(250);
                    dialog.UpdateLayout();
                    Require(dialog.Crop == new PlayerImageCrop(0, 504, 1280, 216), "字幕默认区域没有按原图像素计算");
                    var x = (NumberBox)dialog.FindName("CaptureX");
                    var y = (NumberBox)dialog.FindName("CaptureY");
                    x.Value = 20;
                    y.Value = 100;
                    ((Button)dialog.FindName("CaptureWholeImage")).Focus(FocusState.Programmatic);
                    Require(dialog.Crop is { X: 20, Y: 100 }, "真实数字控件未更新裁切区域");
                    var body = (FrameworkElement)dialog.FindName("CaptureBody");
                    Require(body.ActualWidth > 0 && body.ActualWidth <= width, "裁切内容超出窄窗宽度");
                    await SaveSettledFrameAsync(dialog, $"player-capture-{width}-render.png");
                    await SaveCaptureDialogScreenAsync($"player-capture-{width}-screen.png");
                    Require(attempts == 0, "显示和调整裁切区域不应保存图片");
                    await dialog.SaveAsync();
                    var failure = (InfoBar)dialog.FindName("CaptureError");
                    Require(attempts == 1 && failure.IsOpen && failure.Visibility == Visibility.Visible,
                        "保存失败没有留在当前弹窗显示");
                    failure.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                    await Task.Delay(350);
                    var scroll = (ScrollViewer)dialog.FindName("CaptureScroll");
                    var bounds = failure.TransformToVisual(scroll).TransformBounds(new Windows.Foundation.Rect(0, 0, failure.ActualWidth, failure.ActualHeight));
                    Require(bounds.Height > 0 && bounds.Y >= 0 && bounds.Bottom <= scroll.ViewportHeight + 1,
                        "保存失败提示没有进入截图可见区域。");
                    await SaveCaptureDialogScreenAsync($"player-capture-failed-{width}-screen.png");
                    await dialog.SaveAsync();
                    Require(attempts == 2 && dialog.SavedPath is null, "保存失败不能重试或误记成功");
                }
                catch (Exception caught) { error = caught; }
                finally { dialog.Hide(); }
            };
            await dialog.ShowAsync();
            if (error is not null) throw error;
            Require(dialog.SavedPath is null && attempts == 2, "取消裁切重复执行保存");
        }
        ResizeInspect(1420, 980);
    }

    private async Task InspectPlayerCaptureAsync()
    {
        var input = Path.Combine(_options.Paths.LogDirectory, "player-capture-inspect-fixture.png");
        await File.WriteAllBytesAsync(input, await FixtureImageAsync(1280, 720));
        var picture = await PlayerCaptureController.LoadPictureAsync(input);
        var dialog = new PlayerCaptureDialog(picture, true, _ => throw new PlayerToolException("离线保存失败示例，请重试或取消。"))
            { XamlRoot = _root.XamlRoot };
        await dialog.ShowAsync();
    }

    private async Task SaveCaptureDialogScreenAsync(string name)
    {
        await Task.Delay(300);
        if (_window is null) throw new InvalidOperationException("裁切探针窗口不存在。");
        var point = new Interop.NativePoint();
        Require(Interop.Native.GetClientRect(_window.Handle, out var client)
            && Interop.Native.ClientToScreen(_window.Handle, ref point), "裁切探针坐标不可用。");
        point.X += client.Width / 2;
        point.Y += client.Height / 2;
        var target = Interop.Native.GetAncestor(Interop.Native.WindowFromPoint(point), 2);
        Interop.Native.GetWindowThreadProcessId(target, out var process);
        Require(process == (uint)Environment.ProcessId, "裁切探针实际像素被其他窗口遮挡。");
        Require(target == _window.Handle || Interop.Native.GetWindow(target, 4) == _window.Handle,
            "实际像素窗口不属于裁切探针宿主。");
        // ContentDialog 可以有自己的 Popup HWND，主窗口四角会因此被同进程遮罩覆盖。
        // 核对前台真实所属进程后拍它，不能把主窗口的可见性守卫放宽成允许任意遮挡。
        await CompositionPlaybackProbe.SaveWindowScreenAsync(target, Path.Combine(_options.Paths.LogDirectory, name));
    }

    private async Task PlayerToolsSettingsAsync()
    {
        using var fixture = CreateFixture();
        using var moviePilot = new Momoka.MoviePilot.MoviePilotClient(new RejectNetwork());
        var paths = new Momoka.Infrastructure.AppPaths(Path.Combine(_options.Paths.Root, "player-tools-settings"));
        paths.EnsureCreated();
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        _root.Children.Add(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = ViewModels.SettingsViewModel.PlayerToolsCategory;
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                foreach (var title in new[] { "视频导出", "字幕同步", "AI 字幕" })
                {
                    var card = page.ViewModel.Sections.Single(section => section.Title == title);
                    Require(card.SectionVisibility == Visibility.Visible, "播放工具设置卡不可达：" + title);
                    var target = Descendants(page).OfType<FrameworkElement>().First(element => ReferenceEquals(element.DataContext, card)
                        && element.ActualWidth > 0 && element.ActualHeight > 0);
                    target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                    await Task.Delay(350);
                    await SaveProbeScreenAsync($"player-tools-{title}-{width}-screen.png");
                }
            }
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }
    }
}
