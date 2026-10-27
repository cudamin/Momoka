using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Momoka.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SettingsTranslationAsync()
    {
        using var fixture = CreateFixture();
        using var mpClient = new MoviePilotClient(new RejectNetwork());
        using var updates = new UpdateClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "translation-settings"));
        paths.EnsureCreated();
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var credentials = new SubtitleTranslationCredentials(PassthroughSecretProtector.Instance);
        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(mpClient, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), updates, fixture.Capabilities, fixture.Images,
            translationCredentials: credentials);
        var oldTheme = ThemeHost.Current;
        _root.Children.Add(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "字幕";
            await SettlePageAsync();
            Require(TranslationRows().Length == 1 && TranslationRows()[0].Label == "启用实时字幕翻译", "默认关闭没有隐藏从属翻译配置");
            await ToggleAsync("启用实时字幕翻译");
            Require(fixture.Settings.SubtitleTranslation.Enabled && TranslationRows().Any(row => row.Label == "谷歌 API 密钥"), "开启翻译没有显示谷歌配置");
            foreach (var provider in new[] { "微软 · Microsoft Translator", "AI · 兼容 OpenAI", "谷歌 · Google Cloud Translation" })
            {
                await PickAsync("翻译服务", provider);
                var names = TranslationRows().Select(row => row.Label).ToArray();
                Require(names.Count(name => name.EndsWith("API 密钥", StringComparison.Ordinal)) == 1, "不同服务密钥行同时显示");
                Require(names.Contains("微软资源区域") == provider.StartsWith("微软", StringComparison.Ordinal), "微软区域显隐不跟随服务");
                Require(names.Contains("AI 模型") == provider.StartsWith("AI", StringComparison.Ordinal), "AI 模型显隐不跟随服务");
            }
            await PickAsync("翻译服务", "AI · 兼容 OpenAI");
            var input = Descendants(page).OfType<PasswordBox>().Single(control => control.Name == "SecretInput");
            input.Password = "offline-placeholder-key";
            await SettlePageAsync();
            var secret = TranslationRows().OfType<SettingSecretRow>().Single();
            Require(secret.Draft.Length > 0, "真实 PasswordBox 绑定没有接收草稿");
            InvokeSubscriptionButton(Descendants(page).OfType<Button>().Single(control => control.Name == "SaveSecret"));
            await SettlePageAsync();
            Require(fixture.Settings.SubtitleTranslation.HasAiApiKey && secret.Draft.Length == 0 && input.Password.Length == 0,
                "保存占位密钥后输入框没有清空或密文缺失");
            InvokeSubscriptionButton(Descendants(page).OfType<Button>().Single(control => control.Name == "ClearSecret"));
            await SettlePageAsync();
            Require(!fixture.Settings.SubtitleTranslation.HasAiApiKey, "清除占位密钥没有写回设置");
            input.Password = "offline-placeholder-key";
            await SettlePageAsync();
            InvokeSubscriptionButton(Descendants(page).OfType<Button>().Single(control => control.Name == "SaveSecret"));
            await SettlePageAsync();
            await PickAsync("翻译为", "日语");
            await ToggleAsync("启用实时字幕翻译");
            Require(TranslationRows().Length == 1 && fixture.Settings.SubtitleTranslation.TargetLanguage == "ja"
                && fixture.Settings.SubtitleTranslation.HasAiApiKey, "隐藏翻译配置删除了偏好或密钥");
            await ToggleAsync("启用实时字幕翻译");
            Require(TranslationRows().Any(row => row.Label == "AI 模型"), "重新开启没有恢复 AI 服务");
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                foreach (var width in new[] { 1100, 760 })
                {
                    ResizeInspect(width, 900);
                    await SettlePageAsync();
                    await CaptureAsync($"settings-translation-{theme.Id}-{width}");
                }
            }
            fixture.Settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            fixture.Settings.Mpv.EnableIpc = false;
            Call(page.ViewModel, "RefreshPlayerBackend");
            Require(page.ViewModel.Sections.All(section => section.Title != "实时字幕翻译"), "外部播放器无 IPC 仍显示不会生效的翻译卡");
            fixture.Settings.Mpv.EnableIpc = true;
            Call(page.ViewModel, "RefreshPlayerBackend");
            Require(page.ViewModel.Sections.Any(section => section.Title == "实时字幕翻译"), "恢复 IPC 后没有恢复翻译卡");
            Require(fixture.Settings.SubtitleTranslation.TargetLanguage == "ja" && fixture.Settings.SubtitleTranslation.HasAiApiKey,
                "能力显隐删除了翻译偏好");
        }
        finally
        {
            ThemeHost.Apply(oldTheme);
            page.Release(); _root.Children.Remove(page); ResizeInspect(1420, 980);
        }

        SettingRow[] TranslationRows() => page.ViewModel.Sections.Single(section => section.Title == "实时字幕翻译").Rows.ToArray();
        async Task SettlePageAsync() { await LayoutAsync(page); await WaitForSettingsSettledAsync(page); await Task.Delay(80); }
        async Task ToggleAsync(string label)
        {
            var control = Descendants(page).OfType<ToggleSwitch>().Single(element => AutomationProperties.GetName(element) == label);
            ((IToggleProvider)new ToggleSwitchAutomationPeer(control).GetPattern(PatternInterface.Toggle)).Toggle();
            await SettlePageAsync();
        }
        async Task PickAsync(string label, string value)
        {
            var row = page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingChoiceRow>().Single(item => item.Label == label);
            var control = Descendants(page).OfType<ComboBox>().Single(element => AutomationProperties.GetName(element) == label);
            control.SelectedItem = row.Choices.Single(choice => choice.Label == value);
            await SettlePageAsync();
        }
        async Task CaptureAsync(string name)
        {
            Require(page.SelectedCategory == "字幕" && page.ViewModel.Sections.Where(section => section.IsVisible)
                .All(section => section.Category == "字幕"), "截图前实际分类与目标不同，不能把上一页截图当验收");
            var scroll = Descendants(page).OfType<ScrollViewer>().Single(view => view.Name == "SettingsScroll");
            scroll.ChangeView(0, scroll.ScrollableHeight, null, disableAnimation: true);
            await SettlePageAsync(); await SaveProbeScreenAsync(name + "-screen.png");
            scroll.ChangeView(0, 0, null, disableAnimation: true);
        }
    }
}
