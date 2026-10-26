using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

internal sealed class HandleFixture : IAsyncDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Process = typeof(LibMpvHandle).GetMethod("ProcessEvent", Private)
        ?? throw new InvalidOperationException("真实 ProcessEvent 入口不存在");

    internal NativeBoundary Native { get; } = new();
    internal SurfaceBoundary Surface { get; } = new();
    internal LibMpvHandle Handle { get; }
    internal Task<PlaybackExit> Exit { get; }
    internal Task<PlaybackExit> Waiter { get; }
    internal List<PlayerStatus> Published { get; } = [];
    internal bool Swapping => Field<bool>("_swapping");
    internal bool LeaveLoop => Field<bool>("_leaveLoop");
    internal int StatusListeners => Field<Action<PlayerStatus>?>("StatusChanged")?.GetInvocationList().Length ?? 0;

    internal HandleFixture(bool standalone = false)
    {
        LibMpvNative.Boundary = Native;
        var signature = standalone ? new PlaybackLaunchSignature(VideoPipelineKind.Standalone, [], [], [], []) : null;
        Handle = new LibMpvHandle(new IntPtr(48), standalone ? null : Surface, signature,
            signature is null ? null : _ => signature);
        Handle.StatusChanged += Published.Add;
        Exit = Field<TaskCompletionSource<PlaybackExit>>("_exit").Task;
        Waiter = Handle.WaitForExitAsync(CancellationToken.None);
        Native.OnCommand = command =>
        {
            if (command[0] == "quit") Dispatch(LibMpvNative.EventShutdown);
        };
    }

    internal bool Dispatch(int eventId, IntPtr data = default, ulong userData = 0) => Invoke(new LibMpvNative.MpvEvent
    {
        EventId = eventId,
        Data = data,
        ReplyUserData = userData
    });

    private bool Invoke(LibMpvNative.MpvEvent value)
    {
        // The production loop serializes this exact dispatcher through the real API lifetime gate.
        var gate = Field<LibMpvLifetimeGate>("_apiGate");
        return gate.Run(() =>
        {
            try { return (bool)Process.Invoke(Handle, [value])!; }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        });
    }

    internal bool End(int reason, int error = 0, long entry = 101, long insert = 0, int count = 0)
    {
        // Actual pinned x64 C ABI payload, not the managed declaration's unused tail.
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<PinnedEndFile>());
        try
        {
            Marshal.StructureToPtr(new PinnedEndFile
            {
                Reason = reason,
                Error = error,
                PlaylistEntryId = entry,
                PlaylistInsertId = insert,
                PlaylistInsertNumEntries = count
            }, memory, false);
            return Dispatch(LibMpvNative.EventEndFile, memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    internal void ManagedEnd(int reason)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<LibMpvNative.MpvEventEndFile>());
        try
        {
            Marshal.StructureToPtr(new LibMpvNative.MpvEventEndFile { Reason = reason }, memory, false);
            Dispatch(LibMpvNative.EventEndFile, memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    internal void Number(ulong observer, double value)
    {
        var number = Marshal.AllocHGlobal(sizeof(double));
        var payload = Marshal.AllocHGlobal(Marshal.SizeOf<LibMpvNative.MpvEventProperty>());
        try
        {
            Marshal.StructureToPtr(value, number, false);
            Marshal.StructureToPtr(new LibMpvNative.MpvEventProperty
            {
                Format = LibMpvNative.FormatDouble,
                Data = number
            }, payload, false);
            Dispatch(LibMpvNative.EventPropertyChange, payload, observer);
        }
        finally { Marshal.FreeHGlobal(payload); Marshal.FreeHGlobal(number); }
    }

    internal void Ready(double position = 12.5)
    {
        Dispatch(LibMpvNative.EventStartFile);
        Dispatch(LibMpvNative.EventFileLoaded);
        Number(2, 100);
        Number(1, position);
        Dispatch(LibMpvNative.EventPlaybackRestart);
    }

    internal T Field<T>(string name) => (T)(typeof(LibMpvHandle).GetField(name, Private)
        ?? throw new InvalidOperationException("真实字段不存在：" + name)).GetValue(Handle)!;

    internal static PlaybackRequest Request() => new()
    {
        MediaUrl = new Uri("https://r048-media.invalid/placeholder"),
        Title = "R048 anonymous",
        ItemId = "episode",
        RunTimeTicks = TimeSpan.TicksPerSecond * 100
    };

    public async ValueTask DisposeAsync() => await Handle.DisposeAsync();

    // mpv-player/mpv commit 270367e8af/include/mpv/client.h, lines 1508-1550;
    // same layout in v0.41.0. reason/error are the only fields consumed by this fix.
    [StructLayout(LayoutKind.Sequential)]
    internal struct PinnedEndFile
    {
        internal int Reason;
        internal int Error;
        internal long PlaylistEntryId;
        internal long PlaylistInsertId;
        internal int PlaylistInsertNumEntries;
    }
}

internal sealed class SurfaceBoundary : IVideoSurface
{
    private Action? _geometry;
    internal int DetachCalls { get; private set; }
    internal int CaptureClearCalls { get; private set; }
    internal List<IntPtr> Chains { get; } = [];
    public event Action? GeometryChanged { add => _geometry += value; remove => _geometry -= value; }
    public (int Width, int Height) Size => (640, 360);
    public void AttachSwapChain(IntPtr value)
    {
        Chains.Add(value);
        if (value == IntPtr.Zero) DetachCalls++;
    }
    public void SetFrameCapture(Func<Task<VideoFrame?>>? capture)
    {
        if (capture is null) CaptureClearCalls++;
    }
}
