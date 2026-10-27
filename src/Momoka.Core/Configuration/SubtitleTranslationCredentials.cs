namespace Momoka.Configuration;

/// <summary>明文只在进程内取用；父 Shell 注入每 Windows 用户的 DPAPI protector。</summary>
public sealed class SubtitleTranslationCredentials
{
    private readonly ISecretProtector _protector;

    public SubtitleTranslationCredentials(ISecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    public void SetGoogleApiKey(SubtitleTranslationSettings settings, string apiKey) =>
        SetApiKey(settings, SubtitleTranslationProvider.Google, apiKey);
    public string GetGoogleApiKey(SubtitleTranslationSettings settings) =>
        GetApiKey(settings, SubtitleTranslationProvider.Google);
    public void SetMicrosoftApiKey(SubtitleTranslationSettings settings, string apiKey) =>
        SetApiKey(settings, SubtitleTranslationProvider.Microsoft, apiKey);
    public string GetMicrosoftApiKey(SubtitleTranslationSettings settings) =>
        GetApiKey(settings, SubtitleTranslationProvider.Microsoft);
    public void SetAiApiKey(SubtitleTranslationSettings settings, string apiKey) =>
        SetApiKey(settings, SubtitleTranslationProvider.OpenAi, apiKey);
    public string GetAiApiKey(SubtitleTranslationSettings settings) =>
        GetApiKey(settings, SubtitleTranslationProvider.OpenAi);

    /// <summary>传空字符串清除；加密失败保留原密文，不保存明文或异常详情。</summary>
    public void SetApiKey(SubtitleTranslationSettings settings, SubtitleTranslationProvider provider, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateProvider(provider);
        string protectedKey;
        try
        {
            protectedKey = string.IsNullOrWhiteSpace(apiKey) ? "" : _protector.Protect(apiKey.Trim());
            if (!string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(protectedKey))
                throw new InvalidOperationException("Protector 未返回密文。");
        }
        catch (Exception)
        {
            // Protector 的异常可能含敏感输入，不能携带 inner exception 或记日志。
            throw new InvalidOperationException("无法加密字幕翻译密钥，原设置未更改。");
        }
        if (provider == SubtitleTranslationProvider.Google) settings.ProtectedGoogleApiKey = protectedKey;
        else if (provider == SubtitleTranslationProvider.Microsoft) settings.ProtectedMicrosoftApiKey = protectedKey;
        else settings.ProtectedAiApiKey = protectedKey;
    }

    /// <summary>空密文、损坏密文或其他 Windows 用户的密文返回空，要求重新输入。</summary>
    public string GetApiKey(SubtitleTranslationSettings settings, SubtitleTranslationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateProvider(provider);
        var value = provider switch
        {
            SubtitleTranslationProvider.Google => settings.ProtectedGoogleApiKey,
            SubtitleTranslationProvider.Microsoft => settings.ProtectedMicrosoftApiKey,
            _ => settings.ProtectedAiApiKey
        };
        if (string.IsNullOrWhiteSpace(value)) return "";
        try { return _protector.Unprotect(value) ?? ""; }
        catch (Exception)
        {
            // 静默退回待输入；不删除用户保存的密文（跨 Windows 账号复制的密文只能重新输入）。
            return "";
        }
    }

    private static void ValidateProvider(SubtitleTranslationProvider provider)
    {
        if (!Enum.IsDefined(provider)) throw new ArgumentOutOfRangeException(nameof(provider), "翻译服务无效。");
    }
}
