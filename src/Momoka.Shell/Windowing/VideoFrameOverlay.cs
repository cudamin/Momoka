using System.ComponentModel;
using System.Runtime.InteropServices;
using Momoka.Playback;
using Momoka.Shell.Interop;

namespace Momoka.Shell.Windowing;

/// <summary>只在视频换尺寸时短暂承接画面的无输入层；像素与窗口位置一次交给 DWM，不依赖岛布局。</summary>
internal sealed partial class VideoFrameOverlay : IDisposable
{
    private const string ClassName = "MomokaVideoFrame";
    private const int Transparent = 0x20;
    private const uint UlwAlpha = 4;
    private const uint LwaAlpha = 2;
    private static readonly WindowProcedure Procedure = Dispatch;
    private static bool _registered;
    private IntPtr _window;

    /// <summary>
    /// 这一层已经画好的那块位图：内存 DC 与 DIB（DIB 一直选在 DC 里，<c>UpdateLayeredWindow</c> 直接读它）。
    /// <para>
    /// <b>2026-11-08 起留着不删。</b>从前每次 <c>Show</c> 现建一块、用完即弃 —— 一次性用完就撤的场合那是最省事
    /// 的写法，但退场淡出要在这块画好之后连拧十几拍 alpha，每拍重画一遍整帧等于把一次淡出做成十几次截图。
    /// 现在只有尺寸或底色变了才重画，其余每一拍只换 blend 里的常量 alpha。
    /// </para>
    /// </summary>
    private IntPtr _memory;
    private IntPtr _bitmap;
    private IntPtr _oldBitmap;
    private NativeRect _painted;
    private uint _paintedSurround;
    private bool _paintedOnce;

    /// <summary>要盖的那一帧；纯色模式（<see cref="_solid"/>）时为空。</summary>
    private readonly VideoFrame? _frame;

    /// <summary>
    /// 这一帧按<b>铺满</b>摆（cover，超出的方向裁掉）还是按 contain 摆（留边）。默认 contain。
    /// <para>
    /// 两条起播整屏的路要 cover：那块层显示的是加载遮罩垫底的背景图，而遮罩里那张图是 <c>UniformToFill</c>
    /// —— 覆盖层留边、遮罩裁切，撤层那一刻图上会跳一下（2026-09-25，用户令「不要黑屏，主页和背景图无缝切换」）。
    /// 视频画面那一趟（窗口切换抓的帧）仍旧 contain，与留帧的默认摆法同一套。
    /// </para>
    /// </summary>
    private readonly bool _cover;

    /// <summary>
    /// 无帧的纯色模式：整块按 <see cref="_fill"/> 铺满。起播自动全屏那一趟还在加载、抓不到画面帧，
    /// 用加载遮罩同色的纯色层盖住窗口长到全屏那一拍（见 <c>PlayerPage.ChangeWindowAsync</c>）。
    /// </summary>
    private readonly bool _solid;

    /// <summary>纯色模式的填充色，分层窗口的 BGRA（不透明，见 <see cref="Show"/> 的 ULW_OPAQUE）。</summary>
    private readonly uint _fill;

