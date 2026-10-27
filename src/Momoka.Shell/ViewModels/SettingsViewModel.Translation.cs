using Momoka.Configuration;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    private SubtitleTranslationCredentials? _translationCredentials;
    private const string TranslationCardTitle = "实时字幕翻译";

    /// <summary>翻译分类下三张卡的抬头：主开关、服务与密钥、译文外观。按标题对号原地更新行。</summary>
    private static readonly string[] TranslationCardTitles = [TranslationCardTitle, "服务与密钥", "译文外观"];

    private IReadOnlyList<SettingSection> SubtitleTranslationSections()
    {
        var settings = Settings.SubtitleTranslation;

        // 2026-10-27 拆卡（用户令「其他页也拆一下」）：开关与语言、连哪个服务、译文长什么样，原来九行挤在
        // 一张卡里。服务那几行（地址、区域、模型、密钥）随所选服务进出，单独一张更清楚。
        var mainRows = new List<SettingRow>
        {
            Toggle("启用实时字幕翻译", "开启后把正在显示的文字字幕逐句发送给所选服务；需要 API 密钥，可能按量收费。"
                    + "原字幕保留，译文使用独立图层。不是语音识别；没有文字字幕或 PGS／VOBSUB 图形字幕时不能翻译。",
                () => settings.Enabled, value => settings.Enabled = value, after: RefreshSubtitleTranslationRows)
        };

        var serviceRows = new List<SettingRow>();
        var providerRows = new List<SettingRow>();
        var appearanceRows = new List<SettingRow>();
        if (settings.Enabled)
        {
            serviceRows.Add(Pick("翻译服务", "谷歌、微软需要各自的云翻译 API；AI 使用兼容 OpenAI 的接口，不会读取浏览器登录。",
                new[]
                {
                    ("谷歌 · Google Cloud Translation", SubtitleTranslationProvider.Google),
                    ("微软 · Microsoft Translator", SubtitleTranslationProvider.Microsoft),
                    ("AI · 兼容 OpenAI", SubtitleTranslationProvider.OpenAi)
                }, () => settings.Provider, value => settings.Provider = value,
                EqualityComparer<SubtitleTranslationProvider>.Default, after: RefreshSubtitleTranslationRows));
            serviceRows.Add(Pick("字幕原语言", "自动识别由翻译服务判断；混合语言字幕可以手动指定。",
                TranslationLanguages.Prepend(("自动识别", "auto")), () => settings.SourceLanguage,
                value => settings.SourceLanguage = value, StringComparer.OrdinalIgnoreCase, after: ApplySubtitleTranslationSettings));
            serviceRows.Add(Pick("翻译为", "已经是目标语言的台词仍可能计入服务用量；只翻译当前选择并显示的主字幕。",
                TranslationLanguages, () => settings.TargetLanguage, value => settings.TargetLanguage = value,
                StringComparer.OrdinalIgnoreCase, after: ApplySubtitleTranslationSettings));
            switch (settings.Provider)
            {
                case SubtitleTranslationProvider.Google:
                    providerRows.Add(TranslationText("谷歌 API 地址", SubtitleTranslationSettings.DefaultGoogleEndpoint,
                        () => settings.GoogleEndpoint, value => settings.GoogleEndpoint = value,
                        "默认使用 Google Cloud Translation v2。需要启用云翻译 API，并为密钥设置 API 与额度限制。"));
                    break;
                case SubtitleTranslationProvider.Microsoft:
                    providerRows.Add(TranslationText("微软 API 地址", SubtitleTranslationSettings.DefaultMicrosoftEndpoint,
                        () => settings.MicrosoftEndpoint, value => settings.MicrosoftEndpoint = value,
                        "默认使用 Azure AI Translator v3。选择与资源对应的端点。"));
                    providerRows.Add(TranslationText("微软资源区域", "例如 eastasia；全局资源可留空",
                        () => settings.MicrosoftRegion, value => settings.MicrosoftRegion = value,
                        "区域必须与 Azure Translator 资源一致；多服务资源通常需要填写。"));
                    break;
                case SubtitleTranslationProvider.OpenAi:
                    providerRows.Add(TranslationText("AI API 地址", SubtitleTranslationSettings.DefaultAiEndpoint,
                        () => settings.AiEndpoint, value => settings.AiEndpoint = value,
                        "填写兼容 OpenAI 的服务基址（含 /v1），不填 /chat/completions。只允许 HTTPS；本机模型可用 HTTP 回环地址。"
                            + "字幕会发送到此地址，请只使用可信服务。"));
                    providerRows.Add(TranslationText("AI 模型", SubtitleTranslationSettings.DefaultAiModel,
                        () => settings.AiModel, value => settings.AiModel = value,
                        "填写该服务支持的模型名称。AI 翻译通常延迟和费用更高，不保证每句都能在消失前返回。"));
                    break;
            }
            if (_translationCredentials is { } credentials)
            {
                var provider = settings.Provider;
                var label = provider switch
                {
                    SubtitleTranslationProvider.Google => "谷歌 API 密钥",
                    SubtitleTranslationProvider.Microsoft => "微软 API 密钥",
                    _ => "AI API 密钥"
                };
                providerRows.Add(new SettingSecretRow(label, value => credentials.SetApiKey(settings, provider, value),
                    () => provider switch
                    {
                        SubtitleTranslationProvider.Google => settings.HasGoogleApiKey,
                        SubtitleTranslationProvider.Microsoft => settings.HasMicrosoftApiKey,
                        _ => settings.HasAiApiKey
                    }, TrySaveTranslationKey, ApplySubtitleTranslationSettings));
            }
            appearanceRows.Add(Slider("译文字号", SubtitleTranslationSettings.MinFontSize, SubtitleTranslationSettings.MaxFontSize, 2,
                () => settings.FontSize, value => settings.FontSize = (int)value,
                "独立于原字幕字号，按画面高度缩放。", after: ApplySubtitleTranslationSettings));
            appearanceRows.Add(Slider("译文位置（%）", SubtitleTranslationSettings.MinPosition, SubtitleTranslationSettings.MaxPosition, 5,
                () => settings.Position, value => settings.Position = (int)value,
                "越大越靠下。默认放在原字幕上方；与原字幕重叠时向上调整。", after: ApplySubtitleTranslationSettings));
        }

        // 服务卡的行表：先服务与语言，再所选服务自己的地址、区域／模型与密钥。
        serviceRows.AddRange(providerRows);

        return
        [
            new SettingSection("字幕", TranslationCardTitle,
                "把正在显示的文字字幕逐句翻成另一种语言。自动保存，开关、服务与外观立即应用到当前播放；"
                    + "只保留最多 256 句的内存缓存，切片清除，不保存原文或译文文件。", mainRows),
            new SettingSection("字幕", "服务与密钥",
                "连哪个翻译服务、用哪种语言、密钥存哪儿。地址随所选服务变化；密钥只保存在本机。", serviceRows),
            new SettingSection("字幕", "译文外观",
                "译文自己一层，字号和位置都不跟原字幕走。", appearanceRows)
        ];
    }

    private static readonly (string Label, string Value)[] TranslationLanguages =
    [
        ("简体中文", "zh-CN"), ("繁体中文", "zh-TW"), ("英语", "en"), ("日语", "ja"),
        ("韩语", "ko"), ("法语", "fr"), ("德语", "de"), ("西班牙语", "es"),
        ("俄语", "ru"), ("葡萄牙语", "pt"), ("意大利语", "it"), ("阿拉伯语", "ar")
    ];

    private SettingTextRow TranslationText(string label, string placeholder, Func<string> read, Action<string> write,
        string note) => new(label, note, placeholder, read(), typed =>
        {
            write(typed.Trim());
            return typed.Trim();
        }, () => { Save(); ApplySubtitleTranslationSettings(); });

    private bool TrySaveTranslationKey()
    {
        if (_settings is null) return false;
        var saved = _settings.TrySave();
        SyncSaveState();
        return saved;
    }

    private void RefreshSubtitleTranslationRows()
    {
        RefreshSubtitleTranslationPresentation();
        ApplySubtitleTranslationSettings();
    }

    private void RefreshSubtitleTranslationAvailability()
    {
        var available = Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv || Settings.Mpv.EnableIpc;
        var existing = Sections.Where(section => TranslationCardTitles.Contains(section.Title, StringComparer.Ordinal)).ToList();
        if (!available)
        {
            foreach (var section in existing) Sections.Remove(section);
        }
        else if (existing.Count == 0)
        {
            // 三张卡一起插在字幕分类的最后一张之后：字幕（挑轨）→ 字幕外观 → 编码与画面 → 实时字幕翻译 →
            // 服务与密钥 → 译文外观，与 ReloadAsync 的建卡次序同一处。
            var index = Sections.ToList().FindLastIndex(section => section.Category == "字幕");
            var insert = index < 0 ? Sections.Count : index + 1;
            foreach (var section in SubtitleTranslationSections()) Sections.Insert(insert++, section);
        }
    }

    private void RefreshSubtitleTranslationPresentation()
    {
        var existing = Sections.Where(section => TranslationCardTitles.Contains(section.Title, StringComparer.Ordinal)).ToArray();
        if (existing.Length == 0) return;
        foreach (var section in SubtitleTranslationSections())
            existing.FirstOrDefault(current => current.Title == section.Title)?.UpdateRows(section.Rows);
    }

    private static void ApplySubtitleTranslationSettings() => ShellPrefs.ApplySubtitleTranslation();
}
