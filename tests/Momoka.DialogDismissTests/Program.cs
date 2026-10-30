using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Momoka.Shell.Views;
using Windows.Foundation;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Test("实际弹窗行为：独立遮罩按下取消且不换算跨树坐标", () =>
        {
            var f = new Fixture();
            var args = new PointerRoutedEventArgs(new(0, 0));
            f.Overlay.Press(args);
            Assert.True(args.Handled);
            Assert.Equal(0, args.CoordinateReads);
            Assert.False(f.Dialog.IsOpen);
            Assert.Equal<ContentDialogResult?>(ContentDialogResult.None, f.Dialog.Result);
        });
        Test("实际弹窗行为：正文、表面边界和已处理内部按下不误关", () =>
        {
            var f = new Fixture();
            foreach (var point in new[] { new Point(100, 80), new Point(200, 120), new Point(400, 280) })
            {
                var args = new PointerRoutedEventArgs(point);
                f.Layout.Press(args);
                Assert.False(args.Handled);
            }
            var handled = new PointerRoutedEventArgs(new(180, 100)) { Handled = true };
            f.Layout.Press(handled);
            Assert.True(f.Dialog.IsOpen);
            Assert.Equal(0, f.Dialog.HideCalls);
        });
        Test("实际弹窗行为：模板外部已处理按下也能取消", () =>
        {
            var f = new Fixture();
            var args = new PointerRoutedEventArgs(new(12, 30)) { Handled = true };
            f.Layout.Press(args);
            Assert.False(f.Dialog.IsOpen);
            Assert.Equal(1, f.Dialog.HideCalls);
        });
        Test("实际弹窗行为：未排版不从几何误关", () =>
        {
            var f = new Fixture();
            f.Surface.ActualWidth = 0;
            Assert.False(DialogDismiss.TryDismiss(f.Dialog, new(0, 0)));
            Assert.True(f.Dialog.IsOpen);
        });
        Test("实际弹窗行为：写入保护拒绝关闭仍吞点击，结束后恢复", () =>
        {
            var f = new Fixture();
            var writing = true;
            var handledBeforeClosing = false;
            var args = new PointerRoutedEventArgs(new(0, 0));
            f.Dialog.Closing += (_, e) => { handledBeforeClosing = args.Handled; e.Cancel = writing; };
            f.Overlay.Press(args);
            Assert.True(handledBeforeClosing, "Closing 之前必须先阻止点穿");
            Assert.True(args.Handled);
            Assert.True(f.Dialog.IsOpen);
            Assert.Equal(1, f.Overlay.HandlerCount);
            writing = false;
            f.Overlay.Press(new(new(0, 0)));
            Assert.False(f.Dialog.IsOpen);
        });
        Test("实际弹窗行为：开放后 Unloaded 不摘掉遮罩，Loaded 重接不重复", () =>
        {
            var f = new Fixture();
            f.Dialog.Unload();
            Assert.Equal(1, f.Overlay.HandlerCount);
            f.Dialog.Load();
            f.Dialog.Load();
            Assert.Equal(1, f.Overlay.HandlerCount);
            Assert.Equal(1, f.Layout.HandlerCount);
            f.Overlay.Press(new(new(0, 0)));
            Assert.Equal(1, f.Dialog.HideCalls);
        });
        Test("实际弹窗行为：Closed 清理两个部件，重开找新遮罩", () =>
        {
            var f = new Fixture();
            var old = f.Overlay;
            f.Dialog.Hide();
            Assert.Equal(0, old.HandlerCount);
            Assert.Equal(0, f.Layout.HandlerCount);
            f.ReplaceOverlay();
            f.Dialog.Open();
            old.Press(new(new(0, 0)));
            Assert.True(f.Dialog.IsOpen);
            f.Overlay.Press(new(new(0, 0)));
            Assert.False(f.Dialog.IsOpen);
            Assert.Equal(2, f.Dialog.HideCalls);
        });
        Test("实际弹窗行为：内部菜单与另一个窗口的遮罩不订阅", () =>
        {
            var f = new Fixture();
            var menu = new FrameworkElement { Name = "FlyoutPresenter" };
            f.Dialog.XamlRoot!.Popups.Insert(0, new Popup { Child = menu });
            var unrelated = new Fixture();
            f.Dialog.Load();
            Assert.Equal(0, menu.HandlerCount);
            menu.Press(new(new(0, 0)));
            unrelated.Overlay.Press(new(new(0, 0)));
            Assert.True(f.Dialog.IsOpen);
            Assert.False(unrelated.Dialog.IsOpen);
        });
        Test("实际弹窗行为：附加属性反复开关立即清理，禁用时入口不取消", () =>
        {
            var f = new Fixture();
            DialogDismiss.SetClosesOnOutsideClick(f.Dialog, false);
            Assert.Equal(0, f.Overlay.HandlerCount);
            Assert.Equal(0, f.Layout.HandlerCount);
            Assert.False(DialogDismiss.TryDismissFromBackdrop(f.Dialog));
            Assert.False(DialogDismiss.TryDismiss(f.Dialog, new(0, 0)));
            f.Dialog.Load();
            Assert.Equal(0, f.Overlay.HandlerCount);
            DialogDismiss.SetClosesOnOutsideClick(f.Dialog, true);
            DialogDismiss.SetClosesOnOutsideClick(f.Dialog, true);
            Assert.Equal(1, f.Overlay.HandlerCount);
            f.Overlay.Press(new(new(0, 0)));
            Assert.False(f.Dialog.IsOpen);
        });
        Test("实际弹窗行为：先开后启用、无 XamlRoot、非弹窗目标安全", () =>
        {
            var f = new Fixture(enabled: false);
            Assert.Equal(0, f.Overlay.HandlerCount);
            DialogDismiss.SetClosesOnOutsideClick(f.Dialog, true);
            Assert.Equal(1, f.Overlay.HandlerCount);
            DialogDismiss.SetClosesOnOutsideClick(new FrameworkElement(), true);
            var noRoot = new ContentDialog();
            DialogDismiss.SetClosesOnOutsideClick(noRoot, true);
            noRoot.Open();
            Assert.Equal(1, noRoot.HandlerCount);
            Assert.False(DialogDismiss.TryDismiss(noRoot, new(0, 0)));
            noRoot.Hide();
            Assert.Equal(0, noRoot.HandlerCount);
        });
        return Run();
    }

    private sealed class Fixture
    {
        public ContentDialog Dialog { get; } = new() { XamlRoot = new(), ActualWidth = 800, ActualHeight = 600 };
        public FrameworkElement Layout { get; } = new() { Name = "LayoutRoot", ActualWidth = 800, ActualHeight = 600 };
        public Border Surface { get; } = new() { Name = "BackgroundElement", X = 100, Y = 80, ActualWidth = 300, ActualHeight = 200 };
        public FrameworkElement Overlay { get; private set; } = null!;
        public Fixture(bool enabled = true)
        {
            Layout.Children.Add(Surface);
            Dialog.Children.Add(Layout);
            ReplaceOverlay();
            DialogDismiss.SetClosesOnOutsideClick(Dialog, enabled);
            Dialog.Open();
            Dialog.Load();
        }
        public void ReplaceOverlay()
        {
            Overlay = new FrameworkElement { Name = "SmokeLayerBackground", ActualWidth = 800, ActualHeight = 600 };
            Dialog.XamlRoot!.Popups.Clear();
            Dialog.XamlRoot.Popups.Add(new Popup { Child = Overlay });
        }
    }
}
