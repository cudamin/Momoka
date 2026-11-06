using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private CancellationTokenSource? _seamCancel;
    private (ImageSource Image, int Boundary, int Rows, int End, Windows.UI.Color Paper, byte Shade, bool Fixed, double Height, double PictureHeight)? _seamKey;
    private bool _seamLive;

    private void PrepareSeam()
    {
        BodySeam.RenderTransform = new TranslateTransform();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DetailViewModel.HeroImage) or nameof(DetailViewModel.Showcase)
                or nameof(DetailViewModel.GaussianBackdropApplied)) UpdateSeam();
        };
        Loaded += (_, _) => { _seamLive = true; UpdateSeam(); };
        Unloaded += (_, _) => StopSeam();
    }

    private void StopSeam()
    {
        _seamLive = false;
        ClearSeam();
    }

    private void ClearSeam()
    {
        _seamCancel?.Cancel();
        _seamCancel?.Dispose();
        _seamCancel = null;
        _seamKey = null;
        BodySeam.Background = null;
        BodySeam.Visibility = Visibility.Collapsed;
        BackdropSeam.Background = null;
        BackdropSeam.Visibility = Visibility.Collapsed;
        BodyPaper.Margin = new Thickness(0);
    }

    private void UpdateSeam()
    {
        if (!_seamLive || !UsesBackdropFade || ViewModel.HeroImage is not { } image
            || ViewModel.HeroPixelSize is not { } size || !ViewModel.GaussianBackdropReady
            || ViewModel.PageWidth <= 0 || XamlRoot is null)
        {
            ClearSeam();
            return;
        }
        var scale = 1 / Math.Max(ViewModel.PageWidth / size.Width, ViewModel.PictureHeight / size.Height);
        var boundaryDip = BodySheet.TransformToVisual(Backdrop).TransformPoint(default).Y;
        var boundary = (int)Math.Round(boundaryDip * scale);
        var end = (int)Math.Round(ViewModel.PictureHeight * scale);
        var height = ViewModel.PictureHeight * GaussianBackdrop.BottomShare;
        var rows = Math.Max(2, (int)Math.Round(height * scale));
        var paper = _sheetBrush.Color;
        var shade = ReadingShadeColor.A;
        if (boundary < -rows / 2)
        {
            ClearSeam();
            return;
        }
        // 正文在图下沿之后才出现时，衔接的是固定图片与页面底色；正文提前盖图时随正文接缝滚动。
        var fixedEdge = boundary >= end;
        if (fixedEdge) boundary = end;
        var key = (Image: image, Boundary: boundary, Rows: rows, End: end, Paper: paper, Shade: shade,
            Fixed: fixedEdge, Height: height, PictureHeight: ViewModel.PictureHeight);
        if (_seamKey == key)
        {
            if (key.Fixed && BackdropSeam.Visibility == Visibility.Visible)
                BodyPaper.Margin = new Thickness(0, Math.Max(0, key.PictureHeight + key.Height / 2 - boundaryDip), 0, 0);
            return;
        }
        ClearSeam();
        _seamKey = key;
        _seamCancel = new CancellationTokenSource();
        _ = PaintSeamAsync(key, height, _seamCancel.Token);
    }

    private async Task PaintSeamAsync((ImageSource Image, int Boundary, int Rows, int End, Windows.UI.Color Paper, byte Shade,
        bool Fixed, double Height, double PictureHeight) key,
        double height, CancellationToken token)
    {
        try
        {
            var picture = await ViewModel.RenderBackdropSeamAsync(key.Boundary, key.Rows,
                [key.Paper.B, key.Paper.G, key.Paper.R], key.Shade, key.End, token);
            if (token.IsCancellationRequested || !_seamLive || _seamKey != key || picture is null) return;
            var brush = new ImageBrush { ImageSource = picture, Stretch = Stretch.Fill };
            var sourceWidth = ViewModel.HeroPixelSize is { } size
                ? size.Width * Math.Max(ViewModel.PageWidth / size.Width, key.PictureHeight / size.Height)
                : ViewModel.PageWidth;
            var horizontalCrop = Math.Max(0, (sourceWidth - ViewModel.PageWidth) / 2);
            if (key.Fixed)
            {
                BackdropSeam.Width = sourceWidth;
                BackdropSeam.Height = height;
                BackdropSeam.Margin = new Thickness(-horizontalCrop, key.PictureHeight - height / 2, -horizontalCrop, 0);
                BackdropSeam.Background = brush;
                BackdropSeam.Visibility = Visibility.Visible;
                var paperTop = BodySheet.TransformToVisual(Backdrop).TransformPoint(default).Y;
                BodyPaper.Margin = new Thickness(0, Math.Max(0, key.PictureHeight + height / 2 - paperTop), 0, 0);
            }
            else
            {
                BodySeam.Width = sourceWidth;
                BodySeam.Height = height;
                BodySeam.Margin = new Thickness(-horizontalCrop, 0, -horizontalCrop, 0);
                ((TranslateTransform)BodySeam.RenderTransform).Y = -height / 2;
                BodySeam.Background = brush;
                BodySeam.Visibility = Visibility.Visible;
                BodyPaper.Margin = new Thickness(0, height / 2, 0, 0);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_seamKey == key) ClearSeam();
            Momoka.Diagnostics.Log.Debug("背景接缝", error.Message);
        }
    }
}
