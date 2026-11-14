using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class PlayerPage
{
    private Momoka.Danmaku.DanmakuHeat? _paintedHeat;
    private double _paintedHeatWidth = double.NaN;
    private double _paintedHeatHeight = double.NaN;
    private bool _paintedHeatShown;

    /// <summary>
    /// 把当前弹幕热力画进进度条：每个桶一根底对齐的细柱，桶宽约 2 DIP，最多 600 桶。
    /// 只在快照、轨道尺寸或显隐真的变化时重画；拖动和每帧状态推送都不碰它。
    /// </summary>
    private void RenderDanmakuHeat()
    {
        if (!Attached) return;
        var heat = ViewModel.DanmakuHeatmap;
        var width = SeekTrack.ActualWidth;
        var height = SeekTrack.ActualHeight;
        var visible = ViewModel.DanmakuHeatVisible && !heat.IsEmpty && width > 1 && height > 0;
        if (!visible)
        {
            DanmakuHeat.Visibility = Visibility.Collapsed;
            TrimChildren(DanmakuHeat, 0);
            _paintedHeat = null;
            _paintedHeatShown = false;
            return;
        }
        if (_paintedHeatShown && ReferenceEquals(_paintedHeat, heat)
            && _paintedHeatWidth == width && _paintedHeatHeight == height)
        {
            DanmakuHeat.Visibility = Visibility.Visible;
            return;
        }

        _paintedHeat = heat;
        _paintedHeatWidth = width;
        _paintedHeatHeight = height;
        _paintedHeatShown = true;
        DanmakuHeat.Visibility = Visibility.Visible;

        var buckets = Math.Clamp((int)Math.Round(width / 2), 120, 600);
        var levels = heat.Levels(buckets);
        if (levels.Length == 0) return;
        var brush = (Brush)Resources["PlayerDanmakuHeatBrush"];
        var slot = width / levels.Length;
        var ceiling = Math.Max(1, height * 0.6);
        var drawn = 0;
        for (var index = 0; index < levels.Length; index++)
        {
            var level = levels[index];
            if (level <= 0.001) continue;
            var bar = GetBorder(DanmakuHeat, drawn++);
            bar.Width = Math.Max(1, slot);
            bar.Height = Math.Max(1, level * ceiling);
            bar.Background = brush;
            Canvas.SetLeft(bar, index * slot);
            Canvas.SetTop(bar, height - bar.Height);
        }
        TrimChildren(DanmakuHeat, drawn);
    }
}
