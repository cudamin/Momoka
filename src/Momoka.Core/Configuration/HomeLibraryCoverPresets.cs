namespace Momoka.Configuration;

/// <summary>主页媒体库封面的客户端显示预设；存储、设置选项和绘制共用同一组标识。</summary>
public static class HomeLibraryCoverPresets
{
    public const string Stack = "stack";
    public const string Diagonal = "diagonal";
    public const string Wall = "wall";
    public const string Gradient = "gradient";

    public static string Normalize(string? value) => value switch
    {
        Stack or Diagonal or Wall or Gradient => value,
        _ => Stack
    };
}
