using System.Runtime.InteropServices;
using Momoka.Tests;

namespace Momoka.Mpv;

// Managed-only endpoints for the source-linked handle. No DllImport or native library binding.
internal static partial class LibMpvNative
{
    internal static NativeBoundary Boundary { get; set; } = new();

    internal static IntPtr mpv_wait_event(IntPtr context, double timeout) =>
        throw new AssertionException("R048 禁止启动原生事件线程");

    internal static int mpv_observe_property(IntPtr context, ulong id, string name, int format) =>
        throw new AssertionException("R048 不调用 Start/原生属性订阅");

    internal static int mpv_command(IntPtr context, IntPtr[] args)
    {
        var command = args.TakeWhile(pointer => pointer != IntPtr.Zero)
            .Select(pointer => Marshal.PtrToStringUTF8(pointer) ?? "").ToArray();
        Boundary.Commands.Add(command);
        Boundary.OnCommand?.Invoke(command);
        return 0;
    }

    internal static int mpv_command_ret(IntPtr context, IntPtr[] args, IntPtr result) =>
        throw new AssertionException("R048 不抓真实视频帧");

    internal static int mpv_set_property_string(IntPtr context, string name, string data)
    {
        Boundary.Writes.Add(name);
        return 0;
    }

    internal static IntPtr mpv_get_property_string(IntPtr context, string name)
    {
        Boundary.Reads.Add(name);
        // The fake standalone file has a video track; profile-list is intentionally unavailable.
        return name == "vid" ? Marshal.StringToCoTaskMemUTF8("1") : IntPtr.Zero;
    }

    internal static int mpv_get_property(IntPtr context, string name, int format, IntPtr data)
    {
        Boundary.Reads.Add(name);
        if (name is not ("chapter-list" or "track-list" or "demuxer-cache-state"))
            throw new AssertionException("未安排的节点读取：" + name);
        return -10; // MPV_ERROR_PROPERTY_UNAVAILABLE; the real node reader runs its fallback.
    }

    internal static int mpv_get_property_double(IntPtr context, string name, int format, out double data)
    {
        Boundary.Reads.Add(name);
        data = 0;
        return -10;
    }

    internal static int mpv_get_property_int64(IntPtr context, string name, int format, out long data)
    {
        Boundary.Reads.Add(name);
        data = name == "display-swapchain" ? 777 : 0;
        return name == "display-swapchain" ? 0 : -10;
    }

    internal static void mpv_free(IntPtr data) => Marshal.FreeCoTaskMem(data);
    internal static void mpv_free_node_contents(IntPtr node) { }
    internal static IntPtr mpv_error_string(int error) => IntPtr.Zero;
    internal static void mpv_terminate_destroy(IntPtr context) => Boundary.DestroyCalls++;
}
