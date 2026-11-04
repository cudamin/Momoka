using Momoka.Configuration;
using Momoka.Danmaku;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    private const string DanmakuCategory = "弹幕";
    private SettingFontRow? _danmakuFont;
    private static readonly (string Label, int Value)[] DanmakuDensityModes = [("正常", 0), ("较多", 1), ("重叠", 2)];
    private static readonly (string Label, int Value)[] DanmakuBorderStyles = [("描边", 0), ("重墨", 1), ("45° 投影", 2)];

    private static readonly (string Label, DanmakuSourceMode Value)[] DanmakuSources =
    [
        ("弹弹play", DanmakuSourceMode.DandanPlay),
        ("danmu_api（自己部署）", DanmakuSourceMode.DanmuApi),
        ("两者合并", DanmakuSourceMode.Both)
    ];

    /// <summary>
    /// 弹幕此刻能不能真的画出来：服务开关开着、而且当前播放后端画得了弹幕。
    /// <para>
    /// 设置页弹幕那几张卡、快捷键里那四条动作都按它显隐；自检也读这一个（原先自检拿整张
    /// <see cref="Momoka.Playback.ShortcutCatalog.Actions"/> 去比，本机把弹幕关掉之后当场红成
    /// 「59 行（应当 63 行）」—— 卡片是对的，判据错了）。未附加时按不可用算，别在这里读 <c>_settings!</c>。
    /// </para>
    /// </summary>
    public bool DanmakuReady => _settings is not null && DandanPlayMatching.IsConfigured(Settings);

    /// <summary>
    /// 「弹幕」分类下的四张卡：开关与匹配、来源与聚合、显示与外观、类型与屏蔽。
    /// <para>
    /// 2026-10-27 用户令拆卡，同一天又令删掉「应用凭据」卡。2026-11-02 接 danmu_api 时新增「来源与聚合」卡：
    /// 每个人填自己部署的地址与令牌，Momoka 不提供公共服务。三张旧卡的实例继续保留，来源卡也走
    /// <see cref="SettingSection.UpdateRows"/> 原地增删行，行数归零时整张收起。
    /// </para>
    /// </summary>
    private IReadOnlyList<SettingSection> DanmakuSections()
    {
        var settings = Settings.Danmaku;
        var configured = DanmakuReady;

        var rows = new List<SettingRow>
        {
            Toggle("启用弹幕", "默认关闭。开启后使用「来源与聚合」里选择的来源；弹弹play 使用 Momoka 内置凭据，"
                + "danmu_api 使用你自己部署的服务。搜索或自动匹配会把作品名称和集数发送到所选服务，"
                + "不上传视频、媒体服务器地址或登录信息。",
                () => settings.Enabled, value => settings.Enabled = value, after: RefreshDanmakuRows),
        };
        if (configured)
        {
            rows.Add(Toggle("自动匹配弹幕", "自动匹配只接受能确认的作品与集数；danmu_api 会带年份并核对集号。"
                + "续季、特别篇或不确定的结果在播放页手动搜索选择，避免串集。",
                () => settings.AutoMatch, value => settings.AutoMatch = value, after: RefreshDanmakuRows));
            if (DanmakuSourceRules.IncludesDandanPlay(settings.Source))
                rows.Add(Toggle("包含关联弹幕", "同时获取弹弹play 关联的第三方网站弹幕；修改后重新加载当前弹幕库。",
                    () => settings.WithRelated, value => settings.WithRelated = value, after: RefreshDanmakuRows));
        }

        var sourceRows = new List<SettingRow>();
        if (configured)
        {
            sourceRows.Add(Choice("弹幕来源", DanmakuSources, () => settings.Source, value => settings.Source = value,
                "每个人填自己部署的 danmu_api；Momoka 不提供公共服务。两者合并会同时请求两个来源，"
                + "并消耗弹弹play 的共享额度。",
                after: RefreshDanmakuRows));
            if (DanmakuSourceRules.IncludesDanmuApi(settings.Source))
            {
                sourceRows.Add(Text("danmu_api 地址", "http://192.168.1.10:9321",
                    () => settings.DanmuApiBaseUrl, value => settings.DanmuApiBaseUrl = value,
                    "只填服务根地址，不要带令牌；令牌在下一行单独填写。",
                    after: RefreshDanmakuRows));
                var tokenRow = new SettingSecretRow("danmu_api 令牌",
                    value => settings.ProtectedDanmuApiToken = DanmuApiToken.Protect(_secretProtector, value),
                    () => !string.IsNullOrEmpty(settings.ProtectedDanmuApiToken),
                    TrySaveDanmuApiToken, RefreshDanmakuRows);
                tokenRow.Restate("可选。服务未启用令牌时留空；保存后不回填明文，也不进入配置备份。");
                sourceRows.Add(tokenRow);
            }
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
                displayRows.Add(Toggle("弹幕随窗口缩放", "开启时随画面高度缩放；关闭时保持固定像素字号。",
                    () => settings.ScaleWithWindow, value => settings.ScaleWithWindow = value, after: ApplyDanmakuSettings));
                displayRows.Add(Toggle("防挡字幕", "字幕可见时为底部预留 20% 的画面；关闭字幕后自动恢复显示区域。",
                    () => settings.AvoidSubtitles, value => settings.AvoidSubtitles = value, after: ApplyDanmakuSettings));
                displayRows.Add(Choice("弹幕密度", DanmakuDensityModes, () => settings.DensityMode, value => settings.DensityMode = value,
                    "正常优先留出行距，较多缩小行距，重叠允许复用已占用的行；均保留同屏上限。", after: RefreshDanmakuRows));
                if (settings.DensityMode != 2)
                    displayRows.Add(Toggle("智能防挡弹幕", "在本地避免弹幕相互重叠和追尾。",
                        () => settings.PreventOverlap, value => settings.PreventOverlap = value, after: ApplyDanmakuSettings));
                displayRows.Add(Slider("不透明度（%）", 10, 100, 5, () => settings.Opacity, value => settings.Opacity = value, after: ApplyDanmakuSettings));
                displayRows.Add(Slider("字号", 20, 64, 2, () => settings.FontSize, value => settings.FontSize = value,
                    "独立于字幕字号；缩放方式由「弹幕随窗口缩放」控制。", after: ApplyDanmakuSettings));
                displayRows.Add(Slider("弹幕速度（倍）", 0.5, 2, 0.1, () => settings.Speed, value => settings.Speed = value, after: ApplyDanmakuSettings));
                displayRows.Add(Toggle("弹幕速度同步播放倍数", "开启时弹幕随播放倍数加速；关闭时补偿播放倍数，保持弹幕移动速度。",
                    () => settings.SpeedSyncPlayback, value => settings.SpeedSyncPlayback = value, after: ApplyDanmakuSettings));
                displayRows.Add(DanmakuFont(settings));
                displayRows.Add(Toggle("弹幕粗体", "使用所选字体的粗体。", () => settings.Bold, value => settings.Bold = value, after: ApplyDanmakuSettings));
                displayRows.Add(Choice("描边类型", DanmakuBorderStyles, () => settings.BorderStyle, value => settings.BorderStyle = value,
                    "描边、加粗黑边或右下方 45° 投影。", after: ApplyDanmakuSettings));
                displayRows.Add(Slider("显示区域（%）", 25, 100, 25, () => settings.Area, value => settings.Area = value,
                    "25% 顶部四分之一、50% 半屏、75% 四分之三、100% 全屏。", after: ApplyDanmakuSettings));
                displayRows.Add(Slider("同屏弹幕上限", 10, 100, 10, () => settings.Density, value => settings.Density = (int)value,
                    "达到上限后丢弃新弹幕，不保证所有弹幕都能显示。", after: ApplyDanmakuSettings));
                displayRows.Add(Fact("恢复弹幕外观默认", "只恢复显示外观，保留来源连接、显示开关和屏蔽词。", "", "恢复默认", () =>
                {
                    settings.RestoreAppearance();
                    Save();
                    RefreshDanmakuRows();
                }));

                typeRows.Add(Toggle("滚动弹幕", "显示从右向左移动的弹幕。", () => settings.Scroll, value => settings.Scroll = value, after: ApplyDanmakuSettings));
                typeRows.Add(Toggle("顶部弹幕", "显示固定在顶部的弹幕。", () => settings.Top, value => settings.Top = value, after: ApplyDanmakuSettings));
                typeRows.Add(Toggle("底部弹幕", "显示固定在所选显示区域底部的弹幕。", () => settings.Bottom, value => settings.Bottom = value, after: ApplyDanmakuSettings));
                typeRows.Add(Toggle("彩色弹幕", "关闭后屏蔽非白色弹幕。", () => settings.Color, value => settings.Color = value, after: ApplyDanmakuSettings));
                typeRows.Add(Toggle("合并重复弹幕", "相同内容在 10 秒内只保留第一条。", () => settings.Deduplicate, value => settings.Deduplicate = value, after: ApplyDanmakuSettings));
                typeRows.Add(new SettingTextRow("屏蔽词", "多个词用逗号分隔，不区分英文大小写；与播放面板共用并同步保存同一份本机列表。", "输入不想看到的词", settings.BlockedWords,
                    typed => { settings.BlockedWords = typed; settings.Normalize(); return settings.BlockedWords; }, () => { Save(); ApplyDanmakuSettings(); }));
            }
        }

        return
        [
            new SettingSection(DanmakuCategory, "弹幕",
                "参考 B 站的显示与屏蔽设置。弹幕使用独立画面叠加层，不占用字幕轨道；支持内置播放器的集成和独占模式。",
                rows),
            new SettingSection(DanmakuCategory, "来源与聚合",
                "弹弹play 使用 Momoka 内置凭据；danmu_api 需要你自己部署并填写地址。"
                + "两者合并会同时请求两个来源，并消耗弹弹play 的共享额度。",
                sourceRows),
            new SettingSection(DanmakuCategory, "显示与外观",
                "弹幕开关、输入栏和画面上的大小与疏密；改完立刻作用到正在播的片子。", displayRows),
            new SettingSection(DanmakuCategory, "类型与屏蔽",
                "哪几种弹幕显示出来，以及不想看到的词。", typeRows)
        ];
    }

    private SettingFontRow DanmakuFont(DanmakuSettings settings)
    {
        var row = new SettingFontRow("弹幕字体", "搜索这台机器安装的字体；与字幕字体独立。", settings.FontFamily,
            value => settings.FontFamily = value, () => { Save(); ApplyDanmakuSettings(); });
        if (_fonts is { Ready.Families.Count: > 0 } library) row.Fill(library.Ready);
        _danmakuFont = row;
        return row;
    }

    private bool TrySaveDanmuApiToken()
    {
        if (_settings is null) return false;
        var saved = _settings.TrySave();
        SyncSaveState();
        return saved;
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
        _danmakuFont = existing.SelectMany(section => section.Rows).OfType<SettingFontRow>().FirstOrDefault();
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
        if (_settings is null || !IsReady) return;
        RefreshDanmakuPresentation();
        foreach (var row in Sections.Where(section => section.Category == DanmakuCategory).SelectMany(section => section.Rows).OfType<SettingTextRow>())
            if (row.Label == "屏蔽词") row.Reseed(Settings.Danmaku.BlockedWords);
    }

    private static void ApplyDanmakuSettings() => ShellPrefs.ApplyDanmaku();
}
