using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 2026-10-29 用户令的两条：所有弹窗点外面就关（<see cref="DialogDismiss"/>），以及字幕管理右上角那颗 ✕。
    /// <para>
    /// 这一关用的是真模板、真弹窗：判「外面」靠的是模板里那颗 <c>BackgroundElement</c> 的矩形，而弹窗元素
    /// 自己铺满整个窗口，所以只有真表量得出这个矩形；屏幕截图另外留一张给右上角的 ✕。
    /// </para>
    /// <para>
    /// <b>没有注入真实指针。</b>这里调的是指针处理里同一个 <see cref="DialogDismiss.TryDismiss"/>（几何判定
    /// 与 <c>Hide</c> 都在里面），按下事件那一层的接线由 <c>DialogChromeTests</c> 的源码契约盯着；真实鼠标
    /// 那一下仍属未覆盖，按验证技能的要求不拿合成鼠标事件冒充。
    /// </para>
    /// </summary>
    private async Task DialogDismissAsync()
    {
        ResizeProbeClient(1100, 900);
        await LayoutAsync(_root);

        // 一、代码里 new 出来的普通表 —— 隐式 TargetType="ContentDialog" 那条样式。
        var plain = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = "点外面关闭（离线）",
            Content = "离线夹具，不含账号与网络。",
            CloseButtonText = "关闭"
        };
        var plainShown = plain.ShowAsync();
        string insideRead;
        try
        {
            await LayoutAsync(plain);
            Require(DialogDismiss.GetClosesOnOutsideClick(plain), "普通弹窗没有接上「点外面关闭」");
            var bounds = SurfaceBounds(plain);
            var inside = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            var outside = OutsidePoint(bounds);
            Require(!DialogDismiss.ShouldDismissAt(plain, inside), "弹窗里面的点被当成外面");
            Require(DialogDismiss.ShouldDismissAt(plain, outside), "弹窗外面那块没被认出来");
            Require(!DialogDismiss.TryDismiss(plain, inside), "点在弹窗里面就把表关了");
            await Task.Delay(120);
            Require(plainShown.Status == Windows.Foundation.AsyncStatus.Started, "点在弹窗里面之后表已经关了");
            insideRead = $"表面 {bounds.Width:0}×{bounds.Height:0}，里面 {inside.X:0},{inside.Y:0}，外面 {outside.X:0},{outside.Y:0}";
            Require(DialogDismiss.TryDismiss(plain, outside), "点在弹窗外面没有关表");
            Require(await plainShown == ContentDialogResult.None, "点外面关表的结果不是「没选」");
        }
        finally { plain.Hide(); await plainShown; }

        // 二、字幕管理：右下角那颗关闭没了，右上角那颗 ✕ 与正文右沿对齐、按它真的关表。
        var (dialog, shown) = SubtitleFixture("第 4 集", () => Task.CompletedTask);
        string headerRead;
        try
        {
            await LayoutAsync(dialog);
            Require(DialogDismiss.GetClosesOnOutsideClick(dialog), "字幕弹窗没有接上「点外面关闭」");
            var header = dialog.ProbeHeader();
            Require(header.Ok, header.Detail);
            headerRead = header.Detail;
            await SaveProbeScreenAsync("dialog-dismiss-subtitle-screen.png");
            dialog.OnCloseForProbe();
            Require(await shown == ContentDialogResult.None, "点右上角 ✕ 没有关表");
        }
        finally { dialog.Hide(); await shown; }

        // 三、写入在飞时点外面也关不掉：Closing 里那条拦截要照样管住这一条新出口。
        var write = Pending<bool>();
        var (busy, busyShown) = SubtitleFixture("写入在飞", () => write.Task);
        string busyRead;
        try
        {
            await LayoutAsync(busy);
            busy.OnDownloadForProbe();
            DialogDismiss.TryDismiss(busy, OutsidePoint(SurfaceBounds(busy)));
            await Task.Delay(150);
            var stillOpen = busyShown.Status == Windows.Foundation.AsyncStatus.Started;
            var closeEnabled = busy.ProbeHeader().CloseEnabled;
            Require(stillOpen, "写入在飞时点外面把表关掉了");
            Require(!closeEnabled, "写入在飞时右上角 ✕ 还能按");
            busyRead = $"点外面后仍开着={stillOpen}、✕ 可用={closeEnabled}";
            write.SetResult(true);
            // 写入收尾后 Closing 不再拦，这条出口要恢复；也是 finally 里 Hide 能真的关掉它的前提。
            await UntilAsync(() => !busy.ProbeBusy);
        }
        finally
        {
            write.TrySetResult(true);
            // 收尾不抛：这里再抛一次只会盖掉上面真正的失败原因。
            for (var waited = 0; waited < 40 && busy.ProbeBusy; waited++) await Task.Delay(50);
            busy.Hide();
            await busyShown;
        }

        Require(true, $"{insideRead}；{headerRead}；{busyRead}");
    }

    private (SubtitleDialog Dialog, Windows.Foundation.IAsyncOperation<ContentDialogResult> Shown) SubtitleFixture(
        string name, Func<Task> download)
    {
        var stream = new MediaStream { Index = 1, Type = "Subtitle", Codec = "srt", IsExternal = true, Title = "离线字幕" };
        var source = new MediaSource { Id = "fixture", Name = "离线文件.mkv", MediaStreams = [stream] };
        var dialog = new SubtitleDialog(new EmbyItem { Id = "fixture", Name = name }, source,
            _ => Task.FromResult(new List<RemoteSubtitleInfo>()),
            _ => download(),
            _ => Task.CompletedTask)
        { XamlRoot = _root.XamlRoot };
        return (dialog, dialog.ShowAsync());
    }

    private static Rect SurfaceBounds(ContentDialog dialog)
    {
        var surface = DialogDismiss.Surface(dialog) ?? throw new InvalidOperationException("弹窗模板里没有 BackgroundElement");
        return surface.TransformToVisual(dialog).TransformBounds(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
    }

    /// <summary>表面左上方 8 DIP —— 落在那圈遮罩上，也就是用户说的「弹窗外部主窗口内」。</summary>
    private static Point OutsidePoint(Rect bounds)
    {
        if (bounds.X >= 12) return new Point(bounds.X - 8, bounds.Y + 8);
        if (bounds.Y >= 12) return new Point(bounds.X + 8, bounds.Y - 8);
        return new Point(bounds.Right + 8, bounds.Y + 8);
    }
}
