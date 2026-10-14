using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 集页的头图带子和尾部那条窄带 —— 用户 2026-10-13 那几句话：「固定集页面封面的大小，拉窄窗口时不要缩小」、
    /// 「切换到窄窗口时可以缩小」、「再给集页面的窄窗口加背景图」，最后落在「窄窗口下不要封面」和「窄窗口下这些
    /// 组件上下之间弄紧凑一点，改动包括电影、电视剧、季、集页面」。
    /// <para>
    /// 两条版式规矩落在同一页上，所以这一条用例把同一页在四档宽度上各量一遍：
    /// <list type="bullet">
    /// <item>1280 与 1024（宽版式）：封面画出来都是 250×375（400×600 的竖图装进 260×375 那一格），背景层收着。</item>
    /// <item>1023 与 768（紧凑版式）：封面整个收掉（<see cref="DetailHero.ShowsStill"/> 对紧凑档是 false），
    /// 背景那一层开始画 —— 那正是参考图上窄版式头图的样子。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 量的是真实控件（<c>PosterStill</c> 和 <c>Backdrop</c>／<c>BackdropPicture</c>）而不是视图模型上的数：
    /// 这两条规矩各有一半在绑定上 —— 封面那一格是绑上去的宽高和显隐、背景那一层是绑上去的显隐，算对了而绑丢了
    /// 在读数里长得一模一样。期望值仍走 Core 那一支纯函数（<see cref="DetailHero.StillBox"/>），不在这里另抄一份
    /// 算术。
    /// </para>
    /// <para>
    /// 尾部那条窄带量的是绑定本身：那几块的竖距和内边距都在视图模型上（<c>TailSpacing</c>／
    /// <c>TailPanelPadding</c>／<c>SheetPadding</c>／<c>SheetSpacing</c>），标记里各绑一次。数算对了而某一块
    /// 忘了绑、或者绑错了源，屏上就是那一块比别处松一档 —— 那正是用户圈出来的那件事，所以这里读的是控件自己
    /// 那一份，和视图模型上的数逐个对上（紧凑档 8／8／14，宽版式 12／24／20）。
    /// </para>
    /// <para>
    /// 夹具给两张图：封面那一张是竖的（400×600，一张海报的样子，截图里看得出来「这是海报」），背景那一张是
    /// 横的（默认 320×200）。
    /// </para>
    /// </summary>
    private async Task EpisodeBandAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();

        var series = Series();
        series.BackdropImageTags.Add("fixture");

        var episode = Episode("a");
        episode.Name = "离线单集 · 集页封面与窄窗背景";
        episode.SeriesName = series.Name;
        episode.ParentIndexNumber = 1;

        // 封面取的是剧集那一层的主图（「集页面的封面改用剧页面的封面」），背景图借的也是剧集那一层 ——
        // 服务器把剧集的背景图挂在 ParentBackdrop 那一对字段上，单集自己是拿不到那张图的。
        episode.ImageTags["Primary"] = "fixture";
        episode.ParentBackdropItemId = series.Id;
        episode.ParentBackdropImageTags.Add("fixture");

        var poster = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
            {
                var bytes = path.Contains("/Images/Primary", StringComparison.Ordinal) ? poster : backdrop;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
            if (path.EndsWith("/Items/" + series.Id, StringComparison.Ordinal)) return Task.FromResult(Json(series));
            if (path.EndsWith("/Items/" + episode.Id, StringComparison.Ordinal)) return Task.FromResult(Json(episode));
            return Task.FromResult(FakeTransport.Standard(request));
        };

        var frame = new Frame();
        var readings = new List<string>();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, episode));
            var page = (DetailPage)frame.Content;
            await UntilAsync(() => page.IsReady);
            await UntilAsync(() => page.ViewModel.StillImage is not null);
            await UntilAsync(() => page.ViewModel.HeroImage is not null);

            var coverWidths = new Dictionary<int, double>();
            foreach (var width in new[] { 1280, 1024, 1023, 768 })
            {
                ResizeProbeClient(width, 900);
                await UntilAsync(() =>
                {
                    page.UpdateLayout();
                    return Math.Abs(page.ViewModel.PageWidth - width) < 0.6;
                });
                await LayoutAsync(page);

                var compact = page.ViewModel.IsCompact;
                Require(compact == (width < 1024), $"{width} 这一档的版式不对：IsCompact={compact}");

                var view = page.ViewModel;
                var cover = Get<Border>(page, "PosterStill");
                var backdropLayer = Get<Grid>(page, "Backdrop");
                var picture = Get<Border>(page, "BackdropPicture");

                // 期望值走 Core 那一支：400×600 的竖图装进 260×375 那一格（贴高，宽收到 250）。
                var fit = DetailHero.StillBox(400, 600, 260, 375) ?? default;

                // 封面：宽版式画（「固定集页面封面的大小，拉窄窗口时不要缩小」——1280 和 1024 上是同一个数），
                // 紧凑版式整个收掉（「窄窗口下不要封面」，四种页面同一个答案）。
                if (compact)
                {
                    Require(cover.Visibility == Visibility.Collapsed,
                        $"{width} 这一档是紧凑版式，封面该收着，量出来 {cover.Visibility}");
                    // 收起来的那一档 ActualWidth 是上一次排版的残值（元素不参与这一拍的量测），所以这一句问
                    // 视图模型上那个数：版面里那一栏必须真的是 0 宽 —— 不然片名那一栏的左沿（StackLeft 读
                    // StillWidth）还按一张海报算，屏上就是片名往右缩进一格。
                    Require(view.StillWidth == 0 && view.StillHeight == 0,
                        $"{width} 这一档封面收着了，可那一格还占着 {view.StillWidth:0.##}×{view.StillHeight:0.##}");
                }
                else
                {
                    Require(cover.Visibility == Visibility.Visible, $"{width} 这一档集页的封面整个收着");
                    Require(Math.Abs(cover.ActualWidth - fit.Width) < 1.5 && Math.Abs(cover.ActualHeight - fit.Height) < 1.5,
                        $"{width} 这一档封面该是 {fit.Width:0.##}×{fit.Height:0.##}，量出来"
                            + $" {cover.ActualWidth:0.##}×{cover.ActualHeight:0.##}");
                }

                // 背景那一层：宽版式不铺（「去掉集页面的背景图」），紧凑版式铺（「再给集页面的窄窗口加背景图」）。
                var wantBackdrop = compact ? Visibility.Visible : Visibility.Collapsed;
                Require(backdropLayer.Visibility == wantBackdrop,
                    $"{width} 这一档背景层的显隐该是 {wantBackdrop}，量出来 {backdropLayer.Visibility}");

                if (compact)
                {
                    var want = page.ViewModel.PictureHeight;
                    Require(picture.Visibility == Visibility.Visible && Math.Abs(picture.ActualHeight - want) < 1.5,
                        $"{width} 这一档画面盒的高该是 {want:0.##}，量出来 {picture.ActualHeight:0.##}"
                            + $"（显隐 {picture.Visibility}）");
                    Require(picture.ActualHeight >= page.ViewModel.HeroHeight - 1.5,
                        $"{width} 这一档画面条比带子还矮：{picture.ActualHeight:0.##} 对 {page.ViewModel.HeroHeight:0.##}");
                }

                // 尾部那条窄带：控件自己那一份和视图模型上的数逐个对上（绑定有没有断）。
                var tail = Get<StackPanel>(page, "TailContent");
                var pickers = Get<StackPanel>(page, "PickerPanel");
                var overview = Get<StackPanel>(page, "OverviewPanel");
                var sheet = Get<StackPanel>(page, "BodySheet");
                Require(Math.Abs(tail.Spacing - view.TailSpacing) < 0.01,
                    $"{width} 这一档尾部三块之间的竖距没绑上：控件 {tail.Spacing:0.##} 对视图模型 {view.TailSpacing:0.##}");
                Require(pickers.Padding == view.TailPanelPadding && overview.Padding == view.TailPanelPadding,
                    $"{width} 这一档文件选项／剧情说明的内边距没绑上：{pickers.Padding}、{overview.Padding}"
                        + $" 对 {view.TailPanelPadding}");
                Require(sheet.Padding == view.SheetPadding,
                    $"{width} 这一档纸面的内边距没绑上：{sheet.Padding} 对 {view.SheetPadding}");
                Require(Math.Abs(sheet.Spacing - view.SheetSpacing) < 0.01,
                    $"{width} 这一档纸面上几块的竖距没绑上：控件 {sheet.Spacing:0.##} 对视图模型 {view.SheetSpacing:0.##}");

                // 紧凑档那几个数就是用户要的那一档（8／8／14），宽版式照旧（12／24／20）。
                var wantTail = compact ? 8d : 12d;
                var wantPanel = compact ? 8d : 24d;
                var wantSheet = compact ? 14d : 20d;
                Require(Math.Abs(tail.Spacing - wantTail) < 0.01
                        && Math.Abs(pickers.Padding.Top - wantPanel) < 0.01
                        && Math.Abs(pickers.Padding.Left - 24) < 0.01
                        && Math.Abs(sheet.Spacing - wantSheet) < 0.01,
                    $"{width} 这一档的紧凑度不对：尾部 {tail.Spacing:0.##}（该 {wantTail}）、"
                        + $"面板 {pickers.Padding}（该上下 {wantPanel}、左右 24）、纸面 {sheet.Spacing:0.##}（该 {wantSheet}）");

                coverWidths[width] = view.StillWidth;
                readings.Add($"集页 {width}: 版式={(compact ? "紧凑" : "宽")}，封面 {cover.Visibility}"
                    + $" {cover.ActualWidth:0.##}×{cover.ActualHeight:0.##}（那一格 {view.StillWidth:0.##}×{view.StillHeight:0.##}），"
                    + $"带高 {page.ViewModel.HeroHeight:0.##}，背景层 {backdropLayer.Visibility}，画面盒 {picture.ActualHeight:0.##}"
                    + $"（该 {page.ViewModel.PictureHeight:0.##}），尾部竖距 {tail.Spacing:0.##}，"
                    + $"面板内边距 {pickers.Padding}，纸面 {sheet.Padding}／{sheet.Spacing:0.##}");
                await SaveProbeScreenAsync($"detail-episode-band-{width}.png");
            }

            // 这一条是用户第一句话本身：宽版式里 1280 到 1024 窄了 256 像素，封面一个像素不动。
            Require(Math.Abs(coverWidths[1280] - coverWidths[1024]) < 0.5 && coverWidths[1280] > 0,
                $"宽版式收窄窗口时封面跟着缩了：1280 上 {coverWidths[1280]:0.##}、1024 上 {coverWidths[1024]:0.##}");
            // 而跨过紧凑断点那一下它是整个收掉，不是缩成一小条（最后一句「窄窗口下不要封面」）。
            Require(coverWidths[1023] == 0 && coverWidths[768] == 0,
                $"紧凑版式里那一格还占着地方：1023 上 {coverWidths[1023]:0.##}、768 上 {coverWidths[768]:0.##}");

            Require(fixture.Actions.Plays == 0, "布局检查意外请求播放");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-episode-band.txt"), readings);
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
