using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Media;
using Windows.Media.Playback;
using MediaSource = Windows.Media.Core.MediaSource;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private CancellationTokenSource? _storeTrailerLoad;
    private EmbyItem? _storeTrailerSubject;
    private StoreTrailerPreview? _storeTrailerPreview;
    private MediaPlayer? _storeTrailerPlayer;
    private MediaPlayerElement? _storeTrailerElement;
    private MediaSource? _storeTrailerSource;
    private MediaPlaybackItem? _storeTrailerItem;
    private MenuFlyout? _storeTrailerTracksMenu;
    private int _storeTrailerGeneration;
    private EmbyItem? _storeTrailerFailed;
    private bool _storeTrailerOpened;
    private bool _storeTrailerSuspended;
    private bool _storeTrailerUserPaused;
    private bool _storeTrailerScrollPaused;
    private CancellationTokenRegistration _storeTrailerCancellation;
    private StoreYouTubePreview? _storeYouTube;
    private bool _storeYouTubeMuted = true;
    private readonly StoreVideoBrush _storeVideoBrush = new();

    internal void SetStorePreviewActive(bool active)
    {
        _storeTrailerSuspended = !active;
        if (active) UpdateStoreTrailer(); else StopStoreTrailer();
    }

    private void UpdateStoreTrailer()
    {
        var subject = ViewModel.CurrentItem;
        var wide = ViewModel.PageWidth >= DetailHero.StoreWideFloor;
        if (_storeTrailerSuspended || !IsLoaded || !ViewModel.StoreMode || !wide || _actionAccessibility.HighContrast
            || subject is null || !ItemTrailers.HasAny(subject, subject.Source?.Connection.ServerKind ?? default)
            || subject.Source?.IsAvailable != true)
        {
            StopStoreTrailer();
            return;
        }
        LayoutStoreTrailer();

        if (ReferenceEquals(subject, _storeTrailerSubject))
        {
            if (_storeTrailerOpened) StoreBackdrop.Visibility = Visibility.Collapsed;
            return;
        }
        if (ReferenceEquals(subject, _storeTrailerFailed)) return;
        StopStoreTrailer();
        LayoutStoreTrailer();
        _storeTrailerSubject = subject;
        var generation = _storeTrailerGeneration;
        _storeTrailerLoad = CancellationTokenSource.CreateLinkedTokenSource(subject.Source.Lifetime);
        _storeTrailerCancellation = _storeTrailerLoad.Token.Register(() =>
        {
            if (generation == _storeTrailerGeneration) _storeTrailerPreview?.Cancel();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation == _storeTrailerGeneration) StopStoreTrailer();
            });
        });
        _ = StartStoreTrailerAsync(subject, generation, _storeTrailerLoad.Token);
    }

    private void LayoutStoreTrailer()
    {
        StoreTrailerHost.Width = TrailerPreview.BackgroundWidth(ViewModel.PageWidth);
        StoreTrailerHost.Height = TrailerPreview.BackgroundHeight(ViewModel.PageWidth);
        StoreTrailerControls.Margin = new Thickness(0, StoreTrailerHost.Height + 32, 48, 0);
        StoreTrailerFoundation.Height = StoreTrailerHost.Height + StoreBackdropBlend.FoundationTail;
        StoreTrailerFoundation.Background = _storeVideoBrush.ResolveFoundation(ThemeHost.Current.Colors.Window,
            ViewModel.PageWidth, StoreTrailerHost.Width, StoreTrailerHost.Height, StoreTrailerHost.Margin.Right);
        StoreTrailerFade.Background = _storeVideoBrush.Resolve(_storeAmbientBrush.Frame, ThemeHost.Current.Colors.Window,
            ViewModel.PageWidth, ViewModel.StoreAmbientHeight, StoreTrailerHost.Width, StoreTrailerHost.Height,
            StoreTrailerHost.Margin.Right);
    }

    private async Task StartStoreTrailerAsync(EmbyItem subject, int generation, CancellationToken token)
    {
        StoreTrailerPreview? preview = null;
        try
        {
            preview = await StoreTrailerPreview.CreateAsync(subject, token);
            if (token.IsCancellationRequested || generation != _storeTrailerGeneration
                || !ReferenceEquals(ViewModel.CurrentItem, subject) || !ViewModel.StoreMode || !IsLoaded) return;
            // 元数据/媒体准备期间文字列可能完成实测，首帧使用当前环境底坐标。
            LayoutStoreTrailer();
            if (preview is null)
            {
                var id = TrailerPreview.YouTubeId(ItemTrailers.Remote(subject));
                if (id is null) return;
                _storeYouTube = new StoreYouTubePreview();
                _storeTrailerScrollPaused = Body.VerticalOffset > StoreTrailerHost.Height * 0.5;
                _storeYouTube.SetActive(!_storeTrailerScrollPaused);
                StoreTrailerContent.Children.Add(_storeYouTube.Element);
                StoreTrailerHost.Visibility = Visibility.Visible;
                StoreTrailerHost.Opacity = 0;
                UpdateStoreTrailerControls();
                await _storeYouTube.StartAsync(id, Path.Combine(_request!.Services.GetRequiredService<Momoka.Infrastructure.AppPaths>().LogDirectory,
                    "trailer-preview", Guid.NewGuid().ToString("N")), token, playing =>
                {
                    if (token.IsCancellationRequested || generation != _storeTrailerGeneration || subject.Source?.IsAvailable != true) return;
                    if (!playing) { _storeTrailerFailed = subject; StopStoreTrailer(); return; }
                    LayoutStoreTrailer();
                    _storeTrailerOpened = true;
                    StoreTrailerFoundation.Visibility = Visibility.Visible;
                    StoreTrailerHost.Opacity = 1;
                    StoreTrailerControls.Visibility = Visibility.Visible;
                    StoreBackdrop.Visibility = Visibility.Collapsed;
                    ScrollStoreTrailer();
                });
                return;
            }
            _storeTrailerPreview = preview;
            preview = null;
            StartStoreMedia(MediaSource.CreateFromUri(_storeTrailerPreview.Source), generation);

        }
        catch (OperationCanceledException) { }
        catch
        {
            if (generation == _storeTrailerGeneration) { _storeTrailerFailed = subject; StopStoreTrailer(); }
        }
        finally { if (preview is not null) await preview.DisposeAsync(); }
    }

    // Gallery Samples/MediaPlayerElement/MediaPlayerElementPage.xaml/.xaml.cs,
    // v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7：独立播放器，无transport控件的自动播放样例。
    private void StartStoreMedia(MediaSource source, int generation)
    {
        _storeTrailerPlayer = new MediaPlayer { IsMuted = true, IsLoopingEnabled = true, AutoPlay = true };
        _storeTrailerPlayer.CommandManager.IsEnabled = false;
        _storeTrailerPlayer.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _storeTrailerGeneration && IsLoaded && ViewModel.StoreMode)
            {
                LayoutStoreTrailer();
                _storeTrailerOpened = true;
                StoreTrailerFoundation.Visibility = Visibility.Visible;
                StoreTrailerHost.Opacity = 1;
                StoreTrailerHost.Visibility = Visibility.Visible;
                StoreTrailerControls.Visibility = Visibility.Visible;
                StoreBackdrop.Visibility = Visibility.Collapsed;
                ScrollStoreTrailer();
            }
        });
        _storeTrailerPlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _storeTrailerGeneration) { _storeTrailerFailed = _storeTrailerSubject; StopStoreTrailer(); }
        });
        _storeTrailerElement = new MediaPlayerElement
        {
            AreTransportControlsEnabled = false,
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false,
            IsTabStop = false
        };
        _storeTrailerElement.SetMediaPlayer(_storeTrailerPlayer);
        StoreTrailerContent.Children.Add(_storeTrailerElement);
        _storeTrailerSource = source;
        _storeTrailerItem = new MediaPlaybackItem(_storeTrailerSource);
        _storeTrailerItem.AudioTracksChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _storeTrailerGeneration) UpdateStoreTrailerControls();
        });
        _storeTrailerItem.TimedMetadataTracksChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _storeTrailerGeneration)
            {
                _storeTrailerTracksMenu?.Hide();
                if (_storeTrailerItem is { } item)
                    foreach (var index in SubtitleTracks()) item.TimedMetadataTracks.SetPresentationMode((uint)index, TimedMetadataTrackPresentationMode.Hidden);
                UpdateStoreTrailerControls();
            }
        });
        _storeTrailerPlayer.Source = _storeTrailerItem;
        UpdateStoreTrailerControls();

    }


    private void StopStoreTrailer()
    {
        _storeTrailerGeneration++;
        _storeTrailerCancellation.Dispose();
        _storeTrailerCancellation = default;
        _storeTrailerLoad?.Cancel();
        _storeTrailerLoad?.Dispose();
        _storeTrailerLoad = null;
        _storeTrailerSubject = null;
        _storeTrailerItem = null;
        _storeTrailerTracksMenu?.Hide();
        _storeTrailerTracksMenu = null;
        _storeTrailerOpened = false;
        _storeTrailerUserPaused = false;
        _storeTrailerScrollPaused = false;
        _storeYouTubeMuted = true;
        _storeYouTube?.Dispose();
        _storeYouTube = null;
        StoreTrailerHost.Visibility = Visibility.Collapsed;
        StoreTrailerFoundation.Visibility = Visibility.Collapsed;
        StoreTrailerFoundation.Background = null;
        StoreTrailerControls.Visibility = Visibility.Collapsed;
        _storeVideoBrush.Clear();
        StoreTrailerFade.Background = null;
        var player = _storeTrailerPlayer;
        var element = _storeTrailerElement;
        var source = _storeTrailerSource;
        var preview = _storeTrailerPreview;
        _storeTrailerPlayer = null;
        _storeTrailerElement = null;
        _storeTrailerSource = null;
        _storeTrailerPreview = null;
        preview?.Cancel();
        try { player?.Pause(); if (player is not null) player.Source = null; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        try { element?.SetMediaPlayer(null); StoreTrailerContent.Children.Clear(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        try { source?.Dispose(); }
        finally
        {
            try { player?.Dispose(); }
            finally { if (preview is not null) _ = DisposeStorePreviewAsync(preview); }
        }
        StoreBackdrop.Visibility = _actionAccessibility.HighContrast ? Visibility.Collapsed : ViewModel.StoreBackdropVisibility;
    }

    private static async Task DisposeStorePreviewAsync(StoreTrailerPreview preview)
    {
        try { await preview.DisposeAsync(); }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
    }

    private void OnStoreTrailerPause(object sender, RoutedEventArgs args)
    {
        _storeTrailerUserPaused = !_storeTrailerUserPaused;
        if (_storeTrailerUserPaused || _storeTrailerScrollPaused) _storeTrailerPlayer?.Pause();
        else _storeTrailerPlayer?.Play();
        _storeYouTube?.SetActive(!_storeTrailerUserPaused && !_storeTrailerScrollPaused);
        UpdateStoreTrailerControls();
    }

    private void OnStoreTrailerMute(object sender, RoutedEventArgs args)
    {
        if (_storeTrailerPlayer is not null) _storeTrailerPlayer.IsMuted = !_storeTrailerPlayer.IsMuted;
        if (_storeYouTube is not null) { _storeYouTubeMuted = !_storeYouTubeMuted; _storeYouTube.Mute(_storeYouTubeMuted); }
        UpdateStoreTrailerControls();
    }

    private void UpdateStoreTrailerControls()
    {
        ((FontIcon)StoreTrailerPause.Icon).Glyph = _storeTrailerUserPaused ? "\uE768" : "\uE769";
        var muted = _storeYouTube is not null ? _storeYouTubeMuted : _storeTrailerPlayer?.IsMuted != false;
        ((FontIcon)StoreTrailerMute.Icon).Glyph = muted ? "\uE74F" : "\uE767";
        StoreTrailerSubtitles.Visibility = SubtitleTracks().Any() ? Visibility.Visible : Visibility.Collapsed;
        StoreTrailerAudio.Visibility = _storeTrailerItem?.AudioTracks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var pause = _storeTrailerUserPaused ? "播放预告片" : "暂停预告片";
        var mute = muted ? "取消预告片静音" : "预告片静音";
        StoreTrailerPause.Label = pause;
        StoreTrailerMute.Label = mute;
        AutomationProperties.SetName(StoreTrailerSubtitles, "选择预告片字幕");
        AutomationProperties.SetName(StoreTrailerAudio, "选择预告片音频");
        AutomationProperties.SetName(StoreTrailerPause, pause);
        AutomationProperties.SetName(StoreTrailerMute, mute);
        ToolTipService.SetToolTip(StoreTrailerPause, pause);
        ToolTipService.SetToolTip(StoreTrailerMute, mute);
    }

    private IEnumerable<int> SubtitleTracks()
    {
        if (_storeTrailerItem is not { } item) yield break;
        for (var i = 0; i < item.TimedMetadataTracks.Count; i++)
            if (item.TimedMetadataTracks[i].TimedMetadataKind is Windows.Media.Core.TimedMetadataKind.Subtitle
                or Windows.Media.Core.TimedMetadataKind.Caption or Windows.Media.Core.TimedMetadataKind.ImageSubtitle) yield return i;
    }

    private void OnStoreTrailerAudio(object sender, RoutedEventArgs args)
    {
        if (_storeTrailerItem is not { } item || item.AudioTracks.Count <= 1) return;
        var menu = new MenuFlyout();
        var group = Guid.NewGuid().ToString("N");
        for (var i = 0; i < item.AudioTracks.Count; i++)
        {
            var index = i;
            var track = item.AudioTracks[i];
            var entry = new RadioMenuFlyoutItem
            {
                Text = TrackLabel(track.Label, track.Language, "音轨", i),
                GroupName = group,
                IsChecked = item.AudioTracks.SelectedIndex == i
            };
            entry.Click += (_, _) => { if (ReferenceEquals(item, _storeTrailerItem) && index < item.AudioTracks.Count) item.AudioTracks.SelectedIndex = index; };
            menu.Items.Add(entry);
        }
        ShowStoreTrackMenu(menu, StoreTrailerAudio);
    }

    private void OnStoreTrailerSubtitles(object sender, RoutedEventArgs args)
    {
        if (_storeTrailerItem is not { } item || !SubtitleTracks().Any()) return;
        var menu = new MenuFlyout();
        var group = Guid.NewGuid().ToString("N");
        void Select(int selected)
        {
            if (!ReferenceEquals(item, _storeTrailerItem) || selected >= item.TimedMetadataTracks.Count) return;
            foreach (var index in SubtitleTracks()) item.TimedMetadataTracks.SetPresentationMode((uint)index,
                index == selected ? TimedMetadataTrackPresentationMode.PlatformPresented : TimedMetadataTrackPresentationMode.Hidden);
        }
        var off = new RadioMenuFlyoutItem
        {
            Text = "关闭字幕",
            GroupName = group,
            IsChecked = SubtitleTracks().All(index => item.TimedMetadataTracks.GetPresentationMode((uint)index) == TimedMetadataTrackPresentationMode.Hidden)
        };
        off.Click += (_, _) => Select(-1);
        menu.Items.Add(off);
        foreach (var index in SubtitleTracks())
        {
            var track = item.TimedMetadataTracks[index];
            var entry = new RadioMenuFlyoutItem
            {
                Text = TrackLabel(track.Label, track.Language, "字幕", index),
                GroupName = group,
                IsChecked = item.TimedMetadataTracks.GetPresentationMode((uint)index) == TimedMetadataTrackPresentationMode.PlatformPresented
            };
            entry.Click += (_, _) => Select(index);
            menu.Items.Add(entry);
        }
        ShowStoreTrackMenu(menu, StoreTrailerSubtitles);
    }

    private static string TrackLabel(string label, string language, string kind, int index) =>
        !string.IsNullOrWhiteSpace(label) ? label : !string.IsNullOrWhiteSpace(language) ? language : $"{kind} {index + 1}";

    private void ShowStoreTrackMenu(MenuFlyout menu, AppBarButton anchor)
    {
        _storeTrailerTracksMenu?.Hide();
        _storeTrailerTracksMenu = menu;
        menu.ShowAt(anchor);
    }

    private void ScrollStoreTrailer()
    {
        if (_storeTrailerPlayer is null && _storeYouTube is null) return;
        if (Body.VerticalOffset > StoreTrailerContent.ActualHeight * 0.5 + StoreTrailerHost.Margin.Top)
        {
            _storeTrailerScrollPaused = true;
            _storeTrailerPlayer?.Pause();
            _storeYouTube?.SetActive(false);
        }
        else if (_storeTrailerScrollPaused)
        {
            _storeTrailerScrollPaused = false;
            if (!_storeTrailerUserPaused) { _storeTrailerPlayer?.Play(); _storeYouTube?.SetActive(true); }
        }
    }
}
