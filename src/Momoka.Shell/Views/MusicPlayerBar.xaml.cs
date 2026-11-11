using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MusicPlayerBar : UserControl
{
    public MusicPlaybackViewModel ViewModel { get; private set; } = null!;
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
    public static string FavoriteGlyph(bool value) => char.ConvertFromUtf32(value ? 0xEB52 : 0xEB51);
    public static string VolumeGlyph(bool value) => char.ConvertFromUtf32(value ? 0xE74F : 0xE767);
    private async void OnPrevious(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.PreviousAsync(); }
    private async void OnPause(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.TogglePauseAsync(); }
    private async void OnStop(object sender, RoutedEventArgs args) { if (_attached) { QueueFlyout.Hide(); await ViewModel.StopAsync(); } }
    private async void OnNext(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.NextAsync(); }
    private async void OnFavorite(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.ToggleFavoriteAsync(); }
    private async void OnMute(object sender, RoutedEventArgs args) { if (_attached) await ViewModel.ToggleMuteAsync(); }
    private void OnShuffle(object sender, RoutedEventArgs args) { if (_attached) { ViewModel.ToggleShuffle(); ShuffleButton.Opacity = ViewModel.Shuffle ? 1 : 0.55; } }
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
    private void OnBarSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (Surface is null) return;
        var compact = args.NewSize.Width < 760;
        var narrow = args.NewSize.Width < 480;
        ShuffleButton.Visibility = RepeatButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CoverFrame.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        ClockPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BarGrid.ColumnSpacing = narrow ? 4 : 12;
        Surface.Padding = new Thickness(narrow ? 6 : 12, 8, narrow ? 6 : 12, 8);
        // 胶囊的两端必须是半圆：圆角取实测高度的一半，字号行高怎么量都不破形；
        // 窄窗两行排布时收回到固定圆角，免得半圆端啃掉左缘的传输键。
        Surface.CornerRadius = new CornerRadius(narrow ? 24 : args.NewSize.Height / 2);
        Grid.SetColumn(TrackInfo, narrow ? 0 : 2);
        Grid.SetColumnSpan(TrackInfo, narrow ? 4 : 1);
        Grid.SetRow(TrackInfo, 0);
        Grid.SetRow(Transport, narrow ? 1 : 0);
        Grid.SetColumnSpan(Transport, narrow ? 2 : 1);
        Grid.SetRow(Extras, narrow ? 1 : 0);
    }
}
