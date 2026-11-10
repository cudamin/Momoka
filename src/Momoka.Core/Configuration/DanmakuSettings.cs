namespace Momoka.Configuration;

/// <summary>
/// 弹弹play 连接与弹幕偏好。服务默认关闭，应用凭据固定由 Momoka 内置端点下发（2026-10-27 起不再支持
/// 用户自己的 AppId／AppSecret），所以这里只有外观与匹配偏好，没有密钥字段。
/// </summary>
public sealed class DanmakuSettings
{
    public bool Enabled { get; set; }

    /// <summary>
    /// 弹幕来源。出厂「两个都用」（2026-11-08 用户令「把当前的系统设置设为默认」，此前只有弹弹play）：
    /// 合并模式下 danmu_api 没填地址时仍可用弹弹play（见 <c>DanmakuSourceRules.IsAvailable</c>），所以这个默认
    /// 不会把没配过 danmu_api 的人挡在门外。认不出来的值由 <see cref="Normalize"/> 拨回它。
    /// </summary>
    public DanmakuSourceMode Source { get; set; } = DanmakuSourceMode.Both;
    public string DanmuApiBaseUrl { get; set; } = "";
    public string ProtectedDanmuApiToken { get; set; } = "";

    public bool AutoMatch { get; set; } = true;
    public bool WithRelated { get; set; } = true;
    public bool Show { get; set; } = true;

    /// <summary>播放页的弹幕输入框；出厂关（2026-11-08 按当前设置抄成默认，此前是开）。</summary>
    public bool ShowInput { get; set; }
    public double Opacity { get; set; } = 70;
    public double FontSize { get; set; } = 36;
    public double Speed { get; set; } = 1;
    public double Area { get; set; } = 50;
    public int Density { get; set; } = 40;
    public bool ScaleWithWindow { get; set; } = true;
    public bool AvoidSubtitles { get; set; }
    public bool PreventOverlap { get; set; } = true;
    public bool SpeedSyncPlayback { get; set; } = true;
    public string FontFamily { get; set; } = "Microsoft YaHei";
    public bool Bold { get; set; }
    public int BorderStyle { get; set; }
    public int DensityMode { get; set; }
    public bool Scroll { get; set; } = true;
    public bool Top { get; set; } = true;
    public bool Bottom { get; set; } = true;
    public bool Color { get; set; } = true;
    public bool Deduplicate { get; set; } = true;
    public string BlockedWords { get; set; } = "";

    public void Normalize()
    {
        if (!Enum.IsDefined(Source)) Source = DanmakuSourceMode.Both;
        var address = DanmuApiBaseUrl ?? "";
        DanmuApiBaseUrl = address[..Math.Min(address.Length, 2048)].Trim();
        var token = ProtectedDanmuApiToken ?? "";
        ProtectedDanmuApiToken = token[..Math.Min(token.Length, 8192)].Trim();
        var words = BlockedWords ?? "";
        BlockedWords = words[..Math.Min(words.Length, 4000)];
        Opacity = Clamp(Opacity, 10, 100, 70);
        FontSize = Clamp(FontSize, 20, 64, 36);
        Speed = Clamp(Speed, 0.5, 2, 1);
        Area = Clamp(Area, 25, 100, 50);
        Density = Math.Clamp(Density, 10, 100);
        var font = (FontFamily ?? "").Trim();
        FontFamily = font.Length is > 0 and <= 128 && !font.Any(character => char.IsControl(character) || character is '{' or '}' or '\\')
            ? font : "Microsoft YaHei";
        if (BorderStyle is < 0 or > 2) BorderStyle = 0;
        if (DensityMode is < 0 or > 2) DensityMode = 0;
    }

    /// <summary>只重置画面外观；服务、来源、匹配、显示开关和屏蔽列表都保留。</summary>
    public void RestoreAppearance()
    {
        var defaults = new DanmakuSettings();
        Opacity = defaults.Opacity;
        FontSize = defaults.FontSize;
        Speed = defaults.Speed;
        Area = defaults.Area;
        Density = defaults.Density;
        ScaleWithWindow = defaults.ScaleWithWindow;
        AvoidSubtitles = defaults.AvoidSubtitles;
        PreventOverlap = defaults.PreventOverlap;
        SpeedSyncPlayback = defaults.SpeedSyncPlayback;
        FontFamily = defaults.FontFamily;
        Bold = defaults.Bold;
        BorderStyle = defaults.BorderStyle;
        DensityMode = defaults.DensityMode;
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
