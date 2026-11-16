using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MusicPlayerBar : UserControl
{
    public MusicPlaybackViewModel ViewModel { get; private set; } = null!;
    private readonly Dictionary<string, Geometry> _iconGeometry = [];
    private bool _attached;
    private bool _seeking;
    private (long Intent, long Track, long Generation) _scrubOwner;
    public MusicPlayerBar() => InitializeComponent();
    internal void Attach(MusicPlaybackViewModel viewModel)
    {
        ViewModel = viewModel;
        VolumeSlider.Maximum = Momoka.Configuration.AudioSettings.MaxVolume;
        Bindings.Update();
        _attached = true;
        SeekSlider.AddHandler(PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        {
            _seeking = true;
            _scrubOwner = ViewModel.BeginScrub();
        }), true);
        SeekSlider.AddHandler(PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSeekReleased), true);
        SeekSlider.PointerCanceled += (_, _) => { _seeking = false; ViewModel.CancelScrub(); };
        SeekSlider.KeyUp += async (_, _) => { if (_attached) await ViewModel.SeekAsync(SeekSlider.Value); };
    }
    public static string FavoriteLabel(bool value) => value ? "取消收藏" : "收藏歌曲";
    /// <summary>主页底部那一条是空底：栏只留控件，不铺胶囊；压在封面上的页面仍用玻璃。</summary>
    internal void ShowSurface(bool show) => SurfaceGlass.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    public Geometry ShuffleGeometry => IconGeometry("ShuffleData");
    public Geometry FavoriteGeometry(bool value) => IconGeometry(value ? "HeartFilledData" : "HeartData");
    public Geometry RepeatGeometry(string label) => IconGeometry(label == "单曲循环" ? "RepeatOneData" : "RepeatData");
    // Geometry不能跨控件关联；每个播放栏只为自己的PathIcon创建并复用状态路径。
    private Geometry IconGeometry(string key)
    {
        if (_iconGeometry.TryGetValue(key, out var geometry)) return geometry;
        geometry = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), Resources[key]);
        _iconGeometry.Add(key, geometry);
        return geometry;
    }
    public static string VolumeGlyph(bool value) => char.ConvertFromUtf32(value ? 0xE74F : 0xE767);
    private async void OnPrevious(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.PreviousAsync(); }
    private async void OnPause(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.TogglePauseAsync(); }
    private async void OnStop(object sender, RoutedEventArgs args) { if (_attached) { QueueFlyout.Hide(); await ViewModel.StopAsync(); } }
    private async void OnNext(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.NextAsync(); }
    private async void OnFavorite(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.ToggleFavoriteAsync(); }
    private async void OnMute(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.ToggleMuteAsync(); }
    private void OnShuffle(object sender, RoutedEventArgs args) { if (_attached) ViewModel.ToggleShuffle(); }
    private void OnRepeat(object sender, RoutedEventArgs args) { if (_attached) ViewModel.CycleRepeat(); }
    private void OnSeek(object sender, RangeBaseValueChangedEventArgs args)
    {
        // The bound position is observation. Only actual pointer release or keyboard input commits a seek.
    }
    private async void OnSeekReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!_attached || !_seeking) return;
        _seeking = false;
        await ViewModel.EndScrubAsync(_scrubOwner, SeekSlider.Value);
    }
    private async void OnVolume(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_attached && Math.Abs(args.NewValue - ViewModel.Volume) > 0.1) await ViewModel.SetVolumeAsync(args.NewValue);
    }
    private async void OnQueueItem(object sender, ItemClickEventArgs args)
    {
        if (_attached && args.ClickedItem is EmbyItem item) await ViewModel.SelectAsync(ViewModel.Queue.IndexOf(item));
    }
    private void OnTrackInfoSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // 左对齐的Slider需要明确宽度，否则平台自然测量只剩一个Thumb。
        SeekSlider.Width = Math.Min(360, args.NewSize.Width);
    }
    private void OnBarSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (Surface is null) return;
        // 胶囊按内容收紧，断点仍由外层可用宽度决定；恢复宽窗时轨道重新争取360。
        if (args.PreviousSize.Width != args.NewSize.Width) SeekSlider.Width = 360;
        var compact = args.NewSize.Width < 760;
        var narrow = args.NewSize.Width < 480;
        ShuffleButton.Visibility = RepeatButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CoverFrame.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        ClockPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BarGrid.ColumnSpacing = narrow ? 4 : 12;
        // 第二行只在窄窗用。宽窗它空着，间距却照算，整排键因此被顶到胶囊中线之上 4 —— 空行不该留间距。
        BarGrid.RowSpacing = narrow ? 8 : 0;
        BarGrid.Margin = new Thickness(narrow ? 6 : 12, 8, narrow ? 6 : 12, 8);
        // 正常窗宽两端保持半圆；两行窄窗用24圆角，避免切掉边缘按钮。
        Surface.CornerRadius = new CornerRadius(narrow ? 24 : args.NewSize.Height / 2);
        SurfaceGlass.CornerRadius = Surface.CornerRadius;
        Grid.SetColumn(TrackInfo, narrow ? 0 : 2);
        Grid.SetColumnSpan(TrackInfo, narrow ? 4 : 1);
        Grid.SetRow(TrackInfo, 0);
        Grid.SetRow(Transport, narrow ? 1 : 0);
        Grid.SetColumnSpan(Transport, narrow ? 2 : 1);
        Grid.SetRow(Extras, narrow ? 1 : 0);
    }
}
