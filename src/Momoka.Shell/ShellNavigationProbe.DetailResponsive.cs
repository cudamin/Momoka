using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 三页宽窗沉浸与窄窗紧凑布局、播放前文件选项、单音轨文字和开关恢复。
    /// 缩放、最大化与还原只改变排版，不在宽窗中切换背景模式；只使用匿名假传输。
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
        fixture.Settings.Ui.EnableImmersive = true;
        fixture.Settings.Ui.UseGaussianBackdrop = false;
        fixture.Settings.Ui.UseAcrylicBackdrop = false;
        var widths = new[] { 1280, 1200, 1100, 1024, 1023, 760 };
        try
        {
            foreach (var item in new[] { movie, series, season })
            {
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
                    var target = page.ViewModel.PlayTarget;
                    foreach (var width in widths)
                    {
                        await ResizeResponsiveAsync(page, width, 900);
                        await LayoutAsync(page);
                        await UntilAsync(() =>
                        {
                            page.UpdateLayout();
                            var view = page.ViewModel;
                            return Same(Get<Grid>(page, "HeroContent").Margin, view.HeroInset)
                                && Same(Get<StackPanel>(page, "TailContent").Padding, view.TailInset)
                                && Same(Get<StackPanel>(page, "PickerPanel").Padding, view.TailPanelPadding)
                                && Same(Get<StackPanel>(page, "OverviewPanel").Padding, view.OverviewPanelPadding);
                        });
                        var view = page.ViewModel;
                        var compact = width < DetailHero.CompactFloor;
                        Require(view.IsCompact == compact && view.Showcase == !compact && view.BackdropShown,
                            $"{item.Type} {width}：开启时未保持宽窗沉浸或窄窗紧凑背景");
                        Require(!view.HeroCardShown, "开启时错误保留关闭后的宽窗卡面");
                        RequireResponsiveBindings(page);
                        var scroll = Get<ScrollView>(page, "Body");
                        var content = (UIElement)scroll.Content;
                        var options = Get<StackPanel>(page, compact ? "PickerPanel" : "ColumnPickers");
                        var actions = Get<StackPanel>(page, compact ? "CompactBlock" : "ColumnActions");
                        var optionsBottom = options.TransformToVisual(content).TransformPoint(new(0, options.ActualHeight)).Y;
                        var actionsTop = actions.TransformToVisual(content).TransformPoint(default).Y;
                        Require(options.Visibility == Visibility.Visible && optionsBottom <= actionsTop + 1,
                            $"{item.Type} {width} 文件选项没有排在播放前：{optionsBottom:0.#}/{actionsTop:0.#}");
                        Require(Get<ComboBox>(page, compact ? "AudioPicker" : "ColumnAudioPicker").Visibility == Visibility.Collapsed
                            && Get<TextBlock>(page, compact ? "AudioText" : "ColumnAudioText").Visibility == Visibility.Visible
                            && !string.IsNullOrWhiteSpace(Get<TextBlock>(page, compact ? "AudioText" : "ColumnAudioText").Text),
                            $"{item.Type} {width} 唯一音轨未保留文字或仍显示下拉");
                        Require(Get<Border>(page, "PosterStill").Visibility == view.StillVisibility,
                            $"{item.Type} {width} 封面显隐没有跟随宽窄排版");
                        Require(ReferenceEquals(target, view.PlayTarget), "缩放窗口改变了播放目标");
                        if (width == 1280)
                        {
                            // 修改一个真实布局值，确认绑定检查仍能拒绝错误界面。
                            var hero = Get<Grid>(page, "HeroContent");
                            var inset = hero.Margin;
                            try
                            {
                                hero.Margin = new Thickness(inset.Left + 12, inset.Top, inset.Right, inset.Bottom);
                                var rejected = false;
                                try { RequireResponsiveBindings(page); }
                                catch (InvalidOperationException) { rejected = true; }
                                Require(rejected, "响应式绑定检查没有拒绝错误头部留白");
                            }
                            finally { hero.Margin = inset; }
                        }
                        readings.Add($"{item.Type} {width}：模式={(compact ? "窄窗紧凑" : "宽窗沉浸")}，"
                            + $"带高={view.HeroHeight:0.##}、背景高={view.PictureHeight:0.##}、"
                            + $"尾部间距={view.TailSpacing:0.##}、正文间距={view.SheetSpacing:0.##}");
                        await SaveProbeScreenAsync($"detail-responsive-{item.Type}-{width}.png");
                    }
                    await ResponsiveModesAsync(page, fixture.Settings.Ui);
                }
                finally
                {
                    (frame.Content as IShellContent)?.Release();
                    _root.Children.Remove(host);
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

    private static void RequireResponsiveBindings(DetailPage page)
    {
        var view = page.ViewModel;
        Require(Same(Get<Grid>(page, "HeroContent").Margin, view.HeroInset), "头部留白没有同步绑定");
        var tail = Get<StackPanel>(page, "TailContent");
        Require(Same(tail.Padding, view.TailInset) && Math.Abs(tail.Spacing - view.TailSpacing) < 0.01,
            "尾部留白或竖距没有同步绑定");
        Require(Same(Get<StackPanel>(page, "PickerPanel").Padding, view.TailPanelPadding)
            && Same(Get<StackPanel>(page, "OverviewPanel").Padding, view.OverviewPanelPadding), "文件选项或简介内边距没有同步绑定");
        var sheet = Get<StackPanel>(page, "BodySheet");
        Require(Same(sheet.Padding, view.SheetPadding) && Math.Abs(sheet.Spacing - view.SheetSpacing) < 0.01,
            "正文留白或竖距没有同步绑定");
        Require(Math.Abs(Get<Grid>(page, "HeroBand").ActualHeight - view.HeroHeight) < 1.5, "头部带高没有按实际排版更新");
        Require((Get<Grid>(page, "Backdrop").Visibility == Visibility.Visible) == view.BackdropShown, "背景整层显隐没有同步绑定");
        if (view.BackdropShown)
            Require(Math.Abs(Get<Border>(page, "BackdropPicture").ActualHeight - view.PictureHeight) < 1.5,
                "实际背景盒高度没有同步绑定");
        Require(Get<StackPanel>(page, "ColumnPickers").Visibility == view.ColumnPickersVisibility
            && Get<StackPanel>(page, "PickerPanel").Visibility == view.PickersVisibility, "宽窄文件选项容器没有同步显隐");
    }

    private async Task ResponsiveModesAsync(DetailPage page, UiSettings ui)
    {
        var target = page.ViewModel.PlayTarget;
        foreach (var height in new[] { 620, 1120 })
        {
            await ResizeResponsiveAsync(page, 1080, height);
            await LayoutAsync(page);
            Require(page.ViewModel.Showcase && !page.ViewModel.IsCompact && page.ViewModel.BackdropShown,
                "改变宽窗高度切换了沉浸模式");
            RequireResponsiveBindings(page);
            Require(ScrimAlphaOf(page) == ScrimExpected(page), "改变宽窗高度改变了背景遮罩浓度");
            await SaveProbeScreenAsync($"detail-responsive-{page.ViewModel.ItemType}-height-{height}.png");
        }
        if (!_window!.IsMaximized) _window.ToggleMaximize();
        await UntilAsync(() => _window.IsMaximized && page.ViewModel.Showcase);
        await LayoutAsync(page);
        RequireResponsiveBindings(page);
        Require(ScrimAlphaOf(page) == ScrimExpected(page), "最大化改变了背景遮罩浓度");
        await SaveProbeScreenAsync($"detail-responsive-{page.ViewModel.ItemType}-maximized.png");
        _window.ToggleMaximize();
        await UntilAsync(() => !_window.IsMaximized && Math.Abs(page.ViewModel.PageWidth - 1080) < 1);
        await LayoutAsync(page);
        Require(page.ViewModel.Showcase, "还原宽窗退出了沉浸模式");
        RequireResponsiveBindings(page);
        foreach (var width in new[] { 1200, 760 })
        {
            await ResizeResponsiveAsync(page, width, 900);
            ui.EnableImmersive = false;
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
            Require(!page.ViewModel.BackdropShown && !page.ViewModel.Showcase
                && page.ViewModel.HeroCardShown == (width >= DetailHero.CompactFloor),
                "关闭沉浸后没有使用宽窗卡面或仍铺背景");
            Require(page.ViewModel.TailMinHeight == 0, "关闭沉浸仍留有背景占位");
            RequireResponsiveBindings(page);
            await SaveProbeScreenAsync($"detail-responsive-{page.ViewModel.ItemType}-disabled-{width}.png");
            ui.EnableImmersive = true;
            ShellPrefs.ApplyAppearance(ui);
            await LayoutAsync(page);
            Require(page.ViewModel.BackdropShown && page.ViewModel.Showcase == (width >= DetailHero.CompactFloor)
                && !page.ViewModel.HeroCardShown, "恢复沉浸没有恢复宽窄排版与背景图");
            RequireResponsiveBindings(page);
            Require(ReferenceEquals(target, page.ViewModel.PlayTarget), "开关切换或最大化改变了播放目标");
        }
    }

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
