using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.Views
{
    internal sealed partial class BannerReturnFixture
    {
        private readonly List<BannerSlide> _slides = [new("return")];
        private int _index;
        private bool _second;
        private bool _everLoaded;
        private bool _restoringEntry;
        private bool _paintingEntry;
        private readonly Layer LayerA = new();
        private readonly Layer LayerB = new();
        private readonly Layer LogoImage = new();
        private readonly Layer LogoRow = new();
        private readonly TranslateTransform LogoShift = new();
        private static readonly TimeSpan CrossFade = TimeSpan.FromMilliseconds(650);
        private static readonly TimeSpan Lift = TimeSpan.FromMilliseconds(600);
        private const double TextDrop = 22;
        internal int Rises { get; private set; }
        internal int ImageFades { get; private set; }
        internal int LogoFades { get; private set; }
        internal Action? DuringShow { get; set; }
        internal double PictureOpacity => FrontLayer.Opacity;
        internal double LogoOpacity => LogoRow.Opacity;
        private Layer FrontLayer => _second ? LayerB : LayerA;
        private object? FrontPicture => Brush(FrontLayer).ImageSource;
        internal void Loaded() => PaintVisit();
        internal void RestoreImage() => Paint(new BitmapImage());
        internal void RestoreLogo()
        {
            _slides[0].Logo = new BitmapImage();
            PaintLogo(_slides[0]);
        }
        internal void Advance()
        {
            _restoringEntry = false;
            Show(0);
        }
        private void Show(int index, bool animate = true)
        {
            _index = index;
            if (animate) Rises++;
            DuringShow?.Invoke();
        }
        private void PaintBackdropShade() { }
        private void PaintOpenButtonInk() { }
        private void StopMotion(string channel) { }
        private void Play(Storyboard board, Action settle, string channel)
        {
            if (channel == "image") ImageFades++;
            if (channel == "logo") LogoFades++;
        }
        private static DoubleAnimation FadeTo(DependencyObject target, double to, TimeSpan span, TimeSpan delay) => new();
        private static DoubleAnimation SlideTo(TranslateTransform target, double to, TimeSpan span, TimeSpan delay) => new();
        private static Layer Brush(Layer layer) => layer;
        private sealed class Layer : FrameworkElement
        {
            internal object? ImageSource { get; set; }
            internal object? Source { get; set; }
        }
    }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage;
}
