using System.Runtime.InteropServices;
using Momoka.Emby;
using Momoka.Shell.Interop;
using Momoka.Shell.Windowing;

namespace Momoka.Shell.ViewModels
{
    public sealed class PlaybackBoundary
    {
        public long Generation { get; set; } = 10;
        public long? ActiveFileGeneration { get; set; } = 5;
        public bool IsPlaying { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public bool RejectPause { get; set; }
        public TaskCompletionSource? PendingPause { get; set; }
        public Action<bool>? PauseApplied { get; set; }
        public List<bool> Pauses { get; } = [];
        public async Task<bool> CommandForActiveFileAsync(long generation, long? file, params string[] args)
        {
            if (args is not ["set", "pause", var value]) throw new InvalidOperationException("Unexpected playback command");
            if (value == "yes" && RejectPause) return false;
            if (value == "yes" && PendingPause is { } pending) await pending.Task;
            if (generation != Generation || file != ActiveFileGeneration || !IsPlaying) return false;
            Pauses.Add(value == "yes");
            PauseApplied?.Invoke(value == "yes");
            return true;
        }
    }

    public sealed partial class PlayerViewModel
    {
        private readonly CancellationTokenSource _lifetime = new();
        private readonly PlaybackBoundary _playback = new();
        private int _playbackAttempt = 1, _generation = 1, _interactionVersion = 1;
        private readonly EmbyItem? _nowPlaying = new() { Id = "anonymous" };
        public bool Paused { get; set; }
        public bool PictureInHostWindow { get; set; } = true;
        public PlaybackBoundary Playback => _playback;
        public void SwapFile() => _playback.ActiveFileGeneration++;
        public void RefreshMenus() => _interactionVersion++;
    }
}

namespace Momoka.Shell.Windowing
{
    internal sealed record FrameBoundary(int Content);

    internal sealed class VideoTargetBoundary
    {
        public bool HasPicture { get; set; } = true;
        public bool HasAttachedVisual { get; set; } = true;
        public bool IsContentReady { get; set; } = true;
        public bool IsInteractiveResize { get; private set; }
        public TaskCompletionSource? PendingCommit { get; set; }
        public FrameBoundary? CompletedFrame { get; set; }
        public List<TaskCompletionSource<FrameBoundary?>> Captures { get; } = [];
        public List<string> Trace { get; } = [];
        public Task<FrameBoundary?> CaptureFrameAsync()
        {
            Trace.Add("capture");
            var capture = new TaskCompletionSource<FrameBoundary?>();
            Captures.Add(capture);
            if (CompletedFrame is { } frame) capture.SetResult(frame);
            return capture.Task;
        }
        public void SetInteractiveResize(bool active) { IsInteractiveResize = active; Trace.Add(active ? "interactive" : "settling"); }
        public void ResizePresentation(NativeRect client) => Trace.Add($"size:{client.Width}x{client.Height}");
        public void RefreshPresentation() { }
        public Task CommitPresentationAsync() { Trace.Add("commit"); return PendingCommit?.Task ?? Task.CompletedTask; }
    }

    internal sealed class VideoFrameOverlay : IDisposable
    {
        public static List<VideoFrameOverlay> Created { get; } = [];
        public static int Flushes { get; private set; }
        public static bool ThrowOnShow { get; set; }
        public FrameBoundary Frame { get; }
        public bool Disposed { get; private set; }
        public List<NativeRect> Shows { get; } = [];
        public VideoFrameOverlay(IntPtr handle, FrameBoundary frame) { Frame = frame; Created.Add(this); }
        public void Show(NativeRect client, bool topmost)
        {
            if (ThrowOnShow) throw new InvalidOperationException("Anonymous overlay failure");
            Shows.Add(client);
        }
        public void Dispose() => Disposed = true;
        public static NativeRect ClientRect(IntPtr handle) => Native.Client;
        public static void Flush() => Flushes++;
        public static void Reset() { Created.Clear(); Flushes = 0; ThrowOnShow = false; }
    }

