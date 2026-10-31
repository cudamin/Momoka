using System.ComponentModel;
using System.Runtime.InteropServices;
using Momoka.Shell.Interop;
using Momoka.Shell.Windowing;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell;

internal static partial class CompositionPlaybackProbe
{
    internal static Task SaveScreenAsync(HostWindow window, string file) => SaveScreenAsync(window.Handle, file);

    /// <summary>框架设置窗口与原生播放窗口共用实际客户区、遮挡守卫和屏幕取样。</summary>
    internal static async Task SaveScreenAsync(IntPtr handle, string file)
    {
        var screen = CapturePixels(handle);
        await EncodeAsync(screen, file);
    }

    // Retained-frame edge evidence; compile-linked verbatim with capture/encoding boundary substitutes.
    internal static async Task<string> ReadEdgeEvidenceAsync(HostWindow window, string file, Action<string> write)
    {
        var frame = CapturePixels(window);
        var read = new char[4];
        var owned = true;
        var positions = new[] { 0.08, 0.25, 0.75, 0.92 };
        for (var index = 0; index < positions.Length; index++)
        {
            var column = (int)Math.Round((frame.Width - 1) * positions[index]);
            var row = (int)Math.Round((frame.Height - 1) * 0.4);
            var point = new NativePoint { X = frame.OriginX + column, Y = frame.OriginY + row };
            var under = Native.WindowFromPoint(point);
            var root = Native.GetAncestor(under, Native.GaRoot);
            var thread = Native.GetWindowThreadProcessId(under, out var process);
            var name = new char[256];
            var length = under == IntPtr.Zero ? 0 : Native.GetClassName(under, name, name.Length);
            read[index] = frame.At(positions[index], 0.4);
            owned &= root == window.Handle && process == (uint)Environment.ProcessId;
            write($"保留帧采样：客户区={column},{row}，屏幕={point.X},{point.Y}，RGB={frame.RgbAt(column, row)}"
                + $"，颜色={read[index]}，命中=0x{under:X}，根=0x{root:X}，PID={process}，线程={thread}"
                + $"，类={new string(name, 0, Math.Max(0, length))}，前台=0x{Native.GetForegroundWindow():X}");
        }
        // 失败帧也先保存；颜色、RGB 与 PNG 必须来自同一份捕获，不能重新采一次洗掉遮挡。
        await EncodeAsync(frame, file);
        write($"保留帧证据：{file}，客户区={frame.Width}x{frame.Height}，颜色={new string(read)}，归属={owned}");
        if (!owned) throw new InvalidOperationException("保留帧实际采样点被遮挡或不属于本次窗口；已保留同帧证据");
        return new string(read);
    }

    /// <summary>
    /// 整窗取样（含系统标题栏）。标题栏图标、窗口标题这类只在非客户区画的东西，客户区取样看不到 ——
    /// 「左上角那颗图标没有了」这条只能靠这一张。
    /// </summary>
    internal static async Task SaveWindowScreenAsync(IntPtr handle, string file)
    {
        var screen = CaptureWindowPixels(handle);
        await EncodeAsync(screen, file);
    }

