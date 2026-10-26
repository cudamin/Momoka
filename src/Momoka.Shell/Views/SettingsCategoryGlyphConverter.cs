using Microsoft.UI.Xaml.Data;

namespace Momoka.Shell.Views;

/// <summary>
/// 设置页左边那列分类的图标：分类名 → 一个 Segoe Fluent 字形，参考图里 QQ 那样每个分类带一个图标。
/// <para>
/// 分类还是一串字符串（<c>SettingsViewModel.Categories</c>，是 <c>SelectedCategory</c>、跳转、自检等十来处
/// 共用的那份），没有为了加图标改成对象 —— 那要动一大圈。这里只在渲染时把名字翻成一个字形，认不出的名字给个
/// 兜底字形（不炸、也不空）。名字要和 <c>SettingsViewModel</c> 里那份对得上，对不上就只是显示兜底图标。
/// </para>
/// <para>
/// 一个自己的文件、页面本地注册（<c>&lt;local:SettingsCategoryGlyphConverter x:Key="CategoryGlyph"/&gt;</c>），
/// 和 <see cref="SeekClockConverter"/> 一个路子。字形存的是码点整数（同 <c>PlayerViewModel</c> 里 <c>0xE768</c>
/// 那套），画的时候 <c>char.ConvertFromUtf32</c> 成字符串 —— 源码里不塞看不见的私用区字符。
/// </summary>
public sealed class SettingsCategoryGlyphConverter : IValueConverter
{
    /// <summary>
    /// Page：认不出的分类给一页通用的纸，不留空。以前兜底是齿轮，Jellyfin 一批分类没进表时
    /// 整列几乎全是同一个齿轮（用户 2026-10-19 当场指出）；齿轮只留给真正叫「常规／设置」的两项。
    /// </summary>
    private const int Fallback = 0xE7C3;

    private static readonly IReadOnlyDictionary<string, int> Glyphs = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["播放器"] = 0xE768,       // Play
        ["播放行为"] = 0xE81C,     // History（续播、进度）
        ["字幕"] = 0xE8D2,         // Font（字）
        ["弹弹play"] = 0xE8F2,     // Message
        ["视频输出"] = 0xE7F4,     // TVMonitor
        ["音频输出"] = 0xE767,     // Volume
        ["主页"] = 0xE80F,         // Home
        ["界面"] = 0xE771,         // Personalize
        ["快捷键"] = 0xE765,       // KeyboardClassic
        ["关于"] = 0xE946,         // Info
        ["服务器"] = 0xE753,       // Cloud（远端服务器）
        ["诊断"] = 0xE8A5,         // Document（日志）
        ["通知"] = 0xE7E7,         // Mail（转发出去的消息；与通知页自己的头图标同一个字形）
        ["控制台"] = 0xE9D9,       // ViewDashboard
        ["常规"] = 0xE713,         // Settings
        ["通用"] = 0xE713,         // Settings
        ["用户"] = 0xE716,         // People
        ["Emby Premiere"] = 0xE734, // FavoriteStar
        ["媒体库"] = 0xE8B7,       // Folder
        ["显示"] = 0xE7F4,         // TVMonitor
        ["元数据"] = 0xE8A5,       // Document
        ["NFO 设置"] = 0xE943,     // Code
        ["电视直播"] = 0xE7F4,     // TVMonitor
        ["数字录像机"] = 0xE714,   // Video
        ["网络"] = 0xE701,         // WiFi
        ["转码"] = 0xE8AB,         // Switch
        ["继续观看"] = 0xE81C,     // History
        ["流媒体"] = 0xE774,       // Globe
        ["特技播放"] = 0xE768,     // Play
        ["数据库"] = 0xE8F1,       // Library
        ["转换"] = 0xE8AB,         // Switch
        ["计划任务"] = 0xE787,     // Calendar
        ["日志"] = 0xE8A5,         // Document
        ["设备"] = 0xE7F4,         // TVMonitor
        ["活动"] = 0xE81C,         // History
        ["下载"] = 0xE896,         // Download
        ["相机上传"] = 0xE722,     // Camera
        ["插件"] = 0xE71D,         // AllApps（插件）
        ["我的插件"] = 0xE71D,     // AllApps（插件）
        ["目录"] = 0xE8F1,         // Library（插件目录）
        ["API 密钥"] = 0xE8D7,    // Permissions
        ["元数据管理器"] = 0xE8A5, // Document
        ["媒体资料管理器"] = 0xE70F, // Edit（铅笔，照 Jellyfin 网页端）
        ["品牌"] = 0xE790,         // Color
        ["设置"] = 0xE713,         // Settings
        ["任务计划"] = 0xE787,     // Calendar
        ["修改密码"] = 0xE72E,     // Lock
        ["播放偏好"] = 0xE768,     // Play
        ["外观"] = 0xE790,         // Color（调色板）
        ["网页控制台"] = 0xE774,   // Globe
        ["恢复默认"] = 0xE72C      // Refresh（还原、重来一遍）
    };

    public object Convert(object value, Type targetType, object parameter, string language) =>
        char.ConvertFromUtf32(value is string category && Glyphs.TryGetValue(category, out var code) ? code : Fallback);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
