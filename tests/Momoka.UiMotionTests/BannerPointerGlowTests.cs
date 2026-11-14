using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Shell.Views;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class BannerPointerGlowTests
{
    internal static void Register()
    {
        Test("轮播柔光跟随鼠标 DIP 坐标，移动不重播入场或移动封面", () =>
        {
            Reset();
            var (surface, brush, glow) = Create();
            glow.SetEnabled(true);
            glow.Move(new Point(200, 80), mouse: true);
            Assert.Equal(Visibility.Visible, surface.Visibility);
            var board = Storyboard.Active.Single();
            Storyboard.Advance(40);
            var opacity = surface.Opacity;
            glow.Move(new Point(330, 100), mouse: true);
            Assert.Equal(new Point(330, 100), brush.Center);
            Assert.Equal(brush.Center, brush.GradientOrigin);
            Assert.Equal(opacity, surface.Opacity);
            Assert.True(ReferenceEquals(board, Storyboard.Active.Single()));
            Assert.Equal(0d, ((TranslateTransform)surface.RenderTransform!).Y);
            glow.Leave(immediate: true);
        });

        Test("柔光离开途中重入取消旧关闭，迟到回调不能收起新光", () =>
        {
            Reset();
            var (surface, brush, glow) = Create();
            glow.SetEnabled(true);
            glow.Move(new Point(120, 80), mouse: true);
            Storyboard.Advance(180);
            glow.Leave();
            var stale = Storyboard.Active.Single().CapturePostedCompletion();
            Storyboard.Advance(50);
            var fading = surface.Opacity;
            glow.Move(new Point(220, 80), mouse: true);
            Assert.Equal(fading, surface.Opacity);
            stale();
            Assert.Equal(Visibility.Visible, surface.Visibility);
            Assert.Equal(new Point(220, 80), brush.Center);
            Storyboard.Advance(180);
            Assert.Equal(1d, surface.Opacity);
            glow.Leave();
            Storyboard.Advance(140);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("关透明或高对比度立即收起，离开后恢复效果不会复活旧光", () =>
        {
            Reset();
            var (surface, _, glow) = Create();
            glow.SetEnabled(true);
            glow.Move(new Point(120, 80), mouse: true);
            var stale = Storyboard.Active.Single().CapturePostedCompletion();
            glow.SetEnabled(false);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            Assert.Equal(0, Storyboard.Count);
            stale();
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            glow.SetEnabled(true);
            Assert.Equal(Visibility.Visible, surface.Visibility);
            glow.SetEnabled(false);
            glow.Leave();
            glow.SetEnabled(true);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("触摸与笔不显示轮播柔光，并清掉先前鼠标位置", () =>
        {
            Reset();
            var (surface, _, glow) = Create();
            glow.SetEnabled(true);
            glow.Move(new Point(120, 80), mouse: true);
            glow.Move(new Point(200, 120), mouse: false);
            Storyboard.Advance(140);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            glow.SetEnabled(false);
            glow.SetEnabled(true);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
        });

        Test("柔光拒绝右下边界和无效坐标，宽窄改变不保留屏外光心", () =>
        {
            Reset();
            var (surface, _, glow) = Create();
            glow.SetEnabled(true);
            foreach (var point in new[] { new Point(-1, 10), new Point(600, 10), new Point(10, 300),
                new Point(double.NaN, 10), new Point(10, double.PositiveInfinity) })
            {
                glow.Move(point, mouse: true);
                Assert.Equal(Visibility.Collapsed, surface.Visibility);
            }
            glow.Move(new Point(599, 299), mouse: true);
            Assert.Equal(Visibility.Visible, surface.Visibility);
            glow.Move(new Point(600, 299), mouse: true);
            Storyboard.Advance(140);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("笔触摸持续移动和重复离开不重启柔光退出", () =>
        {
            Reset();
            var (surface, _, glow) = Create();
            glow.SetEnabled(true);
            glow.Move(new Point(120, 80), mouse: true);
            Storyboard.Advance(180);
            glow.Move(new Point(200, 100), mouse: false);
            var exit = Storyboard.Active.Single();
            for (var index = 0; index < 5; index++)
            {
                Storyboard.Advance(20);
                glow.Move(new Point(220 + index, 100), mouse: false);
                glow.Leave();
                Assert.True(ReferenceEquals(exit, Storyboard.Active.Single()));
            }
            Storyboard.Advance(40);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            Assert.Equal(0, Storyboard.Count);
        });

        foreach (var system in new[] { false, true })
            Test("柔光关闭" + (system ? "系统" : "应用") + "动画落定，离开立即隐藏", () =>
            {
                Reset();
                var (surface, _, glow) = Create();
                glow.SetEnabled(true);
                glow.Move(new Point(120, 80), mouse: true);
                Storyboard.Advance(40);
                if (system) UISettings.SetAnimationsEnabled(false);
                else HomeMotion.ApplyPreference(false);
                Assert.Equal(1d, surface.Opacity);
                Assert.Equal(0, Storyboard.Count);
                glow.Leave();
                Assert.Equal(Visibility.Collapsed, surface.Visibility);
                Reset();
            });

        Test("柔光卸载立即释放动画和偏好订阅，重载须等新的鼠标", () =>
        {
            Reset();
            var (surface, _, glow) = Create();
            glow.SetEnabled(true);
            glow.Move(new Point(120, 80), mouse: true);
            var stale = Storyboard.Active.Single().CapturePostedCompletion();
            glow.Leave(immediate: true);
            surface.RaiseUnload();
            stale();
            Assert.Equal(0, Storyboard.Count);
            Assert.True(UISettings.Created.All(settings => settings.SubscriptionCount == 0));
            surface.RaiseLoaded();
            glow.SetEnabled(true);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            glow.Move(new Point(240, 80), mouse: true);
            Assert.Equal(Visibility.Visible, surface.Visibility);
            glow.Leave(immediate: true);
        });

        Test("首次光层未布局时用封面几何点亮，封面缩窄后清掉屏外光心", () =>
        {
            Reset();
            var surface = new FrameworkElement { ActualWidth = 0, Height = 0, Visibility = Visibility.Collapsed };
            var bounds = new FrameworkElement { ActualWidth = 600, Height = 300 };
            var brush = new RadialGradientBrush();
            var glow = new BannerPointerGlow(surface, brush, bounds);
            glow.SetEnabled(true);
            glow.Move(new Point(500, 120), mouse: true);
            Assert.Equal(Visibility.Visible, surface.Visibility);
            Assert.Equal(new Point(500, 120), brush.Center);
            bounds.ActualWidth = 300;
            glow.SetEnabled(true);
            Assert.Equal(Visibility.Collapsed, surface.Visibility);
            Assert.Equal(0, Storyboard.Count);
        });

        Test("实际轮播标记把柔光放在阅读遮罩下，禁用命中并保留原生圆形径向画刷", () =>
        {
            var root = FindRoot();
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace names = "http://schemas.microsoft.com/winfx/2006/xaml";
            var document = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/HomeBanner.xaml"));
            var glow = document.Descendants(xaml + "Rectangle").Single(node => (string?)node.Attribute(names + "Name") == "PointerGlow");
            Assert.Equal("False", (string?)glow.Attribute("IsHitTestVisible"));
            Assert.Equal("Raw", (string?)glow.Attribute("AutomationProperties.AccessibilityView"));
            Assert.Equal("Collapsed", (string?)glow.Attribute("Visibility"));
            var brush = glow.Descendants(xaml + "RadialGradientBrush").Single();
            Assert.Equal("Absolute", (string?)brush.Attribute("MappingMode"));
            Assert.Equal((string?)brush.Attribute("RadiusX"), (string?)brush.Attribute("RadiusY"));
            var siblings = glow.Parent!.Elements().ToArray();
            int Position(string name) => Array.FindIndex(siblings, node => (string?)node.Attribute(names + "Name") == name);
            Assert.True(Position("LayerB") < Position("PointerGlow") && Position("PointerGlow") < Position("Top"));
            var source = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/Views/HomeBanner.xaml.cs"));
            Assert.True(source.Contains("Microsoft.UI.Input.PointerDeviceType.Mouse", StringComparison.Ordinal));
            Assert.True(source.Contains("if (!up) _pointerGlow.Leave();", StringComparison.Ordinal));
            Assert.True(source.Contains("_pointerGlow.Leave(immediate: true);", StringComparison.Ordinal));
        });
    }

    private static (FrameworkElement Surface, RadialGradientBrush Brush, BannerPointerGlow Glow) Create()
    {
        var surface = new FrameworkElement { ActualWidth = 600, Height = 300, Visibility = Visibility.Collapsed };
        var brush = new RadialGradientBrush();
        return (surface, brush, new BannerPointerGlow(surface, brush, surface));
    }

    private static void Reset()
    {
        Storyboard.Reset();
        UISettings.Reset();
        HomeMotion.ApplyPreference(true);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("Missing current repository root.");
    }
}
