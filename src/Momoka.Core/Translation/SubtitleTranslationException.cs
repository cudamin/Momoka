namespace Momoka.Translation;

public enum SubtitleTranslationError
{
    InvalidConfiguration, TextTooLong, MissingApiKey, Unauthorized, RateLimited,
    ServiceUnavailable, UnsafeRedirect, InvalidResponse, TimedOut
}

/// <summary>只含固定的可展示错误，不携带 HTTP body、URL、请求头、密钥或 inner exception。</summary>
public sealed class SubtitleTranslationException : Exception
{
    internal SubtitleTranslationException(SubtitleTranslationError error, string message) : base(message) => Error = error;
    public SubtitleTranslationError Error { get; }
}
