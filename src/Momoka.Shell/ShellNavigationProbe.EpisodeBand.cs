using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    /// <item>关闭沉浸时1280 与 1024（宽版式）：封面画出来都是 250×375，背景层收着，保留旧卡面。</item>
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
        fixture.Settings.Ui.EnableImmersive = false;
        fixture.Settings.Ui.UseGaussianBackdrop = false;
        fixture.Settings.Ui.UseAcrylicBackdrop = false;
        ShellPrefs.ApplyAppearance(fixture.Settings.Ui);

        var series = Series();
        series.BackdropImageTags.Add("fixture");
        series.ImageTags["Art"] = "fixture";
        series.ImageTags["Primary"] = "fixture";

        var episode = Episode("a");
        episode.Name = "离线单集 · 集页封面与窄窗背景";
        episode.SeriesName = series.Name;
        episode.ParentIndexNumber = 1;
        // 断点与一段两行的简介：宽版式上「剩余 x 分钟」那一行、以及被提上去的剧情说明都要有实底可量
        // （2026-11-02「接到 剩余18分钟下面」）。
        episode.RunTimeTicks = TimeSpan.FromMinutes(23).Ticks;
        episode.UserData = new() { PlaybackPositionTicks = TimeSpan.FromMinutes(5).Ticks };
        episode.Overview = "为了查明充满谜团的有栖的真实身份，梅和绽决定偷偷观察她的日常生活。"
            + "没想到两人趁机翻找有栖的随身物品时，有栖竟然回来了！？这下该怎么办！？";
        // 两条媒体源、两条音轨：用户的集页上就有「视频：…」和音频那一颗下拉（2026-11-02 那张截图），
        // 它们把片名那一叠撑高 —— 说明提上去之后离板底还剩多少，正是在这一档上量的「自动剪裁下方空白」。
        // 没有它们的那一版夹具里那一叠只有两百四十来高，读数上的余量比真页面上大一截。
        episode.MediaSources =
        [
            new()
            {
                Id = "source-a",
                Name = "离线版本 · 1080p",
                Size = 385L * 1024 * 1024,
                Container = "mkv",
                MediaStreams =
                [
                    new() { Index = 0, Type = "Video", Codec = "h264", Width = 1920, Height = 1080 },
                    new() { Index = 1, Type = "Audio", DisplayTitle = "Japanese · AAC stereo", IsDefault = true },
                    new() { Index = 2, Type = "Audio", DisplayTitle = "Chinese · AAC stereo" }
                ]
            },
            new()
            {
                Id = "source-b",
                Name = "离线版本 · 720p",
                Size = 180L * 1024 * 1024,
                Container = "mkv",
                MediaStreams = [new() { Index = 0, Type = "Video", Codec = "h264", Width = 1280, Height = 720 }]
            }
        ];

        // 封面取的是剧集那一层的主图（「集页面的封面改用剧页面的封面」），背景图借的也是剧集那一层 ——
        // 服务器把剧集的背景图挂在 ParentBackdrop 那一对字段上，单集自己是拿不到那张图的。
        episode.ImageTags["Primary"] = "fixture";
        episode.ParentBackdropItemId = series.Id;
        episode.ParentBackdropImageTags.Add("fixture");

        var poster = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync();
        var artwork = await FixtureImageAsync(1600, 900);
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
            {
                var bytes = path.Contains("/Images/Primary", StringComparison.Ordinal) ? poster
                    : path.Contains("/Images/Art", StringComparison.Ordinal) ? artwork : backdrop;
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
            page.AttachWindow(_window!);
            await UntilAsync(() => page.IsReady);
            await UntilAsync(() => page.ViewModel.StillImage is not null);
            await UntilAsync(() => page.ViewModel.HeroImage is not null);
            await UntilAsync(() => page.ViewModel.CornerImage is not null);

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
                Require(Same(pickers.Padding, view.TailPanelPadding)
                        && Same(overview.Padding, view.OverviewPanelPadding),
                    $"{width} 这一档文件选项／剧情说明的内边距没绑上：{pickers.Padding}、{overview.Padding}"
                        + $" 对 {view.TailPanelPadding}／{view.OverviewPanelPadding}");
                Require(Same(sheet.Padding, view.SheetPadding),
                    $"{width} 这一档纸面的内边距没绑上：{sheet.Padding} 对 {view.SheetPadding}");
                Require(Math.Abs(sheet.Spacing - view.SheetSpacing) < 0.01,
                    $"{width} 这一档纸面上几块的竖距没绑上：控件 {sheet.Spacing:0.##} 对视图模型 {view.SheetSpacing:0.##}");

                // 2026-11-02「把集页面下方的剧情说明接到 剩余18分钟下面（多留点空位），然后自动剪裁下方空位
                // （留点空位）」以及同一天的更正「不要固定间距，以封面下沿为准」：宽版式上说明的第一行落在封面
                // 实测的下沿再向下 8（2026-11-05）；封面下沿离「剩余 x 分钟」比 OverviewMinGap 还近、或者
                // 没有封面时才退回那一行底下 40。板底下沿只剩说明自己的 8 加收尾格的 16（那 16 是给板底那两个
                // 圆角撑出来的板面）；紧凑版式不提拉（那一档的进度和说明本来就排在同一条单列里），收尾格也不占高。
                var overviewText = Get<TextBlock>(page, "Overview");
                var textTop = overviewText.TransformToVisual(page).TransformPoint(default).Y;
                var textBottom = textTop + overviewText.ActualHeight;
                var cardGap = Get<Border>(page, "CardTailGap");
                if (compact)
                {
                    Require(view.TailMargin == default,
                        $"{width} 这一档紧凑版式不该提拉剧情说明：{view.TailMargin}");
                    Require(Math.Abs(cardGap.ActualHeight) < 0.01,
                        $"{width} 这一档紧凑版式那块板不存在，收尾格却占了 {cardGap.ActualHeight:0.##}");
                }
                else
                {
                    var actions = Get<StackPanel>(page, "ColumnActions");
                    var actionsBottom = actions.TransformToVisual(page).TransformPoint(default).Y + actions.ActualHeight;
                    var heroTail = Get<StackPanel>(page, "HeroTail");
                    var tailTop = heroTail.TransformToVisual(page).TransformPoint(default).Y;
                    var cardBottom = tailTop + heroTail.ActualHeight;
                    var foot = cardGap.ActualHeight;
                    var gap = textTop - actionsBottom;
                    var under = cardBottom - textBottom;
                    var coverBottom = cover.TransformToVisual(page).TransformPoint(default).Y + cover.ActualHeight;
                    var wantGap = Math.Max(DetailHero.OverviewMinGap, coverBottom + DetailHero.OverviewExtraGap - actionsBottom);
                    var wantBottom = Math.Max(tailTop + foot, textBottom + view.OverviewPanelPadding.Bottom + foot);
                    Require(view.TailMargin.Top < -1,
                        $"{width} 这一档宽版式没有提拉剧情说明：{view.TailMargin}");
                    Require(Math.Abs(gap - wantGap) < 1.5,
                        $"{width} 这一档剧情说明离「剩余 x 分钟」{gap:0.#}，该是 {wantGap:0.#}"
                            + $"（封面下沿 {coverBottom:0.#} 离那一行 {coverBottom - actionsBottom:0.#}，"
                            + $"OverviewMinGap {DetailHero.OverviewMinGap:0.#}）");
                    Require(textTop >= coverBottom + DetailHero.OverviewExtraGap - 0.5,
                        $"{width} 这一档剧情说明第一行 {textTop:0.#} 未在封面下沿 {coverBottom:0.#} 下方多留 8");
                    Require(Math.Abs(textTop - Math.Max(coverBottom + DetailHero.OverviewExtraGap, actionsBottom + DetailHero.OverviewMinGap)) < 1.5,
                        $"{width} 这一档剧情说明第一行 {textTop:0.#} 没落在封面下沿 {coverBottom:0.#} 再向下 8"
                            + $"（或那一行底下 {DetailHero.OverviewMinGap:0.#}）那条线上");
                    Require(Math.Abs(foot - DetailHero.CardTailGap) < 0.01,
                        $"{width} 这一档板底的收尾格 {foot:0.##}，该是 {DetailHero.CardTailGap:0.##}"
                            + "（不足 16 板底那两个圆角就画不出来）");
                    Require(Math.Abs(cardBottom - wantBottom) < 1.5,
                        $"{width} 这一档板底下沿 {cardBottom:0.#} 没贴住内容（该是 {wantBottom:0.#}：说明底 {textBottom:0.#} 加面板的"
                            + $" {view.OverviewPanelPadding.Bottom:0.#} 再加收尾格 {foot:0.#}，或带子底下 {tailTop:0.#}）");
                    Require(under >= DetailHero.CardTailGap - 0.01,
                        $"{width} 这一档字到板底只剩 {under:0.#}，板底那两个 16 的圆角画不出来");
                    Require(Same(view.HeroInset, new Thickness(48, 28, 48, 0)),
                        $"{width} 这一档封面留白被意外改变：{view.HeroInset}");
                    readings.Add($"{width} 提拉 {view.TailMargin.Top:0.#}，说明距剩余 {gap:0.#}"
                        + $"（封面下沿 {coverBottom:0.#}），板底留 {under:0.#}"
                        + $"（说明自己 {view.OverviewPanelPadding.Bottom:0.#}＋收尾格 {foot:0.#}）");
                }

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

            var maximum = _window!.WorkArea().Width / _root.XamlRoot.RasterizationScale;
            var midpoint = (int)Math.Round((DetailHero.CornerGrowFrom + maximum) / 2);
            foreach (var width in new[] { 1280, 1400, midpoint, 0, 1280 })
            {
                if (width == 0) _window.ToggleMaximize();
                else
                {
                    if (_window.IsMaximized) _window.ToggleMaximize();
                    ResizeProbeClient(width, 900);
                }
                await LayoutAsync(page);
                var corner = Get<Image>(page, "CornerArt");
                var actualWindow = width == 0 ? maximum : _window.ClientSize.Width / _root.XamlRoot.RasterizationScale;
                var scale = DetailHero.CornerScale(actualWindow, maximum);
                var wantWidth = DetailHero.CornerBoxWidth(page.ViewModel.PageWidth, page.ViewModel.StillWidth, scale);
                var wantHeight = wantWidth * 9 / 16;
                bool LimitsMatch() => Math.Abs(corner.MaxWidth - wantWidth) < 0.01
                    && Math.Abs(corner.MaxHeight - wantHeight) < 0.01;
                if (width == 1280)
                {
                    var saved = corner.MaxWidth;
                    try
                    {
                        corner.MaxWidth = saved + 40;
                        Require(!LimitsMatch(), "错误的艺术图尺寸上限未被检查识别");
                    }
                    finally { corner.MaxWidth = saved; }
                }
                // 解码后的位图像素会取整，Image 的 Uniform 按那一份比例排版；分别验上限绑定、贴边与真实比例，
                // 不能把原图 16:9 当作缩略图也能精确保留的比例，否则两次取整会造成约一像素的假失败。
                var bitmap = (BitmapSource)corner.Source;
                var pixel = 1 / _root.XamlRoot.RasterizationScale;
                Require(corner.Visibility == Visibility.Visible
                    && LimitsMatch()
                    && corner.ActualWidth <= wantWidth + pixel && corner.ActualHeight <= wantHeight + pixel
                    && Math.Min(Math.Abs(corner.ActualWidth - wantWidth), Math.Abs(corner.ActualHeight - wantHeight)) <= pixel
                    && Math.Abs(corner.ActualWidth - corner.ActualHeight * bitmap.PixelWidth / bitmap.PixelHeight) <= pixel,
                    $"艺术图尺寸未跟随窗口：{actualWindow:0.##} 上 {corner.ActualWidth:0.##}×{corner.ActualHeight:0.##}，应为 {wantWidth:0.##}×{wantHeight:0.##}");
                Require(page.ViewModel.HeroHeight >= corner.ActualHeight, "艺术图溢出头部高度");
                readings.Add($"艺术图：窗口 {actualWindow:0.##}，最大 {maximum:0.##}，倍率 {scale:0.###}，实际 {corner.ActualWidth:0.##}×{corner.ActualHeight:0.##}");
                await SaveProbeScreenAsync($"detail-corner-{(width == 0 ? "max" : width.ToString())}.png");
            }

            fixture.Settings.Ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            var target = page.ViewModel.PlayTarget;
            var expectedPoster = DetailHero.StillBox(400, 600, 260, 375) ?? default;
            foreach (var width in new[] { 1280, 1024, 1023, 768, 1280 })
            {
                await ResizeResponsiveAsync(page, width, 900);
                await LayoutAsync(page);
                var view = page.ViewModel;
                var compact = width < DetailHero.CompactFloor;
                var cover = Get<Border>(page, "PosterStill");
                Require(view.Showcase == !compact && view.BackdropShown && !view.HeroCardShown,
                    $"开启沉浸的单集 {width} 未使用剧式宽窗或原紧凑背景");
                RequireEpisodeSubject(page, episode);
                Require(ReferenceEquals(target, view.PlayTarget), "单集沉浸缩放改变播放目标");
                Require(Get<Image>(page, "CornerArt").Visibility == Visibility.Collapsed,
                    "单集沉浸仍显示右上角艺术图");
                if (compact)
                {
                    Require(cover.Visibility == Visibility.Collapsed && view.StillWidth == 0 && view.StillHeight == 0,
                        "单集沉浸窄窗隐藏海报后仍占位");
                }
                else
                {
                    Require(Math.Abs(cover.ActualWidth - expectedPoster.Width) < 1.5
                        && Math.Abs(cover.ActualHeight - expectedPoster.Height) < 1.5,
                        $"单集沉浸海报未使用统一剧页尺寸：{cover.ActualWidth:0.#}×{cover.ActualHeight:0.#}");
                    Require(Get<Button>(page, "CoverButton").VerticalAlignment == VerticalAlignment.Top
                        && Get<Grid>(page, "HeroContent").VerticalAlignment == VerticalAlignment.Bottom,
                        "单集沉浸封面或文字栏未使用剧页对齐");
                    Require(view.TailMargin == default && view.HeroInset.Left == 32,
                        "单集沉浸仍使用旧卡面简介提拉或48/60左距");
                    RequireImmersiveSurface(page);
                    RequireResponsiveBindings(page);
                    if (width == 1024)
                    {
                        var fill = Get<AcrylicBrush>(page, "_panelFill");
                        var opacity = fill.Opacity;
                        try
                        {
                            fill.Opacity = 1;
                            var rejected = false;
                            try { RequireImmersiveSurface(page); }
                            catch (InvalidOperationException) { rejected = true; }
                            Require(rejected, "沉浸正文检查没有拒绝不透明卡面反例");
                        }
                        finally { fill.Opacity = opacity; }
                    }
                }
                readings.Add($"沉浸单集 {width}：海报{view.StillWidth:0.#}×{view.StillHeight:0.#}，集号{view.Subline}，背景{view.BackdropShown}");
                await SaveProbeScreenAsync($"detail-episode-immersive-{width}.png");
            }

            await CheckImmersiveSwitchAsync(page, fixture.Settings.Ui);

            // 真正的无图DTO：不改HeroArt伪造结果，重新进入无任何图片标签的单集。
            var noArtwork = Episode("a");
            noArtwork.Id = "episode-no-artwork";
            noArtwork.Name = "离线无图单集";
            noArtwork.SeriesName = series.Name;
            noArtwork.ParentIndexNumber = 1;
            noArtwork.Overview = episode.Overview;
            noArtwork.MediaSources = episode.MediaSources;
            fixture.Transport.Reply = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/Items/" + noArtwork.Id, StringComparison.Ordinal)) return Task.FromResult(Json(noArtwork));
                if (path.EndsWith("/Episodes", StringComparison.Ordinal))
                    return Task.FromResult(Json(new { Items = new[] { noArtwork } }));
                return Task.FromResult(FakeTransport.Standard(request));
            };
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, noArtwork));
            var emptyPage = (DetailPage)frame.Content;
            emptyPage.AttachWindow(_window!);
            await UntilAsync(() => emptyPage.IsReady);
            foreach (var width in new[] { 1280, 760, 1280 })
            {
                await ResizeResponsiveAsync(emptyPage, width, 900);
                await LayoutAsync(emptyPage);
                Require(!emptyPage.ViewModel.BackdropShown && !emptyPage.ViewModel.Showcase,
                    "无图单集错误进入沉浸或保留旧背景");
                Require(Get<Grid>(emptyPage, "Backdrop").Visibility == Visibility.Collapsed
                    && Get<AcrylicBrush>(emptyPage, "_panelFill").Opacity == 1,
                    "无图单集没有恢复可读正文卡面");
                RequireEpisodeSubject(emptyPage, noArtwork);
                await SaveProbeScreenAsync($"detail-episode-no-artwork-{width}.png");
            }
            await CheckImmersiveSwitchAsync(emptyPage, fixture.Settings.Ui);

            Require(fixture.Actions.Plays == 0, "布局检查意外请求播放");
        }
        finally
        {
            if (_window?.IsMaximized == true) _window.ToggleMaximize();
            try { File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-episode-band.txt"), readings); }
            finally
            {
                (frame.Content as IShellContent)?.Release();
                _root.Children.Remove(frame);
                ResizeInspect(1420, 980);
            }
        }
    }
}
