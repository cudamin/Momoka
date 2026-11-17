using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Media;
using Momoka.Theming;
using Windows.Media.Playback;
using MediaSource = Windows.Media.Core.MediaSource;

namespace Momoka.Shell.Views;

/// <summary>
/// 订阅详情的预告片：商店宽版式在右上角自动静音循环，沉浸档由「预告片」键列出条目。
/// 数据只从当前登录身份能看到的媒体库里取（<see cref="ViewModels.MoviePilotMediaViewModel.TrailerSubject"/>），
/// MoviePilot 的媒体详情接口本身不带预告片。
/// </summary>
public sealed partial class MoviePilotSubscriptionPage
{
    private CancellationTokenSource? _trailerLoad;
    private CancellationTokenRegistration _trailerCancellation;
    private EmbyItem? _trailerSubject;
    private EmbyItem? _trailerFailed;
    private StoreTrailerPreview? _trailerPreview;
    private MediaPlayer? _trailerPlayer;
    private MediaPlayerElement? _trailerElement;
    private MediaSource? _trailerSource;
    private StoreYouTubePreview? _trailerYouTube;
    private Momoka.Shell.Media.YouTubeSession? _trailerYouTubeSession;
    private int _trailerGeneration;
    private bool _trailerOpened;
    private bool _trailerSuspended;
    private bool _trailerUserPaused;
    private bool _trailerScrollPaused;
    private bool _trailerYouTubeMuted = true;
    private MenuFlyout? _trailerMenu;
    private readonly StoreVideoBrush _trailerBrush = new();

    internal void SetStorePreviewActive(bool active)
    {
        _trailerSuspended = !active;
        if (active) UpdateStoreTrailer(); else StopStoreTrailer();
    }

    /// <summary>
    /// 设置页「界面 → 预告片 → 详情页自动播放」改完当场生效：这一页若还开着，收掉或重新接上右上角那支预告。
    /// </summary>
    private void WatchTrailerPreferences()
    {
        Loaded += (_, _) =>
        {
            ShellPrefs.TrailerChanged -= OnTrailerPreferencesChanged;
            ShellPrefs.TrailerChanged += OnTrailerPreferencesChanged;
            // 设置页登录/退出 YouTube 之后，已经收起的失败预览要能当场重试一次。
            if (_request?.YouTube is { } session && !ReferenceEquals(_trailerYouTubeSession, session))
            {
                if (_trailerYouTubeSession is not null) _trailerYouTubeSession.Changed -= OnYouTubeSessionChanged;
                _trailerYouTubeSession = session;
                session.Changed += OnYouTubeSessionChanged;
            }
        };
        Unloaded += (_, _) =>
        {
            ShellPrefs.TrailerChanged -= OnTrailerPreferencesChanged;
            if (_trailerYouTubeSession is not null) _trailerYouTubeSession.Changed -= OnYouTubeSessionChanged;
        };
    }

    private void OnYouTubeSessionChanged()
    {
        if (!IsLoaded) return;
        if (DispatcherQueue.HasThreadAccess) RetryStoreTrailer();
        else DispatcherQueue.TryEnqueue(RetryStoreTrailer);
    }

    private void RetryStoreTrailer()
    {
        _trailerFailed = null;
        UpdateStoreTrailer();
    }

    private void OnTrailerPreferencesChanged(TrailerSettings preferences)
    {
        if (!IsLoaded) return;
        if (DispatcherQueue.HasThreadAccess) UpdateStoreTrailer();
        else DispatcherQueue.TryEnqueue(UpdateStoreTrailer);
    }

