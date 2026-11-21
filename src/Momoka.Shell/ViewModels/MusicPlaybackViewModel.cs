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
using Momoka.Shell.Localization;
using Momoka.Shell.Platform;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>Owns music intent independently of the page; the shared playback service owns native lifetime.</summary>
public sealed partial class MusicPlaybackViewModel : ObservableObject, IDisposable
{
    /// <summary>播放页封面要的那一档宽度（DIP）。飞牛网页版封面最大 480，这里留一档余量给高 DPI。</summary>
    private const int CoverArtWidth = 560;

    /// <summary>
    /// 氛围底图另要一张很小的解码结果：铺满整窗时放大本身就是一层柔化，再压一层应用内亚克力，
    /// 比把 560 的封面放大再糊一遍省内存，也不会和封面抢同一份解码缓存。
    /// </summary>
    private const int AmbientArtWidth = 40;

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
    private MusicLyrics? _lyrics;
    private long _lyricsTrack = -1;
    private int _activeLyricIndex = -1;

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

    /// <summary>播放页那张大封面。播放栏收起时页面不在树上，图片不会真的下载。</summary>
    [ObservableProperty] public partial CardItem? CoverArtwork { get; set; }

    /// <summary>铺满窗口的氛围底图，只要一张很小的解码结果。</summary>
    [ObservableProperty] public partial CardItem? AmbientArtwork { get; set; }

    /// <summary>此刻在放的那首歌本身；播放页要点它才能拿到专辑、封面和格式。</summary>
    [ObservableProperty] public partial EmbyItem? CurrentItem { get; set; }

    /// <summary>进度条中间那枚格式角标（FLAC、MP3…）。服务器没给就收起，不猜。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatVisibility))]
    public partial string FormatLabel { get; set; } = "";

