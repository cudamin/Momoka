using System.Reflection;

namespace Momoka.Tests;

internal static class PlayerCaptureGeometryTests
{
    internal static void Register(SettingsPageStateTests.ShellFixture fixture)
    {
        PlayerCaptureControllerTests.Register(fixture);
        TestHarness.Test("截图裁切：默认全图与字幕底部三成使用原图像素", () =>
        {
            var full = Invoke("Default", 1920, 1080, false);
            Assert.Equal("0,0,1920,1080", Value(full));
            var subtitles = Invoke("Default", 1920, 1080, true);
            Assert.Equal("0,756,1920,324", Value(subtitles));
        });
        TestHarness.Test("截图裁切：Uniform留白不算图片，正反拖动得到相同区域", () =>
        {
            var forward = Invoke("FromViewport", 1920, 1080, 800d, 500d, 100d, 100d, 700d, 400d);
            var backward = Invoke("FromViewport", 1920, 1080, 800d, 500d, 700d, 400d, 100d, 100d);
            Assert.Equal(Value(forward), Value(backward));
            Assert.Equal("240,180,1440,720", Value(forward));
            var outside = Invoke("FromViewport", 1920, 1080, 800d, 500d, -50d, -40d, 900d, 550d);
            Assert.Equal("0,0,1920,1080", Value(outside));
        });
        TestHarness.Test("截图裁切：数字边界夹回图片且空值拒绝保存", () =>
        {
            Assert.Equal("1919,1079,1,1", Value(Invoke("Clamp", 1920, 1080, 2500d, 1500d, 1200d, 600d)));
            Assert.Equal("0,0,1,1", Value(Invoke("Clamp", 1920, 1080, -1d, -1d, 0d, 0d)));
            Assert.Null(Invoke("Clamp", 1920, 1080, double.NaN, 0d, 100d, 100d));
        });
        TestHarness.Test("截图裁切：对话框继承点外关闭且全部输入有自动化句柄", () =>
        {
            var root = fixture.Root;
            var xaml = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src", "Momoka.Shell", "Views", "PlayerCaptureDialog.xaml"));
            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var input in xaml.Descendants().Where(element => element.Name.LocalName is "NumberBox" or "Button"))
                Assert.True(input.Attribute(ns + "Name") is not null, "裁切输入缺少UIA句柄");
            var styles = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Theme", "Styles.xaml"));
            Assert.Contains("TargetType=\"local:PlayerCaptureDialog\" BasedOn=\"{StaticResource EgDialogSurfaceStyle}\"", styles);
            var controller = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "PlayerCaptureController.cs"));
            Assert.Contains("CommandForActiveFileAsync(generation, fileGeneration", controller);
            Assert.Contains("\"screenshot-to-file\", temporary, \"subtitles\"", controller);
            Assert.DoesNotContain("loadfile", controller);
        });
        object? Invoke(string name, params object[] args)
        {
            var shell = fixture.NewViewModel().GetType().Assembly;
            var type = shell.GetType("Momoka.Shell.PlayerCaptureGeometry")
                ?? throw new AssertionException("缺少实际截图裁切几何计算");
            return type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, args);
        }
        static string Value(object? crop)
        {
            Assert.NotNull(crop);
            return string.Join(',', new[] { "X", "Y", "Width", "Height" }.Select(name => crop!.GetType().GetProperty(name)!.GetValue(crop)));
        }
    }
}
