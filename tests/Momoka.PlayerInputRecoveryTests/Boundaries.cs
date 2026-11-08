using Momoka.Emby;

namespace Momoka.Shell.ViewModels
{
    public sealed class FakePlayback
    {
        public long Generation { get; set; } = 10;
        public long? ActiveFileGeneration { get; set; } = 5;
        public bool IsPlaying { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public string? Scrollable { get; set; } = "false";
        public TaskCompletionSource<string?>? PendingRead { get; set; }
        public List<string[]> Commands { get; } = [];
        public TaskCompletionSource? PendingPause { get; set; }
        public Action<bool>? PauseApplied { get; set; }
        public bool RejectPause { get; set; }
        public Task<string?> GetTextAsync(string name) => PendingRead?.Task ?? Task.FromResult(Scrollable);
        public async Task<bool> CommandForActiveFileAsync(long generation, long? file, params string[] args)
        {
            if (args is ["set", "pause", "yes"] && RejectPause) return false;
            if (args is ["set", "pause", "yes"] && PendingPause is { } pending) await pending.Task;
            if (generation != Generation || file != ActiveFileGeneration || !IsPlaying) return false;
            Commands.Add(args);
            if (args is ["set", "pause", var value]) PauseApplied?.Invoke(value == "yes");
            return true;
        }
    }
    public sealed partial class PlayerViewModel
    {
        private readonly CancellationTokenSource _lifetime = new();
        private readonly FakePlayback _playback = new();
        private int _playbackAttempt = 1, _generation = 1, _interactionVersion = 1;
        private EmbyItem? _nowPlaying;
        public bool Paused { get; set; }
        public bool PictureInHostWindow { get; set; } = true;
        public IReadOnlyDictionary<string, string> ShortcutBindings { get; } = new Dictionary<string, string>();
        public bool RunAdditionalShortcut(string action) => true;
        public FakePlayback Playback => _playback;
        public void FirstPublish() { _generation++; _interactionVersion++; _nowPlaying = new EmbyItem { Id = "current" }; }
        public void Swap() { _playback.Generation++; _playback.ActiveFileGeneration++; }
        public void NativeSwapGap() => _playback.ActiveFileGeneration++;
        public void Stop() { _playback.IsPlaying = false; _playback.CanControl = false; }
    }
}

namespace Momoka.Shell.Views
{
    public sealed class Window { public IntPtr Handle => (IntPtr)1; }
    public sealed partial class PlayerPage
    {
        private bool _resizePaused, _resizeLoop = false, _onStage = true;
        private int _resizeGeneration;
        private Task _resizeFreeze = Task.CompletedTask;
        private Task<bool> _freezePauseWrite = Task.FromResult(false);
        private bool _freezePauseAccepted;
        private Window? _window = new();
        private Momoka.Shell.ViewModels.PlayerViewModel.InteractionContext _resizePauseContext;
        private Momoka.Shell.ViewModels.PlayerViewModel.FreezeContext _resizeFreezeContext;
        public Momoka.Shell.ViewModels.PlayerViewModel ViewModel { get; } = new();
        public bool Attached => true;
        private long _handoffMutedAt;
        private static long Now => Environment.TickCount64;
        private readonly Dictionary<string, Action> ShortcutHandlers = [];
        private readonly Chrome _chrome = new();
        public List<string> Actions { get; } = [];
        private static bool WakesChrome(string action) => false;
        private void Render() { }
        public PlayerPage()
        {
            ViewModel.Playback.PauseApplied = paused => { ViewModel.Paused = paused; Pauses.Add(paused); };
            ShortcutHandlers["volume-up"] = () => Actions.Add("volume-up");
            ShortcutHandlers["volume-down"] = () => Actions.Add("volume-down");
            ShortcutHandlers["seek-backward-long"] = () => Actions.Add("seek-backward-long");
            ShortcutHandlers["seek-forward-long"] = () => Actions.Add("seek-forward-long");
            ShortcutHandlers["chapter-previous"] = () => Actions.Add("chapter-previous");
            ShortcutHandlers["chapter-next"] = () => Actions.Add("chapter-next");
        }
        public List<bool> Pauses { get; } = [];
        internal Action<bool>? PauseRequested { get; set; }
        public void Begin() => BeginResizeFreeze();
        public Task Release() => ReleaseResizeFreezeAsync();
        public void NextDrag() => _resizeGeneration++;
        public Task Key(Windows.System.VirtualKey key) { ShortcutCommand(key, out _); return _statsKeyTail; }
    }
    public sealed class Chrome { public bool WakeFully(long now) => false; }
}

namespace Windows.System { public enum VirtualKey { Up, Down, PageUp, PageDown } }
namespace Momoka.Shell.Interop
{
    public static class Native { public static bool CtrlHeld => false; public static bool AltHeld => false; public static bool ShiftHeld => false; }
    public static class KeyStrokeInterop { public static string? Token(Windows.System.VirtualKey key) => key.ToString(); }
}
