using System.Net;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
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
        var items = new[] { Series(), Season("a"), Episode("a"),
            new EmbyItem { Id = "mica-movie", Type = EmbyItemType.Movie },
            new EmbyItem { Id = "mica-video", Type = EmbyItemType.Video },
            new EmbyItem { Id = "mica-trailer", Type = EmbyItemType.Trailer },
            new EmbyItem { Id = "mica-mv", Type = EmbyItemType.MusicVideo } };
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
                    foreach (var theme in item.Type is EmbyItemType.Video or EmbyItemType.Trailer or EmbyItemType.MusicVideo
                        ? new[] { UiThemes.Default } : UiThemes.All)
                    {
                        ThemeHost.Apply(theme.Id);
                        await LayoutAsync(shell);
                        Call(page, "SettleShowcase");
                        var command = Get<Border>(page, "PlainBackdrop");
                        var content = Get<Border>(page, "MicaLayerSource");
                        Require(_window.UseBackdrop
                            && Get<DesktopWindowXamlSource>(_window, "_source").SystemBackdrop is MicaBackdrop { Kind: MicaKind.BaseAlt },
                            "简洁详情没有复用主窗口 Mica Alt");
                        Require(!Descendants(page).OfType<SystemBackdropElement>().Any(), "简洁详情仍重复创建页面或卡片背景材质");
                        Require(page.Background is null && shell.BrowseRoot is Panel { Background: null }, "页面或外壳实色盖住窗口云母");
                        Require(command.Visibility == Visibility.Visible, "简洁详情命令层未显示");
                        RequireMicaFill(command.Background, "LayerOnMicaBaseAltFillColorDefault", "详情命令层");
                        RequireMicaFill(content.Background, "LayerFillColorDefault", "详情内容层");
                        Require(Get<Border>(page, "BodyPaper").Background is SolidColorBrush { Color.A: 0 }, "正文实色挡住云母");
                        Require(!page.ViewModel.BackdropShown && Get<StackPanel>(page, "HeroTail").Background is null, "简洁详情仍铺图或黑色尾部");
                        Require(!page.ViewModel.HeroCardShown || ReferenceEquals(Get<Border>(page, "HeroCardTop").Background, content.Background)
                            && ReferenceEquals(Get<Border>(page, "HeroCardBottom").Background, content.Background), "头部两块卡面未复用内容叠层");
                        Require(ReferenceEquals(Get<StackPanel>(page, "InfoPanel").Background, content.Background), "信息卡未复用内容叠层");
                        Require(Bounds(command, shell).Top <= Bounds(page, shell).Top + 1
                            && Bounds(command, shell).Bottom >= Bounds(page, shell).Bottom - 1, "命令层未铺满详情页");
                        Require(shell.TrailBase is null && shell.TitleStripNow == TitleStrip.PagePainted, "面包屑实色挡住云母");
                        Require(Get<Border>(page, "PlainHeroSurface").Opacity == 0
                            && Get<Border>(page, "HeroScrim").Opacity == 0, "旧顶部渐变挡住简洁云母");
                        ExpectMicaRejection(() => RequireMicaFill(new AcrylicBrush(), "LayerOnMicaBaseAltFillColorDefault", "详情命令层"),
                            "错误详情命令层");
                        reads.Add($"{item.Type}/{width}/{theme.Id}: 单窗口 BaseAlt，command={((SolidColorBrush)command.Background).Color.A}，"
                            + $"content={((SolidColorBrush)content.Background).Color.A}，页面及正文透明");
                        await SaveProbeScreenAsync($"detail-mica-{item.Type}-{width}-{theme.Id}-screen.png");
                    }
                }
                fixture.Settings.Ui.EnableImmersive = true;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(Get<Border>(page, "PlainBackdrop").Visibility == Visibility.Collapsed
                    && page.Background is not null, "沉浸恢复后简洁命令层未让开");
                fixture.Settings.Ui.EnableImmersive = false;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(page);
                Require(Get<Border>(page, "PlainBackdrop").Visibility == Visibility.Visible
                    && page.Background is null && _window.UseBackdrop, "简洁恢复未重新使用窗口云母叠层");
                page.Release();
                Require(_window.UseBackdrop && !Descendants(page).OfType<SystemBackdropElement>().Any(), "离页改变窗口云母或仍残留重复背景");
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
