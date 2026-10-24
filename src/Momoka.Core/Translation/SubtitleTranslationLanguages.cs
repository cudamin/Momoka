using Momoka.Configuration;

namespace Momoka.Translation;

/// <summary>本地、封闭的语言映射；未知值不进入 URL 或 AI system 提示。</summary>
public static class SubtitleTranslationLanguages
{
    // 选择三家均可表达的常用语言，不猜测供应商尚未核对的语言码。
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "af", "ar", "bg", "bn", "ca", "cs", "cy", "da", "de", "el", "en", "es", "et", "fa",
        "fi", "fr", "gu", "he", "hi", "hr", "hu", "id", "is", "it", "ja", "kn", "ko", "lt",
        "lv", "ml", "mr", "ms", "mt", "nl", "or", "pa", "pl", "pt", "ro", "ru", "sk", "sl",
        "sv", "sw", "ta", "te", "th", "tr", "uk", "ur", "vi"
    };

    public static bool TryMap(string? language, SubtitleTranslationProvider provider, bool allowAuto, out string mapped)
    {
        mapped = "";
        if (!Enum.IsDefined(provider) || string.IsNullOrWhiteSpace(language) || language.Length > 32) return false;
        var code = language.Trim().Replace('_', '-').ToLowerInvariant();
        if (code == "auto") return allowAuto; // 空 source 表示不发送 from/source，由服务识别。
        code = code switch
        {
            "zh" or "zh-cn" or "zh-hans" or "zh-sg" or "zho" or "chi" => "zh-CN",
            "zh-tw" or "zh-hant" or "zh-hk" or "zh-mo" => "zh-TW",
            "en-us" or "en-gb" or "eng" => "en",
            "ja-jp" or "jpn" => "ja",
            "ko-kr" or "kor" => "ko",
            "fr-fr" or "fra" or "fre" => "fr",
            "de-de" or "deu" or "ger" => "de",
            "es-es" or "es-mx" or "spa" => "es",
            "pt-br" or "pt-pt" or "por" => "pt",
            "ru-ru" or "rus" => "ru",
            "iw" => "he",
            "nb" or "nb-no" or "no" or "nor" => "no",
            "tl" or "fil" => "fil",
            "sr" or "sr-cyrl" => "sr",
            _ => code
        };
        mapped = code switch
        {
            "zh-CN" => provider == SubtitleTranslationProvider.Microsoft ? "zh-Hans" : "zh-CN",
            "zh-TW" => provider == SubtitleTranslationProvider.Microsoft ? "zh-Hant" : "zh-TW",
            "no" => provider == SubtitleTranslationProvider.Microsoft ? "nb" : "no",
            "fil" => provider == SubtitleTranslationProvider.Google ? "tl" : "fil",
            "sr" => provider == SubtitleTranslationProvider.Microsoft ? "sr-Cyrl" : "sr",
            _ when Common.Contains(code) => code,
            _ => ""
        };
        return mapped.Length > 0;
    }
}
