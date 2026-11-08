namespace Momoka.PlayerExitHandoffTests;

/// <summary>窗口矩形。生产里是 <c>Momoka.Shell.Interop.NativeRect</c>，这里要值相等，所以用 record struct。</summary>
internal readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;
    internal int Height => Bottom - Top;
}

/// <summary>逐字抽取的退场流程所需的成员：时钟、就绪/打断开关、以及它调用到的那几个小方法。</summary>
internal sealed partial class PlayerPage
{
    private const string Category = "anonymous-player-exit";
    private const int PoseTickMilliseconds = 16;
    private const int ExitReadyWaitMilliseconds = 800;
    private const uint StageColor = 0xFF0C0E11;

    private static long _clock;
    private static int _waitTicks;
    private static int _readyAfter;
    private static int _interruptAfter;
    private static bool _animations = true;

    private bool _onStage = true;
    private readonly HostWindow _window;
    private readonly VideoTarget _videoTarget = new();
    private int _presentationGeneration;
    private bool _exitSnap;
    private TaskCompletionSource? _exitLanded;
    private VideoFrameOverlay? _exitFade;
    private Task _exitFadeTask = Task.CompletedTask;
    private static bool Attached => true;
    private PlayerState ViewModel { get; } = new();

    internal bool Left { get; private set; }

    internal int FramesWaited { get; private set; }

    internal Task ExitFade => _exitFadeTask;

    internal string FadeLog => LastFadeLog;

    internal PlayerPage(bool fullscreen) => _window = new HostWindow { Fullscreen = fullscreen };

    internal static int Ticks => _waitTicks;

    internal static int ReadyCeiling => ExitReadyWaitMilliseconds;

    internal static bool AnimationsOn => _animations;

    internal static string LastFadeLog { get; private set; } = "";

    internal static void Reset(int readyAfterTicks, int interruptAfter, bool animations)
    {
        _clock = 1000;
        _waitTicks = 0;
        _readyAfter = readyAfterTicks;
        _interruptAfter = interruptAfter;
        _animations = animations;
        LastFadeLog = "";
    }

    internal static int TicksFor(int milliseconds) => milliseconds / PoseTickMilliseconds;

    private static long Now => _clock;

    private uint ExitSurroundColor => StageColor;

    private bool BrowseReady => _waitTicks >= _readyAfter;

    /// <summary>假时钟：每等一拍就把时间推 16ms；到约定的那一拍把 generation 推走，模拟中途换片。</summary>
    private Task WaitExitTickAsync()
    {
        _waitTicks++;
        _clock += PoseTickMilliseconds;
        if (_interruptAfter > 0 && _waitTicks == _interruptAfter) _presentationGeneration++;
        return Task.CompletedTask;
    }

    private void LeavePlayer()
    {
        if (!_exitSnap) throw new InvalidOperationException("Expected production snap handoff.");
        _onStage = false;
        _presentationGeneration++;
        Left = true;
        _exitLanded?.TrySetResult();
    }

    private Task<int> WaitFramesAsync(int frames)
    {
        FramesWaited += frames;
        return Task.FromResult(frames);
    }

    internal static void NoteFadeLog(string message) => LastFadeLog = message;
}

internal sealed class PlayerState
{
    internal int CaptureInteraction() => 1;
    internal bool IsCurrentInteraction(int context) => context == 1;
}

internal sealed class HostWindow
{
    internal IntPtr Handle => 1;
    internal bool Fullscreen { get; init; }
    internal bool FreeSizing { get; set; } = true;
    internal void RestorePlayerToBrowse() => VideoFrameOverlay.Bounds = new NativeRect(10, 10, 1010, 610);
}

internal sealed class VideoTarget
{
    internal bool HasAttachedVisual => true;
    internal bool RetainLastFrame { get; set; } = true;
    internal Task<object?> CaptureFrameAsync() => Task.FromResult<object?>(new object());
}

/// <summary>覆盖层替身：记录每次 Show 的矩形、topmost、常量 alpha 与围边色，并数撤层次数。</summary>
internal sealed class VideoFrameOverlay(IntPtr owner, object frame) : IDisposable
{
    internal static List<Receipt> Receipts { get; } = [];
    internal static NativeRect Bounds { get; set; }
    internal static int Disposals { get; set; }
    internal static int FirstFadeWaits { get; set; } = -1;

    private Receipt? _receipt;
    private bool _disposed;

    internal void Show(NativeRect bounds, bool topmost, byte alpha = 255, uint surround = 0)
    {
        if (owner == IntPtr.Zero || frame is null) throw new InvalidOperationException("Invalid boundary input.");
        // 生产里撤层之后 Show 只是个空转（窗口句柄已经归零），替身照这条走。
        if (_disposed) return;
        if (alpha < 255 && FirstFadeWaits < 0) FirstFadeWaits = PlayerPage.Ticks;
        _receipt = new Receipt(bounds, topmost, alpha, surround);
        Receipts.Add(_receipt);
    }

    internal static NativeRect ClientRect(IntPtr window) => Bounds;

    internal static NativeRect FullscreenRect(IntPtr window) => new(0, 0, 1920, 1080);

    internal static void Flush() { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disposals++;
        if (_receipt is not null) _receipt.Disposed = true;
    }
}

internal sealed record Receipt(NativeRect Bounds, bool Topmost, byte Alpha, uint Surround)
{
    public bool Disposed { get; set; }
}

internal static class HomeMotion
{
    internal static bool AnimationsEnabled => PlayerPage.AnimationsOn;
}

internal static class Log
{
    internal static void Warn(string category, string message, Exception? error = null) =>
        throw new InvalidOperationException($"Unexpected production catch: {category}: {message}", error);

    internal static void Debug(string category, string message) => PlayerPage.NoteFadeLog(message);
}
