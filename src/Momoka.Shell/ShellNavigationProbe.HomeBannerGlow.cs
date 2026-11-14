using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeBannerGlowAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var image = await FixtureImageAsync(1280, 720);
        var item = new EmbyItem { Id = "glow-movie", Name = "轮播柔光 · 匿名示例", Type = EmbyItemType.Movie, BackdropImageTags = ["backdrop"] };
        fixture.Session.Capture().Bind(item);
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
            ? Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(image) })
            : Task.FromResult(FakeTransport.Standard(request));
        var preference = HomeMotion.PreferenceEnabled;
        var banner = new HomeBanner();
        _root.Children.Add(banner);
        var readings = new List<string>();
        try
        {
            HomeMotion.ApplyPreference(false);
            var slide = new BannerSlide(item, fixture.Images);
            banner.Apply([slide]);
            await WaitAsync(() => slide.Picture is not null, 8000);
            var bounds = Get<Grid>(banner, "Root");
            // 显式展示状态，不依赖用户共用的指针停留；此项不冒充真实鼠标输入证据。
            bounds.IsHitTestVisible = false;
            var surface = Get<Rectangle>(banner, "PointerGlow");
            var brush = Get<RadialGradientBrush>(banner, "PointerGlowBrush");
            var glow = Get<BannerPointerGlow>(banner, "_pointerGlow");
            void ValidateGradient() => Require(brush.MappingMode == BrushMappingMode.Absolute && brush.RadiusX == brush.RadiusY
                && brush.RadiusX == 180 && Math.Abs(brush.Opacity - 0.13) < 0.001
                && brush.GradientStops[^1].Color.A == 0 && !surface.IsHitTestVisible
                && AutomationProperties.GetAccessibilityView(surface) == AccessibilityView.Raw,
                "轮播柔光半径、渐隐或输入隔离不符合标记");
            ValidateGradient();
            var rejected = false;
            try
            {
                brush.RadiusY = 90;
                try { ValidateGradient(); }
                catch (InvalidOperationException) { rejected = true; }
            }
            finally { brush.RadiusY = 180; }
            Require(rejected, "柔光检查没有拒绝椭圆半径负例");
            ValidateGradient();
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(banner);
                glow.Leave(immediate: true);
                Require(surface.Visibility == Visibility.Collapsed, "轮播柔光未按初始态收起");
                var before = await ReadBannerRegionAsync(banner);
                var point = new Point(bounds.ActualWidth * 0.70, bounds.ActualHeight * 0.30);
                glow.SetEnabled(true);
                glow.Move(point, mouse: true);
                await LayoutAsync(banner);
                Require(surface.Visibility == Visibility.Visible && brush.Center == point && brush.GradientOrigin == point,
                    "原生径向光心没有跟随封面 DIP 坐标");
                ValidateGradient();
                var after = await ReadBannerRegionAsync(banner);
                var changed = 0;
                var lightened = 0;
                for (var index = 0; index < before.Length; index += 4)
                {
                    if (!before.AsSpan(index, 3).SequenceEqual(after.AsSpan(index, 3))) changed++;
                    if (after[index] + after[index + 1] + after[index + 2] > before[index] + before[index + 1] + before[index + 2]) lightened++;
                }
                Require(changed > 100 && lightened > changed * 0.9, $"原生柔光未产生轻微增亮像素：{changed}/{lightened}");
                readings.Add($"{width} DIP：光心 {point.X:0.0},{point.Y:0.0}，半径180，峰值0.13，变化{changed}、增亮{lightened}像素");
                await SaveProbeScreenAsync($"home-banner-glow-{width}-screen.png");
                glow.Move(new Point(point.X - 80, point.Y + 20), mouse: true);
                Require(brush.Center.X == point.X - 80 && brush.Center.Y == point.Y + 20, "移动后光心没有同步");
                glow.SetEnabled(false);
                Require(surface.Visibility == Visibility.Collapsed, "模拟禁用效果后柔光仍显示");
                glow.Leave();
                glow.SetEnabled(true);
                Require(surface.Visibility == Visibility.Collapsed, "离开后恢复效果复活旧光");
                glow.Move(point, mouse: false);
                Require(surface.Visibility == Visibility.Collapsed, "触摸或笔点亮了鼠标光");
                var restored = await ReadBannerRegionAsync(banner);
                Require(before.SequenceEqual(restored), "移出后封面像素没有恢复");
            }
            Require(fixture.Actions.Plays == 0, "柔光检查意外播放");
            readings.Add("显式调用真实生产柔光；禁用/恢复、非鼠标、恢复原图和零播放通过。真实指针及系统设置待另验。");
        }
        finally
        {
            _root.Children.Remove(banner);
            HomeMotion.ApplyPreference(preference);
            ResizeProbeClient(1420, 980);
            File.WriteAllLines(System.IO.Path.Combine(_options.Paths.LogDirectory, "home-banner-glow.txt"), readings);
        }
    }
}
