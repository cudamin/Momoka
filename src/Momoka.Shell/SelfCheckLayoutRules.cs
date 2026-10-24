using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Shell;

/// <summary>Offline predicates shared by the real self-check and its malformed-reading tests.</summary>
internal static class SelfCheckLayoutRules
{
    internal static bool LibraryViews(IReadOnlyList<(string Label, string Layout, double Cell, double Row)> walk,
        bool restored, int textHeight)
    {
        var views = Enum.GetValues<LibraryView>();
        if (!restored || walk.Count != views.Length
            || textHeight != CardSize.Chrome && textHeight != CardSize.Chrome + 18) return false;
        foreach (var view in views)
        {
            var reading = walk[(int)view];
            if (reading.Label != LibraryDisplayCatalog.LabelOf(view)) return false;
            if (view is LibraryView.List or LibraryView.Table)
            {
                if (reading.Layout != "StackLayout" || reading.Cell != 0 || reading.Row != 0) return false;
                continue;
            }
            if (reading.Layout != "UniformGridLayout" || !double.IsFinite(reading.Cell)
                || !double.IsFinite(reading.Row) || reading.Cell <= 0 || reading.Cell > int.MaxValue
                || reading.Cell != Math.Round(reading.Cell)) return false;
            var frame = view is LibraryView.PosterCard or LibraryView.ThumbCard ? LibraryCardMetrics.CardFrameChrome : 0;
            var extra = reading.Row - LibraryCardMetrics.Height(view, (int)reading.Cell) - frame;
            // Expected text height is captured from the selected fields, not inferred from the first
            // reading. Row is the actual layout MinItemHeight, so a stale binding cannot self-approve.
            if (extra != textHeight) return false;
        }
        return true;
    }

    internal static bool HdrNumbers(IReadOnlyList<string> labels, bool tonemap)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "HDR 参考白亮度", "文字字幕与 OSD 亮度", "图形字幕亮度", "HDR 对比度恢复"
        };
        if (!tonemap) expected.Add("HDR 输出峰值亮度");
        return labels.Count == expected.Count && expected.SetEquals(labels);
    }

    internal static bool PanelBorder(double left, double top, double right, double bottom,
        double expectedLeft, double expectedTop, double expectedRight, double expectedBottom) =>
        double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(right) && double.IsFinite(bottom)
        && left == expectedLeft && top == expectedTop && right == expectedRight && bottom == expectedBottom;
}
