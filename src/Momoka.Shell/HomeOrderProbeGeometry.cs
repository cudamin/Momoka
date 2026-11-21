namespace Momoka.Shell;

/// <summary>主页排序表按内容自动增高；用当前有效容器的首尾和间距检查隐藏项是否留下空白。</summary>
internal static class HomeOrderProbeGeometry
{
    internal static bool Fits(int count, double height, double topInset, double bottomInset,
        IReadOnlyList<(double Top, double Height)> rows)
    {
        if (count <= 0 || rows.Count != count || !double.IsFinite(height) || height <= 0
            || !double.IsFinite(topInset) || topInset < 0 || !double.IsFinite(bottomInset) || bottomInset < 0) return false;

        var bottom = topInset;
        foreach (var row in rows)
        {
            if (!double.IsFinite(row.Top) || !double.IsFinite(row.Height) || row.Height <= 0
                || Math.Abs(row.Top - bottom) >= 1) return false;
            bottom = row.Top + row.Height;
            if (!double.IsFinite(bottom)) return false;
        }
        return Math.Abs(height - bottomInset - bottom) < 1;
    }
}
