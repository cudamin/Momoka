using System.Text.Json.Serialization;

namespace Momoka.Configuration;

public enum SubtitleTranslationProvider { Google, Microsoft, OpenAi }

/// <summary>默认不发送字幕；只保存加密密钥，原文字幕始终由原播放链管理。</summary>
public sealed class SubtitleTranslationSettings
{
    public const int MinFontSize = 20;
    public const int MaxFontSize = 64;
    public const int MinPosition = 10;
    public const int MaxPosition = 90;
    public const string DefaultGoogleEndpoint = "https://translation.googleapis.com/language/translate/v2";
    public const string DefaultMicrosoftEndpoint = "https://api.cognitive.microsofttranslator.com";
    public const string DefaultAiEndpoint = "https://api.openai.com/v1";
    public const string DefaultAiModel = "gpt-4.1-mini";

    public bool Enabled { get; set; }
    public SubtitleTranslationProvider Provider { get; set; } = SubtitleTranslationProvider.Google;
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "zh-CN";
    public string GoogleEndpoint { get; set; } = DefaultGoogleEndpoint;
    public string MicrosoftEndpoint { get; set; } = DefaultMicrosoftEndpoint;
    public string MicrosoftRegion { get; set; } = "";
    public string AiEndpoint { get; set; } = DefaultAiEndpoint;
    public string AiModel { get; set; } = DefaultAiModel;
    public string ProtectedGoogleApiKey { get; set; } = "";
    public string ProtectedMicrosoftApiKey { get; set; } = "";
    public string ProtectedAiApiKey { get; set; } = "";
    public int FontSize { get; set; } = 32;
    public int Position { get; set; } = 80;

    // 这里只表示已有密文；换 Windows 用户后能否解密由 Credentials 判断。
    [JsonIgnore] public bool HasGoogleApiKey => !string.IsNullOrWhiteSpace(ProtectedGoogleApiKey);
    [JsonIgnore] public bool HasMicrosoftApiKey => !string.IsNullOrWhiteSpace(ProtectedMicrosoftApiKey);
    [JsonIgnore] public bool HasAiApiKey => !string.IsNullOrWhiteSpace(ProtectedAiApiKey);

    public void Normalize()
    {
        if (!Enum.IsDefined(Provider)) Provider = SubtitleTranslationProvider.Google;
        SourceLanguage = DefaultIfBlank(SourceLanguage, "auto");
        TargetLanguage = DefaultIfBlank(TargetLanguage, "zh-CN");
        GoogleEndpoint = DefaultIfBlank(GoogleEndpoint, DefaultGoogleEndpoint);
        MicrosoftEndpoint = DefaultIfBlank(MicrosoftEndpoint, DefaultMicrosoftEndpoint);
        MicrosoftRegion = MicrosoftRegion?.Trim() ?? "";
        AiEndpoint = DefaultIfBlank(AiEndpoint, DefaultAiEndpoint);
        AiModel = DefaultIfBlank(AiModel, DefaultAiModel);
        ProtectedGoogleApiKey ??= "";
        ProtectedMicrosoftApiKey ??= "";
        ProtectedAiApiKey ??= "";
        FontSize = Math.Clamp(FontSize, MinFontSize, MaxFontSize);
        Position = Math.Clamp(Position, MinPosition, MaxPosition);
    }

    // 不把非法 URL 或未知语言静默换成另一个服务；Client 会在发请求前拒绝它们。
    private static string DefaultIfBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
