using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell;
using Momoka.Shell.Views;
using Momoka.Shell.Windowing;
using Windows.UI;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Case("主岛：不支持时无controller且窗口保留浏览意图", () =>
        {
            MaterialFaults.MicaSupported = false;
            using var host = new HostWindow();
            host.InitializeForTest();
            Assert.True(host.UseBackdrop); Assert.False(host.VideoVisible); Assert.False(host.BackdropConnected);
            Assert.Equal(0, MaterialFaults.Controllers.Count);
        });
        foreach (var point in new[] { "mica.IsSupported", "mica.New", "mica.Kind", "framework.GetConfiguration", "config.Active", "config.Theme", "mica.SetConfiguration", "mica.AddBefore", "mica.AddAfter" })
            Case("主岛：初始化故障释放且保持意图/" + point, () =>
            {
                MaterialFaults.FailOnce(point);
                using var host = new HostWindow();
                host.InitializeForTest();
                Assert.True(host.UseBackdrop); Assert.False(host.VideoVisible); Assert.False(host.BackdropConnected);
                foreach (var controller in MaterialFaults.Controllers)
                {
                    Assert.Equal(1, controller.DisposeCalls);
                    Assert.Equal(0, controller.Targets.Count);
                }
                Assert.Null(((Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop)host.SourceForTest).SystemBackdrop);
            });
        Case("主岛：Add false不冒充连接，false后同值启用真的重连", () =>
        {
            MaterialFaults.MicaAddResult = false;
            using var host = new HostWindow(); host.InitializeForTest();
            Assert.False(host.BackdropConnected); Assert.True(host.UseBackdrop);
            var first = MaterialFaults.Controllers.Single();
            Assert.Equal(1, first.DisposeCalls);
            var firstConnections = host.SourceForTest.ConnectCalls;
            MaterialFaults.MicaAddResult = true;
            host.UseBackdrop = true;
            Assert.True(host.BackdropConnected);
            Assert.Equal(firstConnections + 1, host.SourceForTest.ConnectCalls);
            Assert.Equal(1, host.SourceForTest.DisconnectCalls);
        });
        Case("主岛：成功连接的同值启用不重建controller", () =>
        {
            using var host = new HostWindow(); host.InitializeForTest();
            var count = host.SourceForTest.ConnectCalls;
            host.UseBackdrop = true; host.UseBackdrop = true;
            Assert.Equal(count, host.SourceForTest.ConnectCalls);
            Assert.Equal(1, MaterialFaults.Controllers.Count);
        });
        Case("主岛：宿主赋值异常可恢复且不变成播放态", () =>
        {
            MaterialFaults.FailOnce("host.Assign");
            using var host = new HostWindow(); host.InitializeForTest();
            Assert.True(host.UseBackdrop); Assert.False(host.VideoVisible); Assert.False(host.BackdropConnected);
            host.UseBackdrop = true;
            Assert.True(host.BackdropConnected);
        });
        Case("主岛：首次创建前与播放期间失活状态都保留", () =>
        {
            using var host = new HostWindow();
            host.FocusForTest(false); host.InitializeForTest();
            Assert.False(MaterialFaults.Controllers.Single().Configuration!.IsInputActive);
            host.VideoVisible = true;
            host.FocusForTest(true); host.VideoVisible = false;
            Assert.True(MaterialFaults.Controllers.Last().Configuration!.IsInputActive);
            Assert.True(host.BackdropConnected);
        });
        Case("主岛：框架改写激活后恢复宿主状态并保留高对比", () =>
        {
            var backdrop = new IslandMicaBackdrop(); var target = new MaterialTarget(); var root = new XamlRoot();
            backdrop.Connect(target, root);
            try
            {
                backdrop.SetInputActive(false);
                backdrop.Change(target, config => { config.IsInputActive = true; config.IsHighContrast = true; });
                var config = MaterialFaults.Controllers.Single().Configuration!;
                Assert.False(config.IsInputActive); Assert.True(config.IsHighContrast);
                Assert.Equal(SystemBackdropTheme.Dark, config.Theme);
                ThemeHost.Apply(ThemeHost.Palette(3, isDark: false)); backdrop.ApplyTheme();
                Assert.Equal(SystemBackdropTheme.Light, config.Theme); Assert.True(config.IsHighContrast);
            }
            finally { backdrop.Disconnect(target); }
        });
        Case("主岛：系统Fallback是成功连接而非未接入", () =>
        {
            MaterialFaults.NextState = SystemBackdropState.Fallback;
            using var host = new HostWindow(); host.InitializeForTest();
            Assert.True(host.BackdropConnected); Assert.False(host.VideoVisible);
            Assert.Contains("Fallback", host.BackdropState);
        });
        Case("主岛：激活setter失败后同意图通知仍重试", () =>
        {
            var backdrop = new IslandMicaBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new());
            try
            {
                var configuration = MaterialFaults.Controllers.Single().Configuration!;
                MaterialFaults.FailOnce("config.Active");
                backdrop.SetInputActive(false); Assert.True(configuration.IsInputActive);
                backdrop.SetInputActive(false); Assert.False(configuration.IsInputActive);
                Assert.Equal(1, backdrop.ConnectedTargetCount);
            }
            finally { backdrop.Disconnect(target); }
        });
        Case("主岛：播放撤材质、恢复与重复释放一次", () =>
        {
            using var host = new HostWindow(); host.InitializeForTest();
            var first = MaterialFaults.Controllers.Single();
            host.VideoVisible = true;
            Assert.True(host.VideoVisible); Assert.False(host.BackdropConnected); Assert.Equal(1, first.DisposeCalls);
            host.VideoVisible = false;
            Assert.False(host.VideoVisible); Assert.True(host.BackdropConnected);
            var second = MaterialFaults.Controllers.Last();
            host.DisposeForTest(); host.DisposeForTest();
            Assert.Equal(1, second.DisposeCalls); Assert.Null(host.SourceForTest.SystemBackdrop);
        });
        Case("主岛：透明效果关闭时不挂系统底材，重新打开同值启用可重连", () =>
        {
            ThemeHost.SetTransparency(false);
            using var host = new HostWindow();
            host.InitializeForTest();
            Assert.True(host.UseBackdrop); Assert.False(host.BackdropConnected);
            Assert.Null(host.SourceForTest.SystemBackdrop);
            Assert.Equal(0, MaterialFaults.Controllers.Count);
            ThemeHost.SetTransparency(true);
            host.UseBackdrop = true;
            Assert.True(host.BackdropConnected);
        });
        Case("主岛：Remove异常仍Dispose且重复关闭不再清理", () =>
        {
            using var host = new HostWindow(); host.InitializeForTest();
            var controller = MaterialFaults.Controllers.Single(); MaterialFaults.FailOnce("mica.Remove");
            host.VideoVisible = true;
            Assert.Equal(1, controller.RemoveCalls); Assert.Equal(1, controller.DisposeCalls); Assert.True(controller.Disposed);
            host.VideoVisible = true;
            Assert.Equal(1, controller.DisposeCalls);
        });
        Case("主岛：Dispose异常不阻断清除生产Entry", () =>
        {
            var backdrop = new IslandMicaBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new()); MaterialFaults.FailOnce("mica.Dispose");
            backdrop.Disconnect(target);
            Assert.Equal(0, backdrop.ConnectedTargetCount);
            Assert.Equal(1, MaterialFaults.Controllers.Single().DisposeCalls);
            backdrop.Disconnect(target); Assert.Equal(1, MaterialFaults.Controllers.Single().DisposeCalls);
        });

        Case("菜单：支持连接使用Thin/.65/.45与当前卡片色", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new());
            try
            {
                var controller = (DesktopAcrylicController)MaterialFaults.Controllers.Single();
                Assert.Equal(DesktopAcrylicKind.Thin, controller.Kind);
                Assert.Equal(0.65f, controller.TintOpacity); Assert.Equal(0.45f, controller.LuminosityOpacity);
                Assert.Equal(ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt), controller.TintColor);
                Assert.Equal(controller.TintColor, controller.FallbackColor);
                Assert.Equal(1, backdrop.ConnectedTargetCount); Assert.Equal(0, backdrop.SolidTargetCount);
                Assert.False(backdrop.NeedsSolidSurface); Assert.Equal(1, ThemeHost.Subscribers);
            }
            finally { backdrop.Disconnect(target); }
            Assert.Equal(0, ThemeHost.Subscribers);
        });
        Case("菜单：不支持时实色登记且没有controller", () =>
        {
            MaterialFaults.AcrylicSupported = false;
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new());
            Assert.Equal(0, MaterialFaults.Controllers.Count); Assert.Equal(1, backdrop.TargetCount);
            Assert.Equal(1, backdrop.SolidTargetCount); Assert.True(backdrop.NeedsSolidSurface);
            backdrop.Disconnect(target); Assert.Equal(0, backdrop.TargetCount); Assert.Equal(0, ThemeHost.Subscribers);
        });
        foreach (var point in new[] { "framework.GetConfiguration", "acrylic.IsSupported", "acrylic.New", "acrylic.Kind", "acrylic.Tint", "acrylic.Fallback", "acrylic.TintOpacity", "acrylic.Luminosity", "acrylic.SetConfiguration", "acrylic.AddBefore", "acrylic.AddAfter" })
            Case("菜单：初始化故障进入实色并释放controller/" + point, () =>
            {
                MaterialFaults.FailOnce(point);
                var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget();
                backdrop.Connect(target, new());
                try
                {
                    Assert.Equal(0, backdrop.ConnectedTargetCount); Assert.Equal(1, backdrop.SolidTargetCount);
                    Assert.True(backdrop.NeedsSolidSurface);
                    foreach (var controller in MaterialFaults.Controllers)
                    {
                        Assert.Equal(1, controller.DisposeCalls); Assert.Equal(0, controller.Targets.Count);
                    }
                    Assert.Null(target.SystemBackdrop);
                }
                finally { backdrop.Disconnect(target); }
                Assert.Equal(0, ThemeHost.Subscribers);
            });
        Case("菜单：Add false只记实色且先释放controller", () =>
        {
            MaterialFaults.AcrylicAddResult = false;
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new());
            Assert.Equal(0, backdrop.ConnectedTargetCount); Assert.Equal(1, backdrop.SolidTargetCount);
            Assert.Equal(1, MaterialFaults.Controllers.Single().DisposeCalls); Assert.True(backdrop.NeedsSolidSurface);
            backdrop.Disconnect(target);
        });
        Case("菜单：两target共享一次主题订阅，逐个关闭", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var first = new MaterialTarget(); var second = new MaterialTarget();
            backdrop.Connect(first, new()); backdrop.Connect(second, new());
            Assert.Equal(2, backdrop.ConnectedTargetCount); Assert.Equal(1, ThemeHost.Subscribers); Assert.Equal(1, ThemeHost.AddCalls);
            backdrop.Disconnect(first); Assert.Equal(1, ThemeHost.Subscribers);
            backdrop.Disconnect(second); Assert.Equal(0, ThemeHost.Subscribers); Assert.Equal(1, ThemeHost.RemoveCalls);
            Assert.True(MaterialFaults.Controllers.All(controller => controller.DisposeCalls == 1));
        });
        Case("菜单：主/子开着切五主题后颜色更新且保留controller对象", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var first = new MaterialTarget(); var second = new MaterialTarget();
            backdrop.Connect(first, new()); backdrop.Connect(second, new());
            var original = MaterialFaults.Controllers.ToArray();
            try
            {
                for (var index = 1; index <= 5; index++)
                {
                    ThemeHost.Apply(ThemeHost.Palette(index));
                    foreach (var controller in original.Cast<DesktopAcrylicController>())
                    {
                        Assert.Equal(ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt), controller.TintColor);
                        Assert.Equal(controller.TintColor, controller.FallbackColor);
                        Assert.Equal(0.65f, controller.TintOpacity); Assert.Equal(0.45f, controller.LuminosityOpacity);
                    }
                    Assert.True(backdrop.UsesCardColor); Assert.Equal(2, MaterialFaults.Controllers.Count);
                }
            }
            finally { backdrop.Disconnect(first); backdrop.Disconnect(second); }
        });
        Case("菜单：关闭后换主题不再写已销毁controller", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new()); backdrop.Disconnect(target);
            var before = MaterialFaults.Trace.Count;
            ThemeHost.Apply(ThemeHost.Palette(3));
            Assert.Equal(before, MaterialFaults.Trace.Count); Assert.Equal(0, ThemeHost.Subscribers);
        });
        Case("菜单：系统Fallback仍是controller成功，不强迫XAML实色", () =>
        {
            MaterialFaults.NextState = SystemBackdropState.Fallback;
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new());
            Assert.Equal(1, backdrop.ConnectedTargetCount); Assert.False(backdrop.NeedsSolidSurface);
            Assert.Contains("Fallback", backdrop.MaterialState); backdrop.Disconnect(target);
        });
        Case("菜单：Remove失败仍Dispose并退订", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new()); MaterialFaults.FailOnce("acrylic.Remove"); backdrop.Disconnect(target);
            Assert.Equal(1, MaterialFaults.Controllers.Single().DisposeCalls);
            Assert.Equal(0, backdrop.TargetCount); Assert.Equal(0, ThemeHost.Subscribers);
        });
        Case("菜单：Dispose失败不阻断Entry/订阅清理", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget();
            backdrop.Connect(target, new()); MaterialFaults.FailOnce("acrylic.Dispose"); backdrop.Disconnect(target);
            Assert.Equal(0, backdrop.TargetCount); Assert.Equal(0, ThemeHost.Subscribers);
            backdrop.Disconnect(target); Assert.Equal(1, MaterialFaults.Controllers.Single().DisposeCalls);
        });
        Case("菜单：失败target使组实色，失败target离开恢复透明", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var first = new MaterialTarget(); var second = new MaterialTarget();
            backdrop.Connect(first, new()); MaterialFaults.AcrylicAddResult = false; backdrop.Connect(second, new());
            Assert.True(backdrop.NeedsSolidSurface); Assert.Equal(1, backdrop.ConnectedTargetCount); Assert.Equal(1, backdrop.SolidTargetCount);
            backdrop.Disconnect(second); Assert.False(backdrop.NeedsSolidSurface);
            backdrop.Disconnect(first);
        });
        Case("菜单：一个SurfaceChanged异常不吞其它订阅者", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget(); var observed = 0;
            Action broken = () => throw new InvalidOperationException("inert observer"); Action healthy = () => observed++;
            backdrop.SurfaceChanged += broken; backdrop.SurfaceChanged += healthy;
            backdrop.Connect(target, new()); Assert.Equal(1, observed);
            ThemeHost.Apply(ThemeHost.Palette(1)); Assert.Equal(2, observed);
            backdrop.Disconnect(target); Assert.Equal(3, observed);
            backdrop.SurfaceChanged -= broken; backdrop.SurfaceChanged -= healthy;
        });

        Case("实色层：Enabled前后Loaded只订一次，成功target透明而前景不变", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget(); backdrop.Connect(target, new());
            var foreground = new SolidColorBrush(Colors.White); var presenter = new MenuFlyoutPresenter { SystemBackdrop = backdrop, Foreground = foreground };
            DetailMenuSurface.SetEnabled(presenter, true);
            Assert.Equal(1, presenter.LoadedHandlerCount); Assert.Equal(0, presenter.PropertyCallbackCount);
            presenter.Load(); presenter.Load();
            Assert.Equal(1, presenter.PropertyCallbackCount); Assert.Equal((byte)0, Background(presenter).A);
            Assert.True(ReferenceEquals(foreground, presenter.Foreground));
            presenter.Unload(); Assert.Equal(0, presenter.PropertyCallbackCount);
            DetailMenuSurface.SetEnabled(presenter, false);
            Assert.Equal(0, presenter.LoadedHandlerCount); Assert.Equal(0, presenter.UnloadedHandlerCount);
            Assert.Null(presenter.Background); backdrop.Disconnect(target);
        });
        Case("实色层：已Loaded再Enabled立即绘制，失败target实色并随主题更新", () =>
        {
            MaterialFaults.AcrylicAddResult = false;
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget(); backdrop.Connect(target, new());
            var foreground = new SolidColorBrush(Colors.White); var presenter = new MenuFlyoutPresenter { SystemBackdrop = backdrop, Foreground = foreground };
            presenter.Load(); DetailMenuSurface.SetEnabled(presenter, true);
            Assert.Equal(ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt.WithAlpha(255)), Background(presenter));
            ThemeHost.Apply(ThemeHost.Palette(4, alpha: 40));
            Assert.Equal(ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt.WithAlpha(255)), Background(presenter));
            Assert.Equal((byte)255, Background(presenter).A); Assert.True(ReferenceEquals(foreground, presenter.Foreground));
            presenter.Unload(); DetailMenuSurface.SetEnabled(presenter, false); backdrop.Disconnect(target);
        });
        Case("实色层：高对比配置边界选择系统背景，不修改前景", () =>
        {
            MaterialFaults.AcrylicSupported = false; AccessibilitySettings.Contrast = true;
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget(); backdrop.Connect(target, new());
            var foreground = new SolidColorBrush(Colors.White); var presenter = new MenuFlyoutPresenter { SystemBackdrop = backdrop, Foreground = foreground };
            presenter.Load(); DetailMenuSurface.SetEnabled(presenter, true);
            Assert.Equal(UISettings.Background, Background(presenter)); Assert.True(ReferenceEquals(foreground, presenter.Foreground));
            presenter.Unload(); DetailMenuSurface.SetEnabled(presenter, false); backdrop.Disconnect(target);
        });
        Case("实色层：共享一失败把两个presenter变实色，失败目标移除都恢复", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var first = new MaterialTarget(); var second = new MaterialTarget();
            backdrop.Connect(first, new());
            var main = new MenuFlyoutPresenter { SystemBackdrop = backdrop }; var sub = new MenuFlyoutPresenter { SystemBackdrop = backdrop };
            main.Load(); sub.Load(); DetailMenuSurface.SetEnabled(main, true); DetailMenuSurface.SetEnabled(sub, true);
            MaterialFaults.AcrylicAddResult = false; backdrop.Connect(second, new());
            Assert.Equal((byte)255, Background(main).A); Assert.Equal((byte)255, Background(sub).A);
            backdrop.Disconnect(second); Assert.Equal((byte)0, Background(main).A); Assert.Equal((byte)0, Background(sub).A);
            main.Unload(); sub.Unload(); DetailMenuSurface.SetEnabled(main, false); DetailMenuSurface.SetEnabled(sub, false);
            backdrop.Disconnect(first);
        });
        Case("实色层：SystemBackdrop更换解绑旧通知并订新通知", () =>
        {
            var first = new DetailMenuBackdrop(); var a = new MaterialTarget(); first.Connect(a, new());
            MaterialFaults.AcrylicAddResult = false; var second = new DetailMenuBackdrop(); var b = new MaterialTarget(); second.Connect(b, new());
            var presenter = new MenuFlyoutPresenter { SystemBackdrop = first }; presenter.Load(); DetailMenuSurface.SetEnabled(presenter, true);
            Assert.Equal((byte)0, Background(presenter).A); presenter.SystemBackdrop = second; Assert.Equal((byte)255, Background(presenter).A);
            var replacement = new SolidColorBrush(Colors.White); presenter.Background = replacement;
            first.Disconnect(a); Assert.True(ReferenceEquals(replacement, presenter.Background));
            second.Disconnect(b); presenter.Unload(); DetailMenuSurface.SetEnabled(presenter, false);
        });
        Case("实色层：Unloaded后旧通知不重绘，Loaded重新取当前主题", () =>
        {
            MaterialFaults.AcrylicSupported = false;
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget(); backdrop.Connect(target, new());
            var presenter = new MenuFlyoutPresenter { SystemBackdrop = backdrop }; presenter.Load(); DetailMenuSurface.SetEnabled(presenter, true);
            var oldColor = Background(presenter); presenter.Unload(); ThemeHost.Apply(ThemeHost.Palette(4));
            Assert.Equal(oldColor, Background(presenter)); Assert.Equal(0, presenter.PropertyCallbackCount);
            presenter.Load(); Assert.Equal(ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt.WithAlpha(255)), Background(presenter));
            presenter.Unload(); DetailMenuSurface.SetEnabled(presenter, false); backdrop.Disconnect(target);
        });
        Case("实色层：无backdrop时保守实色，关闭Enabled清除本地覆盖", () =>
        {
            var presenter = new MenuFlyoutPresenter(); presenter.Load(); DetailMenuSurface.SetEnabled(presenter, true);
            Assert.Equal((byte)255, Background(presenter).A); Assert.Equal(1, presenter.PropertyCallbackCount);
            DetailMenuSurface.SetEnabled(presenter, false);
            Assert.Equal(0, presenter.PropertyCallbackCount); Assert.Null(presenter.Background);
        });
        Case("主岛：两次摘除失败后同值请求仍可重试", () =>
        {
            using var host = new HostWindow(); host.InitializeForTest();
            MaterialFaults.FailOnce("host.Assign"); MaterialFaults.FailOnce("host.Assign");
            host.VideoVisible = true;
            Assert.False(host.UseBackdrop); Assert.True(host.VideoVisible); Assert.True(host.BackdropConnected);
            host.VideoVisible = true;
            Assert.False(host.BackdropConnected); Assert.Null(host.SourceForTest.SystemBackdrop);
        });
        foreach (var point in new[] { "mica.State", "acrylic.State" })
            Case("诊断读取失败不释放已登记controller/" + point, () =>
            {
                MaterialFaults.FailOnce(point);
                if (point.StartsWith("mica", StringComparison.Ordinal))
                {
                    using var host = new HostWindow(); host.InitializeForTest();
                    Assert.True(host.BackdropConnected); Assert.Equal(0, MaterialFaults.Controllers.Single().DisposeCalls);
                }
                else
                {
                    var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget(); backdrop.Connect(target, new());
                    Assert.Equal(1, backdrop.ConnectedTargetCount); Assert.False(backdrop.NeedsSolidSurface);
                    Assert.Equal(0, MaterialFaults.Controllers.Single().DisposeCalls); backdrop.Disconnect(target);
                }
            });
        Case("实色层：延迟Unloaded不摘仍Loaded的订阅，禁用保留后来背景", () =>
        {
            MaterialFaults.AcrylicSupported = false;
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget(); backdrop.Connect(target, new());
            var presenter = new MenuFlyoutPresenter { SystemBackdrop = backdrop }; presenter.Load();
            DetailMenuSurface.SetEnabled(presenter, true); presenter.DelayedUnload();
            Assert.Equal(1, presenter.PropertyCallbackCount);
            ThemeHost.Apply(ThemeHost.Palette(4));
            Assert.Equal(ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt), Background(presenter));
            var replacement = new SolidColorBrush(Colors.White); presenter.Background = replacement;
            DetailMenuSurface.SetEnabled(presenter, false);
            Assert.True(ReferenceEquals(replacement, presenter.Background)); Assert.Equal(0, presenter.PropertyCallbackCount);
            backdrop.Disconnect(target);
        });
        Case("实色层：系统颜色读取失败不穿透Loaded，后续更新可恢复", () =>
        {
            MaterialFaults.AcrylicSupported = false; AccessibilitySettings.Contrast = true;
            var backdrop = new DetailMenuBackdrop(); var target = new MaterialTarget(); backdrop.Connect(target, new());
            var presenter = new MenuFlyoutPresenter { SystemBackdrop = backdrop }; presenter.Load();
            MaterialFaults.FailOnce("ui.GetColorValue"); DetailMenuSurface.SetEnabled(presenter, true);
            Assert.Equal(ThemeHost.ToColor(ThemeHost.Current.Colors.SurfaceAlt.WithAlpha(0xFF)), Background(presenter));
            ThemeHost.Apply(ThemeHost.Palette(1)); Assert.Equal(UISettings.Background, Background(presenter));
            presenter.Unload(); DetailMenuSurface.SetEnabled(presenter, false); backdrop.Disconnect(target);
        });
        Case("菜单：同一XamlRoot的主/子目标持独立框架配置", () =>
        {
            var backdrop = new DetailMenuBackdrop(); var first = new MaterialTarget(); var second = new MaterialTarget();
            var root = new XamlRoot(); backdrop.Connect(first, root); backdrop.Connect(second, root);
            var firstConfig = backdrop.ConfigurationFor(first); var secondConfig = backdrop.ConfigurationFor(second);
            Assert.False(ReferenceEquals(firstConfig, secondConfig));
            backdrop.Change(first, config => { config.IsHighContrast = true; config.IsInputActive = true; });
            Assert.True(firstConfig.IsHighContrast); Assert.False(secondConfig.IsHighContrast);
            Assert.True(firstConfig.IsInputActive); Assert.False(secondConfig.IsInputActive);
            backdrop.Disconnect(first); Assert.Equal(1, backdrop.TargetCount);
            ThemeHost.Apply(ThemeHost.Palette(3)); Assert.True(backdrop.UsesCardColor);
            Assert.True(ReferenceEquals(secondConfig, backdrop.ConfigurationFor(second)));
            backdrop.Disconnect(second);
        });
        Case("菜单属性订阅失败不穿透Loaded，再次加载可恢复", () =>
        {
            var presenter = new MenuFlyoutPresenter();
            DetailMenuSurface.SetEnabled(presenter, true);
            MaterialFaults.FailOnce("dependency.Register"); presenter.Load();
            Assert.Equal(0, presenter.PropertyCallbackCount);
            presenter.Unload(); presenter.Load();
            Assert.Equal(1, presenter.PropertyCallbackCount); Assert.Equal((byte)255, Background(presenter).A);
            presenter.Unload(); DetailMenuSurface.SetEnabled(presenter, false);
        });
        return TestHarness.Run();
    }

    private static Color Background(MenuFlyoutPresenter presenter) => ((SolidColorBrush)presenter.Background!).Color;
    private static void Case(string name, Action action) => Test(name, () =>
    {
        MaterialFaults.Reset();
        action();
        Assert.Equal(0, ThemeHost.Subscribers, "用例结束时仍有应用主题订阅，不能靠下一项 Reset 清掉");
    });
}
