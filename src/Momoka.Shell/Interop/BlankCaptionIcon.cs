using System.IO;
using Momoka.Diagnostics;

namespace Momoka.Shell.Interop;

/// <summary>
/// 一颗 16×16 的全透明小图标，专门占住标题栏画的那一档。
/// <para>
/// 系统画标题栏图标时的查找链（<see cref="Native.WmSetIcon"/> 那段）没有「什么都不画」这一档 —— 发空句柄
/// 只是退到下一环，窗口类或 exe 资源里的图标照样补回来。所以「摘掉图标」在这里只能做成「占住那一位、
/// 让画出来的每个像素都是透明的」。
/// </para>
/// <para>
/// 必须是 32bpp：头一版用的单色 1bpp（AND 屏全 1），DWM 不认那个透明掩码，标题栏上画出来的是一整块
/// 淡淡的暗色方块（2026-09-13 截图放大实量）；32bpp 的图标 DWM 按 alpha 通道合成，XOR 位数据全 0
/// 即每个像素 alpha 都是 0，才是真的看不见。造失败给回空句柄，标题栏退回老查找链画应用图标 ——
/// 和没摘一样，只是少化一层妆，不是错。
/// </para>
/// <para>
/// 进程里只造一颗、留着不销毁：设置窗口和「在 MoviePilot 搜索版本」那个窗口都会重开，句柄得一直有效
/// （2026-10-30 起两处共用这一颗；此前是设置窗口自己造、关窗口时销毁）。
/// </para>
/// </summary>
internal static class BlankCaptionIcon
{
    private static readonly Lazy<IntPtr> Lazy = new(Create, isThreadSafe: true);

    /// <summary>全透明小图标的句柄；造不出来是 <see cref="IntPtr.Zero"/>。</summary>
    internal static IntPtr Handle => Lazy.Value;

    private static IntPtr Create()
    {
        // 32bpp 全透明图标的位数据：BITMAPINFOHEADER + XOR（BGRA 全 0，alpha 全 0）+ AND 屏（全 0）。
        // 注意这必须是「单张图像」的那一段（ICO 目录里 ICONDIRENTRY 指向的数据），不带 ICONDIR/
        // ICONDIRENTRY 容器 —— 头一版把整个 .ico 容器喂了进去，函数从第一个字节按 BITMAPINFOHEADER
        // 解析不动，退回空句柄，标题栏就又画回了应用图标（2026-09-13 截图放大实量）。
        // biHeight 写的是 XOR 和 AND 两块的总行数，16×2 = 32。
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40);                          // biSize
        writer.Write(16);                          // biWidth
        writer.Write(32);                          // biHeight
        writer.Write((short)1);                    // biPlanes
        writer.Write((short)32);                   // biBitCount
        writer.Write(0);                           // biCompression = BI_RGB
        writer.Write(16 * 16 * 4 + 16 * 4);        // biSizeImage
        writer.Write(0);                           // biXPelsPerMeter
        writer.Write(0);                           // biYPelsPerMeter
        writer.Write(0);                           // biClrUsed
        writer.Write(0);                           // biClrImportant
        writer.Write(new byte[16 * 16 * 4]);       // XOR：BGRA 全 0 —— 每个像素的 alpha 都是 0
        writer.Write(new byte[16 * 4]);            // AND 屏：32bpp 的图标以 alpha 为准，全 0

        var bits = stream.ToArray();
        var icon = Native.CreateIconFromResourceEx(bits, (uint)bits.Length, true, 0x00030000, 0, 0, 0x1);
        if (icon == IntPtr.Zero) Log.Warn("界面", "全透明小图标造不出来，标题栏会照旧画应用图标");
        return icon;
    }
}
