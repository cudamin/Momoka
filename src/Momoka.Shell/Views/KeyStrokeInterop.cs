using Windows.System;
using Momoka.Playback;

namespace Momoka.Shell.Views;

/// <summary>
/// <see cref="VirtualKey"/> ↔ <see cref="Playback.ShortcutCatalog"/> 的 token 串，全项目**唯一**碰
/// <c>VirtualKey</c> 的地方 —— Core 那一层不认 WinRT，只认 token（见 <c>KeyStroke</c> 的注释）。
/// <para>
/// 只翻裸键，修饰键不在这里：<c>KeyRoutedEventArgs</c> 不带修饰键，Ctrl/Alt/Shift 是在按下那一刻由
/// <c>Native.Ctrl/Alt/ShiftHeld</c> 从系统读的（派发和捕获两头共用这套读法）。方括号没有 <c>VirtualKey</c>
/// 名字，用原始 OEM 码（219=VK_OEM_4「[」、221=VK_OEM_6「]」），和改造前那张 switch 表里的写法一致。
/// </para>
/// </summary>
internal static class KeyStrokeInterop
{
    /// <summary>Windows 键值翻成持久化 token；具体映射由 Core 与独占播放器共享。</summary>
    public static string? Token(VirtualKey key, bool extended = false) => ShortcutKeyCatalog.Token((int)key, extended);
}
