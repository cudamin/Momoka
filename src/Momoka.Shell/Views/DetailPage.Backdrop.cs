using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Theming;
using Windows.Foundation;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private readonly LinearGradientBrush _backdropFadeBrush = new()
    {
        StartPoint = new Point(0.5, 0),
        EndPoint = new Point(0.5, 1)
    };

    // 2026-11-06：底部六分之一；遮罩与渐变取较大alpha，不能先遮一次再叠渐变。
    private bool UsesBackdropFade => ViewModel.BackdropShown && !ViewModel.Showcase
        && ViewModel.ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season;

    // 整张图上的阅读遮罩。老版固定 0xB8；2026-11-06 起浓度读设置里那一档（0 不遮、100 完全不透明），
    // 最浓档就是 ScrimCeiling 那一格 —— 见 AppearanceOpacity.ScrimAlpha。高斯模糊那一档不画它（模糊自己压细节）。
    private Color ReadingShadeColor => ViewModel.GaussianBackdropApplied
        ? Color.FromArgb(0, 0, 0, 0)
        : Color.FromArgb(
            AppearanceOpacity.ScrimAlpha(DetailHero.ScrimCeiling, ViewModel.BackdropStrength),
            DetailHero.ScrimInk.R, DetailHero.ScrimInk.G, DetailHero.ScrimInk.B);

    private void PaintBackdropFade()
    {
        var color = BackdropThemeSource.Background is SolidColorBrush theme ? theme.Color : PaperColor;
        var reading = ReadingShadeColor;
        var shade = new ThemeColor(reading.A, reading.R, reading.G, reading.B);
        var paper = new ThemeColor(color.A, color.R, color.G, color.B);
        _backdropFadeBrush.GradientStops.Clear();
        // alpha在渐变超过遮罩浓度时才继续增加；加上该交点保证浓度不会再次叠加。
        var offsets = new[] { 0d, DetailBackdrop.FadeStart + reading.A / 255d / 6 }
            .Concat(Enumerable.Range(0, 17).Select(step => DetailBackdrop.FadeStart + step / 96d))
            .Distinct().Order();
        foreach (var offset in offsets)
        {
            var stop = DetailBackdrop.Shade(shade, paper, offset);
            _backdropFadeBrush.GradientStops.Add(new GradientStop
            {
                Offset = offset,
                Color = Color.FromArgb(stop.A, stop.R, stop.G, stop.B)
            });
        }
        BackdropFade.Background = _backdropFadeBrush;
        BackdropFade.Visibility = UsesBackdropFade ? Visibility.Visible : Visibility.Collapsed;
    }
}
