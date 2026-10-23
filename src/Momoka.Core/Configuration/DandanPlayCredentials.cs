namespace Momoka.Configuration;

/// <summary>密钥只在进程内解开，不送 URL、日志、子进程参数或配置备份。</summary>
public sealed class DandanPlayCredentials(ISecretProtector protector)
{
    public void SetSecret(DanmakuSettings settings, string secret) =>
        settings.ProtectedAppSecret = string.IsNullOrWhiteSpace(secret) ? "" : protector.Protect(secret.Trim());

    public string GetSecret(DanmakuSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProtectedAppSecret)) return "";
        try { return protector.Unprotect(settings.ProtectedAppSecret); }
        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or ArgumentException or FormatException)
        {
            // 从其他 Windows 用户复制的设置不能解密；由配置入口要求重新输入，异常内容也不写日志。
            return "";
        }
    }
}
