using System.Security.Cryptography;
using Momoka.Configuration;

namespace Momoka.Danmaku;

/// <summary>danmu_api 访问令牌的 DPAPI 保护；明文只在进程内取用。</summary>
public static class DanmuApiToken
{
    public static string Protect(ISecretProtector protector, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "";
        try
        {
            var protectedText = protector.Protect(token.Trim());
            if (string.IsNullOrWhiteSpace(protectedText)) throw new InvalidOperationException();
            return protectedText;
        }
        catch (Exception)
        {
            // 保护器的异常可能夹带输入，不能向上携带，也不能记录。
            throw new CryptographicException("无法加密 danmu_api 令牌，原设置未更改。");
        }
    }

    /// <summary>空密文代表未设置；损坏或跨 Windows 用户的密文返回空并给出可操作原因。</summary>
    public static string Unprotect(ISecretProtector? protector, string? protectedToken, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(protectedToken)) return "";
        if (protector is null)
        {
            error = "danmu_api 令牌无法解密，请在设置中重新输入。";
            return "";
        }
        try { return protector.Unprotect(protectedToken) ?? ""; }
        catch (Exception)
        {
            error = "danmu_api 令牌无法解密，请在设置中重新输入。";
            return "";
        }
    }
}
