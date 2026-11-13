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
        var items = new[] { Series(), Season("a"), Episode("a"), new EmbyItem { Id = "mica-movie", Type = EmbyItemType.Movie } };
        foreach (var item in items)
        {
            item.Name = "云母详情 · " + item.Type;
            item.Overview = "核对简洁详情页的系统云母、信息卡、主题切换与导航恢复。";
            item.ImageTags["Primary"] = "fixture";
            item.BackdropImageTags.Add("fixture");
        }
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
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
                foreach (var width in new[] { 1200, 760 })
                {
                    await ResizeResponsiveAsync(page, width, 900);
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
    }

    private static void RequireSharedDetailContentLayer(DetailPage page)
    {
        Require(!DetailHasLocalBackdrop(page), "详情页又创建了局部系统材质");
        var layer = Get<Border>(page, "HeroContentLayerSource").Background;
        Require(layer is SolidColorBrush { Color.A: > 0 and < 255 }, "普通模式内容层不是半透明系统层色");
        foreach (var name in new[] { "HeroCardTop", "HeroCardBottom" })
        {
            var card = Get<Border>(page, name);
            Require(ReferenceEquals(card.Background, layer), "顶部卡面没有共用系统内容层");
            Require(card.Child is null, "顶部卡面仍叠了子材质层");
        }
    }

    private static bool DetailHasLocalBackdrop(DependencyObject node)
    {
        if (node is SystemBackdropElement) return true;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            if (DetailHasLocalBackdrop(VisualTreeHelper.GetChild(node, index))) return true;
        return false;
    }
}
