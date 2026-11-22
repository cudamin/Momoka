using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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

    // 构图参考呀哈哈封面工坊 preset_templates.py 的静态模板，4943fb3a5bd00cc0e8b1b6f79f84bf2f1922ce41。
    // 原生绘图参考 Gallery 6de91feccadfbb0a796b71caedead5a2284f730e 的 Samples/Image (UniformToFill)、
    // Viewbox (整组缩放)、ImplicitTransition (旋转/渐变)。没有对应库封面组合；这里独立组合原生元素。
    // WinUI Clip 仅支持 RectangleGeometry，海报墙出框部分靠矩形 Clip 裁切。
    private UIElement? LibraryCover(string? preset, IReadOnlyList<BitmapImage>? pictures,
        BitmapImage? poster, BitmapImage? libraryPoster, string title, string identity)
    {
        // 预设覆盖封面不保留外框浅色描边，避免顶角露出白色短线；关闭时恢复样式。
        if (preset is null) Art.ClearValue(Border.BorderThicknessProperty);
        else Art.BorderThickness = new Thickness(0);
        if (preset is null) return null;
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
        canvas.Background = preset == HomeLibraryCoverPresets.Stack && !highContrast
            ? StackGradient(start, end, palette.Accent) : Gradient(start, end);
        IReadOnlyList<BitmapImage> images = pictures is { Count: > 0 } ? pictures
            : poster is not null ? [poster] : libraryPoster is not null ? [libraryPoster] : [];
        var textOnly = highContrast || preset == HomeLibraryCoverPresets.Gradient || images.Count == 0;
        if (!textOnly)
        {
            switch (preset)
            {
                case HomeLibraryCoverPresets.Stack:
                    // 悬浮扇形：后卡向左右展开并降低不透明度，前卡最后以直立姿态覆盖。
                    AddStackCard(images[0], 108, 22, 56, 82, -14, 0.52);
                    AddStackCard(images[0], 145, 21, 56, 82, 14, 0.68);
                    AddStackCard(images[0], 126, 13, 62, 92);
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
        if (preset == HomeLibraryCoverPresets.Stack && !textOnly)
        {
            var accent = new Border
            {
                Width = 20,
                Height = 3,
                CornerRadius = new(1.5),
                Background = new SolidColorBrush(Color(palette.Accent))
            };
            Canvas.SetLeft(accent, 12);
            Canvas.SetTop(accent, 18);
            canvas.Children.Add(accent);
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
        Canvas.SetTop(text, preset == HomeLibraryCoverPresets.Stack && !textOnly ? 26 : 21);
        canvas.Children.Add(text);
        return new Viewbox { Stretch = Stretch.Fill, Child = canvas };

        void AddStackCard(BitmapImage source, double x, double y, double width, double height,
            double rotation = 0, double opacity = 1)
        {
            var card = new Grid
            {
                Width = width,
                Height = height,
                Opacity = opacity,
                RenderTransformOrigin = new(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = rotation }
            };
            // 小尺寸下用两层低透明圆角偏移形成柔影，不为静态封面引入 Composition 生命周期。
            card.Children.Add(new Border
            {
                Margin = new Thickness(-3, 5, -3, -1),
                CornerRadius = new(10),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(22, 0, 0, 0))
            });
            card.Children.Add(new Border
            {
                Margin = new Thickness(-1, 3, -1, 1),
                CornerRadius = new(9),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(42, 0, 0, 0))
            });
            card.Children.Add(new Border
            {
                CornerRadius = new(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(36, 255, 255, 255)),
                Child = new Image { Source = source, Stretch = Stretch.UniformToFill }
            });
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            canvas.Children.Add(card);
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
                RenderTransform = new RotateTransform { Angle = rotation },
                Child = new Image { Source = source, Stretch = Stretch.UniformToFill }
            };
            Canvas.SetLeft(frame, x);
            Canvas.SetTop(frame, y);
            canvas.Children.Add(frame);
        }
    }

    private static Windows.UI.Color Color(ThemeColor color) => Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

    private static LinearGradientBrush StackGradient(ThemeColor start, ThemeColor end, ThemeColor accent) => new()
    {
        StartPoint = new(0, 0),
        EndPoint = new(1, 1),
        GradientStops = [new GradientStop { Offset = 0, Color = Color(start) },
            new GradientStop { Offset = 0.58, Color = Color(end) },
            new GradientStop { Offset = 1, Color = Color(end.Mix(accent, 0.38)) }]
    };

    private static LinearGradientBrush Gradient(ThemeColor start, ThemeColor end) => new()
    {
        StartPoint = new(0, 0),
        EndPoint = new(1, 1),
        GradientStops = [new GradientStop { Offset = 0, Color = Color(start) },
            new GradientStop { Offset = 1, Color = Color(end) }]
    };
}
