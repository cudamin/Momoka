namespace Momoka.PlayerExitHandoffTests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var failures = 0;
        foreach (var bounds in new[] { new Rect(200, 100, 1000, 550), new Rect(0, 0, 1920, 1080), new Rect(-1600, 80, -800, 530) })
        {
            try
            {
                VideoFrameOverlay.Receipts.Clear();
                VideoFrameOverlay.Bounds = bounds;
                var page = new PlayerPage();
                await page.ReturnToBrowseBeforeStopAsync();
                var receipt = VideoFrameOverlay.Receipts.Single();
                if (receipt.Bounds != bounds || !receipt.Topmost || !receipt.Disposed || !page.Left || page.FramesWaited != 2)
                    throw new InvalidOperationException($"Expected original client {bounds}, actual {receipt.Bounds}; exit/cleanup must complete.");
                Console.WriteLine($"通过：客户区交接 {bounds}");
            }
            catch (Exception error) { failures++; Console.WriteLine(error); }
        }
        Console.WriteLine($"共 3 项，通过 {3 - failures}，失败 {failures}，跳过 0；原样生产方法，未运行 DWM/WinUI/播放。");
        return failures == 0 ? 0 : 1;
    }
}

internal sealed partial class PlayerPage
{
    private const string Category = "anonymous-player-exit";
    private bool _onStage = true;
    private readonly HostWindow _window = new();
    private readonly VideoTarget _videoTarget = new();
    private int _presentationGeneration;
    private bool _exitSnap;
    private TaskCompletionSource? _exitLanded;
    private static bool Attached => true;
    private PlayerState ViewModel { get; } = new();
    internal bool Left { get; private set; }
    internal int FramesWaited { get; private set; }
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
}

internal sealed class PlayerState
{
    internal int CaptureInteraction() => 1;
    internal bool IsCurrentInteraction(int context) => context == 1;
}

internal sealed class HostWindow
{
    internal IntPtr Handle => 1;
    internal bool FreeSizing { get; set; } = true;
    internal void RestorePlayerToBrowse() => VideoFrameOverlay.Bounds = new(10, 10, 1010, 610);
}

internal sealed class VideoTarget
{
    internal bool HasAttachedVisual => true;
    internal bool RetainLastFrame { get; set; } = true;
    internal Task<object?> CaptureFrameAsync() => Task.FromResult<object?>(new object());
}

internal readonly record struct Rect(int Left, int Top, int Right, int Bottom);
internal sealed class Receipt(Rect bounds, bool topmost)
{
    public Rect Bounds { get; } = bounds;
    public bool Topmost { get; } = topmost;
    public bool Disposed { get; set; }
}

internal sealed class VideoFrameOverlay(IntPtr owner, object frame) : IDisposable
{
    internal static List<Receipt> Receipts { get; } = [];
    internal static Rect Bounds { get; set; }
    private Receipt? _receipt;
    internal void Show(Rect bounds, bool topmost)
    {
        if (owner == IntPtr.Zero || frame is null) throw new InvalidOperationException("Invalid boundary input.");
        _receipt = new(bounds, topmost);
        Receipts.Add(_receipt);
    }
    internal static Rect ClientRect(IntPtr window) => Bounds;
    internal static Rect FullscreenRect(IntPtr window) => new(0, 0, 1920, 1080);
    internal static void Flush() { }
    public void Dispose() { if (_receipt is not null) _receipt.Disposed = true; }
}

internal static class Log
{
    internal static void Warn(string category, string message, Exception? error = null) =>
        throw new InvalidOperationException($"Unexpected production catch: {category}: {message}", error);
}
