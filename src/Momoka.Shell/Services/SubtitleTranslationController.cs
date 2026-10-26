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
    private void PreferencesChanged() => ui.Run(Restart);

    private void Restart()
    {
        if (_disposed) return;
        var old = Interlocked.Exchange(ref _run, null);
        if (old is not null) Cancel(old);
        if (!settings.Settings.SubtitleTranslation.Enabled) return;
        var session = playback.CaptureSubtitleTranslationSession();
        if (session is null) return;
        // The settings document remains editable; a request always keeps one coherent service snapshot.
        var snapshot = JsonSerializer.Deserialize<SubtitleTranslationSettings>(
            JsonSerializer.Serialize(settings.Settings.SubtitleTranslation))!;
        var key = credentials.GetApiKey(snapshot, snapshot.Provider);
        var source = new CancellationTokenSource();
        _run = source;
        var previous = _work;
        _work = RunAfterPreviousAsync(previous, session, snapshot, key, source);
    }

    private async Task RunAfterPreviousAsync(Task previous, ISubtitleTranslationPlaybackSession session,
        SubtitleTranslationSettings snapshot, string key, CancellationTokenSource source)
    {
        try
        {
            // Old clear must finish before a same-file new overlay starts (including provider/locale changes).
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            if (source.IsCancellationRequested || _disposed) return;
            await SubtitleTranslationRuntime.RunAsync(session, snapshot, key,
                MpvSubtitleTranslation.ScriptPath(AppContext.BaseDirectory), client.TranslateAsync,
                message => { if (!source.IsCancellationRequested && session.IsCurrent && !_disposed) Noticed?.Invoke(message); },
                source.Token).ConfigureAwait(false);
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