    private static async Task EncodeAsync(ScreenPixels screen, string file)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)screen.Width, (uint)screen.Height, 96, 96, screen.Bytes);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var output = File.Create(file);
        await stream.AsStreamForRead().CopyToAsync(output);
    }

    /// <summary>深色界面中指定文字矩形的亮像素数；只检查实际呈现，不用控件尺寸冒充已画出文字。</summary>
    internal static int BrightPixels(HostWindow window, Windows.Foundation.Rect bounds) => BrightPixels(window.Handle, bounds);

    internal static int BrightPixels(IntPtr handle, Windows.Foundation.Rect bounds)
    {
        var screen = CapturePixels(handle);
        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        var right = (int)Math.Ceiling(bounds.Right);
        var bottom = (int)Math.Ceiling(bounds.Bottom);
        if (left < 0 || top < 0 || right > screen.Width || bottom > screen.Height || left >= right || top >= bottom)
            throw new InvalidOperationException("文字像素检查的矩形不在客户区内");
        var count = 0;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var index = (y * screen.Width + x) * 4;
                if (screen.Bytes[index] >= 180 && screen.Bytes[index + 1] >= 180 && screen.Bytes[index + 2] >= 180) count++;
            }
        return count;
    }

    // GetPixel 每问一点都会等待屏幕，连续读点既跨帧又堵住 UI 提交。
    // 一次 BitBlt 取得同一帧，后续颜色判断只读内存。
    private static ScreenPixels CapturePixels(HostWindow window) => CapturePixels(window.Handle);

    private static ScreenPixels CapturePixels(IntPtr handle)
    {
        if (!Native.GetClientRect(handle, out var client) || client.Width <= 0 || client.Height <= 0)
            throw new InvalidOperationException("像素探针无法读取客户区");
        var origin = new NativePoint();
        if (!Native.ClientToScreen(handle, ref origin))
            throw new InvalidOperationException("像素探针无法定位客户区");
        GuardVisible(handle, origin.X, origin.Y, client.Width, client.Height);
        return CopyScreen(origin.X, origin.Y, client.Width, client.Height);
    }

    private static ScreenPixels CaptureWindowPixels(IntPtr handle)
    {
        if (!Native.GetVisibleFrame(handle, out var frame) || frame.Width <= 0 || frame.Height <= 0)
            throw new InvalidOperationException("像素探针无法读取窗口边框");
        // 圆角四角是透明的：取样点往里缩 4 像素，别把桌面的窗口当成这扇窗。
        GuardVisible(handle, frame.Left + 4, frame.Top + 4, frame.Width - 8, frame.Height - 8);
        return CopyScreen(frame.Left, frame.Top, frame.Width, frame.Height);
    }

    /// <summary>四个取样点都得落在这扇窗上（<see cref="Native.GetAncestor"/> 取顶层窗口比）。</summary>
    private static void GuardVisible(IntPtr handle, int x, int y, int width, int height)
    {
        foreach (var (column, row) in new[] { (0.05, 0.2), (0.95, 0.2), (0.05, 0.8), (0.95, 0.8) })
        {
            var point = new NativePoint
            {
                X = x + (int)(width * column),
                Y = y + (int)(height * row)
            };
            if (Native.GetAncestor(Native.WindowFromPoint(point), 2) != handle)
                throw new InvalidOperationException("像素探针窗口被遮挡或移到屏外，不能把其他窗口的像素当作视频");
        }
    }

    private static ScreenPixels CopyScreen(int x, int y, int width, int height)
    {
        var screen = Native.GetDC(IntPtr.Zero);
        var memory = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            if (screen == IntPtr.Zero) throw new Win32Exception();
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw new Win32Exception();
            var info = new ProbeBitmapInfo
            {
                Size = (uint)Marshal.SizeOf<ProbeBitmapInfo>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            };
            bitmap = CreateDIBSection(memory, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception();
            previous = SelectObject(memory, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception();
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, 0x00CC0020))
                throw new Win32Exception();
            if (!GdiFlush()) throw new Win32Exception();
            var bytes = new byte[checked(width * height * 4)];
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            return new ScreenPixels(width, height, bytes) { OriginX = x, OriginY = y };
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private sealed record ScreenPixels(int Width, int Height, byte[] Bytes)
    {
        internal int OriginX { get; init; }
        internal int OriginY { get; init; }
        internal string RgbAt(int column, int row) => CompositionProbePixels.Rgb(Bytes, Width, Height, column, row);

        internal char At(double x, double y)
        {
            var column = (int)Math.Round((Width - 1) * Math.Clamp(x, 0, 1));
            var row = (int)Math.Round((Height - 1) * Math.Clamp(y, 0, 1));
            return ColorLetter(CompositionProbePixels.ColorRef(Bytes, Width, Height, column, row));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeBitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateCompatibleDC(IntPtr device);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateDIBSection(IntPtr device, ref ProbeBitmapInfo info, uint usage,
        out IntPtr bits, IntPtr section, uint offset);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr SelectObject(IntPtr device, IntPtr item);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GdiFlush();
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr item);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr device);
}
