using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;

namespace Momoka.Playback;

/// <summary>Live playback state for the now-playing bar.</summary>
public readonly record struct PlaybackProgress(long PositionTicks, long RunTimeTicks, bool IsPaused, string Title, long Generation)
{
    public PlaybackProgress(long PositionTicks, long RunTimeTicks, bool IsPaused, string Title)
        : this(PositionTicks, RunTimeTicks, IsPaused, Title, 0) { }

    public double Fraction => RunTimeTicks > 0 ? Math.Clamp(PositionTicks / (double)RunTimeTicks, 0, 1) : 0;
    public string Clock => RunTimeTicks > 0
        ? $"{TimeFormat.Clock(PositionTicks)} / {TimeFormat.Clock(RunTimeTicks)}"
        : TimeFormat.Clock(PositionTicks);
}

/// <summary>The generation is captured at the source, before a UI dispatcher can delay delivery.</summary>
public readonly record struct PlaybackUpdate<T>(long Generation, T Value);

/// <summary>
/// Owns playback and server reporting independently of any page. Request cancellation, handle ownership,
/// and final reporting are kept together so closing a page cannot abandon a server session.
/// </summary>
public sealed partial class PlaybackService(
    EmbySession session,
    AppSettings settings,
    Func<IPlaybackBackend> backendFactory,
    PlaybackPlanner planner,
    AudioDeviceCatalogue? audioDevices = null,
    bool allowPlayback = true)
{
    private const string Category = "playback";
    private const int MarkWatchedFloor = 50;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _requestGate = new();
    private readonly HashSet<PlayOperation> _operations = [];
    private readonly HashSet<TaskCompletionSource> _manualReports = [];
    private TaskCompletionSource? _shutdownCompletion;
    private PlayOperation? _latestRequest;
    private volatile IPlaybackHandle? _current;
    private volatile PlayingAttempt? _active;
    private sealed record PlayingItem(long Generation, EmbyItem? Item);
    private volatile PlayingItem? _playingItem;
    private readonly SemaphoreSlim _audioDelayGate = new(1, 1);
    private readonly SemaphoreSlim _subtitleDelayGate = new(1, 1);
    private readonly SemaphoreSlim _volumeCommandGate = new(1, 1);
    private readonly SemaphoreSlim _shaderGate = new(1, 1);
    private readonly SemaphoreSlim _subtitleStyleGate = new(1, 1);
    private long _subtitleStyleRevision;
    private readonly SemaphoreSlim _screenshotOptionsGate = new(1, 1);
    private long _screenshotOptionsRevision;

    public bool ShaderStateKnown { get; private set; } = true;
    private long _playbackGeneration;
    private IReadOnlyList<KeyValuePair<string, string>> _launchOptions = [];
    private int _launchGroupOptionCount;

    public event Action<PlaybackProgress>? ProgressChanged;
    public event Action<EmbyItem?>? NowPlayingChanged;
    public event Action<PlayerStatus>? StatusChanged;
    public event Action<IReadOnlyList<MpvTrack>>? TracksChanged;
    public event Action<string, string>? VideoWindowMessage;
    public event Action<PlaybackUpdate<EmbyItem?>>? NowPlayingUpdated;
    public event Action<PlaybackUpdate<PlayerStatus>>? StatusUpdated;
    public event Action<PlaybackUpdate<IReadOnlyList<MpvTrack>>>? TracksUpdated;
    public event Action<PlaybackUpdate<(string Key, string Value)>>? VideoWindowUpdated;

    public long Generation => Interlocked.Read(ref _playbackGeneration);
    public bool IsPlaying => _current is not null;
    public long? ActiveFileGeneration => (_current as IFileScopedPlayerControl)?.FileGeneration;
    public EmbyItem? NowPlaying => _current is not null && _playingItem is { } item && item.Generation == Generation
        ? item.Item : null;
    public bool? PictureInHostWindow => _current?.PictureInHostWindow;
    public MpvBackendKind? PlayingBackend => _active?.BackendKind;
    public bool PlayingWithSvp => _active?.Request.SvpEnabled == true;
    public PlayerStatus Status => (_current as IPlayerControl)?.Status ?? new PlayerStatus();
    public bool CanControl => _current is IPlayerControl and IPlaybackHandle { HasControlChannel: true };
    public bool ReadsOverlap => _current?.ReadsOverlap ?? false;
    public PlaybackPlanner Planner => planner;
    public IReadOnlyList<KeyValuePair<string, string>> LaunchOptions => _launchOptions;
    public string? LaunchShaderProfile { get; private set; }
    public string? LaunchShaderReason { get; private set; }
    public ShaderDecision? LaunchShaderDecision { get; private set; }

    /// <summary>
    /// The same facts as the <c>Launch*</c> properties above, but kept after playback ends rather than
    /// cleared with it — the diagnostics page is read <em>after</em> a playback went wrong, which is
    /// exactly when the live properties are back to null and it had nothing to show.
    /// <para>
    /// Additive rather than a change to those properties: 播放统计 reads them while playing and shows a
    /// placeholder when the list is empty, so keeping their values past the end would leave stale options
    /// on that panel.
    /// </para>
    /// </summary>
    public LaunchRecord? LastLaunch { get; private set; }

    /// <summary>
    /// The 画质预设 in force when this playback started; null when nothing is playing. Captured rather
    /// than read from the settings on demand, because the settings can be edited while a file plays and
    /// the panel is meant to say what mpv was actually given.
    /// </summary>
    public string? LaunchQualityPreset { get; private set; }
    public MediaSource? PlayingSource { get; private set; }

    /// <summary>The requested device and active driver, not proof of the physical Windows endpoint.</summary>
    public string? AudioDeviceInUse { get; private set; }
    public void NoteAudioDevice(string? description) => AudioDeviceInUse = description;
    public string? Validate() => backendFactory().Validate();

    private async Task<string?> ResolveAudioDeviceAsync()
    {
        var stored = settings.Audio.Device.Trim();
        if (stored.Length == 0 || audioDevices is null || settings.Mpv.Backend != MpvBackendKind.BuiltInLibMpv) return null;
        var devices = await audioDevices.LoadAsync().ConfigureAwait(false);
        var usable = AudioDeviceCatalogue.UsableDevice(stored, devices);
        if (usable.Length > 0) return usable;
        Log.Warn(Category, $"本次音频设备名单未找到 {stored}（设备离线或枚举不可用），本次播放尝试系统默认");
        return "";
    }

    private async Task ReleaseAsync(IPlaybackHandle handle)
    {
        if (ReferenceEquals(_current, handle)) _current = null;
        try { await handle.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { Log.Warn(Category, "释放播放句柄失败", error); }
    }

    private static async Task StopHandleAsync(IPlaybackHandle handle)
    {
        try { await Task.Run(handle.StopAsync).ConfigureAwait(false); }
        catch (Exception error) { Log.Warn(Category, "停止播放失败", error); }
    }

    private static void Raise<T>(Action<T>? listeners, T value)
    {
        if (listeners is null) return;
        foreach (var listener in listeners.GetInvocationList())
        {
            try { ((Action<T>)listener)(value); }
            catch (Exception error) { Log.Warn(Category, "播放通知失败", error); }
        }
    }

    private void RaiseNowPlaying(EmbyItem? item, long generation)
    {
        if (generation == Generation) _playingItem = new(generation, item);
        Raise(NowPlayingUpdated, new PlaybackUpdate<EmbyItem?>(generation, item));
        Raise(NowPlayingChanged, item);
    }

    private bool IsCurrent(PlayingAttempt attempt) => ReferenceEquals(_active, attempt) && !attempt.Ending;

    // A batch must retain both handle and generation: inline switching deliberately reuses the handle.
    private bool IsCurrent(IPlaybackHandle handle, long generation) =>
        ReferenceEquals(_current, handle) && Generation == generation && _active is { Ending: false };

    // ---- runtime control --------------------------------------------------------

    /// <summary>
    /// Applies a wheel/arrow volume step <b>relatively in the kernel</b>, then reads the applied level
    /// back. A step computed from the displayed value would go out as an absolute command, and the
    /// display can legitimately be this playback's launch volume while no real kernel reading has
    /// arrived yet — an unintended 「down」 step would then raise the level (independent review,
    /// 2026-10-04). The returned double is the kernel's own level after the step; null means the step
    /// was not applied and the caller must not treat its intent as fact.
    /// </summary>
    public async Task<(double Level, long Sample)?> AdjustVolumeAsync(double step)
    {
        var handle = _current;
        var generation = Generation;
        if (!double.IsFinite(step) || handle is not IPlayerControl control || !handle.HasControlChannel) return null;

        bool Current() => ReferenceEquals(_current, handle) && generation == Generation && handle.HasControlChannel;
        await _volumeCommandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Current()) return null;
            if (!await control.CommandAsync(
                ["add", "volume", step.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)],
                CancellationToken.None).ConfigureAwait(false) || !Current()) return null;
            var actual = await handle.GetNumberAsync("volume", CancellationToken.None).ConfigureAwait(false);
            if (!Current() || actual is not { } level || !double.IsFinite(level)) return null;
            return Current() ? (level, handle.VolumeReadSample) : null;
        }
        catch (Exception error)
        {
            Log.Warn(Category, "调整音量失败", error);
            return null;
        }
        finally { _volumeCommandGate.Release(); }
    }

    /// <summary>Confirms an absolute volume level; false when the command could not be verified.</summary>
    public async Task<bool> SetVolumeAsync(double value)
    {
        var handle = _current;
        var generation = Generation;
        if (handle is not IPlayerControl control || !handle.HasControlChannel
            || VolumeMemory.Level(value) is not { } level) return false;

        await _volumeCommandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_current, handle) || generation != Generation || !handle.HasControlChannel) return false;
            var accepted = await control.CommandAsync(
                ["set", "volume", level.ToString(System.Globalization.CultureInfo.InvariantCulture)], CancellationToken.None)
                .ConfigureAwait(false);
            return accepted && ReferenceEquals(_current, handle) && generation == Generation && handle.HasControlChannel;
        }
        catch (Exception error)
        {
            Log.Warn(Category, "设置音量失败", error);
            return false;
        }
        finally { _volumeCommandGate.Release(); }
    }

    /// <summary>Adjusts in the kernel, then reads the applied seconds without changing global settings.</summary>
    public async Task<double?> ChangeAudioDelayAsync(double value, bool relative, bool notice = false)
    {
        var handle = _current;
        var generation = Generation;
        if (!double.IsFinite(value) || handle is not IPlayerControl control || !handle.HasControlChannel) return null;

        var fileControl = control as IFileScopedPlayerControl;
        var fileGeneration = fileControl?.FileGeneration;
        bool Current() => ReferenceEquals(_current, handle) && generation == Generation && handle.HasControlChannel
            && (fileControl is null || _active is { Ending: false } && fileGeneration == fileControl.FileGeneration);
        Task<bool> CommandAsync(IReadOnlyList<string> arguments) => fileControl is null
            ? control.CommandAsync(arguments, CancellationToken.None)
            : fileControl.CommandForFileAsync(fileGeneration!.Value, arguments, CancellationToken.None);
        await _audioDelayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Current()) return null;
            var accepted = await CommandAsync(
                [relative ? "add" : "set", "audio-delay", value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)]).ConfigureAwait(false);
            if (!accepted || !Current()) return null;
            var actual = await handle.GetNumberAsync("audio-delay", CancellationToken.None).ConfigureAwait(false);
            if (!Current() || actual is not { } seconds || !double.IsFinite(seconds)) return null;
            if (notice)
                await CommandAsync(
                    ["show-text", "音频延迟：${audio-delay} 秒", "1200"]).ConfigureAwait(false);
            return Current() ? seconds : null;
        }
        catch (Exception error)
        {
            Log.Warn(Category, "调整音频延迟失败", error);
            return null;
        }
        finally { _audioDelayGate.Release(); }
    }
    public async Task<double?> ChangeSubtitleDelayAsync(double value, bool relative, bool notice = false)
    {
        var handle = _current;
        var generation = Generation;
        if (!double.IsFinite(value) || handle is not IPlayerControl control || !handle.HasControlChannel) return null;
        var scoped = handle as IFileScopedPlayerControl;
        var file = scoped?.FileGeneration;
        bool Current() => IsCurrent(handle, generation) && (scoped is null || file == scoped.FileGeneration);
        Task<bool> Command(IReadOnlyList<string> args) => scoped is null
            ? control.CommandAsync(args, CancellationToken.None)
            : scoped.CommandForFileAsync(file!.Value, args, CancellationToken.None);
        await _subtitleDelayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Current() || !await Command([relative ? "add" : "set", "sub-delay", value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)]).ConfigureAwait(false)
                || !Current()) return null;
            var actual = await handle.GetNumberAsync("sub-delay", CancellationToken.None).ConfigureAwait(false);
            if (!Current() || actual is not { } delay || !double.IsFinite(delay)) return null;
            if (notice) await Command(["show-text", "字幕延迟：${sub-delay} 秒", "1200"]).ConfigureAwait(false);
            return Current() ? delay : null;
        }
        catch (Exception error) { Log.Warn(Category, "调整字幕延迟失败", error); return null; }
        finally { _subtitleDelayGate.Release(); }
    }

    public async Task ExitNativeFullscreenOrStopAsync()
    {
        var handle = _current;
        var generation = Generation;
        if (handle?.PictureInHostWindow != false) return;
        var fullscreen = await GetTextAsync(handle, generation, "fullscreen").ConfigureAwait(false);
        if (!IsCurrent(handle, generation)) return;
        if (fullscreen == "yes") await SetPropertyAsync(handle, generation, "fullscreen", false).ConfigureAwait(false);
        else if (fullscreen == "no") await StopAsync().ConfigureAwait(false);
    }

    public Task SetPropertyAsync(string name, object? value)
    {
        if (name is "sid" or "sub-visibility" && _active is { } attempt)
            Interlocked.Exchange(ref attempt.ExternalSubtitleSelection, 1);
        var handle = _current;
        if (PlayingWithSvp && MpvSvp.BlocksCommand(["set", name,
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""])) return Task.CompletedTask;
        return handle is null ? Task.CompletedTask : SetPropertyAsync(handle, Generation, name, value);
    }

    public Task ApplyScreenshotDirectoryAsync(string directory) => SetPropertyAsync("screenshot-directory", directory);

    /// <summary>
    /// 「截图格式与画质那几行改了，推给正在播的那一部」（用户令 2026-10-11：截图参数做成设置页的自定义选项）。
    /// 与 <see cref="ApplyScreenshotDirectoryAsync"/> 同一语义，与 <see cref="ApplySubtitleStyleAsync"/> 同一手法：
    /// 整组发，不猜哪条变了 —— 这几行没有「不设置」的档位，在运行中的播放器上「不推」就是「保持我上一分钟发的
    /// 那条」。选项表由 <see cref="MpvBaseline.ScreenshotOptions.ToMpvOptions"/> 出（起播那一路的同一份），
    /// WebP 三条只随 webp 格式发。没在播就是无事可做，不算错误。
    /// </summary>
    public async Task ApplyScreenshotOptionsAsync()
    {
        if (_current is not { } handle) return;
        var generation = Generation;
        var revision = Interlocked.Increment(ref _screenshotOptionsRevision);
        var options = MpvBaseline.ScreenshotOptions.From(settings.Mpv).ToMpvOptions().ToArray();
        var scoped = handle as IFileScopedPlayerControl;
        var file = scoped?.FileGeneration;
        bool Current() => IsCurrent(handle, generation) && revision == Volatile.Read(ref _screenshotOptionsRevision)
            && (scoped is null || file == scoped.FileGeneration);
        await _screenshotOptionsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var failures = new List<string>();
            foreach (var (name, value) in options)
            {
                if (!Current()) return;
                try
                {
                    var accepted = true;
                    if (scoped is not null)
                        accepted = await scoped.CommandForFileAsync(file!.Value, ["set", name, value], CancellationToken.None).ConfigureAwait(false);
                    else if (handle is IPlayerControl control)
                        accepted = await control.CommandAsync(["set", name, value], CancellationToken.None).ConfigureAwait(false);
                    else await handle.SetPropertyAsync(name, value, CancellationToken.None).ConfigureAwait(false);
                    if (!accepted) failures.Add(name);
                }
                catch (Exception error)
                {
                    Log.Warn(Category, $"应用截图选项 {name} 失败", error);
                    failures.Add(name);
                }
            }
            if (Current() && failures.Count > 0)
                throw new InvalidOperationException($"部分截图选项未应用：{string.Join("、", failures)}。已保存的偏好仍用于下次播放。");
        }
        finally { _screenshotOptionsGate.Release(); }
    }

    private async Task SetPropertyAsync(IPlaybackHandle handle, long generation, string name, object? value)
    {
        if (!IsCurrent(handle, generation)) return;
        try { await handle.SetPropertyAsync(name, value, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) { Log.Warn(Category, $"设置 mpv 属性 {name} 失败", error); }
    }

    /// <summary>Unspecified live options restore mpv defaults; omitting a write would keep the old value.</summary>
    public async Task ApplySubtitleStyleAsync()
    {
        if (_current is not { } handle) return;
        var generation = Generation;
        var revision = Interlocked.Increment(ref _subtitleStyleRevision);
        var scoped = handle as IFileScopedPlayerControl;
        var file = scoped?.FileGeneration;
        var wanted = MpvOutputOptions.SubtitleAppearance(settings.Playback)
            .ToDictionary(option => option.Key, option => option.Value, StringComparer.Ordinal);
        bool Current() => ReferenceEquals(_current, handle) && Generation == generation
            && Volatile.Read(ref _subtitleStyleRevision) == revision
            && (scoped is null || _active is { Ending: false } && file == scoped.FileGeneration);

        await _subtitleStyleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Current()) return;
            var plan = new List<KeyValuePair<string, string>>(MpvOutputOptions.SubtitleStyleOptions.Count);
            var unavailable = new List<string>();
            foreach (var name in MpvOutputOptions.SubtitleStyleOptions)
            {
                if (!Current()) return;
                var value = wanted.TryGetValue(name, out var chosen)
                    ? chosen
                    : await GetTextAsync(handle, generation, $"option-info/{name}/default-value").ConfigureAwait(false);
                if (!Current()) return;
                if (value is null)
                {
                    var supported = await handle.HasOptionAsync(name, CancellationToken.None).ConfigureAwait(false);
                    if (!Current()) return;
                    if (supported == false)
                    {
                        Log.Info(Category, $"当前内核不支持字幕选项 {name}，跳过该项");
                    }
                    else unavailable.Add(name);
                    continue;
                }
                plan.Add(new(name, value));
            }

            foreach (var (name, value) in plan)
            {
                if (!Current()) return;
                try
                {
                    if (scoped is not null)
                    {
                        if (!await scoped.CommandForFileAsync(file!.Value, ["set", name, value], CancellationToken.None).ConfigureAwait(false))
                            unavailable.Add(name);
                    }
                    else if (handle is IPlayerControl control)
                    {
                        if (!await control.CommandAsync(["set", name, value], CancellationToken.None).ConfigureAwait(false))
                            unavailable.Add(name);
                    }
                    else await handle.SetPropertyAsync(name, value, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    Log.Warn(Category, $"应用字幕选项 {name} 失败", error);
                    unavailable.Add(name);
                }
            }
            if (Current() && unavailable.Count > 0)
                throw new InvalidOperationException($"部分字幕外观未应用：{string.Join("、", unavailable)}。已保存的偏好仍会用于下次播放。");
        }
        finally { _subtitleStyleGate.Release(); }
    }

    public async Task<IReadOnlyList<MpvTrack>> GetTracksAsync()
    {
        var handle = _current;
        if (handle is null) return [];
        var generation = Generation;
        try
        {
            var tracks = await handle.GetTracksAsync(CancellationToken.None).ConfigureAwait(false);
            return IsCurrent(handle, generation) ? tracks : [];
        }
        catch (Exception error) { Log.Warn(Category, "读取 mpv 轨道列表失败", error); return []; }
    }

    public async Task<IReadOnlyList<SkipChapter>> GetChaptersAsync()
    {
        var handle = _current;
        if (handle is null) return [];
        var generation = Generation;
        try
        {
            var chapters = await handle.GetChaptersAsync(CancellationToken.None).ConfigureAwait(false);
            return IsCurrent(handle, generation) ? chapters : [];
        }
        catch (Exception error) { Log.Warn(Category, "读取 mpv 章节列表失败", error); return []; }
    }

    public async Task<double?> GetNumberAsync(string name)
    {
        var handle = _current;
        if (handle is null) return null;
        var generation = Generation;
        try
        {
            var value = await handle.GetNumberAsync(name, CancellationToken.None).ConfigureAwait(false);
            return IsCurrent(handle, generation) ? value : null;
        }
        catch (Exception error) { Log.Warn(Category, $"读取 mpv 属性 {name} 失败", error); return null; }
    }

    public Task<string?> GetTextAsync(string name)
    {
        var handle = _current;
        return handle is null ? Task.FromResult<string?>(null) : GetTextAsync(handle, Generation, name);
    }

    private async Task<string?> GetTextAsync(IPlaybackHandle handle, long generation, string name)
    {
        if (!IsCurrent(handle, generation)) return null;
        try
        {
            var value = await handle.GetTextAsync(name, CancellationToken.None).ConfigureAwait(false);
            return IsCurrent(handle, generation) ? value : null;
        }
        catch (Exception error) { Log.Warn(Category, $"读取 mpv 属性 {name} 失败", error); return null; }
    }

    public async Task<bool> CommandAsync(params string[] arguments)
    {
        if (arguments.Length > 0 && (arguments[0] is "sub-add" or "sub-remove"
            || arguments.Length > 1 && arguments[1] is "sid" or "sub-visibility") && _active is { } attempt)
            Interlocked.Exchange(ref attempt.ExternalSubtitleSelection, 1);
        if (_current is not IPlayerControl control || PlayingWithSvp && MpvSvp.BlocksCommand(arguments)) return false;
        try { return await control.CommandAsync(arguments, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error)
        {
            Log.Warn(Category, $"执行 mpv 命令 {(arguments.Length > 0 ? arguments[0] : "空命令")} 失败", error);
            return false;
        }
    }
    /// <summary>One user selection, bound to the file that owned the menu before its first await.</summary>
    public async Task<IReadOnlyList<MpvTrack>?> SelectTrackAsync(bool audio, int? id)
    {
        if (!audio && _active is { } attempt) Interlocked.Exchange(ref attempt.ExternalSubtitleSelection, 1);
        var handle = _current;
        var generation = Generation;
        if (handle is not IPlayerControl control || !handle.HasControlChannel) return null;
        var scoped = handle as IFileScopedPlayerControl;
        var file = scoped?.FileGeneration;
        bool Current() => IsCurrent(handle, generation) && (scoped is null || file == scoped.FileGeneration);
        Task<bool> Command(IReadOnlyList<string> args) => scoped is null
            ? control.CommandAsync(args, CancellationToken.None)
            : scoped.CommandForFileAsync(file!.Value, args, CancellationToken.None);
        if (!Current()) return null;
        var tracks = await handle.GetTracksAsync(CancellationToken.None).ConfigureAwait(false);
        if (!Current() || id is { } chosen && !tracks.Any(track => track.Id == chosen && (audio ? track.IsAudio : track.IsSubtitle))) return null;
        if (!await Command(["set", audio ? "aid" : "sid", id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no"]).ConfigureAwait(false)
            || !Current()) return null;
        if (!audio && id is not null && !await Command(["set", "sub-visibility", "yes"]).ConfigureAwait(false)) return null;
        if (!Current()) return null;
        tracks = await handle.GetTracksAsync(CancellationToken.None).ConfigureAwait(false);
        return Current() ? tracks : null;
    }

    public async Task<bool> SetShaderGroupAsync(ShaderGroup? group)
    {
        var handle = _current;
        if (handle is not IPlayerControl control || !handle.HasControlChannel) return false;
        var generation = Generation;
        // The native file counter is not the service counter: HandOver and Swap each advance it.
        // Capture once before waiting/reading, and retain it for both writes and rollback.
        var fileControl = control as IFileScopedPlayerControl;
        var fileGeneration = fileControl?.FileGeneration;
        var batchControl = control is IShaderBatchPlayerControl { SupportsShaderBatch: true } batch ? batch : null;
        var batchGeneration = batchControl?.FileGeneration;
        await _shaderGate.WaitAsync().ConfigureAwait(false);
        var before = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = new List<string>();
        var stateWasKnown = ShaderStateKnown;
        IReadOnlyList<KeyValuePair<string, string>>? batchRollback = null;
        try
        {
            if (!Current()) return false;
            if (group is not null && group.ResolveShaderPaths(ShaderGroupCatalog.ShaderRoot).Any(path => !File.Exists(path)))
                throw new InvalidOperationException("目标着色器链缺少文件，未改变当前配置");
            var launch = _launchOptions;
            var chainCount = _launchGroupOptionCount;
            if (handle.UsesCustomConfig)
            {
                // The planner precedes mpv.conf; only the handle's pre-command snapshot can restore it.
                var restore = handle.ShaderRestoreOptions;
                if (restore is null || ShaderGroupCatalog.NeutralOptions.Any(option =>
                    option.Key != "glsl-shaders" && !restore.ContainsKey(option.Key)))
                    throw new InvalidOperationException("读不到自定义配置的画质恢复基线");
                launch = restore.ToArray();
                chainCount = 0;
            }
            var profiles = await handle.GetTextAsync("profile-list", CancellationToken.None).ConfigureAwait(false);
            if (!Current()) return false;
            var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, _) in ShaderGroupCatalog.NeutralOptions)
            {
                if (name == "glsl-shaders")
                {
                    defaults[name] = "";
                    continue;
                }
                var value = await handle.GetTextAsync($"option-info/{name}/default-value", CancellationToken.None).ConfigureAwait(false);
                if (!Current()) return false;
                if (value is null) throw new InvalidOperationException($"读不到 {name} 的恢复基线");
                defaults[name] = value;
            }
            var options = ShaderSwitch.Options(launch, chainCount, group, ShaderGroupCatalog.ShaderRoot, profiles, defaults);
            foreach (var (name, _) in options)
            {
                string? value;
                if (name == "glsl-shaders")
                {
                    var paths = await handle.GetStringListAsync(name, CancellationToken.None).ConfigureAwait(false);
                    value = paths is null ? null : MpvListValue.JoinFiles(paths);
                }
                else value = await handle.GetTextAsync(name, CancellationToken.None).ConfigureAwait(false);
                if (!Current()) return false;
                if (value is null) throw new InvalidOperationException($"读不到 {name} 的切换前状态");
                before[name] = value;
            }
            if (batchControl is not null)
            {
                if (!Current()) return false;
                // Config parsing may apply every valid entry even when another entry is rejected.
                // Save the entire rollback before entering native code; keep the shader list last.
                batchRollback = options.Select(pair => new KeyValuePair<string, string>(pair.Key, before[pair.Key])).ToArray();
                if (!await batchControl.ApplyShaderOptionsForFileAsync(batchGeneration!.Value, options, CancellationToken.None).ConfigureAwait(false))
                    throw new InvalidOperationException("播放器未完整应用着色器配置");
            }
            else
            {
                foreach (var (name, value) in options)
                {
                    if (!Current()) return false;
                    changed.Add(name);
                    if (!await CommandAsync(["set", name, value]).ConfigureAwait(false))
                        throw new InvalidOperationException($"播放器拒绝 {name}");
                }
            }
            if (!Current()) return false;
            ShaderStateKnown = true;
            Log.Info(Category, group is null ? "已关闭着色器" : $"已切换着色器档位：{group.Name}");
            return true;
        }
        catch (Exception error)
        {
            var restored = true;
            if (batchRollback is not null)
            {
                if (!Current()) return false;
                try
                {
                    restored = await batchControl!.ApplyShaderOptionsForFileAsync(batchGeneration!.Value,
                        batchRollback, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    restored = false;
                    Log.Warn(Category, "着色器整批回滚失败", rollbackError);
                }
            }
            foreach (var name in changed.AsEnumerable().Reverse())
            {
                if (!Current()) return false;
                try
                {
                    restored &= await CommandAsync(["set", name, before[name]]).ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    restored = false;
                    Log.Warn(Category, $"着色器回滚 {name} 失败", rollbackError);
                }
            }
            if (Current()) ShaderStateKnown = restored && stateWasKnown;
            Log.Warn(Category, restored ? "着色器切换失败，保留切换前状态" : "着色器切换与回滚均未完成，实际状态未知", error);
            return false;
        }
        finally { _shaderGate.Release(); }

        // A retained handle may already contain B while the service has not established B yet.
        // Do not bind a new transaction in that handoff gap; normal external controls keep their contract.
        bool Current() => ReferenceEquals(_current, handle) && generation == Generation
            && (fileControl is null || _active is { Ending: false } && fileGeneration == fileControl.FileGeneration)
            && (batchControl is null || batchGeneration == batchControl.FileGeneration);

        Task<bool> CommandAsync(IReadOnlyList<string> arguments) => fileControl is null
            ? control.CommandAsync(arguments, CancellationToken.None)
            : fileControl.CommandForFileAsync(fileGeneration!.Value, arguments, CancellationToken.None);
    }

    internal static int StopReportPercent(bool isDonghua, int markWatchedPercent, int donghuaMarkWatchedPercent) =>
        isDonghua ? donghuaMarkWatchedPercent : markWatchedPercent;

    internal static bool ShouldReportStop(long positionTicks, long runTimeTicks, bool enabled, int percent) =>
        enabled && runTimeTicks > 0 && positionTicks >= runTimeTicks * (Math.Clamp(percent, MarkWatchedFloor, 100) / 100d);
}