    /// <summary>歌词行。稳定集合：换曲只清空重填，页面控件不重建。</summary>
    public ObservableCollection<MusicLyricLine> Lyrics { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LyricsListVisibility))]
    [NotifyPropertyChangedFor(nameof(LyricsEmptyVisibility))]
    public partial bool HasLyrics { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LyricsNoteVisibility))]
    public partial bool LyricsSynced { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LyricsLoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(LyricsListVisibility))]
    [NotifyPropertyChangedFor(nameof(LyricsEmptyVisibility))]
    public partial bool LyricsLoading { get; set; }

    [ObservableProperty] public partial string LyricsEmptyText { get; set; } = UiText.Translate("暂无歌词");

    /// <summary>刚唱到的那一句；播放页据它把列表滚进视野。纯文本歌词一直是 null。</summary>
    [ObservableProperty] public partial MusicLyricLine? ActiveLyric { get; set; }

    [ObservableProperty] public partial double Position { get; set; }
    [ObservableProperty] public partial double Duration { get; set; } = 1;
    [ObservableProperty] public partial string PositionClock { get; set; } = "0:00";
    [ObservableProperty] public partial string DurationClock { get; set; } = "0:00";
    [ObservableProperty] public partial double Volume { get; set; } = 100;
    [ObservableProperty] public partial bool Muted { get; set; }
    [ObservableProperty] public partial bool Favorite { get; set; }
    [ObservableProperty] public partial Visibility FavoriteVisibility { get; set; }
    [ObservableProperty] public partial bool Shuffle { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayRepeatLabel))]
    public partial string RepeatLabel { get; set; } = "顺序播放";
    public string DisplayRepeatLabel => UiText.Translate(RepeatLabel);
    [ObservableProperty] public partial int QueueIndex { get; set; } = -1;

    /// <summary>
    /// 此刻在放的那一首的 <see cref="EmbyItem.IdentityKey"/>，没有在放就是空串。
    /// <para>
    /// 音乐页拿它把曲目表里那一行标出来（左缘强调色、标题换色）。用身份键而不是 Id：同一个 Id 在两台服务器上
    /// 是两首不同的歌。停止、换曲和队列结束都由这一位自己说，页面不必去猜播放状态。
    /// </para>
    /// </summary>
    [ObservableProperty] public partial string CurrentKey { get; set; } = "";

    public Visibility BarVisibility => Visible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FormatVisibility => FormatLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LyricsListVisibility => HasLyrics && !LyricsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LyricsEmptyVisibility => !HasLyrics && !LyricsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LyricsLoadingVisibility => LyricsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LyricsNoteVisibility => HasLyrics && !LyricsSynced ? Visibility.Visible : Visibility.Collapsed;
    public string PlayGlyph => char.ConvertFromUtf32(Paused ? 0xE768 : 0xE769);
    public string PlayLabel => Paused ? UiText.Translate("播放") : UiText.Translate("暂停");
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
                CurrentKey = item.IdentityKey;
                CurrentItem = item;
                FormatLabel = MusicCatalog.FormatLabel(item);
                Title = item.Name;
                Subtitle = MusicCatalog.Subtitle(item);
                Favorite = item.UserData?.IsFavorite == true;
                FavoriteVisibility = scope.Connection.IsPlex ? Visibility.Collapsed : Visibility.Visible;
                Preparing = true;
                Paused = false;
                Position = 0;
                PositionClock = "0:00";
                Visible = true;
                ResetLyrics();
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
            if (Current()) Noticed?.Invoke(UiText.Format("音乐播放失败：{0}", Failure.Describe(error)), InfoBarSeverity.Error);
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

            // 文件装载完成之后标签才读得到；每首只问一次，换曲由 ResetLyrics 把闸门打开。
            if (status.Loaded) BeginLyricsRead(_trackVersion);
            UpdateActiveLyric(status.Position);
        });
    }

    /// <summary>
    /// 读到歌词。先问服务器自己的歌词表（飞牛音乐的 <c>lyric/list</c> 把内嵌、同名 .lrc 与手工歌词
    /// 一并返回），服务器没有或这个服务没有核对过的接口时，再读当前文件标签里的原文。
    /// <para>
    /// 这里只读属性，不碰播放生命周期；换曲、停止、转视频和换代都在回填时按版本核对，
    /// 迟到的歌词不会贴到下一首上。
    /// </para>
    /// </summary>
    private void BeginLyricsRead(long trackVersion)
    {
        if (_lyricsTrack == trackVersion || !HasIntent || !_playback.IsAudioOnly) return;
        if (CurrentItem is not { } item) return;
        _lyricsTrack = trackVersion;
        _ = ReadLyricsAsync(trackVersion, item);
    }

    private async Task ReadLyricsAsync(long trackVersion, EmbyItem item)
    {
        var parsed = await ReadServerLyricsAsync(item).ConfigureAwait(false);
        if (parsed is not { IsEmpty: false })
            parsed = MusicLyrics.Parse(await ReadTagLyricsAsync().ConfigureAwait(false));

        _ui.Run(() =>
        {
            if (_disposed || trackVersion != _trackVersion || !HasIntent || !_playback.IsAudioOnly) return;
            ApplyLyrics(parsed);
        });
    }

    /// <summary>
    /// 服务器歌词。没有歌词、服务没有核对过这条接口或读取失败都返回 null，由调用方退回文件标签；
    /// 失败不弹通知打扰正在听歌的人。
    /// </summary>
    private async Task<MusicLyrics?> ReadServerLyricsAsync(EmbyItem item)
    {
        try
        {
            var scope = item.Source ?? _session.Capture(item);
            return await scope.ExecuteAsync((client, token) => client.GetLyricsAsync(item.Id, token), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Log.Debug("音乐", $"读取服务端歌词失败：{error.Message}");
            return null;
        }
    }

    /// <summary>
    /// 当前音频文件自己的歌词标签。走 mpv 的 <c>metadata/by-key</c>：标签名先从
    /// <c>display-tags</c> 里找像歌词的那一个，读不到再按几个常见名兜底。
    /// </summary>
    private async Task<string?> ReadTagLyricsAsync()
    {
        string? raw = null;
        try
        {
            var tags = await _playback.GetStringListAsync("display-tags").ConfigureAwait(false);
            if (MusicLyrics.FindTag(tags) is { } key)
                raw = await _playback.GetTextAsync($"metadata/by-key/{key}").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                foreach (var fallback in MusicLyrics.FallbackTags)
                {
                    raw = await _playback.GetTextAsync($"metadata/by-key/{fallback}").ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(raw)) break;
                }
            }
        }
        catch (Exception error)
        {
            // 读取失败与「文件没有歌词」在屏上是同一件事，不弹通知打扰正在听歌的人。
            Log.Debug("音乐", $"读取内嵌歌词失败：{error.Message}");
        }
        return raw;
    }

    /// <summary>把读到的结果换成行。没有内容时停在空态，不把上一首的歌词留在屏上。</summary>
    private void ApplyLyrics(MusicLyrics? parsed)
    {
        LyricsLoading = false;
        foreach (var line in Lyrics) line.IsActive = false;
        Lyrics.Clear();
        _lyrics = parsed;
        _activeLyricIndex = -1;
        ActiveLyric = null;
        if (_lyrics is not { IsEmpty: false })
        {
            HasLyrics = false;
            LyricsSynced = false;
            LyricsEmptyText = UiText.Translate("暂无歌词");
            return;
        }

        foreach (var line in _lyrics.Lines) Lyrics.Add(line);
        HasLyrics = true;
        LyricsSynced = _lyrics.Synced;
        UpdateActiveLyric(Position);
    }

    /// <summary>播放进度跨过一句时只改这两行，其余行不动；纯文本歌词没有「正在唱」。</summary>
    private void UpdateActiveLyric(double position)
    {
        if (_lyrics is not { Synced: true } lyrics || Lyrics.Count == 0) return;
        var index = lyrics.ActiveIndex(position);
        if (index == _activeLyricIndex) return;
        if (_activeLyricIndex >= 0 && _activeLyricIndex < Lyrics.Count) Lyrics[_activeLyricIndex].IsActive = false;
        _activeLyricIndex = index;
        if (index >= 0 && index < Lyrics.Count)
        {
            Lyrics[index].IsActive = true;
            ActiveLyric = Lyrics[index];
        }
        else
        {
            ActiveLyric = null;
        }
    }

    /// <summary>换曲或停止时把歌词栏清回「正在读取」；新的一首装好后再读。</summary>
    private void ResetLyrics()
    {
        _lyrics = null;
        _lyricsTrack = -1;
        _activeLyricIndex = -1;
        ActiveLyric = null;
        HasLyrics = false;
        LyricsSynced = false;
        LyricsLoading = true;
        LyricsEmptyText = "";
        foreach (var line in Lyrics) line.IsActive = false;
        Lyrics.Clear();
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

        // 播放页那两张只在页面真的在树上时才会被 Image 领取并下载（见 CardArtwork）。
        // 这里只把卡片造出来，收起播放页时它们不占解码内存，也不发请求。
        CoverArtwork?.AbandonPoster();
        CoverArtwork = new CardItem(item, _images, CoverArtWidth, aspect: 1, indicators: false);
        AmbientArtwork?.AbandonPoster();
        AmbientArtwork = new CardItem(item, _images, AmbientArtWidth, aspect: 1, indicators: false);
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
        CurrentKey = "";
        CurrentItem = null;
        FormatLabel = "";
        Artwork?.AbandonPoster();
        Artwork = null;
        CoverArtwork?.AbandonPoster();
        CoverArtwork = null;
        AmbientArtwork?.AbandonPoster();
        AmbientArtwork = null;
        ResetLyrics();
        LyricsLoading = false;
        LyricsEmptyText = UiText.Translate("暂无歌词");
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
