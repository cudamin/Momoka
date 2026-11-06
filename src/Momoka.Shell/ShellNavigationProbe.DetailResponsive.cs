using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 三页宽窄留白、播放前文件选项和单音轨文字；剧页参考线与真实集卡副标题下沿核对，
    /// 再验证三页相同规则的进入/退出、动画及最大化。只使用匿名假传输。
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
        series.People = [new() { Id = "person", Name = "离线导演", Type = "Director" }];
        series.BackdropImageTags.Add("fixture");
        series.ImageTags["Primary"] = "fixture";
        series.ImageTags["Art"] = "fixture";

        var season = Season("a");
        season.Overview = ResponsiveOverview;
        season.People = [new() { Id = "person", Name = "离线导演", Type = "Director" }];
        season.BackdropImageTags.Add("fixture");
        season.ImageTags["Primary"] = "fixture";
        season.ImageTags["Art"] = "fixture";

        var movie = new EmbyItem
        {
            Id = "movie",
            Name = "离线测试电影 · 窄窗响应",
            Type = EmbyItemType.Movie,
            Overview = ResponsiveOverview,
            People = [new() { Id = "person", Name = "离线导演", Type = "Director" }],
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
            if (path.EndsWith("/Similar", StringComparison.Ordinal)) return Task.FromResult(Items(2, 2, "similar"));
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
                        await ResizeResponsiveAsync(page, width, 1120);
                        await LayoutAsync(page);

                        var view = page.ViewModel;
                        // Squeeze 的一支读的是上一帧量到的 TailIntrinsic；它更新后 x:Bind 还要再过一次布局
                        // 才落到控件上。等控件自己那一份追上视图模型再断言 —— 一帧的滞后不是绑定断了，但
                        // 追不上仍然是红。
                        for (var settle = 0; settle < 20; settle++)
                        {
                            page.UpdateLayout();
                            if (Math.Abs(view.PageWidth - width) < 0.6 && Same(pickers.Padding, view.TailPanelPadding)
                                && Same(overview.Padding, view.OverviewPanelPadding)
                                && Same(tail.Padding, view.TailInset)
                                && Same(heroContent.Margin, view.HeroInset))
                                break;
                            await Task.Delay(16);
                        }

                        Require(Math.Abs(view.PageWidth - width) < 0.6, $"窗口离开了目标宽度：{view.PageWidth}/{width}");
                        Require(!view.IsCompact && !view.Showcase,
                            $"{type} {width}：这一档该是纸面宽版式，实际 紧凑={view.IsCompact}、沉浸={view.Showcase}");

                        var options = Get<StackPanel>(page, "ColumnPickers");
                        var actions = Get<StackPanel>(page, "ColumnActions");
                        var optionsBottom = options.TransformToVisual(content).TransformPoint(new(0, options.ActualHeight)).Y;
                        var actionsTop = actions.TransformToVisual(content).TransformPoint(default).Y;
                        Require(options.Visibility == Visibility.Visible && optionsBottom <= actionsTop + 1,
                            $"{type}选择框没有排在播放前：{optionsBottom:0.#}/{actionsTop:0.#}");
                        Require(Get<ComboBox>(page, "ColumnAudioPicker").Visibility == Visibility.Collapsed
                            && Get<TextBlock>(page, "ColumnAudioText").Visibility == Visibility.Visible
                            && !string.IsNullOrWhiteSpace(Get<TextBlock>(page, "ColumnAudioText").Text),
                            $"{type}唯一音轨应保留文字音频栏并隐藏下拉");
                        if (type == EmbyItemType.Series)
                        {
                            var subtitle = Descendants(Get<StackPanel>(page, "EpisodePanel")).OfType<TextBlock>()
                                .First(text => text.Name == "SubLine" && text.ActualHeight > 0);
                            var actualBottom = subtitle.TransformToVisual(content).TransformPoint(new(0, subtitle.ActualHeight)).Y;
                            Require(Math.Abs(actualBottom - view.SeriesContentBottom) < 3,
                                $"剧页参考触发线偏离实际集数/时长：{view.SeriesContentBottom:0.#}/{actualBottom:0.#}，"
                                + $"head={Get<ShelfHead>(page, "EpisodeHead").ActualHeight:0.##}，sub={subtitle.ActualHeight:0.##}，"
                                + $"title={Descendants(Get<StackPanel>(page, "EpisodePanel")).OfType<TextBlock>().First(t => t.Name == "TitleLine").ActualHeight:0.##}，"
                                + $"sheet={Get<StackPanel>(page, "BodySheet").TransformToVisual(content).TransformPoint(default).Y:0.##}，"
                                + $"subInStrip={subtitle.TransformToVisual(Get<ShelfStrip>(page, "EpisodeStrip")).TransformPoint(new(0, subtitle.ActualHeight)).Y:0.##}");
                        }

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
                        var posterControl = Get<Border>(page, "PosterStill");
                        var prose = Get<TextBlock>(page, "Overview");
                        var posterLeft = posterControl.TransformToVisual(content).TransformPoint(default).X;
                        var proseLeft = prose.TransformToVisual(content).TransformPoint(default).X;
                        var heroTop = heroContent.TransformToVisual(content).TransformPoint(default).Y;
                        Require(Math.Abs(posterLeft - proseLeft) < 1 && Math.Abs(posterLeft - 32) < 1,
                            $"{type} {width} 剧情说明未与封面左沿对齐：封面 {posterLeft:0.##}／说明 {proseLeft:0.##}");
                        var sheet = Get<StackPanel>(page, "BodySheet");
                        foreach (var panel in sheet.Children.OfType<StackPanel>().Where(panel => panel.Visibility == Visibility.Visible))
                        {
                            var head = panel.Children.OfType<ShelfHead>().First();
                            var left = head.TransformToVisual(content).TransformPoint(default).X;
                            Require(Math.Abs(left - posterLeft) < 1,
                                $"{type} {width} 下方{panel.Name}未与封面对齐：{left:0.##}/{posterLeft:0.##}");
                        }
                        Require(Math.Abs(heroTop - 20) < 1,
                            $"{type} {width} 头部未保持面包屑等距留白：顶 {heroTop:0.##}");
                        var posterTop = posterControl.TransformToVisual(content).TransformPoint(default).Y;
                        Require(posterTop >= -0.5 && heroTop >= -0.5,
                            $"{type} {width} 上移后内容超出页面顶边：封面 {posterTop:0.##}／头部 {heroTop:0.##}");

                        if (width == 1280)
                        {
                            // 还原旧位置，确认实际几何检查拒绝原布局，再恢复并保存两张真实屏幕。
                            var finalInset = heroContent.Margin;
                            var heroLayout = Get<Grid>(page, "HeroLayout");
                            var finalLayoutHeight = heroLayout.Height;
                            var finalBandHeight = band.Height;
                            var coverButton = Get<Button>(page, "CoverButton");
                            var finalAlignment = coverButton.VerticalAlignment;
                            try
                            {
                                var oldInset = DetailHero.Between(12, 0, view.Squeeze);
                                var delta = oldInset - finalInset.Top;
                                band.Height = finalBandHeight + delta;
                                heroLayout.Height = finalLayoutHeight + delta;
                                heroContent.Margin = new Thickness(44, oldInset, 76, finalInset.Bottom);
                                coverButton.VerticalAlignment = VerticalAlignment.Center;
                                await LayoutAsync(page);
                                var oldLeft = posterControl.TransformToVisual(content).TransformPoint(default).X;
                                var oldTop = heroContent.TransformToVisual(content).TransformPoint(default).Y;
                                Require(Math.Abs(oldLeft - proseLeft - 12) < 1 && Math.Abs(oldTop - heroTop - delta) < 1,
                                    $"{type} 上一版布局反例没有还原44左沿与旧顶边：{oldLeft:0.##}／{oldTop:0.##}");
                                await SaveProbeScreenAsync($"detail-position-{type}-before.png");
                            }
                            finally
                            {
                                heroContent.Margin = finalInset;
                                heroLayout.Height = finalLayoutHeight;
                                band.Height = finalBandHeight;
                                coverButton.VerticalAlignment = finalAlignment;
                            }
                            await LayoutAsync(page);
                            Require(Math.Abs(posterControl.TransformToVisual(content).TransformPoint(default).X - proseLeft) < 1,
                                $"{type} 旧位置反例后未恢复对齐");
                            await SaveProbeScreenAsync($"detail-position-{type}-after.png");
                        }

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
                            // 面板在24与8之间，顶部等距留白固定；1440以上不收底部的那段在Core。
                            // 的单测里（副屏的逻辑宽只有 1280）。
                            Require(view.Squeeze > 0.25 && view.Squeeze < 0.5,
                                $"{type} 1280：这一档该收三成上下，实际 {view.Squeeze:0.##}");
                            Require(Math.Abs(band.ActualHeight - DetailHero.WideHeight(view.HeroContent, view.HeroInset.Top, view.HeroInset.Bottom)) < 1.5,
                                $"{type} 1280：头部应按内容和收口留白计算，实际 {band.ActualHeight:0.##}");
                            Require(pickers.Padding.Top < 24 && pickers.Padding.Top > DetailHero.TailPadTight,
                                $"{type} 1280：面板上下该在 8 与 24 之间，实际 {pickers.Padding.Top:0.##}");
                        }

                        if (width == 1024)
                        {
                            // 最紧那一档：顶部20固定，底部24→12，面板24→8。
                            Require(Math.Abs(band.ActualHeight - DetailHero.WideHeight(view.HeroContent, 20, 12)) < 1.5,
                                $"{type} 1024：头部留白应为20/12，实际 {band.ActualHeight:0.##}");
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

                        var mode = view.IsCompact ? "窄窗" : view.Showcase ? "大窗" : "正常";
                        readings.Add($"{type} {width}: 模式={mode}，收 {view.Squeeze:0.##}，带 {band.ActualHeight:0.##}，"
                            + $"头部 {view.HeroInset.Top:0.##}／{view.HeroInset.Bottom:0.##}，"
                            + $"尾部 {view.TailInset.Top:0.##}／间距 {tail.Spacing:0.##}／面板 {pickers.Padding.Top:0.##}，"
                            + $"尾高 {tail.ActualHeight:0.##}，简介下沿 {overviewBottom:0.#}，画面下沿 {pictureBottom:0.#}，"
                            + $"差 {overflow:0.#}");
                        readings.Add($"{type} {width}: 封面左={posterLeft:0.##}，剧情左={proseLeft:0.##}，头部顶={heroTop:0.##}");

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
                    Require(Math.Abs(row.Pad - first.Pad) < 0.01
                            && Math.Abs(row.Spacing - first.Spacing) < 0.01,
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

    /// <summary>1080宽度下缩短/拉高窗口，验证内容触发及退出动画，再验证最大化与还原。</summary>
    private async Task ResponsiveShowcaseAsync(DetailPage page)
    {
        // 前一条用例若把窗口留在最大化上，ResizeClient 会被忽略 —— 先还原再量，免得把「窗口没动」当成版面没跟上。
        if (_window!.IsMaximized) _window.ToggleMaximize();
        await SettleAsync();

        // 「所有路径统一动画」（2026-11-02 用户令）：拉宽触发裁切这条从前是瞬间换版，现在也要走过渡。
        // 屏上一个已经结束的动画看不出来，只有页面自己数的次数分得清「走过」和「直接落定」。
        await ResizeResponsiveAsync(page, 1080, 1120);
        await LayoutAsync(page);
        Require(!page.ViewModel.Showcase, "拉高窗口后应退出内容裁切触发的沉浸模式");

        var animationsBeforeEnter = page.ShowcaseAnimations;

        await ResizeResponsiveAsync(page, 1080, 620, ResponsiveViewport);
        await LayoutAsync(page);
        Require(!page.ViewModel.Spacious, "这一档窗口已经铺开，测不出「只因裁切才切换」那条路");
        Require(page.ViewModel.NearCrop, "1080×620 上剧集文字参考线应接近裁切");
        if (HomeMotion.AnimationsEnabled)
        {
            Require(page.ShowcaseAnimations > animationsBeforeEnter,
                "拉宽触发裁切时没有走过渡动画（ShowcaseAnimations 没有增加）");
        }

        var state = $"模式={(page.ViewModel.IsCompact ? "窄窗" : page.ViewModel.Showcase ? "大窗" : "正常")}、"
            + $"裁切={page.ViewModel.NearCrop}、沉浸={page.ViewModel.Showcase}、铺开={page.ViewModel.Spacious}、"
            + $"紧凑={page.ViewModel.IsCompact}、有图={page.ViewModel.BackdropShown}、类型={page.ViewModel.ItemType}、"
            + $"页宽={page.ViewModel.PageWidth:0}、视口={page.ViewModel.Viewport:0}、图比={page.ViewModel.HeroHeightRatio:0.###}、"
            + $"遮罩={ScrimAlphaOf(page)}";
        Require(page.ViewModel.Showcase, "裁切那条线没有触发无遮挡版式：" + state);
        await UntilAsync(() => !page.ShowcaseAnimating);
        // 2026-11-06 定稿：滑块管的是图上那层遮罩，翻面不改它 —— 两种版式画出同一个 alpha。
        Require(ScrimAlphaOf(page) == ScrimExpected(page),
            $"裁切触发后遮罩不是设置里那一档：实际 {ScrimAlphaOf(page)}、该是 {ScrimExpected(page)}（{state}）");
        await SaveProbeScreenAsync("detail-responsive-crop-showcase.png");

        var animationsBeforeExit = page.ShowcaseAnimations;

        await ResizeResponsiveAsync(page, 1080, 1120);
        await LayoutAsync(page);
        Require(!page.ViewModel.NearCrop && !page.ViewModel.Showcase,
            $"1080×1120 上不该再算「快被裁掉」：裁切={page.ViewModel.NearCrop}、沉浸={page.ViewModel.Showcase}");
        if (HomeMotion.AnimationsEnabled)
        {
            Require(page.ShowcaseAnimations > animationsBeforeExit,
                "拉高退出大窗时没有走过渡动画（ShowcaseAnimations 没有增加）");
        }

        // 过渡收尾也读同一档：动画停下不能把遮罩写回某种旧值。
        await UntilAsync(() => !page.ShowcaseAnimating);
        Require(ScrimAlphaOf(page) == ScrimExpected(page),
            $"回到纸面版式后遮罩不是设置里那一档：实际 {ScrimAlphaOf(page)}、该是 {ScrimExpected(page)}");
        await SaveProbeScreenAsync("detail-responsive-crop-paper.png");

        // 最大化那一路（窗口铺开）走同一档，浓度照旧不动。
        var animationsBeforeMaximize = page.ShowcaseAnimations;
        if (!_window!.IsMaximized) _window.ToggleMaximize();
        await UntilAsync(() => page.ViewModel.Showcase && !page.ShowcaseAnimating);
        Require(ScrimAlphaOf(page) == ScrimExpected(page),
            $"最大化后遮罩不是设置里那一档：实际 {ScrimAlphaOf(page)}、该是 {ScrimExpected(page)}");
        if (HomeMotion.AnimationsEnabled)
        {
            Require(page.ShowcaseAnimations > animationsBeforeMaximize,
                "最大化进入大窗时没有走过渡动画（ShowcaseAnimations 没有增加）");
        }

        await SaveProbeScreenAsync("detail-responsive-maximized.png");
        var maximizedSheet = Get<StackPanel>(page, "BodySheet");
        foreach (var panel in maximizedSheet.Children.OfType<StackPanel>().Where(panel => panel.Visibility == Visibility.Visible))
        {
            var head = panel.Children.OfType<ShelfHead>().First();
            Require(Math.Abs(head.TransformToVisual(page).TransformPoint(default).X - 32) < 1,
                $"最大化下方{panel.Name}未与封面对齐");
        }

        var animationsBeforeRestore = page.ShowcaseAnimations;
        _window.ToggleMaximize();
        await UntilAsync(() => !page.ViewModel.Showcase && !page.ShowcaseAnimating);
        Require(ScrimAlphaOf(page) == ScrimExpected(page),
            $"还原后遮罩不是设置里那一档：实际 {ScrimAlphaOf(page)}、该是 {ScrimExpected(page)}");
        if (HomeMotion.AnimationsEnabled)
        {
            Require(page.ShowcaseAnimations > animationsBeforeRestore,
                "从最大化还原时没有走过渡动画（ShowcaseAnimations 没有增加）");
        }
    }

    /// <summary>
    /// 这一条要的那个视口高：客户区 620 让出 68（自绘标题栏加面包屑）就是 552。
    /// <para>
    /// 2026-11-05 22:13 起 <c>ResizeProbeClient</c> 按真实非客户区差值设窗口，<c>ResizeProbeClient(1080, 620)</c>
    /// 落地的就是客户区 620（此前按 <c>AppWindow.ResizeClient</c> 会被系统标题栏重复加 31，落成 651）。这里的
    /// 期望值跟着改成 552 —— 不改的话尺寸本身是对的、断言却在等一个再也到不了的数。
    /// </para>
    /// </summary>
    private const double ResponsiveViewport = 552;

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
