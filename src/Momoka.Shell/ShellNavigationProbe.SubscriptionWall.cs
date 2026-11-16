using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 2026-11-16 用户令「订阅媒体库怎么不会跟媒体库一样自动调整封面大小」：卡片宽度跟着这一页的可用宽度走，
    /// 窗口变宽卡片跟着变宽、塞得下多一列时退回最小宽附近再涨（与媒体库同一条算术）。
    /// <para>
    /// 问的是活的面板与活的卡片，不是源码文本：格子宽要等于按当前可用宽度算出来的那个数，卡片要横向填满
    /// 自己的格子（差一条 12 的缝就是没铺满），而且窗口变宽之后列数要真的多出来。
    /// </para>
    /// </summary>
    private async Task CheckSubscriptionWallSizingAsync(MoviePilotSubscriptionWallView wall)
    {
        var grid = Get<ListView>(wall, "WallGrid");

        // 版面这一拍铺出来的格子：Live 是面板手里那个数，Expected 是按墙宽算出来该是的那个数。
        (int Columns, int Live, int Expected, double Available, double Card) Read()
        {
            wall.UpdateLayout();
            var available = grid.ActualWidth;
            var expected = MoviePilotWallMetrics.Fit(available);
            var live = grid.ItemsPanelRoot is ItemsWrapGrid panel ? (int)Math.Round(panel.ItemWidth) : -1;
            var card = Descendants(wall).OfType<MoviePilotSubscriptionWallCard>().FirstOrDefault();
            return (expected.Columns, live, expected.ItemWidth, available, card?.ActualWidth ?? 0);
        }

        var readings = new List<(int Width, int Columns, int Live, double Available, double Card)>();
        foreach (var width in new[] { 760, 1240 })
        {
            ResizeProbeClient(width, 900);
            await LayoutAsync(wall);
            // 窗口尺寸变了之后要对三拍：版面按新宽度铺格子 → 这里把格宽交给面板 → 卡片按新格子重画。
            // 等面板真换成按当前可用宽度算出来的那个数；等不到就是没接上，下面几条断言会把读数一起报出来。
            try
            {
                await UntilAsync(() =>
                {
                    var live = Read();
                    return live.Live == live.Expected;
                });
            }
            catch (TimeoutException)
            {
            }

            var measured = Read();
            var detail = $"{width} 宽窗口：墙 {measured.Available:0}、格子 {measured.Live}、卡片 {measured.Card:0}"
                + $"、应为 {measured.Columns} 列 × {measured.Expected}";
            Require(measured.Available > 0, $"订阅墙没有画出宽度（{detail}）");
            Require(measured.Columns * measured.Expected <= measured.Available + 0.5,
                $"算出来的格子放不进这一行（{detail}）");
            Require(measured.Live == measured.Expected,
                $"格子宽度没有跟着可用宽度走 —— 卡片还钉在旧档上（{detail}）");
            Require(Math.Abs(measured.Card - (measured.Live - MoviePilotWallMetrics.Spacing)) <= 1,
                $"卡片没有铺满格子，右边空出一条缝（{detail}）");
            readings.Add((width, measured.Columns, measured.Live, measured.Available, measured.Card));
            await SaveProbeScreenAsync($"mp-wall-sizing-{width}-screen.png");
        }

        var narrow = readings[0];
        var wide = readings[1];
        Require(wide.Columns > narrow.Columns,
            $"窗口从 {narrow.Width} 宽到 {wide.Width} 宽，订阅墙没有多排出一列"
            + $"（{narrow.Columns} 列 → {wide.Columns} 列，墙 {narrow.Available:0} → {wide.Available:0}）");
        ResizeProbeClient(1240, 900);
        await LayoutAsync(wall);
    }
}
