using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Music;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Windowing;

namespace Momoka.Shell.Views;

/// <summary>
/// 全屏音乐播放页。外壳把它当一个浮层：开合、焦点和播放栏的收起由 <see cref="ShellPage"/> 决定，
/// 页面自己只画当前这一首、把用户的操作转给 <see cref="MusicPlaybackViewModel"/>。
/// </summary>
public sealed partial class MusicPlayerPage : UserControl
{
    private readonly Dictionary<string, Geometry> _iconGeometry = [];
    private HostWindow? _window;
    private SurfaceMotion? _motion;
    private bool _attached;
    private bool _seeking;
    private bool _enteredFull;
    private long _lyricsPauseUntil;
    private (long Intent, long Track, long Generation) _scrubOwner;

    internal event Action? CollapseRequested;

    public MusicPlayerPage()
    {
        InitializeComponent();
        // 用户自己拨歌词时先别抢回滚动位；下一次换句时再看这 8 秒有没有过去。
        LyricsList.AddHandler(PointerWheelChangedEvent,
            new PointerEventHandler((_, _) => _lyricsPauseUntil = Environment.TickCount64 + 8000), true);
    }

    public MusicPlaybackViewModel ViewModel { get; private set; } = null!;

    internal void Attach(MusicPlaybackViewModel viewModel, HostWindow window)
    {
        if (_attached) return;
        ViewModel = viewModel;
        _window = window;
        VolumeSlider.Maximum = Momoka.Configuration.AudioSettings.MaxVolume;
        Bindings.Update();
        _attached = true;

        SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) =>
        {
            _seeking = true;
            _scrubOwner = ViewModel.BeginScrub();
        }), true);
        SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekReleased), true);
        SeekSlider.PointerCanceled += (_, _) => { _seeking = false; ViewModel.CancelScrub(); };
        SeekSlider.KeyUp += async (_, _) => { if (_attached) await ViewModel.SeekAsync(SeekSlider.Value); };
        ViewModel.PropertyChanged += OnViewModelChanged;
        ApplyLayout();
    }

    internal void Open()
    {
        // 正在收起时 Visibility 还是 Visible，但 SurfaceMotion 已经不在「显示着」那一档：
        // 这一下要把退场打断，不能因为 Visibility 还亮着就直接返回（否则落定回调会把页面收掉）。
        if (Visibility == Visibility.Visible && (_motion is null || _motion.IsShown)) return;
        Visibility = Visibility.Visible;
        _motion ??= new SurfaceMotion(this);
        _motion.Enter();
        ApplyLayout();
        UpdateFullscreenChrome();
        DispatcherQueue.TryEnqueue(() =>
        {
            CollapseButton.Focus(FocusState.Programmatic);
            ScrollToActive(force: true);
        });
    }

    internal void Close()
    {
        if (Visibility != Visibility.Visible) return;
        // 从播放页进的全屏，收起时还给窗口化；进页前本来就全屏的不动。
        if (_enteredFull && _window is { Fullscreen: true }) _window.Fullscreen = false;
        _enteredFull = false;
        _motion ??= new SurfaceMotion(this);
        _motion.Exit(landed: () => Visibility = Visibility.Collapsed);
    }

    public Geometry ShuffleGeometry => IconGeometry("ShuffleData");
    public Geometry FavoriteGeometry(bool value) => IconGeometry(value ? "HeartFilledData" : "HeartData");

    public static string FavoriteLabel(bool value) => value ? "取消收藏" : "收藏歌曲";
    public static string VolumeGlyph(bool value) => char.ConvertFromUtf32(value ? 0xE74F : 0xE767);
    public static string RepeatMenuLabel(string label) => $"循环：{label}";
    public static string QueueDuration(long? ticks) => TimeFormat.Clock(ticks);

    // Geometry不能跨控件关联；播放页只为自己的PathIcon创建并复用状态路径。
    private Geometry IconGeometry(string key)
    {
        if (_iconGeometry.TryGetValue(key, out var geometry)) return geometry;
        geometry = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), Resources[key]);
        _iconGeometry.Add(key, geometry);
        return geometry;
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ApplyLayout();
        UpdateFullscreenChrome();
    }

    /// <summary>
    /// 两栏的宽度与封面尺寸都按当前窗口算，和 Core 的 <see cref="MusicPlayerLayout"/> 同一份判据。
    /// 窄窗收起歌词，左栏居中；更窄时再收起随机与音量，控制排不被挤成两行。
    /// </summary>
    private void ApplyLayout()
    {
        if (!_attached || Root.ActualWidth <= 0) return;
        var width = Root.ActualWidth;
        var height = MusicPlayerLayout.StageHeight(Root.ActualHeight);
        var lyrics = MusicPlayerLayout.ShowsLyrics(width, height);
        var cover = MusicPlayerLayout.CoverSize(width, height, lyrics);
        CoverFrame.Width = CoverFrame.Height = cover;
        NowPlaying.Width = cover;
        if (lyrics)
        {
            LyricsHost.Width = MusicPlayerLayout.LyricsWidth(width);
            LyricsHost.Visibility = Visibility.Visible;
            Stage.ColumnDefinitions[1].Width = new GridLength(MusicPlayerLayout.Gap(width));
        }
        else
        {
            LyricsHost.Visibility = Visibility.Collapsed;
            Stage.ColumnDefinitions[1].Width = new GridLength(0);
        }

        var extras = cover >= 260;
        ShuffleButton.Visibility = extras ? Visibility.Visible : Visibility.Collapsed;
        VolumeButton.Visibility = extras ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(MusicPlaybackViewModel.ActiveLyric):
                ScrollToActive();
                break;
            case nameof(MusicPlaybackViewModel.Title):
                _lyricsPauseUntil = 0;
                DispatcherQueue.TryEnqueue(() => ScrollToActive(force: true));
                break;
            case nameof(MusicPlaybackViewModel.HasLyrics):
                if (ViewModel.HasLyrics) DispatcherQueue.TryEnqueue(() => ScrollToActive(force: true));
                break;
        }
    }

    /// <summary>
    /// 把正在唱的那一句送进视野。用户刚刚自己滚过就等一会儿再接管，免得每换一句都把人拽回来。
    /// 纯文本歌词没有「正在唱」，这里退到第一句，只保证换曲后回到开头。
    /// </summary>
    private void ScrollToActive(bool force = false)
    {
        if (Visibility != Visibility.Visible || !_attached) return;
        var line = ViewModel.ActiveLyric ?? (ViewModel.Lyrics.Count > 0 ? ViewModel.Lyrics[0] : null);
        if (line is null) return;
        if (!force && Environment.TickCount64 < _lyricsPauseUntil) return;
        try { LyricsList.ScrollIntoView(line, ScrollIntoViewAlignment.Leading); }
        catch (Exception) { /* 列表还没量过时平台会拒绝，下一次换句再试。 */ }
    }

    private void UpdateFullscreenChrome()
    {
        var fullscreen = _window?.Fullscreen == true;
        FullscreenIcon.Glyph = char.ConvertFromUtf32(fullscreen ? 0xE73F : 0xE740);
        var label = fullscreen ? "退出全屏" : "全屏显示";
        AutomationProperties.SetName(FullscreenButton, label);
        ToolTipService.SetToolTip(FullscreenButton, label);
    }

    private void OnCollapse(object sender, RoutedEventArgs args) => CollapseRequested?.Invoke();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CollapseRequested?.Invoke();
    }

    private void OnFullscreen(object sender, RoutedEventArgs args)
    {
        if (_window is null) return;
        _window.Fullscreen = !_window.Fullscreen;
        _enteredFull = _window.Fullscreen;
        UpdateFullscreenChrome();
        ApplyLayout();
    }

    private async void OnPrevious(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.PreviousAsync(); }
    private async void OnPlay(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.TogglePauseAsync(); }
    private async void OnTogglePause(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.TogglePauseAsync(); }
    private async void OnNext(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.NextAsync(); }
    private async void OnFavorite(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.ToggleFavoriteAsync(); }
    private async void OnMute(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.ToggleMuteAsync(); }
    private void OnShuffle(object sender, RoutedEventArgs args) { if (_attached) ViewModel.ToggleShuffle(); }
    private void OnRepeat(object sender, RoutedEventArgs args) { if (_attached) ViewModel.CycleRepeat(); }

    private void OnQueue(object sender, RoutedEventArgs args)
    {
        if (QueueButton.Flyout is { } flyout) flyout.ShowAt(QueueButton);
    }

    private async void OnStop(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.StopAsync(); }

    private async void OnQueueItem(object sender, ItemClickEventArgs args)
    {
        if (_attached && args.ClickedItem is EmbyItem item) await ViewModel.SelectAsync(ViewModel.Queue.IndexOf(item));
    }

    private void OnSeek(object sender, RangeBaseValueChangedEventArgs args)
    {
        // 绑定来的 Position 只是观察值；只有指针释放或键盘操作才真正提交 seek。
    }

    private async void OnSeekReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!_attached || !_seeking) return;
        _seeking = false;
        await ViewModel.EndScrubAsync(_scrubOwner, SeekSlider.Value);
    }

    private async void OnVolume(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_attached && Math.Abs(args.NewValue - ViewModel.Volume) > 0.1) await ViewModel.SetVolumeAsync(args.NewValue);
    }
}
