using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 电影、剧、季三页的窄窗响应（2026-11-02 用户令）：
    /// <list type="number">
    /// <item>窗口窄下去时竖留白一起收（<see cref="DetailHero.Narrow"/>），带子跟着矮、下方整体上移；</item>
    /// <item>剧情说明快掉出背景图时，头部再往上收那 24 像素（<see cref="DetailHero.Fit"/>）；收完还不够就
    /// 允许它探出画面，但那种时候一定已经在最紧那一档；</item>
    /// <item>窗口拉宽到背景图快被裁掉时，自动走无遮挡版式（<see cref="DetailHero.NearCrop"/>），并把背景图的
    /// 不透明度从 0.9 抬到 0.96。</item>
    /// </list>
    /// <para>
    /// 量的是真实控件（带子、尾部那一叠、剧情说明那一块、背景图那一层）而不是视图模型上的数：算对了而某一条
    /// 绑定断了，在读数里长得一模一样，而那正是用户会看见的那一半。期望值仍走 Core 那几支纯函数，不在这里
    /// 另抄一份算术。
    /// </para>
    /// <para>
    /// 三个页面在同一组宽度上各走一遍，然后逐档对一遍三者的带高、竖距和内边距 —— 「三个页面的表现一致」是
    /// 用户点名的要求，而它们共用同一个视图模型，错起来只会是某一页的某个绑定没接上。
    /// </para>
    /// </summary>
    private async Task DetailResponsiveAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();

        // 前一条用例若把窗口留在最大化上，ResizeProbeClient 会被忽略 —— 先还原再量，
        // 否则下面每一档量的都是最大化那一扇窗口，版面根本没跟着宽度走。
        if (_window!.IsMaximized) _window.ToggleMaximize();
        await SettleAsync();

        var poster = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync(1600, 900);
        var source = new MediaSource
        {
            Id = "offline",
            Name = "离线版本 · 1080p",
            Container = "mkv",
            MediaStreams =
            [
                new() { Index = 0, Type = "Video", Codec = "h264", Width = 1920, Height = 1080 },
                new() { Index = 1, Type = "Audio", DisplayTitle = "国语 · AAC 2.0", IsDefault = true },
                new() { Index = 2, Type = "Subtitle", DisplayTitle = "简体中文", IsDefault = true }
            ]
        };

        var series = Series();
        series.Overview = ResponsiveOverview;
        series.People = [new() { Name = "离线导演", Type = "Director" }];
        series.BackdropImageTags.Add("fixture");
        series.ImageTags["Primary"] = "fixture";
        series.ImageTags["Art"] = "fixture";

        var season = Season("a");
        season.Overview = ResponsiveOverview;
        season.People = [new() { Name = "离线导演", Type = "Director" }];
        season.BackdropImageTags.Add("fixture");
        season.ImageTags["Primary"] = "fixture";
        season.ImageTags["Art"] = "fixture";

        var movie = new EmbyItem
        {
            Id = "movie",
            Name = "离线测试电影 · 窄窗响应",
            Type = EmbyItemType.Movie,
            Overview = ResponsiveOverview,
            People = [new() { Name = "离线导演", Type = "Director" }],
            RunTimeTicks = TimeSpan.FromMinutes(45).Ticks
        };
        movie.BackdropImageTags.Add("fixture");
        movie.ImageTags["Primary"] = "fixture";
        movie.ImageTags["Art"] = "fixture";
        movie.MediaSources = [source];

        var episode = Episode("a");
        episode.Name = "离线单集 · 窄窗响应";
        episode.SeriesName = series.Name;
        episode.ParentIndexNumber = 1;
        episode.MediaSources = [source];

        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
            {
                var bytes = path.Contains("/Images/Primary", StringComparison.Ordinal) ? poster : backdrop;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
            if (path.EndsWith("/Items/movie", StringComparison.Ordinal)) return Task.FromResult(Json(movie));
            if (path.EndsWith("/Items/a", StringComparison.Ordinal)) return Task.FromResult(Json(season));
            if (path.EndsWith("/Items/show", StringComparison.Ordinal)) return Task.FromResult(Json(series));
            if (path.EndsWith("/Episodes", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = new[] { episode } }));
            return Task.FromResult(FakeTransport.Standard(request));
        };

        var readings = new List<string>();
        // 副屏的逻辑宽度只有 1280（1920 物理、150% 缩放），所以最宽这一档取 1280：再宽窗口会伸出屏幕，
        // 几何仍然量得到，可截图只有左半边，拿不出完整证据。1440 以上「一分不收」那一段在 Core 的单测里。
        var widths = new[] { 1280, 1200, 1100, 1024 };
        var metrics = new Dictionary<int, List<(double Band, double Pad, double Spacing, double Height)>>();
        try
        {
            foreach (var (item, type) in new[] { (movie, EmbyItemType.Movie), (series, EmbyItemType.Series), (season, EmbyItemType.Season) })
            {
                // 头上让出 68（标题栏加面包屑）—— 真页面的那一截由外壳给，这一条要量的是「画面下沿折进
                // 滚动内容之后」那个坐标（HeaderLift），不把这一截摆出来，裁切与「还差多少」都会算得偏松。
                var host = new Grid { Margin = new Thickness(0, 68, 0, 0) };
                var frame = new Frame();
                host.Children.Add(frame);
                _root.Children.Add(host);
                try
                {
                    frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
                    var page = (DetailPage)frame.Content;
                    page.AttachWindow(_window!);
                    await UntilAsync(() => page.IsReady && page.ViewModel.HeroImage is not null
                        && page.ViewModel.StillImage is not null);
                    await LayoutAsync(page);

                    var scroll = Get<ScrollView>(page, "Body");
                    var band = Get<Grid>(page, "HeroBand");
                    var heroContent = Get<Grid>(page, "HeroContent");
                    var tail = Get<StackPanel>(page, "TailContent");
                    var overview = Get<StackPanel>(page, "OverviewPanel");
                    var pickers = Get<StackPanel>(page, "PickerPanel");
                    var picture = Get<Border>(page, "BackdropPicture");
                    var content = (UIElement)scroll.Content;

                    var previousSqueeze = -1d;
                    var previousOverflow = double.NegativeInfinity;

                    foreach (var width in widths)
                    {
                        await ResizeResponsiveAsync(page, width, 900);
                        await LayoutAsync(page);

                        var view = page.ViewModel;
                        // Squeeze 的一支读的是上一帧量到的 TailIntrinsic；它更新后 x:Bind 还要再过一次布局
                        // 才落到控件上。等控件自己那一份追上视图模型再断言 —— 一帧的滞后不是绑定断了，但
                        // 追不上仍然是红。
                        for (var settle = 0; settle < 20; settle++)
                        {
                            page.UpdateLayout();
                            if (Same(pickers.Padding, view.TailPanelPadding)
                                && Same(overview.Padding, view.OverviewPanelPadding)
                                && Same(tail.Padding, view.TailInset)
                                && Same(heroContent.Margin, view.HeroInset))
                                break;
                            await Task.Delay(16);
                        }

                        Require(!view.IsCompact && !view.Showcase,
                            $"{type} {width}：这一档该是纸面宽版式，实际 紧凑={view.IsCompact}、沉浸={view.Showcase}");

                        // 1) 视图模型给的数与控件自己那一份逐个对上 —— 绑定断了在这里响。
                        Require(Same(heroContent.Margin, view.HeroInset),
                            $"{type} {width} 头部留白没绑上：控件 {heroContent.Margin} 对 {view.HeroInset}");
                        Require(Same(tail.Padding, view.TailInset),
                            $"{type} {width} 尾部留白没绑上：{tail.Padding} 对 {view.TailInset}");
                        Require(Math.Abs(tail.Spacing - view.TailSpacing) < 0.01,
                            $"{type} {width} 尾部竖距没绑上：{tail.Spacing:0.##} 对 {view.TailSpacing:0.##}");
                        Require(Same(pickers.Padding, view.TailPanelPadding)
                                && Same(overview.Padding, view.OverviewPanelPadding),
                            $"{type} {width} 面板内边距没绑上：{pickers.Padding}／{overview.Padding}"
                                + $" 对 {view.TailPanelPadding}／{view.OverviewPanelPadding}；收 {view.Squeeze:0.###}");
                        Require(Math.Abs(band.ActualHeight - view.HeroHeight) < 1.5,
                            $"{type} {width} 带高没按视图模型排：{band.ActualHeight:0.##} 对 {view.HeroHeight:0.##}");

                        // 2) 窗口越窄收得越紧，而且宽度那一条曲线至少占一部分（Fit 只会再加，不会减）。
                        // 拿实际页宽算曲线，不拿名义上的 width：150% 缩放下窗口客户区会落成
                        // 1024.几，名义值 1024 的曲线已经到 1，而实际页宽还差一点没到最紧。
                        Require(view.Squeeze >= view.Narrow - 0.011,
                            $"{type} {width} 的收口比宽度曲线还松：{view.Squeeze:0.##} < {view.Narrow:0.##}"
                                + $"（页宽 {view.PageWidth:0.##}）");
                        Require(previousSqueeze < 0 || view.Squeeze >= previousSqueeze - 0.011,
                            $"{type} {width} 更窄了反而更松：{previousSqueeze:0.##} → {view.Squeeze:0.##}");
                        previousSqueeze = view.Squeeze;

                        if (width == 1280)
                        {
                            // 1280 是这一组里最宽的一档：宽度曲线已经起了头（约三成），但还没收到最紧 ——
                            // 带子比 352 矮、比 328 高，面板在 24 与 8 之间。1440 以上一分不收那一段在 Core
                            // 的单测里（副屏的逻辑宽只有 1280）。
                            Require(view.Squeeze > 0.25 && view.Squeeze < 0.5,
                                $"{type} 1280：这一档该收三成上下，实际 {view.Squeeze:0.##}");
                            Require(band.ActualHeight < 352 && band.ActualHeight > 328,
                                $"{type} 1280：带高该在 328 与 352 之间，实际 {band.ActualHeight:0.##}");
                            Require(pickers.Padding.Top < 24 && pickers.Padding.Top > DetailHero.TailPadTight,
                                $"{type} 1280：面板上下该在 8 与 24 之间，实际 {pickers.Padding.Top:0.##}");
                        }

                        if (width == 1024)
                        {
                            // 最紧那一档：带子 352 收到 328（头部两道留白 28／24 → 16／12），面板 24 → 8。
                            Require(Math.Abs(band.ActualHeight - 328) < 0.51,
                                $"{type} 1024：带高该收到 328，实际 {band.ActualHeight:0.##}");
                            Require(Math.Abs(pickers.Padding.Top - DetailHero.TailPadTight) < 0.01,
                                $"{type} 1024：面板上下该收到 8，实际 {pickers.Padding.Top:0.##}");
                        }

                        // 3) 剧情说明的下沿与背景图下沿：收得动的时候该留在画面里；真探出去时必须在最紧那一档，
                        //    不能是「还能收却没再收」。
                        var overviewBottom = overview.TransformToVisual(content)
                            .TransformPoint(new(0, overview.ActualHeight)).Y;
                        var pictureBottom = view.PictureHeight - view.HeaderLift;
                        var overflow = overviewBottom - pictureBottom;

                        if (overflow > 0.5)
                        {
                            Require(view.Squeeze >= 0.99,
                                $"{type} {width}：剧情说明探出画面 {overflow:0.#} 像素，可这一档只收了 {view.Squeeze:0.##}");
                        }

                        Require(double.IsNegativeInfinity(previousOverflow) || overflow >= previousOverflow - 1.5,
                            $"{type} {width}：更窄了，剧情说明反而离画面下沿更远（{previousOverflow:0.#} → {overflow:0.#}）");
                        previousOverflow = overflow;

                        readings.Add($"{type} {width}: 收 {view.Squeeze:0.##}，带 {band.ActualHeight:0.##}，"
                            + $"头部 {view.HeroInset.Top:0.##}／{view.HeroInset.Bottom:0.##}，"
                            + $"尾部 {view.TailInset.Top:0.##}／间距 {tail.Spacing:0.##}／面板 {pickers.Padding.Top:0.##}，"
                            + $"尾高 {tail.ActualHeight:0.##}，简介下沿 {overviewBottom:0.#}，画面下沿 {pictureBottom:0.#}，"
                            + $"差 {overflow:0.#}");

                        if (!metrics.TryGetValue(width, out var rows))
                        {
                            rows = [];
                            metrics[width] = rows;
                        }

                        rows.Add((band.ActualHeight, pickers.Padding.Top, tail.Spacing, view.HeroHeight));

                        await SaveProbeScreenAsync($"detail-responsive-{type}-{width}.png");
                    }

                    await ResponsiveShowcaseAsync(page);
                }
                finally
                {
                    (frame.Content as IShellContent)?.Release();
                    _root.Children.Remove(host);
                }
            }

            // 4) 三个页面在同一档上数字一致 —— 「请确保三个页面的表现一致」。
            foreach (var (width, rows) in metrics)
            {
                var first = rows[0];
                foreach (var row in rows)
                {
                    Require(Math.Abs(row.Band - first.Band) < 1.5
                            && Math.Abs(row.Pad - first.Pad) < 0.01
                            && Math.Abs(row.Spacing - first.Spacing) < 0.01
                            && Math.Abs(row.Height - first.Height) < 1.5,
                        $"{width} 上三页的表现不一致：{string.Join("；", rows.Select(row =>
                            $"带 {row.Band:0.##}／面板 {row.Pad:0.##}／间距 {row.Spacing:0.##}"))}");
                }
            }

            Require(fixture.Actions.Plays == 0, "布局检查意外请求播放");
        }
        finally
        {
            if (_window?.IsMaximized == true) _window.ToggleMaximize();
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-responsive.txt"), readings);
            ResizeInspect(1420, 980);
        }
    }

    /// <summary>
    /// 背景图快被裁掉时自动走无遮挡版式，并把那张图的不透明度从 0.9 抬到 0.96 ——
    /// 「当拉长窗口导致背景图接近被裁剪时，自动切换到最大化（无遮挡）页面状态」与「将页面背景图透明度略微
    /// 降低（更不透明）」。
    /// <para>
    /// 窗口 1080×620：页面头上让出 68（标题栏加面包屑），而客户区报回来的高带上那条 31 的自绘标题栏
    /// （651），所以视口是 651 − 68 = 583；16:9 的图按页宽铺开是 607.5 高，583 的 98% 是 571 —— 已经过线；
    /// 而这一扇窗口的面积离工作区五分之四还远，所以 Showcase 只可能是裁切那一条来路。收窄到 900×620 时图
    /// 只有 506.25 高，那条线松开，回到纸面版式。
    /// <para>
    /// 这两档都压在副屏的逻辑宽度（1080）以内，截下来是一整扇窗；再宽就只有左半边了。
    /// </para>
    /// </summary>
    private async Task ResponsiveShowcaseAsync(DetailPage page)
    {
        var picture = Get<Border>(page, "BackdropPicture");

        // 前一条用例若把窗口留在最大化上，ResizeClient 会被忽略 —— 先还原再量，免得把「窗口没动」当成版面没跟上。
        if (_window!.IsMaximized) _window.ToggleMaximize();
        await SettleAsync();

        await ResizeResponsiveAsync(page, 1080, 620, ResponsiveViewport);
        await LayoutAsync(page);
        Require(!page.ViewModel.Spacious, "这一档窗口已经铺开，测不出「只因裁切才切换」那条路");
        Require(page.ViewModel.NearCrop, "1080×620 上背景图该算「快被裁掉」");
        var state = $"裁切={page.ViewModel.NearCrop}、沉浸={page.ViewModel.Showcase}、铺开={page.ViewModel.Spacious}、"
            + $"紧凑={page.ViewModel.IsCompact}、有图={page.ViewModel.BackdropShown}、类型={page.ViewModel.ItemType}、"
            + $"页宽={page.ViewModel.PageWidth:0}、视口={page.ViewModel.Viewport:0}、图比={page.ViewModel.HeroHeightRatio:0.###}、"
            + $"不透明度={picture.Opacity:0.###}";
        Require(page.ViewModel.Showcase, "裁切那条线没有触发无遮挡版式：" + state);
        await UntilAsync(() => Math.Abs(picture.Opacity - DetailHero.ShowcaseBackdropOpacity) < 0.001);
        Require(Math.Abs(picture.Opacity - DetailHero.ShowcaseBackdropOpacity) < 0.001,
            $"裁切触发后背景图该是 {DetailHero.ShowcaseBackdropOpacity}，实际 {picture.Opacity:0.###}（{state}）");
        await SaveProbeScreenAsync("detail-responsive-crop-showcase.png");

        await ResizeResponsiveAsync(page, 900, 620, ResponsiveViewport);
        await LayoutAsync(page);
        Require(!page.ViewModel.NearCrop && !page.ViewModel.Showcase,
            $"900×620 上不该再算「快被裁掉」：裁切={page.ViewModel.NearCrop}、沉浸={page.ViewModel.Showcase}");
        Require(Math.Abs(picture.Opacity - DetailHero.BackdropOpacity) < 0.001,
            $"回到纸面版式后背景图该是 {DetailHero.BackdropOpacity}，实际 {picture.Opacity:0.###}");
        await SaveProbeScreenAsync("detail-responsive-crop-paper.png");

        // 最大化那一路（窗口铺开）也走同一档、同一支不透明度；还原后回到 0.9。
        if (!_window!.IsMaximized) _window.ToggleMaximize();
        await UntilAsync(() => page.ViewModel.Showcase
            && Math.Abs(picture.Opacity - DetailHero.ShowcaseBackdropOpacity) < 0.001);
        Require(Math.Abs(picture.Opacity - DetailHero.ShowcaseBackdropOpacity) < 0.001,
            $"最大化后背景图该是 {DetailHero.ShowcaseBackdropOpacity}，实际 {picture.Opacity:0.###}");
        await SaveProbeScreenAsync("detail-responsive-maximized.png");

        _window.ToggleMaximize();
        await UntilAsync(() => !page.ViewModel.Showcase
            && Math.Abs(picture.Opacity - DetailHero.BackdropOpacity) < 0.001);
        Require(Math.Abs(picture.Opacity - DetailHero.BackdropOpacity) < 0.001,
            $"还原后背景图该回到 {DetailHero.BackdropOpacity}，实际 {picture.Opacity:0.###}");
    }

    /// <summary>
    /// 这一条要的那个视口高。窗口客户区报回来是 651（620 加上那条 31 的自绘标题栏），页面在探针里又让出
    /// 68（标题栏加面包屑），所以滚动视图是 583；<c>ResizeProbeClient</c> 写 620 只是拿它当「希望的高度」，
    /// 真正落地的是客户区的最小高。
    /// </summary>
    private const double ResponsiveViewport = 583;

    /// <summary>
    /// 改窗口客户区并等页宽跟上。这台桌面上窗口改宽偶尔要第二次才生效（第一次的消息落在最大化
    /// 切换或前台切换中间），所以重试两次；两次都不动就是真失败，把窗宽、客户区、最大化与缩放
    /// 一起写进异常，别只留一句「未等到预期状态」。
    /// </summary>
    private async Task ResizeResponsiveAsync(DetailPage page, int width, int height, double viewport = 0)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (_window!.IsMaximized) _window.ToggleMaximize();

            ResizeProbeClient(width, height);
            var deadline = Environment.TickCount64 + 2500;
            while (Environment.TickCount64 < deadline)
            {
                page.UpdateLayout();
                var widthOk = Math.Abs(page.ViewModel.PageWidth - width) < 0.6;
                var viewportOk = viewport <= 0 || Math.Abs(page.ViewModel.Viewport - viewport) < 1.5;
                if (widthOk && viewportOk) return;
                await Task.Delay(16);
            }
        }

        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window!.Handle));
        var client = _window.ClientSize;
        throw new TimeoutException(
            $"窗口没到目标尺寸 {width}×{height}"
                + (viewport > 0 ? $"（视口 {viewport:0.#}）" : "")
                + $"：页宽 {page.ViewModel.PageWidth:0.#}、视口 {page.ViewModel.Viewport:0.#}、"
                + $"客户区 {client.Width:0.#}×{client.Height:0.#}、AppWindow {appWindow.Size.Width}×{appWindow.Size.Height}、"
                + $"最大化 {_window.IsMaximized}、缩放 {_root.XamlRoot?.RasterizationScale:0.##}");
    }

    /// <summary>
    /// 两个 <see cref="Thickness"/> 是不是同一个数（按 0.01 的容差）。2026-11-02 起窄窗的留白是插值出来的
    /// 小数（例如 23.68），绑到控件上会经过一次 float 转换，控件读回来是 23.68000030517578，而视图模型
    /// 手里是 23.68 —— 精确相等会把一条完全正常的绑定判红。
    /// </summary>
    private static bool Same(Thickness left, Thickness right) =>
        Math.Abs(left.Left - right.Left) < 0.01
        && Math.Abs(left.Top - right.Top) < 0.01
        && Math.Abs(left.Right - right.Right) < 0.01
        && Math.Abs(left.Bottom - right.Bottom) < 0.01;

    /// <summary>够长、够跨四行的简介 —— 窄窗下「剧情说明快掉出画面」那一条要的是一段真会折满的文字。</summary>
    private const string ResponsiveOverview =
        "为了查明充满谜团的有栖的真实身份，梅和绽决定偷偷观察她的日常生活。没想到两人趁机翻找有栖的随身物品时，"
        + "有栖竟然回来了！？这下该怎么办？两人慌忙躲进壁橱，却听见门外传来一段意料之外的对话。"
        + "第二天，学校里又多了一个知道秘密的人，而那个人的身份，恰恰是所有猜测里最不可能的那一个。";
}