    private VideoFrameOverlay(IntPtr owner)
    {
        if (!_registered)
        {
            var name = Marshal.StringToHGlobalUni(ClassName);
            try
            {
                var windowClass = new WindowClass
                {
                    Size = (uint)Marshal.SizeOf<WindowClass>(),
                    Instance = Native.GetModuleHandle(null),
                    ClassName = name,
                    Procedure = Marshal.GetFunctionPointerForDelegate(Procedure)
                };
                if (Native.RegisterClassEx(ref windowClass) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                _registered = true;
            }
            finally { Marshal.FreeHGlobal(name); }
        }

        _window = Native.CreateWindowEx(
            Native.WsExLayered | Native.WsExToolWindow | Native.WsExNoActivate | Transparent,
            ClassName, "", Native.WsPopup, 0, 0, 1, 1, owner, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>盖一帧真画面（视频换尺寸／进退全屏那一拍的承接，按 contain 摆）。</summary>
    internal VideoFrameOverlay(IntPtr owner, VideoFrame frame, bool cover = false) : this(owner)
    {
        _frame = frame;
        _cover = cover;
    }

    /// <summary>盖一块纯色（没有画面可抓时，如起播加载中长到全屏）。<paramref name="fill"/> 是不透明 BGRA。</summary>
    internal VideoFrameOverlay(IntPtr owner, uint fill) : this(owner)
    {
        _solid = true;
        _fill = fill;
    }

    /// <summary>
    /// 把这一层铺到屏幕上的 <paramref name="bounds"/>。
    /// <para>
    /// <paramref name="alpha"/> 是整层的<b>不透明度</b>（255 为不透明）。255 那一档与改动前逐字一致（一次
    /// <c>UpdateLayeredWindow</c>，像素现画现传）；小于 255 时改为逐拍只调
    /// <c>SetLayeredWindowAttributes</c> —— 位图不重画也不重传。为什么不是 ULW 自带的
    /// <c>BLENDFUNCTION.SourceConstantAlpha</c>，见 <see cref="Fade"/>。
    /// </para>
    /// <para>
    /// <paramref name="surround"/> 是<b>画面之外那一圈</b>的底色（BGRA，0 为透空）。只有退场改铺新客户区时才给
    /// 值：那时窗口比播放时的形状高，多出来的那几道若透空，画面看着就是一条悬空的带子 —— 补上播放页的舞台色，
    /// 读作「播放页还在，只是化了」。画面按 contain 摆进新的 bounds，宽度与中心不变时它在屏上的位置分毫不动。
    /// </para>
    /// </summary>
    internal unsafe void Show(NativeRect bounds, bool topmost, byte alpha = 255, uint surround = 0)
    {
        if (_window == IntPtr.Zero || bounds.Width <= 0 || bounds.Height <= 0) return;
        var repaint = !_paintedOnce || !Same(_painted, bounds) || _paintedSurround != surround;
        if (repaint) Paint(bounds, surround);
        if (!_paintedOnce || _memory == IntPtr.Zero) return;
        if (repaint) Present(bounds, topmost);
        if (alpha < 255) Fade(alpha);
    }

    /// <summary>
    /// 整层的不透明度（<c>LWA_ALPHA</c>）。<b>这是本机上唯一真能淡掉这一层的办法。</b>
    /// <para>
    /// 2026-11-08 实测（独立探针，纯红 400×400 分层窗口，同一块位图不换）：<c>UpdateLayeredWindow</c> 的
    /// <c>BLENDFUNCTION.SourceConstantAlpha</c> 从 255 一路降到 0，屏上像素<b>一个数都没变</b>，
    /// <c>AlphaFormat</c> 取 0 或 1 都一样；而 <c>SetLayeredWindowAttributes</c> 在 ULW 之后照常生效 ——
    /// 同一个窗口 255→200→100→0 逐档变淡，读到的是「红 → 混进桌面底色 → 桌面原色」。所以淡出走这一支，
    /// 位图那一侧完全不动。
    /// </para>
    /// <para>
    /// 用它有一个前提：这一层此后不再重传像素。退场淡出正是这样用的（内容在改铺那一拍就定了），
    /// 别的几处（拖边保留帧、起播整屏覆盖层）从头到尾都传 255，一行 ULW 都不受影响。
    /// </para>
    /// </summary>
    private void Fade(byte alpha)
    {
        if (_window == IntPtr.Zero) return;
        if (!SetLayeredWindowAttributes(_window, 0, alpha, LwaAlpha))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>把画好的那块位图交给 DWM，并按 <paramref name="topmost"/> 摆好这一层。</summary>
    private unsafe void Present(NativeRect bounds, bool topmost)
    {
        var screen = Native.GetDC(IntPtr.Zero);
        try
        {
            var position = new NativePoint { X = bounds.Left, Y = bounds.Top };
            var size = new NativePoint { X = bounds.Width, Y = bounds.Height };
            var origin = new NativePoint();
            if (!UpdateLayeredWindow(_window, screen, ref position, ref size, _memory, ref origin, 0, IntPtr.Zero, UlwAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Native.SetWindowPos(_window, topmost ? Native.HwndTopMost : Native.HwndTop,
                0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate);
            Native.ShowWindow(_window, Native.SwShowNoActivate);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>把 <paramref name="bounds"/> 那一块画进新位图；尺寸与底色没变时由 <see cref="Show"/> 跳过。</summary>
    private unsafe void Paint(NativeRect bounds, uint surround)
    {
        ReleaseBitmap();
        var screen = Native.GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var info = new BitmapInfo
        {
            Size = (uint)Marshal.SizeOf<BitmapInfo>(),
            Width = bounds.Width,
            Height = -bounds.Height,
            Planes = 1,
            BitCount = 32
        };
        var bitmap = CreateDIBSection(memory, ref info, 0, out var bits, IntPtr.Zero, 0);
        if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero)
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screen);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var old = SelectObject(memory, bitmap);
        try
        {
            // 先铺一层底：纯色模式是它自己的填充色，画面模式是调用方给的围边（默认透明）。
            new Span<uint>((void*)bits, bounds.Width * bounds.Height).Fill(_solid ? _fill : surround);
            if (!_solid && _frame is { } frame)
            {
                var picture = frame.Picture;
                var source = new BitmapInfo
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfo>(),
                    Width = frame.Stride / 4,
                    Height = -frame.Height,
                    Planes = 1,
                    BitCount = 32
                };
                SetStretchBltMode(memory, 4);
                SetBrushOrgEx(memory, 0, 0, IntPtr.Zero);
                fixed (byte* pixels = frame.Pixels)
                {
                    // 铺满那一档：目标恒是整块，改的是「从源里取哪一块」；留边那一档：源取可见画面，目标居中。
                    var from = _cover
                        ? VideoPresentation.FillSource((int)picture.Width, (int)picture.Height,
                            bounds.Width, bounds.Height, (int)picture.Left, (int)picture.Top)
                        : picture;
                    var scale = _cover
                        ? 1d
                        : VideoPresentation.FitScale(picture.Width, picture.Height, bounds.Width, bounds.Height, false);
                    var width = _cover ? bounds.Width : (int)Math.Round(picture.Width * scale);
                    var height = _cover ? bounds.Height : (int)Math.Round(picture.Height * scale);
                    if (StretchDIBits(memory, (bounds.Width - width) / 2, (bounds.Height - height) / 2,
                            width, height, (int)Math.Round(from.Left), (int)Math.Round(from.Top),
                            (int)Math.Round(from.Width), (int)Math.Round(from.Height),
                            (IntPtr)pixels, ref source, 0, 0x00CC0020) == 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            _memory = memory;
            _bitmap = bitmap;
            _oldBitmap = old;
            _painted = bounds;
            _paintedSurround = surround;
            _paintedOnce = true;
        }
        catch
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            throw;
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>放掉已经画好的那块位图。<see cref="Show"/> 换尺寸前、以及 <see cref="Dispose"/> 都走这里。</summary>
    private void ReleaseBitmap()
    {
        _paintedOnce = false;
        if (_memory != IntPtr.Zero)
        {
            if (_bitmap != IntPtr.Zero) SelectObject(_memory, _oldBitmap);
            DeleteDC(_memory);
            _memory = IntPtr.Zero;
            _oldBitmap = IntPtr.Zero;
        }
        if (_bitmap != IntPtr.Zero)
        {
            DeleteObject(_bitmap);
            _bitmap = IntPtr.Zero;
        }
    }

    private static bool Same(NativeRect left, NativeRect right) => left.Left == right.Left
        && left.Top == right.Top && left.Right == right.Right && left.Bottom == right.Bottom;

    internal static NativeRect ClientRect(IntPtr window)
    {
        Native.GetClientRect(window, out var client);
        var origin = new NativePoint();
        Native.ClientToScreen(window, ref origin);
        return new NativeRect
        {
            Left = origin.X,
            Top = origin.Y,
            Right = origin.X + client.Width,
            Bottom = origin.Y + client.Height
        };
    }

    internal static NativeRect FullscreenRect(IntPtr window)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return Native.GetMonitorInfo(Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest), ref info)
            ? info.Monitor : ClientRect(window);
    }

    /// <summary>
    /// 最大化的目标矩形：所在显示器的工作区（任务栏除外）。进全屏／最大化之前先按它摆覆盖层 ——
    /// <b>窗口还没变大，屏上已经是变完的样子</b>，合成器那一拍「新几何 + 旧内容」就没有空隙可露。
    /// </summary>
    internal static NativeRect WorkArea(IntPtr window)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return Native.GetMonitorInfo(Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest), ref info)
            ? info.Work : ClientRect(window);
    }

    internal static void Flush() => DwmFlush();

    public void Dispose()
    {
        ReleaseBitmap();
        if (_window == IntPtr.Zero) return;
        Native.DestroyWindow(_window);
        _window = IntPtr.Zero;
    }

    private static IntPtr Dispatch(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) => message switch
    {
        0x0084 => new IntPtr(-1),
        0x0021 => new IntPtr(3),
        _ => Native.DefWindowProc(window, message, wParam, lParam)
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr device);
    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr device, IntPtr item);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr item);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr device);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateDIBSection(IntPtr device, ref BitmapInfo info, uint usage,
        out IntPtr bits, IntPtr section, uint offset);
    [LibraryImport("gdi32.dll")]
    private static partial int SetStretchBltMode(IntPtr device, int mode);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetBrushOrgEx(IntPtr device, int x, int y, IntPtr previous);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int StretchDIBits(IntPtr device, int x, int y, int width, int height,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, IntPtr bits,
        ref BitmapInfo info, uint usage, uint operation);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref NativePoint position,
        ref NativePoint size, IntPtr source, ref NativePoint origin, uint key, IntPtr blend, uint flags);
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmFlush();
}
