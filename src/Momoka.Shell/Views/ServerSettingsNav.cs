using Momoka.Emby;

namespace Momoka.Shell.Views;

/// <summary>
/// 服务器控制台左栏的一页。页码、路由和分区来自 Core 的 <see cref="ServerSettingsMenu"/>，这里只补一个
/// Segoe 字形 —— 网页端用的是 Material 图标名，换成 XAML 能画的字形。
/// </summary>
public sealed class ServerSettingsNavItem(
    string label, string route, string icon, ServerSettingsSection section, bool fromPlugin)
{
    public string Label { get; } = label;

    public string Route { get; } = route;

    public ServerSettingsSection Section { get; } = section;

    public bool FromPlugin { get; } = fromPlugin;

    public string Glyph { get; } = ServerSettingsGlyph.For(icon);
}

/// <summary>一组标题＋若干页，照网页端 <c>navDrawerCollapseSection</c> 的形状。</summary>
public sealed class ServerSettingsNavGroup(string name) : List<ServerSettingsNavItem>
{
    public string Name { get; } = name;
}

/// <summary>
/// 网页端管理菜单的图标名 → Segoe Fluent 字形。固定条目用 Core 里那串键；插件页拿服务器给的
/// <c>MenuIcon</c>，认不出就用拼图，不留空。
/// </summary>
internal static class ServerSettingsGlyph
{
    private const int Fallback = 0xE713;

    private static readonly IReadOnlyDictionary<string, int> Glyphs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = 0xE9D9,     // ViewDashboard
        ["settings"] = 0xE713,      // Settings
        ["users"] = 0xE716,         // People
        ["premiere"] = 0xE734,      // FavoriteStar
        ["library"] = 0xE8B7,       // Folder
        ["livetv"] = 0xE7F4,        // TVMonitor
        ["network"] = 0xE701,       // WiFi
        ["transcode"] = 0xE8AB,     // Switch
        ["database"] = 0xE8F1,      // Library（数据库）
        ["conversions"] = 0xE8AB,   // Switch
        ["tasks"] = 0xE787,         // Calendar
        ["logs"] = 0xE8A5,          // Document
        ["devices"] = 0xE7F4,       // TVMonitor
        ["downloads"] = 0xE896,     // Download
        ["camera"] = 0xE722,        // Camera
        ["plugins"] = 0xE9CE,       // Puzzle
        ["plugin"] = 0xE9CE,        // Puzzle
        ["apikey"] = 0xE8D7,        // Permissions
        ["metadata"] = 0xE8A5,      // Document

        // 插件页的 MenuIcon 是 Material 名字；常见的那几个认一下，其余落拼图。
        ["movie"] = 0xE8B2,         // Movies
        ["code"] = 0xE943,          // Code
        ["folder"] = 0xE8B7,        // Folder
        ["backup"] = 0xE777,        // History
        ["restore"] = 0xE777,       // History
        ["subtitles"] = 0xE8D2,     // Font（字幕）
        ["cloud"] = 0xE753,         // Cloud
        ["wifi"] = 0xE701,          // WiFi
        ["star"] = 0xE734,          // FavoriteStar
        ["people"] = 0xE716,        // People
        ["storage"] = 0xE8F1,       // Library
        ["schedule"] = 0xE787,      // Calendar
        ["photo_camera"] = 0xE722,  // Camera
        ["photo"] = 0xE722,         // Camera
        ["download"] = 0xE896,      // Download
        ["photo_library"] = 0xE8B9, // Photo
        ["cast"] = 0xE7F4,          // TVMonitor
        ["dvr"] = 0xE7F4            // TVMonitor
    };

    public static string For(string? icon) =>
        char.ConvertFromUtf32(icon is not null && Glyphs.TryGetValue(icon.Trim(), out var code) ? code : Fallback);
}
