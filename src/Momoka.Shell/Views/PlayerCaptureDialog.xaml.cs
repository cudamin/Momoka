using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Diagnostics;
using Momoka.Playback;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>A crop of an already saved bitmap; the dialog never reads a film or player property.</summary>
public sealed partial class PlayerCaptureDialog : ContentDialog
{
    private readonly int _width;
    private readonly int _height;
    private readonly Func<PlayerImageCrop, Task<string?>> _save;
    private bool _syncing;
    private bool _saving;
    private bool _saved;
    private bool _revoked;
    private Point? _dragStart;
    private PlayerImageCrop _dragOriginal;

    public PlayerCaptureDialog(BitmapImage picture, bool subtitleRegion, Func<PlayerImageCrop, Task<string?>> save)
    {
        InitializeComponent();
        _width = picture.PixelWidth;
        _height = picture.PixelHeight;
        if (_width <= 0 || _height <= 0) throw new ArgumentException("截图没有有效的像素尺寸。", nameof(picture));
        _save = save;
        CaptureImage.Source = picture;
        Title = subtitleRegion ? "截取字幕区域" : "裁切截图";
        PrimaryButtonText = subtitleRegion ? "保存并加入拼图" : "保存裁切";
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        CaptureX.Maximum = _width - 1;
        CaptureY.Maximum = _height - 1;
        CaptureWidth.Maximum = _width;
        CaptureHeight.Maximum = _height;
        CaptureDimensions.Text = $"原始图片：{_width} × {_height} 像素";
        SetCrop(PlayerCaptureGeometry.Default(_width, _height, subtitleRegion));
        CaptureError.Closed += (_, _) => CaptureError.Visibility = Visibility.Collapsed;
    }

    public string? SavedPath { get; private set; }
    internal PlayerImageCrop? Crop => PlayerCaptureGeometry.Clamp(_width, _height,
        CaptureX.Value, CaptureY.Value, CaptureWidth.Value, CaptureHeight.Value);

    internal void Revoke()
    {
        _revoked = true;
        if (!_saving) Hide();
    }

    private void SetCrop(PlayerImageCrop crop)
    {
        _syncing = true;
        try
        {
            CaptureX.Value = crop.X;
            CaptureY.Value = crop.Y;
            CaptureWidth.Value = crop.Width;
            CaptureHeight.Value = crop.Height;
        }
        finally { _syncing = false; }
        RenderSelection();
    }

    private void OnCropValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || _width <= 0 || _height <= 0) return;
        if (Crop is { } crop) SetCrop(crop);
        else IsPrimaryButtonEnabled = false;
    }

    private void RenderSelection()
    {
        IsPrimaryButtonEnabled = !_saving && Crop is not null;
        if (Crop is not { } crop) return;
        var scale = PlayerCaptureGeometry.Scale(_width, _height, CapturePreview.ActualWidth, CapturePreview.ActualHeight);
        if (!double.IsFinite(scale) || scale <= 0) return;
        Canvas.SetLeft(CaptureSelection, (CapturePreview.ActualWidth - _width * scale) / 2 + crop.X * scale);
        Canvas.SetTop(CaptureSelection, (CapturePreview.ActualHeight - _height * scale) / 2 + crop.Y * scale);
        CaptureSelection.Width = crop.Width * scale;
        CaptureSelection.Height = crop.Height * scale;
    }

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) => RenderSelection();
    private void OnWholeImage(object sender, RoutedEventArgs e) => SetCrop(PlayerCaptureGeometry.Default(_width, _height, false));
    private void OnSubtitleRegion(object sender, RoutedEventArgs e) => SetCrop(PlayerCaptureGeometry.Default(_width, _height, true));

    private void OnPreviewPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_saving || !e.GetCurrentPoint(CapturePreview).Properties.IsLeftButtonPressed || Crop is not { } crop) return;
        if (!CapturePreview.CapturePointer(e.Pointer)) return;
        _dragOriginal = crop;
        _dragStart = e.GetCurrentPoint(CapturePreview).Position;
        e.Handled = true;
    }

    private void OnPreviewMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var end = e.GetCurrentPoint(CapturePreview).Position;
        if (PlayerCaptureGeometry.FromViewport(_width, _height, CapturePreview.ActualWidth, CapturePreview.ActualHeight,
            start.X, start.Y, end.X, end.Y) is { } crop) SetCrop(crop);
        e.Handled = true;
    }

    private void OnPreviewReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is null) return;
        OnPreviewMoved(sender, e);
        _dragStart = null;
        CapturePreview.ReleasePointerCaptures();
        e.Handled = true;
    }

    private void OnPreviewCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is null) return;
        _dragStart = null;
        SetCrop(_dragOriginal);
        CapturePreview.ReleasePointerCaptures();
    }

    private async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try { await SaveAsync().ConfigureAwait(true); }
        finally { deferral.Complete(); }
        if (_saved || _revoked) Hide();
    }

    internal async Task SaveAsync()
    {
        if (_saving || Crop is not { } crop) return;
        _saving = true;
        CaptureSaving.Visibility = Visibility.Visible;
        CaptureError.IsOpen = false;
        CaptureError.Visibility = Visibility.Collapsed;
        CaptureBody.IsHitTestVisible = false;
        RenderSelection();
        try
        {
            var result = await _save(crop).ConfigureAwait(true);
            if (result is null) throw new PlayerToolException("当前播放已切换，请重新截图。");
            SavedPath = result;
            _saved = true;
        }
        catch (Exception error)
        {
            CaptureError.Message = Failure.Describe(error);
            CaptureError.Visibility = Visibility.Visible;
            CaptureError.IsOpen = true;
        }
        finally
        {
            _saving = false;
            CaptureSaving.Visibility = Visibility.Collapsed;
            CaptureBody.IsHitTestVisible = true;
            RenderSelection();
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_saving) args.Cancel = true;
        else
        {
            _dragStart = null;
            CapturePreview.ReleasePointerCaptures();
        }
    }
}
