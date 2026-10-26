namespace Momoka.Configuration;

/// <summary>弹弹play 凭据来源。Auto 只用于旧配置：加载时按有没有本机密钥落到内置或自定义。</summary>
public enum DanmakuCredentialSource
{
    Auto = 0,
    BuiltIn = 1,
    Custom = 2
}

/// <summary>弹弹play 连接与弹幕偏好。服务默认关闭；密钥只存 DPAPI 密文，不随偏好备份导出。</summary>
public sealed class DanmakuSettings
{
    /// <summary>Momoka 的公开 AppId。内置凭据由服务端下发 AppId 与密钥，这一项只在自定义凭据时由用户填写。</summary>
    public const string DefaultAppId = "dav282msq5";

    public bool Enabled { get; set; }

    /// <summary>装机走内置凭据；旧配置里存过 AppSecret 的会自动落到 Custom。</summary>
    public DanmakuCredentialSource CredentialSource { get; set; }

    public string AppId { get; set; } = DefaultAppId;
    public string ProtectedAppSecret { get; set; } = "";
    public bool AutoMatch { get; set; } = true;
    public bool WithRelated { get; set; } = true;
    public bool Show { get; set; } = true;
    public bool ShowInput { get; set; } = true;
    public double Opacity { get; set; } = 70;
    public double FontSize { get; set; } = 36;
    public double Speed { get; set; } = 1;
    public double Area { get; set; } = 50;
    public int Density { get; set; } = 40;
    public bool Scroll { get; set; } = true;
    public bool Top { get; set; } = true;
    public bool Bottom { get; set; } = true;
    public bool Color { get; set; } = true;
    public bool Deduplicate { get; set; } = true;
    public string BlockedWords { get; set; } = "";

    public void Normalize()
    {
        AppId = (AppId ?? "").Trim();
        if (AppId.Length == 0) AppId = DefaultAppId;
        ProtectedAppSecret ??= "";
        if (!Enum.IsDefined(CredentialSource)) CredentialSource = DanmakuCredentialSource.Auto;
        if (CredentialSource == DanmakuCredentialSource.Auto)
            CredentialSource = ProtectedAppSecret.Length > 0
                ? DanmakuCredentialSource.Custom
                : DanmakuCredentialSource.BuiltIn;
        var words = BlockedWords ?? "";
        BlockedWords = words[..Math.Min(words.Length, 4000)];
        Opacity = Clamp(Opacity, 10, 100, 70);
        FontSize = Clamp(FontSize, 20, 64, 36);
        Speed = Clamp(Speed, 0.5, 2, 1);
        Area = Clamp(Area, 25, 100, 50);
        Density = Math.Clamp(Density, 10, 100);
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
