using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Music;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>Owns music intent independently of the page; the shared playback service owns native lifetime.</summary>
public sealed partial class MusicPlaybackViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackService _playback;
    private readonly EmbySession _session;
    private readonly EmbyImageStore _images;
    private readonly IUiDispatcher _ui;
    private readonly ISettingsService _settings;
    private readonly MusicQueue _queue = new();
    private CancellationTokenSource? _intent;
    private long _intentVersion;
    private long _statusGeneration;
    private long _trackVersion;
    private long _minimumGeneration;
    private Task _stopping = Task.CompletedTask;
    private bool _disposed;
    private bool _favoriteBusy;
    private bool _scrubbing;
    private bool _loadingQueue;

    public MusicPlaybackViewModel(PlaybackService playback, EmbySession session, EmbyImageStore images, IUiDispatcher ui, ISettingsService settings)
    {
        _playback = playback;
        _session = session;
        _images = images;
        _ui = ui;
        _settings = settings;
        playback.StatusUpdated += OnStatus;
        playback.NowPlayingUpdated += OnPlaying;
    }

    public ObservableCollection<EmbyItem> Queue { get; } = [];
    internal Func<Task>? BeforeStart { get; set; }
    public event Action<string, InfoBarSeverity>? Noticed;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BarVisibility))]
    public partial bool Visible { get; set; }
    [ObservableProperty] public partial bool Preparing { get; set; }
    public Visibility PreparingVisibility => Preparing ? Visibility.Visible : Visibility.Collapsed;
    partial void OnPreparingChanged(bool value) => OnPropertyChanged(nameof(PreparingVisibility));
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayGlyph))]
    [NotifyPropertyChangedFor(nameof(PlayLabel))]
    public partial bool Paused { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Subtitle { get; set; } = "";
    [ObservableProperty] public partial CardItem? Artwork { get; set; }
    [ObservableProperty] public partial double Position { get; set; }
    [ObservableProperty] public partial double Duration { get; set; } = 1;
    [ObservableProperty] public partial string PositionClock { get; set; } = "0:00";
    [ObservableProperty] public partial string DurationClock { get; set; } = "0:00";
    [ObservableProperty] public partial double Volume { get; set; } = 100;
    [ObservableProperty] public partial bool Muted { get; set; }
    [ObservableProperty] public partial bool Favorite { get; set; }
    [ObservableProperty] public partial Visibility FavoriteVisibility { get; set; }
    [ObservableProperty] public partial bool Shuffle { get; set; }
    [ObservableProperty] public partial string RepeatLabel { get; set; } = "顺序播放";
    [ObservableProperty] public partial int QueueIndex { get; set; } = -1;

    public Visibility BarVisibility => Visible ? Visibility.Visible : Visibility.Collapsed;
    public string PlayGlyph => char.ConvertFromUtf32(Paused ? 0xE768 : 0xE769);
    public string PlayLabel => Paused ? "播放" : "暂停";
    public bool HasIntent => _intent is not null;
    internal (long Intent, long Track, long Generation) BeginScrub()
    {
        _scrubbing = true;
        return (_intentVersion, _trackVersion, _playback.Generation);
    }
    internal async Task EndScrubAsync((long Intent, long Track, long Generation) owner, double value)
    {
        _scrubbing = false;
        if (owner == (_intentVersion, _trackVersion, _playback.Generation)) await SeekAsync(value);
    }
    internal void CancelScrub() => _scrubbing = false;

    internal async Task PlayAsync(EmbyItem item, IReadOnlyList<EmbyItem>? items = null,
        Func<CancellationToken, Task<IReadOnlyList<EmbyItem>>>? loadQueue = null)
    {
        if (_disposed || !MusicCatalog.IsAudio(item.Type)) return;
        var tracks = items?.Where(track => MusicCatalog.IsAudio(track.Type)).ToArray() ?? [item];
        var index = Array.FindIndex(tracks, track => ReferenceEquals(track, item));
        if (index < 0) index = Array.FindIndex(tracks, track => track.IdentityKey == item.IdentityKey);
        if (index < 0) { tracks = [item]; index = 0; }
        CancelIntent();
        _queue.Replace(tracks, index);
        Queue.Clear();
        foreach (var track in tracks) Queue.Add(track);
        await StartSelectedAsync(loadQueue);
    }

    private async Task StartSelectedAsync(Func<CancellationToken, Task<IReadOnlyList<EmbyItem>>>? loadQueue = null)
    {
        if (_disposed || _queue.Index < 0) return;
        CancelIntent();
        var owner = new CancellationTokenSource();
        _intent = owner;
        _minimumGeneration = _playback.Generation + 1;
        var version = ++_intentVersion;
        var token = owner.Token;
        _loadingQueue = loadQueue is not null;
        try
        {
            Title = _queue.Items[_queue.Index].Name;
            Subtitle = MusicCatalog.Subtitle(_queue.Items[_queue.Index]);
            Preparing = true;
            Visible = true;
            await _stopping;
            if (!Current()) return;
            if (BeforeStart is { } beforeStart) await beforeStart();
            if (!Current()) return;
            if (_playback.ValidateAudio() is { } problem) throw new InvalidOperationException(problem);
            if (loadQueue is not null)
            {
                var selected = _queue.Items[_queue.Index];
                var index = _queue.Index;
                var all = await loadQueue(token);
                if (!Current()) return;
                if (index >= all.Count || all[index].IdentityKey != selected.IdentityKey)
                    index = all.ToList().FindIndex(track => track.IdentityKey == selected.IdentityKey);
                if (index < 0) throw new InvalidOperationException("歌单已变化，请重新选择歌曲");
                _queue.Replace(all, index);
                Queue.Clear();
                foreach (var track in all) Queue.Add(track);
                _loadingQueue = false;
            }
            while (Current() && _queue.Index >= 0)
            {
                var item = _queue.Items[_queue.Index];
                ++_trackVersion;
                _minimumGeneration = _playback.Generation + 1;
                var scope = item.Source ?? _session.Capture(item);
                scope.ThrowIfUnavailable();
                QueueIndex = _queue.Index;
                Title = item.Name;
                Subtitle = MusicCatalog.Subtitle(item);
                Favorite = item.UserData?.IsFavorite == true;
                FavoriteVisibility = scope.Connection.IsPlex ? Visibility.Collapsed : Visibility.Visible;
                Preparing = true;
                Paused = false;
                Position = 0;
                PositionClock = "0:00";
                Visible = true;
                SetArtwork(item);
                var detail = await scope.ExecuteAsync((client, cancellation) => client.GetItemAsync(item.Id, cancellation), token);
                if (!Current() || !scope.IsAvailable) return;
                scope.Bind(detail);
                var source = detail.DefaultMediaSource;
                if (source is null) throw new InvalidOperationException("这首歌曲没有可播放的音频文件");
                var ticket = new PlaybackTicket { Item = detail, Source = source, AudioOnly = true, ExactStart = true };
                var result = await _playback.PlayAsync(ticket, scope, token);
                if (!Current()) return;
                if (result.Exit.Reason == PlaybackEndReason.Error)
                    throw new InvalidOperationException(result.Exit.Message ?? "音频播放失败");
                if (result.Exit.Reason != PlaybackEndReason.EndOfFile || _queue.Next(automatic: true) < 0) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current()) Noticed?.Invoke($"音乐播放失败：{Failure.Describe(error)}", InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_intent, owner))
            {
                _intent = null;
                Hide();
                if (_queue.Items.Any(track => track.Source is { IsAvailable: false }))
                {
                    _queue.Clear();
                    Queue.Clear();
                    QueueIndex = -1;
                }
            }
            owner.Dispose();
        }

        bool Current() => !_disposed && version == _intentVersion && ReferenceEquals(_intent, owner) && !token.IsCancellationRequested;
    }

    internal Task StopAsync()
    {
        var owned = HasIntent || _playback.IsAudioOnly;
        CancelIntent();
        Hide();
        _queue.Clear();
        _loadingQueue = false;
        Queue.Clear();
        QueueIndex = -1;
        return owned ? _stopping = _playback.StopAsync() : _stopping;
    }

    internal Task NextAsync() => !_loadingQueue && _queue.Next(automatic: false) >= 0 ? StartSelectedAsync() : Task.CompletedTask;
    internal Task PreviousAsync() => !_loadingQueue && _queue.Previous() >= 0 ? StartSelectedAsync() : Task.CompletedTask;
    internal Task SelectAsync(int index) => !_loadingQueue && _queue.Select(index) >= 0 ? StartSelectedAsync() : Task.CompletedTask;
    internal Task TogglePauseAsync() => ControlAsync("cycle", "pause");
    internal Task ToggleMuteAsync() => ControlAsync("cycle", "mute");
    internal Task SeekAsync(double seconds) => double.IsFinite(seconds)
        ? ControlAsync("seek", Math.Clamp(seconds, 0, Duration).ToString("0.###", CultureInfo.InvariantCulture), "absolute")
        : Task.CompletedTask;
    internal async Task SetVolumeAsync(double value)
    {
        if (!OwnsPlayback) return;
        var generation = _playback.Generation;
        if (!await _playback.SetVolumeAsync(value) || generation != _playback.Generation || !OwnsPlayback) return;
        var actual = await _playback.GetNumberAsync("volume");
        if (generation != _playback.Generation || !OwnsPlayback || actual is not { } reading || VolumeMemory.Level(reading) is not { } level) return;
        _settings.Settings.Audio.Volume = level;
        _settings.SaveDeferred();
        Volume = level;
    }
    private bool OwnsPlayback => HasIntent && _playback.IsAudioOnly && _statusGeneration == _playback.Generation;
    private async Task ControlAsync(params string[] arguments)
    {
        if (OwnsPlayback) await _playback.CommandAsync(arguments);
    }
    internal void ToggleShuffle() { Shuffle = !Shuffle; _queue.Shuffle = Shuffle; }
    internal void CycleRepeat()
    {
        _queue.Repeat = (MusicRepeat)(((int)_queue.Repeat + 1) % 3);
        RepeatLabel = _queue.Repeat switch { MusicRepeat.All => "列表循环", MusicRepeat.One => "单曲循环", _ => "顺序播放" };
    }
    internal async Task ToggleFavoriteAsync()
    {
        if (_favoriteBusy || _queue.Index < 0 || !HasIntent) return;
        var item = _queue.Items[_queue.Index];
        var scope = item.Source;
        if (scope is not { IsAvailable: true } || scope.Connection.IsPlex) return;
        var version = _intentVersion;
        var trackVersion = _trackVersion;
        var value = !Favorite;
        _favoriteBusy = true;
        try
        {
            await scope.ExecuteAsync((client, token) => client.SetFavoriteAsync(item.Id, value, token), _intent!.Token);
            if (version != _intentVersion || trackVersion != _trackVersion || !scope.IsAvailable) return;
            item.UserData ??= new();
            item.UserData.IsFavorite = value;
            Favorite = value;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (version == _intentVersion) Noticed?.Invoke(Failure.Describe(error), InfoBarSeverity.Error); }
        finally { _favoriteBusy = false; }
    }
    private void OnStatus(PlaybackUpdate<PlayerStatus> update)
    {
        var intent = _intentVersion;
        _ui.Run(() =>
        {
            if (_disposed || intent != _intentVersion || !HasIntent || !update.AudioOnly || update.Generation < _minimumGeneration
                || update.Generation != _playback.Generation || !_playback.IsAudioOnly) return;
            _statusGeneration = update.Generation;
            var status = update.Value;
            Preparing = !status.Loaded || status.Buffering;
            Paused = status.Paused;
            if (!_scrubbing) Position = Math.Max(0, status.Position);
            Duration = Math.Max(1, status.Duration);
            PositionClock = status.PositionClock;
            DurationClock = status.DurationClock;
            if (status.VolumeKnown) Volume = status.Volume;
            Muted = status.Muted;
        });
    }
    private void OnPlaying(PlaybackUpdate<EmbyItem?> update)
    {
        var intent = _intentVersion;
        _ui.Run(() =>
        {
            if (_disposed || intent != _intentVersion || update.Generation < _minimumGeneration || update.Generation != _playback.Generation) return;
            if (!update.AudioOnly && update.Value is not null)
            {
                if (!_playback.IsPlaying || _playback.IsAudioOnly || !ReferenceEquals(_playback.NowPlaying, update.Value)) return;
                CancelIntent();
                Hide();
            }
            else if (HasIntent && update.AudioOnly && update.Value is not null)
            {
                _statusGeneration = update.Generation;
                Title = update.Value.Name;
                Subtitle = MusicCatalog.Subtitle(update.Value);
            }
        });
    }
    private void SetArtwork(EmbyItem item)
    {
        Artwork?.AbandonPoster();
        Artwork = new CardItem(item, _images, 48, aspect: 1, indicators: false);
        Artwork.ClaimCard(this);
        _ = Artwork.EnsurePosterAsync(Task.CompletedTask);
    }
    private void CancelIntent()
    {
        ++_intentVersion;
        var old = _intent;
        _intent = null;
        old?.Cancel();
    }
    private void Hide()
    {
        _scrubbing = false;
        Visible = false;
        Preparing = false;
        Artwork?.AbandonPoster();
        Artwork = null;
    }
    public void Dispose()
    {
        _disposed = true;
        CancelIntent();
        Hide();
        _playback.StatusUpdated -= OnStatus;
        _playback.NowPlayingUpdated -= OnPlaying;
    }
}
