namespace Momoka.Shell;

/// <summary>自检与匿名系统外观检查共用的严格读数判据；连接标记不代表材质当前 Active。</summary>
internal static class SettingsBackdropCheck
{
    internal static bool Matches(bool supported, bool assigned, byte alpha, bool highContrast, bool effects) =>
        assigned == supported && alpha == (supported && !highContrast && effects ? 0x00 : 0xFF);
}
