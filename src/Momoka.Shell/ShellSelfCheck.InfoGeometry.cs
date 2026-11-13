using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal static partial class ShellSelfCheck
{
    private static Action<string>? _writeInfoGeometry;
    private static Action<Exception>? _infoGeometryFailure;
    private static readonly List<object> InfoGeometryReadings = [];

    /// <summary>每次正式ReadInfo的数字快照；不改变滚动、布局、集合或原渲染判据。</summary>
    private static void RecordInfoGeometry(DetailPage page)
    {
        if (_writeInfoGeometry is not { } write) return;
        try
        {
            var body = page.FindName("Body") as ScrollView
                ?? throw new InvalidOperationException("媒体信息读数缺少正文滚动视图");
            var content = body.Content as UIElement
                ?? throw new InvalidOperationException("媒体信息读数缺少正文内容");
            var panel = page.FindName("InfoPanel") as FrameworkElement
                ?? throw new InvalidOperationException("媒体信息读数缺少信息区域");
            var table = page.FindName("InfoTable") as ItemsRepeater
                ?? throw new InvalidOperationException("媒体信息读数缺少信息表");
            var rows = Enumerable.Range(0, page.ViewModel.InfoRows.Count).Select(index =>
            {
                var row = table.TryGetElement(index) as FrameworkElement;
                return new
                {
                    index,
                    realized = row is not null,
                    viewport = row is null ? null : Box(row, body),
                    content = row is null ? null : Box(row, content)
                };
            }).ToArray();
            InfoGeometryReadings.Add(new
            {
                tick = _ticks,
                stage = _stage,
                phase = _returned ? "after-return" : _detailReloaded ? "after-reload" : "before-reload",
                infoLooks = _infoLooks,
                returned = _returned,
                detailReloaded = _detailReloaded,
                rowsCount = page.ViewModel.InfoRows.Count,
                realized = rows.Where(row => row.realized).Select(row => row.index).ToArray(),
                body = new
                {
                    body.VerticalOffset,
                    body.ScrollableHeight,
                    body.ExtentHeight,
                    body.ViewportHeight,
                    state = body.State.ToString(),
                    contentHeight = (content as FrameworkElement)?.ActualHeight
                },
                panelViewport = Box(panel, body),
                panelContent = Box(panel, content),
                tableViewport = Box(table, body),
                tableContent = Box(table, content),
                rows
            });
            write(JsonSerializer.Serialize(InfoGeometryReadings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            // 证据失败也让最终报告保持红；不阻断ReadInfo取得原来的真实行数。
            _requiredEvidenceFailed = true;
            _infoGeometryFailure?.Invoke(error);
        }

        static object Box(FrameworkElement element, UIElement target)
        {
            var point = element.TransformToVisual(target).TransformPoint(new Point(0, 0));
            return new { x = Finite(point.X), y = Finite(point.Y), width = Finite(element.ActualWidth), height = Finite(element.ActualHeight) };
        }
        static double? Finite(double value) => double.IsFinite(value) ? value : null;
    }
}
