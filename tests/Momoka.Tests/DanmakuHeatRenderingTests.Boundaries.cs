#if DANMAKU_HEAT_RENDERING_FIXTURE
using Momoka.Danmaku;

// Values and child collection endpoints only. PlayerPage.DanmakuHeat.cs is compile-linked unchanged.
namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
}

namespace Microsoft.UI.Xaml.Media
{
    public class Brush;
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class Canvas
    {
        public List<Border> Children { get; } = [];
        public Microsoft.UI.Xaml.Visibility Visibility { get; set; }
        public static void SetLeft(Border border, double value) => border.Left = value;
        public static void SetTop(Border border, double value) => border.Top = value;
    }

    public sealed class Border
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public Microsoft.UI.Xaml.Media.Brush? Background { get; set; }
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class PlayerPage
    {
        internal bool Attached { get; set; } = true;
        internal HeatFixtureViewModel ViewModel { get; } = new();
        internal HeatFixtureTrack SeekTrack { get; } = new();
        internal Microsoft.UI.Xaml.Controls.Canvas DanmakuHeat { get; } = new();
        internal Dictionary<string, object> Resources { get; } = new()
        {
            ["PlayerDanmakuHeatBrush"] = new Microsoft.UI.Xaml.Media.Brush()
        };
        internal void PaintHeatForFixture() => RenderDanmakuHeat();
        internal int Allocated { get; private set; }

        private Microsoft.UI.Xaml.Controls.Border GetBorder(Microsoft.UI.Xaml.Controls.Canvas canvas, int index)
        {
            if (index < canvas.Children.Count) return canvas.Children[index];
            Allocated++;
            var child = new Microsoft.UI.Xaml.Controls.Border();
            canvas.Children.Add(child);
            return child;
        }

        private static void TrimChildren(Microsoft.UI.Xaml.Controls.Canvas canvas, int count)
        {
            while (canvas.Children.Count > count) canvas.Children.RemoveAt(canvas.Children.Count - 1);
        }
    }

    internal sealed class HeatFixtureViewModel
    {
        internal DanmakuHeat DanmakuHeatmap { get; set; } = DanmakuHeat.Empty;
        internal bool DanmakuHeatVisible { get; set; } = true;
    }

    internal sealed class HeatFixtureTrack
    {
        internal double ActualWidth { get; set; } = 600;
        internal double ActualHeight { get; set; } = 31;
    }
}
#endif
