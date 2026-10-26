using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;

namespace Momoka.SubtitleTranslationTests;

internal static class ConfigurationTests
{
    public static void Register()
    {
        Test.Case("settings/defaults", () =>
        {
            var settings = new SubtitleTranslationSettings();
            Test.True(!settings.Enabled);
            Test.Equal(SubtitleTranslationProvider.Google, settings.Provider);
            Test.Equal("auto", settings.SourceLanguage);
            Test.Equal("zh-CN", settings.TargetLanguage);
            Test.Equal("https://translation.googleapis.com/language/translate/v2", settings.GoogleEndpoint);
            Test.Equal("https://api.cognitive.microsofttranslator.com", settings.MicrosoftEndpoint);
            Test.Equal("https://api.openai.com/v1", settings.AiEndpoint);
            Test.Equal("gpt-6-luna", settings.AiModel);
            Test.Equal(32, settings.FontSize);
            Test.Equal(80, settings.Position);
            Test.True(!settings.HasGoogleApiKey && !settings.HasMicrosoftApiKey && !settings.HasAiApiKey);
        });
        Test.Case("settings/normalize-null-and-bounds", () =>
        {
            var settings = new SubtitleTranslationSettings
            {
                Enabled = true,
                Provider = (SubtitleTranslationProvider)99,
                SourceLanguage = null!,
                TargetLanguage = " ",
                GoogleEndpoint = null!,
                MicrosoftEndpoint = " ",
                AiEndpoint = null!,
                AiModel = " ",
                MicrosoftRegion = null!,
                ProtectedAiApiKey = null!,
                ProtectedGoogleApiKey = null!,
                ProtectedMicrosoftApiKey = null!,
                FontSize = int.MaxValue,
                Position = int.MinValue
            };
            settings.Normalize();
            Test.True(settings.Enabled);
            Test.Equal(SubtitleTranslationProvider.Google, settings.Provider);
            Test.Equal("auto", settings.SourceLanguage);
            Test.Equal("zh-CN", settings.TargetLanguage);
            Test.Equal(SubtitleTranslationSettings.DefaultGoogleEndpoint, settings.GoogleEndpoint);
            Test.Equal(SubtitleTranslationSettings.DefaultMicrosoftEndpoint, settings.MicrosoftEndpoint);
            Test.Equal(SubtitleTranslationSettings.DefaultAiEndpoint, settings.AiEndpoint);
            Test.Equal(SubtitleTranslationSettings.DefaultAiModel, settings.AiModel);
            Test.Equal(64, settings.FontSize);
            Test.Equal(10, settings.Position);
            Test.Equal("", settings.MicrosoftRegion);
            Test.Equal("", settings.ProtectedAiApiKey);
            settings.FontSize = int.MinValue; settings.Position = int.MaxValue; settings.Normalize();
            Test.Equal(20, settings.FontSize); Test.Equal(90, settings.Position);
        });
        Test.Case("settings/normalize-preserves-custom-and-cipher", () =>
        {
            var settings = new SubtitleTranslationSettings
            {
                SourceLanguage = " ja ",
                TargetLanguage = " zh-Hant ",
                AiEndpoint = " http://localhost:9999/v1 ",
                AiModel = " custom-model ",
                MicrosoftRegion = " eastus ",
                ProtectedAiApiKey = " cipher unchanged "
            };
            settings.Normalize();
            Test.Equal("ja", settings.SourceLanguage); Test.Equal("zh-Hant", settings.TargetLanguage);
            Test.Equal("http://localhost:9999/v1", settings.AiEndpoint); Test.Equal("custom-model", settings.AiModel);
            Test.Equal("eastus", settings.MicrosoftRegion); Test.Equal(" cipher unchanged ", settings.ProtectedAiApiKey);
            var before = JsonSerializer.Serialize(settings); settings.Normalize(); Test.Equal(before, JsonSerializer.Serialize(settings));
        });
        Test.Case("settings/json-ignores-derived-secret-flags", () =>
        {
            var settings = new SubtitleTranslationSettings { ProtectedGoogleApiKey = "cipher" };
            var json = JsonSerializer.Serialize(settings);
            Test.True(!json.Contains("HasGoogleApiKey", StringComparison.Ordinal) && !json.Contains("HasMicrosoftApiKey", StringComparison.Ordinal) &&
                !json.Contains("HasAiApiKey", StringComparison.Ordinal));
            var read = JsonSerializer.Deserialize<SubtitleTranslationSettings>(json)!;
            Test.True(read.HasGoogleApiKey && !read.HasMicrosoftApiKey && !read.HasAiApiKey);
        });
        Test.Case("credentials/encrypted-roundtrip-three-providers", () =>
        {
            using var protector = new TestProtector();
            var credentials = new SubtitleTranslationCredentials(protector);
            var settings = new SubtitleTranslationSettings();
            credentials.SetGoogleApiKey(settings, " " + Fixture.Key + " ");
            credentials.SetMicrosoftApiKey(settings, Fixture.Key + "-ms");
            credentials.SetAiApiKey(settings, Fixture.Key + "-ai");
            Test.Equal(Fixture.Key, credentials.GetGoogleApiKey(settings));
            Test.Equal(Fixture.Key + "-ms", credentials.GetMicrosoftApiKey(settings));
            Test.Equal(Fixture.Key + "-ai", credentials.GetAiApiKey(settings));
            var json = JsonSerializer.Serialize(settings); Test.True(!json.Contains(Fixture.Key, StringComparison.Ordinal));
            var read = JsonSerializer.Deserialize<SubtitleTranslationSettings>(json)!;
            Test.Equal(Fixture.Key, credentials.GetApiKey(read, SubtitleTranslationProvider.Google));
            Test.True(settings.HasGoogleApiKey && settings.HasMicrosoftApiKey && settings.HasAiApiKey);
        });
        foreach (var provider in Enum.GetValues<SubtitleTranslationProvider>())
            Test.Case("credentials/clear/" + provider, () =>
            {
                using var protector = new TestProtector(); var credentials = new SubtitleTranslationCredentials(protector);
                var settings = new SubtitleTranslationSettings(); credentials.SetApiKey(settings, provider, Fixture.Key);
                credentials.SetApiKey(settings, provider, " "); Test.Equal("", credentials.GetApiKey(settings, provider));
                Test.True(!settings.HasGoogleApiKey && !settings.HasMicrosoftApiKey && !settings.HasAiApiKey);
            });
        Test.Case("credentials/corrupt-or-other-user-preserves-cipher", () =>
        {
            using var protector = new TestProtector(); using var otherUser = new TestProtector();
            var settings = new SubtitleTranslationSettings(); var credentials = new SubtitleTranslationCredentials(protector);
            credentials.SetAiApiKey(settings, Fixture.Key); var cipher = settings.ProtectedAiApiKey;
            Test.Equal("", new SubtitleTranslationCredentials(otherUser).GetAiApiKey(settings));
            Test.Equal(cipher, settings.ProtectedAiApiKey);
            settings.ProtectedAiApiKey = "bad cipher"; Test.Equal("", credentials.GetAiApiKey(settings));
            Test.Equal("bad cipher", settings.ProtectedAiApiKey);
        });
        Test.Case("credentials/protect-failure-sanitized-and-preserves-old", () =>
        {
            var settings = new SubtitleTranslationSettings { ProtectedAiApiKey = "old cipher" };
            var credentials = new SubtitleTranslationCredentials(new FailingProtector());
            var error = Test.Throws<InvalidOperationException>(() => credentials.SetAiApiKey(settings, Fixture.Key));
            Test.Safe(error); Test.Equal("old cipher", settings.ProtectedAiApiKey);
            Test.Equal("", credentials.GetAiApiKey(settings));
        });
        Test.Case("credentials/empty-protection-result-preserves-old", () =>
        {
            var settings = new SubtitleTranslationSettings { ProtectedAiApiKey = "old cipher" };
            var credentials = new SubtitleTranslationCredentials(new EmptyProtector());
            Test.Safe(Test.Throws<InvalidOperationException>(() => credentials.SetAiApiKey(settings, Fixture.Key)));
            Test.Equal("old cipher", settings.ProtectedAiApiKey);
        });
        Test.Case("credentials/invalid-provider-and-null-guard", () =>
        {
            var credentials = new SubtitleTranslationCredentials(PassthroughSecretProtector.Instance);
            Test.Throws<ArgumentNullException>(() => new SubtitleTranslationCredentials(null!));
            Test.Throws<ArgumentNullException>(() => credentials.GetGoogleApiKey(null!));
            Test.Throws<ArgumentOutOfRangeException>(() => credentials.SetApiKey(new(), (SubtitleTranslationProvider)99, Fixture.Key));
        });
    }
}

// 测试替身用随机进程内 AES-GCM；不是 DPAPI，不能代表真实用户切换验证。
internal sealed class TestProtector : ISecretProtector, IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    public string Protect(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText); var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[bytes.Length]; var tag = new byte[16];
        using var aes = new AesGcm(_key, 16); aes.Encrypt(nonce, bytes, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public string Unprotect(string cipherText)
    {
        var bytes = Convert.FromBase64String(cipherText); var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(_key, 16); aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
        return Encoding.UTF8.GetString(plain);
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}

internal sealed class FailingProtector : ISecretProtector
{
    public string Protect(string plainText) => throw new CryptographicException(Fixture.Key + Fixture.BodyMarker);
    public string Unprotect(string cipherText) => throw new CryptographicException(Fixture.Key + Fixture.BodyMarker);
}


internal sealed class EmptyProtector : ISecretProtector
{
    public string Protect(string plainText) => "";
    public string Unprotect(string cipherText) => "";
}
