using System.Text.Json;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Translation;

namespace Momoka.Shell.Services;

/// <summary>绑定应用/播放生命周期；切集、关窗、关闭翻译或改密钥立即撤销旧句任务。</summary>
public sealed class SubtitleTranslationController(
    PlaybackService playback,
    ISettingsService settings,
    SubtitleTranslationClient client,
    SubtitleTranslationCredentials credentials,
    Momoka.Shell.Platform.IUiDispatcher ui) : IDisposable
{
    private CancellationTokenSource? _run;
    private Task _work = Task.CompletedTask;
    private bool _attached;
    private bool _disposed;
    private Presentation? _presentation;
    private ISubtitleTranslationPlaybackSession? _session;
    public event Action<string>? Noticed;

    public void Attach()
    {
        if (_attached || _disposed) return;
        _attached = true;
        playback.NowPlayingUpdated += PlayingChanged;
        ShellPrefs.SubtitleTranslationChanged += PreferencesChanged;
        Restart();
    }

    private void PlayingChanged(PlaybackUpdate<Momoka.Emby.EmbyItem?> update) => ui.Run(() =>
    {
        if (update.Generation == playback.Generation) Restart();
    });
    private void PreferencesChanged() => ui.Run(() =>
    {
        var snapshot = Snapshot();
        if (_run is not null && _session?.IsCurrent == true && _presentation is { } presentation
            && SameRequest(presentation.Settings, snapshot))
        {
            Volatile.Write(ref presentation.Settings, snapshot);
            return;
        }
        Restart();
    });

    private SubtitleTranslationSettings Snapshot() => JsonSerializer.Deserialize<SubtitleTranslationSettings>(
        JsonSerializer.Serialize(settings.Settings.SubtitleTranslation))!;

    private static bool SameRequest(SubtitleTranslationSettings left, SubtitleTranslationSettings right) =>
        left.Enabled == right.Enabled && left.Provider == right.Provider
        && left.SourceLanguage == right.SourceLanguage && left.TargetLanguage == right.TargetLanguage
        && left.GoogleEndpoint == right.GoogleEndpoint && left.MicrosoftEndpoint == right.MicrosoftEndpoint
        && left.MicrosoftRegion == right.MicrosoftRegion && left.AiEndpoint == right.AiEndpoint
        && left.AiModel == right.AiModel && left.ProtectedGoogleApiKey == right.ProtectedGoogleApiKey
        && left.ProtectedMicrosoftApiKey == right.ProtectedMicrosoftApiKey && left.ProtectedAiApiKey == right.ProtectedAiApiKey;

    private sealed class Presentation(SubtitleTranslationSettings settings)
    {
        public SubtitleTranslationSettings Settings = settings;
    }

    private void Restart()
    {
        if (_disposed) return;
        var old = Interlocked.Exchange(ref _run, null);
        if (old is not null) Cancel(old);
        if (!settings.Settings.SubtitleTranslation.Enabled) return;
        var session = playback.CaptureSubtitleTranslationSession();
        if (session is null) return;
        // The settings document remains editable; a request always keeps one coherent service snapshot.
        var snapshot = Snapshot();
        var presentation = new Presentation(snapshot);
        _presentation = presentation;
        _session = session;
        var key = credentials.GetApiKey(snapshot, snapshot.Provider);
        var source = new CancellationTokenSource();
        _run = source;
        var previous = _work;
        _work = RunAfterPreviousAsync(previous, session, snapshot, key, source, presentation);
    }

    private async Task RunAfterPreviousAsync(Task previous, ISubtitleTranslationPlaybackSession session,
        SubtitleTranslationSettings snapshot, string key, CancellationTokenSource source, Presentation presentation)
    {
        try
        {
            // Old clear must finish before a same-file new overlay starts (including provider/locale changes).
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            if (source.IsCancellationRequested || _disposed) return;
            await SubtitleTranslationRuntime.RunAsync(session, snapshot, key,
                MpvSubtitleTranslation.ScriptPath(AppContext.BaseDirectory), client.TranslateAsync,
                message => ui.Run(() =>
                {
                    // Validate where the UI consumes it, including same-file provider/credential edits.
                    if (!source.IsCancellationRequested && ReferenceEquals(_presentation, presentation) && session.IsCurrent
                        && !_disposed && SameRequest(presentation.Settings, Snapshot())) Noticed?.Invoke(message);
                }),
                source.Token, () => Volatile.Read(ref presentation.Settings)).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _run, null, source);
            source.Dispose();
        }
    }

    public async Task StopAsync()
    {
        if (_attached)
        {
            playback.NowPlayingUpdated -= PlayingChanged;
            ShellPrefs.SubtitleTranslationChanged -= PreferencesChanged;
            _attached = false;
        }
        _disposed = true;
        var current = Interlocked.Exchange(ref _run, null);
        if (current is not null) Cancel(current);
        try { await _work.ConfigureAwait(false); } catch (Exception) { }
    }

    public void Dispose()
    {
        _ = StopAsync();
        Noticed = null;
    }

    private static void Cancel(CancellationTokenSource source)
    {
        try { source.Cancel(); } catch (ObjectDisposedException) { }
    }
}
