using Momoka.Configuration;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SettingsCustomConfigAsync(SettingsPage page, Fixture fixture, int width)
    {
        const string title = "自定义 mpv.conf";
        const string toggleName = "启用自定义 mpv.conf";
        const string pathName = "mpv.conf 路径";
        const string path = @"C:\配置文件\portable_config\mpv.conf";
        await page.ViewModel.ReloadAsync();
        page.SelectedCategory = "播放器";
        await SettleAsync();
        AssertCard(true);
        AssertToggle(true);
        AssertPath(false);
        await ToggleAsync();
        AssertPath(true);
        var box = Descendants(page).OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == pathName);
        box.Text = "\"" + path + "\"";
        // TextBox 使用失焦提交；通过焦点转移执行实际 TwoWay 绑定。
        box.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == toggleName)
            .Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        await SettleAsync();
        Require(fixture.Settings.Mpv.CustomConfigPath == path && !fixture.SettingsService.HasUnsavedChanges,
            "配置路径没有去引号并保存到临时设置");
        await CaptureAsync("enabled");
        await ToggleAsync();
        AssertPath(false);
        Require(fixture.Settings.Mpv.CustomConfigPath == path, "关闭开关清空了路径");
        await CaptureAsync("disabled");

        // 开着再切到集成模式（用户令 2026-10-16）：卡不收起，但那一项固定为关 —— 开关换成只读文字，路径也收起；
        // 存着的偏好（开 + 路径）不许被这一趟清掉。
        await ToggleAsync();
        await PipelineAsync(VideoPipelineKind.Integrated);
        AssertCard(true);
        AssertToggle(false);
        AssertFixedOff();
        AssertPath(false);
        Require(fixture.Settings.Mpv.CustomConfigEnabled && fixture.Settings.Mpv.CustomConfigPath == path,
            "集成模式清空了开关或路径偏好");
        await CaptureAsync("integrated");
        await PipelineAsync(VideoPipelineKind.Standalone);
        AssertCard(true);
        AssertToggle(true);
        AssertPath(true);
        Require(fixture.Settings.Mpv.CustomConfigEnabled && fixture.Settings.Mpv.CustomConfigPath == path, "切回独占丢失偏好");
        await page.ViewModel.ReloadAsync();
        await SettleAsync();
        Require(Descendants(page).OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == pathName).Text == path,
            "重新进入设置没有恢复路径");
        await CaptureAsync("restored");

        var backend = page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingChoiceRow>()
            .Single(row => row.Label == "播放后端");
        Descendants(page).OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == "播放后端")
            .SelectedItem = backend.Choices[1];
        await SettleAsync();
        AssertCard(false);
        fixture.Settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        fixture.Settings.Mpv.CustomConfigEnabled = false;
        fixture.Settings.Mpv.CustomConfigPath = "";
        await page.ViewModel.ReloadAsync();
        await SettleAsync();
        AssertCard(true);
        AssertToggle(true);
        AssertPath(false);

        void AssertCard(bool present)
        {
            Require(page.ViewModel.Sections.Any(section => section.Title == title) == present, "配置分区数据显隐错误");
            Require(Descendants(page).OfType<TextBlock>().Any(control => control.Text == title) == present, "配置分区标题显隐错误");
        }
        void AssertToggle(bool present) => Require(Descendants(page).OfType<ToggleSwitch>()
            .Any(control => AutomationProperties.GetName(control) == toggleName) == present, "配置开关显隐错误");
        // 集成模式下那一行换成只读文字：数据行是 SettingFactRow、值是「关」，屏上那颗开关必须不在。
        void AssertFixedOff()
        {
            var row = page.ViewModel.Sections.First(section => section.Title == title).Rows
                .OfType<SettingFactRow>().SingleOrDefault(candidate => candidate.Label == toggleName);
            Require(row is { Value: "关" }, "集成模式下这一项没有固定为关的只读文字");
            Require(Descendants(page).OfType<TextBlock>().Any(text => text.Text == toggleName)
                && Descendants(page).OfType<TextBlock>().Any(text => text.Text == "关"),
                "集成模式下的只读文字没有画在屏上");
        }
        void AssertPath(bool present) => Require(Descendants(page).OfType<TextBox>()
            .Any(control => AutomationProperties.GetName(control) == pathName) == present, "路径输入框显隐错误");
        async Task SettleAsync() { await LayoutAsync(page); await Task.Delay(450); }
        async Task ToggleAsync()
        {
            var toggle = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == toggleName);
            var peer = new ToggleSwitchAutomationPeer(toggle);
            ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
            await SettleAsync();
        }
        async Task PipelineAsync(VideoPipelineKind pipeline)
        {
            page.SelectedCategory = "视频输出";
            await SettleAsync();
            var (row, combo) = PipelineRow(page);
            combo.SelectedItem = row.Choices[pipeline == VideoPipelineKind.Integrated ? 0 : 1];
            await SettleAsync();
            page.SelectedCategory = "播放器";
            await SettleAsync();
        }
        async Task CaptureAsync(string state)
        {
            await SaveReturnFrameAsync(page, $"settings-custom-config-{state}-{width}.png");
            await SaveProbeScreenAsync($"settings-custom-config-{state}-{width}-screen.png");
        }
    }
}
