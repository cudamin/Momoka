using System.Net;
using Microsoft.UI.Composition.SystemBackdrops;
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
                        Call(page, "SettleShowcase");
                        // 2026-11-12 用户令「把媒体详情页的简洁模式也改成跟主页一样的」：这一页不再挂页面级的
                        // 局部材质，底就是外壳 ContentHost 那块画布（Mica Base ＋ 60% 窗口色）。从前这里读的是
                        // PlainBackdrop 上的 Mica Alt —— 它把外壳画布整个盖掉，两页的底因此对不上。
                        var host = Get<Grid>(shell, "ContentHost");
                        var top = Get<SystemBackdropElement>(page, "HeroMicaTop");
                        Require(!((Grid)page.Content).Children.OfType<SystemBackdropElement>().Any(),
                            "简洁详情又给页面挂了局部材质");
                        Require(host.Background is SolidColorBrush { Color.A: 0x99 }, "外壳画布不是那层 60% 的窗口色");
                        Require(Get<Border>(page, "BodyPaper").Background is SolidColorBrush { Color.A: 0 }, "正文实色挡住了窗口画布");
                        Require(!page.ViewModel.BackdropShown && Get<StackPanel>(page, "HeroTail").Background is null, "简洁详情仍铺图或黑色尾部");
                        Require(!page.ViewModel.HeroCardShown || top.SystemBackdrop is MicaBackdrop { Kind: MicaKind.Base }, "头部卡面缺少 Mica");
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
                        reads.Add($"{item.Type}/{width}/{theme.Id}: canvas + {(page.ViewModel.HeroCardShown ? "Base" : "—")}, "
                            + $"body=transparent");
                        await SaveProbeScreenAsync($"detail-mica-{item.Type}-{width}-{theme.Id}-screen.png");
                    }
                }
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(Get<SystemBackdropElement>(page, "HeroMicaTop").SystemBackdrop is null
                    && Get<SystemBackdropElement>(page, "HeroMicaBottom").SystemBackdrop is null, "沉浸恢复后未释放简洁材质");
                fixture.Settings.Ui.EnableImmersive = false;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(!((Grid)page.Content).Children.OfType<SystemBackdropElement>().Any(),
                    "简洁恢复后页面级材质又回来了");
                Require(!page.ViewModel.HeroCardShown
                    || Get<SystemBackdropElement>(page, "HeroMicaTop").SystemBackdrop is MicaBackdrop,
                    "简洁恢复后卡面没有重新连接材质");
                page.Release();
                Require(Get<SystemBackdropElement>(page, "HeroMicaTop").SystemBackdrop is null
                    && Get<SystemBackdropElement>(page, "HeroMicaBottom").SystemBackdrop is null, "离页未释放材质");
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
}
