using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Theming;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

public sealed partial class PosterCard
{
    private void OnLibraryCoverThemeChanged(FrameworkElement sender, object args)
    {
        if (Card is { } card)
            LibraryCoverHost.Content = LibraryCover(card.HomeLibraryCoverPreset, card.HomeLibraryCoverImages,
                card.Poster, card.LibraryTile0, card.Title, card.Item.IdentityKey);
    }

    // 构图参考呀哈哈封面工坊 preset_templates.py static_1–4，4943fb3a5bd00cc0e8b1b6f79f84bf2f1922ce41。
    // 原生绘图参考 Gallery 6de91feccadfbb0a796b71caedead5a2284f730e 的 Samples/Image (UniformToFill)、
    // Viewbox (整组缩放)、ImplicitTransition (旋转/渐变)。没有对应库封面组合；这里独立组合原生元素。
    // WinUI Clip 仅支持 RectangleGeometry，斜切用同坐标渐变 Polygon 遮盖，不用不受支持的路径 Clip。
    private UIElement? LibraryCover(string? preset, IReadOnlyList<BitmapImage>? pictures,
        BitmapImage? poster, BitmapImage? libraryPoster, string title, string identity)
    {
        // 预设覆盖封面不保留外框浅色描边，避免顶角露出白色短线；关闭时恢复样式。
        if (preset is null) Art.ClearValue(Border.BorderThicknessProperty);
        else Art.BorderThickness = new Thickness(0);
        if (preset is null) return null;
        var motion = new Storyboard();
        var canvas = new Canvas { Width = 208, Height = 117, Clip = new RectangleGeometry { Rect = new Rect(0, 0, 208, 117) } };
        var palette = UiThemes.All[HomeLibraryCoverArtwork.PaletteIndex(identity)].Colors;
        var start = palette.Window;
        var end = palette.SurfaceAlt.Mix(palette.Accent, 0.28);
        var ink = palette.Text;
        var highContrast = new AccessibilitySettings().HighContrast;
        if (highContrast)
        {
            var colors = new UISettings();
            var background = colors.GetColorValue(UIColorType.Background);
            var foreground = colors.GetColorValue(UIColorType.Foreground);
            start = end = new(background.A, background.R, background.G, background.B);
            ink = new(foreground.A, foreground.R, foreground.G, foreground.B);
        }
        canvas.Background = Gradient(start, end);
        IReadOnlyList<BitmapImage> images = pictures is { Count: > 0 } ? pictures
            : poster is not null ? [poster] : libraryPoster is not null ? [libraryPoster] : [];
        var textOnly = highContrast || preset == HomeLibraryCoverPresets.Gradient || images.Count == 0;
        if (!textOnly)
        {
            switch (preset)
            {
                case HomeLibraryCoverPresets.Stack:
                    AddImage(images[0], 109, 18, 82, 82, 36, 0.56, 10);
                    AddImage(images[0], 109, 18, 82, 82, 18, 0.74, 10);
                    AddImage(images[0], 109, 18, 82, 82, 0, 1, 10);
                    break;
                case HomeLibraryCoverPresets.Diagonal:
                    AddImage(images[0], -2, -2, 212, 121);
                    canvas.Children.Add(new Polygon
                    {
                        Points = new PointCollection { new(0, 0), new(114.4, 0), new(83.2, 117), new(0, 117) },
                        Fill = Gradient(start, end, absolute: true)
                    });
                    break;
                case HomeLibraryCoverPresets.Wall:
                    // 原模板九张海报右侧出框，少图稳定循环补足；同一张 BitmapImage 可由多个 Image 共用。
                    int[] order = [2, 0, 4, 3, 1, 5, 8, 7, 6];
                    for (var row = 0; row < 3; row++)
                        for (var column = 0; column < 3; column++)
                            AddImage(images[order[row * 3 + column] % images.Count],
                                106 + column * 50.5 - row * 18.9, -36 + row * 67.2,
                                44.5, 66.2, 15.8, 1, 3);
                    // 左侧文字区保持足够对比，右端渐隐到海报墙，底色与整张封面同源。
                    var shade = new Rectangle
                    {
                        Width = 108,
                        Height = 117,
                        Fill = new LinearGradientBrush
                        {
                            StartPoint = new(0, 0),
                            EndPoint = new(1, 0),
                            GradientStops = [new GradientStop { Offset = 0, Color = Color(start) },
                            new GradientStop { Offset = 0.76, Color = Color(start) },
                            new GradientStop { Offset = 1, Color = Color(start.WithAlpha(0)) }]
                        }
                    };
                    canvas.Children.Add(shade);
                    break;
            }
        }
        var heading = new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["EgDataStrongStyle"],
            Foreground = new SolidColorBrush(Color(ink)),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = textOnly ? 172 : 84,
            MaxLines = 2,
            TextAlignment = textOnly ? TextAlignment.Center : TextAlignment.Left
        };
        var text = new Grid { Width = textOnly ? 172 : 84, Height = 75 };
        heading.VerticalAlignment = VerticalAlignment.Center;
        text.Children.Add(heading);
        Canvas.SetLeft(text, textOnly ? 18 : 10);
        Canvas.SetTop(text, 21);
        canvas.Children.Add(text);
        var cover = new Viewbox { Stretch = Stretch.Fill, Child = canvas };
        if (motion.Children.Count > 0) _ = new LibraryCoverMotion(cover, motion);
        return cover;

        void Animate(CompositeTransform target, string property, double from, double to, double seconds)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromSeconds(seconds)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            motion.Children.Add(animation);
        }

        void AddImage(BitmapImage source, double x, double y, double width, double height,
            double rotation = 0, double opacity = 1, double corner = 0)
        {
            var frame = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new(corner),
                Opacity = opacity,
                RenderTransformOrigin = new(0.5, 0.5),
                RenderTransform = new CompositeTransform { Rotation = rotation },
                Child = new Image { Source = source, Stretch = Stretch.UniformToFill }
            };
            Canvas.SetLeft(frame, x);
            Canvas.SetTop(frame, y);
            canvas.Children.Add(frame);
            var transform = (CompositeTransform)frame.RenderTransform;
            switch (preset)
            {
                case HomeLibraryCoverPresets.Stack:
                    Animate(transform, "Rotation", rotation, rotation + (rotation == 18 ? -6 : 6), 4.5);
                    break;
                case HomeLibraryCoverPresets.Diagonal:
                    Animate(transform, "ScaleX", 1, 1.1, 7);
                    Animate(transform, "ScaleY", 1, 1.1, 7);
                    Animate(transform, "TranslateX", 0, -8, 7);
                    break;
                case HomeLibraryCoverPresets.Wall:
                    Animate(transform, "TranslateY", 0, x > 140 ? -9 : 9, 5.5);
                    break;
            }
        }
    }

    private static Windows.UI.Color Color(ThemeColor color) => Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

    private static LinearGradientBrush Gradient(ThemeColor start, ThemeColor end, bool absolute = false) => new()
    {
        StartPoint = new(0, 0),
        EndPoint = absolute ? new(208, 117) : new(1, 1),
        MappingMode = absolute ? BrushMappingMode.Absolute : BrushMappingMode.RelativeToBoundingBox,
        GradientStops = [new GradientStop { Offset = 0, Color = Color(start) },
            new GradientStop { Offset = 1, Color = Color(end) }]
    };
}
