using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 「轮播图有bug，进度条经常会卡住不动，或者跑到一半回退，请修复」与「刚入库的资源如果没有背景图或可用于
    /// 轮播图的图片就不要放到轮播列表里」（2026-11-02 用户令）。
    /// <para>
    /// 直接驱动真实的 <see cref="HomeBanner"/>（不走 HomePage，减掉整页读取的等待）：读秒连续、悬停和焦点占着
    /// 也不停表、整批重读不把台上那张和进度推回第一张、取不到宽图的那张被摘掉且读数跟着收。离线夹具，不连真实
    /// 服务器、不播放、不改真实设置。
    /// </para>
    /// </summary>
    private async Task HomeBannerClockAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        ResizeProbeClient(1280, 900);

        var image = await FixtureImageAsync(1280, 720);
        var items = Enumerable.Range(0, 3).Select(index => new EmbyItem
        {
            Id = "clock-movie-" + index,
            Name = "读秒影片 " + (index + 1),
            Type = EmbyItemType.Movie,
            BackdropImageTags = ["backdrop"]
        }).ToArray();

        // 服务器发下来的标签说它有背景图，真去取却 404 —— 「刚入库、图还没生成」的那一类。
        var broken = new EmbyItem
        {
            Id = "clock-broken",
            Name = "没有宽图的影片",
            Type = EmbyItemType.Movie,
            BackdropImageTags = ["backdrop"]
        };

        foreach (var item in items.Append(broken)) fixture.Session.Capture().Bind(item);

        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
            {
                return Task.FromResult(path.Contains(broken.Id, StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });
            }

            return Task.FromResult(FakeTransport.Standard(request));
        };

        // 页面那一头的收尾：清单里也要少这一件（`HomePage` 把带子的 SlideDropped 交给这一句）。
        using (var home = new HomeViewModel())
        {
            home.Attach(fixture.Actions, [], fixture.SettingsService, fixture.Session, fixture.Images, fixture.Dispatcher);
            home.Slides = [new BannerSlide(broken, fixture.Images)];
            home.DropSlide(broken);
            Require(home.Slides.Count == 0, "视图模型清单没有收回取不到图的那一件");
        }

        var banner = new HomeBanner();
        _root.Children.Add(banner);

        var readings = new List<string>();
        var dropped = new List<string>();
        BannerSlide? opened = null;
        banner.OpenRequested += (_, slide) => opened = slide;
        banner.SlideDropped += (_, slide) => dropped.Add(slide.Item.Id);
        try
        {
            await LayoutAsync(banner);

            var slides = items.Select(item => new BannerSlide(item, fixture.Images)).ToArray();
            banner.Apply(slides);
            await WaitAsync(() => slides.All(slide => slide.Picture is not null), 8000);
            await LayoutAsync(banner);
            var open = Get<Button>(banner, "OpenButton");
            Require(open.Content is string { Length: > 0 } text && text == "详情"
                && open.ActualWidth > open.Padding.Left + open.Padding.Right + 2
                && Descendants(open).OfType<TextBlock>().Any(block => block.Text == "详情" && block.ActualWidth > 0),
                "首页详情按钮没有呈现文字");
            foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
            {
                Require(VisualStateManager.GoToState(open, state, false), "详情按钮缺少状态：" + state);
                await LayoutAsync(banner);
                var presenter = Descendants(open).OfType<ContentPresenter>().Single(part => part.Name == "ContentPresenter");
                Require(presenter.Foreground is SolidColorBrush ink && ink.Color.A == byte.MaxValue,
                    "详情按钮文字在 " + state + " 变透明");
            }
            VisualStateManager.GoToState(open, "Normal", false);
            ((IInvokeProvider)new ButtonAutomationPeer(open).GetPattern(PatternInterface.Invoke)).Invoke();
            await SettleAsync();
            Require(ReferenceEquals(opened, slides[0]) && fixture.Actions.Plays == 0,
                "首页详情按钮没有交给当前条目，或意外请求播放");
            await SaveProbeScreenAsync("home-banner-clock-screen.png");

            var scale = Get<ScaleTransform>(banner, "CountdownScale");
            var counter = Get<TextBlock>(banner, "SlideCounter");
            Require(counter.Text.StartsWith("01  /  03", StringComparison.Ordinal), $"首张读数不对：{counter.Text}");

            // 悬停和焦点都占着：从前这两样都会把钟停掉、把条清零。现在它们只管箭头。
            Field(banner, "_hover", true);
            Field(banner, "_focusWithin", true);
            banner.ApplyDwell(3);

            var samples = new List<double>();
            var picture = banner.FrontPicture;
            var cameraSamples = new List<string>();
            Require(HomeMotion.AnimationsEnabled, "静止轮播回归需要启用系统动画，避免旧慢缩放直接落定而误通过");
            var beforePicture = await ReadBannerRegionAsync(banner);
            for (var index = 0; index < 8; index++)
            {
                await Task.Delay(150);
                samples.Add(scale.ScaleX);
                var camera = (ScaleTransform)banner.FrontLayer.RenderTransform;
                cameraSamples.Add($"{camera.ScaleX:0.0000}/{camera.ScaleY:0.0000}");
                Require(ReferenceEquals(picture, banner.FrontPicture)
                    && Math.Abs(camera.ScaleX - 1) < 1e-6 && Math.Abs(camera.ScaleY - 1) < 1e-6,
                    "停留期间背景不得自动缩放：" + string.Join("、", cameraSamples));
            }
            readings.Add("背景自然停留，两轴单位缩放：" + string.Join("、", cameraSamples));
            var afterPicture = await ReadBannerRegionAsync(banner);
            Require(beforePicture.SequenceEqual(afterPicture), "自然停留期间右侧背景像素发生移动");
            readings.Add($"自然停留背景区域 {beforePicture.Length / 4} 像素完全一致");

            var rising = true;
            for (var index = 1; index < samples.Count; index++)
                if (samples[index] < samples[index - 1] - 1e-6) rising = false;

            Require(rising && samples[^1] > 0.1,
                $"指针和焦点在带上时进度条没有连续前进：{string.Join("、", samples.Select(value => $"{value:0.00}"))}");
            Require(counter.Text.StartsWith("01  /  03", StringComparison.Ordinal), "停留时长没到就换片了");
            readings.Add($"悬停＋焦点：进度 {samples[0]:0.00}→{samples[^1]:0.00}，读数仍是 {counter.Text}");

            // 跑满就翻页，条接着从 0 再走 —— 不是停在满格、也不是半路回退。
            await WaitAsync(() => counter.Text.StartsWith("02  /  03", StringComparison.Ordinal), 6000);
            Require(scale.ScaleX < 0.6, $"翻页之后进度条没有从头开始：{scale.ScaleX:0.00}");
            readings.Add($"3 秒到点：读数 {counter.Text}，条 {scale.ScaleX:0.00}");
            var nextCamera = (ScaleTransform)banner.FrontLayer.RenderTransform;
            Require(Math.Abs(nextCamera.ScaleX - 1) < 1e-6 && Math.Abs(nextCamera.ScaleY - 1) < 1e-6,
                "自动换图不得重启背景缩放");

            // 整页重读（新入库的定时检查、设置里别的行改了）会重建一批对象；还是同一批就该沿用台上那张，
            // 进度条不许回零。这里直接交一批新对象，走的正是 HomeViewModel 每次重读的那条路。
            var beforeRefresh = scale.ScaleX;
            var beforeRefreshText = counter.Text;
            var refreshed = items.Select(item => new BannerSlide(item, fixture.Images)).ToArray();
            banner.Apply(refreshed);
            await Task.Delay(180);
            Require(counter.Text == beforeRefreshText, $"整批重读把台上那张换走了（{beforeRefreshText}→{counter.Text}）");
            Require(scale.ScaleX >= beforeRefresh - 0.02,
                $"整批重读让进度条回退（{beforeRefresh:0.00}→{scale.ScaleX:0.00}）");
            readings.Add($"整批重读：读数仍 {counter.Text}，条 {beforeRefresh:0.00}→{scale.ScaleX:0.00}");

            // 保持当前那张，把取不到图的插到它后面（邻居会被预取、于是当场发现 404）：摘掉的是台下那张，
            // 台上那张、读数和钟都不该动。
            banner.ApplyDwell(30);
            await Task.Delay(220);
            var beforeDrop = scale.ScaleX;
            var currentId = banner.Current!.Item.Id;
            var brokenSlide = new BannerSlide(broken, fixture.Images);
            banner.Apply([slides[0], slides[1], brokenSlide, slides[2]]);
            await WaitAsync(() => dropped.Contains(broken.Id), 6000);

            Require(banner.Current?.Item.Id == currentId, "摘掉台下那张时台上那张被换走了");
            Require(counter.Text.StartsWith("02  /  03", StringComparison.Ordinal),
                $"清单没有收回被摘掉的那张：{counter.Text}");
            Require(scale.ScaleX >= beforeDrop - 0.02,
                $"摘掉台下那张让进度条回退了（{beforeDrop:0.00}→{scale.ScaleX:0.00}）");
            readings.Add($"坏图摘除：读数 {counter.Text}，条 {beforeDrop:0.00}→{scale.ScaleX:0.00}");

            // 一个能站住的都没有时整条带收起来，不是留一块黑底加几行字。
            banner.Apply([new BannerSlide(broken, fixture.Images)]);
            await WaitAsync(() => banner.Visibility == Visibility.Collapsed, 6000);
            Require(dropped.Count(id => id == broken.Id) == 2, $"坏图只报了一次：{string.Join('、', dropped)}");
            readings.Add("只剩坏图：整条带收起");

            Require(readings.Count == 7, $"轮播读秒与静止检查没有走完（{readings.Count} 条读数）");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "home-banner-clock.txt"), readings);
            _root.Children.Remove(banner);
            ResizeProbeClient(1420, 980);
        }
    }

    private static async Task<byte[]> ReadBannerRegionAsync(HomeBanner banner)
    {
        // 渲染父容器使图片层的变换进入结果；只取右侧背景，避开字块、箭头和倒计时。
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(Get<Grid>(banner, "Band"), 640, 360);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var left = (int)(bitmap.PixelWidth * 0.60);
        var top = (int)(bitmap.PixelHeight * 0.20);
        var width = (int)(bitmap.PixelWidth * 0.20);
        var height = (int)(bitmap.PixelHeight * 0.20);
        var region = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            pixels.AsSpan(((y + top) * bitmap.PixelWidth + left) * 4, width * 4)
                .CopyTo(region.AsSpan(y * width * 4));
        Require(region.Any(value => value != 0), "背景采样区域为空，不能证明静止");
        return region;
    }

    /// <summary>
    /// 等一件要在几秒后才发生的事（翻页、摘图）。<see cref="UntilAsync"/> 只等两秒，翻页那一档不够。
    /// </summary>
    private static async Task WaitAsync(Func<bool> ready, int timeoutMs)
    {
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < timeoutMs)
        {
            if (ready()) return;
            await Task.Delay(50);
        }

        throw new TimeoutException("未等到预期状态");
    }
}
