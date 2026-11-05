using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // 实际按钮与旧平台模板在同一帧对照；状态切换不冒充指针输入。
    private async Task HomeButtonEdgesAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var savedTheme = ThemeHost.Current;
        var savedTopMost = _window!.TopMost;
        _window.TopMost = true;
        var item = new EmbyItem { Id = "button-edge", Name = "圆角按钮检查", Type = EmbyItemType.Movie, BackdropImageTags = ["fixture"] };
        fixture.Session.Capture().Bind(item);
        var image = await FixtureImageAsync(1280, 720);
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get || request.Method == HttpMethod.Head, "圆角检查拒绝服务器写入");
            return Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
                ? new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(image) }
                : FakeTransport.Standard(request));
        };
        var banner = new HomeBanner();
        var holder = new StackPanel();
        holder.Children.Add(banner);
        _root.Children.Add(holder);
        var reads = new List<string>();
        BannerSlide? opened = null;
        banner.OpenRequested += (_, slide) => opened = slide;
        try
        {
            banner.Apply([new BannerSlide(item, fixture.Images)]);
            await UntilAsync(() => banner.FrontPicture is not null);
            var button = Get<Button>(banner, "OpenButton");
            var old = new Button
            {
                Content = button.Content,
                Style = (Style)Application.Current.Resources["DefaultButtonStyle"],
                Height = button.Height,
                CornerRadius = button.CornerRadius,
                Padding = button.Padding,
                BorderThickness = button.BorderThickness,
                BorderBrush = button.BorderBrush,
                Background = button.Background,
                Foreground = button.Foreground
            };
            var sample = new Button { Content = button.Content, Style = button.Style, Background = button.Background };
            foreach (var state in new[] { "PointerOver", "Pressed", "Disabled" })
                foreach (var prefix in new[] { "Background", "BorderBrush", "Foreground" })
                {
                    var key = "Button" + prefix + state;
                    var value = button.Resources.TryGetValue(key, out var local) ? local : Application.Current.Resources[key];
                    old.Resources[key] = value;
                    sample.Resources[key] = value;
                }
            var comparisons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(24) };
            comparisons.Children.Add(old);
            comparisons.Children.Add(sample);
            holder.Children.Add(comparisons);
            var originalTemplate = button.Template;
            button.Template = old.Template;
            button.ApplyTemplate();
            Require(!Descendants(button).OfType<Rectangle>().Any(part => part.Name == "Surface"), "旧模板反例未实际替换");
            button.Template = originalTemplate;
            button.ApplyTemplate();
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 900);
                await UntilAsync(() => Math.Abs(_root.ActualWidth - width) < 1);
                foreach (var theme in UiThemes.All)
                {
                    ThemeHost.Apply(theme);
                    foreach (var acrylic in new[] { false, true })
                    {
                        banner.ApplyAppearance(new UiSettings { UseAcrylicControls = acrylic });
                        foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Disabled" })
                        {
                            foreach (var control in new[] { button, old, sample })
                                Require(VisualStateManager.GoToState(control, state, false), "圆角按钮缺少状态：" + state);
                            await LayoutAsync(holder);
                            var surface = Descendants(button).OfType<Rectangle>().Single(part => part.Name == "Surface");
                            var presenter = Descendants(button).OfType<ContentPresenter>().Single(part => part.Name == "ContentPresenter");
                            Require(surface.Fill is AcrylicBrush && surface.Stroke is not null && surface.StrokeThickness == 1
                                && surface.RadiusX == 16 && surface.RadiusY == 16 && !surface.UseLayoutRounding
                                && presenter.Background is null && presenter.BorderBrush is null,
                                "圆角底色与边线没有只在同一矢量轮廓绘制");
                            Require(button.ActualHeight == 44 && Math.Abs(old.ActualWidth - sample.ActualWidth) < 0.01,
                                "圆角修复改变按钮尺寸或内容占位");
                            if (state == "Normal" || width == 1280 && theme == UiThemes.All[0] && !acrylic)
                            {
                                var name = $"home-button-edge-{theme.Id}-{width}-{acrylic}-{state}-screen.png";
                                await SaveProbeScreenAsync(name);
                                reads.Add($"{name}: 左=旧模板，右=矢量模板；实际首页按钮={Box(button)}；缩放={_root.XamlRoot.RasterizationScale}");
                            }
                        }
                    }
                }
            }
            VisualStateManager.GoToState(button, "Normal", false);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(opened?.Item.Id == item.Id && fixture.Actions.Plays == 0, "详情按钮导航失效或请求播放");
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "home-button-edges.txt"), reads);
            _root.Children.Remove(holder);
            ThemeHost.Apply(savedTheme);
            _window.TopMost = savedTopMost;
        }
    }
}
