using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task DetailMicaAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var savedTheme = ThemeHost.Current.Id;
        var savedAppearance = fixture.Settings.Ui.EnableImmersive;
        var picture = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync(1600, 900);
        var items = new[] { Series(), Season("a"), Episode("a"), new EmbyItem { Id = "mica-movie", Type = EmbyItemType.Movie } };
        foreach (var item in items)
        {
            item.Name = "云母详情 · " + item.Type;
            item.Overview = "核对简洁详情页的系统云母、信息卡、主题切换与导航恢复。";
            item.ImageTags["Primary"] = "fixture";
            item.BackdropImageTags.Add("fixture");
            item.ImageTags["Art"] = "fixture";
        }
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(path.Contains("/Images/Primary", StringComparison.Ordinal) ? picture : backdrop) });
            var item = items.FirstOrDefault(candidate => path.EndsWith("/Items/" + candidate.Id, StringComparison.Ordinal));
            return Task.FromResult(item is null ? FakeTransport.Standard(request) : Json(item));
        };
        fixture.Settings.Ui.EnableImmersive = false;
        ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        var reads = new List<string>();
        try
        {
            if (_window!.IsMaximized) _window.ToggleMaximize();
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            foreach (var item in items)
            {
                shell.OpenDetail(DetailRequest.For(fixture.Services, item));
                var page = (DetailPage)Get<Frame>(shell, "ContentFrame").Content;
                // Fixture DI uses a rejecting action sink; connect the real shell only for its title paint.
                Field(page, "_actions", shell);
                page.AttachWindow(_window);
                await UntilAsync(() => page.IsReady);
                // 数据完成可能早于真实 Loaded；必须等页面接上外观观察，不能把构造期按钮刷当最终状态。
                await UntilAsync(() => Get<bool>(page, "_appearanceObserved"));
                await UntilAsync(() => page.ViewModel.HeroImage is not null);
                if (item.Type != EmbyItemType.Season)
                    await UntilAsync(() => page.ViewModel.CornerImage is not null);
                foreach (var width in new[] { 1200, 760 })
                {
                    await ResizeResponsiveAsync(page, width, 900);
                    RequireStoreComponents(page, shell);
                    // 简洁模式的按钮与闭合选择框吃那支固定面色（2026-11-11 用户令）：先核四态画笔，
                    // 再把按钮材质那几项改到另一个极端，证明它一个字节都不读。
                    var actions = new[] { Get<StackPanel>(page, "ColumnActions"), Get<StackPanel>(page, "CompactBlock") }
                        .SelectMany(group => group.Children).OfType<WrapRow>()
                        .SelectMany(row => row.Children).OfType<Control>()
                        .Where(control => control is Button or ToggleButton && control.Visibility == Visibility.Visible)
                        .ToArray();
                    if (width == 1200)
                    {
                        Require(actions.Length > 0, "简洁详情没有找到操作按钮");
                        RequireFixedSurface(actions);
                        var saved = (fixture.Settings.Ui.UseAcrylicControls, fixture.Settings.Ui.ControlTintOpacity,
                            fixture.Settings.Ui.ControlTintLuminosityOpacity, fixture.Settings.Ui.ControlBackgroundOpacity,
                            fixture.Settings.Ui.ControlBackgroundColor);
                        try
                        {
                            fixture.Settings.Ui.UseAcrylicControls = false;
                            fixture.Settings.Ui.ControlBackgroundOpacity = 100;
                            fixture.Settings.Ui.ControlTintOpacity = 90;
                            fixture.Settings.Ui.ControlTintLuminosityOpacity = 5;
                            fixture.Settings.Ui.ControlBackgroundColor = "#FF00FF";
                            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                            await LayoutAsync(page);
                            RequireFixedSurface(actions);
                        }
                        finally
                        {
                            (fixture.Settings.Ui.UseAcrylicControls, fixture.Settings.Ui.ControlTintOpacity,
                                fixture.Settings.Ui.ControlTintLuminosityOpacity, fixture.Settings.Ui.ControlBackgroundOpacity,
                                fixture.Settings.Ui.ControlBackgroundColor) = saved;
                            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                            await LayoutAsync(page);
                        }
                    }
                    foreach (var theme in UiThemes.All)
                    {
                        ThemeHost.Apply(theme.Id);
                        await LayoutAsync(shell);
                        // 顶部与页面共用窗口基底；内容卡只叠系统层色，不能回到局部 Mica 或不透明卡底。
                        var host = Get<Grid>(shell, "ContentHost");
                        RequireSharedDetailContentLayer(page);
                        Require(host.Background is SolidColorBrush { Color.A: 0x99 }, "外壳画布不是那层 60% 的窗口色");
                        Require(Get<Border>(page, "BodyPaper").Background is SolidColorBrush { Color.A: 0 }, "正文实色挡住了窗口画布");
                        Require(!page.ViewModel.BackdropShown && Get<StackPanel>(page, "HeroTail").Background is null, "简洁详情仍铺图或黑色尾部");
                        RequireStoreBackdrop(page);
                        Require(shell.TrailBase is null && shell.TitleStripNow == TitleStrip.PagePainted, "面包屑实色挡住了窗口画布");
                        Require(Get<Border>(page, "PlainHeroSurface").Opacity == 0
                            && Get<Border>(page, "HeroScrim").Opacity == 0, "旧顶部渐变挡住了窗口画布");
                        var paper = (SolidColorBrush)Get<Border>(page, "BodyPaper").Background;
                        var original = paper.Color;
                        try
                        {
                            paper.Color = Microsoft.UI.Colors.Black;
                            var rejected = false;
                            try { Require(paper.Color.A == 0, "正文实色挡住了窗口画布"); }
                            catch (InvalidOperationException) { rejected = true; }
                            Require(rejected, "画布检查没有拒绝旧实色正文");
                        }
                        finally { paper.Color = original; }
                        var top = Get<Border>(page, "HeroCardTop");
                        var savedBackground = top.Background;
                        try
                        {
                            top.Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
                            var rejected = false;
                            try { RequireSharedDetailContentLayer(page); }
                            catch (InvalidOperationException) { rejected = true; }
                            Require(rejected, "内容卡检查没有拒绝旧不透明背景");
                        }
                        finally { top.Background = savedBackground; }
                        reads.Add($"{item.Type}/{width}/{theme.Id}: canvas + {(page.ViewModel.HeroCardShown ? "content layer" : "—")}, "
                            + $"body=transparent");
                        await SaveProbeScreenAsync($"detail-mica-{item.Type}-{width}-{theme.Id}-screen.png");
                    }
                }
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                // 前一轮停在760窄窗，先恢复宽窗，实际进入沉浸后才检验资源回调。
                await ResizeResponsiveAsync(page, 1200, 900);
                Require(page.ViewModel.Showcase, "回调检查未进入有图宽窗沉浸模式");
                Require(Get<Grid>(page, "StoreBackdrop").Visibility == Visibility.Collapsed,
                    "恢复沉浸后商店背景仍然叠加");
                Require(ReferenceEquals(Get<StackPanel>(page, "ColumnActions").Parent, Get<StackPanel>(page, "HeroStack"))
                    && ReferenceEquals(Get<StackPanel>(page, "FileOptions").Parent, Get<StackPanel>(page, "HeroStack")),
                    "恢复沉浸后操作和文件选项没有回到原位置");
                Require(!Get<StoreBackdropFade>(page, "_storeBackdropFade").IsActive,
                    "恢复沉浸后商店背景仍在持续采样");
                Require(page.ViewModel.CornerVisibility == (item.Type == EmbyItemType.Season ? Visibility.Collapsed : Visibility.Visible),
                    "恢复沉浸后右上角艺术图未恢复");
                Require(!DetailHasLocalBackdrop(page), "沉浸恢复后出现局部系统材质");
                Require(!ReferenceEquals(Get<Border>(page, "HeroCardTop").Background,
                    Get<Border>(page, "HeroContentLayerSource").Background), "沉浸恢复后仍使用简洁内容层");
                if (page.ViewModel.Showcase)
                {
                    // 单独触发顶部资源回调，拒绝它把沉浸信息卡覆盖成透明正文刷的旧路径。
                    var source = Get<Border>(page, "HeroContentLayerSource");
                    var savedLayer = source.Background;
                    try
                    {
                        source.Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
                        Require(ReferenceEquals(Get<StackPanel>(page, "InfoPanel").Background,
                            Get<Border>(page, "CardMaterialSource").Background), "顶部主题回调覆盖了沉浸信息卡背景");
                    }
                    finally { source.Background = savedLayer; }
                }
                fixture.Settings.Ui.EnableImmersive = false;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                RequireSharedDetailContentLayer(page);
                RequireStoreBackdrop(page);
                var scroll = Get<ScrollView>(page, "Body");
                var scrollStart = storeTop(page);
                await ScrollProbeAsync(scroll, Math.Min(160, scroll.ScrollableHeight));
                await LayoutAsync(page);
                Require(Math.Abs(storeTop(page) - scrollStart + scroll.VerticalOffset) < 2,
                    "商店背景未随页面滚动上移");
                await ScrollProbeAsync(scroll, 0);
                var heroImage = page.ViewModel.HeroImage;
                var store = Get<Grid>(page, "StoreBackdrop");
                try
                {
                    page.ViewModel.HeroImage = null;
                    await LayoutAsync(page);
                    Require(store.Visibility == Visibility.Collapsed, "商店背景缺图后未收起");
                    Require(!Get<StoreBackdropFade>(page, "_storeBackdropFade").IsActive,
                        "商店背景缺图后未释放组合资源");
                }
                finally { page.ViewModel.HeroImage = heroImage; }
                await LayoutAsync(page);
                RequireStoreBackdrop(page);
                try
                {
                    store.Visibility = Visibility.Collapsed;
                    var rejected = false;
                    try { RequireStoreBackdrop(page); }
                    catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "商店背景检查没有拒绝错误的隐藏状态");
                }
                finally { store.Visibility = Visibility.Visible; }
                page.Release();
                Require(!DetailHasLocalBackdrop(page), "离页后仍有局部系统材质");
            }
        }
        finally
        {
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
            fixture.Settings.Ui.EnableImmersive = savedAppearance;
            ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
            ThemeHost.Apply(savedTheme);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "detail-mica.txt"), reads);
        }

        static double storeTop(DetailPage page) => Get<Grid>(page, "StoreBackdrop").TransformToVisual(page).TransformPoint(default).Y;
    }

    private static void RequireStoreComponents(DetailPage page, ShellPage shell)
    {
        var details = Get<StackPanel>(page, "StoreHeroDetails");
        var actions = Get<StackPanel>(page, "ColumnActions");
        var options = Get<StackPanel>(page, "FileOptions");
        Require(ReferenceEquals(actions.Parent, Get<StackPanel>(page, "HeroStack")) && ReferenceEquals(options.Parent, details),
            "商店操作没有在封面右侧或文件信息没有在下方");
        double Top(FrameworkElement element) => element.TransformToVisual(page).TransformPoint(default).Y;
        var cover = Get<Border>(page, "PosterStill");
        if (actions.Visibility == Visibility.Visible && cover.Visibility == Visibility.Visible)
            Require(actions.TransformToVisual(page).TransformPoint(default).X >= cover.TransformToVisual(page).TransformPoint(default).X + cover.ActualWidth + 8, "商店操作没有位于封面右侧");
        if (options.Visibility == Visibility.Visible)
            Require(Top(options) >= Top(cover) + cover.ActualHeight && Top(options) >= Top(actions) + actions.ActualHeight + 4,
                "商店文件信息没有排在封面和操作之后");
        var band = Get<Grid>(page, "HeroBand");
        Require(Top(band) + band.ActualHeight >= Top(options) + options.ActualHeight - 1, "商店头部裁切文件信息");
        Require(Get<Button>(page, "ColumnTrailerButton").Visibility == Visibility.Collapsed
            && Get<Button>(page, "CompactTrailerButton").Visibility == Visibility.Collapsed, "商店仍显示预告片入口");
        Require(Get<Grid>(shell, "TrailBar").Visibility == Visibility.Collapsed, "商店仍显示媒体路径栏");
    }

    private static void RequireSharedDetailContentLayer(DetailPage page)
    {
        Require(!DetailHasLocalBackdrop(page), "详情页又创建了局部系统材质");
        foreach (var name in new[] { "HeroCardTop", "HeroCardBottom" })
        {
            var card = Get<Border>(page, name);
            Require(card.Background is null && card.Visibility == Visibility.Collapsed,
                "商店顶部仍用大卡片遮挡背景");
            Require(card.Child is null, "顶部卡面仍叠了子材质层");
        }
    }

    private static void RequireStoreBackdrop(DetailPage page)
    {
        var view = page.ViewModel;
        var store = Get<Grid>(page, "StoreBackdrop");
        Require(view.StoreMode && view.HeroImage is not null && store.Visibility == Visibility.Visible,
            "商店模式有图时没有显示顶部背景");
        Require(store.ActualHeight is > 0 and <= 520 && store.ActualWidth < view.PageWidth && !store.IsHitTestVisible,
            "商店背景高度越界或遮挡输入");
        Require(Get<Image>(page, "CornerArt").Visibility == Visibility.Collapsed,
            "商店模式仍显示右上角艺术图");
        Require(Get<StoreBackdropFade>(page, "_storeBackdropFade").IsActive,
            "商店背景没有启用透明渐隐");
        Require(Get<Border>(page, "StoreBackdropMask").Background is ImageBrush { ImageSource: Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap },
            "商店背景没有双轴透明遮罩");
        Require(Get<Border>(page, "StoreBackdropSides").Background is LinearGradientBrush { GradientStops.Count: 5 },
            "商店背景没有左右阅读渐变");
        Require(Get<Border>(page, "StoreBackdropBottom").Background is LinearGradientBrush { GradientStops.Count: 5 } bottom
            && bottom.GradientStops[^1].Color.A == 255,
            "商店背景底部没有渐隐到当前图片色");
    }

    private static bool DetailHasLocalBackdrop(DependencyObject node)
    {
        if (node is SystemBackdropElement) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            if (DetailHasLocalBackdrop(VisualTreeHelper.GetChild(node, index))) return true;
        return false;
    }
}
