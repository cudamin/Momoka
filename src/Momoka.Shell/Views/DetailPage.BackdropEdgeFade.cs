using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.ViewModels;
using Windows.Foundation;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private const double BackdropEdgeFadeShare = 0.05;

    private void PrepareBackdropEdgeFade()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        brush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(0, 0, 0, 0) });
        brush.GradientStops.Add(new GradientStop { Offset = 1, Color = PaperColor });
        BackdropEdgeFade.Background = brush;
        BackdropThemeSource.RegisterPropertyChangedCallback(Border.BackgroundProperty, (_, _) => UpdateBackdropEdgeFade());
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DetailViewModel.PictureHeight) or nameof(DetailViewModel.HeroArtVisibility))
                UpdateBackdropEdgeFade();
        };
        Loaded += (_, _) => UpdateBackdropEdgeFade();
        UpdateBackdropEdgeFade();
    }

    private void UpdateBackdropEdgeFade()
    {
        var pictureHeight = Math.Max(0, ViewModel.PictureHeight);
        var height = pictureHeight * BackdropEdgeFadeShare;
        BackdropEdgeFade.Height = height;
        // 和图片同属固定背景层，覆盖图片最后5%；正文滚动不会带走渐变。
        BackdropEdgeFade.Margin = new Thickness(0, pictureHeight - height, 0, 0);
        var color = BackdropThemeSource.Background is SolidColorBrush theme ? theme.Color : PaperColor;
        var brush = (LinearGradientBrush)BackdropEdgeFade.Background;
        brush.GradientStops[0].Color = Color.FromArgb(0, color.R, color.G, color.B);
        brush.GradientStops[1].Color = color;
        BackdropEdgeFade.Visibility = ViewModel.BackdropShown && height > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
