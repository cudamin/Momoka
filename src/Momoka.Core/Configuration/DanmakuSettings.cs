namespace Momoka.Configuration;

/// <summary>弹弹play 连接与弹幕偏好。服务默认关闭；密钥只存 DPAPI 密文，不随偏好备份导出。</summary>
public sealed class DanmakuSettings
{
    /// <summary>用户提供的 Momoka 公开 AppId。密钥与服务授权不是这个 ID，仍需在本机配置。</summary>
    public const string DefaultAppId = "dav282msq5";

    public bool Enabled { get; set; }
    public string AppId { get; set; } = DefaultAppId;
    public string ProtectedAppSecret { get; set; } = "";
    public bool AutoMatch { get; set; } = true;
    public bool WithRelated { get; set; } = true;
    public bool Show { get; set; } = true;
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
        ProtectedAppSecret ??= "";
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
