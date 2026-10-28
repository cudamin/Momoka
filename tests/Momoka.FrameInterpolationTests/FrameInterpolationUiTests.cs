using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FrameInterpolationUiTests
{
    internal static void Register()
    {
        Test("补帧卡：关闭只保留恢复模式入口，分类/标题/生效文案正确", () =>
        {
            var vm = new SettingsViewModel();
            var card = vm.BuildCard();
            Assert.Equal("视频输出", card.Category);
            Assert.Equal("补帧", card.Title);
            Assert.Equal(vm.Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv ? 2 : 1, card.Rows.Count);
            var mode = (SettingChoiceRow)card.Rows[0];
            Assert.Equal("关闭|平滑插值（轻量）|运动补偿（CPU）", string.Join('|', mode.Choices.Select(choice => choice.Label)));
            Assert.Equal("关闭", mode.Selected?.Label);
            Assert.Contains("下次播放生效", card.Description);
            Assert.Contains("CPU", mode.Note);
            Assert.Contains("不是 GPU／AI", mode.Note);
            Assert.Contains("HDR", card.Description);
            Assert.Equal(0, vm.SaveCount, "构造卡片不改存值");
        });
        Test("补帧卡：模式after联动本卡和父刷新，稳定集合且不递归", () =>
        {
            var vm = new SettingsViewModel();
            var card = vm.BuildCard();
            var rows = card.Rows;
            var mode = (SettingChoiceRow)card.Rows[0];
            mode.Selected = mode.Choices.Single(choice => choice.Label == "运动补偿（CPU）");
            Assert.Equal(FrameInterpolationMode.MotionCompensated, vm.Settings.Video.FrameInterpolationMode);
            Assert.Equal(4, card.Rows.Count);
            Assert.Equal(1, vm.SaveCount);
            Assert.Equal(1, vm.BackendRefreshCount);
            Assert.True(ReferenceEquals(rows, card.Rows));
            Assert.True(ReferenceEquals(mode, card.Rows[0]));
            mode.Selected = mode.Choices.Single(choice => choice.Label == "平滑插值（轻量）");
            Assert.Equal(vm.Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv ? 3 : 2, card.Rows.Count);
            Assert.Equal("平滑算法", card.Rows[1].Label);
            Assert.Equal(2, vm.SaveCount);
            Assert.Equal(2, vm.BackendRefreshCount);
        });
        Test("补帧卡：隐藏从属行不清fps/质量/旧插值及恢复值", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Video.FrameInterpolationMode = FrameInterpolationMode.MotionCompensated;
            vm.Settings.Video.FrameInterpolationFps = 120;
            vm.Settings.Video.FrameInterpolationQuality = FrameInterpolationQuality.High;
            vm.Settings.Video.Interpolation = true;
            vm.Settings.Video.Tscale = "bicubic";
            var card = vm.BuildCard();
            var mode = (SettingChoiceRow)card.Rows[0];
            mode.Selected = mode.Choices.Single(choice => choice.Label == "关闭");
            Assert.Equal(vm.Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv ? 2 : 1, card.Rows.Count);
            Assert.Equal(120, vm.Settings.Video.FrameInterpolationFps);
            Assert.Equal(FrameInterpolationQuality.High, vm.Settings.Video.FrameInterpolationQuality);
            Assert.True(vm.Settings.Video.Interpolation);
            Assert.Equal("bicubic", vm.Settings.Video.Tscale);
            mode.Selected = mode.Choices.Single(choice => choice.Label == "运动补偿（CPU）");
            Assert.Equal(120.0, ((SettingNumberRow)card.Rows[1]).Value);
            Assert.Equal("细节优先（更重）", ((SettingChoiceRow)card.Rows[2]).Selected?.Label);
        });
        Test("补帧卡：Number真实提交夹限、清空不改存值", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Video.FrameInterpolationMode = FrameInterpolationMode.MotionCompensated;
            var card = vm.BuildCard();
            var fps = (SettingNumberRow)card.Rows[1];
            Assert.Equal(24.0, fps.Minimum);
            Assert.Equal(120.0, fps.Maximum);
            fps.Value = 200;
            Assert.Equal(120, vm.Settings.Video.FrameInterpolationFps);
            Assert.Equal(120.0, fps.Value);
            fps.Value = 1;
            Assert.Equal(24, vm.Settings.Video.FrameInterpolationFps);
            fps.Value = double.NaN;
            Assert.Equal(24, vm.Settings.Video.FrameInterpolationFps);
            Assert.Equal(2, vm.SaveCount);
        });
        Test("补帧卡：质量真实选择写枚举，Smooth共用旧算法不改旧开关", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Video.FrameInterpolationMode = FrameInterpolationMode.MotionCompensated;
            var card = vm.BuildCard();
            var quality = (SettingChoiceRow)card.Rows[2];
            quality.Selected = quality.Choices.Single(choice => choice.Label == "速度优先");
            Assert.Equal(FrameInterpolationQuality.Fast, vm.Settings.Video.FrameInterpolationQuality);
            var mode = (SettingChoiceRow)card.Rows[0];
            mode.Selected = mode.Choices.Single(choice => choice.Label == "平滑插值（轻量）");
            var kernel = (SettingChoiceRow)card.Rows[1];
            kernel.Selected = kernel.Choices.Single(choice => choice.Label == "Mitchell（均衡）");
            Assert.Equal("mitchell", vm.Settings.Video.Tscale);
            Assert.False(vm.Settings.Video.Interpolation);
        });
        Test("补帧卡：后端/渲染不适用时隐藏平滑算法且保留恢复入口", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Video.FrameInterpolationMode = FrameInterpolationMode.Smooth;
            vm.Settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            vm.Settings.Video.Renderer = "null";
            var card = vm.BuildCard();
            Assert.Equal(vm.Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv ? 2 : 1, card.Rows.Count);
            vm.Settings.Video.Renderer = "gpu-next";
            vm.RefreshCard();
            Assert.Equal(vm.Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv ? 3 : 2, card.Rows.Count);
            Assert.Equal(FrameInterpolationMode.Smooth, vm.Settings.Video.FrameInterpolationMode);
            Assert.Equal(0, vm.SaveCount);
        });
        Test("补帧卡：自定义mpv.conf提示覆盖风险，关配置后原地更新", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
            vm.Settings.Mpv.Pipeline = VideoPipelineKind.Standalone;
            vm.Settings.Mpv.CustomConfigEnabled = true;
            var card = vm.BuildCard();
            Assert.Contains("不保证", card.Rows[0].Note);
            var mode = card.Rows[0];
            vm.Settings.Mpv.CustomConfigEnabled = false;
            vm.RefreshCard();
            Assert.DoesNotContain("不保证", card.Rows[0].Note);
            Assert.True(ReferenceEquals(mode, card.Rows[0]));
        });
        Test("SVP 设置：开关切换隐藏 CPU 行，恢复原模式／帧率／质量并保持集合", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Video.FrameInterpolationMode = FrameInterpolationMode.MotionCompensated;
            vm.Settings.Video.FrameInterpolationFps = 72;
            vm.Settings.Video.FrameInterpolationQuality = FrameInterpolationQuality.High;
            vm.Settings.Mpv.SvpVapourSynthPath = "unavailable-anonymous-component";
            var card = vm.BuildCard();
            var rows = card.Rows;
            var toggle = (SettingToggleRow)card.Rows.Single(row => row.Label == "接入 SVP");
            toggle.IsOn = true;
            Assert.True(vm.Settings.Mpv.SvpEnabled);
            Assert.False(card.Rows.Any(row => row.Label is "模式" or "目标帧率" or "计算质量" or "平滑算法"));
            Assert.True(card.Rows.Any(row => row.Label == "VapourSynth 组件"));
            Assert.Contains("缺少", ((SettingFactRow)card.Rows.Single(row => row.Label == "SVP 接入状态")).Value);
            Assert.True(ReferenceEquals(rows, card.Rows));
            Assert.True(ReferenceEquals(toggle, card.Rows.Single(row => row.Label == "接入 SVP")));
            toggle.IsOn = false;
            Assert.False(card.Rows.Any(row => row.Label.StartsWith("SVP 接入状态", StringComparison.Ordinal) || row.Label == "VapourSynth 组件"));
            Assert.Equal("运动补偿（CPU）", ((SettingChoiceRow)card.Rows[0]).Selected?.Label);
            Assert.Equal(72.0, ((SettingNumberRow)card.Rows[1]).Value);
            Assert.Equal("细节优先（更重）", ((SettingChoiceRow)card.Rows[2]).Selected?.Label);
            Assert.Equal("unavailable-anonymous-component", vm.Settings.Mpv.SvpVapourSynthPath);
            Assert.Equal(2, vm.SaveCount);
            Assert.Equal(2, vm.BackendRefreshCount);
        });
        Test("SVP 设置：外部后端／自定义配置收起从属项，条件恢复不清空偏好", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Mpv.SvpEnabled = true;
            vm.Settings.Mpv.SvpVapourSynthPath = "retained-anonymous-component";
            vm.Settings.Video.FrameInterpolationMode = FrameInterpolationMode.Smooth;
            var card = vm.BuildCard();
            vm.Settings.Mpv.CustomConfigEnabled = true;
            vm.RefreshCard();
            Assert.True(card.Rows.Any(row => row.Label == "接入 SVP"));
            Assert.False(card.Rows.Any(row => row.Label == "VapourSynth 组件"));
            Assert.True(card.Rows.Any(row => row.Label == "模式"));
            Assert.Contains("自定义", ((SettingFactRow)card.Rows.Single(row => row.Label == "SVP 接入状态")).Value);
            vm.Settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            vm.RefreshCard();
            Assert.False(card.Rows.Any(row => row.Label.Contains("SVP", StringComparison.Ordinal) || row.Label == "VapourSynth 组件"));
            vm.Settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
            vm.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
            vm.RefreshCard();
            Assert.True(card.Rows.Any(row => row.Label == "VapourSynth 组件"));
            Assert.False(card.Rows.Any(row => row.Label == "平滑算法"));
            Assert.True(vm.Settings.Mpv.SvpEnabled);
            Assert.Equal("retained-anonymous-component", vm.Settings.Mpv.SvpVapourSynthPath);
            Assert.Equal(0, vm.SaveCount);
        });
        Test("SVP 设置：真实文本行清理路径并更新状态，重新检测不保存", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Mpv.SvpEnabled = true;
            vm.Settings.Mpv.SvpVapourSynthPath = "initial-anonymous-component";
            var card = vm.BuildCard();
            var text = (SettingTextRow)card.Rows.Single(row => row.Label == "VapourSynth 组件");
            text.Text = " \"unavailable-anonymous-component\" ";
            Assert.Equal("unavailable-anonymous-component", text.Text);
            Assert.Equal(text.Text, vm.Settings.Mpv.SvpVapourSynthPath);
            Assert.True(ReferenceEquals(text, card.Rows.Single(row => row.Label == "VapourSynth 组件")));
            Assert.Equal(1, vm.SaveCount);
            ((SettingFactRow)card.Rows.Single(row => row.Label == "SVP 接入状态")).RunCommand.Execute(null);
            Assert.Equal(1, vm.SaveCount);
            Assert.Equal(2, vm.BackendRefreshCount);
        });
        Test("补帧卡：未挂卡刷新安全，重新构建和未知值不写入设置", () =>
        {
            var vm = new SettingsViewModel();
            vm.Settings.Video.FrameInterpolationMode = (FrameInterpolationMode)99;
            vm.Settings.Video.FrameInterpolationQuality = (FrameInterpolationQuality)99;
            vm.Settings.Video.FrameInterpolationFps = 500;
            vm.RefreshCard();
            var card = vm.BuildCard();
            Assert.Equal("关闭", ((SettingChoiceRow)card.Rows[0]).Selected?.Label);
            Assert.Equal((FrameInterpolationMode)99, vm.Settings.Video.FrameInterpolationMode);
            Assert.Equal(500, vm.Settings.Video.FrameInterpolationFps);
            Assert.Equal(0, vm.SaveCount);
        });
    }
}
