using Momoka.Configuration;
using Momoka.Danmaku;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    private DandanPlayCredentials? _dandanCredentials;

    private SettingSection DanmakuCard()
    {
        var settings = Settings.Danmaku;
        var custom = settings.CredentialSource == DanmakuCredentialSource.Custom;
        var rows = new List<SettingRow>
        {
            Toggle("启用弹弹play", "默认关闭。开启后自动使用 Momoka 内置凭据；搜索或自动匹配会把作品名称和集数发送到弹弹play，不上传视频、媒体服务器地址或登录信息。内置额度用尽时可在下面改用自定义凭据。",
                () => settings.Enabled, value => settings.Enabled = value, after: RefreshDanmakuRows),
            Toggle("使用自己的应用凭据", "内置凭据由 Momoka 统一提供，普通用户不需要申请；如果你有自己的弹弹play 应用，可以改用自定义的 AppId 与 AppSecret，凭据只保存在本机。",
                () => settings.CredentialSource == DanmakuCredentialSource.Custom,
                value =>
                {
                    settings.CredentialSource = value ? DanmakuCredentialSource.Custom : DanmakuCredentialSource.BuiltIn;
                    if (value && string.IsNullOrWhiteSpace(settings.AppId)) settings.AppId = DanmakuSettings.DefaultAppId;
                }, after: RefreshDanmakuRows),
        };
        if (custom)
        {
            rows.Add(new SettingTextRow("AppId", "你的弹弹play 应用 ID；使用内置凭据时不需要填写。", "弹弹play 应用 ID", settings.AppId,
                typed => { settings.AppId = typed.Trim(); return settings.AppId; }, () => { Save(); RefreshDanmakuRows(); }));
            rows.Add(new SettingSecretRow("AppSecret", secret => _dandanCredentials?.SetSecret(settings, secret),
                () => settings.ProtectedAppSecret.Length > 0, () => { Save(); return !HasUnsavedChanges; }, RefreshDanmakuRows));
            rows.Add(Fact("弹弹play 开发者中心", "申请或管理 AppId 与 AppSecret。弹幕数据由弹弹play 开放弹幕网络提供。", "", "打开开发者中心",
                () => _launcher?.OpenUrl(DandanPlayClient.DeveloperUrl)));
        }
        if (DandanPlayMatching.IsConfigured(Settings))
        {
            rows.Add(Toggle("自动匹配弹幕", "只自动接受唯一的精确标题。续季、特别篇或不确定的结果在播放页手动搜索选择，避免串集。",
                () => settings.AutoMatch, value => settings.AutoMatch = value, after: RefreshDanmakuRows));
            rows.Add(Toggle("包含关联弹幕", "同时获取弹弹play 关联的第三方网站弹幕；修改后重新加载当前弹幕库。",
                () => settings.WithRelated, value => settings.WithRelated = value, after: RefreshDanmakuRows));
            rows.Add(Toggle("显示弹幕", "播放页的弹幕按钮和 D 快捷键可以随时开关；关闭不丢失外观与屏蔽偏好。",
                () => settings.Show, value => settings.Show = value, after: RefreshDanmakuRows));
            if (settings.Show)
            {
                rows.Add(Toggle("显示弹幕输入栏", "在播放器顶部影片标题右侧显示输入栏；关闭不影响观看弹幕。",
                    () => settings.ShowInput, value => settings.ShowInput = value, after: RefreshDanmakuRows));
                rows.Add(Slider("不透明度（%）", 10, 100, 5, () => settings.Opacity, value => settings.Opacity = value, after: ApplyDanmakuSettings));
                rows.Add(Slider("字号", 20, 64, 2, () => settings.FontSize, value => settings.FontSize = value,
                    "按画面高度等比例缩放，独立于字幕字号。", after: ApplyDanmakuSettings));
                rows.Add(Slider("弹幕速度（倍）", 0.5, 2, 0.1, () => settings.Speed, value => settings.Speed = value, after: ApplyDanmakuSettings));
                rows.Add(Slider("显示区域（%）", 25, 100, 25, () => settings.Area, value => settings.Area = value,
                    "25% 顶部四分之一、50% 半屏、75% 四分之三、100% 全屏。", after: ApplyDanmakuSettings));
                rows.Add(Slider("同屏弹幕上限", 10, 100, 10, () => settings.Density, value => settings.Density = (int)value,
                    "遇到拥挤时优先避免重叠，不保证所有弹幕都能显示。", after: ApplyDanmakuSettings));
                rows.Add(Toggle("滚动弹幕", "显示从右向左移动的弹幕。", () => settings.Scroll, value => settings.Scroll = value, after: ApplyDanmakuSettings));
                rows.Add(Toggle("顶部弹幕", "显示固定在顶部的弹幕。", () => settings.Top, value => settings.Top = value, after: ApplyDanmakuSettings));
                rows.Add(Toggle("底部弹幕", "显示固定在所选显示区域底部的弹幕。", () => settings.Bottom, value => settings.Bottom = value, after: ApplyDanmakuSettings));
                rows.Add(Toggle("彩色弹幕", "关闭后屏蔽非白色弹幕。", () => settings.Color, value => settings.Color = value, after: ApplyDanmakuSettings));
                rows.Add(Toggle("合并重复弹幕", "相同内容在 10 秒内只保留第一条。", () => settings.Deduplicate, value => settings.Deduplicate = value, after: ApplyDanmakuSettings));
                rows.Add(new SettingTextRow("屏蔽词", "多个词用逗号分隔，不区分英文大小写。", "输入不想看到的词", settings.BlockedWords,
                    typed => { settings.BlockedWords = typed; settings.Normalize(); return settings.BlockedWords; }, () => { Save(); ApplyDanmakuSettings(); }));
            }
        }
        return new SettingSection("弹幕", "弹幕", "参考 B 站的显示与屏蔽设置。弹幕使用独立画面叠加层，不占用字幕轨道；支持内置播放器的集成和独占模式。", rows);
    }

    private void RefreshDanmakuRows()
    {
        RefreshDanmakuPresentation();
        ApplyDanmakuSettings();
    }

    private void RefreshDanmakuPresentation()
    {
        var card = Sections.FirstOrDefault(section => section.Category == "弹幕");
        card?.UpdateRows(DanmakuCard().Rows);
        RefreshShortcutAvailability();
    }

    private void RefreshDanmakuAvailability()
    {
        var card = Sections.FirstOrDefault(section => section.Category == "弹幕");
        if (!DandanPlayMatching.CanRender(Settings.Mpv))
        {
            if (card is not null) Sections.Remove(card);
        }
        else if (card is null)
        {
            var index = Sections.ToList().FindIndex(section => section.Category == "字幕");
            Sections.Insert(index + 1, DanmakuCard());
        }
        var groups = CategoryGroups.ToArray();
        var momoka = groups.FirstOrDefault(group => group.Name == "Momoka");
        if (momoka is not null)
        {
            var available = DandanPlayMatching.CanRender(Settings.Mpv);
            if (momoka.Entries.Any(entry => entry.Label == "弹幕") == available) return;
            var rebuilt = new SettingsCategoryGroup("Momoka");
            foreach (var entry in momoka.Entries.Where(entry => entry.Label != "弹幕"))
            {
                rebuilt.Add(entry.Label, entry.SourceKey, entry.Route, entry.CardCategory);
                if (entry.Label == "字幕" && available) rebuilt.Add("弹幕");
            }
            ReplaceCategories(groups.Select(group => ReferenceEquals(group, momoka) ? rebuilt : group).ToArray());
        }
    }

    internal void OnDanmakuPreferencesChanged()
    {
        if (_settings is not null && IsReady) RefreshDanmakuPresentation();
    }

    private static void ApplyDanmakuSettings() => ShellPrefs.ApplyDanmaku();
}