    public sealed partial class HostWindow
    {
        private bool _interactiveResize;
        private NativeRect? _pendingInteractiveRect;
        internal event Func<NativeRect, bool>? ClientSizeChanging;
        internal event Action<NativeRect>? ClientSizeChanged;
        internal event Action<bool>? InteractiveResizeChanged;
        public IntPtr Handle { get; set; } = (IntPtr)1;
        public bool Fullscreen { get; set; }
        public bool TopMost { get; set; }
        public bool FreeSizing => true;
        public double PictureAspect => 16d / 9;
        private int MinimumWidth => 640;
        private int MinimumHeight => 360;
        private static (int Width, int Height) FrameThickness(uint dpi) => (16, 16);
        private static NativeRect ClientRectOnScreen() => Native.Client;
        internal NativeRect? Pending => _pendingInteractiveRect;
        internal void Begin() { _interactiveResize = true; InteractiveResizeChanged?.Invoke(true); }
        internal void End()
        {
            // HostWindow's WM_EXITSIZEMOVE clears the abandoned pending rectangle before publishing.
            _pendingInteractiveRect = null;
            _interactiveResize = false;
            InteractiveResizeChanged?.Invoke(false);
        }
        internal NativeRect Size(NativeRect requested)
        {
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
            try
            {
                Marshal.StructureToPtr(requested, memory, false);
                LockAspectDuringResize(Handle, (IntPtr)Momoka.Playback.ResizeEdge.Right, memory);
                var actual = Marshal.PtrToStructure<NativeRect>(memory);
                if (actual.Width != Native.Outer.Width || actual.Height != Native.Outer.Height)
                {
                    Native.Apply(actual);
                    ClientSizeChanged?.Invoke(Native.Client);
                }
                return actual;
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        internal void ConnectNative() => Native.SizeApplied = () => ClientSizeChanged?.Invoke(Native.Client);
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class PlayerPage
    {
        private readonly HostWindow _window = new();
        private readonly VideoTargetBoundary _videoTarget = new();
        private readonly CoverBoundary Cover = new();
        private bool _onStage = true, _resizeLoop, _resizePaused, _resizeCaptureFailed, _freezePauseAccepted;
        private int _resizeGeneration;
        private VideoFrameOverlay? _resizeFrame;
        private Task? _resizeCapture;
        private Task _resizeChange = Task.CompletedTask, _resizeFreeze = Task.CompletedTask;
        private Task<bool> _freezePauseWrite = Task.FromResult(false);
        private NativeRect _resizeTarget;
        private Momoka.Shell.ViewModels.PlayerViewModel.InteractionContext _resizePauseContext;
        private Momoka.Shell.ViewModels.PlayerViewModel.FreezeContext _resizeFreezeContext;
        private long _handoffMutedAt;
        private static long Now => Environment.TickCount64;
        private const int HandoffSettleMilliseconds = 120;
        public bool Attached { get; set; } = true;
        public Momoka.Shell.ViewModels.PlayerViewModel ViewModel { get; } = new();
        internal Action<bool>? PauseRequested { get; set; }
        internal HostWindow Window => _window;
        internal VideoTargetBoundary Target => _videoTarget;
        internal VideoFrameOverlay? Overlay => _resizeFrame;
        internal Task Capture => _resizeCapture ?? Task.CompletedTask;
        internal Task Change => _resizeChange;
        internal bool CaptureFailed => _resizeCaptureFailed;
        internal bool Frozen => _resizePaused;
        internal PlayerPage()
        {
            _window.ClientSizeChanging += OnClientSizeChanging;
            _window.ClientSizeChanged += OnClientSizeChanged;
            _window.InteractiveResizeChanged += OnInteractiveResizeChanged;
            _window.ConnectNative();
            ViewModel.Playback.PauseApplied = paused => { ViewModel.Paused = paused; _videoTarget.Trace.Add(paused ? "pause" : "resume"); };
        }
        internal void Leave() { _onStage = false; CancelResizeChange(); }
        internal Task Finish() => FinishResizeAsync(_window, _resizeGeneration);
    }
    internal sealed class CoverBoundary { public Microsoft.UI.Xaml.Visibility Visibility => Microsoft.UI.Xaml.Visibility.Collapsed; }
}

namespace Momoka.Shell.Interop
{
    internal static class Native
    {
        internal const uint SwpNoZOrder = 4, SwpNoActivate = 16;
        internal const int WmSizing = 0x0214;
        internal static NativeRect Outer { get; private set; }
        internal static NativeRect Client { get; private set; }
        internal static Action? SizeApplied { get; set; }
        internal static bool FailSetWindowPos { get; set; }
        internal static List<NativeRect> Applied { get; } = [];
        internal static bool IsZoomed(IntPtr handle) => false;
        internal static uint GetDpiForWindow(IntPtr handle) => 96;
        internal static bool GetWindowRect(IntPtr handle, out NativeRect rect) { rect = Outer; return true; }
        internal static IntPtr DefWindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam) => IntPtr.Zero;
        internal static bool SetWindowPos(IntPtr handle, IntPtr after, int left, int top, int width, int height, uint flags)
        {
            if (FailSetWindowPos) return false;
            Apply(new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height });
            Applied.Add(Outer);
            SizeApplied?.Invoke();
            return true;
        }
        internal static void Apply(NativeRect outer)
        {
            Outer = outer;
            Client = new NativeRect { Left = outer.Left + 8, Top = outer.Top + 8, Right = outer.Right - 8, Bottom = outer.Bottom - 8 };
        }
        internal static void Reset()
        {
            Apply(new NativeRect { Left = 92, Top = 92, Right = 748, Bottom = 468 });
            Applied.Clear(); SizeApplied = null; FailSetWindowPos = false;
        }
    }
}

namespace Microsoft.UI.Xaml { internal enum Visibility { Visible, Collapsed } }
