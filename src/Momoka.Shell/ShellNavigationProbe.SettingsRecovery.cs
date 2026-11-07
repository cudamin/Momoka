using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SettingsRecoveryControlsAsync(SettingsPage page, Fixture fixture, int width)
    {
        var video = fixture.Settings.Video;
        fixture.Settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        video.HdrMode = "auto";
        video.ToneMapping = "spline";
        video.HdrPeakNits = 777;
        video.HdrReferenceWhiteNits = 215;
        video.HdrSubtitleNits = 180;
        video.HdrImageSubtitleNits = 222;
        video.HdrContrastRecovery = 0.4;
        await page.ViewModel.ReloadAsync();
        page.SelectedCategory = "视频输出";
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        var hdr = page.ViewModel.Sections.Single(section => section.Title == "HDR 与杜比视界");
        Require(hdr.Rows.OfType<SettingOptionalNumberRow>().Count() == 5, "HDR 自动输入夹具没有实现五个可选数值");
        foreach (var row in hdr.Rows.OfType<SettingOptionalNumberRow>())
        {
            var number = Descendants(page).OfType<NumberBox>().Single(control => AutomationProperties.GetName(control) == row.Label);
            var automatic = Descendants(page).OfType<CheckBox>().Single(control => AutomationProperties.GetName(control) == row.Label);
            var original = number.Value;
            Require(!row.Automatic && number.Visibility == Visibility.Visible && original == row.Value, "HDR 手动初值没有显示：" + row.Label);
            Toggle(automatic);
            await LayoutAsync(page);
            Require(row.Automatic && number.Visibility == Visibility.Collapsed && !number.Focus(FocusState.Programmatic)
                && automatic.Visibility == Visibility.Visible && row.Value == original, "HDR 自动状态没有隐藏输入并保留恢复入口：" + row.Label);
            Toggle(automatic);
            await LayoutAsync(page);
            Require(!row.Automatic && number.Visibility == Visibility.Visible && number.Value == original && row.Value == original,
                "HDR 手动恢复丢失原数值：" + row.Label);
        }
        foreach (var row in hdr.Rows.OfType<SettingOptionalNumberRow>())
            Toggle(Descendants(page).OfType<CheckBox>().Single(control => AutomationProperties.GetName(control) == row.Label));
        await LayoutAsync(page);
        await SaveProbeScreenAsync($"settings-hdr-automatic-hidden-{width}-screen.png");
        foreach (var row in hdr.Rows.OfType<SettingOptionalNumberRow>())
            Toggle(Descendants(page).OfType<CheckBox>().Single(control => AutomationProperties.GetName(control) == row.Label));
        await LayoutAsync(page);
        await SaveProbeScreenAsync($"settings-hdr-manual-restored-{width}-screen.png");
        page.SelectedCategory = "播放器";
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        await Pick("播放后端", "外部 mpv.exe（独立窗口）");
        page.SelectedCategory = "视频输出";
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        await Pick("视频渲染", "GPU（兼容回退）");
        AssertGpu(false);
        await SaveProbeScreenAsync($"settings-hdr-external-gpu-{width}-screen.png");
        await Pick("视频渲染", "GPU-Next");
        AssertGpu(true);
        await SaveProbeScreenAsync($"settings-hdr-gpu-next-restored-{width}-screen.png");
        await SettingsSupportingNotesAsync(page, fixture, width);

        async Task Pick(string label, string value)
        {
            var choice = page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingChoiceRow>().Single(row => row.Label == label);
            var combo = Descendants(page).OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == label);
            combo.SelectedItem = choice.Choices.Single(item => item.Label == value);
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
        }

        void AssertGpu(bool next)
        {
            hdr = page.ViewModel.Sections.Single(section => section.Title == "HDR 与杜比视界");
            var choice = hdr.Rows.OfType<SettingChoiceRow>().Single(row => row.Label == "HDR 映射算法");
            var combo = Descendants(page).OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == choice.Label);
            foreach (var label in new[] { "Spline", "BT.2446A", "ST 2094-40", "ST 2094-10" })
                Require(choice.Choices.Any(item => item.Label == label) == next && combo.Items.OfType<SettingChoice>().Any(item => item.Label == label) == next,
                    "HDR 曲线的真实下拉目录与渲染后端不符：" + label);
            foreach (var label in new[] { "HDR 参考白亮度", "HDR 对比度恢复" })
                Require(hdr.Rows.Any(row => row.Label == label) == next
                    && Descendants(page).OfType<NumberBox>().Any(control => AutomationProperties.GetName(control) == label && control.Visibility == Visibility.Visible) == next,
                    "HDR 从属行没有按渲染后端隐藏或恢复：" + label);
            Require(video.ToneMapping == "spline" && video.HdrReferenceWhiteNits == 215 && video.HdrContrastRecovery == 0.4,
                "HDR 后端显隐修改了保存的曲线或手动值");
            if (next) Require(choice.Selected?.Label == "Spline" && ReferenceEquals(combo.SelectedItem, choice.Selected),
                "GPU-Next 恢复未显示原曲线");
        }

        static void Toggle(CheckBox control) => ((IToggleProvider)new CheckBoxAutomationPeer(control).GetPattern(PatternInterface.Toggle)).Toggle();
    }

    private async Task SettingsSupportingNotesAsync(SettingsPage page, Fixture fixture, int width)
    {
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.AniBtEnabled = true;
        fixture.Settings.Ui.CompactMode = false;
        await page.ViewModel.ReloadAsync();
        page.SelectedCategory = "界面";
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        var moviePilot = page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingMoviePilotRow>().Single();
        // Anonymous available-state boundary: only exercise these two template notes, never query a site.
        moviePilot.SitesAvailable = true;
        foreach (var hidden in new[] { false, true, false })
        {
            page.SelectedCategory = "界面";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var compact = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "隐藏功能下方说明");
            compact.IsOn = hidden;
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            page.SelectedCategory = "MoviePilot";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            AssertNote("关闭后隐藏 AniBT 识别、AniBT 搜索和版本来源入口；MoviePilot 其他功能不受影响。", hidden);
            AssertNote("搜索时默认使用的站点。在搜索框里改选也会记住，这一行跟着变。", hidden);
            Require(Descendants(page).OfType<ToggleSwitch>().Any(control => AutomationProperties.GetName(control) == "显示 AniBT 入口"),
                "隐藏说明误收起 AniBT 控件");
            page.SelectedCategory = "字幕";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var preview = page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingSubtitlePreviewRow>().Single();
            var rules = page.ViewModel.Sections.Where(section => section.Category == "字幕").SelectMany(section => section.Rows)
                .OfType<SettingTitleRulesRow>().First();
            AssertNote(preview.Note, hidden);
            AssertNote(rules.PriorityHint, hidden);
            Require(Descendants(page).OfType<TextBlock>().Any(text => text.Text == rules.Label && text.Visibility == Visibility.Visible),
                "隐藏说明误收起关键词标签");
            await SaveProbeScreenAsync($"settings-supporting-notes-{width}-{(hidden ? "hidden" : "shown")}-screen.png");
        }

        void AssertNote(string text, bool hidden)
        {
            var notes = Descendants(page).OfType<TextBlock>().Where(block => block.Text == text).ToArray();
            Require(notes.Length > 0 && notes.All(block => block.Visibility == (hidden ? Visibility.Collapsed : Visibility.Visible)),
                "辅助说明没有响应隐藏或恢复：" + text);
        }
    }
}