    /// <summary>商店档、宽版式、有图有预告、自动播放开着且不是高对比度时才铺预览；其它情况一律收干净。</summary>
    internal void UpdateStoreTrailer()
    {
        var subject = MediaViewModel.TrailerSubject;
        if (_trailerSuspended || !IsLoaded || !MediaViewModel.Plain || !MediaViewModel.StoreWide
            || !ShellPrefs.Trailers.DetailAutoplay
            || _accessibility.HighContrast || subject is null || subject.Source?.IsAvailable != true
            || (subject.LocalTrailerCount is not > 0 && TrailerPreview.YouTubeId(subject) is null))
        {
            StopStoreTrailer();
            return;
        }
        LayoutStoreTrailer();

        if (ReferenceEquals(subject, _trailerSubject))
        {
            if (_trailerOpened) StoreBackdrop.Visibility = Visibility.Collapsed;
            return;
        }
        if (ReferenceEquals(subject, _trailerFailed)) return;
        StopStoreTrailer();
        LayoutStoreTrailer();
        _trailerSubject = subject;
        var generation = _trailerGeneration;
        _trailerLoad = CancellationTokenSource.CreateLinkedTokenSource(subject.Source.Lifetime);
        _trailerCancellation = _trailerLoad.Token.Register(() =>
        {
            if (generation == _trailerGeneration) _trailerPreview?.Cancel();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation == _trailerGeneration) StopStoreTrailer();
            });
        });
        _ = StartStoreTrailerAsync(subject, generation, _trailerLoad.Token);
    }

    private void LayoutStoreTrailer()
    {
        // 同媒体详情页（2026-11-17 用户令「订阅详情页一起改」）：画面大小仍按内容列，右边那道缝按窗口宽给；
        // 视频局部底已铺满窗口，所以这里的坐标系就是窗口宽。
        var window = MediaViewModel.PageWidth > 0 ? MediaViewModel.PageWidth : MediaViewModel.StorePageWidth;
        var gap = DetailHero.StorePreviewEdgeGap(window);
        StoreTrailerHost.Width = TrailerPreview.BackgroundWidth(MediaViewModel.StorePageWidth);
        StoreTrailerHost.Height = TrailerPreview.BackgroundHeight(MediaViewModel.StorePageWidth);
        StoreTrailerHost.Margin = MediaViewModel.StorePreviewMargin;
        StoreTrailerControls.Margin = new Thickness(0, StoreTrailerHost.Height + 32, MediaViewModel.StorePreviewMargin.Right + 32, 0);
        StoreTrailerFoundation.Height = StoreTrailerHost.Height + StoreBackdropBlend.FoundationTail;
        StoreTrailerFoundation.Background = _trailerBrush.ResolveFoundation(ThemeHost.Current.Colors.Window,
            window, StoreTrailerHost.Width, StoreTrailerHost.Height, gap);
        StoreTrailerFade.Background = _trailerBrush.Resolve(_storeAmbientBrush.Frame, ThemeHost.Current.Colors.Window,
            window, MediaViewModel.StoreAmbientHeight, StoreTrailerHost.Width, StoreTrailerHost.Height, gap);
    }

    private async Task StartStoreTrailerAsync(EmbyItem subject, int generation, CancellationToken token)
    {
        StoreTrailerPreview? preview = null;
        StoreYouTubePreview? youTube = null;
        try
        {
            preview = await StoreTrailerPreview.CreateAsync(subject, token);
            if (token.IsCancellationRequested || generation != _trailerGeneration
                || !ReferenceEquals(MediaViewModel.TrailerSubject, subject) || !MediaViewModel.Plain || !IsLoaded) return;
            LayoutStoreTrailer();
            if (preview is null)
            {
                var id = TrailerPreview.YouTubeId(subject);
                if (id is null) { _trailerFailed = subject; StopStoreTrailer(); return; }
                if (_request?.YouTube is not { } session) { _trailerFailed = subject; StopStoreTrailer(); return; }
                youTube = new StoreYouTubePreview(session.GetEnvironmentAsync);
                _trailerYouTube = youTube;
                youTube.PlaybackChanged += () => DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation == _trailerGeneration && ReferenceEquals(_trailerYouTube, youTube)) UpdateStoreTrailerControls();
                });
                youTube.CaptionsChanged += () => DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation == _trailerGeneration && ReferenceEquals(_trailerYouTube, youTube)) UpdateStoreTrailerControls();
                });
                _trailerScrollPaused = DetailScroll.VerticalOffset > StoreTrailerHost.Height * 0.5;
                youTube.SetActive(!_trailerScrollPaused);
                StoreTrailerContent.Children.Add(youTube.Element);
                StoreTrailerHost.Visibility = Visibility.Visible;
                StoreTrailerHost.Opacity = 0;
                UpdateStoreTrailerControls();
                await youTube.StartAsync(id, Path.Combine((_request?.Paths ?? AppPaths.Default).LogDirectory,
                    "trailer-preview", Guid.NewGuid().ToString("N")), token, playing =>
                {
                    if (token.IsCancellationRequested || generation != _trailerGeneration
                        || subject.Source?.IsAvailable != true) return;
                    if (!playing)
                    {
                        _trailerFailed = subject;
                        // 收预览会连 WebView 一起释放，登录判定先读出来。
                        var needsLogin = youTube.NeedsLogin;
                        StopStoreTrailer();
                        if (needsLogin) MediaViewModel.NotifyYouTubeLoginRequired(_request?.YouTubeLogin);
                        return;
                    }
                    LayoutStoreTrailer();
                    _trailerOpened = true;
                    StoreTrailerFoundation.Visibility = Visibility.Visible;
                    StoreTrailerHost.Opacity = 1;
                    StoreTrailerControls.Visibility = Visibility.Visible;
                    StoreBackdrop.Visibility = Visibility.Collapsed;
                    ScrollStoreTrailer();
                });
                return;
            }
            _trailerPreview = preview;
            preview = null;
            StartStoreMedia(MediaSource.CreateFromUri(_trailerPreview.Source), generation);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (generation == _trailerGeneration)
            {
                // 错误回调不一定跑过；预览还在树上且判定需要登录的，在这里补一次提示。
                var needsLogin = youTube is not null && ReferenceEquals(_trailerYouTube, youTube) && youTube.NeedsLogin;
                _trailerFailed = subject;
                StopStoreTrailer();
                if (needsLogin) MediaViewModel.NotifyYouTubeLoginRequired(_request?.YouTubeLogin);
            }
        }
        finally { if (preview is not null) await preview.DisposeAsync(); }
    }

    /// <summary>Gallery Samples/MediaPlayerElement/MediaPlayerElementPage.xaml：独立播放器，无传输控件。</summary>
    private void StartStoreMedia(MediaSource source, int generation)
    {
        _trailerPlayer = new MediaPlayer { IsMuted = true, IsLoopingEnabled = true, AutoPlay = true };
        _trailerPlayer.CommandManager.IsEnabled = false;
        _trailerPlayer.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _trailerGeneration && IsLoaded && MediaViewModel.Plain)
            {
                LayoutStoreTrailer();
                _trailerOpened = true;
                StoreTrailerFoundation.Visibility = Visibility.Visible;
                StoreTrailerHost.Opacity = 1;
                StoreTrailerHost.Visibility = Visibility.Visible;
                StoreTrailerControls.Visibility = Visibility.Visible;
                StoreBackdrop.Visibility = Visibility.Collapsed;
                ScrollStoreTrailer();
            }
        });
        _trailerPlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _trailerGeneration) { _trailerFailed = _trailerSubject; StopStoreTrailer(); }
        });
        _trailerElement = new MediaPlayerElement
        {
            AreTransportControlsEnabled = false,
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false,
            IsTabStop = false
        };
        _trailerElement.SetMediaPlayer(_trailerPlayer);
        StoreTrailerContent.Children.Add(_trailerElement);
        _trailerSource = source;
        _trailerPlayer.Source = new MediaPlaybackItem(source);
        UpdateStoreTrailerControls();
    }

    private void StopStoreTrailer()
    {
        _trailerGeneration++;
        _trailerCancellation.Dispose();
        _trailerCancellation = default;
        _trailerLoad?.Cancel();
        _trailerLoad?.Dispose();
        _trailerLoad = null;
        _trailerSubject = null;
        _trailerOpened = false;
        _trailerUserPaused = false;
        _trailerScrollPaused = false;
        _trailerYouTubeMuted = true;
        _trailerYouTube?.Dispose();
        _trailerYouTube = null;
        StoreTrailerHost.Visibility = Visibility.Collapsed;
        StoreTrailerFoundation.Visibility = Visibility.Collapsed;
        StoreTrailerFoundation.Background = null;
        StoreTrailerControls.Visibility = Visibility.Collapsed;
        _trailerBrush.Clear();
        StoreTrailerFade.Background = null;
        var player = _trailerPlayer;
        var element = _trailerElement;
        var source = _trailerSource;
        var preview = _trailerPreview;
        _trailerPlayer = null;
        _trailerElement = null;
        _trailerSource = null;
        _trailerPreview = null;
        preview?.Cancel();
        try { player?.Pause(); if (player is not null) player.Source = null; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        try { element?.SetMediaPlayer(null); StoreTrailerContent.Children.Clear(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        try { source?.Dispose(); }
        finally
        {
            try { player?.Dispose(); }
            finally { if (preview is not null) _ = DisposeTrailerPreviewAsync(preview); }
        }
        var storeShown = MediaViewModel.Plain && MediaViewModel.Backdrop is not null && !_accessibility.HighContrast;
        StoreBackdrop.Visibility = storeShown ? Visibility.Visible : Visibility.Collapsed;
        StoreAmbient.Visibility = storeShown ? Visibility.Visible : Visibility.Collapsed;
    }

    private static async Task DisposeTrailerPreviewAsync(StoreTrailerPreview preview)
    {
        try { await preview.DisposeAsync(); }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
    }

    private void OnStoreTrailerPause(object sender, RoutedEventArgs args)
    {
        var playing = _trailerYouTube is { } youTube ? youTube.IsPlaying : !_trailerUserPaused;
        _trailerUserPaused = playing;
        if (_trailerUserPaused || _trailerScrollPaused) _trailerPlayer?.Pause();
        else _trailerPlayer?.Play();
        _trailerYouTube?.SetActive(!_trailerUserPaused && !_trailerScrollPaused);
        UpdateStoreTrailerControls();
    }

    private void OnStoreTrailerMute(object sender, RoutedEventArgs args)
    {
        if (_trailerPlayer is not null) _trailerPlayer.IsMuted = !_trailerPlayer.IsMuted;
        if (_trailerYouTube is not null) { _trailerYouTubeMuted = !_trailerYouTubeMuted; _trailerYouTube.Mute(_trailerYouTubeMuted); }
        UpdateStoreTrailerControls();
    }

    private void UpdateStoreTrailerControls()
    {
        var paused = _trailerYouTube is { } youTube
            ? _trailerOpened && !youTube.IsPlaying
            : _trailerUserPaused;
        ((FontIcon)StoreTrailerPause.Content).Glyph = paused ? "\uE768" : "\uE769";
        var muted = _trailerYouTube is not null ? _trailerYouTubeMuted : _trailerPlayer?.IsMuted != false;
        ((FontIcon)StoreTrailerMute.Content).Glyph = muted ? "\uE74F" : "\uE767";
        var pause = paused ? "播放预告片" : "暂停预告片";
        var mute = muted ? "取消预告片静音" : "预告片静音";
        AutomationProperties.SetName(StoreTrailerPause, pause);
        AutomationProperties.SetName(StoreTrailerMute, mute);
        ToolTipService.SetToolTip(StoreTrailerPause, pause);
        ToolTipService.SetToolTip(StoreTrailerMute, mute);
    }

    private void ScrollStoreTrailer()
    {
        if (_trailerPlayer is null && _trailerYouTube is null) return;
        if (DetailScroll.VerticalOffset > StoreTrailerContent.ActualHeight * 0.5 + StoreTrailerHost.Margin.Top)
        {
            if (_trailerScrollPaused) return;
            _trailerScrollPaused = true;
            _trailerPlayer?.Pause();
            _trailerYouTube?.SetActive(false);
        }
        else if (_trailerScrollPaused)
        {
            _trailerScrollPaused = false;
            if (!_trailerUserPaused) { _trailerPlayer?.Play(); _trailerYouTube?.SetActive(true); }
        }
    }

    private void OnMediaNoticeActionClick(object sender, RoutedEventArgs e) => MediaViewModel.InvokeNoticeAction();

    private void OnTrailerButtonClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button anchor && MediaViewModel.TrailerSubject is { } subject)
            _ = ShowTrailerMenuAsync(anchor, subject);
    }

    /// <summary>
    /// 沉浸档的「预告片」：先列服务器上的本地预告（在 Momoka 里播），再列远程链接（交系统浏览器）。
    /// 商店宽版式不显示这颗键 —— 那一档预告片已经铺在右上角自动播放。
    /// </summary>
    private async Task ShowTrailerMenuAsync(Button anchor, EmbyItem subject)
    {
        _trailerMenu?.Hide();
        var menu = new MenuFlyout();
        _trailerMenu = menu;
        menu.Items.Add(new MenuFlyoutItem { Text = "正在读取预告片…", IsEnabled = false });
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trailerMenu, menu)) _trailerMenu = null;
        };
        menu.ShowAt(anchor);

        var options = new List<(string Label, EmbyItem? Item, string? Url)>();
        string? error = null;
        if (subject.LocalTrailerCount > 0 && subject.Source is { IsAvailable: true } scope
            && _pageLifetime is { } lifetime)
        {
            try
            {
                var local = await scope.ExecuteAsync((client, token) => client.GetLocalTrailersAsync(subject.Id, token),
                    lifetime.Token);
                foreach (var item in local)
                    if (item.IsPlayable && item.Id != subject.Id && item.Source?.Key == scope.Key
                        && !string.IsNullOrWhiteSpace(item.Id))
                        options.Add((string.IsNullOrWhiteSpace(item.Name) ? "预告片" : item.Name, item, null));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception failure) { error = "读取服务器预告片失败：" + Failure.Describe(failure); }
        }
        foreach (var link in ItemTrailers.Remote(subject))
            options.Add((link.Name ?? "预告片", null, link.Url));

        if (!ReferenceEquals(_trailerMenu, menu)) return;
        menu.Items.Clear();
        foreach (var option in options)
        {
            var trailer = option.Item;
            var entry = new MenuFlyoutItem
            {
                Text = option.Label + (trailer is null ? " · 在浏览器打开" : " · 在 Momoka 播放")
            };
            AutomationProperties.SetAutomationId(entry, "SubscriptionTrailerOption" + menu.Items.Count);
            entry.Click += (_, _) =>
            {
                if (trailer is not null) _ = PlaySubscriptionTrailerAsync(trailer);
                else if (option.Url is { } url) _request?.Launcher?.OpenUrl(url);
            };
            menu.Items.Add(entry);
        }
        if (error is not null)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(new MenuFlyoutItem { Text = error, IsEnabled = false });
        }
        else if (menu.Items.Count == 0)
            menu.Items.Add(new MenuFlyoutItem { Text = "服务器未返回可用的预告片", IsEnabled = false });
    }

    private async Task PlaySubscriptionTrailerAsync(EmbyItem trailer)
    {
        try
        {
            if (_request?.PlayTrailer is { } play) await play(trailer);
            else if (_request?.Play is { } playSubscribed)
                await playSubscribed(new MoviePilotPlaybackTarget(trailer, null, []));
        }
        catch (Exception error) { MediaViewModel.OperationFailed(error); }
    }
}
