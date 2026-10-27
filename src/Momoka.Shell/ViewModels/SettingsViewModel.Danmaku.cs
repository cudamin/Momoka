using Momoka.Configuration;
using Momoka.Danmaku;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    private const string DanmakuCategory = "弹幕";

    private DandanPlayCredentials? _dandanCredentials;

    /// <summary>
    /// 「弹幕」分类下的四张卡：开关与匹配、显示与外观、类型与屏蔽、应用凭据。
    /// <para>
    /// 2026-10-27 用户令：「同一个页面，有必要分区的功能给他分区，单独弄一个卡片」＋「AppId、AppSecret、
    /// 开发者中心这三个功能移到最下方单独一个卡片」。原来是一张二十来行的长卡：开关、匹配、外观、屏蔽词和
    /// 开发者凭据全挤在一起，找一行要滚很久，凭据那三行还插在「使用自己的应用凭据」与「自动匹配弹幕」中间，
    /// 把开关和它的从属行拆成了两段。现在按「这一行在回答哪个问题」拆开。
    /// </para>
    /// <para>
    /// 凭据那三行跟着它们的上级开关一起走。开关留在上一张卡、它控制的三个字段在最后一张卡，是拆开之后
    /// 最容易出的错 —— 拨一下不知道哪里变了。
    /// </para>
    /// <para>
    /// 四张卡一直建着、行在原地增删（<see cref="SettingSection.UpdateRows"/>），不整卡替换：行数归零的卡
    /// 由 <see cref="SettingSection.SectionVisibility"/> 整张收起，所以服务关着时屏上只有「弹幕」和
    /// 「应用凭据」两张。
    /// </para>
    /// </summary>
    private IReadOnlyList<SettingSection> DanmakuSections()
    {
        var settings = Settings.Danmaku;
        var custom = settings.CredentialSource == DanmakuCredentialSource.Custom;
        var configured = DandanPlayMatching.IsConfigured(Settings);

        var rows = new List<SettingRow>
        {
            Toggle("启用弹弹play", "默认关闭。开启后自动使用 Momoka 内置凭据；搜索或自动匹配会把作品名称和集数发送到弹弹play，不上传视频、媒体服务器地址或登录信息。内置额度用尽时可在最下面的「应用凭据」里改用自定义凭据。",
                () => settings.Enabled, value => settings.Enabled = value, after: RefreshDanmakuRows),
        };
        if (configured)
        {
            rows.Add(Toggle("自动匹配弹幕", "只自动接受唯一的精确标题。续季、特别篇或不确定的结果在播放页手动搜索选择，避免串集。",
                () => settings.AutoMatch, value => settings.AutoMatch = value, after: RefreshDanmakuRows));
            rows.Add(Toggle("包含关联弹幕", "同时获取弹弹play 关联的第三方网站弹幕；修改后重新加载当前弹幕库。",
                () => settings.WithRelated, value => settings.WithRelated = value, after: RefreshDanmakuRows));
        }

        var displayRows = new List<SettingRow>();
        var typeRows = new List<SettingRow>();
        if (configured)
        {
            displayRows.Add(Toggle("显示弹幕", "播放页的弹幕按钮和 D 快捷键可以随时开关；关闭不丢失外观与屏蔽偏好。",
                () => settings.Show, value => settings.Show = value, after: RefreshDanmakuRows));
            if (settings.Show)
            {
                displayRows.Add(Toggle("显示弹幕输入栏", "在播放器顶部影片标题右侧显示输入栏；关闭不影响观看弹幕。",
                    () => settings.ShowInput, value => settings.ShowInput = value, after: RefreshDanmakuRows));
                displayRows.Add(Slider("不透明度（%）", 10, 100, 5, () => settings.Opacity, value => settings.Opacity = value, after: ApplyDanmakuSettings));
                displayRows.Add(Slider("字号", 20, 64, 2, () => settings.FontSize, value => settings.FontSize = value,
                    "按画面高度等比例缩放，独立于字幕字号。", after: ApplyDanmakuSettings));
                displayRows.Add(Slider("弹幕速度（倍）", 0.5, 2, 0.1, () => settings.Speed, value => settings.Speed = value, after: ApplyDanmakuSettings));
                displayRows.Add(Slider("显示区域（%）", 25, 100, 25, () => settings.Area, value => settings.Area = value,
                    "25% 顶部四分之一、50% 半屏、75% 四分之三、100% 全屏。", after: ApplyDanmakuSettings));
                displayRows.Add(Slider("同屏弹幕上限", 10, 100, 10, () => settings.Density, value => settings.Density = (int)value,
                    "遇到拥挤时优先避免重叠，不保证所有弹幕都能显示。", after: ApplyDanmakuSettings));

                typeRows.Add(Toggle("滚动弹幕", "显示从右向左移动的弹幕。", () => settings.Scroll, value => settings.Scroll = value, after: ApplyDanmakuSettings));
                typeRows.Add(Toggle("顶部弹幕", "显示固定在顶部的弹幕。", () => settings.Top, value => settings.Top = value, after: ApplyDanmakuSettings));
                typeRows.Add(Toggle("底部弹幕", "显示固定在所选显示区域底部的弹幕。", () => settings.Bottom, value => settings.Bottom = value, after: ApplyDanmakuSettings));
                typeRows.Add(Toggle("彩色弹幕", "关闭后屏蔽非白色弹幕。", () => settings.Color, value => settings.Color = value, after: ApplyDanmakuSettings));
                typeRows.Add(Toggle("合并重复弹幕", "相同内容在 10 秒内只保留第一条。", () => settings.Deduplicate, value => settings.Deduplicate = value, after: ApplyDanmakuSettings));
                typeRows.Add(new SettingTextRow("屏蔽词", "多个词用逗号分隔，不区分英文大小写。", "输入不想看到的词", settings.BlockedWords,
                    typed => { settings.BlockedWords = typed; settings.Normalize(); return settings.BlockedWords; }, () => { Save(); ApplyDanmakuSettings(); }));
            }
        }

        var credentialRows = new List<SettingRow>
        {
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
            credentialRows.Add(new SettingTextRow("AppId", "你的弹弹play 应用 ID；使用内置凭据时不需要填写。", "弹弹play 应用 ID", settings.AppId,
                typed => { settings.AppId = typed.Trim(); return settings.AppId; }, () => { Save(); RefreshDanmakuRows(); }));
            credentialRows.Add(new SettingSecretRow("AppSecret", secret => _dandanCredentials?.SetSecret(settings, secret),
                () => settings.ProtectedAppSecret.Length > 0, () => { Save(); return !HasUnsavedChanges; }, RefreshDanmakuRows));
            credentialRows.Add(Fact("弹弹play 开发者中心", "申请或管理 AppId 与 AppSecret。弹幕数据由弹弹play 开放弹幕网络提供。", "", "打开开发者中心",
                () => _launcher?.OpenUrl(DandanPlayClient.DeveloperUrl)));
        }

        return
        [
            new SettingSection(DanmakuCategory, "弹幕",
                "参考 B 站的显示与屏蔽设置。弹幕使用独立画面叠加层，不占用字幕轨道；支持内置播放器的集成和独占模式。",
                rows),
            new SettingSection(DanmakuCategory, "显示与外观",
                "弹幕开关、输入栏和画面上的大小与疏密；改完立刻作用到正在播的片子。", displayRows),
            new SettingSection(DanmakuCategory, "类型与屏蔽",
                "哪几种弹幕显示出来，以及不想看到的词。", typeRows),
            new SettingSection(DanmakuCategory, "应用凭据",
                "内置凭据由 Momoka 统一提供；有自己的弹弹play 应用时改用自己的 AppId 与 AppSecret，凭据只保存在本机。",
                credentialRows)
        ];
    }

    private void RefreshDanmakuRows()
    {
        RefreshDanmakuPresentation();
        ApplyDanmakuSettings();
    }

    /// <summary>
    /// 分类下四张卡的行原地重算：按标题对上号再 UpdateRows，卡片实例不动 —— 输入焦点、半截文本和行的
    /// 增删过渡都跟着留下来。卡还没建（外部 mpv 后端把它整组收走了）就什么也不做，恢复时按当时的值重建。
    /// </summary>
    private void RefreshDanmakuPresentation()
    {
        var existing = Sections.Where(section => section.Category == DanmakuCategory).ToArray();
        if (existing.Length > 0)
            foreach (var section in DanmakuSections())
                existing.FirstOrDefault(current => current.Title == section.Title)?.UpdateRows(section.Rows);
        RefreshShortcutAvailability();
    }

    private void RefreshDanmakuAvailability()
    {
        var existing = Sections.Where(section => section.Category == DanmakuCategory).ToList();
        if (!DandanPlayMatching.CanRender(Settings.Mpv))
        {
            foreach (var section in existing) Sections.Remove(section);
        }
        else if (existing.Count == 0)
        {
            // 四张卡一起插在「快捷键」之前：字幕之后、快捷键之前，与 ReloadAsync 的建卡次序同一处。
            var index = Sections.ToList().FindIndex(section => section.Category == "快捷键");
            var insert = index < 0 ? Sections.Count : index;
            foreach (var section in DanmakuSections()) Sections.Insert(insert++, section);
        }
        var groups = CategoryGroups.ToArray();
        var momoka = groups.FirstOrDefault(group => group.Name == "Momoka");
        if (momoka is not null)
        {
            var available = DandanPlayMatching.CanRender(Settings.Mpv);
            if (momoka.Entries.Any(entry => entry.Label == DanmakuCategory) == available) return;
            var rebuilt = new SettingsCategoryGroup("Momoka");
            foreach (var entry in momoka.Entries.Where(entry => entry.Label != DanmakuCategory))
            {
                rebuilt.Add(entry.Label, entry.SourceKey, entry.Route, entry.CardCategory);
                if (entry.Label == "字幕" && available) rebuilt.Add(DanmakuCategory);
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
