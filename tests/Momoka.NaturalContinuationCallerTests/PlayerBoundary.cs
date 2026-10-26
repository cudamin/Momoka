using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Playback;
using Momoka.NaturalContinuationCallerTests;

namespace Momoka.Shell.ViewModels;

// Only framework/presentation/service endpoints are inert. All Start/Next/NowPlaying/Leave/Stop
// control flow and lifetime fields are the original source nodes emitted by the SDK parser.
public sealed partial class PlayerViewModel
{
    private readonly FakePlayback _playback;
    private readonly MemorySettings _settings;
    private readonly Tape _tape;

    internal PlayerViewModel(FakePlayback playback, MemorySettings settings, EmbySession session, Tape tape)
    {
        _playback = playback;
        _settings = settings;
        _session = session;
        _tape = tape;
        _versionLoad = null;
        playback.NowPlaying = OnNowPlayingChanged;
    }

    internal int Hold => _playerHold;
    internal Task Idle => _playbackIdle?.Task ?? Task.CompletedTask;
    internal CancellationToken Lifetime => _lifetime.Token;
    internal EmbyItem? NowPlaying => _nowPlaying;
    internal EmbyItem? Parent => _parent;
    internal EmbySessionScope? PlayingScope => _playbackScope;
    internal long ActiveIntent => _activeIntent;
    internal int Attempt => _playbackAttempt;
    internal bool HasPreparation => _preparing is not null;
    internal bool CoverVisible => CoverUp;
    internal Func<EmbyItem, Task>? CoverWait { get; set; }
    internal Func<EmbyItem, Task>? BackdropLoad { get; set; }
    internal Func<Task>? PrepareStopAsync { get; set; }

    internal Task StartForFixture(EmbyItem item, EmbyItem? parent, IReadOnlyList<EmbyItem>? episodes,
        bool replaceExisting = false, long? intent = null, CancellationToken cancellation = default,
        PlaybackContinuation? continuation = null, PlaybackChoice? choice = null, EmbySessionScope? identity = null) =>
        StartPlaybackAsync(item, parent, choice, episodes, replaceExisting, identity, intent, cancellation, continuation);

    internal void NotifyForFixture(PlaybackUpdate<EmbyItem?> update) => OnNowPlayingChanged(update);
    internal void CancelPreparationForFixture() => _preparing?.Cancel();
    internal void CancelLifetimeForFixture() => _lifetime.Cancel();
    internal void InvalidateIntentForFixture() => _startIntent.CancelAll();
    internal long NewIntentForFixture() => _startIntent.Begin();
    internal void ChangeEpisodesForFixture(IReadOnlyList<EmbyItem> episodes) => Episodes = episodes;

    // Reading inert presentation fields prevents absent unrelated partials from creating CS0414.
    // None of these values can acquire native resources, persist settings or start service work.
    internal object?[] InertState =>
    [
        _aspect, _shaderContext, _outputWatch, _shaderResolver, _shaderPinned, _coverBackdropItemId,
        _coverBackdropLoad, _coverGeneration, _replacingPlayback, _presentedHeadless, _preparingPictureInHost,
        _seekTouched, _seekSent, _versionSwitchTarget, _volumeReady, _episodeLookupBusy
    ];

    internal double SubtitleDelay { get; private set; }
    internal double AudioDelay { get; private set; }
    internal IReadOnlyList<object> Tracks { get; private set; } = [];
    internal IReadOnlyList<SkipChapter> ChapterMarks { get; private set; } = [];
    internal EmbyItem? CurrentItem { get; private set; }
    internal string PlayingItemId { get; private set; } = "";
    private string PlayingItemKey => _nowPlaying?.IdentityKey ?? "";
    private string Title { get; set; } = "";
    private string Subtitle { get; set; } = "";
    private string SourceLabel { get; set; } = "";
    private bool EpisodeControlsVisible { get; set; }
    private object? ActiveShader { get; set; }
    private string CoverMessage { get; set; } = "";
    private bool CoverUp { get; set; }
    internal PlayerStatus LastStatus { get; private set; } = new();

    private Task WaitCoverBackdropAsync(EmbyItem item)
    {
        _tape.Add("cover-wait:" + item.IdentityKey);
        return CoverWait?.Invoke(item) ?? Task.CompletedTask;
    }
    private Task LoadCoverBackdropAsync(EmbyItem item)
    {
        _tape.Add("backdrop:" + item.IdentityKey);
        return BackdropLoad?.Invoke(item) ?? Task.CompletedTask;
    }
    private (int Width, int Height) PrepareShaderPlans(EmbyItem item, MediaSource source, EmbyItem? parent)
    {
        _shaderSettings = new ShaderAutomationSettings();
        return (1920, 1080);
    }
    private void OnUi(Action apply) => apply();
    private void FlushVolume(bool settled) => _tape.Add("flush-volume");
    private void ResetTimelineDrag() => _tape.Add("reset-timeline");
    private void InvalidateMenuInteractions() => _tape.Add("invalidate-menu");
    private void ClearChapterPeek() { }
    private void ApplyStatus(PlayerStatus status) => LastStatus = status;
    private void AdoptShaderLaunch(EmbyItem item) { }
    private void AdoptServerAspect(EmbyItem item) { }
    private void NoteEpisodeCount() { }
    private void NoteSubline() { }
    private void ApplySkipOffer() { }
    private MediaSource? PlayingSourceOrFirst(EmbyItem? item) => item?.MediaSources.FirstOrDefault();
    private static string PlayingSourceLabel(EmbyItem item) => "inert";
    private static Task LoadVersionsAsync(EmbyItem item, int generation, int discovery) => Task.CompletedTask;
    private static Task PushNativeShortcutsAsync(bool force) => Task.CompletedTask;
    private static Task PopulateTracksAsync(int generation) => Task.CompletedTask;
    private static Task RefineSkipSectionsAsync(int generation) => Task.CompletedTask;
    private static Task ApplyAspectAsync(int generation) => Task.CompletedTask;
    private static Task NoteAudioDeviceAsync(int generation) => Task.CompletedTask;
}

internal sealed class MemorySettings
{
    internal AppSettings Settings { get; } = new() { AggregateMode = true };
}
