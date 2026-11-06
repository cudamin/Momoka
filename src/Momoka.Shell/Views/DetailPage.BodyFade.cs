using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.ViewModels;
using Windows.Foundation;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private const double BodyFadeShare = 0.05;

    private void PrepareBodyFade()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        brush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(0, 0, 0, 0) });
        brush.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(255, 0, 0, 0) });
        BodyEdgeFade.Background = brush;
        BodyEdgeFade.RenderTransform = new TranslateTransform();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(DetailViewModel.PictureHeight) or nameof(DetailViewModel.HeroArtVisibility))
                UpdateBodyFade();
        };
        Loaded += (_, _) => UpdateBodyFade();
    }

    private void UpdateBodyFade()
    {
        var height = Math.Max(0, ViewModel.PictureHeight * BodyFadeShare);
        BodyEdgeFade.Height = height;
        BodyEdgeFade.Margin = new Thickness(0, 0, 0, -height);
        // 载体属于正文行，末端固定贴住界面上沿；滚动、沉浸翻面都随正文走，不采样图片底边。
        ((TranslateTransform)BodyEdgeFade.RenderTransform).Y = -height;
        BodyEdgeFade.Visibility = ViewModel.BackdropShown && height > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
