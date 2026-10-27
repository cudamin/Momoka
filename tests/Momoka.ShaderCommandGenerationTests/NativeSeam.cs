using System.Runtime.InteropServices;
using Momoka.Tests;

namespace Momoka.Mpv;

// No DllImport, resolver, native initialization, event thread, media or real rendering.
internal static partial class LibMpvNative
{
    private static readonly Dictionary<IntPtr, (NativeBoundary Owner, List<IntPtr> Allocations)> Nodes = [];
    private static NativeBoundary For(IntPtr context) => NativeBoundary.Contexts[context];
    internal static IntPtr mpv_wait_event(IntPtr context, double timeout) =>
        throw new AssertionException("R051 禁止原生事件线程");
    internal static int mpv_observe_property(IntPtr context, ulong id, string name, int format) =>
        throw new AssertionException("R051 禁止 Start/原生订阅");
    internal static int mpv_command(IntPtr context, IntPtr[] args) => For(context).Command(args
        .TakeWhile(pointer => pointer != IntPtr.Zero).Select(pointer => Marshal.PtrToStringUTF8(pointer) ?? "").ToArray());
    internal static int mpv_command_ret(IntPtr context, IntPtr[] args, IntPtr result) =>
        throw new AssertionException("R051 禁止抓真实视频帧");
    internal static int mpv_set_property_string(IntPtr context, string name, string data)
    {
        For(context).Write(name, data);
        return 0;
    }
    internal static IntPtr mpv_get_property_string(IntPtr context, string name) =>
        For(context).Read(name) is { } text ? Marshal.StringToCoTaskMemUTF8(text) : IntPtr.Zero;
    internal static int mpv_get_property(IntPtr context, string name, int format, IntPtr data)
    {
        var owner = For(context);
        if (name is "track-list" or "chapter-list" or "demuxer-cache-state")
        {
            owner.NoteRead(name);
            return -10;
        }
        if (name != "glsl-shaders" || format != FormatNode)
            throw new AssertionException("R051 拒绝未安排节点读取：" + name);
        var strings = owner.ShaderList();
        if (strings is null) return -10;
        var allocations = new List<IntPtr>();
        var values = Marshal.AllocCoTaskMem(strings.Count * Marshal.SizeOf<MpvNode>());
        allocations.Add(values);
        for (var i = 0; i < strings.Count; i++)
        {
            var value = Marshal.StringToCoTaskMemUTF8(strings[i]);
            allocations.Add(value);
            Marshal.StructureToPtr(new MpvNode { Union = value, Format = FormatString },
                values + i * Marshal.SizeOf<MpvNode>(), false);
        }
        var list = Marshal.AllocCoTaskMem(Marshal.SizeOf<MpvNodeList>());
        allocations.Add(list);
        Marshal.StructureToPtr(new MpvNodeList { Num = strings.Count, Values = values }, list, false);
        Marshal.StructureToPtr(new MpvNode { Union = list, Format = FormatNodeArray }, data, false);
        lock (Nodes) Nodes.Add(data, (owner, allocations));
        owner.NodeAllocations++;
        return 0;
    }
    internal static int mpv_get_property_double(IntPtr context, string name, int format, out double data)
    {
        For(context).NoteRead(name);
        data = 0;
        return -10;
    }
    internal static int mpv_get_property_int64(IntPtr context, string name, int format, out long data)
    {
        For(context).NoteRead(name);
        data = 0;
        return -10;
    }
    internal static void mpv_free(IntPtr data) => Marshal.FreeCoTaskMem(data);
    internal static void mpv_free_node_contents(IntPtr node)
    {
        lock (Nodes)
        {
            if (!Nodes.Remove(node, out var entry)) return;
            foreach (var allocation in entry.Allocations) Marshal.FreeCoTaskMem(allocation);
            entry.Owner.NodeFrees++;
        }
    }
    internal static IntPtr mpv_error_string(int error) => IntPtr.Zero;
    internal static void mpv_terminate_destroy(IntPtr context) => For(context).DestroyCalls++;
}
