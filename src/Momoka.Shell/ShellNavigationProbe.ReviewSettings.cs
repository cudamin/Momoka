using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task ReviewSettingsAvailabilityAsync(int width)
    {
        using var fixture = CreateFixture();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, $"review-settings-{width}"));
        paths.EnsureCreated();
        var settings = fixture.Settings;
        settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        settings.Mpv.SvpEnabled = false;
        settings.Mpv.CustomConfigEnabled = false;
        settings.Video.VideoSync = "audio";
        settings.Video.FrameInterpolationMode = FrameInterpolationMode.Off;
        settings.Danmaku.Enabled = true;
        settings.Danmaku.Show = true;
        settings.Danmaku.ShowInput = true;
        settings.Danmaku.Source = DanmakuSourceMode.DandanPlay;
        settings.Danmaku.DanmuApiBaseUrl = "https://review-danmaku.invalid/";
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        _root.Children.Add(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            ResizeProbeClient(width, 900);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "视频输出";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            AssertRow("基础输出", "视频同步", true);
            await SelectRow("补帧", "模式", "平滑插值（轻量）");
            Require(settings.Video.FrameInterpolationMode == FrameInterpolationMode.Smooth,
                "真实补帧下拉未保存平滑插值");
            AssertRow("基础输出", "视频同步", false);
            Require(settings.Video.VideoSync == "audio", "隐藏视频同步时丢失原偏好");
            await Capture("review-settings-smooth", "基础输出");
            await SelectRow("补帧", "模式", "关闭");
            Require(settings.Video.FrameInterpolationMode == FrameInterpolationMode.Off && settings.Video.VideoSync == "audio",
                "退出平滑插值没有恢复原视频同步偏好");
            AssertRow("基础输出", "视频同步", true);
            var sync = page.ViewModel.Sections.Single(section => section.Title == "基础输出").Rows
                .OfType<SettingChoiceRow>().Single(row => row.Label == "视频同步");
            var syncCombo = Descendants(page).OfType<ComboBox>()
                .Single(control => AutomationProperties.GetName(control) == "视频同步");
            Require(sync.Selected?.Label == "音频同步" && ReferenceEquals(syncCombo.SelectedItem, sync.Selected),
                "恢复的视频同步下拉没有显示保存的音频同步");
            await Capture("review-settings-sync-restored", "基础输出");

            page.SelectedCategory = "弹幕";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            AssertRow("显示与外观", "显示弹幕输入栏", true);
            await SelectRow("来源与聚合", "弹幕来源", "danmu_api（自己部署）");
            Require(settings.Danmaku.Source == DanmakuSourceMode.DanmuApi, "真实弹幕来源下拉未保存 danmu_api");
            AssertRow("显示与外观", "显示弹幕输入栏", false);
            Require(settings.Danmaku.ShowInput, "隐藏输入栏设置时清空了偏好");
            AssertRow("显示与外观", "显示弹幕", true);
            await Capture("review-settings-danmu-api", "显示与外观");
            await SelectRow("来源与聚合", "弹幕来源", "弹弹play");
            Require(settings.Danmaku.Source == DanmakuSourceMode.DandanPlay && settings.Danmaku.ShowInput,
                "恢复弹弹play后丢失输入栏偏好");
            AssertRow("显示与外观", "显示弹幕输入栏", true);
            var input = Descendants(page).OfType<ToggleSwitch>()
                .Single(control => AutomationProperties.GetName(control) == "显示弹幕输入栏");
            Require(input.IsOn, "恢复的真实输入栏开关没有显示原值");
            await Capture("review-settings-input-restored", "显示与外观");
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }

        async Task SelectRow(string title, string label, string choice)
        {
            var row = page.ViewModel.Sections.Single(section => section.Title == title).Rows
                .OfType<SettingChoiceRow>().Single(item => item.Label == label);
            var combo = Descendants(page).OfType<ComboBox>()
                .Single(control => AutomationProperties.GetName(control) == label);
            combo.StartBringIntoView();
            await LayoutAsync(page);
            combo.SelectedItem = row.Choices.Single(item => item.Label == choice);
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
        }

        void AssertRow(string title, string label, bool visible)
        {
            var rows = page.ViewModel.Sections.Single(section => section.Title == title).Rows;
            Require(rows.Any(row => row.Label == label) == visible, $"{title}/{label} 的行模型显隐不符");
            Require(Descendants(page).OfType<TextBlock>().Any(text => text.Text == label && text.ActualHeight > 0) == visible,
                $"{title}/{label} 的实际文字显隐不符");
            var controls = Descendants(page).OfType<Control>().Where(control => AutomationProperties.GetName(control) == label);
            Require(controls.Any(control => control.Visibility == Visibility.Visible && control.ActualHeight > 0) == visible,
                $"{title}/{label} 的实际交互控件显隐不符");
        }

        async Task Capture(string name, string title)
        {
            var section = page.ViewModel.Sections.Single(item => item.Title == title);
            var target = Descendants(page).OfType<FrameworkElement>()
                .First(element => element.Name == "SettingsSection" && ReferenceEquals(element.DataContext, section) && element.ActualHeight > 0);
            var scroll = Descendants(page).OfType<ScrollViewer>().Single(view => view.Name == "SettingsScroll");
            var position = target.TransformToVisual(scroll).TransformPoint(default);
            await ScrollProbeAsync(scroll, Math.Max(0, scroll.VerticalOffset + position.Y - 16));
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            await SaveProbeScreenAsync($"{name}-{width}-screen.png");
        }
    }
}
