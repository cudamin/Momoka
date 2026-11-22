namespace Momoka.Configuration;

/// <summary>主页媒体库封面的客户端显示预设；存储、设置选项和绘制共用同一组标识。</summary>
public static class HomeLibraryCoverPresets
{
    public const string Stack = "stack";
    public const string Wall = "wall";
    public const string Gradient = "gradient";

    // 已退役的 diagonal 与空值、未知值一并回到叠卡，旧配置不阻塞启动。
    public static string Normalize(string? value) => value switch
    {
        Stack or Wall or Gradient => value,
        _ => Stack
    };
}
