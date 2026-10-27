namespace Momoka.Configuration;

/// <summary>
/// 弹弹play 连接与弹幕偏好。服务默认关闭，应用凭据固定由 Momoka 内置端点下发（2026-10-27 起不再支持
/// 用户自己的 AppId／AppSecret），所以这里只有外观与匹配偏好，没有密钥字段。
/// </summary>
public sealed class DanmakuSettings
{
    public bool Enabled { get; set; }

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
