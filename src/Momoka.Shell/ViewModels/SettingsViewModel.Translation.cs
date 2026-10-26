using Momoka.Configuration;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    private SubtitleTranslationCredentials? _translationCredentials;
    private const string TranslationCardTitle = "实时字幕翻译";

    private SettingSection SubtitleTranslationCard()
    {
        var settings = Settings.SubtitleTranslation;
        var rows = new List<SettingRow>
        {
            Toggle("启用实时字幕翻译", "开启后把正在显示的文字字幕逐句发送给所选服务；需要 API 密钥，可能按量收费。"
                    + "原字幕保留，译文使用独立图层。不是语音识别；没有文字字幕或 PGS／VOBSUB 图形字幕时不能翻译。",
                () => settings.Enabled, value => settings.Enabled = value, after: RefreshSubtitleTranslationRows)
        };
        if (settings.Enabled)
        {
            rows.Add(Pick("翻译服务", "谷歌、微软需要各自的云翻译 API；AI 使用兼容 OpenAI 的接口，不会读取浏览器登录。",
                new[]
                {
                    ("谷歌 · Google Cloud Translation", SubtitleTranslationProvider.Google),
                    ("微软 · Microsoft Translator", SubtitleTranslationProvider.Microsoft),
                    ("AI · 兼容 OpenAI", SubtitleTranslationProvider.OpenAi)
                }, () => settings.Provider, value => settings.Provider = value,
                EqualityComparer<SubtitleTranslationProvider>.Default, after: RefreshSubtitleTranslationRows));
            rows.Add(Pick("字幕原语言", "自动识别由翻译服务判断；混合语言字幕可以手动指定。",
                TranslationLanguages.Prepend(("自动识别", "auto")), () => settings.SourceLanguage,
                value => settings.SourceLanguage = value, StringComparer.OrdinalIgnoreCase, after: ApplySubtitleTranslationSettings));
            rows.Add(Pick("翻译为", "已经是目标语言的台词仍可能计入服务用量；只翻译当前选择并显示的主字幕。",
                TranslationLanguages, () => settings.TargetLanguage, value => settings.TargetLanguage = value,
                StringComparer.OrdinalIgnoreCase, after: ApplySubtitleTranslationSettings));
            switch (settings.Provider)
            {
                case SubtitleTranslationProvider.Google:
                    rows.Add(TranslationText("谷歌 API 地址", SubtitleTranslationSettings.DefaultGoogleEndpoint,
                        () => settings.GoogleEndpoint, value => settings.GoogleEndpoint = value,
                        "默认使用 Google Cloud Translation v2。需要启用云翻译 API，并为密钥设置 API 与额度限制。"));
                    break;
                case SubtitleTranslationProvider.Microsoft:
                    rows.Add(TranslationText("微软 API 地址", SubtitleTranslationSettings.DefaultMicrosoftEndpoint,
                        () => settings.MicrosoftEndpoint, value => settings.MicrosoftEndpoint = value,
                        "默认使用 Azure AI Translator v3。选择与资源对应的端点。"));
                    rows.Add(TranslationText("微软资源区域", "例如 eastasia；全局资源可留空",
                        () => settings.MicrosoftRegion, value => settings.MicrosoftRegion = value,
                        "区域必须与 Azure Translator 资源一致；多服务资源通常需要填写。"));
                    break;
                case SubtitleTranslationProvider.OpenAi:
                    rows.Add(TranslationText("AI API 地址", SubtitleTranslationSettings.DefaultAiEndpoint,
                        () => settings.AiEndpoint, value => settings.AiEndpoint = value,
                        "填写兼容 OpenAI 的服务基址（含 /v1），不填 /chat/completions。只允许 HTTPS；本机模型可用 HTTP 回环地址。"
                            + "字幕会发送到此地址，请只使用可信服务。"));
                    rows.Add(TranslationText("AI 模型", SubtitleTranslationSettings.DefaultAiModel,
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
                rows.Add(new SettingSecretRow(label, value => credentials.SetApiKey(settings, provider, value),
                    () => provider switch
                    {
                        SubtitleTranslationProvider.Google => settings.HasGoogleApiKey,
                        SubtitleTranslationProvider.Microsoft => settings.HasMicrosoftApiKey,
                        _ => settings.HasAiApiKey
                    }, TrySaveTranslationKey, ApplySubtitleTranslationSettings));
            }
            rows.Add(Slider("译文字号", SubtitleTranslationSettings.MinFontSize, SubtitleTranslationSettings.MaxFontSize, 2,
                () => settings.FontSize, value => settings.FontSize = (int)value,
                "独立于原字幕字号，按画面高度缩放。", after: ApplySubtitleTranslationSettings));
            rows.Add(Slider("译文位置（%）", SubtitleTranslationSettings.MinPosition, SubtitleTranslationSettings.MaxPosition, 5,
                () => settings.Position, value => settings.Position = (int)value,
                "越大越靠下。默认放在原字幕上方；与原字幕重叠时向上调整。", after: ApplySubtitleTranslationSettings));
        }
        return new SettingSection("字幕", TranslationCardTitle,
            "自动保存，开关、服务与外观立即应用到当前播放。只保留最多 256 句的内存缓存，切片清除；不保存原文或译文文件。", rows);
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
        var card = Sections.FirstOrDefault(section => section.Title == TranslationCardTitle);
        if (!available)
        {
            if (card is not null) Sections.Remove(card);
        }
        else if (card is null)
        {
            var index = Sections.ToList().FindIndex(section => section.Category == "字幕");
            if (index >= 0) Sections.Insert(index + 1, SubtitleTranslationCard());
        }
        else card.UpdateRows(SubtitleTranslationCard().Rows);
    }

    private void RefreshSubtitleTranslationPresentation()
    {
        Sections.FirstOrDefault(section => section.Title == TranslationCardTitle)?.UpdateRows(SubtitleTranslationCard().Rows);
    }

    private static void ApplySubtitleTranslationSettings() => ShellPrefs.ApplySubtitleTranslation();
}
